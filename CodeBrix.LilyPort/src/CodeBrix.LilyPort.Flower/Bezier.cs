/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Jan Nieuwenhuizen <janneke@gnu.org>
  Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Flower; //was previously: lily/bezier.cc, lily/include/bezier.hh;

// Modified by Jeremy Ellis on 2026-08-02 as part of the CodeBrix port.

/// <summary>
/// A cubic Bézier curve: four control points.
/// <para>
/// LilyPond draws every slur, tie and phrasing mark as one of these, so the curve is
/// always cubic -- <see cref="ControlCount"/> is a constant, not a length.
/// </para>
/// </summary>
public sealed class Bezier
{
    /// <summary>The number of control points in a cubic Bézier curve.</summary>
    public const int ControlCount = 4;

    private static readonly double[] BinomialCoefficient3 = { 1.0, 3.0, 3.0, 1.0 };

    private readonly Offset[] _control = new Offset[ControlCount];

    /// <summary>Initializes a curve with all control points at the origin.</summary>
    public Bezier()
    {
    }

    /// <summary>Initializes a curve from its four control points.</summary>
    /// <param name="controlPoints">The control points, first to last.</param>
    public Bezier(IReadOnlyList<Offset> controlPoints)
    {
        if (controlPoints == null)
        {
            throw new ArgumentNullException(nameof(controlPoints));
        }

        if (controlPoints.Count != ControlCount)
        {
            throw new ArgumentException(
                "A cubic Bezier needs exactly " + ControlCount + " control points.",
                nameof(controlPoints));
        }

        for (int i = 0; i < ControlCount; i++)
        {
            _control[i] = controlPoints[i];
        }
    }

    /// <summary>Gets or sets a control point.</summary>
    /// <param name="index">The control-point index, 0 to 3.</param>
    /// <returns>The control point.</returns>
    public Offset this[int index]
    {
        get => _control[index];
        set => _control[index] = value;
    }

    /// <summary>Gets the control points.</summary>
    public IReadOnlyList<Offset> ControlPoints => _control;

    /// <summary>Evaluates the curve at a parameter value.</summary>
    /// <param name="t">The parameter, 0 at the first control point and 1 at the last.</param>
    /// <returns>The point on the curve.</returns>
    public Offset CurvePoint(double t)
    {
        double tj = 1;
        double[] oneMinusTj = new double[ControlCount];
        oneMinusTj[0] = 1;
        for (int i = 1; i < ControlCount; i++)
        {
            oneMinusTj[i] = oneMinusTj[i - 1] * (1 - t);
        }

        Offset result = Offset.Zero;
        for (int j = 0; j < ControlCount; j++)
        {
            result += _control[j] * BinomialCoefficient3[j] * tj * oneMinusTj[ControlCount - 1 - j];
            tj *= t;
        }

        return result;
    }

    /// <summary>Returns the unit tangent of the curve at a parameter value.</summary>
    /// <param name="t">The parameter.</param>
    /// <returns>The direction.</returns>
    public Offset DirectionAtPoint(double t)
    {
        Offset[] secondOrder = new Offset[3];
        for (int i = 0; i < 3; i++)
        {
            secondOrder[i] = ((_control[i + 1] - _control[i]) * t) + _control[i];
        }

        Offset[] thirdOrder = new Offset[2];
        for (int i = 0; i < 2; i++)
        {
            thirdOrder[i] = ((secondOrder[i + 1] - secondOrder[i]) * t) + secondOrder[i];
        }

        return (thirdOrder[1] - thirdOrder[0]).Direction();
    }

    /// <summary>Splits the curve at a parameter value, by de Casteljau's construction.</summary>
    /// <param name="t">The parameter to split at.</param>
    /// <param name="leftPart">Receives the part before the split.</param>
    /// <param name="rightPart">Receives the part after the split.</param>
    public void Subdivide(double t, out Bezier leftPart, out Bezier rightPart)
    {
        Offset[,] p = new Offset[ControlCount, ControlCount];

        for (int i = 0; i < ControlCount; i++)
        {
            p[i, ControlCount - 1] = _control[i];
        }

        for (int j = ControlCount - 2; j >= 0; j--)
        {
            for (int i = 0; i < ControlCount - 1; i++)
            {
                p[i, j] = p[i, j + 1] + (t * (p[i + 1, j + 1] - p[i, j + 1]));
            }
        }

        leftPart = new Bezier();
        rightPart = new Bezier();
        for (int i = 0; i < ControlCount; i++)
        {
            leftPart[i] = p[0, ControlCount - 1 - i];
            rightPart[i] = p[i, i];
        }
    }

