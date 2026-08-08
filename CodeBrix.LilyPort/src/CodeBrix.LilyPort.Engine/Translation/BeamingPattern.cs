/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1999--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/beaming-pattern.cc, lily/include/beaming-pattern.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port:
//   - gc_mark() is dropped throughout, as everywhere else in this port: the
//     managed collector traces the Tuplet_description and beat-structure
//     references that upstream has to mark by hand
//   - beam counts stay UNSIGNED (uint) rather than becoming int. That is not
//     tidiness: beamify's beamlet-chipping step subtracts from a beam count and
//     upstream WRAPS when a zero-count stem is reached through the flag-direction
//     correction loop, and every later comparison reads the wrapped value. See
//     PORT-COVERAGE, BEAMING PATTERN COUNTS ARE UNSIGNED
//   - Beam_rhythmic_element is a class, not a struct, because the algorithm
//     mutates elements in place through the list; a List<struct> would hand out
//     copies and the mutations would be silently lost

/// <summary>
/// The context properties that decide how a run of stems is grouped into beams,
/// snapshotted at the moment a beam begins.
/// </summary>
public sealed class BeamingOptions
{
    private static readonly Symbol SubdivideBeamsSymbol = Symbol.Intern("subdivideBeams");
    private static readonly Symbol StrictBeatBeamingSymbol = Symbol.Intern("strictBeatBeaming");
    private static readonly Symbol RespectIncompleteBeamsSymbol
        = Symbol.Intern("respectIncompleteBeams");

    private static readonly Symbol BeatStructureSymbol = Symbol.Intern("beatStructure");
    private static readonly Symbol BeatBaseSymbol = Symbol.Intern("beatBase");
    private static readonly Symbol BeamMinimumSubdivisionSymbol
        = Symbol.Intern("beamMinimumSubdivision");
    private static readonly Symbol BeamMaximumSubdivisionSymbol
        = Symbol.Intern("beamMaximumSubdivision");

    /// <summary>Initializes the default options.</summary>
    public BeamingOptions()
    {
    }

    /// <summary>Initializes with the current values from the given context.</summary>
    /// <param name="c">The context to read the beaming properties from.</param>
    public BeamingOptions(Context c)
    {
        SubdivideBeams = Epg8Support.ToBool(c?.GetProperty(SubdivideBeamsSymbol));
        StrictBeatBeaming = Epg8Support.ToBool(c?.GetProperty(StrictBeatBeamingSymbol));
        RespectIncompleteBeams = Epg8Support.ToBool(c?.GetProperty(RespectIncompleteBeamsSymbol));
        BeatStructure = c?.GetProperty(BeatStructureSymbol);
        BeatBase = Epg8Support.ToRational(c?.GetProperty(BeatBaseSymbol), new Rational(1, 4));
        Period = CalcPeriod(c, BeatStructure, BeatBase);
        MinimumSubdivisionInterval = Epg8Support.ToRational(
            c?.GetProperty(BeamMinimumSubdivisionSymbol), Rational.Zero);
        MaximumSubdivisionInterval = Epg8Support.ToRational(
            c?.GetProperty(BeamMaximumSubdivisionSymbol), Rational.Infinity);
    }

    /// <summary>Whether beams are subdivided at beat boundaries.</summary>
    public bool SubdivideBeams { get; set; }

    /// <summary>Whether beat structure wins over neighbouring beam counts.</summary>
    public bool StrictBeatBeaming { get; set; }

    /// <summary>Whether an incomplete group at the end raises rhythmic importance.</summary>
    public bool RespectIncompleteBeams { get; set; }

    /// <summary>The beat structure list, as a Scheme list of counts.</summary>
    public object BeatStructure { get; set; } = Nil.Instance;

    /// <summary>The length of one unit of the beat structure.</summary>
    public Rational BeatBase { get; set; } = new Rational(1, 4);

    /// <summary>
    /// The length of the beat structure in whole notes. It normally equals the measure
    /// length, but in music with irregular measures the beat structure may be longer
    /// than the current measure (and be used only partly) or shorter than the current
    /// measure (and be repeated to fill the measure).
    /// </summary>
    public Rational Period { get; set; } = Rational.One;

    /// <summary>The shortest interval a subdivision may fall on.</summary>
    public Rational MinimumSubdivisionInterval { get; set; } = Rational.Zero;

    /// <summary>The longest interval a subdivision may fall on.</summary>
    public Rational MaximumSubdivisionInterval { get; set; } = Rational.Infinity;

