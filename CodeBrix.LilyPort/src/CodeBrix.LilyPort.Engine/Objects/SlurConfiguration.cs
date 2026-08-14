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
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/slur-configuration.cc, lily/include/slur-configuration.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.
//
// SCORER — standing rule 2 applies to this whole file.
//
//   - upstream passes Bezier BY VALUE into avoid_staff_line and fit_factor and mutates the
//     local copy; the port's Bezier is a CLASS, so both take an explicit Copy() first.
//     Without it, fit_factor's rotate/scale would silently deform the caller's curve —
//     which is the curve that gets drawn.

/// <summary>One candidate position for a slur, and the demerits it has earned.</summary>
public sealed class SlurConfiguration
{
    /// <summary>The scorers, ordered by increasing computational cost.</summary>
    public enum SlurScorers
    {
        /// <summary>Charged when the configuration is made.</summary>
        InitialScore,

        /// <summary>How far the slur departs from the slope the music implies.</summary>
        Slope,

        /// <summary>How far the endpoints sit from their base attachments.</summary>
        Edges,

        /// <summary>Collisions with grobs other than heads and stems.</summary>
        ExtraEncompass,

        /// <summary>Collisions with the note heads and stems the slur covers.</summary>
        Encompass,

        /// <summary>The number of scorers.</summary>
        NumScorers,
    }

    private double _score;
    private string _scoreCard = string.Empty;

    /// <summary>Where each end attaches.</summary>
    public DrulArray<Offset> Attachment;

    /// <summary>The curve itself.</summary>
    public Bezier Curve;

    /// <summary>How high the curve rises above the line between its endpoints.</summary>
    public double Height;

    /// <summary>This candidate's position in the enumeration.</summary>
    public int Index;

    /// <summary>Which scorer runs next.</summary>
    public int NextScorerTodo;

    private static readonly Symbol EccentricitySymbol = Symbol.Intern("eccentricity");
    private static readonly Symbol AroundSymbol = Symbol.Intern("around");
    private static readonly Symbol InsideSymbol = Symbol.Intern("inside");
    private static readonly Symbol ControlPointsSymbol = Symbol.Intern("control-points");
    private static readonly Symbol TieInterface = Symbol.Intern("tie-interface");

    private static readonly Direction[] Both = { Direction.Negative, Direction.Positive };
    private static readonly Axis[] BothAxes = { Axis.X, Axis.Y };

    /// <summary>Initializes an unscored configuration.</summary>
    public SlurConfiguration()
    {
        _score = 0.0;
        Index = -1;
        Curve = new Bezier();
    }

    /// <summary>Gets the demerits accumulated so far.</summary>
    /// <returns>The score.</returns>
    public double Score() => _score;

    /// <summary>Gets the human-readable breakdown of the score.</summary>
    /// <returns>The score card.</returns>
    public string Card() => _scoreCard;

    /// <summary>Gets a value indicating whether every scorer has run.</summary>
    /// <returns><see langword="true"/> when scoring is complete.</returns>
    public bool Done() => NextScorerTodo >= (int)SlurScorers.NumScorers;

    /// <summary>Makes a candidate for a pair of endpoints.</summary>
    /// <param name="offs">The endpoints.</param>
    /// <param name="idx">This candidate's position in the enumeration.</param>
    /// <returns>The candidate.</returns>
    public static SlurConfiguration NewConfig(DrulArray<Offset> offs, int idx)
    {
        SlurConfiguration conf = new SlurConfiguration();
        conf.Attachment = offs;
        conf.Index = idx;
        conf.NextScorerTodo = (int)SlurScorers.InitialScore + 1;
        return conf;
    }

    /// <summary>Adds demerits, recording the reason on the score card.</summary>
    /// <param name="s">The demerits.</param>
    /// <param name="desc">Why they were added.</param>
    public void AddScore(double s, string desc)
    {
        if (s < 0.0)
        {
            Warn.ProgrammingError("Negative demerits found for slur.  Ignoring");
            s = 0.0;
        }

        if (s != 0.0)
        {
            if (_scoreCard.Length > 0)
            {
                _scoreCard += ", ";
            }

            _scoreCard += desc + "=" + s.ToString("F2", CultureInfo.InvariantCulture);
            _score += s;
        }
    }

