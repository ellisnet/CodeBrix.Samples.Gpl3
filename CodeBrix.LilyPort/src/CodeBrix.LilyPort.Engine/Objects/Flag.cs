/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
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

// FIXME: there is a whole reimplementation of this in Scheme, in
// flag-styles.scm.  It's more flexible, so why do we still have this?
// Shouldn't this be deleted altogether?

using System;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/flag.cc;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// The flag on an unbeamed eighth note or shorter: a music-font glyph named after the
/// style, the stem's direction and the duration — <c>flags.u3</c>,
/// <c>flags.mensurald06</c> and so on.
/// </summary>
public static class Flag
{
    private static readonly Symbol StencilSymbol = Symbol.Intern("stencil");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol GlyphNameSymbol = Symbol.Intern("glyph-name");
    private static readonly Symbol StrokeStyleSymbol = Symbol.Intern("stroke-style");
    private static readonly Symbol BlotDiameterSymbol = Symbol.Intern("blot-diameter");

    /// <summary>
    /// The <c>X-extent</c> callback: the flag's width beyond the stem's right edge.
    /// </summary>
    /// <param name="me">The flag.</param>
    /// <returns>The extent.</returns>
    public static Interval Width(Grob me)
    {
        if (!(me.GetProperty(StencilSymbol) is Stencil sten))
        {
            return new Interval(0.0, 0.0);
        }

        Grob stem = me.XParent;

        /*
          TODO:
          This reproduces a bad hard-coding that has been in the code for quite some time:
          the bounding boxes for the flags are slightly off and need to be fixed.
        */

        return sten.Extent(Axis.X) - stem.Extent(stem, Axis.X)[Direction.Positive];
    }

    /// <summary>
    /// The <c>glyph-name</c> callback: assembles the font glyph name from the style,
    /// the stem direction and the duration log.
    /// </summary>
    /// <param name="me">The flag.</param>
    /// <returns>The glyph name.</returns>
    public static object GlyphName(Grob me)
    {
        Grob stem = me.XParent;

        Direction d = Stem.GetGrobDirection(stem);
        int log = Stem.DurationLog(stem);
        string flagStyle = string.Empty;

        object flagStyleScm = me.GetProperty(StyleSymbol);
        if (flagStyleScm is Symbol styleSymbol)
        {
            flagStyle = styleSymbol.Name;
        }

        bool adjust = true;

        string stafflineOffs;
        if (flagStyle == "mensural")
        {
            /* Mensural notation: For notes on staff lines, use different
               flags than for notes between staff lines.  The idea is that
               flags are always vertically aligned with the staff lines,
               regardless if the note head is on a staff line or between two
               staff lines.  In other words, the inner end of a flag always
               touches a staff line.
            */
            if (adjust)
            {
                double ss = StaffSymbolReferencer.StaffSpace(me);
                int p = (int)Math.Round(
                    stem.Extent(stem, Axis.Y)[d] * 2 / ss, MidpointRounding.ToEven);
                stafflineOffs
                    = StaffSymbolReferencer.OnLine(stem, p) ? "0" : "1";
            }
            else
            {
                stafflineOffs = "2";
            }
        }
        else
        {
            stafflineOffs = string.Empty;
        }

        char dir = d == Direction.Positive ? 'u' : 'd';
        string fontChar
            = flagStyle + dir + stafflineOffs + log.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        return new MutableString("flags." + fontChar);
    }

    /// <summary>The <c>stencil</c> callback: the flag glyph, plus any grace stroke.</summary>
    /// <param name="me">The flag.</param>
    /// <returns>The stencil.</returns>
    public static object Print(Grob me)
    {
        Grob stem = me.XParent;

        Direction d = Stem.GetGrobDirection(stem);
        string flagStyle = string.Empty;

        object flagStyleScm = me.GetProperty(StyleSymbol);
        if (flagStyleScm is Symbol styleSymbol)
        {
            flagStyle = styleSymbol.Name;
        }

        if (flagStyle == "no-flag")
        {
            return Stencil.Empty;
        }

        char dir = d == Direction.Positive ? 'u' : 'd';
        FontMetric fm = FontInterface.GetDefaultFont(me);
        object glyphValue = me.GetProperty(GlyphNameSymbol);
        string fontChar = glyphValue is MutableString text ? text.ToString() : string.Empty;
        Stencil flag = fm != null ? fm.FindByName(fontChar) : Stencil.Empty;
        if (flag.IsEmpty)
        {
            Warn.Warning("flag `" + fontChar + "' not found");
        }

        /*
          TODO: maybe property stroke-style should take different values,
          e.g. "" (i.e. no stroke), "single" and "double" (currently, it's
          '() or "grace").  */
        object strokeStyleScm = me.GetProperty(StrokeStyleSymbol);
        string strokeStyleValue = strokeStyleScm is MutableString strokeText
            ? strokeText.ToString()
            : strokeStyleScm as string;
        if (strokeStyleValue != null)
        {
            string strokeStyle = strokeStyleValue;
            if (!string.IsNullOrEmpty(strokeStyle) && fm != null)
            {
                string strokeChar = flagStyle + dir + strokeStyle;
                Stencil stroke = fm.FindByName("flags." + strokeChar);
                if (stroke.IsEmpty)
                {
                    strokeChar = dir + strokeStyle;
                    stroke = fm.FindByName("flags." + strokeChar);
                }

                if (stroke.IsEmpty)
                {
                    Warn.Warning("flag stroke `" + strokeChar + "' not found");
                }
                else
                {
                    flag.AddStencil(stroke);
                }
            }
        }

        return flag;
    }

    /// <summary>The <c>Y-offset</c> callback's pure half.</summary>
    /// <param name="me">The flag.</param>
    /// <returns>The offset.</returns>
    public static double PureCalcYOffset(Grob me)
        => InternalCalcYOffset(me, true);

    /// <summary>The <c>Y-offset</c> callback: the flag sits at the stem's end.</summary>
    /// <param name="me">The flag.</param>
    /// <returns>The offset.</returns>
    public static double CalcYOffset(Grob me)
        => InternalCalcYOffset(me, false);

    private static double InternalCalcYOffset(Grob me, bool pure)
    {
        Grob stem = me.XParent;
        Direction d = Stem.GetGrobDirection(stem);

        double blot = me.Layout == null ? 0.0 : me.Layout.GetDimension(BlotDiameterSymbol);

        // Upstream's pure branch reads stem->pure_y_extent; the pure machinery is
        // EPG15's, and the ordinary extent is its recorded fallback.
        Interval stemExtent = pure
            ? Stem.PureYExtent(stem)
            : stem.Extent(stem, Axis.Y);

        return stemExtent.IsEmpty ? 0.0 : stemExtent[d] - d.Value * blot / 2;
    }

    /// <summary>The <c>X-offset</c> callback: the stem's right edge.</summary>
    /// <param name="me">The flag.</param>
    /// <returns>The offset.</returns>
    public static double CalcXOffset(Grob me)
    {
        Grob stem = me.XParent;
        return stem.Extent(stem, Axis.X)[Direction.Positive];
    }
}
