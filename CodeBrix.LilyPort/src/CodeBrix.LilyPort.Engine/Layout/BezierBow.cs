/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/bezier-bow.cc (the shape half only);

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port:
//   - PARTIAL PULL-FORWARD FROM EPG12, on the same footing as EPG22's pull of
//     break-substitution.cc's direction half. Tuplet_bracket::print draws a
//     \tupletSlur through slur_shape, so EPG17 needs the SHAPE half of bezier-bow.cc
//     and nothing else. bezier-bow.cc's ledger row STAYS with EPG12, which still owes
//     Bezier_bow itself (the class that fits a curve around encompassing points) —
//     that half is the decades-tuned code the faithfulness rule is about, and this
//     half is not: it is three closed-form functions with upstream's own derivation
//     kept verbatim in the comments.

/// <summary>
/// The closed-form slur shape: the curve a slur takes before any scoring adjusts it.
/// </summary>
/// <remarks>
/// <para>
/// For small widths the height should be proportional to the width; as the width goes to
/// infinity the height should rise asymptotically to a limit. So the height is
/// <c>h_inf * F (width * r_0 / h_inf)</c> for an <c>F</c> with <c>F (0) = 0</c>,
/// <c>F' (0) = 1</c> and <c>F (inf) = 1</c> — here <c>F (x) = 2/pi * atan (pi x / 2)</c>.
/// </para>
/// <para>
/// The indent is proportional to the slur's height for small slurs. For large slurs that
/// would give a certain hookiness at the end, so the indent is increased: <c>G (w) =
/// 2 h_inf - max_fraction * q^2 / (w + q)</c> with <c>q = 2 h_inf / max_fraction</c>,
/// which satisfies <c>G (0) = 0</c>, <c>G' (0) = 1/3</c> and <c>G (inf) = 2 h_inf</c>.
/// Derivative constraints are why the indent cannot exceed a third of the length.
/// </para>
/// <para>
/// Upstream's own note: although these might seem candidates for SCM-ifying, it is not at
/// all clear which parameters should be. At present <c>h_inf</c> and <c>r_0</c> come from
/// layout settings, and no experiments determined the best combinations.
/// </para>
/// </remarks>
public static class BezierBow
{
    /// <summary>Returns the closed-form slur curve for a width.</summary>
    /// <param name="width">The horizontal span of the slur.</param>
    /// <param name="heightLimit">The height the slur rises to asymptotically.</param>
    /// <param name="ratio">The initial height-to-width ratio.</param>
    /// <returns>The curve, running left to right and pointing upwards.</returns>
    public static Bezier SlurShape(double width, double heightLimit, double ratio)
    {
        GetSlurIndentHeight(out double indent, out double height, width, heightLimit, ratio);

        Bezier curve = new Bezier();
        curve[0] = new Offset(0, 0);
        curve[1] = new Offset(indent, height);
        curve[2] = new Offset(width - indent, height);
        curve[3] = new Offset(width, 0);
        return curve;
    }

    /// <summary>Returns the indent and height a slur of a given width takes.</summary>
    /// <param name="indent">Receives how far in the control points sit.</param>
    /// <param name="height">Receives how high the curve rises.</param>
    /// <param name="width">The horizontal span of the slur.</param>
    /// <param name="heightLimit">The height the slur rises to asymptotically.</param>
    /// <param name="ratio">The initial height-to-width ratio.</param>
    public static void GetSlurIndentHeight(
        out double indent, out double height, double width, double heightLimit, double ratio)
    {
        const double MaxFraction = 1.0 / 3.1;
        height = SlurHeight(width, heightLimit, ratio);

        double q = 2 * heightLimit / MaxFraction;
        indent = (2 * heightLimit) - (q * q * MaxFraction / (width + q));
    }

    /// <summary>Returns the height a slur of a given width rises to.</summary>
    /// <param name="width">The horizontal span of the slur.</param>
    /// <param name="heightLimit">The height the slur rises to asymptotically.</param>
    /// <param name="ratio">The initial height-to-width ratio.</param>
    /// <returns>The height.</returns>
    public static double SlurHeight(double width, double heightLimit, double ratio)
        => F01(width * ratio / heightLimit) * heightLimit;

    // F (x) = 2/pi * atan (pi x / 2): zero at zero, slope one at zero, one at infinity.
    private static double F01(double x) => 2 / Math.PI * Math.Atan(Math.PI * x / 2);
}
