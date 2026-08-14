/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2021--2026 Daniel Eble <nine.fierce.ballads@gmail.com>

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

using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/repeat-styler.cc, lily/include/repeat-styler.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - the three concrete stylers are private nested classes rather than file-static
//     ones, because C# has no file-scope class and the factory methods are the only
//     intended way in, exactly as upstream's are.
//   - std::unique_ptr ownership becomes an ordinary reference; the owning iterator
//     outlives the styler either way.

/// <summary>
/// Announces the boundaries of repeated sections on behalf of the iterators of
/// <c>\repeat</c> and <c>\alternative</c> music.
/// <para>
/// Each <see cref="VoltaRepeatIterator"/> creates one styler, and each
/// <see cref="AlternativeSequenceIterator"/> uses the styler of its enclosing repeat.
/// </para>
/// </summary>
/// <remarks>
/// Upstream's own warning, kept because it is the design rule for this file: think
/// twice before adding state or logic to the repeat stylers. Dealing with the
/// complexities of music structure should mainly be left to the music iterators. The
/// stylers should mainly just act on what the iterators have discovered, using
/// information that is plainly communicated to them.
/// </remarks>
public abstract class RepeatStyler
{
    private static readonly Symbol AlternativeDirSymbol = Symbol.Intern("alternative-dir");
    private static readonly Symbol AlternativeEventSymbol = Symbol.Intern("AlternativeEvent");
    private static readonly Symbol AlternativeNumberSymbol = Symbol.Intern("alternative-number");
    private static readonly Symbol CodaMarkEventSymbol = Symbol.Intern("CodaMarkEvent");
    private static readonly Symbol DalSegnoEventSymbol = Symbol.Intern("DalSegnoEvent");
    private static readonly Symbol RepeatBodyStartMomentSymbol
        = Symbol.Intern("repeat-body-start-moment");

    private static readonly Symbol RepeatCountSymbol = Symbol.Intern("repeat-count");
    private static readonly Symbol ReturnCountSymbol = Symbol.Intern("return-count");
    private static readonly Symbol SegnoMarkEventSymbol = Symbol.Intern("SegnoMarkEvent");
    private static readonly Symbol VoltaDepthSymbol = Symbol.Intern("volta-depth");
    private static readonly Symbol VoltaNumbersSymbol = Symbol.Intern("volta-numbers");
    private static readonly Symbol VoltaRepeatEndEventSymbol = Symbol.Intern("VoltaRepeatEndEvent");
    private static readonly Symbol VoltaRepeatStartEventSymbol
        = Symbol.Intern("VoltaRepeatStartEvent");

    private readonly MusicIterator _owner;
    private MomentInterval _spannedTime = new MomentInterval(Moment.Infinity, Moment.Infinity);
    private long _repeatCount = 2;
    private int _alternativeDepth;
    private int _reportedReturnDepth;

    /// <summary>Initializes a styler owned by an iterator.</summary>
    /// <param name="owner">The owning iterator; must not be <see langword="null"/>.</param>
    protected RepeatStyler(MusicIterator owner) => _owner = owner;

    /// <summary>
    /// Gets the lifetime of the repeat in the timeline of the score, as it was passed
    /// into <see cref="ReportStart"/>.
    /// </summary>
    public MomentInterval SpannedTime => _spannedTime;

    /// <summary>
    /// Gets a value indicating whether <see cref="ReportReturn"/> has been called at
    /// least once.
    /// </summary>
    public bool ReportedReturn { get; private set; }

    /// <summary>Gets the owning iterator.</summary>
    protected MusicIterator Owner => _owner;

    /// <summary>Gets the number of times the section is performed from its start.</summary>
    protected long RepeatCount => _repeatCount;

    /// <summary>Gets how deeply nested the current alternative group is.</summary>
    protected int AlternativeDepth => _alternativeDepth;

    /// <summary>Creates a no-op styler.</summary>
    /// <param name="owner">The owning iterator; must not be <see langword="null"/>.</param>
    /// <returns>The styler.</returns>
    public static RepeatStyler CreateNull(MusicIterator owner) => new NullRepeatStyler(owner);

    /// <summary>Creates a styler for <c>\repeat segno</c>.</summary>
    /// <param name="owner">The owning iterator; must not be <see langword="null"/>.</param>
    /// <returns>The styler.</returns>
    public static RepeatStyler CreateSegno(MusicIterator owner) => new SegnoRepeatStyler(owner);

    /// <summary>Creates a styler for <c>\repeat volta</c>.</summary>
    /// <param name="owner">The owning iterator; must not be <see langword="null"/>.</param>
    /// <returns>The styler.</returns>
    public static RepeatStyler CreateVolta(MusicIterator owner) => new VoltaRepeatStyler(owner);

