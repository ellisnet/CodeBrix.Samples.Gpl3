/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Flower; //was previously: flower/offset.cc, flower/include/offset.hh, flower/include/axis.hh;
// Modified by Jeremy Ellis on 2026-08-02 as part of the CodeBrix port:
//   - translated from C++17 to C# targeting net10.0
//   - class replaced by a readonly struct; Offset is a two-double value copied
//     constantly through the geometry code
//   - the public coordinate array is replaced by X and Y properties plus an Axis
//     indexer, which is how every caller actually uses it

/// <summary>The two axes of the page. LilyPond indexes geometry by these.</summary>
public enum Axis
{
    /// <summary>The horizontal axis.</summary>
    X = 0,

    /// <summary>The vertical axis.</summary>
    Y = 1,
}

/// <summary>
/// A two-dimensional vector. LilyPond sometimes treats these as complex numbers
/// (<c>x + iy</c>), which is why some members are named after complex arithmetic.
/// </summary>
public readonly struct Offset : IEquatable<Offset>
{
    /// <summary>Initializes an offset from its coordinates.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    public Offset(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Gets the horizontal coordinate.</summary>
    public double X { get; }

    /// <summary>Gets the vertical coordinate.</summary>
    public double Y { get; }

    /// <summary>Gets the zero offset. This is also the default value.</summary>
    public static Offset Zero => new Offset(0.0, 0.0);

    /// <summary>Gets the coordinate on an axis.</summary>
    /// <param name="axis">The axis to read.</param>
    /// <returns>The coordinate.</returns>
    public double this[Axis axis] => axis == Axis.X ? X : Y;

    /// <summary>Returns a copy with one axis replaced.</summary>
    /// <param name="axis">The axis to set.</param>
    /// <param name="value">The new coordinate.</param>
    /// <returns>The updated offset.</returns>
    public Offset With(Axis axis, double value)
        => axis == Axis.X ? new Offset(value, Y) : new Offset(X, value);

    /// <summary>Adds two offsets.</summary>
    /// <param name="a">The first offset.</param>
    /// <param name="b">The second offset.</param>
    /// <returns>The sum.</returns>
    public static Offset operator +(Offset a, Offset b) => new Offset(a.X + b.X, a.Y + b.Y);

    /// <summary>Subtracts one offset from another.</summary>
    /// <param name="a">The minuend.</param>
    /// <param name="b">The subtrahend.</param>
    /// <returns>The difference.</returns>
    public static Offset operator -(Offset a, Offset b) => new Offset(a.X - b.X, a.Y - b.Y);

    /// <summary>Negates an offset.</summary>
    /// <param name="offset">The offset to negate.</param>
    /// <returns>The reversed offset.</returns>
    public static Offset operator -(Offset offset) => new Offset(-offset.X, -offset.Y);

    /// <summary>Scales an offset.</summary>
    /// <param name="offset">The offset to scale.</param>
    /// <param name="factor">The scale factor.</param>
    /// <returns>The scaled offset.</returns>
    public static Offset operator *(Offset offset, double factor)
        => new Offset(offset.X * factor, offset.Y * factor);

    /// <summary>Scales an offset.</summary>
    /// <param name="factor">The scale factor.</param>
    /// <param name="offset">The offset to scale.</param>
    /// <returns>The scaled offset.</returns>
    public static Offset operator *(double factor, Offset offset) => offset * factor;

    /// <summary>Divides an offset by a scalar.</summary>
    /// <param name="offset">The offset to divide.</param>
    /// <param name="divisor">The divisor.</param>
    /// <returns>The scaled offset.</returns>
    public static Offset operator /(Offset offset, double divisor) => offset * (1.0 / divisor);

    /// <summary>Gets the vector's length.</summary>
    public double Length => Math.Sqrt((X * X) + (Y * Y));

    /// <summary>Gets a value indicating whether both coordinates are finite.</summary>
    public bool IsSane => !double.IsNaN(X) && !double.IsNaN(Y);

    /// <summary>Returns the offset with its coordinates exchanged.</summary>
    /// <returns>The swapped offset.</returns>
    public Offset Swapped() => new Offset(Y, X);

    /// <summary>Returns the offset reflected across one axis.</summary>
    /// <param name="axis">The axis whose coordinate is negated.</param>
    /// <returns>The mirrored offset.</returns>
    public Offset Mirror(Axis axis)
        => axis == Axis.X ? new Offset(-X, Y) : new Offset(X, -Y);

    /// <summary>
    /// Returns the unit vector pointing the same way. Infinite coordinates are handled
    /// specially, and the zero vector is returned unchanged rather than producing NaN.
    /// </summary>
    /// <returns>The direction vector.</returns>
    public Offset Direction()
    {
        if (double.IsInfinity(X))
        {
            if (!double.IsInfinity(Y))
            {
                return new Offset(X > 0.0 ? 1.0 : -1.0, 0.0);
            }
        }
        else if (double.IsInfinity(Y))
        {
            return new Offset(0.0, Y > 0.0 ? 1.0 : -1.0);
        }
        else if (X == 0.0 && Y == 0.0)
        {
            return this;
        }

        return this / Length;
    }

    /// <summary>
    /// Multiplies two offsets as complex numbers. An infinite imaginary part in the
    /// second operand yields zero, matching upstream's guard.
    /// </summary>
    /// <param name="z1">The first operand.</param>
    /// <param name="z2">The second operand.</param>
    /// <returns>The complex product.</returns>
    public static Offset ComplexMultiply(Offset z1, Offset z2)
    {
        if (double.IsInfinity(z2.Y))
        {
            return Zero;
        }

        return new Offset(
            (z1.X * z2.X) - (z1.Y * z2.Y),
            (z1.X * z2.Y) + (z1.Y * z2.X));
    }

    /// <summary>
    /// Returns the unit offset pointing at an angle, measured in degrees.
    /// <para>
    /// The angle is first folded into (-180, 180], then each component is computed from a
    /// sine of an angle no larger than 90 degrees in absolute value. That is not a
    /// simplification of cos/sin -- it is upstream's deliberate arrangement, which keeps
    /// the rounding error of pi/180 from accumulating and makes the x and y magnitudes
    /// come out exactly equal at odd multiples of 45 degrees. Do not "clean this up".
    /// </para>
    /// </summary>
    /// <param name="degrees">The angle in degrees.</param>
    /// <returns>The unit offset.</returns>
    public static Offset Directed(double degrees)
    {
        double angle = degrees;
        if (angle <= -360.0 || angle >= 360.0)
        {
            // C's fmod truncates toward zero; Math.IEEERemainder rounds to nearest, so
            // it is the wrong operation here even though the names look interchangeable.
            angle = angle % 360.0;
        }

        if (angle <= -180.0)
        {
            angle += 360.0;
        }
        else if (angle > 180.0)
        {
            angle -= 360.0;
        }

        const double ToRadians = Math.PI / 180.0;
        if (angle > 0)
        {
            return angle > 90
                ? new Offset(Math.Sin((90 - angle) * ToRadians), Math.Sin((180 - angle) * ToRadians))
                : new Offset(Math.Sin((90 - angle) * ToRadians), Math.Sin(angle * ToRadians));
        }

        return angle < -90
            ? new Offset(Math.Sin((90 + angle) * ToRadians), Math.Sin((-180 - angle) * ToRadians))
            : new Offset(Math.Sin((90 + angle) * ToRadians), Math.Sin(angle * ToRadians));
    }

    /// <summary>Gets the angle from the positive X axis, in degrees, in -180..180.</summary>
    /// <returns>The angle in degrees.</returns>
    public double AngleDegrees() => Math.Atan2(Y, X) * 180.0 / Math.PI;

    /// <summary>Returns the offset rotated about the origin.</summary>
    /// <param name="degrees">The rotation in degrees, counter-clockwise.</param>
    /// <returns>The rotated offset.</returns>
    public Offset Rotated(double degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        return new Offset((X * cos) - (Y * sin), (X * sin) + (Y * cos));
    }

    /// <summary>Determines whether two offsets are equal.</summary>
    /// <param name="other">The offset to compare with.</param>
    /// <returns><see langword="true"/> when both coordinates match.</returns>
    public bool Equals(Offset other) => X.Equals(other.X) && Y.Equals(other.Y);

    /// <summary>Determines whether this equals another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the object is an equal offset.</returns>
    public override bool Equals(object obj) => obj is Offset other && Equals(other);

    /// <summary>Returns a hash code.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <summary>Tests equality.</summary>
    /// <param name="left">The first offset.</param>
    /// <param name="right">The second offset.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(Offset left, Offset right) => left.Equals(right);

    /// <summary>Tests inequality.</summary>
    /// <param name="left">The first offset.</param>
    /// <param name="right">The second offset.</param>
    /// <returns><see langword="true"/> when not equal.</returns>
    public static bool operator !=(Offset left, Offset right) => !left.Equals(right);

    /// <summary>Returns the external representation.</summary>
    /// <returns>The coordinates as <c>(x, y)</c>.</returns>
    public override string ToString()
        => " (" + X.ToString(CultureInfo.InvariantCulture)
           + ", " + Y.ToString(CultureInfo.InvariantCulture) + ")";
}
