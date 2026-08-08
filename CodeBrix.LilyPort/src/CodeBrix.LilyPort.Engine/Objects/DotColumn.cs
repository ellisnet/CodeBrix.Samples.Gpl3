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

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/dot-column.cc, lily/include/dot-column.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// Groups the dots of one voice's heads into a column, so they align per voice and
/// stay off the staff lines.
/// <para>
/// Upstream's header also declares a <c>compare</c> member and a
/// <c>side_position</c> Scheme callback that no translation unit defines; both are
/// dead upstream and deliberately not carried. Recorded in PORT-COVERAGE.
/// </para>
/// </summary>
public static class DotColumn
{
    private static readonly Symbol DotsSymbol = Symbol.Intern("dots");
    private static readonly Symbol DotSymbol = Symbol.Intern("dot");
    private static readonly Symbol StemSymbol = Symbol.Intern("stem");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol KievanSymbol = Symbol.Intern("kievan");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol NoteCollisionSymbol = Symbol.Intern("note-collision");
    private static readonly Symbol PositioningDoneSymbol = Symbol.Intern("positioning-done");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol ChordDotsLimitSymbol = Symbol.Intern("chord-dots-limit");
    private static readonly Symbol XOffsetSymbol = Symbol.Intern("X-offset");
    private static readonly Symbol YOffsetSymbol = Symbol.Intern("Y-offset");
    private static readonly Symbol SideSupportElements = Symbol.Intern("side-support-elements");
    private static readonly Symbol RestInterface = Symbol.Intern("rest-interface");
    private static readonly Symbol StemInterface = Symbol.Intern("stem-interface");
    private static readonly Symbol NoteHeadInterface = Symbol.Intern("note-head-interface");
    private static readonly Symbol XParentPositioningSymbol
        = Symbol.Intern("ly:grob::x-parent-positioning");