    /// <summary>Reports that a repeat has started.</summary>
    /// <param name="spannedTime">The lifetime of the repeat in the score's timeline.</param>
    /// <param name="repeatCount">
    /// The number of times the section is performed from this starting point.
    /// </param>
    public void ReportStart(MomentInterval spannedTime, long repeatCount)
    {
        _spannedTime = spannedTime;
        _repeatCount = repeatCount;
        DerivedReportStart();
    }

    /// <summary>Reports that an alternative group is starting.</summary>
    /// <param name="start">
    /// <see cref="Direction.Negative"/> when the start of the alternative group is
    /// aligned with the start of the repeated section, otherwise
    /// <see cref="Direction.Center"/>.
    /// </param>
    /// <param name="end">
    /// <see cref="Direction.Positive"/> when the end of the alternative group is aligned
    /// with the end of the repeated section, otherwise <see cref="Direction.Center"/>.
    /// </param>
    /// <param name="inOrder">Whether the alternatives are performed in order.</param>
    /// <returns>
    /// <see langword="true"/> when the styler has determined that volta brackets should
    /// be enabled over this group of alternatives.
    /// </returns>
    public bool ReportAlternativeGroupStart(Direction start, Direction end, bool inOrder)
    {
        ++_alternativeDepth;
        return DerivedReportAlternativeGroupStart(start, end, inOrder);
    }

    /// <summary>Reports that an alternative has started.</summary>
    /// <param name="alternative">The alternative's music.</param>
    /// <param name="alternativeNumber">
    /// The index (1-based) of the alternative within its group.
    /// </param>
    /// <param name="voltaDepth">The depth of the alternative in the repeat structure.</param>
    /// <param name="voltaNumbers">The volta numbers in which the alternative is used.</param>
    public void ReportAlternativeStart(
        MusicObject alternative, long alternativeNumber, int voltaDepth, object voltaNumbers)
        => DerivedReportAlternativeStart(alternative, alternativeNumber, voltaDepth, voltaNumbers);

    /// <summary>Reports that it is time to return to the start of the repeated section.</summary>
    /// <param name="alternativeNumber">
    /// The index (1-based) of the alternative that is ending, or 0 for a simple repeat
    /// with no alternatives.
    /// </param>
    /// <param name="returnCount">The number of times this return is performed.</param>
    public void ReportReturn(long alternativeNumber, long returnCount)
    {
        // When two \alternative groups are nested and both are end-aligned, we report
        // returns for the deeper one and then remain silent when the outer one tries to
        // report.
        if (_alternativeDepth < _reportedReturnDepth)
        {
            return;
        }

        _reportedReturnDepth = _alternativeDepth;
        ReportedReturn = true;
        DerivedReportReturn(alternativeNumber, returnCount);
    }

    /// <summary>Reports that the last alternative of a group has ended.</summary>
    /// <param name="alternative">The alternative's music.</param>
    /// <param name="voltaDepth">The depth of the alternative in the repeat structure.</param>
    public void ReportAlternativeGroupEnd(MusicObject alternative, int voltaDepth)
    {
        DerivedReportAlternativeGroupEnd(alternative, voltaDepth);

        if (_alternativeDepth > 0) // paranoia
        {
            --_alternativeDepth;
            if (_alternativeDepth == 0)
            {
                _reportedReturnDepth = 0;
            }
        }
    }

    /// <summary>Announces an <c>AlternativeEvent</c> at the owner's context.</summary>
    /// <param name="element">The alternative's music, used for the event's origin.</param>
    /// <param name="direction">The alternative direction.</param>
    /// <param name="voltaDepth">The depth of the alternative in the repeat structure.</param>
    /// <param name="voltaNumbers">The volta numbers in which the alternative is used.</param>
    protected void ReportAlternativeEvent(
        MusicObject element, Direction direction, int voltaDepth, object voltaNumbers)
    {
        MusicObject ev = MusicFactory.MakeMusic(AlternativeEventSymbol);
        if (ev == null)
        {
            return;
        }

        if (element?.Origin != null)
        {
            ev.SetSpot(element.Origin);
        }

        ev.SetProperty(AlternativeDirSymbol, (long)(int)direction);
        ev.SetProperty(VoltaDepthSymbol, (long)voltaDepth);
        ev.SetProperty(VoltaNumbersSymbol, voltaNumbers);
        ev.SendToContext(Owner.Context);
    }