    private static Rational CalcPeriod(Context context, object beatStructure, Rational beatBase)
    {
        Rational totalBeats = Rational.Zero;
        object cursor = beatStructure;
        while (cursor is Pair pair)
        {
            totalBeats += Epg8Support.ToRational(pair.Car, Rational.Zero);
            cursor = pair.Cdr;
        }

        Rational period = beatBase * totalBeats;

        // period == 0 is likely in a senza-misura passage (no beat structure).
        return period.IsNonZero ? period : MeasureTiming.MeasureLength(context);
    }
}

/*
  Generate beaming given durations of notes. Beam uses this to
  set_beaming () for each of its stems.
*/

/// <summary>
/// Generates beaming given the durations of notes. <c>Beam</c> uses this to set the
/// beaming of each of its stems.
/// </summary>
public sealed class BeamingPattern
{
    private readonly List<BeamRhythmicElement> _infos = new List<BeamRhythmicElement>();

    /*
      For grace beaming, which involves negative stem start moments,
      the measure position needs to undergo modulo to be nonnegative
    */

    /// <summary>Initializes a pattern whose first stem sits at the given measure offset.</summary>
    /// <param name="measureOffset">
    /// The measure position of the first stem, which must be non-negative.
    /// </param>
    public BeamingPattern(Rational measureOffset)
    {
        MeasureOffset = measureOffset;
        if (measureOffset < Rational.Zero)
        {
            Warn.ProgrammingError("measure offset should not be negative");
        }
    }

    /// <summary>
    /// The measure position of the first stem. It is never negative.
    /// </summary>
    public Rational MeasureOffset { get; }

    /// <summary>The number of stems in the pattern.</summary>
    public int Count => _infos.Count;

    /*
      The function to call to set the stems' beamlet counts
    */

    /// <summary>Computes every stem's beamlet counts.</summary>
    /// <param name="options">The beaming options in force.</param>
    public void Beamify(BeamingOptions options)
    {
        if (_infos.Count <= 1)
        {
            return;
        }

        UnbeamInvisibleStems();

        SetRhythmicImportance(options);

        {
            Direction[] flagDirections = new Direction[_infos.Count];
            Rational curBeat = Rational.Zero;
            Rational nextBeat = _infos[0].StartMoment - MeasureOffset;
            object remainingBeats = Nil.Instance;

            for (int i = 1; i < _infos.Count - 1; i++)
            {
                // If we ever allow setting custom stem flag directions that
                // automatic beam subdivision would obey, here would be the place
                // to set the value and 'continue'
                uint leftCount = _infos[i - 1].CountOf();
                uint rightCount = _infos[i + 1].CountOf();

                // Stems at boundaries of tuplet spans must have CENTER direction.
                // That's why we don't iterate 0 and infos_.size() - 1 as an optimization.
                if (!AtSpanStart(i) && !AtSpanStop(i)
                    && _infos[i].CountOf() > Math.Min(leftCount, rightCount))
                {
                    while (nextBeat <= _infos[i].StartMoment)
                    {
                        if (!(remainingBeats is Pair))
                        {
                            remainingBeats = options.BeatStructure;
                        }

                        curBeat = nextBeat;
                        Pair beats = (Pair)remainingBeats;
                        nextBeat += Epg8Support.ToRational(beats.Car, Rational.Zero)
                                    * options.BeatBase;
                        remainingBeats = beats.Cdr;
                    }

                    bool pointRight;
                    if (!options.StrictBeatBeaming && leftCount != rightCount)
                    {
                        pointRight = rightCount > leftCount;
                    }
                    else if ((_infos[i].StartMoment == curBeat) != (EndMoment(i) == nextBeat))
                    {
                        pointRight = _infos[i].StartMoment == curBeat;
                    }
                    else
                    {
                        pointRight = _infos[i].RhythmicImportance
                                     < _infos[i + 1].RhythmicImportance;
                    }

                    flagDirections[i] = pointRight ? Direction.Positive : Direction.Negative;
                }
            }

            // Correct flag directions for subdivision
            for (int i = 1; i < _infos.Count - 1; i++)
            {
                if (flagDirections[i] == Direction.Center
                    && flagDirections[i - 1] == Direction.Negative)
                {
                    flagDirections[i] = Direction.Positive;
                }

                if (flagDirections[i] == Direction.Center
                    && flagDirections[i + 1] == Direction.Positive)
                {
                    flagDirections[i] = Direction.Negative;
                }
            }

            for (int i = 1; i < _infos.Count - 1; ++i)
            {
                if (flagDirections[i] != Direction.Center)
                {
                    // beamlet count in flag_directions[i] should be preserved
                    // which is why we reference the neighbor of opposite direction
                    Direction oppositeDir = -flagDirections[i];
                    int neighborInd = i + oppositeDir.Value;

                    // if the neighbor has higher beamlet count, then
                    // the neighbor should be the one chipping their beamlet count
                    if (_infos[i].CountOf() >= _infos[neighborInd].CountOf())
                    {
                        _infos[i].BeamCountDrul[oppositeDir] -= Math.Max(
                            unchecked(_infos[i].CountOf() - _infos[neighborInd].CountOf()), 1u);
                    }
                }
            }
        }

        if (options.SubdivideBeams && options.MaximumSubdivisionInterval.Numerator != 0
            && options.MinimumSubdivisionInterval.IsFinite)
        {
            SubdivideBeams(options);
        }

        // stems at boundaries of tuplets should not have beamlets sticking out
        // of the tuplet range
        for (int i = 1; i < _infos.Count - 1; ++i)
        {
            if (AtSpanStart(i))
            {
                _infos[i].BeamCountDrul[Direction.Negative] = Math.Min(
                    BeamletCount(i, Direction.Negative),
                    BeamletCount(i - 1, Direction.Positive));
            }
            else if (AtSpanStop(i))
            {
                _infos[i].BeamCountDrul[Direction.Positive] = Math.Min(
                    BeamletCount(i, Direction.Positive),
                    BeamletCount(i + 1, Direction.Negative));
            }
        }
    }

