/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
                           Jan Nieuwenhuizen <janneke@gnu.org>

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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/completion-note-heads-engraver.cc, lily/completion-rest-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/*
  How does this work?

  When we catch the note, we predict the end of the note. We keep the
  events living until we reach the predicted end-time.

  Every time process_music () is called and there are note events, we
  figure out how long the note to typeset should be. It should be no
  longer than what's specified, than what is left to do and it should
  not cross barlines or sub-bar units.

  We copy the events into scratch note events, to make sure that we get
  all durations exactly right.
*/

/// <summary>
/// Replaces <c>Note_heads_engraver</c>: breaks notes crossing a bar line or completion
/// unit into shorter typeset notes and ties them together.
/// </summary>
public class CompletionHeadsEngraver : Engraver
{
    private static readonly Symbol NoteEventSymbol = Symbol.Intern("note-event");
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol DurationSymbol = Symbol.Intern("duration");
    private static readonly Symbol LengthSymbol = Symbol.Intern("length");
    private static readonly Symbol DurationLogSymbol = Symbol.Intern("duration-log");
    private static readonly Symbol AutosplitEndSymbol = Symbol.Intern("autosplit-end");
    private static readonly Symbol MiddleCPositionSymbol = Symbol.Intern("middleCPosition");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol TimingSymbol = Symbol.Intern("timing");
    private static readonly Symbol CompletionUnitSymbol = Symbol.Intern("completionUnit");
    private static readonly Symbol CompletionFactorSymbol = Symbol.Intern("completionFactor");
    private static readonly Symbol CompletionBusySymbol = Symbol.Intern("completionBusy");

    private readonly List<Item> _notes = new List<Item>();
    private List<Item> _prevNotes = new List<Item>();

    // Must remember notes for explicit ties.
    private readonly List<Spanner> _ties = new List<Spanner>();
    private readonly List<StreamEvent> _noteEvents = new List<StreamEvent>();
    private Spanner _tieColumn;
    private Moment _noteEndMom = Moment.Zero;
    private bool _isFirst;
    private Rational _leftToDo = Rational.Zero;
    private Rational _doNothingUntil = Rational.Zero;
    private Rational _factor = Rational.Zero;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public CompletionHeadsEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Completion_heads_engraver";

    /// <summary>Starts listening for note events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(NoteEventSymbol, ListenNote);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Clears the first-time flag.</summary>
    public override void Initialize()
    {
        _isFirst = false;
    }

    private void ListenNote(StreamEvent ev)
    {
        _noteEvents.Add(ev);

        _isFirst = true;
        Moment now = NowMoment;
        Moment musiclen = GetEventLength(ev, now);

        Moment end = now + musiclen;
        if (end > _noteEndMom)
        {
            _noteEndMom = end;
        }

        _doNothingUntil = Rational.Zero;
    }

    /*
      The duration _until_ the next bar line or completion unit
    */
    private Rational NextMoment(Rational noteLen)
    {
        // TODO: This looks like a copy & paste from Completion_rest_engraver.
        Rational result = Rational.Zero;

        if (!SchemeUtilities.ToBool(GetProperty(TimingSymbol)))
        {
            return result;
        }

        Rational mlen = MeasureTiming.MeasureLength(Context);
        Moment mpos = MeasureTiming.MeasurePosition(Context, mlen);

        result = mlen - mpos.MainPart;

        object unitScm = GetProperty(CompletionUnitSymbol);
        Rational unit = SchemeConvert.IsNumber(unitScm)
            ? SchemeConvert.ToRational(unitScm, "completionUnit")
            : Rational.Zero;
        if (unit <= Rational.Zero)
        {
            return result;
        }

        Rational nowUnit = mpos.MainPart / unit;
        if (nowUnit.Denominator > 1)
        {
            /*
              within a unit - go to the end of that
            */
            result = unit * (Rational.One - (nowUnit - nowUnit.TruncatedRational()));
        }
        else
        {
            /*
              at the beginning of a unit:
              take a power-of-two number of units, but not more than required,
              since then the Duration constructor destroys the unit structure
            */
            if (noteLen < result)
            {
                result = noteLen;
            }

            Rational stepUnit = result / unit;
            if (stepUnit.Denominator < stepUnit.Numerator)
            {
                int log2 = Misc.IntLog2(
                    (int)(stepUnit.Numerator / stepUnit.Denominator));
                result = unit * new Rational(1L << log2);
            }
        }

        return result;
    }