    /// <summary>Announces the event that ends a repeated section at the owner's context.</summary>
    /// <param name="eventSymbol">The music type to announce.</param>
    /// <param name="alternativeNumber">
    /// The alternative that is ending, announced only when positive.
    /// </param>
    /// <param name="repeatCount">The repeat count, announced only when positive.</param>
    /// <param name="returnCount">The return count, announced only when non-negative.</param>
    protected void ReportEndEvent(
        Symbol eventSymbol, long alternativeNumber, long repeatCount, long returnCount)
    {
        MusicObject ev = MusicFactory.MakeMusic(eventSymbol);
        if (ev == null)
        {
            return;
        }

        if (Owner.Music?.Origin != null)
        {
            ev.SetSpot(Owner.Music.Origin);
        }

        if (alternativeNumber > 0)
        {
            ev.SetProperty(AlternativeNumberSymbol, alternativeNumber);
        }

        if (repeatCount > 0)
        {
            ev.SetProperty(RepeatCountSymbol, repeatCount);
        }

        if (returnCount >= 0)
        {
            ev.SetProperty(ReturnCountSymbol, returnCount);
        }

        // Currently, repeat-body-start-moment helps detect conflicting jumps. In the
        // future, it might be used to engrave nested segno repeats in conjunction with a
        // mark table maintained by Mark_tracking_translator. In that future, we would
        // probably also want to report the point of the first coda mark as
        // repeat-body-end-moment.
        ev.SetProperty(RepeatBodyStartMomentSymbol, SpannedTime.Left);

        ev.SendToContext(Owner.Context);
    }

    /// <summary>Subclass hook for the start of a repeated section.</summary>
    protected abstract void DerivedReportStart();

    /// <summary>Subclass hook for the start of an alternative group.</summary>
    /// <param name="start">Whether the group starts where the repeat does.</param>
    /// <param name="end">Whether the group ends where the repeat does.</param>
    /// <param name="inOrder">Whether the alternatives are performed in order.</param>
    /// <returns>Whether volta brackets are enabled over this group.</returns>
    protected abstract bool DerivedReportAlternativeGroupStart(
        Direction start, Direction end, bool inOrder);

    /// <summary>Subclass hook for the start of one alternative.</summary>
    /// <param name="alternative">The alternative's music.</param>
    /// <param name="alternativeNumber">The index (1-based) within its group.</param>
    /// <param name="voltaDepth">The depth in the repeat structure.</param>
    /// <param name="voltaNumbers">The volta numbers the alternative is used in.</param>
    protected abstract void DerivedReportAlternativeStart(
        MusicObject alternative, long alternativeNumber, int voltaDepth, object voltaNumbers);

    /// <summary>Subclass hook for a return to the start of the repeated section.</summary>
    /// <param name="alternativeNumber">The alternative that is ending, or 0.</param>
    /// <param name="returnCount">The number of times this return is performed.</param>
    protected abstract void DerivedReportReturn(long alternativeNumber, long returnCount);

    /// <summary>Subclass hook for the end of an alternative group.</summary>
    /// <param name="alternative">The alternative's music.</param>
    /// <param name="voltaDepth">The depth in the repeat structure.</param>
    protected abstract void DerivedReportAlternativeGroupEnd(
        MusicObject alternative, int voltaDepth);

    /// <summary>The styler that announces nothing at all.</summary>
    private sealed class NullRepeatStyler : RepeatStyler
    {
        public NullRepeatStyler(MusicIterator owner)
            : base(owner)
        {
        }

        protected override void DerivedReportStart()
        {
        }

        protected override bool DerivedReportAlternativeGroupStart(
            Direction start, Direction end, bool inOrder)
            => false; // disable volta brackets

        protected override void DerivedReportAlternativeStart(
            MusicObject alternative, long alternativeNumber, int voltaDepth, object voltaNumbers)
        {
        }

        protected override void DerivedReportReturn(long alternativeNumber, long returnCount)
        {
        }

        protected override void DerivedReportAlternativeGroupEnd(
            MusicObject alternative, int voltaDepth)
        {
        }
    }

    /// <summary>The styler for <c>\repeat segno</c>: segno and coda marks, or brackets.</summary>
    private sealed class SegnoRepeatStyler : RepeatStyler
    {
        private bool _codaMarksEnabled = true;

        public SegnoRepeatStyler(MusicIterator owner)
            : base(owner)
        {
        }

        protected override void DerivedReportStart()
        {
            if (RepeatCount < 2)
            {
                return;
            }

            ReportMark(Owner.Music, 0);
        }

        protected override bool DerivedReportAlternativeGroupStart(
            Direction start, Direction end, bool inOrder)
        {
            if (RepeatCount < 2)
            {
                return false; // disable volta brackets
            }

            // Coda marks are sufficiently informative when the alternatives appear at
            // the tail of the repeated section and are performed in order. The repeat
            // body must also not be empty. In other cases, we fall back on volta
            // brackets and simplify our D.S. instructions.
            if (AlternativeDepth == 1)
            {
                bool alignedAtStart = start == Direction.Negative;
                bool alignedAtEnd = end == Direction.Positive;
                _codaMarksEnabled = !alignedAtStart && alignedAtEnd && inOrder;
            }

            // ... and nested alternatives always get volta brackets.
            return !(_codaMarksEnabled && AlternativeDepth < 2);
        }

