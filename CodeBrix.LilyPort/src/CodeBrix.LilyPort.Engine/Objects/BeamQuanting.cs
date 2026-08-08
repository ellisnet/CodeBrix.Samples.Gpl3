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

using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/beam-quanting.cc, lily/include/beam-scoring-problem.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.
//
// THIS FILE IS A SCORER. It is a decades-tuned heuristic and it is translated
// LITERALLY — every demerit, constant, comparison and loop bound is upstream's.
// A plausible-looking improvement here is a parity bug, so nothing is tidied,
// reordered, or "simplified", including the several places where upstream computes
// a value it does not use.
//
// Two translation decisions worth knowing about:
//   - the configuration queue reproduces libstdc++'s push_heap/pop_heap EXACTLY
//     rather than using System.Collections.Generic.PriorityQueue. Beam quanting
//     routinely produces configurations with EQUAL demerits (symmetric beams are
//     the normal case, not the exotic one), and which of them is returned is the
//     beam's final position. Two unstable heaps do not have to break a tie the
//     same way; only the same heap does. See ConfigurationHeap.
//   - round_halfway_up is floor(x - 0.5) + 1.0, NOT floor(x + 0.5). Upstream says
//     so in libc-extension.cc and the difference shows on exact .5 boundaries,
//     which is precisely where a quant sits.

/// <summary>The scorers, ordered by increasing expensiveness.</summary>
internal enum Scorers
{
    /// <summary>The distance from the unquanted position; charged when the config is made.</summary>
    OriginalDistance,

    /// <summary>Deviation from the calculated ideal slope.</summary>
    SlopeIdeal,

    /// <summary>Going against the direction of the musical pattern.</summary>
    SlopeMusical,

    /// <summary>Sloping against the damped direction.</summary>
    SlopeDirection,

    /// <summary>A horizontal beam sitting between staff lines.</summary>
    HorizontalInter,

    /// <summary>Beams that would hide or graze a staff line.</summary>
    Forbidden,

    /// <summary>Stems that end up too short or far from ideal.</summary>
    StemLengths,

    /// <summary>Collisions with covered grobs.</summary>
    Collisions,

    /// <summary>The number of scorers.</summary>
    NumScorers,
}

/// <summary>One candidate pair of beam end positions, and the demerits it has earned.</summary>
internal sealed class BeamConfiguration
{
    internal DrulArray<double> Y;
    internal double Demerits;
    internal string ScoreCard = string.Empty;
    internal int NextScorerTodo;

    internal BeamConfiguration()
    {
        Demerits = 0.0;
        NextScorerTodo = (int)Scorers.OriginalDistance;
    }

    internal bool Done() => NextScorerTodo >= (int)Scorers.NumScorers;

    internal void Add(double demerit, string reason)
    {
        Demerits += demerit;

        if (demerit != 0.0)
        {
            ScoreCard += " " + reason + " "
                         + demerit.ToString("F2", CultureInfo.InvariantCulture);
        }
    }

    internal static BeamConfiguration NewConfig(DrulArray<double> start, DrulArray<double> offset)
    {
        BeamConfiguration qs = new BeamConfiguration();
        qs.Y = new DrulArray<double>(
            (int)start[Direction.Negative] + offset[Direction.Negative],
            (int)start[Direction.Positive] + offset[Direction.Positive]);

        // This orders the sequence so we try combinations closest to the
        // the ideal offset first.
        double startScore
            = Math.Abs(offset[Direction.Positive]) + Math.Abs(offset[Direction.Negative]);
        qs.Demerits = startScore / 1000.0;
        qs.NextScorerTodo = (int)Scorers.OriginalDistance + 1;

        return qs;
    }
}

/// <summary>The tunable demerits, read out of the beam's <c>details</c> alist.</summary>
internal sealed class BeamQuantParameters
{
    internal double SecondaryBeamDemerit;
    internal double StemLengthDemeritFactor;
    internal double RegionSize;

    /*
      threshold to combat rounding errors.
    */
    internal double BeamEps;

    // possibly ridiculous, but too short stems just won't do
    internal double StemLengthLimitPenalty;
    internal double DampingDirectionPenalty;
    internal double MusicalDirectionFactor;
    internal double HintDirectionPenalty;
    internal double IdealSlopeFactor;
    internal double RoundToZeroSlope;
    internal double CollisionPenalty;
    internal double CollisionPadding;
    internal double HorizontalInterQuantPenalty;
    internal double StemCollisionFactor;

    internal void Fill(Grob him)
    {
        object details = him.GetProperty(Symbol.Intern("details"));

        // General
        BeamEps = GetDetail(details, Symbol.Intern("beam-eps"), 1e-3);
        RegionSize = GetDetail(details, Symbol.Intern("region-size"), 2);

        // forbidden quants
        SecondaryBeamDemerit
            = GetDetail(details, Symbol.Intern("secondary-beam-demerit"), 10.0)

              // For stems that are non-standard, the forbidden beam quanting
              // doesn't really work, so decrease their importance.
              * Math.Exp(-8
                         * Math.Abs(1.0
                                    - Stem.ToDouble(
                                        him.GetProperty(Symbol.Intern("length-fraction")),
                                        1.0)));
        StemLengthDemeritFactor
            = GetDetail(details, Symbol.Intern("stem-length-demerit-factor"), 5);
        HorizontalInterQuantPenalty
            = GetDetail(details, Symbol.Intern("horizontal-inter-quant"), 500);

        StemLengthLimitPenalty
            = GetDetail(details, Symbol.Intern("stem-length-limit-penalty"), 5000);
        DampingDirectionPenalty
            = GetDetail(details, Symbol.Intern("damping-direction-penalty"), 800);
        HintDirectionPenalty
            = GetDetail(details, Symbol.Intern("hint-direction-penalty"), 20);
        MusicalDirectionFactor
            = GetDetail(details, Symbol.Intern("musical-direction-factor"), 400);
        IdealSlopeFactor = GetDetail(details, Symbol.Intern("ideal-slope-factor"), 10);
        RoundToZeroSlope = GetDetail(details, Symbol.Intern("round-to-zero-slope"), 0.02);

        // Collisions
        CollisionPenalty = GetDetail(details, Symbol.Intern("collision-penalty"), 500);

        /* For grace notes, beams get scaled down to 80%, but glyphs go down
           to 63% (magstep -4 for accidentals). To make the padding
           commensurate with glyph size for grace notes, we take the square
           of the length fraction, yielding a 64% decrease.
         */
        double lengthFraction
            = Stem.ToDouble(him.GetProperty(Symbol.Intern("length-fraction")), 1.0);
        CollisionPadding
            = GetDetail(details, Symbol.Intern("collision-padding"), 0.5)
              * (lengthFraction * lengthFraction);
        StemCollisionFactor = GetDetail(details, Symbol.Intern("stem-collision-factor"), 0.1);
    }

    internal static double GetDetail(object alist, Symbol sym, double def)
    {
        Pair entry = SchemeUtilities.Assq(sym, alist);

        if (entry != null)
        {
            return Stem.ToDouble(entry.Cdr, def);
        }

        return def;
    }
}

