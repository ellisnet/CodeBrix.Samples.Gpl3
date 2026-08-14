/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1999--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/script-interface.cc, lily/script-column.cc, lily/include/script-interface.hh, lily/include/script-column.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - script-interface.cc and script-column.cc share a file: the column exists only to
//     order scripts, and it reaches into Script_interface::script_priority_less to do it.
//   - upstream's Grob_scripts_map is std::unordered_map<Grob *, std::vector<Grob *>>.
//     The port uses a Dictionary keyed by reference identity, which is what a raw
//     pointer key gives, and iterates it in INSERTION order rather than hash order —
//     see OrderGrobsByHead's note on why that is a divergence that cannot change output.

/// <summary>
/// An object that is put above or below a note — a staccato dot, an accent, a fermata.
/// </summary>
public static class ScriptInterface
{
    private static readonly Symbol ScriptPrioritySymbol = Symbol.Intern("script-priority");
    private static readonly Symbol CrossStaffSymbol = Symbol.Intern("cross-staff");
    private static readonly Symbol SlurSymbol = Symbol.Intern("slur");
    private static readonly Symbol AvoidSlurSymbol = Symbol.Intern("avoid-slur");
    private static readonly Symbol OutsideSymbol = Symbol.Intern("outside");
    private static readonly Symbol AroundSymbol = Symbol.Intern("around");

    /// <summary>
    /// The <c>positioning-done</c> callback: re-parent the script onto the note head the
    /// stem actually starts from.
    /// <para>
    /// The script engraver parents a script onto the whole note COLUMN, because at
    /// announcement time the head to hang it on is not known — seconds in a chord get
    /// swapped around horizontally. This is where that decision is finally made.
    /// </para>
    /// </summary>
    /// <param name="me">The script.</param>
    /// <returns>Always <see langword="true"/>, as upstream's <c>SCM_BOOL_T</c>.</returns>
    public static object CalcPositioningDone(Grob me)
    {
        if (me.XParent is Grob par)
        {
            Grob stem = NoteColumn.GetStem(par);
            if (stem != null && Stem.FirstHead(stem) != null)
            {
                me.XParent = Stem.FirstHead(stem);
            }
        }

        return true;
    }

    /// <summary>
    /// The <c>cross-staff</c> callback: a script is cross-staff when the thing it hangs
    /// off is.
    /// </summary>
    /// <param name="me">The script.</param>
    /// <returns>Whether the script spans staves.</returns>
    public static bool CalcCrossStaff(Grob me)
    {
        Grob stem = NoteColumn.GetStem(me.XParent);

        if (stem != null && SchemeUtilities.ToBool(stem.GetProperty(CrossStaffSymbol)))
        {
            return true;
        }

        Grob slur = me.GetObject(SlurSymbol) as Grob;
        object avoidSlur = me.GetProperty(AvoidSlurSymbol);
        if (slur != null
            && SchemeUtilities.ToBool(slur.GetProperty(CrossStaffSymbol))
            && (ReferenceEquals(avoidSlur, OutsideSymbol)
                || ReferenceEquals(avoidSlur, AroundSymbol)))
        {
            return true;
        }

        return false;
    }

    /// <summary>Orders two scripts by <c>script-priority</c>.</summary>
    /// <param name="g1">The first script.</param>
    /// <param name="g2">The second script.</param>
    /// <returns>Whether the first sorts before the second.</returns>
    /// <remarks>
    /// Upstream reads both through <c>from_scm&lt;int&gt;</c>, which answers 0 for a
    /// missing or non-numeric priority rather than raising — reproduced here, because a
    /// script whose definition omits the property must still sort somewhere definite.
    /// </remarks>
    public static bool ScriptPriorityLess(Grob g1, Grob g2)
    {
        object p1 = g1.GetProperty(ScriptPrioritySymbol);
        object p2 = g2.GetProperty(ScriptPrioritySymbol);
        return ToInt(p1) < ToInt(p2);
    }

    private static int ToInt(object value)
        => SchemeConvert.IsNumber(value) ? (int)SchemeConvert.ToDouble(value, "script-priority") : 0;
}

