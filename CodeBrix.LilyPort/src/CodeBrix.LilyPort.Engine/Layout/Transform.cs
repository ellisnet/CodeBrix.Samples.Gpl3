/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2016--2026 David Kastrup <dak@gnu.org>

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
using System.Globalization;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/transform.cc, lily/include/transform.hh;

// Modified by Jeremy Ellis on 2026-08-05 as part of the CodeBrix port.

/// <summary>
/// A 2D affine transform, as
/// <code>
///    [ [ xx, xy, x0 ]     [ [ x ]
///      [ yx, yy, y0 ]   *   [ y ]
///      [ 0,  0,  1  ] ]     [ 1 ] ]
/// </code>
/// with the last row implied rather than stored.
/// <para>
/// Upstream reuses Pango's matrix type for this and calls Pango's own matrix
/// arithmetic. The port has no Pango, so the four operations it used —
/// <c>concat</c>, <c>translate</c>, <c>scale</c> and <c>transform_point</c> — are
/// written out here. They are the standard ones, but their ARGUMENT ORDER is not
/// symmetric and the comment in upstream's header is worth repeating: LilyPond's
/// y axis increases UPWARDS and Pango's increases downwards, so "clockwise" in Pango's
/// documentation means counter-clockwise here. That is why rotation is built from
/// <see cref="Offset.Directed"/> rather than from Pango's own rotate.
/// </para>
/// </summary>
public struct Transform : IEquatable<Transform>
{
    private double _xx;
    private double _xy;
    private double _yx;
    private double _yy;
    private double _x0;
    private double _y0;
    private bool _initialized;

    /// <summary>
    /// Initializes a transform from its six coefficients.
    /// <para>
    /// The parameter order is PANGO's — <c>xx, xy, yx, yy</c> — and NOT the order
    /// <c>ly:make-transform</c> takes, which is <c>xx, yx, xy, yy</c>. Upstream's
    /// binding swaps the middle pair on the way in, and so does this port's.
    /// </para>
    /// </summary>
    /// <param name="xx">The x-to-x coefficient.</param>
    /// <param name="xy">The y-to-x coefficient.</param>
    /// <param name="yx">The x-to-y coefficient.</param>
    /// <param name="yy">The y-to-y coefficient.</param>
    /// <param name="x0">The x translation.</param>
    /// <param name="y0">The y translation.</param>
    public Transform(double xx, double xy, double yx, double yy, double x0, double y0)
    {
        _xx = xx;
        _xy = xy;
        _yx = yx;
        _yy = yy;
        _x0 = x0;
        _y0 = y0;
        _initialized = true;
    }

    /// <summary>Initializes a pure translation.</summary>
    /// <param name="offset">How far to move.</param>
    public Transform(Offset offset)
        : this(1, 0, 0, 1, offset[Axis.X], offset[Axis.Y])
    {
    }

    /// <summary>
    /// Initializes a rotation about a centre.
    /// <para>
    /// Built from <see cref="Offset.Directed"/> rather than from a sine and cosine
    /// pair, deliberately: upstream's comment says Pango's own rotate "does not bother
    /// maintaining sane behavior at multiples of 45 degrees", and the exactness at
    /// right angles is what keeps a rotated stencil's extents from drifting.
    /// </para>
    /// </summary>
    /// <param name="angle">The angle, in degrees, counter-clockwise.</param>
    /// <param name="center">The point to rotate about.</param>
    public Transform(double angle, Offset center)
    {
        Offset direction = Offset.Directed(angle);
        _xx = direction[Axis.X];

        // Written as a subtraction from zero to avoid producing negative zero, which
        // upstream calls out in a comment of its own.
        _xy = 0.0 - direction[Axis.Y];
        _yx = direction[Axis.Y];
        _yy = direction[Axis.X];
        _x0 = center[Axis.X];
        _y0 = center[Axis.Y];
        _initialized = true;

        Offset moved = Apply(-center);
        _x0 = moved[Axis.X];
        _y0 = moved[Axis.Y];
    }

    /// <summary>Gets the identity transform.</summary>
    public static Transform Identity => new Transform(1, 0, 0, 1, 0, 0);

