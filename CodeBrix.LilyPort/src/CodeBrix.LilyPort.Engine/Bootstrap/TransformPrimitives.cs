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

using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap; //was previously: lily/transform-scheme.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The affine-transform constructors the markup layer builds rotations and scalings
/// with.
/// </summary>
public static class TransformPrimitives
{
    /// <summary>Installs the transform entry points.</summary>
    /// <param name="interpreter">The interpreter to install into.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            return;
        }

        // ly:make-transform takes its coefficients in the order xx, yx, xy, yy — which
        // is NOT the order the constructor takes. Upstream swaps the middle pair on the
        // way in with a comment saying "Constructor argument order follows that of
        // Pango", and swapping it back here would transpose every transform.
        interpreter.DefinePrimitive("ly:make-transform", 0, 6, a =>
        {
            if (a.Length < 4 || a[0] is DefaultArgument)
            {
                return Transform.Identity;
            }

            double xx = SchemeConvert.ToDouble(a[0], "ly:make-transform");
            double yx = SchemeConvert.ToDouble(a[1], "ly:make-transform");
            double xy = SchemeConvert.ToDouble(a[2], "ly:make-transform");
            double yy = SchemeConvert.ToDouble(a[3], "ly:make-transform");

            if (a.Length < 6 || a[4] is DefaultArgument)
            {
                return new Transform(xx, xy, yx, yy, 0, 0);
            }

            return new Transform(
                xx,
                xy,
                yx,
                yy,
                SchemeConvert.ToDouble(a[4], "ly:make-transform"),
                SchemeConvert.ToDouble(a[5], "ly:make-transform"));
        });

        interpreter.DefinePrimitive("ly:make-scaling", 1, 2, a =>
        {
            if (a.Length > 1 && !(a[1] is DefaultArgument))
            {
                return new Transform(
                    SchemeConvert.ToDouble(a[0], "ly:make-scaling"),
                    0.0,
                    0.0,
                    SchemeConvert.ToDouble(a[1], "ly:make-scaling"),
                    0.0,
                    0.0);
            }

            // A PAIR is two scales; a lone number is a scaled rotation in the manner of
            // complex multiplication. The port has no complex numbers, so a real number
            // is the degenerate case with no imaginary part — a plain uniform scaling.
            if (a[0] is Pair pair)
            {
                return new Transform(
                    SchemeConvert.ToDouble(pair.Car, "ly:make-scaling"),
                    0.0,
                    0.0,
                    SchemeConvert.ToDouble(pair.Cdr, "ly:make-scaling"),
                    0.0,
                    0.0);
            }

            double scale = SchemeConvert.ToDouble(a[0], "ly:make-scaling");
            return new Transform(scale, 0.0, 0.0, scale, 0.0, 0.0);
        });

        interpreter.DefinePrimitive("ly:make-rotation", 1, 2, a =>
        {
            Offset center = a.Length > 1 && a[1] is Pair point
                ? new Offset(
                    SchemeConvert.ToDouble(point.Car, "ly:make-rotation"),
                    SchemeConvert.ToDouble(point.Cdr, "ly:make-rotation"))
                : Offset.Zero;

            return new Transform(
                SchemeConvert.ToDouble(a[0], "ly:make-rotation"), center);
        });

        interpreter.DefinePrimitive("ly:make-translation", 1, 2, a =>
        {
            if (a.Length > 1 && !(a[1] is DefaultArgument))
            {
                return new Transform(new Offset(
                    SchemeConvert.ToDouble(a[0], "ly:make-translation"),
                    SchemeConvert.ToDouble(a[1], "ly:make-translation")));
            }

            if (a[0] is Pair point)
            {
                return new Transform(new Offset(
                    SchemeConvert.ToDouble(point.Car, "ly:make-translation"),
                    SchemeConvert.ToDouble(point.Cdr, "ly:make-translation")));
            }

            return new Transform(new Offset(
                SchemeConvert.ToDouble(a[0], "ly:make-translation"), 0.0));
        });

        interpreter.DefinePrimitive("ly:transform->list", 1, 1, a =>
        {
            if (!(a[0] is Transform transform))
            {
                throw SchemeErrors.WrongType("ly:transform->list", "transform", a[0]);
            }

            // Reported in the SCHEME order — xx, yx, xy, yy — so that feeding the result
            // straight back to ly:make-transform round-trips.
            return Pair.List(
                transform.XX,
                transform.YX,
                transform.XY,
                transform.YY,
                transform.X0,
                transform.Y0);
        });
    }
}