/// <summary>A grob the beam must avoid, reduced to the geometry the scorer needs.</summary>
internal struct BeamCollision
{
    internal double X;
    internal Interval Y;
    internal double BasePenalty;

    // Need to add beam_config->y to get actual offsets.
    internal Interval BeamY;
}

/*
  Parameters for a single beam.  Precomputed to save time in
  scoring individual configurations.

  */

/// <summary>
/// The beam-position problem for one beam: everything the scorers need, precomputed
/// once, plus the search that picks the best pair of end positions.
/// </summary>
internal sealed class BeamScoringProblem
{
    private Spanner _beam;

    private DrulArray<double> _unquantedY;
    private bool _alignBrokenIntos;
    private bool _doInitialSlopeCalculations;

    private double _staffSpace;
    private double _beamThickness;
    private double _lineThickness;
    private double _musicalDy;
    private int _normalStemCount;
    private double _xSpan;

    /*
      Do stem computations.  These depend on YL and YR linearly, so we can
      precompute for every stem 2 factors.

      We store some info to quickly interpolate.  The stemlengths are
      affine linear in YL and YR. If YL == YR == 0, then we might have
      stem_y != 0.0, when we're cross staff.
    */
    private readonly List<StemInfo> _stemInfos = new List<StemInfo>();
    private readonly List<double> _chordStartY = new List<double>();
    private readonly List<Interval> _headPositions = new List<Interval>();
    private readonly List<Slice> _beamMultiplicity = new List<Slice>();
    private readonly List<bool> _isNormal = new List<bool>();
    private readonly List<double> _baseLengths = new List<double>();
    private readonly List<double> _stemXPositions = new List<double>();
    private readonly List<double> _stemYPositions = new List<double>();

    private bool _isXStaff;
    private bool _isKnee;

    private readonly BeamQuantParameters _parameters = new BeamQuantParameters();

    private double _staffRadius;
    private DrulArray<int> _edgeBeamCounts;
    private DrulArray<Direction> _edgeDirs;

    // Half-open intervals, representing allowed positions for the beam,
    // starting from close to the notehead to the direction of the stem
    // end.  This is used for quickly weeding out invalid
    // Beam_configurations.
    private DrulArray<Interval> _quantRange;
    private int _maxBeamCount;
    private double _lengthFraction;
    private double _beamTranslation;
    private readonly List<BeamCollision> _collisions = new List<BeamCollision>();
    private List<Beam.BeamSegment> _segments = new List<Beam.BeamSegment>();

    private static readonly Direction[] BothDirections
        = { Direction.Negative, Direction.Positive };

    /// <summary>Sets up the problem and does the initial slope work.</summary>
    /// <param name="me">The beam.</param>
    /// <param name="ys">The starting positions, infinite when they are to be computed.</param>
    /// <param name="alignBrokenIntos">Whether the broken pieces align.</param>
    internal BeamScoringProblem(Grob me, DrulArray<double> ys, bool alignBrokenIntos)
    {
        _beam = me as Spanner;
        _unquantedY = ys;
        _alignBrokenIntos = alignBrokenIntos;

        _parameters.Fill(me);
        InitInstanceVariables(me, ys, alignBrokenIntos);
        if (_doInitialSlopeCalculations)
        {
            LeastSquaresPositions();
            SlopeDamping();
            ShiftRegionToValid();
        }
    }

    // Compute the increase from dr.front () to dr.back ().
    private static double Delta(DrulArray<double> dr)
        => dr[Direction.Positive] - dr[Direction.Negative];

    // Add x if x is positive, add |x|*fac if x is negative.
    private static double ShrinkExtraWeight(double x, double fac)
        => Math.Abs(x) * ((x < 0) ? fac : 1.0);

    private static double MyModf(double x) => x - Math.Floor(x);

    // Upstream's libc-extension.cc: floor (x - 0.5) + 1.0, NOT floor (x + 0.5).
    // EPG11/EPG12 (2026-08-08) moved the arithmetic to Flower's LibcExtension, which is
    // the file upstream declares it in, once ties and slurs became callers too.
    private static double RoundHalfwayUp(double x) => LibcExtension.RoundHalfwayUp(x);

    private double YAt(double x, BeamConfiguration p)
        => p.Y[Direction.Negative] + (x * Delta(p.Y) / _xSpan);

    /****************************************************************/

    /*
      TODO:

      - Make all demerits customisable

      - Add demerits for quants per se, as to forbid a specific quant
      entirely
    */
    private void AddCollision(double x, Interval y, double scoreFactor)
    {
        // We used to screen for quant range, but no more.

        BeamCollision c = default;
        c.BeamY = Interval.Empty;
        c.BeamY.SetEmpty();

        for (int j = 0; j < _segments.Count; j++)
        {
            if (_segments[j].Horizontal.Contains(x))
            {
                c.BeamY.AddPoint(_segments[j].VerticalCount * _beamTranslation);
            }

            if (_segments[j].Horizontal[Direction.Negative] > x)
            {
                break;
            }
        }

        c.BeamY.Widen(0.5 * _beamThickness);

        c.X = x;

        y = y * (1 / _staffSpace);
        c.Y = y;
        c.BasePenalty = scoreFactor;
        _collisions.Add(c);
    }