    /// <summary>Runs the next scorer that has not yet run.</summary>
    /// <param name="state">The problem this candidate belongs to.</param>
    public void RunNextScorer(SlurScoreState state)
    {
        switch ((SlurScorers)NextScorerTodo)
        {
            case SlurScorers.ExtraEncompass:
                ScoreExtraEncompass(state);
                break;
            case SlurScorers.Slope:
                ScoreSlopes(state);
                break;
            case SlurScorers.Edges:
                ScoreEdges(state);
                break;
            case SlurScorers.Encompass:
                ScoreEncompass(state);
                break;
            default:
                throw new InvalidOperationException(
                    "slur scorer index out of range: " + NextScorerTodo);
        }

        NextScorerTodo++;
    }

    /// <summary>Shapes this candidate's curve so it clears the points it must avoid.</summary>
    /// <param name="state">The problem this candidate belongs to.</param>
    /// <param name="r0">The initial height-to-width ratio.</param>
    /// <param name="hInf">The height the curve rises to asymptotically.</param>
    /// <param name="avoid">The points to stay clear of.</param>
    public void GenerateCurve(
        SlurScoreState state, double r0, double hInf, List<Offset> avoid)
    {
        Offset dz = Attachment[Direction.Positive] - Attachment[Direction.Negative];
        Offset dzUnit = dz;
        dzUnit *= 1 / dz.Length;
        Offset dzPerp = Offset.ComplexMultiply(dzUnit, new Offset(0, 1));

        BezierBow.GetSlurIndentHeight(out double indent, out double height, dz.Length, hInf, r0);

        double len = dz.Length;

        /* This condition,

        len^2 > 4h^2 +  3 (i + 1/3len)^2  - 1/3 len^2

        is equivalent to:

        |bez' (0)| < | bez' (.5)|

        when (control2 - control1) has the same direction as
        (control3 - control0).  */

        double maxIndent = len / 3.1;
        indent = Math.Min(indent, maxIndent);

        double a1 = len * len / 3.0;
        double a2 = 0.75 * (indent + (len / 3.0)) * (indent + (len / 3.0));
        double maxH = a1 - a2;

        if (maxH < 0)
        {
            Warn.ProgrammingError("slur indent too small");
            maxH = len / 3.0;
        }
        else
        {
            maxH = Math.Sqrt(maxH);
        }

        double eccentricity = ReadReal(state.Slur.GetProperty(EccentricitySymbol), 0);

        double x1 = eccentricity + indent;
        double x2 = eccentricity - indent;

        Bezier curve = new Bezier();
        curve[0] = Attachment[Direction.Negative];
        curve[1] = Attachment[Direction.Negative]
                   + (dzPerp * height * (int)state.Dir) + (dzUnit * x1);
        curve[2] = Attachment[Direction.Positive]
                   + (dzPerp * height * (int)state.Dir) + (dzUnit * x2);
        curve[3] = Attachment[Direction.Positive];

        double ff = FitFactor(
            dzUnit, dzPerp, state.Parameters.CloseToEdgeLength, curve, state.Dir, avoid);

        height = Math.Max(height, Math.Min(height * ff, maxH));

        curve[0] = Attachment[Direction.Negative];
        curve[1] = Attachment[Direction.Negative]
                   + (dzPerp * height * (int)state.Dir) + (dzUnit * x1);
        curve[2] = Attachment[Direction.Positive]
                   + (dzPerp * height * (int)state.Dir) + (dzUnit * x2);
        curve[3] = Attachment[Direction.Positive];

        Curve = AvoidStaffLine(state, curve);
        Height = height;
    }