    private Item MakeCompletionNoteHead(StreamEvent ev)
    {
        Item note = MakeItem("NoteHead", ev);
        Pitch pit = ev.GetProperty(PitchSymbol) as Pitch;

        long pos = pit != null ? pit.Steps() : 0;
        object c0 = GetProperty(MiddleCPositionSymbol);
        if (SchemeConvert.IsNumber(c0))
        {
            pos += SchemeConvert.ToLong(c0, "middleCPosition");
        }

        note.SetProperty(StaffPositionSymbol, pos);

        return note;
    }

    /// <summary>Typesets the next split of every sounding note event.</summary>
    public override void ProcessMusic()
    {
        if (!_isFirst && !_leftToDo.IsNonZero)
        {
            return;
        }

        _isFirst = false;

        Moment now = NowMoment;
        if (_doNothingUntil > now.MainPart)
        {
            return;
        }

        Duration noteDur;
        Duration? orig = null;
        if (_leftToDo.IsNonZero)
        {
            /*
              note that note_dur may be strictly less than left_to_do_
              (say, if left_to_do_ == 5/8)
            */
            noteDur = Duration.FromWholeNotes(_leftToDo / _factor, false).Compressed(_factor);
        }
        else
        {
            orig = _noteEvents[0].GetProperty(DurationSymbol) as Duration?;
            noteDur = orig ?? Duration.WholeNote;
            object factor = GetProperty(CompletionFactorSymbol);
            if (SchemeUtilities.IsProcedure(factor))
            {
                factor = SchemeUtilities.CallCallback(factor, Context, noteDur);
            }

            _factor = SchemeConvert.IsNumber(factor)
                ? SchemeConvert.ToRational(factor, "completionFactor")
                : noteDur.Factor;
            _leftToDo = noteDur.ToWholeNotes();
        }

        Rational nb = NextMoment(noteDur.ToWholeNotes());
        if (nb.IsNonZero)
        {
            if (nb < noteDur.ToWholeNotes())
            {
                noteDur = Duration.FromWholeNotes(nb / _factor, false).Compressed(_factor);
            }
        }

        _doNothingUntil = now.MainPart + noteDur.ToWholeNotes();

        for (int i = 0; _leftToDo.IsNonZero && i < _noteEvents.Count; i++)
        {
            bool needClone = !orig.HasValue || orig.Value != noteDur;
            StreamEvent ev = _noteEvents[i];

            if (needClone)
            {
                ev = ev.Clone();
            }

            object pits = _noteEvents[i].GetProperty(PitchSymbol);
            ev.SetProperty(PitchSymbol, pits);
            ev.SetProperty(DurationSymbol, noteDur);
            ev.SetProperty(LengthSymbol, new Moment(noteDur.ToWholeNotes()));
            ev.SetProperty(DurationLogSymbol, (long)noteDur.DurationLog);

            /*
              The Completion_heads_engraver splits an event into a group of consecutive
              events.  For each event in the group, the property "autosplit-end" denotes
              whether the current event was truncated during splitting. Based on
              "autosplit-end", the Tie_engraver decides whether a tie event should be
              processed.
            */
            ev.SetProperty(AutosplitEndSymbol, _leftToDo > noteDur.ToWholeNotes());

            Item note = MakeCompletionNoteHead(ev);
            _notes.Add(note);
        }

        if (_prevNotes.Count == _notes.Count)
        {
            for (int i = 0; i < _notes.Count; i++)
            {
                MakeTie(_prevNotes[i], _notes[i]);
            }
        }

        if (_ties.Count > 0 && _tieColumn == null)
        {
            _tieColumn = MakeSpanner("TieColumn", _ties[0]);
        }

        if (_tieColumn != null)
        {
            for (int i = _ties.Count; i-- > 0;)
            {
                TieColumn.AddTie(_tieColumn, _ties[i]);
            }
        }

        _leftToDo -= noteDur.ToWholeNotes();
        if (_leftToDo.IsNonZero)
        {
            Context?.GlobalContext?.AddMomentToProcess(
                new Moment(now.MainPart + noteDur.ToWholeNotes()));
        }

        /*
          don't do complicated arithmetic with grace notes.
        */
        if (orig.HasValue && now.GracePart.IsNonZero)
        {
            _leftToDo = Rational.Zero;
        }
    }

