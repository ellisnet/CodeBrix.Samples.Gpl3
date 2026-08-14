/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/quote-iterator.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The iterator for <c>\quoteDuring</c> and <c>\cueDuring</c>: it walks the wrapped
/// music normally, and ALONGSIDE it replays events recorded from another voice.
/// <para>
/// The recorded events arrive as a Scheme vector of
/// <c>((moment . pitch) (event . …) …)</c> entries, already sorted by moment — which is
/// why both ends of the window are found by binary search rather than by scanning. The
/// vector is filled by <c>\addQuote</c>, whose recording side registers Scheme listeners
/// through <c>ly:add-listener</c>.
/// </para>
/// <para>
/// Two moments matter and are easy to confuse. <c>_zeroMoment</c> is where this music
/// sits in the SCORE's timeline, so that a recorded event's absolute moment can be
/// compared against the iterator's own relative one; <c>MusicStartMoment</c> is the
/// grace offset the wrapped music itself begins with.
/// </para>
/// </summary>
public sealed class QuoteIterator : MusicWrapperIterator
{
    private static readonly Symbol QuotedEventsSymbol = Symbol.Intern("quoted-events");
    private static readonly Symbol QuotedContextTypeSymbol = Symbol.Intern("quoted-context-type");
    private static readonly Symbol QuotedContextIdSymbol = Symbol.Intern("quoted-context-id");
    private static readonly Symbol QuotedTranspositionSymbol = Symbol.Intern("quoted-transposition");
    private static readonly Symbol QuotedCueEventTypesSymbol = Symbol.Intern("quotedCueEventTypes");
    private static readonly Symbol QuotedEventTypesSymbol = Symbol.Intern("quotedEventTypes");
    private static readonly Symbol InstrumentTranspositionSymbol
        = Symbol.Intern("instrumentTransposition");

    private readonly ContextHandle _quoteHandle = new ContextHandle();

    // zero moment of this music in the timeline of the score; unknown until the
    // first call to Process
    private Moment _zeroMoment = -Moment.Infinity;
    private object _eventVector = Nil.Instance;
    private int _eventIndex; // left closed
    private int _endIndex;   // right open
    private bool _firstTime = true;

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Quote_iterator";

    /// <summary>
    /// Gets the earlier of the wrapped music's next moment and the next recorded event's.
    /// </summary>
    public override Moment PendingMoment
    {
        get
        {
            Moment m = base.PendingMoment;

            if (_eventIndex < _endIndex && _eventVector is object[] vector)
            {
                object entry = vector[_eventIndex];
                if (Caar(entry) is Moment eventMoment)
                {
                    // If eventMoment is not a moment, Process should issue a diagnostic
                    // later, so just ignore it here.
                    Moment candidate = eventMoment - _zeroMoment;
                    if (candidate < m)
                    {
                        m = candidate;
                    }
                }
            }

            return m;
        }
    }

    /// <summary>
    /// Determines whether an event's class is one the context asked to hear quoted.
    /// </summary>
    /// <param name="ev">The recorded event.</param>
    /// <param name="isCue">Whether this is a cue rather than a plain quote.</param>
    /// <returns><see langword="true"/> when the event should be replayed.</returns>
    public bool AcceptMusicType(StreamEvent ev, bool isCue = true)
    {
        object accept = Nil.Instance;

        // Cue notes use the quotedCueEventTypes property, otherwise (and as fallback
        // for cue notes if quotedCueEventTypes is not set) use quotedEventTypes
        if (isCue)
        {
            accept = Context.GetProperty(QuotedCueEventTypesSymbol);
        }

        if (accept is Nil)
        {
            accept = Context.GetProperty(QuotedEventTypesSymbol);
        }

        while (accept is Pair pair)
        {
            if (pair.Car is Symbol className && ev.IsInEventClass(className))
            {
                return true;
            }

            accept = pair.Cdr;
        }

        return false;
    }

    /// <summary>Replays every recorded event that has come due, then delegates.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        if (base.PendingMoment <= until)
        {
            base.Process(until);
        }

        if (_firstTime)
        {
            _firstTime = false;

            // start moment of this music in the timeline of the score
            Moment startMoment = Context.NowMoment;

            _zeroMoment = startMoment - MusicStartMoment;

            if (_eventVector is object[] vector)
            {
                // To quote grace notes, the user currently has to provide grace time
                // in the wrapped music.  It would be nicer to include all grace
                // notes leading into the quote automatically.
                _eventIndex = BinarySearchVector(vector, startMoment);

                // end moment of this music, excluding any grace notes leading to an
                // unquoted note
                Moment endMoment = new Moment(
                    _zeroMoment.MainPart + MusicLength.MainPart,
                    -Rational.Infinity);

                _endIndex = BinarySearchVector(vector, endMoment);
            }
        }