    /// <summary>
    /// Nudges a curve that would run along a staff line far enough off it to stay visible.
    /// </summary>
    /// <param name="state">The problem the curve belongs to.</param>
    /// <param name="bez">The curve.</param>
    /// <returns>The nudged curve.</returns>
    public static Bezier AvoidStaffLine(SlurScoreState state, Bezier bez)
    {
        // upstream takes Bezier by value; the port's is a class, so copy before mutating.
        bez = bez.Copy();

        Offset horiz = new Offset(1, 0);
        List<double> ts = bez.SolveDerivative(horiz);

        /* TODO: handle case of broken slur.  */
        if (ts.Count > 0
            && ReferenceEquals(
                state.Extremes[Direction.Negative].Staff,
                state.Extremes[Direction.Positive].Staff)
            && state.Extremes[Direction.Negative].Staff != null
            && state.Extremes[Direction.Positive].Staff != null)
        {
            double t = ts[0]; // the first (usually only) point where slur is horizontal
            double y = bez.CurvePoint(t)[Axis.Y];

            // A Bezier curve at t moves 3t-3t² as far as the middle control points
            double factor = 3.0 * t * (1.0 - t);

            Grob staff = state.Extremes[Direction.Negative].Staff;

            double p = 2 * (y - staff.RelativeCoordinate(state.Common[(int)Axis.Y], Axis.Y))
                       / state.StaffSpace;

            int roundP = (int)LibcExtension.RoundHalfwayUp(p);
            if (!StaffSymbolReferencer.OnStaffLine(staff, roundP))
            {
                roundP += p > roundP ? 1 : -1;
            }

            if (!StaffSymbolReferencer.OnStaffLine(staff, roundP))
            {
                return bez;
            }

            double distance = (p - roundP) * state.StaffSpace / 2.0;

            // Allow half the thickness of the slur at the point t, plus one basic
            // blot-diameter (half for the slur outline, half for the staff line)
            double minDistance
                = (0.5 * state.Thickness * factor) + state.LineThickness
                  + ((int)state.Dir * distance > 0.0
                      ? state.Parameters.GapToStafflineInside
                      : state.Parameters.GapToStafflineOutside);
            if (Math.Abs(distance) < minDistance)
            {
                Direction resolutionDir = distance > 0.0 ? Direction.Positive : Direction.Negative;

                double dy = (int)resolutionDir * (minDistance - Math.Abs(distance));

                // Shape the curve, moving the horizontal point by factor * dy
                bez[1] = new Offset(bez[1][Axis.X], bez[1][Axis.Y] + dy);
                bez[2] = new Offset(bez[2][Axis.X], bez[2][Axis.Y] + dy);

                // Move the entire curve by the remaining amount
                bez.Translate(new Offset(0.0, dy - (factor * dy)));
            }
        }

        return bez;
    }

    /// <summary>
    /// Returns how far the curve's height must be scaled for it to clear every point.
    /// </summary>
    /// <param name="dzUnit">The unit vector along the slur.</param>
    /// <param name="dzPerp">The unit vector across the slur.</param>
    /// <param name="closeToEdgeLength">Within this distance of an end, a point is ignored.</param>
    /// <param name="curve">The curve.</param>
    /// <param name="d">Which way the slur bends.</param>
    /// <param name="avoid">The points to stay clear of.</param>
    /// <returns>The factor.</returns>
    public static double FitFactor(
        Offset dzUnit,
        Offset dzPerp,
        double closeToEdgeLength,
        Bezier curve,
        Direction d,
        List<Offset> avoid)
    {
        double fitFactor = 0.0;

        // upstream takes Bezier by value; the port's is a class, so copy before mutating.
        curve = curve.Copy();

        Offset x0 = curve[0];
        curve.Translate(-x0);
        curve.Rotate(-dzUnit.AngleDegrees());
        curve.Scale(1, (int)d);

        Interval curveXext = Interval.Empty;
        curveXext.AddPoint(curve[0][Axis.X]);
        curveXext.AddPoint(curve[3][Axis.X]);

        for (int i = 0; i < avoid.Count; i++)
        {
            Offset z = avoid[i] - x0;
            Offset p = new Offset(
                Offset.DotProduct(z, dzUnit), (int)d * Offset.DotProduct(z, dzPerp));

            bool closeToEdge = false;
            foreach (Direction edgeDir in Both)
            {
                closeToEdge
                    = closeToEdge
                      || (-(int)edgeDir * (p[Axis.X] - curveXext[edgeDir])) < closeToEdgeLength;
            }

            if (closeToEdge)
            {
                continue;
            }

            double eps = 0.01;
            Interval pext = new Interval(-eps + p[Axis.X], eps + p[Axis.X]);
            pext.Intersect(curveXext);

            if (pext.IsEmpty || pext.Length <= 1.999 * eps)
            {
                continue;
            }

            double y = curve.GetOtherCoordinate(Axis.X, p[Axis.X]);
            if (y != 0.0)
            {
                fitFactor = Math.Max(fitFactor, p[Axis.Y] / y);
            }
        }

        return fitFactor;
    }

