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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/note-column.cc, lily/include/note-column.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/*
  TODO: figure out if we can prune this class. This is just an
  annoying layer between (rest)collision & (note-head + stem)
*/

/// <summary>
/// A group of note heads with their stem — one voice's simultaneous notes — treated as
/// a single entity, which is what the collision code moves around.
/// </summary>
public static class NoteColumn
{
    private static readonly Symbol RestSymbol = Symbol.Intern("rest");
    private static readonly Symbol StemSymbol = Symbol.Intern("stem");
    private static readonly Symbol FlagSymbol = Symbol.Intern("flag");
    private static readonly Symbol DotSymbol = Symbol.Intern("dot");
    private static readonly Symbol HorizontalShiftSymbol = Symbol.Intern("horizontal-shift");
    private static readonly Symbol NoteHeadsSymbol = Symbol.Intern("note-heads");
    private static readonly Symbol AccidentalGrobSymbol = Symbol.Intern("accidental-grob");
    private static readonly Symbol StemInterface = Symbol.Intern("stem-interface");
    private static readonly Symbol RestInterface = Symbol.Intern("rest-interface");
    private static readonly Symbol NoteHeadInterface = Symbol.Intern("note-head-interface");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol AccidentalPlacementInterface
        = Symbol.Intern("accidental-placement-interface");

    /// <summary>Determines whether the column carries a rest.</summary>
    /// <param name="me">The note column.</param>
    /// <returns><see langword="true"/> when a rest is linked.</returns>
    public static bool HasRests(Grob me) => me.GetObject(RestSymbol) is Grob;

    /// <summary>
    /// Orders two columns by their <c>horizontal-shift</c> property — the order the
    /// collision code resolves same-direction voices in.
    /// </summary>
    /// <param name="p1">The first column.</param>
    /// <param name="p2">The second column.</param>
    /// <returns><see langword="true"/> when the first shifts less.</returns>
    public static bool ShiftLess(Grob p1, Grob p2)
    {
        object s1 = p1.GetProperty(HorizontalShiftSymbol);
        object s2 = p2.GetProperty(HorizontalShiftSymbol);

        int h1 = SchemeConvert.IsNumber(s1) ? SchemeConvert.ToInt(s1, "horizontal-shift") : 0;
        int h2 = SchemeConvert.IsNumber(s2) ? SchemeConvert.ToInt(s2, "horizontal-shift") : 0;
        return h1 < h2;
    }

    /// <summary>Returns the column's stem, when it has one.</summary>
    /// <param name="me">The note column.</param>
    /// <returns>The stem item, or <see langword="null"/>.</returns>
    public static Item GetStem(Grob me) => me.GetObject(StemSymbol) as Item;

    /// <summary>Returns the flag on the column's stem, when there is one.</summary>
    /// <param name="me">The note column.</param>
    /// <returns>The flag item, or <see langword="null"/>.</returns>
    public static Item GetFlag(Grob me)
    {
        Item stem = GetStem(me);
        if (stem != null)
        {
            return stem.GetObject(FlagSymbol) as Item;
        }

        return null;
    }

    /// <summary>Returns the staff-position interval covered by the column's heads.</summary>
    /// <param name="me">The note column.</param>
    /// <returns>The interval, empty when there are no heads.</returns>
    public static Slice HeadPositionsInterval(Grob me)
    {
        Slice iv = Slice.Empty;

        IReadOnlyList<Grob> heads = PointerGroupInterface.ExtractGrobSet(me, NoteHeadsSymbol);
        foreach (Grob se in heads)
        {
            int j = StaffSymbolReferencer.GetRoundedPosition(se);
            iv.Unite(new Slice(j, j));
        }

        return iv;
    }

    /// <summary>
    /// Returns the column's direction: the stem's when it has one, else the side of the
    /// staff its heads sit on.
    /// </summary>
    /// <param name="me">The note column.</param>
    /// <returns>The direction, centre when it cannot be determined.</returns>
    public static Direction Dir(Grob me)
    {
        Grob stem = me.GetObject(StemSymbol) as Grob;
        if (stem != null && stem.HasInterface(StemInterface))
        {
            return DirectionalElementInterface.GetStrictGrobDirection(stem);
        }
        else
        {
            IReadOnlyList<Grob> heads = PointerGroupInterface.ExtractGrobSet(me, NoteHeadsSymbol);
            if (heads.Count > 0)
            {
                Slice positions = HeadPositionsInterval(me);
                return new Direction((long)((positions.Left + positions.Right) / 2));
            }
        }

        if (me.HasInterface(NoteColumnInterface))
        {
            Warn.ProgrammingError("Note_column without heads and stem");
        }
        else
        {
            Warn.ProgrammingError("dir() given grob without Note_column interface");
        }

        return Direction.Center;
    }

    /// <summary>Links a stem to the column and takes it up as an element.</summary>
    /// <param name="me">The note column.</param>
    /// <param name="stem">The stem.</param>
    public static void SetStem(Grob me, Grob stem)
    {
        me.SetObject(StemSymbol, stem);
        AxisGroupInterface.AddElement(me, stem);
    }

    /// <summary>Returns the column's rest, when it has one.</summary>
    /// <param name="me">The note column.</param>
    /// <returns>The rest grob, or <see langword="null"/>.</returns>
    public static Grob GetRest(Grob me) => me.GetObject(RestSymbol) as Grob;