    private void InitInstanceVariables(Grob me, DrulArray<double> ys, bool alignBrokenIntos)
    {
        _beam = me as Spanner;
        _unquantedY = ys;

        /*
          If 'ys' are finite, use them as starting points for y-positions of the
          ends of the beam, instead of the best-fit through the natural ends of
          the stems.  Otherwise, we want to do initial slope calculations.
        */
        _doInitialSlopeCalculations = false;
        foreach (Direction d in BothDirections)
        {
            _doInitialSlopeCalculations |= !double.IsFinite(_unquantedY[d]);
        }

        /*
          Calculations are relative to a unit-scaled staff, i.e. the quants are
          divided by the current staff_space_.
        */
        _staffSpace = StaffSymbolReferencer.StaffSpace(_beam);
        _beamThickness = Beam.GetBeamThickness(_beam) / _staffSpace;
        _lineThickness = StaffSymbolReferencer.LineThickness(_beam) / _staffSpace;
        _maxBeamCount = Beam.GetBeamCount(_beam);
        _lengthFraction
            = Stem.ToDouble(_beam.GetProperty(Symbol.Intern("length-fraction")), 1.0);

        // This is the least-squares DY, corrected for concave beams.
        _musicalDy = Stem.ToDouble(_beam.GetProperty(Symbol.Intern("least-squares-dy")), 0);

        List<Spanner> beams = new List<Spanner>();
        _alignBrokenIntos = alignBrokenIntos;
        if (_alignBrokenIntos)
        {
            Spanner orig = _beam.Original;
            if (orig == null)
            {
                _alignBrokenIntos = false;
            }
            else if (orig.BrokenIntos.Count == 0)
            {
                _alignBrokenIntos = false;
            }
            else
            {
                beams.AddRange(orig.BrokenIntos);
            }
        }

        if (!_alignBrokenIntos)
        {
            beams.Add(_beam);
        }

        /*
          x_span_ is a single scalar, cumulatively summing the length of all the
          segments the parent beam was broken-into.
        */
        _xSpan = 0.0;
        _isKnee = false;
        _normalStemCount = 0;
        for (int i = 0; i < beams.Count; i++)
        {
            IReadOnlyList<Grob> stems
                = PointerGroupInterface.ExtractGrobSet(beams[i], Symbol.Intern("stems"));
            IReadOnlyList<Grob> fakeCollisions
                = PointerGroupInterface.ExtractGrobSet(beams[i], Symbol.Intern("covered-grobs"));
            List<Grob> collisions = new List<Grob>();

            for (int j = 0; j < fakeCollisions.Count; j++)
            {
                if (ReferenceEquals(fakeCollisions[j].GetSystem(), beams[i].GetSystem()))
                {
                    collisions.Add(fakeCollisions[j]);
                }
            }

            Grob[] common = new Grob[Axes.Count];
            foreach (Axis a in new[] { Axis.X, Axis.Y })
            {
                common[(int)a] = Stem.CommonRefpointOfArray(stems, beams[i], a);
            }

            foreach (Direction d in BothDirections)
            {
                // Null-guarded for the same reason Beam::print's walk is — see
                // PORT-COVERAGE, BEAM BOUNDS BEFORE LINE BREAKING.
                common[(int)Axis.X] = Beam.CommonWithBound(beams[i], common[(int)Axis.X], d);
            }

            // positions of the endpoints of this beam segment, including any overhangs
            Interval xPos = ReadInterval(
                beams[i].GetProperty(Symbol.Intern("X-positions")), new Interval(0.0, 0.0));

            DrulArray<Grob> edgeStems = new DrulArray<Grob>(
                Beam.FirstNormalStem(beams[i]), Beam.LastNormalStem(beams[i]));

            DrulArray<bool> dirsFound = new DrulArray<bool>(false, false);

            double myY = beams[i].RelativeCoordinate(common[(int)Axis.Y], Axis.Y);

            Interval beamWidth = new Interval(-1.0, -1.0);
            for (int j = 0; j < stems.Count; j++)
            {
                Grob s = stems[j];
                _beamMultiplicity.Add(Stem.BeamMultiplicity(stems[j]));
                _headPositions.Add(Stem.HeadPositions(stems[j]));
                _isNormal.Add(Stem.IsNormalStem(stems[j]));

                StemInfo si = Stem.GetStemInfo(s);
                si.Scale(1 / _staffSpace);
                _stemInfos.Add(si);
                _chordStartY.Add(Stem.ChordStartY(s));
                dirsFound[si.Dir] = true;

                Beam.BeamStemEnd stemEnd = Beam.CalcStemY(
                    beams[i], s, common, xPos[Direction.Negative], xPos[Direction.Positive],
                    Direction.Center, new Interval(0, 0), 0);
                double y = stemEnd.StemY;

                /* Remark:  French Beaming is irrelevant for beam quanting */
                _baseLengths.Add(y / _staffSpace);
                _stemXPositions.Add(
                    s.RelativeCoordinate(common[(int)Axis.X], Axis.X)
                    - xPos[Direction.Negative] + _xSpan);
                _stemYPositions.Add(
                    s.RelativeCoordinate(common[(int)Axis.Y], Axis.Y) - myY);

                if (_isNormal[_isNormal.Count - 1])
                {
                    if (beamWidth[Direction.Negative] == -1.0)
                    {
                        beamWidth[Direction.Negative] = _stemXPositions[_stemXPositions.Count - 1];
                    }

                    beamWidth[Direction.Positive] = _stemXPositions[_stemXPositions.Count - 1];
                }
            }

            _edgeDirs = new DrulArray<Direction>(Direction.Center, Direction.Center);
            _normalStemCount += Beam.NormalStemCount(beams[i]);
            if (_normalStemCount != 0)
            {
                _edgeDirs = new DrulArray<Direction>(
                    _stemInfos[0].Dir, _stemInfos[_stemInfos.Count - 1].Dir);
            }

            _isXStaff = common[(int)Axis.Y] != null
                        && common[(int)Axis.Y].HasInterface(Symbol.Intern("align-interface"));
            _isKnee |= dirsFound[Direction.Negative] && dirsFound[Direction.Positive];

            _staffRadius = Stem.StaffRadius(beams[i]);
            _edgeBeamCounts = new DrulArray<int>(
                Stem.BeamMultiplicity(stems[0]).Length + 1,
                Stem.BeamMultiplicity(stems[stems.Count - 1]).Length + 1);

            // TODO - why are we dividing by staff_space_?
            _beamTranslation = Beam.GetBeamTranslation(beams[i]) / _staffSpace;

            foreach (Direction d in BothDirections)
            {
                Interval range = _quantRange[d];
                range.SetFull();
                _quantRange[d] = range;
                if (edgeStems[d] == null)
                {
                    continue;
                }

                double stemOffset
                    = edgeStems[d].RelativeCoordinate(common[(int)Axis.Y], Axis.Y)
                      - beams[i].RelativeCoordinate(common[(int)Axis.Y], Axis.Y);
                Interval heads = Stem.HeadPositions(edgeStems[d]) * (0.5 * _staffSpace);

                Direction ed = _edgeDirs[d];
                heads.Widen((0.5 * _staffSpace)
                            + ((_edgeBeamCounts[d] - 1) * _beamTranslation)
                            + (_beamThickness * .5));
                range = _quantRange[d];
                range[-ed] = heads[ed] + stemOffset;
                _quantRange[d] = range;
            }

            _segments = Beam.GetBeamSegments(beams[i]);
            _segments.Sort((a, b) =>
                a.Horizontal[Direction.Negative].CompareTo(b.Horizontal[Direction.Negative]));
            for (int j = 0; j < _segments.Count; j++)
            {
                Interval h = _segments[j].Horizontal;
                h.Translate(_xSpan - xPos[Direction.Negative]);
                _segments[j].Horizontal = h;
            }

            List<Grob> collidingStems = new List<Grob>();
            HashSet<Grob> collidingStemsSeen = new HashSet<Grob>();
            for (int j = 0; j < collisions.Count; j++)
            {
                if (!collisions[j].IsLive)
                {
                    continue;
                }

                if (collisions[j].HasInterface(Symbol.Intern("beam-interface"))
                    && Beam.IsCrossStaff(collisions[j]))
                {
                    continue;
                }

                Box b = new Box();
                foreach (Axis a in new[] { Axis.X, Axis.Y })
                {
                    b[a] = collisions[j].Extent(common[(int)a], a);
                }

                if (b[Axis.X][Direction.Positive] < xPos[Direction.Negative]
                    || b[Axis.X][Direction.Negative] > xPos[Direction.Positive])
                {
                    continue;
                }

                if (b[Axis.X].IsEmpty || b[Axis.Y].IsEmpty)
                {
                    continue;
                }

                Interval bx = b[Axis.X];
                bx.Translate(_xSpan - xPos[Direction.Negative]);
                b[Axis.X] = bx;
                Interval by = b[Axis.Y];
                by.Translate(-myY);
                b[Axis.Y] = by;
                double width = b[Axis.X].Length;
                double widthFactor = Math.Sqrt(width / _staffSpace);

                foreach (Direction d in BothDirections)
                {
                    AddCollision(b[Axis.X][d], b[Axis.Y], widthFactor);
                }

                Grob stem = collisions[j].GetObject(Symbol.Intern("stem")) as Grob;
                if (stem != null && stem.HasInterface(Symbol.Intern("stem-interface"))
                    && Stem.IsNormalStem(stem))
                {
                    if (collidingStemsSeen.Add(stem))
                    {
                        collidingStems.Add(stem);
                    }
                }
            }

            foreach (Grob s in collidingStems)
            {
                Interval ext = LooseColumns.RobustRelativeExtent(s, common[(int)Axis.X], Axis.X);
                ext.Translate(-xPos[Direction.Negative] + _xSpan);
                double x = ext.Center;

                Direction stemDir = Stem.GetGrobDirection(s);
                Interval y = Interval.Empty;
                y.SetFull();
                y[-stemDir] = Stem.ChordStartY(s)
                              + s.RelativeCoordinate(common[(int)Axis.Y], Axis.Y)
                              - myY;

                double factor = _parameters.StemCollisionFactor;
                if (!(s.GetObject(Symbol.Intern("beam")) is Grob))
                {
                    factor = 1.0;
                }

                AddCollision(x, y, factor);
            }

            _xSpan += beams[i].SpannerLength();
        }
    }

