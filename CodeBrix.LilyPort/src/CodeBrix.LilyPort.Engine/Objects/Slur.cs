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

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/slur.cc, lily/include/slur.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - `Slur` is a PARTIAL static class: upstream defines Slur::calc_control_points in
//     slur-scoring.cc rather than here, and the port keeps that split so each file's
//     `//was previously:` line stays true.
//   - slur.hh DECLARES Slur::vertical_skylines and nothing anywhere DEFINES it — no
//     definition in lily/, no reference from scm/ or ly/. It is a dangling declaration
//     upstream, so it is NOT an entry point and the port creates no binding for it.

/// <summary>
/// A slur: the curve over or under a run of notes, placed by trying a field of candidate
/// endpoints and keeping the one with the lowest demerits.
/// </summary>
public static partial class Slur
{
    private static readonly Symbol NoteColumnsSymbol = Symbol.Intern("note-columns");
    private static readonly Symbol EncompassObjectsSymbol = Symbol.Intern("encompass-objects");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol AvoidSlurSymbol = Symbol.Intern("avoid-slur");
    private static readonly Symbol SlurObjectSymbol = Symbol.Intern("slur");
    private static readonly Symbol ControlPointsSymbol = Symbol.Intern("control-points");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");
    private static readonly Symbol LineThicknessSymbol = Symbol.Intern("line-thickness");
    private static readonly Symbol DashDefinitionSymbol = Symbol.Intern("dash-definition");
    private static readonly Symbol AnnotationSymbol = Symbol.Intern("annotation");
    private static readonly Symbol SlurPaddingSymbol = Symbol.Intern("slur-padding");
    private static readonly Symbol CrossStaffSymbol = Symbol.Intern("cross-staff");
    private static readonly Symbol InsideSymbol = Symbol.Intern("inside");
    private static readonly Symbol OutsideSymbol = Symbol.Intern("outside");
    private static readonly Symbol AroundSymbol = Symbol.Intern("around");
    private static readonly Symbol IgnoreSymbol = Symbol.Intern("ignore");
    private static readonly Symbol SeparationItemInterface
        = Symbol.Intern("separation-item-interface");
    private static readonly Symbol TieInterfaceSymbol = Symbol.Intern("tie-interface");
    private static readonly Symbol OutsideSlurCallbackSymbol
        = Symbol.Intern("ly:slur::outside-slur-callback");
    private static readonly Symbol PureOutsideSlurCallbackSymbol
        = Symbol.Intern("ly:slur::pure-outside-slur-callback");
    private static readonly Symbol OutsideSlurCrossStaffSymbol
        = Symbol.Intern("ly:slur::outside-slur-cross-staff");

    private static readonly Direction[] BothDirections
        = { Direction.Negative, Direction.Positive };

    private static readonly Axis[] BothAxesForSlur = { Axis.X, Axis.Y };

    /// <summary>The <c>direction</c> callback: up unless every column already points up.</summary>
    /// <param name="me">The slur.</param>
    /// <returns>The direction, or <see langword="false"/> when the slur died.</returns>
    public static object CalcDirection(Grob me)
    {
        IReadOnlyList<Grob> encompasses
            = PointerGroupInterface.ExtractGrobSet(me, NoteColumnsSymbol);

        if (encompasses.Count == 0)
        {
            me.Suicide();
            return false;
        }

        Direction d = Direction.Negative;
        foreach (Grob col in encompasses)
        {
            if (!NoteColumn.HasRests(col) && NoteColumn.Dir(col) == Direction.Negative)
            {
                d = Direction.Positive;
                break;
            }
        }

        return (long)(int)d;
    }

