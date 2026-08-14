/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2002--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
  Copyright (C) 2020--2026 Daniel Eble <nine.fierce.ballads@gmail.com>

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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/volta-repeat-iterator.cc, lily/alternative-sequence-iterator.cc, lily/volta-specced-music-iterator.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - the three iterators share one file because they are one mechanism: the repeat
//     creates the styler, the alternative sequence borrows it, and the volta-specced
//     wrapper reads its bracket state off the alternative sequence.
//   - std::shared_ptr<Repeat_styler> becomes an ordinary reference.
//   - alt_restores_ is a typed list of (context, symbol, value) instead of a Scheme
//     list of the same three things. Upstream needs the Scheme form so the GC marks it
//     and so scm_apply_0 can splat it onto ly:context-set-property!; neither applies
//     here, and the restore calls Context.SetProperty directly, which is what that
//     binding does.

/// <summary>
/// The iterator for the body of a <c>\repeat volta</c> or <c>\repeat segno</c>: it owns
/// the <see cref="RepeatStyler"/> that announces the section's boundaries.
/// </summary>
public sealed class VoltaRepeatIterator : SequentialIterator
{
    private static readonly Symbol LyricCombineMusicSymbol = Symbol.Intern("lyric-combine-music");
    private static readonly Symbol RepeatCountSymbol = Symbol.Intern("repeat-count");
    private static readonly Symbol SegnoRepeatedMusicSymbol = Symbol.Intern("segno-repeated-music");
    private static readonly Symbol VoltaRepeatedMusicSymbol = Symbol.Intern("volta-repeated-music");

    private bool _started;
    private bool _stopped;

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Volta_repeat_iterator";

    /// <summary>
    /// Gets the styler this repeat announces through, which the enclosed
    /// <see cref="AlternativeSequenceIterator"/> shares.
    /// </summary>
    public RepeatStyler RepeatStyler { get; private set; }

    /// <summary>Announces the start of the section, then walks it, then announces the return.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        if (!_started)
        {
            // Robustness: Avoid printing a misleading bar line for a zero-duration
            // repeated section.
            if (!IsEmpty)
            {
                // This won't compute the correct lifetime inside \grace.
                Moment start = Context.NowMoment;
                Moment length = MusicLength - MusicStartMoment;

                RepeatStyler.ReportStart(
                    new MomentInterval(start, start + length),
                    RepeatIteratorSupport.ReadCount(GetProperty(RepeatCountSymbol), 1));
            }

            _started = true;
        }

        base.Process(until);

        if (_started && !_stopped && until == MusicLength)
        {
            // When there are tail alternatives, Alternative_sequence_iterator issues
            // end-repeat commands.
            if (!IsEmpty && !RepeatStyler.ReportedReturn)
            {
                // -1 because there is no return for the final volta
                long returnCount
                    = RepeatIteratorSupport.ReadCount(GetProperty(RepeatCountSymbol), 1) - 1;
                RepeatStyler.ReportReturn(0, returnCount);
            }

            _stopped = true;
        }
    }

    /// <summary>Chooses the styler for this kind of repeat before the children are made.</summary>
    protected override void CreateChildren()
    {
        // Do not style repeats inside LyricCombineMusic because the way the
        // Lyric_combine_music_iterator drives the processing tends to place things at
        // the wrong point in time. Instead, Lyric_combine_music_iterator forwards repeat
        // events from the music context that it follows to the lyrics context that it
        // guides.
        bool timingIsAccurate = FindAboveByMusicType(LyricCombineMusicSymbol) == null;

        if (Music.IsMusicType(SegnoRepeatedMusicSymbol))
        {
            RepeatStyler = timingIsAccurate ? RepeatStyler.CreateSegno(this) : RepeatStyler.CreateNull(this);
        }
        else if (Music.IsMusicType(VoltaRepeatedMusicSymbol))
        {
            RepeatStyler = timingIsAccurate ? RepeatStyler.CreateVolta(this) : RepeatStyler.CreateNull(this);
        }
        else
        {
            Warn.ProgrammingError("no repeat styler for this type of music");
            RepeatStyler = RepeatStyler.CreateNull(this);
        }

        base.CreateChildren();
    }

    private bool IsEmpty => !MusicLength.IsNonZero && !MusicStartMoment.GracePart.IsNonZero;
}