    // Assuming V is not empty, pick a 'reasonable' point inside V.
    private static double PointInInterval(Interval v, double dist)
    {
        if (double.IsInfinity(v[Direction.Negative]))
        {
            return v[Direction.Positive] - dist;
        }
        else if (double.IsInfinity(v[Direction.Positive]))
        {
            return v[Direction.Negative] + dist;
        }
        else
        {
            return v.Center;
        }
    }

    /* Set stem's shorten property if unset.

    TODO:
    take some y-position (chord/beam/nearest?) into account
    scmify forced-fraction

    This is done in beam because the shorten has to be uniform over the
    entire beam.
    */
    private static void SetMinimumDy(Grob me, ref double dy)
    {
        if (dy != 0.0)
        {
            /*
              If dy is smaller than the smallest quant, we
              get absurd direction-sign penalties.
            */

            double ss = StaffSymbolReferencer.StaffSpace(me);
            double beamThickness = Beam.GetBeamThickness(me) / ss;
            double slt = StaffSymbolReferencer.LineThickness(me) / ss;
            double sit = (beamThickness - slt) / 2;
            double inter = 0.5;
            double hang = 1.0 - ((beamThickness - slt) / 2);

            dy = Math.Sign(dy)
                 * Math.Max(Math.Abs(dy), Math.Min(Math.Min(sit, inter), hang));
        }
    }

    private void NoVisibleStemPositions()
    {
        if (_headPositions.Count == 0)
        {
            _unquantedY = new DrulArray<double>(0.0, 0.0);
            return;
        }

        Interval headPositions = Interval.Empty;
        Slice multiplicity = Slice.Empty;
        for (int i = 0; i < _headPositions.Count; i++)
        {
            headPositions.Unite(_headPositions[i]);
            multiplicity.Unite(_beamMultiplicity[i]);
        }

        Direction dir = Stem.GetGrobDirection(_beam);

        if (!dir.IsNonZero)
        {
            Warn.ProgrammingError("The beam should have a direction by now.");
        }

        double y = (headPositions.LinearCombination(dir.Value) * 0.5 * _staffSpace)
                   + (dir.Value * _beamTranslation * (multiplicity.Length + 1));

        _unquantedY = new DrulArray<double>(y, y);
    }

    private int FirstNormalIndex()
    {
        for (int i = 0; i < _isNormal.Count; i++)
        {
            if (_isNormal[i])
            {
                return i;
            }
        }

        _beam.ProgrammingError("No normal stems, but asking for first normal stem index.");
        return 0;
    }

    private int LastNormalIndex()
    {
        for (int i = _isNormal.Count; i-- > 0;)
        {
            if (_isNormal[i])
            {
                return i;
            }
        }

        _beam.ProgrammingError("No normal stems, but asking for first normal stem index.");
        return 0;
    }

    private void LeastSquaresPositions()
    {
        if (_normalStemCount == 0)
        {
            NoVisibleStemPositions();
            return;
        }

        if (_stemInfos.Count < 1)
        {
            return;
        }

        int fnx = FirstNormalIndex();
        int lnx = LastNormalIndex();

        DrulArray<double> ideal = new DrulArray<double>(
            _stemInfos[fnx].IdealY + _stemYPositions[fnx],
            _stemInfos[lnx].IdealY + _stemYPositions[lnx]);

        double y = 0;
        double slope = 0;
        double dy = 0;
        double ldy = 0.0;
        if (Delta(ideal) == 0.0)
        {
            DrulArray<double> chord = new DrulArray<double>(
                _chordStartY[0], _chordStartY[_chordStartY.Count - 1]);

            /* Simple beams (2 stems) on middle line should be allowed to be
               slightly sloped.

               However, if both stems reach middle line,
               ideal[LEFT] == ideal[RIGHT] and delta (ideal) == 0.

               For that case, we apply artificial slope */
            if (ideal[Direction.Negative] == 0.0 && Delta(chord) != 0.0
                && _stemInfos.Count == 2)
            {
                Direction d = new Direction(Delta(chord));
                _unquantedY[d] = Beam.GetBeamThickness(_beam) / 2;
                _unquantedY[-d] = -_unquantedY[d];
            }
            else
            {
                _unquantedY = ideal;
            }

            ldy = _unquantedY[Direction.Positive] - _unquantedY[Direction.Negative];
        }
        else
        {
            List<Offset> ideals = new List<Offset>();
            for (int i = 0; i < _stemInfos.Count; i++)
            {
                if (_isNormal[i])
                {
                    ideals.Add(new Offset(
                        _stemXPositions[i], _stemInfos[i].IdealY + _stemYPositions[i]));
                }
            }

            LeastSquares.MinimiseLeastSquares(out slope, out y, ideals);

            dy = slope * _xSpan;

            SetMinimumDy(_beam, ref dy);

            ldy = dy;
            _unquantedY = new DrulArray<double>(y, y + dy);
        }

        _musicalDy = ldy;
        _beam.SetProperty(Symbol.Intern("least-squares-dy"), _musicalDy);
    }