    /// <summary>
    /// Returns the sub-curve between two parameter values. A sub-curve of a Bézier curve
    /// is in turn a Bézier curve.
    /// </summary>
    /// <param name="tMin">The lower parameter, 0 at the first control point.</param>
    /// <param name="tMax">The upper parameter, 1 at the last control point.</param>
    /// <returns>The sub-curve.</returns>
    public Bezier Extract(double tMin, double tMax)
    {
        // Upstream reports these through programming_error and carries on with a
        // misshapen curve rather than raising; keeping that keeps the behaviour of any
        // score that relies on it, and the diagnostic is what the regression suite sees.
        if (tMin < 0 || tMax > 1)
        {
            Warn.ProgrammingError(
                "bezier extract arguments outside of limits: curve may have bad shape");
        }

        if (tMin >= tMax)
        {
            Warn.ProgrammingError(
                "lower bezier extract value not less than upper value: curve may have bad shape");
        }

        Bezier second;
        if (tMin == 0.0)
        {
            second = Copy();
        }
        else
        {
            Subdivide(tMin, out _, out second);
        }

        if (tMax == 1.0)
        {
            return second;
        }

        second.Subdivide((tMax - tMin) / (1 - tMin), out Bezier third, out _);
        return third;
    }

    /// <summary>Scales the curve independently on each axis.</summary>
    /// <param name="x">The horizontal factor.</param>
    /// <param name="y">The vertical factor.</param>
    public void Scale(double x, double y)
    {
        for (int i = 0; i < ControlCount; i++)
        {
            _control[i] = new Offset(x * _control[i].X, y * _control[i].Y);
        }
    }

    /// <summary>Rotates the curve about the origin.</summary>
    /// <param name="degrees">The angle in degrees.</param>
    public void Rotate(double degrees)
    {
        Offset rotation = Offset.Directed(degrees);
        for (int i = 0; i < ControlCount; i++)
        {
            _control[i] = Offset.ComplexMultiply(rotation, _control[i]);
        }
    }

    /// <summary>Translates the curve.</summary>
    /// <param name="offset">The translation.</param>
    public void Translate(Offset offset)
    {
        for (int i = 0; i < ControlCount; i++)
        {
            _control[i] += offset;
        }
    }

    /// <summary>Reverses the direction of the curve.</summary>
    public void Reverse()
    {
        Offset[] reversed = new Offset[ControlCount];
        for (int i = 0; i < ControlCount; i++)
        {
            reversed[i] = _control[ControlCount - i - 1];
        }

        for (int i = 0; i < ControlCount; i++)
        {
            _control[i] = reversed[i];
        }
    }

    /// <summary>Returns the extent of the control points along an axis.</summary>
    /// <param name="axis">The axis to measure.</param>
    /// <returns>The interval spanned by the control points.</returns>
    public Interval ControlPointExtent(Axis axis)
    {
        Interval extent = Interval.Empty;
        for (int i = 0; i < ControlCount; i++)
        {
            extent.AddPoint(_control[i][axis]);
        }

        return extent;
    }

    /// <summary>
    /// Returns the curve as a polynomial in t along one axis.
    /// <para>
    /// The four Bernstein basis terms are cached, because <see cref="SolvePoint"/> and
    /// <see cref="SolveDerivative"/> rebuild this on every call and the slur and tie
    /// scorers call them in inner loops.
    /// </para>
    /// </summary>
    /// <param name="axis">The axis to project onto.</param>
    /// <returns>The cubic polynomial.</returns>
    public Polynomial ToPolynomial(Axis axis)
    {
        Polynomial p = new Polynomial(0.0);
        for (int j = 0; j <= 3; j++)
        {
            Polynomial q = BernsteinTerms[j].Copy();
            q.ScalarMultiply(_control[j][axis]);
            p.Add(q);
        }

        return p;
    }

    /// <summary>
    /// Returns the parameter values at which the curve's derivative is parallel to a
    /// given direction.
    /// </summary>
    /// <param name="derivative">The direction to match.</param>
    /// <returns>The solutions inside [0, 1].</returns>
    public List<double> SolveDerivative(Offset derivative)
    {
        Polynomial xp = ToPolynomial(Axis.X);
        Polynomial yp = ToPolynomial(Axis.Y);
        xp.Differentiate();
        yp.Differentiate();

        Polynomial combine = (xp * derivative.Y) - (yp * derivative.X);

        return FilterSolutions(combine.Solve());
    }

    /// <summary>
    /// Returns the parameter values at which the curve crosses a given coordinate on
    /// one axis.
    /// </summary>
    /// <param name="axis">The axis the coordinate is measured on.</param>
    /// <param name="coordinate">The coordinate to hit.</param>
    /// <returns>The solutions inside [0, 1].</returns>
    public List<double> SolvePoint(Axis axis, double coordinate)
    {
        Polynomial p = ToPolynomial(axis);
        p.Coefficients[0] -= coordinate;

        return FilterSolutions(p.Solve());
    }

    /// <summary>
    /// Returns the other coordinate where the curve crosses a coordinate on one axis,
    /// taking the first solution.
    /// </summary>
    /// <param name="axis">The axis the coordinate is measured on.</param>
    /// <param name="coordinate">The coordinate to hit.</param>
    /// <returns>The other coordinate, or zero when the curve never gets there.</returns>
    public double GetOtherCoordinate(Axis axis, double coordinate)
    {
        Axis other = Axes.Other(axis);
        List<double> ts = SolvePoint(axis, coordinate);

        if (ts.Count == 0)
        {
            Warn.ProgrammingError("no solution found for Bezier intersection");
            return 0.0;
        }

        return CurveCoordinate(ts[0], other);
    }