    private void MakeTie(Grob left, Grob right)
    {
        Spanner p = MakeSpanner("Tie", Nil.Instance);
        Tie.SetHead(p, Direction.Negative, left);
        Tie.SetHead(p, Direction.Positive, right);
        AnnounceEndGrob(p, Nil.Instance);
        _ties.Add(p);
    }

    /// <summary>Rolls this timestep's notes over into the previous-notes memory.</summary>
    public override void StopTranslationTimestep()
    {
        _ties.Clear();
        _tieColumn = null;

        if (_notes.Count > 0)
        {
            _prevNotes = new List<Item>(_notes);
        }

        _notes.Clear();
    }

    /// <summary>Expires finished events and reports whether a completion is running.</summary>
    public override void StartTranslationTimestep()
    {
        Moment now = NowMoment;
        if (_noteEndMom.MainPart <= now.MainPart)
        {
            _noteEvents.Clear();
            _prevNotes.Clear();
        }

        Context?.SetProperty(CompletionBusySymbol, _noteEvents.Count != 0);
    }
}

/*
  How does this work?

  When we catch the rest, we predict the end of the rest. We keep the
  events living until we reach the predicted end-time.

  Every time process_music () is called and there are rest events, we
  figure out how long the rest to typeset should be. It should be no
  longer than what's specified, than what is left to do and it should
  not cross barlines or sub-bar units.

  We copy the events into scratch rest events, to make sure that we get
  all durations exactly right.
*/

/// <summary>
/// Replaces <c>Rest_engraver</c>: breaks rests crossing a bar line or completion unit
/// into shorter typeset rests.
/// </summary>
public class CompletionRestEngraver : Engraver
{
    private static readonly Symbol RestEventSymbol = Symbol.Intern("rest-event");
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol DurationSymbol = Symbol.Intern("duration");
    private static readonly Symbol LengthSymbol = Symbol.Intern("length");
    private static readonly Symbol DurationLogSymbol = Symbol.Intern("duration-log");
    private static readonly Symbol MiddleCPositionSymbol = Symbol.Intern("middleCPosition");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol TimingSymbol = Symbol.Intern("timing");
    private static readonly Symbol CompletionUnitSymbol = Symbol.Intern("completionUnit");
    private static readonly Symbol CompletionFactorSymbol = Symbol.Intern("completionFactor");
    private static readonly Symbol RestCompletionBusySymbol
        = Symbol.Intern("restCompletionBusy");

    private readonly List<Item> _rests = new List<Item>();
    private readonly List<StreamEvent> _restEvents = new List<StreamEvent>();
    private Moment _restEndMom = Moment.Zero;
    private bool _isFirst;
    private Rational _leftToDo = Rational.Zero;
    private Rational _doNothingUntil = Rational.Zero;
    private Rational _factor = Rational.Zero;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public CompletionRestEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Completion_rest_engraver";

    /// <summary>Starts listening for rest events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(RestEventSymbol, ListenRest);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Clears the first-time flag.</summary>
    public override void Initialize()
    {
        _isFirst = false;
    }

    private void ListenRest(StreamEvent ev)
    {
        _restEvents.Add(ev);

        _isFirst = true;
        Moment now = NowMoment;
        Moment musiclen = GetEventLength(ev, now);

        Moment end = now + musiclen;
        if (end > _restEndMom)
        {
            _restEndMom = end;
        }

        _doNothingUntil = Rational.Zero;
    }

    /*
      The duration _until_ the next barline or completion unit
    */
    private Rational NextMoment(Rational noteLen)
    {
        // TODO: This looks like a copy & paste from Completion_heads_engraver.
        Rational result = Rational.Zero;

        if (!SchemeUtilities.ToBool(GetProperty(TimingSymbol)))
        {
            return result;
        }

        Rational mlen = MeasureTiming.MeasureLength(Context);
        Moment mpos = MeasureTiming.MeasurePosition(Context, mlen);

        result = mlen - mpos.MainPart;

        object unitScm = GetProperty(CompletionUnitSymbol);
        Rational unit = SchemeConvert.IsNumber(unitScm)
            ? SchemeConvert.ToRational(unitScm, "completionUnit")
            : Rational.Zero;
        if (unit <= Rational.Zero)
        {
            return result;
        }

        Rational nowUnit = mpos.MainPart / unit;
        if (nowUnit.Denominator > 1)
        {
            /*
              within a unit - go to the end of that
            */
            result = unit * (Rational.One - (nowUnit - nowUnit.TruncatedRational()));
        }
        else
        {
            /*
              at the beginning of a unit:
              take a power-of-two number of units, but not more than required,
              since then the Duration constructor destroys the unit structure
            */
            if (noteLen < result)
            {
                result = noteLen;
            }

            Rational stepUnit = result / unit;
            if (stepUnit.Denominator < stepUnit.Numerator)
            {
                int log2 = Misc.IntLog2(
                    (int)(stepUnit.Numerator / stepUnit.Denominator));
                result = unit * new Rational(1L << log2);
            }
        }

        return result;
    }