    /// <summary>Estimates the slur's height before the real curve exists.</summary>
    /// <remarks>
    /// The estimate adds a flat 0.5 to the highest encompassed note head, which is in most
    /// cases SHORTER than the actual slur. Upstream's own list of ways to improve it:
    /// extra height for scripts that avoid slurs on the inside, and extra height for the
    /// bulge above a note head.
    /// </remarks>
    /// <param name="me">The slur.</param>
    /// <param name="start">The first column rank of the line being considered.</param>
    /// <param name="end">The last column rank of the line being considered.</param>
    /// <returns>The estimated vertical extent.</returns>
    public static Interval PureHeight(Grob me, int start, int end)
    {
        Direction dir = DirectionalElementInterface.GetGrobDirection(me);

        IReadOnlyList<Grob> encompasses
            = PointerGroupInterface.ExtractGrobSet(me, NoteColumnsSymbol);
        Interval ret = Interval.Empty;

        Grob parent = me.YParent;
        DrulArray<double> extremalHeights = new DrulArray<double>(
            double.PositiveInfinity, double.NegativeInfinity);
        if (AxisGroupInterface.CommonRefpointOfArray(encompasses, me, Axis.Y) != parent)
        {
            /* this could happen if, for example, we are a cross-staff slur.
               in this case, we want to be ignored */
            return Interval.Empty;
        }

        for (int i = 0; i < encompasses.Count; i++)
        {
            Interval d = encompasses[i].PureYExtent(parent, start, end);
            if (!d.IsEmpty)
            {
                ret.AddPoint(d[dir]);

                if (extremalHeights[Direction.Negative] == double.PositiveInfinity)
                {
                    extremalHeights[Direction.Negative] = d[dir];
                }

                extremalHeights[Direction.Positive] = d[dir];
            }
        }

        if (ret.IsEmpty)
        {
            return Interval.Empty;
        }

        Interval extremalSpan = Interval.Empty;
        foreach (Direction d in BothDirections)
        {
            extremalSpan.AddPoint(extremalHeights[d]);
        }

        ret[-dir] = Direction.MinMax(dir, extremalSpan[-dir], ret[-dir]);

        /*
          The +0.5 comes from the fact that we try to place a slur
          0.5 staff spaces from the note-head.
          (see Slur_score_state.get_base_attachments ())
        */
        ret.Translate(0.5 * (int)dir);
        return ret;
    }

    /// <summary>The <c>Y-extent</c> callback: the drawn stencil's extent.</summary>
    /// <param name="me">The slur.</param>
    /// <returns>The extent.</returns>
    public static Interval Height(Grob me)
    {
        // FIXME uncached
        Stencil? m = me.GetStencil();
        return m.HasValue ? m.Value.Extent(Axis.Y) : Interval.Empty;
    }

    /// <summary>Draws the slur.</summary>
    /// <param name="me">The slur.</param>
    /// <returns>The stencil, or the empty list when the slur died.</returns>
    public static object Print(Grob me)
    {
        IReadOnlyList<Grob> encompasses
            = PointerGroupInterface.ExtractGrobSet(me, NoteColumnsSymbol);
        if (encompasses.Count == 0)
        {
            me.Suicide();
            return Nil.Instance;
        }

        double staffThick = StaffSymbolReferencer.LineThickness(me);
        double baseThick = staffThick * ReadRealOr(me.GetProperty(ThicknessSymbol), 1);
        double lineThick = staffThick * ReadRealOr(me.GetProperty(LineThicknessSymbol), 1);

        Bezier one = GetCurve(me);
        Stencil a;

        object dashDefinition = me.GetProperty(DashDefinitionSymbol);
        a = Lookup.Slur(
            one,
            (int)DirectionalElementInterface.GetGrobDirection(me) * baseThick,
            lineThick,
            dashDefinition);

        object annotation = me.GetProperty(AnnotationSymbol);

        //was previously: `annotation is string`, which is not scm_is_string here.
        if (SchemeUtilities.IsString(annotation))
        {
            Stencil tm = TextInterface.GrobInterpretMarkup(me, annotation);
            a.AddAtEdge(Axis.Y, DirectionalElementInterface.GetGrobDirection(me), tm, 1.0);
        }

        return a;
    }