/// <summary>
/// The iterator for <c>\alternative { ... }</c>: it works out how the alternatives should
/// be presented, then drives the enclosing repeat's styler through them one at a time.
/// </summary>
public sealed class AlternativeSequenceIterator : SequentialIterator
{
    private static readonly Symbol AlternativeRestoresSymbol = Symbol.Intern("alternativeRestores");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol FoldedRepeatedMusicSymbol = Symbol.Intern("folded-repeated-music");
    private static readonly Symbol MeasurePositionSymbol = Symbol.Intern("measurePosition");
    private static readonly Symbol RepeatCountSymbol = Symbol.Intern("repeat-count");
    private static readonly Symbol TimingSymbol = Symbol.Intern("timing");
    private static readonly Symbol VoltaNumbersSymbol = Symbol.Intern("volta-numbers");

    private readonly List<long> _alternativeReturnCounts = new List<long>();
    private readonly List<PropertyRestore> _restores = new List<PropertyRestore>();

    private bool _firstTime = true;
    private int _doneCount;
    private RepeatStyler _repeatStyler;

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Alternative_sequence_iterator";

    /// <summary>
    /// Gets the number of enclosing <c>\alternative</c>s with volta brackets enabled — not
    /// limited to the current <c>\repeat</c>, all the way to the root of the music — plus
    /// one if volta brackets are enabled for this <c>\alternative</c>.
    /// <para>
    /// Before the first call to <see cref="Process"/> the result is possibly incorrect.
    /// </para>
    /// </summary>
    public int VoltaBracketDepth { get; private set; } = 1;

    /// <summary>
    /// Gets a value indicating whether volta brackets should be created for this group of
    /// alternatives.
    /// <para>
    /// Before the first call to <see cref="Process"/> the result is possibly incorrect.
    /// </para>
    /// </summary>
    public bool VoltaBracketsEnabled { get; private set; } = true;

    /// <summary>Analyses the alternatives the first time round, then walks them.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        if (_firstTime)
        {
            _firstTime = false;
            Analyze();

            if (_alternativeReturnCounts.Count > 1)
            {
                // Ignoring context properties when timing is disabled is the legacy
                // behavior, but it is questionable. Wouldn't we want to restore lastChord
                // even in cadenza mode? Why shouldn't timing be saved and restored like
                // other properties?
                if (SchemeUtilities.ToBool(Context.GetProperty(TimingSymbol)))
                {
                    SaveContextProperties();
                }
            }

            StartAlternative();
        }