    /// <summary>
    /// The <c>positioning-done</c> callback: places every dot so none sits on a staff
    /// line or on another dot, then moves the whole column clear of the heads, stems
    /// and flags beside it.
    /// </summary>
    /// <param name="me">The dot column.</param>
    /// <returns><see langword="true"/>.</returns>
    public static bool CalcPositioningDone(Grob me)
    {
        /*
          Trigger note collision resolution first, since that may kill off
          dots when merging.
        */
        if (me.GetObject(NoteCollisionSymbol) is Grob collision)
        {
            collision.GetProperty(PositioningDoneSymbol);
        }

        me.SetProperty(PositioningDoneSymbol, true);

        List<Grob> dots = new List<Grob>(
            PointerGroupInterface.ExtractGrobSet(me, DotsSymbol));

        List<Grob> parentStems = new List<Grob>();
        double ss = 0;

        Grob commonx = me;
        for (int i = 0; i < dots.Count; i++)
        {
            Grob n = dots[i].YParent;
            commonx = n.CommonRefpoint(commonx, Axis.X);

            if (n.GetObject(StemSymbol) is Grob stem)
            {
                commonx = stem.CommonRefpoint(commonx, Axis.X);

                if (ReferenceEquals(Stem.FirstHead(stem), n))
                {
                    parentStems.Add(stem);
                }
            }
        }

        List<Box> boxes = new List<Box>();

        // Upstream keeps the stems in a std::unordered_set, whose iteration order is
        // unspecified; the port keeps first-occurrence order, which is deterministic
        // and — the skyline merge being order-independent — the same outcome.
        List<Grob> stems = new List<Grob>();

        IReadOnlyList<Grob> support
            = PointerGroupInterface.ExtractGrobSet(me, SideSupportElements);

        Interval baseX = Interval.Empty;
        for (int i = 0; i < parentStems.Count; i++)
        {
            Grob head = Stem.FirstHead(parentStems[i]);
            if (head != null)
            {
                baseX.Unite(head.Extent(commonx, Axis.X));
            }
        }

        // TODO: could this be refactored using side-position-interface?
        for (int i = 0; i < support.Count; i++)
        {
            Grob s = support[i];
            if (ss == 0.0)
            {
                ss = StaffSymbolReferencer.StaffSpace(s);
            }

            /* can't inspect Y extent of rest.

               Rest collisions should wait after line breaking.
            */
            Interval y;
            if (s.HasInterface(RestInterface))
            {
                baseX.Unite(s.Extent(commonx, Axis.X));
                continue;
            }
            else if (s.HasInterface(StemInterface))
            {
                Direction stemDir = DirectionalElementInterface.GetGrobDirection(s);
                double y1 = Stem.HeadPositions(s)[-stemDir];
                double y2 = y1 + stemDir.Value * 7;

                y = Interval.Empty;
                y.AddPoint(y1);
                y.AddPoint(y2);

                if (!stems.Contains(s))
                {
                    stems.Add(s);
                }
            }
            else if (s.HasInterface(NoteHeadInterface))
            {
                y = new Interval(-1.1, 1.1);
            }
            else
            {
                Warn.ProgrammingError("unknown grob in dot col support");
                continue;
            }

            y += StaffSymbolReferencer.GetPosition(s);

            Box b = new Box(s.Extent(commonx, Axis.X), y);
            boxes.Add(b);

            if (s.GetObject(StemSymbol) is Grob supportStem && !stems.Contains(supportStem))
            {
                stems.Add(supportStem);
            }
        }

        foreach (Grob stem in stems)
        {
            Grob flag = Stem.FlagGrob(stem);
            if (flag != null)
            {
                Grob commony = stem.CommonRefpoint(flag, Axis.Y);
                Interval y = flag.Extent(commony, Axis.Y) * (2 / ss);
                Interval x = flag.Extent(commonx, Axis.X);

                boxes.Add(new Box(x, y));
            }
        }

        /*
          The use of pure_position_less and pure_get_rounded_position below
          are due to the fact that this callback is called before line breaking
          occurs.  Because dots' actual Y posiitons may be linked to that of
          beams (dots are attached to rests, which are shifted to avoid beams),
          we instead must use their pure Y positions.
        */
        // Stable insertion sort over pure_position_less; upstream's std::sort has an
        // unspecified tie order, and a stable order is deterministic.
        for (int i = 1; i < dots.Count; i++)
        {
            Grob current = dots[i];
            int j = i - 1;
            while (j >= 0 && StaffSymbolReferencer.PurePositionLess(current, dots[j]))
            {
                dots[j + 1] = dots[j];
                j--;
            }

            dots[j + 1] = current;
        }

        object chordDotsLimit = me.GetProperty(ChordDotsLimitSymbol);
        if (SchemeConvert.IsNumber(chordDotsLimit))
        {
            // Sort dots by stem, then check for dots above the limit for each stem
            List<List<Grob>> dotsEachStem = new List<List<Grob>>();
            for (int j = 0; j < parentStems.Count; j++)
            {
                dotsEachStem.Add(new List<Grob>());
            }

            for (int i = 0; i < dots.Count; i++)
            {
                if (dots[i].YParent?.GetObject(StemSymbol) is Grob stem)
                {
                    for (int j = 0; j < parentStems.Count; j++)
                    {
                        if (ReferenceEquals(stem, parentStems[j]))
                        {
                            dotsEachStem[j].Add(dots[i]);
                            break;
                        }
                    }
                }
            }

            for (int j = 0; j < parentStems.Count; j++)
            {
                Interval chord = Stem.HeadPositions(parentStems[j]);
                int totalRoom = ((int)chord.Length + 2
                                 + SchemeConvert.ToInt(chordDotsLimit, "chord-dots-limit"))
                                / 2;
                int totalDots = dotsEachStem[j].Count;

                // remove excessive dots from the ends of the stem
                for (int firstDot = 0; totalDots > totalRoom; totalDots--)
                {
                    if (0 == (totalDots - totalRoom) % 2)
                    {
                        dotsEachStem[j][firstDot++].Suicide();
                    }
                    else
                    {
                        dotsEachStem[j][firstDot + totalDots - 1].Suicide();
                    }
                }
            }
        }

        for (int i = dots.Count; i-- > 0;)
        {
            if (!dots[i].IsLive)
            {
                dots.RemoveAt(i);
            }
            else
            {
                // Undo any fake translations that were done in add_head.
                dots[i].TranslateAxis(
                    -dots[i].RelativeCoordinate(me, Axis.X), Axis.X);
            }
        }

        DotFormattingProblem problem = new DotFormattingProblem(boxes, baseX);

        DotConfiguration cfg = new DotConfiguration(problem);
        for (int i = 0; i < dots.Count; i++)
        {
            DotPosition dp = default;
            dp.Dot = dots[i];

            Grob note = dots[i].YParent;
            if (note != null)
            {
                if (note.HasInterface(NoteHeadInterface))
                {
                    dp.Dir = DirectionalElementInterface.FromScheme(dp.Dot.GetProperty(DirectionSymbol), Direction.Center);
                }

                dp.XExtent = note.Extent(commonx, Axis.X);
            }

            int p = StaffSymbolReferencer.PureGetRoundedPosition(dp.Dot);

            /* icky, since this should go via a Staff_symbol_referencer
               offset callback but adding a dot overwrites Y-offset. */
            object staffPosition = dp.Dot.GetProperty(StaffPositionSymbol);
            p += (int)(SchemeConvert.IsNumber(staffPosition)
                ? SchemeConvert.ToDouble(staffPosition, "staff-position")
                : 0.0);
            dp.Pos = p;

            cfg.RemoveCollision(p);
            cfg[p] = dp;
            if (StaffSymbolReferencer.OnLine(dp.Dot, p)
                && !ReferenceEquals(dp.Dot.GetProperty(StyleSymbol), KievanSymbol))
            {
                cfg.RemoveCollision(p);
            }
        }

        foreach (KeyValuePair<int, DotPosition> ent in cfg.Entries) // Junkme?
        {
            StaffSymbolReferencer.PureSetPosition(ent.Value.Dot, ent.Key);
        }

        object padding = me.GetProperty(PaddingSymbol);
        me.TranslateAxis(
            cfg.XOffset()
                - me.RelativeCoordinate(commonx, Axis.X)
                + (SchemeConvert.IsNumber(padding)
                    ? SchemeConvert.ToDouble(padding, "padding")
                    : 0.0),
            Axis.X);
        return true;
    }