        Moment m = _zeroMoment + until;
        object[] events = _eventVector as object[];
        for (/**/; events != null && _eventIndex < _endIndex; ++_eventIndex)
        {
            object entry = events[_eventIndex];

            if (Caar(entry) is Moment eventMoment)
            {
                if (eventMoment > m) // not time to process this entry yet
                {
                    return;
                }
            }
            else
            {
                Warn.ProgrammingError(
                    "expected moment in event vector: " + Printer.Write(Caar(entry)));
                continue;
            }

            Pitch quotePitch = Cdar(entry) as Pitch;

            /*
              The pitch that sounds when written central C is played.
            */
            Pitch mePitch = Music.GetProperty(QuotedTranspositionSymbol) as Pitch;
            if (mePitch == null)
            {
                mePitch = Context.GetProperty(InstrumentTranspositionSymbol) as Pitch;
            }
            else
            {
                // We are not going to win a beauty contest with this one, but it is
                // slated for replacement and touches little code. quoted-transposition
                // currently has a different sign convention than
                // instrumentTransposition.
                mePitch = mePitch.Negated();
            }

            object cid = Music.GetProperty(QuotedContextIdSymbol);
            bool isCue = (cid is MutableString || cid is string)
                         && string.Equals(cid.ToString(), "cue", StringComparison.Ordinal);

            for (object s = (entry as Pair)?.Cdr; s is Pair sPair; s = sPair.Cdr)
            {
                object evAcc = sPair.Car;

                StreamEvent ev = (evAcc as Pair)?.Car as StreamEvent;
                if (ev == null)
                {
                    Warn.ProgrammingError("no music found in quote");
                }
                else if (AcceptMusicType(ev, isCue))
                {
                    /* create a transposed copy if necessary */
                    if (quotePitch != null || mePitch != null)
                    {
                        Pitch qp = quotePitch ?? new Pitch();
                        Pitch mp = mePitch ?? new Pitch();

                        Pitch diff = Pitch.Interval(mp, qp);
                        ev = ev.Clone();
                        ev.MakeTransposable();
                        ev.Transpose(diff);
                    }

                    _quoteHandle.Context?.EventSource.Broadcast(ev);
                }
            }
        }
    }

    /// <summary>Reads the recorded event vector off the music.</summary>
    protected override void CreateChildren()
    {
        base.CreateChildren();

        _eventVector = Music.GetProperty(QuotedEventsSymbol);
    }

    /// <summary>Finds or creates the context the quoted events are broadcast at.</summary>
    protected override void CreateContexts()
    {
        base.CreateContexts();

        Context cueContext = null;

        object name = Music.GetProperty(QuotedContextTypeSymbol);
        if (name is Symbol contextType)
        {
            object id = Music.GetProperty(QuotedContextIdSymbol);
            string cId = id is MutableString || id is string ? id.ToString() : string.Empty;
            cueContext = Context.FindCreateContext(contextType, cId, Direction.Center, Nil.Instance);
            if (cueContext == null)
            {
                Warn.Warning(
                    "cannot find or create context: "
                    + Context.DiagnosticId(contextType, cId));
            }
        }

        if (cueContext == null)
        {
            cueContext = Context.GetDefaultInterpreter();
        }

        _quoteHandle.Set(cueContext);
    }

    /// <summary>Shuts the wrapped iterator down and releases the cue context.</summary>
    protected override void DoQuit()
    {
        base.DoQuit();
        _quoteHandle.Reset();
    }

    // lower bound: binary search returning the index of the first element that is
    // not less than the key
    private static int BinarySearchVector(object[] vector, Moment key)
    {
        int lo = 0;
        int hi = vector.Length;

        while (lo < hi)
        {
            int cmp = (lo + hi) / 2;

            object when = Caar(vector[cmp]);
            if (when is Moment moment && moment < key)
            {
                lo = cmp + 1;
            }
            else
            {
                hi = cmp;
            }
        }

        return lo;
    }

    private static object Caar(object entry) => ((entry as Pair)?.Car as Pair)?.Car;

    private static object Cdar(object entry) => ((entry as Pair)?.Car as Pair)?.Cdr;
}