        base.Process(until);
    }

    /// <summary>Borrows the styler of the nearest enclosing repeat.</summary>
    protected override void CreateChildren()
    {
        base.CreateChildren();

        // Note: \alternative can be used with \repeat unfold in ly code, but those are
        // transformed before the music is iterated; therefore, searching here for the
        // nearest enclosing folded repeat is the same as searching for the nearest
        // enclosing repeat.
        VoltaRepeatIterator repeatIterator
            = FindAboveByMusicType(FoldedRepeatedMusicSymbol) as VoltaRepeatIterator;

        _repeatStyler = repeatIterator != null
            ? repeatIterator.RepeatStyler
            : RepeatStyler.CreateNull(this); // defensive
    }

    /// <summary>Closes off one alternative and opens the next.</summary>
    protected override void NextElement()
    {
        _doneCount++;
        base.NextElement();
        EndAlternative();
        StartAlternative();
    }

    // Peek at the alternatives to figure out how they should be presented.
    private void Analyze()
    {
        Direction startAlignment = Direction.Center;
        Direction endAlignment = Direction.Center;

        {
            // This won't compute the correct endpoint inside \grace.
            Moment start = Context.NowMoment;
            Moment length = MusicLength - MusicStartMoment;
            Moment end = start + length;

            // Do these alternatives start at the start of the repeated section?
            if (start == _repeatStyler.SpannedTime.Left)
            {
                startAlignment = Direction.Negative;
            }

            // Do these alternatives end at the end of the repeated section?
            if (end == _repeatStyler.SpannedTime.Right)
            {
                endAlignment = Direction.Positive;
            }
        }

        bool alternativesInOrder = true;

        {
            long repeatCount = RepeatIteratorSupport.ReadCount(GetProperty(RepeatCountSymbol), 1);
            long nextExpectedVoltaNumber = 1;

            for (object cursor = Music.GetProperty(ElementsSymbol);
                 cursor is Pair pair;
                 cursor = pair.Cdr)
            {
                MusicObject alternative = pair.Car as MusicObject;
                _alternativeReturnCounts.Add(0);

                object voltaNumbers = alternative != null
                    ? alternative.GetProperty(VoltaNumbersSymbol)
                    : Nil.Instance;

                if (!(voltaNumbers is Pair))
                {
                    if (alternative != null)
                    {
                        ((IDiagnostics)alternative).Warning(
                            "missing volta specification on alternative element");
                    }

                    alternativesInOrder = false;
                }
                else
                {
                    List<long> numbers = RepeatIteratorSupport.SortedCounts(voltaNumbers);
                    foreach (long number in numbers)
                    {
                        // In tail alternatives, we repeat after every volta except the
                        // last.
                        if (endAlignment == Direction.Positive && number < repeatCount)
                        {
                            _alternativeReturnCounts[_alternativeReturnCounts.Count - 1]++;
                        }

                        if (number == nextExpectedVoltaNumber)
                        {
                            ++nextExpectedVoltaNumber;
                        }
                        else
                        {
                            alternativesInOrder = false;
                        }
                    }
                }
            }

            if (alternativesInOrder
                && nextExpectedVoltaNumber == repeatCount + 1
                && _alternativeReturnCounts.Count == 1)
            {
                // The same alternative is used for all volte. A coda mark would mislead:
                // no material needs to be skipped. We don't want to complicate the segno
                // styler to handle this exception: a user who wants something like
                // "D.C. 2 V." without a coda mark can use a simple \repeat without
                // \alternative. We fall back to a bracket.
                alternativesInOrder = false;
            }
        }

        VoltaBracketsEnabled = _repeatStyler.ReportAlternativeGroupStart(
            startAlignment, endAlignment, alternativesInOrder);

        // The local volta bracket depth is whatever it was for the nearest enclosing
        // \alternative, plus one if volta brackets are enabled here.
        VoltaBracketDepth = VoltaBracketsEnabled ? 1 : 0;
        for (MusicIterator scope = Parent; scope != null; scope = scope.Parent)
        {
            if (scope is AlternativeSequenceIterator enclosing)
            {
                VoltaBracketDepth += enclosing.VoltaBracketDepth;
                break;
            }
        }
    }

    private void EndAlternative()
    {
        if (_doneCount > _alternativeReturnCounts.Count) // paranoia
        {
            return;
        }

        long returnCount = _alternativeReturnCounts[_doneCount - 1];
        if (returnCount > 0)
        {
            _repeatStyler.ReportReturn(_doneCount, returnCount);
        }

        if (_doneCount == _alternativeReturnCounts.Count) // ending the final alternative
        {
            _repeatStyler.ReportAlternativeGroupEnd(Music, VoltaBracketDepth);
        }
        else if (_doneCount < _alternativeReturnCounts.Count) // ending an earlier alternative
        {
            if (SchemeUtilities.ToBool(Context.GetProperty(TimingSymbol)))
            {
                RestoreContextProperties();
            }
        }
    }

    private void RestoreContextProperties()
    {
        foreach (PropertyRestore restore in _restores)
        {
            // Repeats may have different grace timing, so we need to adjust the
            // measurePosition grace timing to that of the current alternative rather than
            // that of the first. The Timing_translator does this already but is too late
            // to avoid bad side-effects.
            if (restore.Symbol == MeasurePositionSymbol && restore.Value is Moment saved)
            {
                restore.Context.SetProperty(
                    MeasurePositionSymbol,
                    new Moment(saved.MainPart, Context.NowMoment.GracePart));
            }
            else
            {
                restore.Context.SetProperty(restore.Symbol, restore.Value);
            }
        }
    }

    private void SaveContextProperties()
    {
        // Save the starting values of specified context properties. These will be
        // restored at the end of each alternative but the last.
        //
        // Upstream's TODO, kept because the gaps are real: a property may be defined at
        // multiple levels of the context tree and only the innermost is recorded; a
        // property undefined now but defined by something inside an alternative stays
        // defined; and a property may be defined in a context where several
        // Alternative_sequence_iterators are operative, e.g. Timing.
        for (object cursor = Context.GetProperty(AlternativeRestoresSymbol);
             cursor is Pair pair;
             cursor = pair.Cdr)
        {
            if (!(pair.Car is Symbol symbol))
            {
                continue;
            }

            Context where = Context.WhereDefined(symbol, out object value);
            if (where != null)
            {
                _restores.Insert(0, new PropertyRestore(where, symbol, value));
            }
        }
    }

    private void StartAlternative()
    {
        if (_doneCount >= _alternativeReturnCounts.Count)
        {
            return;
        }

        // Examining the child music is ugly but effective.
        MusicObject music = Child?.Music;

        object voltaNumbers = music != null ? music.GetProperty(VoltaNumbersSymbol) : Nil.Instance;
        if (!(voltaNumbers is Pair))
        {
            // We already warned about this in Analyze().
            voltaNumbers = Nil.Instance;
        }

        _repeatStyler.ReportAlternativeStart(
            music, _doneCount + 1, VoltaBracketDepth, voltaNumbers);
    }

    private readonly struct PropertyRestore
    {
        public PropertyRestore(Context context, Symbol symbol, object value)
        {
            Context = context;
            Symbol = symbol;
            Value = value;
        }

        public Context Context { get; }

        public Symbol Symbol { get; }

        public object Value { get; }
    }
}