    /// <summary>
    /// Replaces any separation item in the encompass list with the grobs inside it that
    /// belong to this slur's own staff and ask to be avoided.
    /// </summary>
    /// <remarks>
    /// Upstream's own note: it would be better to do this at engraver level, but that is
    /// fragile, because breakable items are generated at staff level, by which point slur
    /// starts and ends would have to be tracked.
    /// </remarks>
    /// <param name="me">The slur.</param>
    public static void ReplaceBreakableEncompassObjects(Grob me)
    {
        IReadOnlyList<Grob> extraObjects
            = PointerGroupInterface.ExtractGrobSet(me, EncompassObjectsSymbol);
        List<Grob> newEncompasses = new List<Grob>();

        for (int i = 0; i < extraObjects.Count; i++)
        {
            Grob g = extraObjects[i];

            if (g.HasInterface(SeparationItemInterface))
            {
                IReadOnlyList<Grob> breakables
                    = PointerGroupInterface.ExtractGrobSet(g, ElementsSymbol);
                for (int j = 0; j < breakables.Count; j++)
                {
                    /* if we encompass a separation-item that spans multiple staves,
                       we filter out the grobs that don't belong to our staff */
                    if (ReferenceEquals(me.CommonRefpoint(breakables[j], Axis.Y), me.YParent)
                        && ReferenceEquals(
                            breakables[j].GetProperty(AvoidSlurSymbol), InsideSymbol))
                    {
                        newEncompasses.Add(breakables[j]);
                    }
                }
            }
            else
            {
                newEncompasses.Add(g);
            }
        }

        if (me.GetObject(EncompassObjectsSymbol) is GrobArray a)
        {
            a.SetArray(newEncompasses);
        }
    }

    /// <summary>Returns the slur's drawn curve.</summary>
    /// <param name="me">The slur.</param>
    /// <returns>The curve.</returns>
    public static Bezier GetCurve(Grob me)
    {
        object cp = me.GetProperty(ControlPointsSymbol);
        Bezier b = new Bezier();
        for (int i = 0; i < Bezier.ControlCount; i++)
        {
            if (cp is Pair pair)
            {
                b[i] = ReadOffset(pair.Car);
                cp = pair.Cdr;
            }
            else
            {
                b[i] = new Offset(0.0, 0.0);
            }
        }

        return b;
    }

    /// <summary>Adds a note column for the slur to cover.</summary>
    /// <param name="me">The slur.</param>
    /// <param name="n">The note column.</param>
    public static void AddColumn(Spanner me, Grob n)
    {
        PointerGroupInterface.AddGrob(me, NoteColumnsSymbol, n);
        Spanner.AddBoundItem(me, n);
    }

    /// <summary>Adds a grob other than a note column for the slur to take account of.</summary>
    /// <param name="me">The slur.</param>
    /// <param name="n">The grob.</param>
    public static void AddExtraEncompass(Spanner me, Grob n)
        => PointerGroupInterface.AddGrob(me, EncompassObjectsSymbol, n);

    /// <summary>The pure form of <see cref="OutsideSlurCallback"/>.</summary>
    /// <param name="script">The grob being placed outside the slur.</param>
    /// <param name="start">The first column rank of the line being considered.</param>
    /// <param name="end">The last column rank of the line being considered.</param>
    /// <param name="offsetScm">The offset the callback chain has reached so far.</param>
    /// <returns>The adjusted offset.</returns>
    public static object PureOutsideSlurCallback(
        Grob script, int start, int end, object offsetScm)
    {
        Grob slur = script.GetObject(SlurObjectSymbol) as Grob;
        if (slur == null)
        {
            return offsetScm;
        }

        object avoid = script.GetProperty(AvoidSlurSymbol);
        if (!ReferenceEquals(avoid, OutsideSymbol) && !ReferenceEquals(avoid, AroundSymbol))
        {
            return offsetScm;
        }

        double offset = ReadRealOr(offsetScm, 0.0);
        Direction dir = DirectionalElementInterface.GetGrobDirection(script);
        return offset + ((int)dir * slur.PureYExtent(slur, start, end).Length / 4);
    }

    /// <summary>
    /// Pushes a grob that asks to sit outside or around the slur clear of the curve.
    /// </summary>
    /// <param name="script">The grob being placed.</param>
    /// <param name="offsetScm">The offset the callback chain has reached so far.</param>
    /// <returns>The adjusted offset.</returns>
    public static object OutsideSlurCallback(Grob script, object offsetScm)
    {
        Grob slur = script.GetObject(SlurObjectSymbol) as Grob;

