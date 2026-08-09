/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2000--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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
using System.Globalization;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/arpeggio.cc, lily/include/arpeggio.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - upstream's one file holds THREE classes (Arpeggio, Chord_bracket, Chord_slur) and
//     so does this one, because they share the "positions" property, the squiggle-free
//     vertical-span idea and the engraver that makes all three.
//   - the arrow glyph name is built with the invariant culture: upstream's
//     std::to_string(dir) writes "1"/"-1" and a culture-sensitive format could not find
//     the glyph. See ArrowGlyphName.

/// <summary>
/// Functions and settings for drawing an arpeggio symbol: the vertical wiggle placed to
/// the left of a chord.
/// </summary>
public static class Arpeggio
{
    private static readonly Symbol StemsSymbol = Symbol.Intern("stems");
    private static readonly Symbol PositionsSymbol = Symbol.Intern("positions");
    private static readonly Symbol TransparentSymbol = Symbol.Intern("transparent");
    private static readonly Symbol ArpeggioDirectionSymbol = Symbol.Intern("arpeggio-direction");
    private static readonly Symbol CrossStaffSymbol = Symbol.Intern("cross-staff");

    /// <summary>
    /// The common vertical reference point of the arpeggio and the staff symbols of every
    /// stem it spans.
    /// </summary>
    /// <param name="me">The arpeggio.</param>
    /// <returns>The common reference point.</returns>
    public static Grob GetCommonY(Grob me)
    {
        Grob common = me;

        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, StemsSymbol);
        for (int i = 0; i < stems.Count; i++)
        {
            Grob stem = stems[i];
            common = common.CommonRefpoint(
                StaffSymbolReferencer.GetStaffSymbol(stem), Axis.Y);
        }