    private Item MakeCompletionRest(StreamEvent ev)
    {
        Item rest = MakeItem("Rest", ev);
        if (ev.GetProperty(PitchSymbol) is Pitch p)
        {
            long pos = p.Steps();
            object c0 = GetProperty(MiddleCPositionSymbol);
            if (SchemeConvert.IsNumber(c0))
            {
                pos += SchemeConvert.ToLong(c0, "middleCPosition");
            }

            rest.SetProperty(StaffPositionSymbol, pos);
        }

        return rest;
    }

    /// <summary>Typesets the next split of every sounding rest event.</summary>
    public override void ProcessMusic()
    {
        if (!_isFirst && !_leftToDo.IsNonZero)
        {
            return;
        }

        _isFirst = false;

        Moment now = NowMoment;
        if (_doNothingUntil > now.MainPart)
        {
            return;
        }

        Duration restDur;
        Duration? orig = null;
        if (_leftToDo.IsNonZero)
        {
            /*
              note that rest_dur may be strictly less than left_to_do_
              (say, if left_to_do_ == 5/8)
            */
            restDur = Duration.FromWholeNotes(_leftToDo / _factor, false).Compressed(_factor);
        }
        else
        {
            orig = _restEvents[0].GetProperty(DurationSymbol) as Duration?;
            restDur = orig ?? Duration.WholeNote;
            object factor = GetProperty(CompletionFactorSymbol);
            if (SchemeUtilities.IsProcedure(factor))
            {
                factor = SchemeUtilities.CallCallback(factor, Context, restDur);
            }

            _factor = SchemeConvert.IsNumber(factor)
                ? SchemeConvert.ToRational(factor, "completionFactor")
                : restDur.Factor;
            _leftToDo = restDur.ToWholeNotes();
        }

        Rational nb = NextMoment(restDur.ToWholeNotes());
        if (nb.IsNonZero)
        {
            if (nb < restDur.ToWholeNotes())
            {
                restDur = Duration.FromWholeNotes(nb / _factor, false).Compressed(_factor);
            }
        }

        _doNothingUntil = now.MainPart + restDur.ToWholeNotes();

        for (int i = 0; _leftToDo.IsNonZero && i < _restEvents.Count; i++)
        {
            bool needClone = !orig.HasValue || orig.Value != restDur;
            StreamEvent ev = _restEvents[i];

            if (needClone)
            {
                ev = ev.Clone();
            }

            object pits = _restEvents[i].GetProperty(PitchSymbol);
            ev.SetProperty(PitchSymbol, pits);
            ev.SetProperty(DurationSymbol, restDur);
            ev.SetProperty(LengthSymbol, new Moment(restDur.ToWholeNotes()));
            ev.SetProperty(DurationLogSymbol, (long)restDur.DurationLog);

            Item rest = MakeCompletionRest(ev);
            _rests.Add(rest);
        }

        _leftToDo -= restDur.ToWholeNotes();
        if (_leftToDo.IsNonZero)
        {
            Context?.GlobalContext?.AddMomentToProcess(
                new Moment(now.MainPart + restDur.ToWholeNotes()));
        }

        /*
          don't do complicated arithmetic with grace rests.
        */
        if (orig.HasValue && now.GracePart.IsNonZero)
        {
            _leftToDo = Rational.Zero;
        }
    }

    /// <summary>Forgets the timestep's rests.</summary>
    public override void StopTranslationTimestep()
    {
        _rests.Clear();
    }

    /// <summary>Expires finished events and reports whether a completion is running.</summary>
    public override void StartTranslationTimestep()
    {
        Moment now = NowMoment;
        if (_restEndMom.MainPart <= now.MainPart)
        {
            _restEvents.Clear();
        }

        Context?.SetProperty(RestCompletionBusySymbol, _restEvents.Count != 0);
    }
}