/// <summary>
/// The iterator for one alternative that carries a volta specification: it brackets the
/// wrapped music with a <c>VoltaSpanEvent</c> pair, but only when its parent
/// <see cref="AlternativeSequenceIterator"/> says brackets are wanted.
/// </summary>
public sealed class VoltaSpeccedMusicIterator : MusicWrapperIterator
{
    private static readonly Symbol RepeatCountSymbol = Symbol.Intern("repeat-count");
    private static readonly Symbol VoltaDepthSymbol = Symbol.Intern("volta-depth");
    private static readonly Symbol VoltaNumbersSymbol = Symbol.Intern("volta-numbers");
    private static readonly Symbol VoltaSpanEventSymbol = Symbol.Intern("VoltaSpanEvent");

    private readonly ContextHandle _eventHandler = new ContextHandle();

    private int _voltaDepth;
    private bool _started;
    private bool _stopped;

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Volta_specced_music_iterator";

    /// <summary>Opens the bracket, walks the alternative, and closes the bracket.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        if (!_started)
        {
            // The result of AlternativeSequenceIterator.VoltaBracketsEnabled is not
            // accurate until Process(). If not for that, all this could have been
            // prepared in CreateChildren().
            if (Parent is AlternativeSequenceIterator parent)
            {
                _voltaDepth = parent.VoltaBracketDepth;

                // Let the AlternativeSequenceIterator veto the bracket, e.g. for the tail
                // alternatives of a \repeat segno.
                if (!parent.VoltaBracketsEnabled)
                {
                    _stopped = true;
                }
            }
            else
            {
                // Do not create volta brackets except for children of \alternative.
                _stopped = true;
            }

            _started = _stopped;
        }

        if (!_started)
        {
            _started = true;
            _eventHandler.Set(Context);
            CreateEvent(Direction.Negative)?.SendToContext(_eventHandler.Context);
        }

        base.Process(until);

        if (_started && !_stopped && until == MusicLength)
        {
            _stopped = true;
            if (_eventHandler.Context != null)
            {
                CreateEvent(Direction.Positive)?.SendToContext(_eventHandler.Context);
                _eventHandler.Reset();
            }
        }
    }

    private MusicObject CreateEvent(Direction direction)
    {
        MusicObject ev = MusicFactory.MakeSpanEvent(VoltaSpanEventSymbol, direction);
        if (ev == null)
        {
            return null;
        }

        ev.SetSpot(Music.Origin);
        ev.SetProperty(RepeatCountSymbol, GetProperty(RepeatCountSymbol));
        ev.SetProperty(VoltaDepthSymbol, (long)_voltaDepth);
        ev.SetProperty(VoltaNumbersSymbol, Music.GetProperty(VoltaNumbersSymbol));
        return ev;
    }
}

/// <summary>
/// Small readers the repeat iterators share: upstream reaches these through
/// <c>from_scm</c> template instantiations and <c>scm_sort_list</c>.
/// </summary>
internal static class RepeatIteratorSupport
{
    /// <summary>Reads a count from a Scheme value.</summary>
    /// <param name="value">The Scheme value.</param>
    /// <param name="fallback">What to answer when the value is not an integer.</param>
    /// <returns>The count.</returns>
    public static long ReadCount(object value, long fallback)
        => value is long count ? count : fallback;

    /// <summary>Reads a Scheme list of counts and sorts it ascending.</summary>
    /// <param name="list">The Scheme list.</param>
    /// <returns>The sorted counts; non-integer entries are skipped.</returns>
    public static List<long> SortedCounts(object list)
    {
        List<long> result = new List<long>();
        for (object cursor = list; cursor is Pair pair; cursor = pair.Cdr)
        {
            if (pair.Car is long value)
            {
                result.Add(value);
            }
        }

        result.Sort();
        return result;
    }
}