        if (slur == null)
        {
            return offsetScm;
        }

        object avoid = script.GetProperty(AvoidSlurSymbol);
        if (!ReferenceEquals(avoid, OutsideSymbol) && !ReferenceEquals(avoid, AroundSymbol))
        {
            return offsetScm;
        }

        Direction dir = DirectionalElementInterface.GetGrobDirection(script);
        if (dir == Direction.Center)
        {
            return offsetScm;
        }

        Grob cx = script.CommonRefpoint(slur, Axis.X);
        Grob cy = script.CommonRefpoint(slur, Axis.Y);

        Bezier curve = GetCurve(slur);

        curve.Translate(new Offset(
            slur.RelativeCoordinate(cx, Axis.X), slur.RelativeCoordinate(cy, Axis.Y)));

        Interval yext = LooseColumns.RobustRelativeExtent(script, cy, Axis.Y);
        Interval xext = LooseColumns.RobustRelativeExtent(script, cx, Axis.X);
        Interval slurWid = new Interval(curve[0][Axis.X], curve[3][Axis.X]);

        /*
          cannot use is_empty because some 0-extent scripts
          come up with TabStaffs.
        */
        if (xext.Length <= 0 || yext.Length <= 0)
        {
            return offsetScm;
        }

        bool contains = false;
        foreach (Direction d in BothDirections)
        {
            contains |= slurWid.Contains(xext[d]);
        }

        if (!contains)
        {
            return offsetScm;
        }

        double offset = ReadRealOr(offsetScm, 0);
        yext.Translate(offset);

        /* FIXME: slur property, script property?  */
        double slurPadding = ReadRealOr(script.GetProperty(SlurPaddingSymbol), 0.0);
        yext.Widen(slurPadding);

        Interval[] exts = { xext, yext };
        bool doShift = false;
        const double Eps = 1.0e-5;
        if (ReferenceEquals(avoid, OutsideSymbol))
        {
            foreach (Direction d in BothDirections)
            {
                double x = Direction.MinMax(
                    -d, xext[d], curve[d == Direction.Negative ? 0 : 3][Axis.X] + (-(int)d * Eps));
                double y = curve.GetOtherCoordinate(Axis.X, x);
                doShift = y == Direction.MinMax(dir, yext[-dir], y);
                if (doShift)
                {
                    break;
                }
            }
        }
        else
        {
            foreach (Axis a in BothAxesForSlur)
            {
                foreach (Direction d in BothDirections)
                {
                    List<double> coords = curve.GetOtherCoordinates(a, exts[(int)a][d]);
                    for (int i = 0; i < coords.Count; i++)
                    {
                        doShift = exts[(int)Axes.Other(a)].Contains(coords[i]);
                        if (doShift)
                        {
                            break;
                        }
                    }

                    if (doShift)
                    {
                        break;
                    }
                }

                if (doShift)
                {
                    break;
                }
            }
        }

        double avoidanceOffset = doShift
            ? curve.MinMax(
                  Axis.X,
                  Math.Max(xext[Direction.Negative], curve[0][Axis.X] + Eps),
                  Math.Min(xext[Direction.Positive], curve[3][Axis.X] - Eps),
                  dir)
              - yext[-dir]
            : 0.0;

        return offset + avoidanceOffset;
    }