    /// <summary>
    /// Adds a rhythmic head — a note head or a rest — to the column, warning when it
    /// would end up holding both kinds at once.
    /// </summary>
    /// <param name="me">The note column.</param>
    /// <param name="h">The head to add.</param>
    public static void AddHead(Grob me, Grob h)
    {
        bool both = false;
        if (h.HasInterface(RestInterface))
        {
            IReadOnlyList<Grob> heads = PointerGroupInterface.ExtractGrobSet(me, NoteHeadsSymbol);
            if (heads.Count > 0)
            {
                both = true;
            }
            else
            {
                me.SetObject(RestSymbol, h);
            }
        }
        else if (h.HasInterface(NoteHeadInterface))
        {
            if (me.GetObject(RestSymbol) is Grob)
            {
                both = true;
            }

            PointerGroupInterface.AddGrob(me, NoteHeadsSymbol, h);
        }

        if (both)
        {
            Warn.Warning("cannot have note heads and rests together on a stem");
        }
        else
        {
            AxisGroupInterface.AddElement(me, h);
        }
    }

    /// <summary>Returns the head at the stem's far end.</summary>
    /// <param name="me">The note column.</param>
    /// <returns>The head, or <see langword="null"/> when there is no stem.</returns>
    public static Grob FirstHead(Grob me)
    {
        Grob st = GetStem(me);
        return st != null ? Stem.FirstHead(st) : null;
    }

    /// <summary>Returns (bottom-head, top-head) of the column's own head list.</summary>
    /// <param name="me">The note column.</param>
    /// <returns>The extreme heads.</returns>
    public static DrulArray<Grob> ExtremalHeads(Grob me)
    {
        // This looks weird because it is weird; see the implementation.
        return Stem.ExtremalHeads(me);
    }

    /// <summary>Returns the head that rules the column's horizontal shift.</summary>
    /// <param name="me">The note column.</param>
    /// <returns>The support head, or <see langword="null"/> when there is no stem.</returns>
    public static Grob SupportHead(Grob me)
    {
        Grob st = GetStem(me);
        return st != null ? Stem.SupportHead(st) : null;
    }

    /*
      Return extent of the noteheads in the "main column",
      (i.e. excluding any suspended noteheads), or extent
      of the rest (if there are no heads).
    */

    /// <summary>
    /// The <c>main-extent</c> callback: the X extent of the main head — or of the rest
    /// when there are no heads at all.
    /// </summary>
    /// <param name="me">The note column.</param>
    /// <returns>The extent.</returns>
    public static Interval CalcMainExtent(Grob me)
    {
        Grob mainHead = null;
        if (GetStem(me) != null)
        {
            mainHead = FirstHead(me);
        }
        else
        {
            // no stems => no suspended noteheads.
            IReadOnlyList<Grob> heads = PointerGroupInterface.ExtractGrobSet(me, NoteHeadsSymbol);
            if (heads.Count > 0)
            {
                mainHead = heads[0];
            }
        }

        Grob mainItem = mainHead ?? me.GetObject(RestSymbol) as Grob;

        return mainItem != null ? mainItem.Extent(me, Axis.X) : new Interval(0, 0);
    }

    /*
      Return the first AccidentalPlacement grob that we find in a note-head.
    */

    /// <summary>Returns the accidental placement the column's heads hang under.</summary>
    /// <param name="me">The note column.</param>
    /// <returns>The placement grob, the bare accidental for compatibility, or <see langword="null"/>.</returns>
    public static Grob Accidentals(Grob me)
    {
        IReadOnlyList<Grob> heads = PointerGroupInterface.ExtractGrobSet(me, NoteHeadsSymbol);
        Grob acc = null;
        foreach (Grob h in heads)
        {
            acc = h != null ? h.GetObject(AccidentalGrobSymbol) as Grob : null;
            if (acc != null)
            {
                break;
            }
        }

        if (acc == null)
        {
            return null;
        }

        if (acc.XParent != null && acc.XParent.HasInterface(AccidentalPlacementInterface))
        {
            return acc.XParent;
        }

        /* compatibility. */
        return acc;
    }

    /// <summary>
    /// Returns the dot column the column's dots went into.
    /// <para>
    /// Named <c>GetDotColumn</c> rather than upstream's <c>dot_column</c> because a
    /// static method named <c>DotColumn</c> would shadow the <see cref="DotColumn"/>
    /// type beside it in this namespace. Recorded in PORT-COVERAGE under NAMING.
    /// </para>
    /// </summary>
    /// <param name="me">The note column.</param>
    /// <returns>The dot column, or <see langword="null"/>.</returns>
    public static Grob GetDotColumn(Grob me)
    {
        IReadOnlyList<Grob> heads = PointerGroupInterface.ExtractGrobSet(me, NoteHeadsSymbol);
        foreach (Grob head in heads)
        {
            Grob dots = head.GetObject(DotSymbol) as Grob;
            if (dots != null)
            {
                return dots.XParent;
            }
        }

        return null;
    }

    /* If a note-column contains a cross-staff stem then
       nc->extent (Y_AXIS, refp) will not consider the extent of the stem.
       If you want the extent of the stem to be included (and you are safe
       from any cross-staff issues) then call this function instead. */

    /// <summary>Returns the column's Y extent including its stem's.</summary>
    /// <param name="me">The note column.</param>
    /// <param name="refp">The reference grob.</param>
    /// <returns>The extent.</returns>
    public static Interval CrossStaffExtent(Grob me, Grob refp)
    {
        Interval iv = me.Extent(refp, Axis.Y);
        Grob s = GetStem(me);
        if (s != null)
        {
            iv.Unite(s.Extent(refp, Axis.Y));
        }

        return iv;
    }
}
