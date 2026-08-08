/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2001--2026  Han-Wen Nienhuys <hanwen@xs4all.nl>

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

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/percent-repeat-interface.cc;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// Repeats that look like percent signs: one slash for a beat, a slash between two dots
/// for a measure, and a wider pair for two measures.
/// </summary>
public static class PercentRepeatInterface
{
    private static readonly Symbol DotNegativeKernSymbol = Symbol.Intern("dot-negative-kern");
    private static readonly Symbol SlashCountSymbol = Symbol.Intern("slash-count");
    private static readonly Symbol SlashNegativeKernSymbol = Symbol.Intern("slash-negative-kern");
    private static readonly Symbol SlopeSymbol = Symbol.Intern("slope");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");

    /// <summary>The <c>stencil</c> callback for a one-measure percent sign.</summary>
    /// <param name="me">The grob.</param>
    /// <returns>The percent stencil.</returns>
    public static Stencil Percent(Grob me) => XPercent(me, 1);

    /// <summary>The <c>stencil</c> callback for a two-measure percent sign.</summary>
    /// <param name="me">The grob.</param>
    /// <returns>The double-percent stencil, centred horizontally.</returns>
    public static Stencil DoublePercent(Grob me)
    {
        Stencil m = XPercent(me, 2);
        m.AlignTo(Axis.X, 0.0);
        return m;
    }

    /// <summary>The <c>stencil</c> callback for a beat slash.</summary>
    /// <param name="me">The grob, whose cause carries the slash count.</param>
    /// <returns>The slash stencil.</returns>
    public static Stencil BeatSlash(Grob me)
    {
        StreamEvent cause = me.EventCause();
        long count = ReadLong(cause?.GetProperty(SlashCountSymbol), 1);

        return count == 0 ? XPercent(me, 2) : BrewSlash(me, (int)count);
    }

    /// <summary>Builds a run of slashes, kerned together.</summary>
    /// <param name="me">The grob, read for slope, thickness and kerning.</param>
    /// <param name="count">How many slashes.</param>
    /// <returns>The slash stencil, vertically centred.</returns>
    public static Stencil BrewSlash(Grob me, int count)
    {
        // Scale everything by staff-space, don't scale thickness by line-thickness. The
        // reason is that line-thickness is more to control the thickness of thin lines,
        // which should not get too thin with small staff sizes. Consequently, staff-space
        // and line-thickness are not always proportional. However, percent repeat signs
        // should have the same proportions at all staff sizes.
        double staffSpace = StaffSymbolReferencer.StaffSpace(me);
        double slope = ReadDouble(me.GetProperty(SlopeSymbol), 1);
        double width = 2.0 / slope * staffSpace;
        double thickness = ReadDouble(me.GetProperty(ThicknessSymbol), 1) * staffSpace;

        Stencil slash = Lookup.RepeatSlash(width, slope, thickness);
        Stencil m = slash;

        double slashNegativeKern
            = ReadDouble(me.GetProperty(SlashNegativeKernSymbol), 1.6) * staffSpace;

        for (int i = count - 1; i > 0; i--)
        {
            m.AddAtEdge(Axis.X, Direction.Positive, slash, -slashNegativeKern);
        }

        m.AlignTo(Axis.Y, 0.0);
        return m;
    }

    /// <summary>Builds a slash run flanked by the two percent dots.</summary>
    /// <param name="me">The grob.</param>
    /// <param name="count">How many slashes.</param>
    /// <returns>The percent stencil.</returns>
    public static Stencil XPercent(Grob me, int count)
    {
        double staffSpace = StaffSymbolReferencer.StaffSpace(me);
        Stencil m = BrewSlash(me, count);

        double dotNegativeKern
            = ReadDouble(me.GetProperty(DotNegativeKernSymbol), 0.75) * staffSpace;

        Stencil d1 = FontInterface.GetDefaultFont(me).FindByName("dots.dot");
        Stencil d2 = d1;
        d1.TranslateAxis(0.5 * staffSpace, Axis.Y);
        d2.TranslateAxis(-0.5 * staffSpace, Axis.Y);

        m.AddAtEdge(Axis.X, Direction.Negative, d1, -dotNegativeKern);
        m.AddAtEdge(Axis.X, Direction.Positive, d2, -dotNegativeKern);
        return m;
    }

    private static double ReadDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "percent-repeat-interface")
            : fallback;

    private static long ReadLong(object value, long fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToLong(value, "percent-repeat-interface")
            : fallback;
}