    /*
      Determine whether a beam is concave.

      A beam is concave when the middle notes get closer to the
      beam than the left and right edge notes.

      This is determined in two ways: by looking at the positions of the
      middle notes, or by looking at the deviation of the inside notes
      compared to the line connecting first and last.

      The tricky thing is what to do with beams with chords. There are no
      real guidelines in this case.
    */
    private static bool IsConcaveSingleNotes(IReadOnlyList<int> positions, Direction beamDir)
    {
        Interval covering = Interval.Empty;
        covering.AddPoint(positions[0]);
        covering.AddPoint(positions[positions.Count - 1]);

        bool above = false;
        bool below = false;
        bool concave = false;

        /*
          notes above and below the interval covered by 1st and last note.
        */
        for (int i = 1; i + 1 < positions.Count; i++)
        {
            above = above || (positions[i] > covering[Direction.Positive]);
            below = below || (positions[i] < covering[Direction.Negative]);
        }

        concave = concave || (above && below);

        /*
          A note as close or closer to the beam than begin and end, but the
          note is reached in the opposite direction as the last-first dy
        */
        int dy = positions[positions.Count - 1] - positions[0];
        int closest = Math.Max(
            beamDir.Value * positions[positions.Count - 1], beamDir.Value * positions[0]);
        for (int i = 2; !concave && i + 1 < positions.Count; i++)
        {
            int innerDy = positions[i] - positions[i - 1];
            if (Math.Sign(innerDy) != Math.Sign(dy)
                && (beamDir.Value * positions[i] >= closest
                    || beamDir.Value * positions[i - 1] >= closest))
            {
                concave = true;
            }
        }

        bool allCloser = true;
        for (int i = 1; allCloser && i + 1 < positions.Count; i++)
        {
            allCloser = allCloser && (beamDir.Value * positions[i] > closest);
        }

        concave = concave || allCloser;
        return concave;
    }

    private static double CalcPositionsConcaveness(
        IReadOnlyList<int> positions, Direction beamDir)
    {
        double dy = positions[positions.Count - 1] - positions[0];
        double slope = dy / (positions.Count - 1);
        double concaveness = 0.0;
        for (int i = 1; i + 1 < positions.Count; i++)
        {
            double lineY = (slope * i) + positions[0];
            concaveness += Math.Max(beamDir.Value * (positions[i] - lineY), 0.0);
        }

        concaveness /= positions.Count;

        /*
          Normalize. For dy = 0, the slope ends up as 0 anyway, so the
          scaling of concaveness doesn't matter much.
        */
        if (dy != 0.0)
        {
            concaveness /= Math.Abs(dy);
        }

        return concaveness;
    }

    private double CalcConcaveness()
    {
        object conc = _beam.GetProperty(Symbol.Intern("concaveness"));
        if (SchemeConvert.IsNumber(conc))
        {
            return Stem.ToDouble(conc, 0.0);
        }

        if (_isKnee || _isXStaff)
        {
            return 0.0;
        }

        Direction beamDir = Direction.Center;
        for (int i = _isNormal.Count; i-- > 0;)
        {
            if (_isNormal[i] && _stemInfos[i].Dir.IsNonZero)
            {
                beamDir = _stemInfos[i].Dir;
            }
        }

        if (_normalStemCount <= 2)
        {
            return 0.0;
        }

        List<int> closePositions = new List<int>();
        List<int> farPositions = new List<int>();
        for (int i = 0; i < _isNormal.Count; i++)
        {
            if (_isNormal[i])
            {
                /*
                  For chords, we take the note head that is closest to the beam.

                  Hmmm.. wait, for the beams in the last measure of morgenlied,
                  this doesn't look so good. Let's try the heads farthest from
                  the beam.
                */
                int closePos = (int)Math.Round(_headPositions[i][beamDir],
                                               MidpointRounding.ToEven);
                closePositions.Add(closePos);
                int farPos = (int)Math.Round(_headPositions[i][-beamDir],
                                             MidpointRounding.ToEven);
                farPositions.Add(farPos);
            }
        }

        double concaveness;

        if (IsConcaveSingleNotes(
                beamDir == Direction.Positive ? closePositions : farPositions, beamDir))
        {
            concaveness = 10000;
        }
        else
        {
            concaveness = (CalcPositionsConcaveness(farPositions, beamDir)
                           + CalcPositionsConcaveness(closePositions, beamDir))
                          / 2;
        }

        return concaveness;
    }

    private void SlopeDamping()
    {
        if (_normalStemCount <= 1)
        {
            return;
        }

        object s = _beam.GetProperty(Symbol.Intern("damping"));
        double damping = Stem.ToDouble(s, 0.0);
        double concaveness = CalcConcaveness();
        if ((concaveness >= 10000) || (damping >= 10000))
        {
            _unquantedY[Direction.Negative] = _unquantedY[Direction.Positive];
            _musicalDy = 0;
            damping = 0;
        }

        if (damping != 0.0 && (damping + concaveness) != 0.0)
        {
            double dy = _unquantedY[Direction.Positive] - _unquantedY[Direction.Negative];

            double slope = (dy != 0.0 && _xSpan != 0.0) ? dy / _xSpan : 0;

            slope = 0.6 * Math.Tanh(slope) / (damping + concaveness);

            double dampedDy = slope * _xSpan;

            SetMinimumDy(_beam, ref dampedDy);

            _unquantedY[Direction.Negative] += (dy - dampedDy) / 2;
            _unquantedY[Direction.Positive] -= (dy - dampedDy) / 2;
        }
    }