    /// <summary>Appends a stem to the pattern.</summary>
    /// <param name="m">The stem's start moment.</param>
    /// <param name="inv">Whether the stem is invisible, as a rest's is.</param>
    /// <param name="duration">The stem's duration.</param>
    /// <param name="tuplet">The innermost tuplet the stem is under, or <see langword="null"/>.</param>
    public void AddStem(Rational m, bool inv, Duration duration, TupletDescription tuplet)
    {
        if (_infos.Count != 0 && m <= _infos[_infos.Count - 1].StartMoment)
        {
            Warn.ProgrammingError(
                "stem moment is less than or equal to than previous stem moment");
        }

        _infos.Add(new BeamRhythmicElement(m, inv, duration, tuplet));
    }

    /// <summary>The beamlet count on one side of a stem.</summary>
    /// <param name="i">The stem's index.</param>
    /// <param name="d">Which side to read.</param>
    /// <returns>The beamlet count.</returns>
    public uint BeamletCount(int i, Direction d) => _infos[i].BeamCountDrul[d];

    /// <summary>The start moment of a stem.</summary>
    /// <param name="i">The stem's index.</param>
    /// <returns>The moment.</returns>
    public Rational StartMoment(int i) => _infos[i].StartMoment;

    /// <summary>The moment a stem's duration ends at.</summary>
    /// <param name="i">The stem's index.</param>
    /// <returns>The moment.</returns>
    public Rational EndMoment(int i)
        => _infos[i].StartMoment + _infos[i].Duration.ToWholeNotes();

    /*
      Split a beaming pattern at index i and return a new
      Beaming_pattern containing the removed elements
    */

    /// <summary>
    /// Splits the pattern at the given index, returning a new pattern holding the
    /// elements that were removed.
    /// </summary>
    /// <param name="i">The last index to keep in this pattern.</param>
    /// <param name="measureLength">The measure length, used to fold the new offset.</param>
    /// <returns>The new pattern.</returns>
    public BeamingPattern SplitPattern(int i, Rational measureLength)
    {
        BeamingPattern newPattern = new BeamingPattern(
            (EndMoment(i) - (_infos[0].StartMoment - MeasureOffset))
            .ModuloRational(measureLength));

        for (int j = i + 1; j < _infos.Count; j++)
        {
            newPattern.AddStem(_infos[j].StartMoment, _infos[j].Invisible,
                               _infos[j].Duration, _infos[j].Tuplet);
        }

        while (_infos.Count > i + 1)
        {
            _infos.RemoveAt(_infos.Count - 1);
        }

        return newPattern;
    }