        return common;
    }

    /// <summary>
    /// The <c>cross-staff</c> callback: an arpeggio is cross-staff when its stems do not
    /// all live in the same vertical axis group.
    /// </summary>
    /// <param name="me">The arpeggio.</param>
    /// <returns><c>#t</c> when it spans more than one axis group.</returns>
    public static object CalcCrossStaff(Grob me)
    {
        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, StemsSymbol);
        Grob vag = null;

        for (int i = 0; i < stems.Count; i++)
        {
            if (i == 0)
            {
                vag = SidePositionInterface.GetVerticalAxisGroup(stems[i]);
            }
            else
            {
                if (vag != SidePositionInterface.GetVerticalAxisGroup(stems[i]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The <c>positions</c> callback: the vertical span the arpeggio has to cover,
    /// in staff positions relative to the arpeggio itself.
    /// </summary>
    /// <param name="me">The arpeggio.</param>
    /// <returns>The span, as a Scheme pair.</returns>
    public static object CalcPositions(Grob me)
    {
        Grob common = GetCommonY(me);

        /*
          TODO:

          Using stems here is not very convenient; should store noteheads
          instead, and also put them into the support. Now we will mess up
          in vicinity of a collision.
        */
        Interval heads = Interval.Empty;
        double myY = me.RelativeCoordinate(common, Axis.Y);

        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, StemsSymbol);
        for (int i = 0; i < stems.Count; i++)
        {
            Grob stem = stems[i];
            Grob ss = StaffSymbolReferencer.GetStaffSymbol(stem);
            Interval iv = Stem.HeadPositions(stem);
            iv *= StaffSymbolReferencer.StaffSpace(me) / 2.0;
            double staffY = ss != null ? ss.RelativeCoordinate(common, Axis.Y) : 0.0;
            heads.Unite(iv + staffY - myY);
        }

        heads *= 1 / StaffSymbolReferencer.StaffSpace(me);

        return new Pair(heads.Left, heads.Right);
    }

    /// <summary>The <c>stencil</c> callback: stacks squiggles to cover the chord.</summary>
    /// <param name="me">The arpeggio.</param>
    /// <returns>The stencil, or <c>'()</c> when the arpeggio found no note heads.</returns>
    public static object Print(Grob me)
    {
        double ss = StaffSymbolReferencer.StaffSpace(me);
        Interval heads = SchemeConvert.ToInterval(me.GetProperty(PositionsSymbol), Interval.Empty) * ss;

        if (heads.IsEmpty)
        {
            if (SchemeUtilities.ToBool(me.GetProperty(TransparentSymbol)))
            {
                /*
                  This is part of a cross-staff/-voice span-arpeggio,
                  so we need to ensure `heads' is large enough to encompass
                  a single trill-element since the span-arpeggio depends on
                  its children to prevent collisions.
                */
                heads.Unite(GetSquiggle(me).Extent(Axis.Y));
            }
            else
            {
                me.Warning("no heads for arpeggio found?");
                me.Suicide();
                return Nil.Instance;
            }
        }

        // Adjust lower position to include note head in interval.
        heads[Direction.Negative] -= 0.5;

        // Make sure that we have at least two wiggles (or a wiggle plus an arrow
        // head)
        if (heads.Length < 1.5 * ss)
        {
            heads.Widen(0.5 * ss);
        }

        object ad = me.GetProperty(ArpeggioDirectionSymbol);
        Direction dir = Direction.Center;
        if (DirectionalElementInterface.IsDirection(ad))
        {
            dir = DirectionalElementInterface.FromScheme(ad, Direction.Center);
        }

        Stencil mol = new Stencil();
        Stencil squiggle = GetSquiggle(me);

        /*
          Compensate for rounding error which may occur when a chord
          reaches the center line, resulting in an extra squiggle
          being added to the arpeggio stencil.  This value is appreciably
          larger than the rounding error, which is in the region of 1e-16
          for a global-staff-size of 20, but small enough that it does not
          interfere with smaller staff sizes.
        */
        const double Epsilon = 1e-3;

        Stencil arrow = new Stencil();
        if (dir != Direction.Center)
        {
            FontMetric fm = FontInterface.GetDefaultFont(me);
            arrow = fm.FindByName("scripts.arpeggio.arrow." + ArrowGlyphName(dir));
            heads[dir] -= (int)dir * arrow.Extent(Axis.Y).Length;
        }

        // The loop below stacks squiggles until they cover the chord. It terminates
        // because each one adds its own height — so a squiggle of ZERO height would
        // never terminate, and the symptom would be a sweep that stops making progress
        // with nothing in the log, exactly the shape EPG12's slur search range had.
        // Upstream is protected by the music font always carrying scripts.arpeggio;
        // this asks rather than assumes. When the glyph is real the guard cannot fire
        // and the expression underneath it is upstream's, unchanged.
        if (squiggle.Extent(Axis.Y).Length > 0.0)
        {
            while (mol.Extent(Axis.Y).Length + Epsilon < heads.Length)
            {
                mol.AddAtEdge(Axis.Y, Direction.Positive, squiggle, 0.0);
            }
        }
        else
        {
            me.ProgrammingError("arpeggio squiggle has no height; stacking skipped");
        }

        mol.TranslateAxis(heads[Direction.Negative], Axis.Y);
        if (dir != Direction.Center)
        {
            mol.AddAtEdge(Axis.Y, dir, arrow, 0);
        }

        return mol;
    }

    /// <summary>
    /// The <c>X-extent</c> callback. It is a callback rather than the stencil's own width
    /// because <see cref="Print"/> triggers vertical alignment when the arpeggio is
    /// cross-staff.
    /// </summary>
    /// <param name="me">The arpeggio.</param>
    /// <returns>The squiggle's horizontal extent, as a Scheme pair.</returns>
    public static object Width(Grob me)
    {
        Interval extent = GetSquiggle(me).Extent(Axis.X);
        return new Pair(extent.Left, extent.Right);
    }

    /// <summary>
    /// The <c>pure-Y-extent</c> callback: a cross-staff arpeggio has no pure height,
    /// because measuring one would force vertical alignment before line breaking.
    /// </summary>
    /// <param name="me">The arpeggio.</param>
    /// <returns>The pure height, as a Scheme pair.</returns>
    public static object PureHeight(Grob me)
    {
        if (SchemeUtilities.ToBool(me.GetProperty(CrossStaffSymbol)))
        {
            return new Pair(Interval.Empty.Left, Interval.Empty.Right);
        }

        // Grob::stencil_height, inline: the stencil's own Y extent.
        Stencil? stencil = me.GetStencil();
        Interval height = stencil.HasValue ? stencil.Value.Extent(Axis.Y) : Interval.Empty;
        return new Pair(height.Left, height.Right);
    }

    // std::to_string (dir) on a Direction, which is an int enum: "1" for UP and "-1" for
    // DOWN. The invariant culture is not decoration — a culture whose negative sign is not
    // U+002D would build a glyph name the font does not contain, and a missing glyph is
    // silent here.
    private static string ArrowGlyphName(Direction dir)
        => ((int)dir).ToString(CultureInfo.InvariantCulture);

    private static Stencil GetSquiggle(Grob me)
    {
        FontMetric fm = FontInterface.GetDefaultFont(me);
        Stencil squiggle = fm.FindByName("scripts.arpeggio");

        return squiggle;
    }
}

/// <summary>
/// Functions and settings for drawing a vertical bracket, such as for non-arpeggiato,
/// non-divisi, or optional material.
/// </summary>
public static class ChordBracket
{
    private static readonly Symbol PositionsSymbol = Symbol.Intern("positions");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol ProtrusionSymbol = Symbol.Intern("protrusion");
    private static readonly Symbol LineThicknessSymbol = Symbol.Intern("line-thickness");

    /// <summary>Makes a bracket with the given Y extent.</summary>
    /// <param name="me">The bracket grob.</param>
    /// <param name="yExtent">The vertical extent to cover.</param>
    /// <returns>The stencil.</returns>
    public static Stencil Print(Grob me, Interval yExtent)
    {
        double thickness = me.Layout.GetDimension(LineThicknessSymbol)
                           * ToDouble(me.GetProperty(ThicknessSymbol), 1.0);
        Direction side = DirectionalElementInterface.FromScheme(
            me.GetProperty(DirectionSymbol), Direction.Negative);
        double width = ToDouble(me.GetProperty(ProtrusionSymbol), 0.4);
        return Lookup.Bracket(Axis.Y, yExtent, thickness, width * -(int)side, thickness);
    }

    /// <summary>The <c>stencil</c> callback.</summary>
    /// <param name="me">The bracket grob.</param>
    /// <returns>The stencil.</returns>
    /// <remarks>
    /// Drawn to the left of a chord — Chris Jackson &lt;chris@fluffhouse.org.uk&gt;.
    /// </remarks>
    public static object Print(Grob me)
    {
        Interval yExtent = SchemeConvert.ToInterval(me.GetProperty(PositionsSymbol), Interval.Empty);
        yExtent.Widen(0.75); // candidate for a grob property
        yExtent *= StaffSymbolReferencer.StaffSpace(me);
        return Print(me, yExtent);
    }

    /// <summary>The <c>X-extent</c> callback.</summary>
    /// <param name="me">The bracket grob.</param>
    /// <returns>The horizontal extent, as a Scheme pair.</returns>
    public static object Width(Grob me)
    {
        // dummy Y extent avoids triggering vertical alignment before line breaking
        Interval yExtent = new Interval(0, 1);
        Interval xExtent = Print(me, yExtent).Extent(Axis.X);
        return new Pair(xExtent.Left, xExtent.Right);
    }

    private static double ToDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "chord-bracket")
            : fallback;
}

/// <summary>Functions and settings for drawing a vertical slur.</summary>
public static class ChordSlur
{
    private static readonly Symbol PositionsSymbol = Symbol.Intern("positions");
    private static readonly Symbol DashDefinitionSymbol = Symbol.Intern("dash-definition");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");
    private static readonly Symbol LineThicknessSymbol = Symbol.Intern("line-thickness");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol CrossStaffSymbol = Symbol.Intern("cross-staff");
    private static readonly Symbol SurrogateSymbol
        = Symbol.Intern("vertically-spanning-surrogate");

    /// <summary>Makes a vertical slur covering the given positions.</summary>
    /// <param name="me">The slur grob.</param>
    /// <param name="positions">The vertical span, in staff positions.</param>
    /// <returns>The stencil.</returns>
    public static Stencil Print(Grob me, Interval positions)
    {
        object dashDefinition = me.GetProperty(DashDefinitionSymbol);
        double ss = StaffSymbolReferencer.StaffSpace(me);
        Interval heads = positions * StaffSymbolReferencer.StaffSpace(me);

        double lt = me.Layout.GetDimension(LineThicknessSymbol)
                    * ToDouble(me.GetProperty(LineThicknessSymbol), 1.0);
        double th = me.Layout.GetDimension(LineThicknessSymbol)
                    * ToDouble(me.GetProperty(ThicknessSymbol), 1.0);
        Direction side = DirectionalElementInterface.FromScheme(
            me.GetProperty(DirectionSymbol), Direction.Negative);

        // Adjust lower position to include note head in interval.
        heads[Direction.Negative] -= 0.5;

        // Avoid too short chord slurs for small intervals.
        if (heads.Length < 1.5 * ss)
        {
            heads.Widen(0.5 * ss);
        }
        else if (heads.Length < 2 * ss)
        {
            heads.Widen(0.25 * ss);
        }

        double sp = 0.5 * ss;
        double dy = heads.Length - sp;

        double heightLimit = 1.5;
        double ratio = .33;
        Bezier curve = BezierBow.SlurShape(dy, heightLimit, ratio * -(int)side);
        curve.Rotate(90.0);

        Stencil mol = Lookup.Slur(curve, th, lt, dashDefinition);
        mol.TranslateAxis(heads[Direction.Negative] + 1.5 * sp / 2.0, Axis.Y);
        return mol;
    }

    /// <summary>The <c>stencil</c> callback.</summary>
    /// <param name="me">The slur grob.</param>
    /// <returns>The stencil.</returns>
    public static object Print(Grob me)
    {
        Interval positions = SchemeConvert.ToInterval(
            me.GetProperty(PositionsSymbol), Interval.Empty);
        return Print(me, positions);
    }

    /// <summary>The <c>X-extent</c> callback.</summary>
    /// <param name="me">The slur grob.</param>
    /// <returns>The horizontal extent, as a Scheme pair.</returns>
    public static object Width(Grob me)
    {
        // A surrogate slur appears instead of this one, but does not control
        // horizontal spacing itself.
        Grob surrogate = me.GetObject(SurrogateSymbol) as Grob;

        // Get the width from this grob's stencil if we're not in a cross-staff
        // situation.  Making a cross-staff stencil here would trigger vertical
        // alignment before line breaking.
        if (surrogate == null && !SchemeUtilities.ToBool(me.GetProperty(CrossStaffSymbol)))
        {
            return StencilWidth(me);
        }

        // If a surrogate slur isn't cross-staff (it might be just cross-voice), then
        // it should have no trouble calculating its actual width.
        if (surrogate != null
            && !SchemeUtilities.ToBool(surrogate.GetProperty(CrossStaffSymbol)))
        {
            return StencilWidth(surrogate);
        }

        // We're in a cross-staff situation.  If this slur is cross-staff, we can't
        // make its stencil to get its actual width, and if this slur is not
        // cross-staff, its extent is probably not a good estimate of the extent of
        // the surrogate.  Using a dummy height avoids triggering vertical alignment
        // before line breaking.  We use a large value to aim for the worst case,
        // expecting the stencil code to limit the curvature of the slur.
        Interval positions = new Interval(0, 100);
        Interval xExtent = Print(me, positions).Extent(Axis.X);
        return new Pair(xExtent.Left, xExtent.Right);
    }

    // Grob::stencil_width, inline: the stencil's own X extent.
    private static object StencilWidth(Grob grob)
    {
        Stencil? stencil = grob.GetStencil();
        Interval extent = stencil.HasValue ? stencil.Value.Extent(Axis.X) : Interval.Empty;
        return new Pair(extent.Left, extent.Right);
    }

    private static double ToDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "chord-slur")
            : fallback;
}
