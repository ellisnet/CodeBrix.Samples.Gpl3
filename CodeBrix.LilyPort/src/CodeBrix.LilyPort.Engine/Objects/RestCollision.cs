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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/rest-collision.cc, lily/include/rest-collision.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Moves ordinary rests out of the way: rests against rests when a timestep holds only
/// rests, and rests against the notes of the other voices otherwise.
/// </summary>
public static class RestCollision
{
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol RestSymbol = Symbol.Intern("rest");
    private static readonly Symbol RestCollisionSymbol = Symbol.Intern("rest-collision");
    private static readonly Symbol PositioningDoneSymbol = Symbol.Intern("positioning-done");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol MinimumDistanceSymbol = Symbol.Intern("minimum-distance");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol ForceShiftCallbackRestSymbol
        = Symbol.Intern("ly:rest-collision::force-shift-callback-rest");
    private static readonly Symbol PureChainOffsetCallbackSymbol
        = Symbol.Intern("pure-chain-offset-callback");

    /// <summary>
    /// The chained <c>Y-offset</c> callback on a rest under a collision: applies the
    /// offset computed so far, then makes the collision do its positioning, and answers
    /// zero — the positioning itself moved the rest.
    /// </summary>
    /// <param name="restGrob">The rest.</param>
    /// <param name="offset">The offset computed by the chained-over callback.</param>
    /// <returns>Zero.</returns>
    public static double ForceShiftCallbackRest(Grob restGrob, object offset)
    {
        Grob parent = restGrob.XParent;

        /*
          translate REST; we need the result of this translation later on,
          while the offset probably still is 0/calculation-in-progress.
         */
        if (SchemeConvert.IsNumber(offset))
        {
            restGrob.TranslateAxis(
                SchemeConvert.ToDouble(offset, "force-shift-callback-rest"), Axis.Y);
        }

        if (parent != null && parent.HasInterface(NoteColumnInterface)
            && NoteColumn.HasRests(parent))
        {
            Grob collision = parent.GetObject(RestCollisionSymbol) as Grob;

            collision?.GetProperty(PositioningDoneSymbol);
        }

        return 0.0;
    }

    /// <summary>
    /// Takes a note column into the collision, and chains the collision's shift
    /// callback onto its rest's <c>Y-offset</c>.
    /// </summary>
    /// <param name="me">The collision object.</param>
    /// <param name="p">The note column.</param>
    public static void AddColumn(Grob me, Grob p)
    {
        PointerGroupInterface.AddGrob(me, ElementsSymbol, p);

        p.SetObject(RestCollisionSymbol, me);

        Grob rest = p.GetObject(RestSymbol) as Grob;
        if (rest != null)
        {
            object shiftProc = LilyPondScheme.LookupProcedure(ForceShiftCallbackRestSymbol);
            object pureProc = LilyPondScheme.LookupProcedure(PureChainOffsetCallbackSymbol);
            if (shiftProc == null || pureProc == null)
            {
                Warn.ProgrammingError(
                    "rest-collision force-shift callback is not defined");
                return;
            }

            GrobClosure.ChainOffsetCallback(
                rest,
                new UnpurePureContainer(shiftProc, pureProc),
                Axis.Y);
        }
    }

    private static bool RestShiftLess(Grob r1, Grob r2)
    {
        Grob col1 = r1.XParent;
        Grob col2 = r2.XParent;
        return NoteColumn.ShiftLess(col1, col2);
    }

    /*
      TODO: look at horizontal-shift to determine ordering between rests
      for more than two voices.
    */

    /// <summary>
    /// The <c>positioning-done</c> callback: resolves the rest-rest or rest-note
    /// collisions among the collision's columns.
    /// </summary>
    /// <param name="me">The collision object.</param>
    /// <returns><see langword="true"/>.</returns>
    public static bool CalcPositioningDone(Grob me)
    {
        me.SetProperty(PositioningDoneSymbol, true);

        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);

        List<Grob> rests = new List<Grob>();
        List<Grob> notes = new List<Grob>();

        foreach (Grob e in elts)
        {
            if (e.HasInterface(NoteColumnInterface))
            {
                if (e.GetObject(RestSymbol) is Grob)
                {
                    rests.Add(e);
                }
                else
                {
                    notes.Add(e);
                }
            }
        }

        /*
          handle rest-rest and rest-note collisions

          [todo]
          * decide not to print rest if too crowded?
          */

        /*
          no partners to collide with
        */
        if (rests.Count + notes.Count < 2)
        {
            return true;
        }

        double staffSpace = StaffSymbolReferencer.StaffSpace(me);