    private void SetRhythmicImportance(BeamingOptions options)
    {
        // span_contexts will always be non empty since there always exists
        // the root span
        LinkedList<SpanPosition> spanContexts = new LinkedList<SpanPosition>();
        spanContexts.AddFirst(new SpanPosition(
            options.Period, 1, _infos[0].StartMoment - MeasureOffset,
            EndMoment(_infos.Count - 1)));

        // infos_[i].duration_.factor_ is not sufficient for calculations for certain
        // cases, so we must manually alter the current factor based on incoming and
        // outgoing tuplet spans
        Rational currentFactor = Rational.One;

        for (int i = 0; i < _infos.Count; ++i)
        {
            Rational stemPos = _infos[i].StartMoment;

            // Delete expired tuplet spans
            while (spanContexts.First.Value.Tuplet is TupletDescription curTuplet)
            {
                if (curTuplet.TupletStop > stemPos)
                {
                    break;
                }

                // Undo the expired tuplet span factor to the current factor
                currentFactor /= (long)curTuplet.Numerator;
                currentFactor *= (long)curTuplet.Denominator;
                spanContexts.RemoveFirst();
            }

            // Insert tuplet spans that are not already added
            {
                LinkedListNode<SpanPosition> insertPosition = null;
                TupletDescription currentParent = spanContexts.First.Value.Tuplet;
                TupletDescription tupletIt = _infos[i].Tuplet;
                while (!ReferenceEquals(tupletIt, currentParent))
                {
                    if (tupletIt.TupletStart < stemPos)
                    {
                        SpanPosition inserted = new SpanPosition(tupletIt);
                        insertPosition = insertPosition == null
                            ? spanContexts.AddFirst(inserted)
                            : spanContexts.AddAfter(insertPosition, inserted);
                        currentFactor *= (long)tupletIt.Numerator;
                        currentFactor /= (long)tupletIt.Denominator;
                    }

                    // Rhythmic importance of start of tuplet span should
                    // be set from parent context. If the first stem
                    // is part of a tuplet span, it is not necessarily
                    // the first note of the tuplet span, whereas
                    // a subsequent stem that has a different tuplet span
                    // is guaranteed to be the start of said tuplet span
                    // and may break the loop as an optimization
                    else if (i > 0)
                    {
                        break;
                    }

                    tupletIt = tupletIt.Parent;
                }
            }

            // the appropriate Span_position for current/next moment
            SpanPosition curPosition = spanContexts.First.Value;
            curPosition.Update(stemPos);

            // Notice that if the current stem introduces new tuplets,
            // those tuplets' factors aren't used until the next stem.
            // rhythmic_importance_ of stems at start of a tuplet span
            // is irrelevant to the tuplet span, but is needed for the parent
            // span as if the stem represents the whole child tuplet
            if (stemPos == curPosition.CurrentMoment)
            {
                _infos[i].RhythmicImportance
                    = RhythmicImportanceForLength(
                          (curPosition.NextMoment - curPosition.CurrentMoment) / currentFactor)
                      - (int)curPosition.BeatLevel();
            }
            else
            {
                Rational momentRelativeToBeat
                    = (stemPos - curPosition.CurrentMoment) / currentFactor;

                // We must account for numerator of maximum_subdivision_interval
                // which may be greater than 1. Setting it to beamlet count when
                // moment_relative_to_beat numerator isn't divisible basically
                // means don't subdivide here, even though we technically can in
                // the case of the numerator being a power of 2
                if (options.MaximumSubdivisionInterval.IsFinite
                    && momentRelativeToBeat.Numerator
                       % options.MaximumSubdivisionInterval.Numerator != 0)
                {
                    _infos[i].RhythmicImportance = _infos[i].Duration.DurationLog - 2;
                }
                else
                {
                    _infos[i].RhythmicImportance
                        = RhythmicImportanceForPosition(momentRelativeToBeat);

                    // We must preserve the tuplet denominator subdivision structure
                    // as without this line, a sextuplet of 6 equal-length notes
                    // would subdivide between the 2nd and 3rd notes
                    _infos[i].RhythmicImportance = Math.Max(
                        _infos[i].RhythmicImportance,
                        RhythmicImportanceForLength(
                            (curPosition.NextMoment - stemPos) / currentFactor));

                    // Account for the right side of the subdivision having
                    // incomplete length as that should make the rhythmic_importance_
                    // value higher
                    if (options.RespectIncompleteBeams
                        && EndMoment(i) < curPosition.EndMoment)
                    {
                        _infos[i].RhythmicImportance = Math.Max(
                            _infos[i].RhythmicImportance,
                            RhythmicImportanceForLength(
                                (curPosition.EndMoment - stemPos) / currentFactor));
                    }
                }
            }
        }
    }