/// <summary>
/// Sorts the scripts attached to one note by <c>script-priority</c> and
/// <c>outside-staff-priority</c>, so they stack outwards in a stable order.
/// </summary>
public static class ScriptColumn
{
    private static readonly Symbol ScriptPrioritySymbol = Symbol.Intern("script-priority");
    private static readonly Symbol ScriptsSymbol = Symbol.Intern("scripts");
    private static readonly Symbol ScriptColumnSymbol = Symbol.Intern("script-column");
    private static readonly Symbol YOffsetSymbol = Symbol.Intern("Y-offset");
    private static readonly Symbol XOffsetSymbol = Symbol.Intern("X-offset");
    private static readonly Symbol OutsideStaffPrioritySymbol
        = Symbol.Intern("outside-staff-priority");
    private static readonly Symbol AccidentalPlacementInterface
        = Symbol.Intern("accidental-placement-interface");
    private static readonly Symbol ArpeggioInterface = Symbol.Intern("arpeggio-interface");
    private static readonly Symbol YAlignedSideSymbol
        = Symbol.Intern("ly:side-position-interface::y-aligned-side");
    private static readonly Symbol XAlignedSideSymbol
        = Symbol.Intern("ly:side-position-interface::x-aligned-side");

    /// <summary>Adds a side-positioned script to this column.</summary>
    /// <param name="me">The script column.</param>
    /// <param name="script">The script to add.</param>
    /// <remarks>
    /// A script with no numeric <c>script-priority</c> is silently not added — it has no
    /// place in the ordering, so the column has nothing to say about it.
    /// </remarks>
    public static void AddSidePositioned(Grob me, Grob script)
    {
        object p = script.GetProperty(ScriptPrioritySymbol);
        if (!SchemeConvert.IsNumber(p))
        {
            return;
        }

        PointerGroupInterface.AddGrob(me, ScriptsSymbol, script);
        script.SetObject(ScriptColumnSymbol, me);
    }

    /// <summary>
    /// The <c>row-before-line-breaking</c> callback: order the scripts that sit ABOVE and
    /// BELOW each note head, head by head.
    /// </summary>
    /// <param name="me">The script row.</param>
    /// <returns>Unspecified, as upstream.</returns>
    public static object RowBeforeLineBreaking(Grob me)
    {
        IReadOnlyList<Grob> scripts = PointerGroupInterface.ExtractGrobSet(me, ScriptsSymbol);

        // Upstream's Grob_scripts_map, keyed by the Y parent. Reference identity is what a
        // raw pointer key gives; insertion order is the port's, and cannot change output
        // because each bucket is ordered independently of every other.
        List<Grob> heads = new List<Grob>();
        Dictionary<Grob, List<Grob>> headScriptsMap
            = new Dictionary<Grob, List<Grob>>(ReferenceEqualityComparer.Instance);
        List<Grob> affectAllGrobs = new List<Grob>();

        object yAlignedSide = LilyPondScheme.LookupProcedure(YAlignedSideSymbol);

        for (int i = 0; i < scripts.Count; i++)
        {
            Grob sc = scripts[i];

            // Don't want to consider scripts horizontally next to notes.
            if (sc.HasInterface(AccidentalPlacementInterface)
                || sc.HasInterface(ArpeggioInterface))
            {
                affectAllGrobs.Add(sc);
            }
            else if (!ReferenceEquals(sc.GetPropertyData(YOffsetSymbol), yAlignedSide))
            {
                Grob parent = sc.YParent;
                if (!headScriptsMap.TryGetValue(parent, out List<Grob> bucket))
                {
                    bucket = new List<Grob>();
                    headScriptsMap[parent] = bucket;
                    heads.Add(parent);
                }

                bucket.Add(sc);
            }
        }

        for (int i = 0; i < heads.Count; i++)
        {
            List<Grob> grobs = new List<Grob>(headScriptsMap[heads[i]]);

            // this isn't right in all cases, but in general a safe assumption.
            grobs.AddRange(affectAllGrobs);
            OrderGrobs(grobs);
        }

        return Unspecified.Instance;
    }

    /// <summary>
    /// The <c>before-line-breaking</c> callback: order the scripts stacked away from the
    /// staff.
    /// </summary>
    /// <param name="me">The script column.</param>
    /// <returns>Unspecified, as upstream.</returns>
    public static object BeforeLineBreaking(Grob me)
    {
        List<Grob> staffSided = new List<Grob>();
        object xAlignedSide = LilyPondScheme.LookupProcedure(XAlignedSideSymbol);

