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
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/tie.cc, lily/include/tie.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - Tie::set_head and Tie::head first lived in a shared seam file, since dissolved,
//     because Completion_heads_engraver needed them before this file existed. They come
//     home here and the seam's tie section is deleted.

/// <summary>
/// A tie: the horizontal curve joining two note heads of the same pitch.
/// </summary>
public static class Tie
{
    private static readonly Symbol NoteHeadInterface = Symbol.Intern("note-head-interface");
    private static readonly Symbol TieColumnInterface = Symbol.Intern("tie-column-interface");
    private static readonly Symbol SemiTieColumnInterface = Symbol.Intern("semi-tie-column-interface");
    private static readonly Symbol TiesSymbol = Symbol.Intern("ties");
    private static readonly Symbol PositioningDoneSymbol = Symbol.Intern("positioning-done");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol ControlPointsSymbol = Symbol.Intern("control-points");
    private static readonly Symbol NeutralDirectionSymbol = Symbol.Intern("neutral-direction");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");
    private static readonly Symbol LineThicknessSymbol = Symbol.Intern("line-thickness");
    private static readonly Symbol DashDefinitionSymbol = Symbol.Intern("dash-definition");
    private static readonly Symbol AnnotationSymbol = Symbol.Intern("annotation");

    /// <summary>Attaches one end of the tie to a note head.</summary>
    /// <param name="me">The tie.</param>
    /// <param name="d">Which end.</param>
    /// <param name="h">The note head.</param>
    public static void SetHead(Spanner me, Direction d, Grob h) => me.SetBound(d, h);

    /// <summary>Returns the note head at one end, when there is one.</summary>
    /// <param name="me">The tie.</param>
    /// <param name="d">Which end.</param>
    /// <returns>The note head, or <see langword="null"/> when that end is a column.</returns>
    public static Item Head(Spanner me, Direction d)
    {
        Item it = me.GetBound(d);
        return it != null && it.HasInterface(NoteHeadInterface) ? it : null;
    }

    /// <summary>Returns the paper-column rank of one end.</summary>
    /// <param name="me">The tie.</param>
    /// <param name="d">Which end.</param>
    /// <returns>The rank.</returns>
    public static int GetColumnRank(Spanner me, Direction d)
        => me.GetBound(d).GetColumn().Rank;

    /// <summary>Returns the staff position of the tie's note heads.</summary>
    /// <remarks>
    /// A tie with no head on either side cannot be placed at all, so upstream kills it
    /// rather than guessing — and says so.
    /// </remarks>
    /// <param name="me">The tie.</param>
    /// <returns>The staff position.</returns>
    public static int GetPosition(Spanner me)
    {
        foreach (Direction d in Both)
        {
            Grob h = Head(me, d);
            if (h != null)
            {
                return (int)Math.Round(
                    StaffSymbolReferencer.GetPosition(h), MidpointRounding.ToEven);
            }
        }

        /*
          TODO: this is theoretically possible for ties across more than 2
          systems.. We should look at the first broken copy.
        */
        Warn.ProgrammingError("Tie without heads.  Suicide");
        me.Suicide();
        return 0;
    }

    /// <summary>
    /// Returns the side a lone tie takes: opposite the stem [Wanske p231].
    /// </summary>
    /// <remarks>
    /// In a chord, <see cref="TieColumn"/> takes over. The rules here are more involved
    /// than they look — see [Ross] p136 and further — and upstream's own two questions
    /// about the gaps are kept in the code.
    /// </remarks>
    /// <param name="me">The tie.</param>
    /// <returns>The direction.</returns>
    public static Direction GetDefaultDir(Spanner me)
    {
        DrulArray<Grob> stems = new DrulArray<Grob>(null, null);
        foreach (Direction d in Both)
        {
            Grob oneHead = Head(me, d);
            if (oneHead == null)
            {
                Spanner neighbor = me.BrokenNeighbor(d);
                oneHead = neighbor != null ? Head(neighbor, d) : null;
            }

            Grob stem = oneHead != null ? RhythmicHead.GetStem(oneHead) : null;
            stems[d] = stem != null && !Stem.IsInvisible(stem) ? stem : null;
        }

        Grob left = stems[Direction.Negative];
        Grob right = stems[Direction.Positive];

        if (left != null && right != null)
        {
            if (DirectionalElementInterface.GetGrobDirection(left) == Direction.Positive
                && DirectionalElementInterface.GetGrobDirection(right) == Direction.Positive)
            {
                return Direction.Negative;
            }

            // And why not return UP if both stems are DOWN?

            // And when stems conflict, why fall directly through to using
            // neutral-direction without considering get_position (me)?
        }
        else if (left != null)
        {
            return -DirectionalElementInterface.GetGrobDirection(left);
        }
        else if (right != null)
        {
            return -DirectionalElementInterface.GetGrobDirection(right);
        }
        else
        {
            int p = GetPosition(me);
            if (p != 0)
            {
                return new Direction(p);
            }
        }

        return DirectionalElementInterface.FromScheme(
            me.GetProperty(NeutralDirectionSymbol), Direction.Center);
    }

    /// <summary>The <c>direction</c> callback: defers to the tie's column.</summary>
    /// <param name="me">The tie.</param>
    /// <returns>The direction the column decided.</returns>
    public static object CalcDirection(Grob me)
    {
        // In this method, Tie and Semi_tie require the same logic with different types.
        Grob yparent = me.YParent;
        if (yparent != null
            && (yparent.HasInterface(TieColumnInterface)
                || yparent.HasInterface(SemiTieColumnInterface))
            && yparent.GetObject(TiesSymbol) is GrobArray)
        {
            // trigger positioning.
            yparent.GetProperty(PositioningDoneSymbol);

            return me.GetPropertyData(DirectionSymbol);
        }