    private void SubdivideBeams(BeamingOptions options)
    {
        // if minimum beam subdivision interval is 0, don't bother with it
        bool checkMinimumSubdivisionCount = options.MaximumSubdivisionInterval.IsFinite;
        bool checkMaximumSubdivisionCount = options.MinimumSubdivisionInterval.Numerator != 0;

        // meaning of min/max for beam count is opposite of min/max for interval
        // since we are taking the logarithm of the denominators (basically
        // negating the logarithm of the whole fraction)
        int minimumSubdivisionCount = 0;
        int maximumSubdivisionCount = 0;

        if (checkMinimumSubdivisionCount)
        {
            minimumSubdivisionCount
                = RhythmicImportanceForPosition(options.MaximumSubdivisionInterval);
        }

        if (checkMaximumSubdivisionCount)
        {
            maximumSubdivisionCount
                = RhythmicImportanceForPosition(options.MinimumSubdivisionInterval);

            if (checkMinimumSubdivisionCount)
            {
                maximumSubdivisionCount
                    += Misc.IntLog2(options.MaximumSubdivisionInterval.Numerator)
                       - Misc.IntLog2(options.MinimumSubdivisionInterval.Numerator);
            }
        }

        if (!checkMinimumSubdivisionCount || minimumSubdivisionCount < 1)
        {
            minimumSubdivisionCount = 1;
        }

        // beam counts will be set to at least
        // minimum_subdivision_beam_count_level, whereas
        // maximum_subdivision_beam_count_level is only used to
        // compare rhythmic importance
        for (int i = 1; i < _infos.Count - 1; ++i)
        {
            uint predictedLeftCount = (uint)Math.Max(
                _infos[i].RhythmicImportance, minimumSubdivisionCount);
            uint predictedRightCount = (uint)Math.Max(
                _infos[i + 1].RhythmicImportance, minimumSubdivisionCount);

            // we can only chip off one side
            if ((!checkMaximumSubdivisionCount
                 || _infos[i].RhythmicImportance <= maximumSubdivisionCount)
                && predictedLeftCount < BeamletCount(i, Direction.Negative)
                && BeamletCount(i, Direction.Positive) == _infos[i].CountOf())
            {
                _infos[i].BeamCountDrul[Direction.Negative] = predictedLeftCount;
            }
            else if ((!checkMaximumSubdivisionCount
                      || _infos[i + 1].RhythmicImportance <= maximumSubdivisionCount)
                     && predictedRightCount < BeamletCount(i, Direction.Positive)
                     && BeamletCount(i, Direction.Negative) == _infos[i].CountOf())
            {
                _infos[i].BeamCountDrul[Direction.Positive] = predictedRightCount;
            }
        }
    }

    /*
      Invisible stems should be treated as though they have the same number of
      beams as their least-beamed neighbour. Here we go through the stems and
      modify the invisible stems to satisfy this requirement.
    */
    private void UnbeamInvisibleStems()
    {
        for (int i = 1; i < _infos.Count; i++)
        {
            if (_infos[i].Invisible)
            {
                uint b = Math.Min(_infos[i].CountOf(), _infos[i - 1].CountOf());
                _infos[i].BeamCount = b;
                _infos[i].BeamCountDrul[Direction.Negative] = b;
                _infos[i].BeamCountDrul[Direction.Positive] = b;
            }
        }

        if (_infos.Count != 0)
        {
            for (int i = 0; i < _infos.Count - 1; i++)
            {
                if (_infos[i].Invisible)
                {
                    uint b = Math.Min(_infos[i].CountOf(), _infos[i + 1].CountOf());
                    _infos[i].BeamCount = b;
                    _infos[i].BeamCountDrul[Direction.Negative] = b;
                    _infos[i].BeamCountDrul[Direction.Positive] = b;
                }
            }
        }
    }

    private bool AtSpanStart(int i)
    {
        Rational first = StartMoment(0);
        Rational bound = _infos[i].Tuplet != null
            ? (_infos[i].Tuplet.TupletStart > first ? _infos[i].Tuplet.TupletStart : first)
            : first;
        return StartMoment(i) == bound;
    }

    private bool AtSpanStop(int i)
    {
        Rational lastMoment = EndMoment(_infos.Count - 1);
        Rational bound = _infos[i].Tuplet != null
            ? (_infos[i].Tuplet.TupletStop < lastMoment
                   ? _infos[i].Tuplet.TupletStop
                   : lastMoment)
            : lastMoment;
        return EndMoment(i) == bound;
    }

