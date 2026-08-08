/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2001--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/accidental.cc, lily/include/accidental-interface.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// A single accidental: the sharp, flat or natural drawn left of a note head.
/// <para>
/// The interesting halves are the TIE protocol — an accidental on the right half of a
/// tie is a courtesy reminder that <see cref="RemoveTied"/> deletes unless a line break
/// makes it worth keeping — and the skyline tweak in <see cref="HorizontalSkylines"/>
/// that pads a flat's stem so flats pack against double flats the way an engraver would
/// put them.
/// </para>
/// </summary>
public static class AccidentalInterface
{
    private static readonly Symbol StencilSymbol = Symbol.Intern("stencil");
    private static readonly Symbol RotationSymbol = Symbol.Intern("rotation");
    private static readonly Symbol ParenthesizedSymbol = Symbol.Intern("parenthesized");
    private static readonly Symbol GlyphNameSymbol = Symbol.Intern("glyph-name");
    private static readonly Symbol TieSymbol = Symbol.Intern("tie");
    private static readonly Symbol ForcedSymbol = Symbol.Intern("forced");
    private static readonly Symbol HideTiedAfterBreakSymbol
        = Symbol.Intern("hide-tied-accidental-after-break");

    private static readonly Symbol RestoreFirstSymbol = Symbol.Intern("restore-first");

    /// <summary>
    /// Wraps a stencil in the music font's accidental parentheses.
    /// </summary>
    /// <param name="me">The grob whose font supplies the parentheses.</param>
    /// <param name="m">The stencil to wrap.</param>
    /// <returns>The wrapped stencil.</returns>
    public static Stencil Parenthesize(Grob me, Stencil m)
    {
        FontMetric font = FontInterface.GetDefaultFont(me);
        Stencil open = font.FindByName("accidentals.leftparen");
        Stencil close = font.FindByName("accidentals.rightparen");

        m.AddAtEdge(Axis.X, Direction.Negative, open, 0);
        m.AddAtEdge(Axis.X, Direction.Positive, close, 0);

        return m;
    }

    /// <summary>
    /// The <c>horizontal-skylines</c> callback: the accidental's own drawing as a
    /// skyline pair, with extra padding right of a flat's stem.
    /// <para>
    /// Upstream's comment, kept because it explains a number that looks arbitrary: the
    /// stem is raised horizontally to a bit less than the average horizontal "height"
    /// of the entire glyph, which brings flats closer to double flats — an aesthetic
    /// choice (MS opinion) that works for any font where the flat is not completely
    /// bizarre.
    /// </para>
    /// </summary>
    /// <param name="me">The accidental.</param>
    /// <returns>The skyline pair.</returns>
    public static SkylinePair HorizontalSkylines(Grob me)
    {
        if (!me.IsLive)
        {
            return new SkylinePair();
        }

        Stencil? myStencil = me.GetProperty(StencilSymbol) is Stencil s ? s : (Stencil?)null;
        if (!myStencil.HasValue)
        {
            return new SkylinePair();
        }

        SkylinePair sky = StencilIntegral.SkylinesFromStencil(
            myStencil, me.GetProperty(RotationSymbol), Axis.Y);

        object parenthesized = me.GetProperty(ParenthesizedSymbol);

        string glyphName = me.GetProperty(GlyphNameSymbol) is MutableString text
            ? text.ToString()
            : string.Empty;

        if ((glyphName == "accidentals.flat"
             || glyphName == "accidentals.flatflat")
            && !SchemeUtilities.ToBool(parenthesized))
        {
            // a bit more padding for the right of the stem
            double left = myStencil.Value.Extent(Axis.X)[Direction.Negative];
            double right = myStencil.Value.Extent(Axis.X)[Direction.Positive] * 0.375;
            List<Box> boxes = new List<Box>
            {
                new Box(new Interval(left, right), myStencil.Value.Extent(Axis.Y)),
            };
            Skyline mergeWithMe = new Skyline(boxes, Axis.Y, Direction.Positive);
            sky[Direction.Positive].Merge(mergeWithMe);
        }

        return sky;
    }

    /// <summary>
    /// The <c>Y-extent</c> callback: empty when the accidental belongs to a tied note
    /// and is due to be hidden, the stencil's height otherwise.
    /// </summary>
    /// <param name="me">The accidental.</param>
    /// <returns>The vertical extent.</returns>
    public static Interval Height(Grob me)
    {
        Grob tie = me.GetObject(TieSymbol) as Grob;

        if (tie != null
            && !SchemeUtilities.ToBool(me.GetProperty(ForcedSymbol))
            && SchemeUtilities.ToBool(me.GetProperty(HideTiedAfterBreakSymbol)))
        {
            return Interval.Empty;
        }

        // Grob::stencil_height, inline: the stencil's own Y extent.
        Stencil? stencil = me.GetStencil();
        return stencil.HasValue ? stencil.Value.Extent(Axis.Y) : Interval.Empty;
    }

    /// <summary>
    /// The <c>before-line-breaking</c> callback: deletes the courtesy accidental on the
    /// right half of a tie, unless it was forced or the tie was actually broken across
    /// a line (an unbroken tie has no original).
    /// </summary>
    /// <param name="me">The accidental.</param>
    public static void RemoveTied(Grob me)
    {
        Grob tie = me.GetObject(TieSymbol) as Grob;

        if (tie != null
            && !SchemeUtilities.ToBool(me.GetProperty(ForcedSymbol))
            && (SchemeUtilities.ToBool(me.GetProperty(HideTiedAfterBreakSymbol))
                || tie.Original == null))
        {
            me.Suicide();
        }
    }

    /// <summary>
    /// The <c>stencil</c> callback: the glyph named by <c>glyph-name</c>, with a natural
    /// prefixed when <c>restore-first</c> asks for one and parentheses when
    /// <c>parenthesized</c> does.
    /// </summary>
    /// <param name="me">The accidental.</param>
    /// <returns>The stencil.</returns>
    public static object Print(Grob me)
    {
        FontMetric fm = FontInterface.GetDefaultFont(me);
        string glyphName = me.GetProperty(GlyphNameSymbol) is MutableString text
            ? text.ToString()
            : string.Empty;
        Stencil st = fm.FindByName(glyphName);
        if (st.IsEmpty)
        {
            Warn.Warning("cannot find glyph " + glyphName);
        }

        if (SchemeUtilities.ToBool(me.GetProperty(RestoreFirstSymbol)))
        {
            /*
              this isn't correct for ancient accidentals, but they don't
              use double flats/sharps anyway.
              */
            Stencil acc = fm.FindByName("accidentals.natural");

            if (acc.IsEmpty)
            {
                Warn.Warning("natural alteration glyph not found");
            }
            else
            {
                st.AddAtEdge(Axis.X, Direction.Negative, acc, 0.1);
            }
        }

        if (SchemeUtilities.ToBool(me.GetProperty(ParenthesizedSymbol)))
        {
            st = Parenthesize(me, st);
        }

        return st;
    }
}