        Warn.ProgrammingError("no Tie_column or Semi_tie_column.  Killing grob.");
        me.Suicide();
        return (long)(int)Direction.Center;
    }

    /// <summary>Runs the scorer for a tie that has no column to speak for it.</summary>
    /// <param name="me">The tie.</param>
    /// <returns>The control points, or the empty list when the tie died.</returns>
    public static object GetDefaultControlPoints(Spanner me)
    {
        Grob common = me;
        common = me.GetBound(Direction.Negative).CommonRefpoint(common, Axis.X);
        common = me.GetBound(Direction.Positive).CommonRefpoint(common, Axis.X);

        TieFormattingProblem problem = new TieFormattingProblem();
        problem.FromTie(me);

        if (!me.IsLive)
        {
            return Nil.Instance;
        }

        TiesConfiguration conf = problem.GenerateOptimalConfiguration();

        return GetControlPoints(me, problem.CommonXRefpoint(), conf[0], problem.Details);
    }

    /// <summary>Turns a scored configuration into the tie's four control points.</summary>
    /// <param name="me">The tie.</param>
    /// <param name="common">The reference point the configuration is measured against.</param>
    /// <param name="conf">The configuration.</param>
    /// <param name="details">The tie details.</param>
    /// <returns>The control points, as a Scheme list.</returns>
    public static object GetControlPoints(
        Grob me, Grob common, TieConfiguration conf, TieDetails details)
    {
        Bezier b = conf.GetTransformedBezier(details);
        b.Translate(new Offset(-me.RelativeCoordinate(common, Axis.X), 0));

        object controls = Nil.Instance;
        for (int i = 4; i-- > 0;)
        {
            if (!b[i].IsSane)
            {
                Warn.ProgrammingError("Insane offset");
            }

            controls = new Pair(Stencil.OffsetToScm(b[i]), controls);
        }

        return controls;
    }

    /// <summary>The <c>control-points</c> callback.</summary>
    /// <param name="me">The tie.</param>
    /// <returns>The control points.</returns>
    public static object CalcControlPoints(Spanner me)
    {
        Grob yparent = me.YParent;
        if (yparent != null
            && (yparent.HasInterface(TieColumnInterface)
                || yparent.HasInterface(SemiTieColumnInterface))
            && yparent.GetObject(TiesSymbol) is GrobArray)
        {
            IReadOnlyList<Grob> ties = PointerGroupInterface.ExtractGrobSet(yparent, TiesSymbol);
            if (me.Original != null && ties.Count == 1
                && DirectionalElementInterface.FromScheme(
                       me.GetPropertyData(DirectionSymbol), Direction.Center)
                   == Direction.Center)
            {
                DirectionalElementInterface.SetGrobDirection(me, GetDefaultDir(me));
            }

            // trigger positioning.
            yparent.GetProperty(PositioningDoneSymbol);
        }

        object cp = me.GetPropertyData(ControlPointsSymbol);
        if (!(cp is Pair))
        {
            cp = GetDefaultControlPoints(me);
        }

        return cp;
    }

    /// <summary>Draws the tie.</summary>
    /// <remarks>Upstream's own TODO: merge with <c>Slur::print</c>.</remarks>
    /// <param name="me">The tie.</param>
    /// <returns>The stencil.</returns>
    public static object Print(Grob me)
    {
        object cp = me.GetProperty(ControlPointsSymbol);

        double staffThick = StaffSymbolReferencer.LineThickness(me);
        double baseThick = staffThick * ReadReal(me.GetProperty(ThicknessSymbol), 1);
        double lineThick = staffThick * ReadReal(me.GetProperty(LineThicknessSymbol), 1);

        Bezier b = new Bezier();
        for (int i = 0; i < Bezier.ControlCount; i++)
        {
            if (cp is Pair pair)
            {
                b[i] = ToOffset(pair.Car);
                cp = pair.Cdr;
            }
            else
            {
                b[i] = new Offset(0.0, 0.0);
            }
        }

        object dashDefinition = me.GetProperty(DashDefinitionSymbol);
        Stencil a = Lookup.Slur(
            b,
            (int)DirectionalElementInterface.GetGrobDirection(me) * baseThick,
            lineThick,
            dashDefinition);

        object annotation = me.GetProperty(AnnotationSymbol);
        if (annotation is string annotationText)
        {
            Stencil tm = TextInterface.GrobInterpretMarkup(me, annotationText);
            tm.Translate(new Offset(b[3][Axis.X] + 0.5, b[0][Axis.Y] * 2));
            tm = tm.InColor(1.0, 0.0, 0.0);

            /*
              It would be nice if we could put this in a different layer,
              but alas, this must be done with a Tie override.
            */
            a.AddStencil(tm);
        }

        return a;
    }

    /// <summary>Orders two ties by staff position, which is how a column sorts them.</summary>
    /// <param name="a">The first tie.</param>
    /// <param name="b">The second tie.</param>
    /// <returns><see langword="true"/> when the first sits lower.</returns>
    public static bool Less(Spanner a, Spanner b) => GetPosition(a) < GetPosition(b);

    private static readonly Direction[] Both = { Direction.Negative, Direction.Positive };

    // upstream's from_scm<Offset>: an (x . y) pair, zero for anything else.
    private static Offset ToOffset(object value)
        => value is Pair pair
            ? new Offset(
                SchemeConvert.ToDouble(pair.Car, "tie"), SchemeConvert.ToDouble(pair.Cdr, "tie"))
            : new Offset(0.0, 0.0);

    private static double ReadReal(object value, double fallback)
        => SchemeConvert.IsNumber(value) ? SchemeConvert.ToDouble(value, "tie") : fallback;
}