    private void ShiftRegionToValid()
    {
        if (_normalStemCount == 0)
        {
            return;
        }

        double beamDy = _unquantedY[Direction.Positive] - _unquantedY[Direction.Negative];
        double slope = _xSpan != 0.0 ? beamDy / _xSpan : 0.0;

        /*
          Shift the positions so that we have a chance of finding good
          quants (i.e. no short stem failures.)
        */
        Interval feasibleLeftPoint = Interval.Empty;
        feasibleLeftPoint.SetFull();

        for (int i = 0; i < _stemInfos.Count; i++)
        {
            // TODO - check for invisible here...
            double leftY = _stemInfos[i].ShortestY - (slope * _stemXPositions[i]);

            /*
              left_y is now relative to the stem S. We want relative to
              ourselves, so translate:
            */
            leftY += _stemYPositions[i];
            Interval flp = Interval.Empty;
            flp.SetFull();
            flp[-_stemInfos[i].Dir] = leftY;

            feasibleLeftPoint.Intersect(flp);
        }

        /*
          We only update these for objects that are too large for quanting
          to find a workaround.  Typically, these are notes with
          stems, and timesig/keysig/clef, which take out the entire area
          inside the staff as feasible.

          The code below disregards the thickness and multiplicity of the
          beam.  This should not be a problem, as the beam quanting will
          take care of computing the impact those exactly.
        */
        double minYSize = 2.0;

        // A list of intervals into which beams may not fall
        List<Interval> forbiddenIntervals = new List<Interval>();

        for (int i = 0; i < _collisions.Count; i++)
        {
            if (_collisions[i].X < 0 || _collisions[i].X > _xSpan)
            {
                continue;
            }

            if (_collisions[i].Y.Length < minYSize)
            {
                continue;
            }

            double dy = slope * _collisions[i].X;

            Interval disallowed = Interval.Empty;
            foreach (Direction yd in BothDirections)
            {
                double leftY = _collisions[i].Y[yd] - dy;
                disallowed[yd] = leftY;
            }

            forbiddenIntervals.Add(disallowed);
        }

        forbiddenIntervals.Sort((a, b) => Interval.LeftLess(a, b) ? -1
                                          : (Interval.LeftLess(b, a) ? 1 : 0));
        double beamLeftY = _unquantedY[Direction.Negative];
        Interval feasibleBeamPlacements = new Interval(beamLeftY, beamLeftY);

        IntervalMinefield minefield = new IntervalMinefield(feasibleBeamPlacements, 0.0);
        for (int i = 0; i < forbiddenIntervals.Count; i++)
        {
            minefield.AddForbiddenInterval(forbiddenIntervals[i]);
        }

        minefield.Solve();
        feasibleBeamPlacements = minefield.FeasiblePlacements();

        // if the beam placement falls out of the feasible region, we push it
        // to infinity so that it can never be a feasible candidate below
        foreach (Direction d in BothDirections)
        {
            if (!feasibleLeftPoint.Contains(feasibleBeamPlacements[d]))
            {
                feasibleBeamPlacements[d] = d.Value * Interval.MaxSentinel;
            }
        }

        if ((feasibleBeamPlacements[Direction.Positive] == Interval.MaxSentinel
             && feasibleBeamPlacements[Direction.Negative] == Interval.MinSentinel)
            && !feasibleLeftPoint.IsEmpty)
        {
            // We are somewhat screwed: we have a collision, but at least
            // there is a way to satisfy stem length constraints.
            beamLeftY = PointInInterval(feasibleLeftPoint, 2.0);
        }
        else if (!feasibleLeftPoint.IsEmpty)
        {
            // Only one of them offers is feasible solution. Pick that one.
            if (Math.Abs(beamLeftY - feasibleBeamPlacements[Direction.Negative])
                > Math.Abs(beamLeftY - feasibleBeamPlacements[Direction.Positive]))
            {
                beamLeftY = feasibleBeamPlacements[Direction.Positive];
            }
            else
            {
                beamLeftY = feasibleBeamPlacements[Direction.Negative];
            }
        }
        else
        {
            // We are completely screwed.
            _beam.Warning(
                "no viable initial configuration found: may not find good beam slope");
        }

        _unquantedY = new DrulArray<double>(beamLeftY, beamLeftY + beamDy);
    }

    private void GenerateQuants(List<BeamConfiguration> scores)
    {
        int regionSize = (int)_parameters.RegionSize;

        // Knees and collisions are harder, lets try some more possibilities
        if (_isKnee)
        {
            regionSize += 2;
        }

        if (_collisions.Count != 0)
        {
            regionSize += 2;
        }

        double straddle = 0.0;
        double sit = (_beamThickness - _lineThickness) / 2;
        double inter = 0.5;
        double hang = 1.0 - ((_beamThickness - _lineThickness) / 2);
        double[] baseQuants = { straddle, sit, inter, hang };
        int numBaseQuants = baseQuants.Length;

        /* for normal-sized beams, in case of more than 4 beams, the outer beam
           used for generating quants will never interfere with staff lines, but
           prevent the inside-staff beams from being neatly positioned.
           A correctional grid_shift has to be applied to compensate. */
        double gridShift = 0.0;

        /* grid shift only makes sense for widened normal-sized beams: */
        if (!_isKnee && _maxBeamCount > 4 && _lengthFraction == 1.0)
        {
            gridShift = (_maxBeamCount - 4) * (1.0 - _beamTranslation);
        }

        /*
          Asymetry ? should run to <= region_size ?
        */
        List<double> unshiftedQuants = new List<double>();
        for (int i = -regionSize; i < regionSize; i++)
        {
            for (int j = 0; j < numBaseQuants; j++)
            {
                unshiftedQuants.Add(i + baseQuants[j]);
            }
        }

        for (int i = 0; i < unshiftedQuants.Count; i++)
        {
            for (int j = 0; j < unshiftedQuants.Count; j++)
            {
                Interval corr = new Interval(0.0, 0.0);
                if (gridShift != 0.0)
                {
                    foreach (Direction d in BothDirections)
                    {
                        /* apply grid shift if quant outside 5-line staff: */
                        if ((_unquantedY[d] + unshiftedQuants[i]) * _edgeDirs[d].Value > 2.5)
                        {
                            corr[d] = gridShift * _edgeDirs[d].Value;
                        }
                    }
                }

                BeamConfiguration c = BeamConfiguration.NewConfig(
                    _unquantedY,
                    new DrulArray<double>(
                        unshiftedQuants[i] - corr[Direction.Negative],
                        unshiftedQuants[j] - corr[Direction.Positive]));

                foreach (Direction d in BothDirections)
                {
                    if (!_quantRange[d].Contains(c.Y[d]))
                    {
                        c = null;
                        break;
                    }
                }

                if (c != null)
                {
                    scores.Add(c);
                }
            }
        }
    }

    private void OneScorer(BeamConfiguration config)
    {
        switch ((Scorers)config.NextScorerTodo)
        {
            case Scorers.SlopeIdeal:
                ScoreSlopeIdeal(config);
                break;
            case Scorers.SlopeDirection:
                ScoreSlopeDirection(config);
                break;
            case Scorers.SlopeMusical:
                ScoreSlopeMusical(config);
                break;
            case Scorers.Forbidden:
                ScoreForbiddenQuants(config);
                break;
            case Scorers.StemLengths:
                ScoreStemLengths(config);
                break;
            case Scorers.Collisions:
                ScoreCollisions(config);
                break;
            case Scorers.HorizontalInter:
                ScoreHorizontalInterQuants(config);
                break;

            case Scorers.NumScorers:
            case Scorers.OriginalDistance:
            default:
                Warn.ProgrammingError("beam scorer reached an impossible state");
                break;
        }

        config.NextScorerTodo++;
    }

    private BeamConfiguration ForceScore(
        object inspectQuants, IReadOnlyList<BeamConfiguration> configs)
    {
        DrulArray<double> ins = SchemeConvert.ToDrulDouble(
            inspectQuants, new DrulArray<double>(0.0, 0.0));
        double mindist = 1e6;
        BeamConfiguration best = null;
        for (int i = 0; i < configs.Count; i++)
        {
            double d = Math.Abs(configs[i].Y[Direction.Negative] - ins[Direction.Negative])
                       + Math.Abs(configs[i].Y[Direction.Positive] - ins[Direction.Positive]);
            if (d < mindist)
            {
                best = configs[i];
                mindist = d;
            }
        }

        if (mindist > 1e5)
        {
            Warn.ProgrammingError("cannot find quant");
        }

        while (!best.Done())
        {
            OneScorer(best);
        }

        return best;
    }