    /// <summary>
    /// Takes a head's dot into the column: the head becomes a support, the dot an
    /// element positioned by the column.
    /// </summary>
    /// <param name="me">The dot column.</param>
    /// <param name="head">The rhythmic head whose dot is added.</param>
    public static void AddHead(Grob me, Grob head)
    {
        Grob d = head.GetObject(DotSymbol) as Grob;
        if (d != null)
        {
            SidePositionInterface.AddSupport(me, head);

            PointerGroupInterface.AddGrob(me, DotsSymbol, d);

            object parentPositioning
                = Bootstrap.LilyPondScheme.LookupProcedure(XParentPositioningSymbol);
            if (parentPositioning == null)
            {
                Warn.ProgrammingError("ly:grob::x-parent-positioning is not defined");
                return;
            }

            d.SetProperty(YOffsetSymbol, parentPositioning);

            // Dot formatting requests the Y-offset, which for rests may
            // trigger post-linebreak callbacks.  On the other hand, we need the
            // correct X-offset of the dots for horizontal collision avoidance.
            // The translation here is undone in calc_positioning_done, where we
            // do the X-offset properly.
            // TODO: this seems very hacky.  We should try to find something better.
            if (head.HasInterface(RestInterface))
            {
                object padding = me.GetProperty(PaddingSymbol);
                d.TranslateAxis(
                    head.Extent(head, Axis.X).Length
                        + (SchemeConvert.IsNumber(padding)
                            ? SchemeConvert.ToDouble(padding, "padding")
                            : 0.0),
                    Axis.X);
            }
            else
            {
                d.SetProperty(XOffsetSymbol, parentPositioning);
            }

            AxisGroupInterface.AddElement(me, d);
        }
    }
}