    /// <summary>
    /// Wires a grob that sits outside or around the slur into the slur's callbacks.
    /// </summary>
    /// <remarks>Used by both <c>Slur_engraver</c> and <c>Phrasing_slur_engraver</c>.</remarks>
    /// <param name="e">The grob.</param>
    /// <param name="slurs">The slurs currently open.</param>
    /// <param name="endSlurs">The slurs ending this timestep.</param>
    public static void AuxiliaryAcknowledgeExtraObject(
        Grob e, List<Spanner> slurs, List<Spanner> endSlurs)
    {
        if (slurs.Count == 0 && endSlurs.Count == 0)
        {
            return;
        }

        object avoid = e.GetProperty(AvoidSlurSymbol);
        Spanner slur;
        if (endSlurs.Count > 0 && slurs.Count == 0)
        {
            slur = endSlurs[0];
        }
        else
        {
            slur = slurs[0];
        }

        if (e.HasInterface(TieInterfaceSymbol) || ReferenceEquals(avoid, InsideSymbol))
        {
            for (int i = slurs.Count; i-- > 0;)
            {
                AddExtraEncompass(slurs[i], e);
            }

            for (int i = endSlurs.Count; i-- > 0;)
            {
                AddExtraEncompass(endSlurs[i], e);
            }

            if (slur != null)
            {
                e.SetObject(SlurObjectSymbol, slur);
            }
        }
        else if (ReferenceEquals(avoid, OutsideSymbol) || ReferenceEquals(avoid, AroundSymbol))
        {
            if (slur != null)
            {
                GrobClosure.ChainOffsetCallback(
                    e,
                    new UnpurePureContainer(
                        LilyPondScheme.LookupProcedure(OutsideSlurCallbackSymbol),
                        LilyPondScheme.LookupProcedure(PureOutsideSlurCallbackSymbol)),
                    Axis.Y);
                GrobClosure.ChainCallback(
                    e,
                    LilyPondScheme.LookupProcedure(OutsideSlurCrossStaffSymbol),
                    CrossStaffSymbol);
                e.SetObject(SlurObjectSymbol, slur);
            }
        }
        else if (!ReferenceEquals(avoid, IgnoreSymbol))
        {
            e.Warning("Ignoring grob for slur: " + e.Name + ".  avoid-slur not set?");
        }
    }

    /// <summary>
    /// Chained onto the <c>cross-staff</c> of a grob placed outside or around a slur: such
    /// a grob becomes cross-staff whenever the slur it dodges is.
    /// </summary>
    /// <param name="me">The grob.</param>
    /// <param name="previous">The value the chain has reached so far.</param>
    /// <returns>Whether the grob is cross-staff.</returns>
    public static object OutsideSlurCrossStaff(Grob me, object previous)
    {
        if (SchemeUtilities.ToBool(previous))
        {
            return previous;
        }

        Grob slur = me.GetObject(SlurObjectSymbol) as Grob;

        if (slur == null)
        {
            return false;
        }

        return slur.GetProperty(CrossStaffSymbol);
    }

    /// <summary>The <c>cross-staff</c> callback.</summary>
    /// <param name="me">The slur.</param>
    /// <returns>Whether the slur reaches into more than one staff.</returns>
    public static object CalcCrossStaff(Grob me)
    {
        IReadOnlyList<Grob> cols = PointerGroupInterface.ExtractGrobSet(me, NoteColumnsSymbol);
        IReadOnlyList<Grob> extras
            = PointerGroupInterface.ExtractGrobSet(me, EncompassObjectsSymbol);

        for (int i = 0; i < cols.Count; i++)
        {
            Grob s = NoteColumn.GetStem(cols[i]);
            if (s != null && SchemeUtilities.ToBool(s.GetProperty(CrossStaffSymbol)))
            {
                return true;
            }
        }

        /* the separation items are dealt with in replace_breakable_encompass_objects
           so we can ignore them here */
        List<Grob> nonSepExtras = new List<Grob>();
        for (int i = 0; i < extras.Count; i++)
        {
            if (!extras[i].HasInterface(SeparationItemInterface))
            {
                nonSepExtras.Add(extras[i]);
            }
        }

        Grob common = AxisGroupInterface.CommonRefpointOfArray(cols, me, Axis.Y);
        common = AxisGroupInterface.CommonRefpointOfArray(nonSepExtras, common, Axis.Y);

        return !ReferenceEquals(common, me.YParent);
    }

    private static double ReadRealOr(object value, double fallback)
        => SchemeConvert.IsNumber(value) ? SchemeConvert.ToDouble(value, "slur") : fallback;

    private static Offset ReadOffset(object value)
        => value is Pair pair
            ? new Offset(
                SchemeConvert.ToDouble(pair.Car, "slur"), SchemeConvert.ToDouble(pair.Cdr, "slur"))
            : new Offset(0.0, 0.0);
}