        protected override void DerivedReportAlternativeStart(
            MusicObject alternative, long alternativeNumber, int voltaDepth, object voltaNumbers)
        {
            if (RepeatCount < 2)
            {
                return;
            }

            if (_codaMarksEnabled && AlternativeDepth < 2)
            {
                // In general, there is no reason to mark an empty passage. Importantly,
                // this allows "al Coda" structures where the final alternative has no
                // music and a section label is defined at the same moment.
                bool empty = alternative == null
                    || (!alternative.GetLength().IsNonZero
                        && !alternative.StartMoment().GracePart.IsNonZero);

                if (!empty)
                {
                    ReportMark(alternative, alternativeNumber);
                }
            }
            else
            {
                ReportAlternativeEvent(
                    alternative,
                    alternativeNumber == 1 ? Direction.Negative : Direction.Center,
                    voltaDepth,
                    voltaNumbers);
            }
        }

        protected override void DerivedReportReturn(long alternativeNumber, long returnCount)
        {
            long reps = RepeatCount;
            if (reps < 2)
            {
                return;
            }

            if (_codaMarksEnabled && AlternativeDepth < 2)
            {
                // Allow a detailed D.S. al ... instruction.
            }
            else
            {
                // We have fallen back on notating alternatives with volta brackets.
                // Keep redundant information out of our D.S. instructions.
                alternativeNumber = -1;
                reps = -1;
                returnCount = -1;
            }

            ReportEndEvent(DalSegnoEventSymbol, alternativeNumber, reps, returnCount);
        }

        protected override void DerivedReportAlternativeGroupEnd(
            MusicObject alternative, int voltaDepth)
        {
            if (RepeatCount < 2)
            {
                return;
            }

            if (_codaMarksEnabled && AlternativeDepth < 2)
            {
                // Though marks are enabled, we don't mark the end.
            }
            else
            {
                ReportAlternativeEvent(
                    alternative, Direction.Positive, voltaDepth, Nil.Instance);
            }
        }

        private void ReportMark(MusicObject music, long alternativeNumber)
        {
            Symbol eventName = alternativeNumber == 0
                ? SegnoMarkEventSymbol
                : CodaMarkEventSymbol;

            MusicObject ev = MusicFactory.MakeMusic(eventName);
            if (ev == null)
            {
                return;
            }

            if (music?.Origin != null)
            {
                ev.SetSpot(music.Origin);
            }

            ev.SendToContext(Owner.Context);
        }
    }

    /// <summary>The styler for <c>\repeat volta</c>: repeat bar lines and volta brackets.</summary>
    private sealed class VoltaRepeatStyler : RepeatStyler
    {
        public VoltaRepeatStyler(MusicIterator owner)
            : base(owner)
        {
        }

        protected override void DerivedReportStart()
        {
            MusicObject ev = MusicFactory.MakeMusic(VoltaRepeatStartEventSymbol);
            if (ev == null)
            {
                return;
            }

            if (Owner.Music?.Origin != null)
            {
                ev.SetSpot(Owner.Music.Origin);
            }

            if (RepeatCount > 0)
            {
                ev.SetProperty(RepeatCountSymbol, RepeatCount);
            }

            ev.SendToContext(Owner.Context);
        }

        protected override bool DerivedReportAlternativeGroupStart(
            Direction start, Direction end, bool inOrder)
            => RepeatCount >= 1; // below 1, disable volta brackets

        protected override void DerivedReportAlternativeStart(
            MusicObject alternative, long alternativeNumber, int voltaDepth, object voltaNumbers)
        {
            if (RepeatCount < 1)
            {
                return;
            }

            ReportAlternativeEvent(
                alternative,
                alternativeNumber == 1 ? Direction.Negative : Direction.Center,
                voltaDepth,
                voltaNumbers);
        }

        protected override void DerivedReportReturn(long alternativeNumber, long returnCount)
        {
            long reps = alternativeNumber < 1 ? RepeatCount : 0;
            ReportEndEvent(VoltaRepeatEndEventSymbol, alternativeNumber, reps, returnCount);
        }

        protected override void DerivedReportAlternativeGroupEnd(
            MusicObject alternative, int voltaDepth)
        {
            if (RepeatCount < 1)
            {
                return;
            }

            ReportAlternativeEvent(alternative, Direction.Positive, voltaDepth, Nil.Instance);
        }
    }
}