        IReadOnlyList<Grob> scripts = PointerGroupInterface.ExtractGrobSet(me, ScriptsSymbol);
        for (int i = 0; i < scripts.Count; i++)
        {
            Grob sc = scripts[i];
            if (sc != null && sc.IsLive)
            {
                // Don't want to consider scripts horizontally next to notes.
                if (!ReferenceEquals(sc.GetPropertyData(XOffsetSymbol), xAlignedSide))
                {
                    staffSided.Add(sc);
                }
            }
        }

        OrderGrobs(staffSided);
        return Unspecified.Instance;
    }

    /// <summary>
    /// Stacks a set of scripts outwards, up and down separately, preserving the order
    /// their <c>script-priority</c> gives them.
    /// </summary>
    /// <param name="grobs">The scripts to order.</param>
    /// <remarks>
    /// <para>
    /// The <c>outside-staff-priority</c> bump of 0.1 is upstream's mechanism for keeping
    /// the stacking order once the skyline placer takes over: equal priorities would let
    /// it reorder them freely.
    /// </para>
    /// <para>
    /// Upstream builds the two lists by CONSing and then reverses, so a script announced
    /// first ends up first. The port appends instead and skips the reverse, which is the
    /// same sequence with one less traversal; the sort that follows is stable either way.
    /// </para>
    /// </remarks>
    public static void OrderGrobs(List<Grob> grobs)
    {
        DrulArray<List<Grob>> scriptsDrul
            = new DrulArray<List<Grob>>(new List<Grob>(), new List<Grob>());

        for (int i = 0; i < grobs.Count; i++)
        {
            Grob g = grobs[i];
            Direction d = DirectionalElementInterface.GetStrictGrobDirection(g);
            scriptsDrul[d].Add(g);
        }

        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            List<Grob> ss = scriptsDrul[d];
            StableSort(ss, ScriptInterface.ScriptPriorityLess);

            Grob last = null;                    // previous grob in list
            object initialOutsideStaff = Nil.Instance;
            object lastInitialOutsideStaff = Nil.Instance;

            // loop over all grobs in script column (already sorted by script_priority)
            for (int i = 0; i < ss.Count; i++)
            {
                Grob g = ss[i];
                initialOutsideStaff = g.GetProperty(OutsideStaffPrioritySymbol);
                if (last != null) // not the first grob in the list
                {
                    object lastOutsideStaff = last.GetProperty(OutsideStaffPrioritySymbol);

                    // if outside_staff_priority is missing for previous grob, use all the
                    // scripts so far as support for the current grob
                    if (!SchemeConvert.IsNumber(lastOutsideStaff))
                    {
                        for (int t = 0; t < i; t++)
                        {
                            SidePositionInterface.AddSupport(g, ss[t]);
                        }
                    }

                    // if outside_staff_priority is missing or is equal to original
                    // outside_staff_priority of previous grob, set new
                    // outside_staff_priority to just higher than outside_staff_priority of
                    // previous grob in order to preserve ordering.
                    else if (!SchemeConvert.IsNumber(initialOutsideStaff)
                             || System.Math.Abs(
                                    ToDouble(initialOutsideStaff, 0.0)
                                    - ToDouble(lastInitialOutsideStaff, 0.0))
                                < 0.001)
                    {
                        g.SetProperty(
                            OutsideStaffPrioritySymbol,
                            ToDouble(lastOutsideStaff, 0.0) + 0.1);
                    }
                }

                last = g;
                lastInitialOutsideStaff = initialOutsideStaff;
            }
        }
    }

    private static double ToDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "outside-staff-priority")
            : fallback;

    // scm_stable_sort_x. An insertion sort is stable by construction, and these lists hold
    // the scripts on one note head — single digits, never enough for the order to cost.
    private static void StableSort(List<Grob> items, System.Func<Grob, Grob, bool> less)
    {
        for (int i = 1; i < items.Count; i++)
        {
            Grob current = items[i];
            int j = i - 1;
            while (j >= 0 && less(current, items[j]))
            {
                items[j + 1] = items[j];
                j--;
            }

            items[j + 1] = current;
        }
    }
}