        /*
          only rests
        */
        if (notes.Count == 0)
        {
            /*
              This is incomplete: in case of an uneven number of rests, the
              center one should be centered on the staff.
            */
            DrulArray<List<Grob>> orderedRests
                = new DrulArray<List<Grob>>(new List<Grob>(), new List<Grob>());
            foreach (Grob restColumn in rests)
            {
                Grob r = NoteColumn.GetRest(restColumn);

                Direction d = DirectionalElementInterface.GetGrobDirection(r);
                if (d.IsNonZero)
                {
                    orderedRests[d].Add(r);
                }
                else
                {
                    Warn.Warning("cannot resolve rest collision: rest direction not set");
                }
            }

            foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
            {
                // Stable insertion sort over rest_shift_less; see GetClashGroups for
                // the reasoning.
                List<Grob> list = orderedRests[d];
                for (int i = 1; i < list.Count; i++)
                {
                    Grob current = list[i];
                    int j = i - 1;
                    while (j >= 0 && RestShiftLess(current, list[j]))
                    {
                        list[j + 1] = list[j];
                        j--;
                    }

                    list[j + 1] = current;
                }
            }

            foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
            {
                if (orderedRests[d].Count < 1)
                {
                    if (orderedRests[-d].Count > 1)
                    {
                        Warn.Warning("too many colliding rests");
                    }

                    return true;
                }
            }

            Grob common = AxisGroupInterface.CommonRefpointOfArray(
                orderedRests[Direction.Negative], me, Axis.Y);
            common = AxisGroupInterface.CommonRefpointOfArray(
                orderedRests[Direction.Positive], common, Axis.Y);

            List<Grob> down = orderedRests[Direction.Negative];
            List<Grob> up = orderedRests[Direction.Positive];

            double diff
                = (down[down.Count - 1].Extent(common, Axis.Y)[Direction.Positive]
                   - up[up.Count - 1].Extent(common, Axis.Y)[Direction.Negative])
                  / staffSpace;

            if (diff > 0)
            {
                int amountDown = (int)Math.Ceiling(diff / 2);
                diff -= amountDown;
                Rest.Translate(down[down.Count - 1], -2 * amountDown);
                if (diff > 0)
                {
                    Rest.Translate(up[up.Count - 1], 2 * (int)Math.Ceiling(diff));
                }
            }

            foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
            {
                List<Grob> list = orderedRests[d];
                for (int i = list.Count - 1; i-- > 0;)
                {
                    double lastY = list[i + 1].Extent(common, Axis.Y)[d];
                    double y = list[i].Extent(common, Axis.Y)[-d];

                    double stepDiff = d.Value * ((lastY - y) / staffSpace);
                    if (stepDiff > 0)
                    {
                        int amount = (int)Math.Ceiling(stepDiff) * 2;
                        Rest.Translate(list[i], d.Value * amount);
                    }
                }
            }
        }
        else
        {
            /*
              Rests and notes.
            */
            // Count how many rests we move
            DrulArray<int> rcount = new DrulArray<int>(0, 0);

            foreach (Grob rcol in rests)
            {
                Grob rest = NoteColumn.GetRest(rcol);

                Direction dir = DirectionalElementInterface.GetGrobDirection(rest);
                if (!dir.IsNonZero)
                {
                    dir = NoteColumn.Dir(rcol);
                }

                // Do not compute a translation for pre-positioned rests,
                //  nor count them for the "too many colliding rests" warning
                if (SchemeConvert.IsNumber(rest.GetProperty(StaffPositionSymbol)))
                {
                    continue;
                }

                Grob common = AxisGroupInterface.CommonRefpointOfArray(notes, rcol, Axis.Y);
                Interval restdim = rest.Extent(common, Axis.Y);
                if (restdim.IsEmpty)
                {
                    continue;
                }

                double columnStaffSpace = StaffSymbolReferencer.StaffSpace(rcol);
                object minDist = me.GetProperty(MinimumDistanceSymbol);
                double minimumDist
                    = (SchemeConvert.IsNumber(minDist)
                          ? SchemeConvert.ToDouble(minDist, "minimum-distance")
                          : 1.0)
                      * columnStaffSpace;

                Interval notedim = Interval.Empty;
                foreach (Grob note in notes)
                {
                    if (NoteColumn.Dir(note) == -dir
                        // If the note has already happened (but it has a long
                        // duration, so there is a collision), don't look at the stem.
                        // If we do, the rest gets shifted down a lot and it looks bad.
                        || !ReferenceEquals(
                            (note as Item)?.GetColumn(),
                            (rest as Item)?.GetColumn()))
                    {
                        /* try not to look at the stem, as looking at a beamed
                           note may trigger beam positioning prematurely.

                           This happens with dotted rests, which need Y
                           positioning to compute X-positioning.
                        */
                        Grob head = NoteColumn.FirstHead(note);
                        if (head != null)
                        {
                            notedim.Unite(head.Extent(common, Axis.Y));
                        }
                        else
                        {
                            Warn.ProgrammingError("Note_column without first_head()");
                        }
                    }
                    else
                    {
                        notedim.Unite(note.Extent(common, Axis.Y));
                    }
                }

                double y = dir.Value
                    * Math.Max(
                        0.0,
                        -dir.Value * restdim[-dir] + dir.Value * notedim[dir]
                            + minimumDist);

                // move discretely by half spaces.
                int discreteY = dir.Value
                    * (int)Math.Ceiling(y / (0.5 * dir.Value * columnStaffSpace));

                Interval staffSpan = StaffSymbolReferencer.StaffSpan(rest);
                staffSpan.Widen(1);

                // move by whole spaces inside the staff.
                if (staffSpan.Contains(
                        StaffSymbolReferencer.GetPosition(rest) + discreteY))
                {
                    discreteY = dir.Value
                        * (int)(Math.Ceiling(dir.Value * discreteY / 2.0) * 2.0);
                }

                Rest.Translate(rest, discreteY);
                if (rcount[dir]++ != 0)
                {
                    Warn.Warning("too many colliding rests");
                }
            }
        }

        return true;
    }
}