    private void ScoreEncompass(SlurScoreState state)
    {
        Bezier bez = Curve;
        double demerit = 0.0;

        /*
          Distances for heads that are between slur and line between
          attachment points.
        */
        List<double> convexHeadDistances = new List<double>();
        for (int j = 0; j < state.EncompassInfos.Count; j++)
        {
            double x = state.EncompassInfos[j].X;

            bool lEdge = j == 0;
            bool rEdge = j == state.EncompassInfos.Count - 1;
            bool edge = lEdge || rEdge;

            if (!(x < Attachment[Direction.Positive][Axis.X]
                  && x > Attachment[Direction.Negative][Axis.X]))
            {
                continue;
            }

            double y = bez.GetOtherCoordinate(Axis.X, x);
            if (!edge)
            {
                double headDy = y - state.EncompassInfos[j].Head;
                if ((int)state.Dir * headDy < 0)
                {
                    demerit += state.Parameters.HeadEncompassPenalty;
                    convexHeadDistances.Add(0.0);
                }
                else
                {
                    double hd = headDy != 0.0
                        ? (1 / Math.Abs(headDy)) - (1 / state.Parameters.FreeHeadDistance)
                        : state.Parameters.HeadEncompassPenalty;
                    hd = Math.Min(Math.Max(hd, 0.0), state.Parameters.HeadEncompassPenalty);

                    demerit += hd;
                }

                double lineY = Misc.LinearInterpolate(
                    x,
                    Attachment[Direction.Positive][Axis.X],
                    Attachment[Direction.Negative][Axis.X],
                    Attachment[Direction.Positive][Axis.Y],
                    Attachment[Direction.Negative][Axis.Y]);

                // upstream's condition here is the literal `1`, with the real test kept
                // beside it in a comment; the branch is therefore unconditional and stays so.
                {
                    double closest
                        = (int)state.Dir
                          * Math.Max(
                              (int)state.Dir * state.EncompassInfos[j].GetPoint(state.Dir),
                              (int)state.Dir * lineY);
                    double dd = Math.Abs(closest - y);

                    convexHeadDistances.Add(dd);
                }
            }

            if ((int)state.Dir * (y - state.EncompassInfos[j].Stem) < 0)
            {
                double stemDem = state.Parameters.StemEncompassPenalty;
                if ((lEdge && state.Dir == Direction.Positive)
                    || (rEdge && state.Dir == Direction.Negative))
                {
                    stemDem /= 5;
                }

                demerit += stemDem;
            }
        }

        AddScore(demerit, "encompass");

        int n = convexHeadDistances.Count;
        if (n != 0)
        {
            double avgDistance = 0.0;
            double minDist = double.PositiveInfinity;

            for (int j = 0; j < n; j++)
            {
                minDist = Math.Min(minDist, convexHeadDistances[j]);
                avgDistance += convexHeadDistances[j];
            }

            /*
              For slurs over 3 or 4 heads, the average distance is not a
              good normalizer.
            */
            if (n <= 2)
            {
                double fact = 1.0;
                avgDistance += Height * fact;
                ++n;
            }

            /*
              TODO: maybe it's better to use (avgdist - mindist)*factor
              as penalty.
            */
            avgDistance /= n;
            double variancePenalty = state.Parameters.HeadSlurDistanceMaxRatio;
            if (minDist > 0.0)
            {
                variancePenalty = Math.Min(
                    (avgDistance / (minDist + state.Parameters.AbsoluteClosenessMeasure)) - 1.0,
                    variancePenalty);
            }

            variancePenalty = Math.Max(variancePenalty, 0.0);
            variancePenalty *= state.Parameters.HeadSlurDistanceFactor;

            AddScore(variancePenalty, "variance");
        }
    }