    private static int RhythmicImportanceForPosition(Rational r)
        => Misc.IntLog2(r.Denominator) - 2
           - (r.Denominator == 1 ? Misc.IntLog2(r.Numerator) : 0);

    private static int RhythmicImportanceForLength(Rational r)
        => Misc.IntLog2(r.Denominator) - 2 - Misc.IntLog2(r.Numerator);

    /*
      Represents a stem belonging to a beam. Sometimes (for example, if the stem
      belongs to a rest and stemlets aren't used) the stem will be invisible.

      The rhythmic_importance_ of an element tells us the significance of the
      moment at which this element occurs. A naive calculation would be
      the binary logarithm of the denominator of the difference between
      the stem's start position and the first stem's start position, minus 2.
      For examle, if we have consecutive 32nd notes, their rhythmic importance will
      likely be 0 3 2 3 1 3 2 3 0 3 2 3 ...

      Smaller values are more important. The rhythmic_importance_ is decided
      and filled in by Beaming_pattern. The first stem's rhythmic_importance_
      value is theoretically unnecessary for auto-beaming calculations,
      but this is
      explained in set_rhythmic_importance.
    */
    private sealed class BeamRhythmicElement
    {
        internal BeamRhythmicElement(
            Rational m, bool inv, Duration duration, TupletDescription tuplet)
        {
            StartMoment = m;
            BeamCount = (uint)Math.Max(duration.DurationLog - 2, 0);
            Invisible = inv;
            Duration = duration;
            Tuplet = tuplet;

            BeamCountDrul[Direction.Negative] = BeamCount;
            BeamCountDrul[Direction.Positive] = BeamCount;
        }

        internal Rational StartMoment { get; }

        internal uint BeamCount;

        // stores beam count of left-right neighboring stems
        internal DrulArray<uint> BeamCountDrul;

        internal int RhythmicImportance;

        // rests are "invisibile"
        internal bool Invisible { get; }

        internal Duration Duration { get; }

        // if not under a tuplet, then null
        internal TupletDescription Tuplet { get; }

        internal uint CountOf() => BeamCount;
    }

    /*
      Temporary class that stores current_moment_ and next_moment_ for
      each tuplet span layer. When new tuplets are introduced, they get
      their own Span_position to keep track. When a tuplet ends,
      the position context of the parent tuplet span goes back into effect
    */
    private sealed class SpanPosition
    {
        private readonly Rational _beatBase;
        private readonly uint _beatLength; // stays constant
        private Rational _currentMoment;
        private Rational _nextMoment;
        private int _momentNum = -1;

        // Since tuplet start may be negative, current_moment_ must
        // be set to same value of next_moment_ to be safe.
        // If we have a sextuplet, beat_length_ should be 3
        internal SpanPosition(TupletDescription tuplet)
        {
            _beatBase = (tuplet.TupletStop - tuplet.TupletStart) / (long)tuplet.Denominator;

            // last set bit of tuplet.denominator_
            _beatLength = tuplet.Denominator / LowestSetBit(tuplet.Denominator);
            _currentMoment = tuplet.TupletStart;
            _nextMoment = tuplet.TupletStart;
            EndMoment = tuplet.TupletStop;
            Tuplet = tuplet;
        }

        internal SpanPosition(Rational beatBase, uint beatLength, Rational start, Rational end)
        {
            _beatBase = beatBase;
            _beatLength = beatLength;
            _currentMoment = start;
            _nextMoment = start;
            EndMoment = end;
            Tuplet = null;
        }

        internal Rational EndMoment { get; }

        internal TupletDescription Tuplet { get; }

        internal Rational CurrentMoment => _currentMoment;

        internal Rational NextMoment => _nextMoment;

        // Must be called before each stem to align the moments
        internal void Update(Rational pos)
        {
            while (_nextMoment <= pos)
            {
                _currentMoment = _nextMoment;
                _nextMoment += _beatBase;
                ++_momentNum;
            }
        }

        internal uint BeatLevel()
        {
            // Incomplete 'beats' or start of tuplet span should not have
            // their rhythmic importance value lowered
            if (_momentNum == 0 || unchecked((uint)_momentNum) % _beatLength != 0)
            {
                return 0;
            }

            int beatNum = _momentNum / (int)_beatLength;
            return (uint)(Misc.IntLog2(beatNum & -beatNum) + 1);
        }

        private static uint LowestSetBit(uint value) => unchecked(value & (~value + 1u));
    }
}