    /// <summary>Gets the x-to-x coefficient.</summary>
    public double XX => _initialized ? _xx : 1.0;

    /// <summary>Gets the y-to-x coefficient.</summary>
    public double XY => _xy;

    /// <summary>Gets the x-to-y coefficient.</summary>
    public double YX => _yx;

    /// <summary>Gets the y-to-y coefficient.</summary>
    public double YY => _initialized ? _yy : 1.0;

    /// <summary>Gets the x translation.</summary>
    public double X0 => _x0;

    /// <summary>Gets the y translation.</summary>
    public double Y0 => _y0;

    /// <summary>Applies this transform to a point.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The transformed point.</returns>
    public Offset Apply(Offset point)
        => new Offset(
            (XX * point[Axis.X]) + (XY * point[Axis.Y]) + X0,
            (YX * point[Axis.X]) + (YY * point[Axis.Y]) + Y0);

    /// <summary>
    /// Applies this transform to another, which is composition: the result does
    /// <paramref name="other"/> first and then this.
    /// </summary>
    /// <param name="other">The transform to apply first.</param>
    /// <returns>The composed transform.</returns>
    public Transform Apply(Transform other)
    {
        Transform result = this;
        result.Concat(other);
        return result;
    }

    /// <summary>Composes another transform into this one, in place.</summary>
    /// <param name="other">The transform to apply first.</param>
    public void Concat(Transform other)
    {
        double xx = XX;
        double xy = XY;
        double yx = YX;
        double yy = YY;
        double x0 = X0;
        double y0 = Y0;

        _xx = (xx * other.XX) + (xy * other.YX);
        _xy = (xx * other.XY) + (xy * other.YY);
        _yx = (yx * other.XX) + (yy * other.YX);
        _yy = (yx * other.XY) + (yy * other.YY);
        _x0 = (xx * other.X0) + (xy * other.Y0) + x0;
        _y0 = (yx * other.X0) + (yy * other.Y0) + y0;
        _initialized = true;
    }

    /// <summary>Translates, in place and in the transform's own frame.</summary>
    /// <param name="offset">How far to move.</param>
    public void Translate(Offset offset)
    {
        double tx = offset[Axis.X];
        double ty = offset[Axis.Y];
        double xx = XX;
        double yy = YY;

        _x0 += (xx * tx) + (XY * ty);
        _y0 += (YX * tx) + (yy * ty);
        _xx = xx;
        _yy = yy;
        _initialized = true;
    }

    /// <summary>Scales, in place and in the transform's own frame.</summary>
    /// <param name="xScale">The horizontal factor.</param>
    /// <param name="yScale">The vertical factor.</param>
    public void Scale(double xScale, double yScale)
    {
        double xx = XX;
        double yy = YY;

        _xx = xx * xScale;
        _yx = YX * xScale;
        _xy = XY * yScale;
        _yy = yy * yScale;
        _initialized = true;
    }

    /// <summary>Rotates, in place, about a centre.</summary>
    /// <param name="angle">The angle, in degrees.</param>
    /// <param name="center">The point to rotate about.</param>
    public void Rotate(double angle, Offset center) => Concat(new Transform(angle, center));

    /// <summary>Determines whether another transform has the same coefficients.</summary>
    /// <param name="other">The transform to compare with.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public bool Equals(Transform other)
        => XX == other.XX && XY == other.XY && YX == other.YX
           && YY == other.YY && X0 == other.X0 && Y0 == other.Y0;

    /// <summary>Determines whether an object is an equal transform.</summary>
    /// <param name="obj">The object.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public override bool Equals(object obj) => obj is Transform other && Equals(other);

    /// <summary>Returns a hash code.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(XX, XY, YX, YY, X0, Y0);

    /// <summary>Returns the external representation.</summary>
    /// <returns>The transform, printed as upstream prints it.</returns>
    public override string ToString()
        => string.Format(
            CultureInfo.InvariantCulture,
            "#<Transform [[{0:F6} {1:F6} {2:F6}] [{3:F6} {4:F6} {5:F6}]]>",
            XX, XY, X0, YX, YY, Y0);
}