    /// <summary>Searches for the best pair of beam end positions.</summary>
    /// <returns>The quantized positions.</returns>
    internal DrulArray<double> Solve()
    {
        List<BeamConfiguration> configs = new List<BeamConfiguration>();
        GenerateQuants(configs);

        if (configs.Count == 0)
        {
            Warn.ProgrammingError("No viable beam quanting found.  Using unquanted y value.");
            return _unquantedY;
        }

        if (SchemeUtilities.ToBool(_beam.GetProperty(Symbol.Intern("skip-quanting"))))
        {
            return _unquantedY;
        }

        BeamConfiguration best;

        bool debug = _beam.Layout != null
                     && SchemeUtilities.ToBool(
                         _beam.Layout.LookupVariable(Symbol.Intern("debug-beam-scoring")));
        object inspectQuants = _beam.GetProperty(Symbol.Intern("inspect-quants"));
        if (inspectQuants is Pair)
        {
            debug = true;
            best = ForceScore(inspectQuants, configs);
        }
        else
        {
            // Beam_configuration_less: "Invert" — so the heap's top is the SMALLEST demerits.
            ConfigurationHeap<BeamConfiguration> queue
                = new ConfigurationHeap<BeamConfiguration>((l, r) => l.Demerits > r.Demerits);
            for (int i = 0; i < configs.Count; i++)
            {
                queue.Push(configs[i]);
            }

            /*
              TODO

              It would be neat if we generated new configurations on the
              fly, depending on the best complete score so far, eg.

              if (best->done()) {
                if (best->demerits < sqrt(queue.size())
                  break;
                while (best->demerits > sqrt(queue.size()) {
                  generate and insert new configuration
                }
              }

              that would allow us to do away with region_size altogether.
            */
            while (true)
            {
                best = queue.Top();
                if (best.Done())
                {
                    break;
                }

                queue.Pop();
                OneScorer(best);
                queue.Push(best);
            }
        }

        DrulArray<double> finalPositions = best.Y;

        if (debug)
        {
            // debug quanting
            int completed = 0;
            for (int i = 0; i < configs.Count; i++)
            {
                if (configs[i].Done())
                {
                    completed++;
                }
            }

            string card = best.ScoreCard
                          + " c" + completed.ToString(CultureInfo.InvariantCulture)
                          + "/" + configs.Count.ToString(CultureInfo.InvariantCulture);
            _beam.SetProperty(Symbol.Intern("annotation"), card);
        }

        configs.Clear();
        if (_alignBrokenIntos)
        {
            Interval normalizedEndpoints = ReadInterval(
                _beam.GetProperty(Symbol.Intern("normalized-endpoints")), new Interval(0, 1));
            double yLength
                = finalPositions[Direction.Positive] - finalPositions[Direction.Negative];

            finalPositions[Direction.Negative]
                += normalizedEndpoints[Direction.Negative] * yLength;
            finalPositions[Direction.Positive]
                -= (1 - normalizedEndpoints[Direction.Positive]) * yLength;
        }

        return finalPositions;
    }

    private void ScoreStemLengths(BeamConfiguration config)
    {
        double limitPenalty = _parameters.StemLengthLimitPenalty;
        DrulArray<double> score = new DrulArray<double>(0.0, 0.0);
        DrulArray<int> count = new DrulArray<int>(0, 0);

        for (int i = 0; i < _stemXPositions.Count; i++)
        {
            if (!_isNormal[i])
            {
                continue;
            }

            double x = _stemXPositions[i];
            double dx = _xSpan;
            double beamY = dx != 0.0
                ? (config.Y[Direction.Positive] * x / dx)
                  + (config.Y[Direction.Negative] * (_xSpan - x) / dx)
                : (config.Y[Direction.Positive] + config.Y[Direction.Negative]) / 2;
            double currentY = beamY + _baseLengths[i];
            double lengthPen = _parameters.StemLengthDemeritFactor;

            StemInfo info = _stemInfos[i];
            Direction d = info.Dir;

            score[d] += limitPenalty * Math.Max(0.0, d.Value * (info.ShortestY - currentY));

            double idealDiff = d.Value * (currentY - info.IdealY);
            double idealScore = ShrinkExtraWeight(idealDiff, 1.5);

            /* We introduce a power, to make the scoring strictly
               convex. Otherwise a symmetric knee beam (up/down/up/down)
               does not have an optimum in the middle. */
            if (_isKnee)
            {
                idealScore = Math.Pow(idealScore, 1.1);
            }

            score[d] += lengthPen * idealScore;
            count[d]++;
        }

        /* Divide by number of stems, to make the measure scale-free. */
        foreach (Direction d in BothDirections)
        {
            score[d] /= Math.Max(count[d], 1);
        }

        /*
          sometimes, two perfectly symmetric kneed beams will have the same score
          and can either be quanted up or down.

          we choose the quanting in the direction of the slope so that the first stem
          always seems longer, reaching to the second, rather than squashed.
        */
        if (_isKnee && (count[Direction.Negative] == count[Direction.Positive])
            && (count[Direction.Negative] == 1))
        {
            Direction d = new Direction(Delta(_unquantedY));
            if (d.IsNonZero)
            {
                score[d] += (score[d] < 1.0) ? 0.01 : 0.0;
            }
        }

        config.Add(score[Direction.Negative] + score[Direction.Positive], "L");
    }

    private void ScoreSlopeDirection(BeamConfiguration config)
    {
        double dy = Delta(config.Y);
        double dampedDy = Delta(_unquantedY);
        double dem = 0.0;

        /*
          DAMPING_DIRECTION_PENALTY is a very harsh measure, while for
          complex beaming patterns, horizontal is often a good choice.

          TODO: find a way to incorporate the complexity of the beam in this
          penalty.
        */
        if (Math.Sign(dampedDy) != Math.Sign(dy))
        {
            if (dy == 0.0)
            {
                if (Math.Abs(dampedDy / _xSpan) > _parameters.RoundToZeroSlope)
                {
                    dem += _parameters.DampingDirectionPenalty;
                }
                else
                {
                    dem += _parameters.HintDirectionPenalty;
                }
            }
            else
            {
                dem += _parameters.DampingDirectionPenalty;
            }
        }

        config.Add(dem, "Sd");
    }

    // Score for going against the direction of the musical pattern
    private void ScoreSlopeMusical(BeamConfiguration config)
    {
        double dy = Delta(config.Y);
        double dem = _parameters.MusicalDirectionFactor
                     * Math.Max(0.0, Math.Abs(dy) - Math.Abs(_musicalDy));
        config.Add(dem, "Sm");
    }

    // Score deviation from calculated ideal slope.
    private void ScoreSlopeIdeal(BeamConfiguration config)
    {
        double dy = Delta(config.Y);
        double dampedDy = Delta(_unquantedY);
        double dem = 0.0;

        double slopePenalty = _parameters.IdealSlopeFactor;

        /* Xstaff beams tend to use extreme slopes to get short stems. We
           put in a penalty here. */
        if (_isXStaff)
        {
            slopePenalty *= 10;
        }

        /* Huh, why would a too steep beam be better than a too flat one ? */
        dem += ShrinkExtraWeight(Math.Abs(dampedDy) - Math.Abs(dy), 1.5) * slopePenalty;

        config.Add(dem, "Si");
    }