    private void ScoreExtraEncompass(SlurScoreState state)
    {
        // we find forbidden attachments
        List<Offset> forbiddenAttachments = new List<Offset>();
        for (int i = 0; i < state.ExtraEncompassInfos.Count; i++)
        {
            if (state.ExtraEncompassInfos[i].Grob.HasInterface(TieInterface))
            {
                Grob t = state.ExtraEncompassInfos[i].Grob;
                Grob commonX = SidePositionInterface.GetVerticalAxisGroup(t);
                double rp = t.RelativeCoordinate(commonX, Axis.X);
                object cp = t.GetProperty(ControlPointsSymbol);

                Bezier b = new Bezier();
                for (int j = 0; j < Bezier.ControlCount; ++j)
                {
                    if (cp is Pair pair)
                    {
                        b[j] = ToOffset(pair.Car);
                        cp = pair.Cdr;
                    }
                    else
                    {
                        b[j] = new Offset(0.0, 0.0);
                    }
                }

                forbiddenAttachments.Add(b[0] + new Offset(rp, 0));
                forbiddenAttachments.Add(b[3] + new Offset(rp, 0));
            }
        }

        bool tooClose = false;
        for (int k = 0; k < forbiddenAttachments.Count; k++)
        {
            foreach (Direction side in Both)
            {
                if ((forbiddenAttachments[k] - Attachment[side]).Length
                    < state.Parameters.SlurTieExtremaMinDistance)
                {
                    tooClose = true;
                    break;
                }
            }
        }

        if (tooClose)
        {
            AddScore(state.Parameters.SlurTieExtremaMinDistancePenalty, "extra");
        }

        for (int j = 0; j < state.ExtraEncompassInfos.Count; j++)
        {
            DrulArray<Offset> attachment = Attachment;
            ExtraCollisionInfo info = state.ExtraEncompassInfos[j];

            Interval slurWid = new Interval(
                attachment[Direction.Negative][Axis.X], attachment[Direction.Positive][Axis.X]);

            /*
              to prevent numerical inaccuracies in
              Bezier::get_other_coordinate ().
            */

            bool found = false;
            double y = 0.0;

            foreach (Direction d in Both)
            {
                /*
                  We need to check for the bound explicitly, since the
                  slur-ending can be almost vertical, making the Y
                  coordinate a bad approximation of the object-slur
                  distance.
                */
                if (!(state.ExtraEncompassInfos[j].Grob is Item asItem))
                {
                    continue;
                }

                Interval itemX = asItem.Extent(state.Common[(int)Axis.X], Axis.X);
                itemX.Intersect(state.Extremes[d].SlurHeadXExtent);
                if (!itemX.IsEmpty)
                {
                    y = attachment[d][Axis.Y];
                    found = true;
                }
            }

            if (!found)
            {
                double x = info.Extents[Axis.X].LinearCombination(info.Idx);

                if (!slurWid.Contains(x))
                {
                    continue;
                }

                y = Curve.GetOtherCoordinate(Axis.X, x);
            }

            double dist = 0.0;
            if (ReferenceEquals(info.Type, AroundSymbol))
            {
                dist = info.Extents[Axis.Y].Distance(y);
            }

            /*
              Have to score too: the curve enumeration is limited in its
              shape, and may produce curves which collide anyway.
            */
            else if (ReferenceEquals(info.Type, InsideSymbol))
            {
                dist = (int)state.Dir * (y - info.Extents[Axis.Y][state.Dir]);
            }
            else
            {
                Warn.ProgrammingError("unknown avoidance type");
            }

            dist = Math.Max(dist, 0.0);

            double penalty = info.Penalty
                             * Misc.PeakAround(
                                 0.1 * state.Parameters.ExtraEncompassFreeDistance,
                                 state.Parameters.ExtraEncompassFreeDistance,
                                 dist);

            AddScore(penalty, "extra");
        }
    }