    /// <summary>
    /// Returns every other coordinate where the curve crosses a coordinate on one axis.
    /// </summary>
    /// <param name="axis">The axis the coordinate is measured on.</param>
    /// <param name="coordinate">The coordinate to hit.</param>
    /// <returns>The other coordinates, one per crossing.</returns>
    public List<double> GetOtherCoordinates(Axis axis, double coordinate)
    {
        Axis other = Axes.Other(axis);
        List<double> ts = SolvePoint(axis, coordinate);
        List<double> solutions = new List<double>(ts.Count);
        for (int i = 0; i < ts.Count; i++)
        {
            solutions.Add(CurveCoordinate(ts[i], other));
        }

        return solutions;
    }

    /// <summary>Returns one coordinate of the curve point at a parameter value.</summary>
    /// <param name="t">The parameter, from 0 to 1.</param>
    /// <param name="axis">The axis to read.</param>
    /// <returns>The coordinate.</returns>
    public double CurveCoordinate(double t, Axis axis) => CurvePoint(t)[axis];

    /// <summary>
    /// For the portion of the curve between <paramref name="left"/> and
    /// <paramref name="right"/> along one axis, returns the bounding limit in one
    /// direction along the other axis.
    /// </summary>
    /// <param name="axis">The axis the range is measured on.</param>
    /// <param name="left">The lower end of the range.</param>
    /// <param name="right">The upper end of the range.</param>
    /// <param name="direction">Which limit to return.</param>
    /// <returns>The limit, or zero when no part of the curve lies in the range.</returns>
    public double MinMax(Axis axis, double left, double right, Direction direction)
    {
        Axis other = Axes.Other(axis);

        // The curve could hit its bounding box limit along the other axis at:
        //  points where the curve is parallel to this axis,
        Offset vector = Offset.Zero.With(axis, 1.0);
        List<double> solutions = SolveDerivative(vector);

        //  or endpoints of the curve,
        // (using points just inside the ends, so that an endpoint is evaluated
        //  if it falls within rounding error of L or R and the curve lies inside)
        solutions.Add(0.999);
        solutions.Add(0.001);

        Interval extent = Interval.Empty;
        for (int i = solutions.Count - 1; i >= 0; i--)
        {
            Offset p = CurvePoint(solutions[i]);
            if (p[axis] >= left && p[axis] <= right)
            {
                extent.AddPoint(p[other]);
            }
        }

        //  or intersections of the curve with the bounding lines at L and R.
        Interval range = new Interval(left, right);
        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            List<double> crossings = GetOtherCoordinates(axis, range[d]);
            for (int i = crossings.Count - 1; i >= 0; i--)
            {
                extent.AddPoint(crossings[i]);
            }
        }

        if (extent.IsEmpty)
        {
            Warn.ProgrammingError("Bezier curve does not cross region of concern");
            return 0.0;
        }

        return extent[direction];
    }

    /// <summary>
    /// Returns the true bounding extent along an axis — the curve's own extent, not
    /// the hull of its control points.
    /// </summary>
    /// <param name="axis">The axis to measure.</param>
    /// <returns>The extent.</returns>
    public Interval Extent(Axis axis)
    {
        Offset d = Offset.Zero.With(Axes.Other(axis), 1.0);
        Interval extent = Interval.Empty;
        List<double> solutions = SolveDerivative(d);
        solutions.Add(1.0);
        solutions.Add(0.0);

        for (int i = solutions.Count - 1; i >= 0; i--)
        {
            Offset o = CurvePoint(solutions[i]);
            extent.Unite(new Interval(o[axis], o[axis]));
        }

        return extent;
    }

    /// <summary>Returns a copy of this curve.</summary>
    /// <returns>The copy.</returns>
    public Bezier Copy() => new Bezier(_control);

    /// <summary>Removes all numbers outside [0, 1] from a solution set.</summary>
    /// <param name="solutions">The solutions to filter. Filtered in place.</param>
    /// <returns>The same list, with out-of-range solutions removed.</returns>
    private static List<double> FilterSolutions(List<double> solutions)
    {
        for (int i = solutions.Count - 1; i >= 0; i--)
        {
            if (solutions[i] < 0 || solutions[i] > 1)
            {
                solutions.RemoveAt(i);
            }
        }

        return solutions;
    }

    /*
      Cache binom (3, j) t^j (1-t)^{3-j}
    */
    private static readonly Polynomial[] BernsteinTerms = BuildBernsteinTerms();

    private static Polynomial[] BuildBernsteinTerms()
    {
        Polynomial[] terms = new Polynomial[4];
        for (int j = 0; j <= 3; j++)
        {
            Polynomial term = Polynomial.Power(j, new Polynomial(0, 1))
                              * Polynomial.Power(3 - j, new Polynomial(1, -1));
            term.ScalarMultiply(BinomialCoefficient3[j]);
            terms[j] = term;
        }

        return terms;
    }
}