    // TODO - there is some overlap with forbidden quants, but for
    // horizontal beams, it is much more serious to have stafflines
    // appearing in the wrong place, so we have a separate scorer.
    private void ScoreHorizontalInterQuants(BeamConfiguration config)
    {
        if (Delta(config.Y) == 0.0
            && Math.Abs(config.Y[Direction.Negative]) < _staffRadius * _staffSpace)
        {
            double yshift = config.Y[Direction.Negative] - (0.5 * _staffSpace);
            if (Math.Abs(RoundHalfwayUp(yshift) - yshift) < 0.01 * _staffSpace)
            {
                config.Add(_parameters.HorizontalInterQuantPenalty, "H");
            }
        }
    }

    /*
      TODO: The fixed value SECONDARY_BEAM_DEMERIT is probably flawed:
      because for 32nd and 64th beams the forbidden quants are relatively
      more important than stem lengths.
    */
    private void ScoreForbiddenQuants(BeamConfiguration config)
    {
        double dy = Delta(config.Y);

        double extraDemerit
            = _parameters.SecondaryBeamDemerit
              / Math.Max(_edgeBeamCounts[Direction.Negative],
                         _edgeBeamCounts[Direction.Positive]);

        double dem = 0.0;
        double eps = _parameters.BeamEps;

        foreach (Direction d in BothDirections)
        {
            for (int j = 1; j <= _edgeBeamCounts[d]; j++)
            {
                Direction stemDir = _edgeDirs[d];

                /*
                  The fudge_factor is to provide a little leniency for
                  borderline cases. If we do 2.0, then the upper outer line
                  will be in the gap of the (2, sit) quant, leading to a
                  false demerit. By increasing the fudge factor to 2.2, we
                  fix this case.
                */
                double fudgeFactor = 2.2;
                double gap1 = config.Y[d]
                              - (stemDir.Value
                                 * (((j - 1) * _beamTranslation) + (_beamThickness / 2)
                                    - (_lineThickness / fudgeFactor)));
                double gap2 = config.Y[d]
                              - (stemDir.Value
                                 * ((j * _beamTranslation) - (_beamThickness / 2)
                                    + (_lineThickness / fudgeFactor)));

                Interval gap = Interval.Empty;
                gap.AddPoint(gap1);
                gap.AddPoint(gap2);

                for (double k = -_staffRadius; k <= _staffRadius + eps; k += 1.0)
                {
                    if (gap.Contains(k))
                    {
                        double dist = Math.Min(
                            Math.Abs(gap[Direction.Positive] - k),
                            Math.Abs(gap[Direction.Negative] - k));

                        /*
                          this parameter is tuned to grace-stem-length.ly
                          retuned from 0.40 to 0.39 by MS because of slight increases
                          in gap.length () resulting from measuring beams at real ends
                          instead of from the middle of stems.

                          TODO:
                          This function needs better comments so we know what is forbidden
                          and why.
                        */
                        double fixedDemerit = 0.39;

                        dem += extraDemerit
                               * (fixedDemerit
                                  + ((1 - fixedDemerit) * (dist / gap.Length) * 2));
                    }
                }
            }
        }

        config.Add(dem, "Fl");
        dem = 0.0;
        if (Math.Max(_edgeBeamCounts[Direction.Negative],
                     _edgeBeamCounts[Direction.Positive]) >= 2)
        {
            double straddle = 0.0;
            double sit = (_beamThickness - _lineThickness) / 2;
            double inter = 0.5;
            double hang = 1.0 - ((_beamThickness - _lineThickness) / 2);

            foreach (Direction d in BothDirections)
            {
                if (_edgeBeamCounts[d] >= 2
                    && Math.Abs(config.Y[d] - (_edgeDirs[d].Value * _beamTranslation))
                       < _staffRadius + inter)
                {
                    // TODO up/down symmetry.
                    if (_edgeDirs[d] == Direction.Positive && dy <= eps
                        && Math.Abs(MyModf(config.Y[d]) - sit) < eps)
                    {
                        dem += extraDemerit;
                    }

                    if (_edgeDirs[d] == Direction.Negative && dy >= eps
                        && Math.Abs(MyModf(config.Y[d]) - hang) < eps)
                    {
                        dem += extraDemerit;
                    }
                }

                if (_edgeBeamCounts[d] >= 3
                    && Math.Abs(config.Y[d] - (2 * _edgeDirs[d].Value * _beamTranslation))
                       < _staffRadius + inter)
                {
                    // TODO up/down symmetry.
                    if (_edgeDirs[d] == Direction.Positive && dy <= eps
                        && Math.Abs(MyModf(config.Y[d]) - straddle) < eps)
                    {
                        dem += extraDemerit;
                    }

                    if (_edgeDirs[d] == Direction.Negative && dy >= eps
                        && Math.Abs(MyModf(config.Y[d]) - straddle) < eps)
                    {
                        dem += extraDemerit;
                    }
                }
            }
        }

        config.Add(dem, "Fs");
    }

    private void ScoreCollisions(BeamConfiguration config)
    {
        double demerits = 0.0;
        for (int i = 0; i < _collisions.Count; i++)
        {
            Interval collisionY = _collisions[i].Y;
            double x = _collisions[i].X;

            double centerBeamY = YAt(x, config);
            Interval beamY = _collisions[i].BeamY;
            beamY.Translate(centerBeamY);

            double dist;
            if (!Interval.Intersection(beamY, collisionY).IsEmpty)
            {
                dist = 0.0;
            }
            else
            {
                dist = Math.Min(
                    beamY.Distance(collisionY[Direction.Negative]),
                    beamY.Distance(collisionY[Direction.Positive]));
            }

            double scaleFree
                = Math.Max(_parameters.CollisionPadding - dist, 0.0)
                  / _parameters.CollisionPadding;
            double collisionDemerit = _collisions[i].BasePenalty
                                      * Math.Pow(scaleFree, 3)
                                      * _parameters.CollisionPenalty;

            if (collisionDemerit > 0)
            {
                demerits += collisionDemerit;
            }
        }

        config.Add(demerits, "C");
    }

    private static Interval ReadInterval(object value, Interval fallback)
    {
        DrulArray<double> drul = SchemeConvert.ToDrulDouble(
            value, new DrulArray<double>(fallback.Left, fallback.Right));
        return new Interval(drul[Direction.Negative], drul[Direction.Positive]);
    }

    // EPG11/EPG12 (2026-08-08): the libstdc++ heap replica this file used to define
    // privately now lives in Objects/ConfigurationHeap.cs, because Slur_score_state runs the
    // identical pattern with the identical inverting comparator. The algorithm is unchanged
    // and beams still break equal-demerit ties exactly as before; only its home moved.
}