    private void ScoreEdges(SlurScoreState state)
    {
        Offset dz = Attachment[Direction.Positive] - Attachment[Direction.Negative];
        double slope = dz[Axis.Y] / dz[Axis.X];
        double factor = state.Parameters.EdgeAttractionFactor;
        foreach (Direction d in Both)
        {
            double y = Attachment[d][Axis.Y];
            double dy = Math.Abs(y - state.BaseAttachments[d][Axis.Y]);
            double demerit = factor * dy;
            if (state.Extremes[d].Stem != null
                && state.Extremes[d].StemDir == state.Dir

                // TODO - Stem::get_beaming() should be precomputed.
                && Stem.GetBeaming(state.Extremes[d].Stem, -d) == 0)
            {
                demerit /= 5;
            }

            demerit *= Math.Exp(
                (int)state.Dir * (int)d * slope * state.Parameters.EdgeSlopeExponent);

            string dirStr = d == Direction.Negative ? "L" : "R";
            AddScore(demerit, dirStr + " edge");
        }
    }

    private void ScoreSlopes(SlurScoreState state)
    {
        double dy = state.MusicalDy;
        Offset slurDz = Attachment[Direction.Positive] - Attachment[Direction.Negative];
        double slurDy = slurDz[Axis.Y];
        double demerit = 0.0;

        demerit += Math.Max(
                       Math.Abs(slurDy / slurDz[Axis.X]) - state.Parameters.MaxSlope, 0.0)
                   * state.Parameters.MaxSlopeFactor;

        /* 0.2: account for staffline offset. */
        double maxDy = Math.Abs(dy) + 0.2;
        if (state.EdgeHasBeams)
        {
            maxDy += 1.0;
        }

        if (!state.IsBroken)
        {
            demerit += state.Parameters.SteeperSlopeFactor
                       * Math.Max(Math.Abs(slurDy) - maxDy, 0.0);
        }

        // upstream adds the max-slope term TWICE, and the port does too.
        demerit += Math.Max(
                       Math.Abs(slurDy / slurDz[Axis.X]) - state.Parameters.MaxSlope, 0.0)
                   * state.Parameters.MaxSlopeFactor;

        // This morally checks for 0, but account for rounding errors.  TODO: use a
        // detail to set a threshold for what a 'horizontal' slur is?
        if (Math.Abs(dy) < 0.01 && Math.Abs(slurDy) > 0.01 && !state.IsBroken)
        {
            demerit += state.Parameters.NonHorizontalPenalty;
        }

        if (Sign(dy) != 0 && !state.IsBroken && Sign(slurDy) != 0 && Sign(slurDy) != Sign(dy))
        {
            demerit += state.EdgeHasBeams
                ? state.Parameters.SameSlopePenalty / 10
                : state.Parameters.SameSlopePenalty;
        }

        AddScore(demerit, "slope");
    }

    private static int Sign(double value) => value > 0 ? 1 : value < 0 ? -1 : 0;

    private static double ReadReal(object value, double fallback)
        => SchemeConvert.IsNumber(value) ? SchemeConvert.ToDouble(value, "slur") : fallback;

    // upstream's from_scm<Offset>: an (x . y) pair, zero for anything else.
    private static Offset ToOffset(object value)
        => value is Pair pair
            ? new Offset(
                SchemeConvert.ToDouble(pair.Car, "slur"), SchemeConvert.ToDouble(pair.Cdr, "slur"))
            : new Offset(0.0, 0.0);
}
