/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/hara-kiri-group-spanner.cc, lily/include/hara-kiri-group-spanner.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - find_in_range is a FREE function upstream, declared in no header and used only here;
//     it is a private helper. Its recursion is kept rather than turned into a loop, because
//     the comment above it ("there is probably a way that doesn't involve re-implementing a
//     binary search") is the only record of why it exists at all.

/// <summary>
/// A staff that removes itself when it has nothing to say.
/// <para>
/// This is what makes <c>\RemoveEmptyStaves</c> work, and the ugly name is upstream's own
/// joke: the spanner commits suicide, taking its children with it. It matters most in
/// orchestral scores, where a page on which the piccolo rests entirely should not carry an
/// empty piccolo staff.
/// </para>
/// <para>
/// The decision is made PER LINE, which is why it belongs to line breaking: a staff can be
/// empty on one system and busy on the next, and the pure variant is asked the question
/// for every candidate line before any line is chosen. It is also collective — see
/// <c>keep-alive-with</c> and <c>make-dead-when</c>, which the
/// <c>Keep_alive_together_engraver</c> fills in so that a group of staves lives or dies
/// together.
/// </para>
/// </summary>
public static class HaraKiriGroupSpanner
{
    private static readonly Symbol MakeDeadWhenSymbol = Symbol.Intern("make-dead-when");
    private static readonly Symbol KeepAliveWithSymbol = Symbol.Intern("keep-alive-with");
    private static readonly Symbol RemoveEmptySymbol = Symbol.Intern("remove-empty");
    private static readonly Symbol RemoveFirstSymbol = Symbol.Intern("remove-first");
    private static readonly Symbol ImportantColumnRanksSymbol
        = Symbol.Intern("important-column-ranks");

    private static readonly Symbol ItemsWorthLivingSymbol = Symbol.Intern("items-worth-living");

    /// <summary>
    /// The group's vertical extent, having first decided whether the group should exist —
    /// <c>ly:hara-kiri-group-spanner::y-extent</c>.
    /// </summary>
    /// <param name="me">The group.</param>
    /// <returns>The extent.</returns>
    public static object YExtent(Grob me)
    {
        ConsiderSuicide(me);
        Interval extent = AxisGroupInterface.GenericGroupExtent(me, Axis.Y);
        return new Pair(extent.Left, extent.Right);
    }

    /// <summary>
    /// The group's skylines, having first decided whether the group should exist —
    /// <c>ly:hara-kiri-group-spanner::calc-skylines</c>.
    /// </summary>
    /// <param name="me">The group.</param>
    /// <returns>The skyline pair.</returns>
    public static object CalcSkylines(Grob me)
    {
        ConsiderSuicide(me);

        // Axis_group_interface::calc_skylines, which is SKYLINE_SPACING and not
        // combine_skylines: upstream's own comment separates the two, and combine is for
        // an axis group whose only children are other axis groups, i.e. VerticalAlignment.
        //
        // Both halves of this line were wrong when EPG15 first landed it (fixed at its
        // close-out, 2026-08-08): it called combine, and it answered the SkylinePair
        // OBJECT where the property holds a Scheme CONS of two skylines. There is no
        // skyline-pair type in Scheme -- ly:skyline-pair? is "a pair whose car and cdr are
        // skylines" -- so every answer failed its own property's type check and
        // vertical-skylines was left UNSET on every hara-kiri group in every score. The
        // symptom was a `Type check for vertical-skylines failed' programming error per
        // group per file, which is how it was found.
        return AxisGroupInterfaceVertical.SkylineSpacing(me).ToScheme();
    }

    /// <summary>
    /// The group's PURE vertical extent for a candidate line, which is empty when the
    /// group would remove itself on that line —
    /// <c>ly:hara-kiri-group-spanner::pure-height</c>.
    /// </summary>
    /// <param name="me">The group.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The extent.</returns>
    public static object PureHeight(Grob me, int start, int end)
    {
        if (RequestSuicide(me, start, end))
        {
            Interval empty = Interval.Empty;
            return new Pair(empty.Left, empty.Right);
        }

        Interval height = AxisGroupInterfacePure.PureGroupHeight(me, start, end);
        return new Pair(height.Left, height.Right);
    }

    /// <summary>
    /// Whether this group should remove itself on a line running from
    /// <paramref name="start"/> to <paramref name="end"/>, taking its allies into account.
    /// <para>
    /// The collective rules run in a fixed order. A live FOE that wants to stay forces this
    /// group to die (that is <c>make-dead-when</c>, which implements <c>remove-layer</c>
    /// priorities). Then, if this group would not die on its own, it lives. Then a live
    /// FRIEND that wants to stay keeps it alive.
    /// </para>
    /// </summary>
    /// <param name="me">The group.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns><see langword="true"/> when the group should remove itself.</returns>
    public static bool RequestSuicide(Grob me, int start, int end)
    {
        IReadOnlyList<Grob> foes = PointerGroupInterface.ExtractGrobSet(me, MakeDeadWhenSymbol);
        for (int i = 0; i < foes.Count; i++)
        {
            if (foes[i].IsLive && !RequestSuicideAlone(foes[i], start, end))
            {
                return true;
            }
        }

        if (!RequestSuicideAlone(me, start, end))
        {
            return false;
        }

        IReadOnlyList<Grob> friends = PointerGroupInterface.ExtractGrobSet(me, KeepAliveWithSymbol);
        for (int i = 0; i < friends.Count; ++i)
        {
            if (friends[i].IsLive && !RequestSuicideAlone(friends[i], start, end))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether this group alone, ignoring its allies, should remove itself on a given line.
    /// <para>
    /// The answer is driven by <c>important-column-ranks</c>, a SORTED vector of the column
    /// ranks at which this group has something worth showing. It is built lazily on the
    /// first ask — from <c>items-worth-living</c>, expanded to every rank each item spans,
    /// sorted and de-duplicated — and cached on the grob, because the pure pass asks this
    /// question once per candidate line.
    /// </para>
    /// <para>
    /// Two guards precede it. A group that is not <c>remove-empty</c> never dies; and one
    /// that is not <c>remove-first</c> never dies on the FIRST line, which is what keeps
    /// the instrument names on page one even for an instrument that is silent there.
    /// </para>
    /// </summary>
    /// <param name="me">The group.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns><see langword="true"/> when the group alone should remove itself.</returns>
    public static bool RequestSuicideAlone(Grob me, int start, int end)
    {
        if (!SchemeUtilities.ToBool(me.GetProperty(RemoveEmptySymbol)))
        {
            return false;
        }

        bool removeFirst = SchemeUtilities.ToBool(me.GetProperty(RemoveFirstSymbol));
        if (!removeFirst && start <= 0)
        {
            return false;
        }

        object important = me.GetProperty(ImportantColumnRanksSymbol);
        if (important is object[] vector)
        {
            int len = vector.Length;
            /* interval too small to find any relevant columns */
            if (end < 2 || end - start < 2)
            {
                return true;
            }

            if (FindInRange(vector, 0, len, start + 1, end - 1))
            {
                return false;
            }
        }
        else
        {
            /* build the important-columns-cache */
            IReadOnlyList<Grob> worth
                = PointerGroupInterface.ExtractGrobSet(me, ItemsWorthLivingSymbol);
            List<int> ranks = new List<int>();

            for (int i = 0; i < worth.Count; i++)
            {
                Slice iv = worth[i].SpannedColumnRankInterval();
                if (iv.IsEmpty)
                {
                    continue;
                }

                for (int j = iv.Left; j <= iv.Right; j++)
                {
                    ranks.Add(j);
                }
            }

            ranks.Sort();
            List<int> unique = new List<int>();
            foreach (int rank in ranks)
            {
                if (unique.Count == 0 || unique[unique.Count - 1] != rank)
                {
                    unique.Add(rank);
                }
            }

            object[] scmVec = new object[unique.Count];
            for (int i = 0; i < unique.Count; i++)
            {
                scmVec[i] = (long)unique[i];
            }

            me.SetProperty(ImportantColumnRanksSymbol, scmVec);

            return RequestSuicide(me, start, end);
        }

        return true;
    }

    /// <summary>
    /// The UNPURE question: whether this group should remove itself now that real
    /// positions exist.
    /// <para>
    /// It asks whether any item worth living is still live, rather than consulting column
    /// ranks — by this point the line is known, so the rank arithmetic has nothing left to
    /// estimate.
    /// </para>
    /// </summary>
    /// <param name="me">The group.</param>
    /// <returns><see langword="true"/> when the group should remove itself.</returns>
    public static bool UnpureRequestSuicide(Grob me)
    {
        IReadOnlyList<Grob> foes = PointerGroupInterface.ExtractGrobSet(me, MakeDeadWhenSymbol);
        for (int i = 0; i < foes.Count; i++)
        {
            if (foes[i].IsLive && !UnpureRequestSuicideAlone(foes[i]))
            {
                return true;
            }
        }

        if (!UnpureRequestSuicideAlone(me))
        {
            return false;
        }

        IReadOnlyList<Grob> friends = PointerGroupInterface.ExtractGrobSet(me, KeepAliveWithSymbol);
        for (int i = 0; i < friends.Count; ++i)
        {
            if (friends[i].IsLive && !UnpureRequestSuicideAlone(friends[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether this group alone should remove itself, now that real positions exist.
    /// </summary>
    /// <param name="me">The group.</param>
    /// <returns><see langword="true"/> when the group alone should remove itself.</returns>
    public static bool UnpureRequestSuicideAlone(Grob me)
    {
        if (!SchemeUtilities.ToBool(me.GetProperty(RemoveEmptySymbol)))
        {
            return false;
        }

        bool removeFirst = SchemeUtilities.ToBool(me.GetProperty(RemoveFirstSymbol));
        if (!removeFirst)
        {
            Spanner sp = me as Spanner;
            int left = 0;
            Item l = sp?.GetBound(Direction.Negative);
            if (l != null && l.GetColumn() != null)
            {
                left = l.GetColumn().Rank;
            }

            if (left <= 0)
            {
                return false;
            }
        }

        IReadOnlyList<Grob> worth = PointerGroupInterface.ExtractGrobSet(me, ItemsWorthLivingSymbol);
        foreach (Grob worthItem in worth)
        {
            if (worthItem.IsLive)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Removes the group and all its children when it has nothing worth showing.
    /// <para>
    /// Upstream's comment on the last line — "very appropriate name here :-)" — is about
    /// <c>me-&gt;suicide ()</c>, and is the reason the whole class is named as it is.
    /// </para>
    /// </summary>
    /// <param name="me">The group.</param>
    public static void ConsiderSuicide(Grob me)
    {
        if (!UnpureRequestSuicide(me))
        {
            return;
        }

        List<Grob> childs = new List<Grob>();
        AxisGroupInterface.GetChildren(me, childs);
        for (int i = 0; i < childs.Count; i++)
        {
            childs[i].Suicide();
        }

        me.Suicide();
    }

    /// <summary>
    /// Forces the suicide decision before anything asks for an offset —
    /// <c>ly:hara-kiri-group-spanner::force-hara-kiri-callback</c>.
    /// <para>
    /// Upstream's reason, kept: offsets and dimensions inside a hara-kiri group cannot be
    /// relied on until the group has decided whether it exists, so the decision is forced
    /// through a callback rather than left to whoever asks first.
    /// </para>
    /// </summary>
    /// <param name="me">The group.</param>
    /// <returns>Zero.</returns>
    public static object ForceHaraKiriCallback(Grob me)
    {
        ConsiderSuicide(me);
        return 0.0;
    }

    /// <summary>
    /// Forces the suicide decision in this grob's vertical PARENT —
    /// <c>ly:hara-kiri-group-spanner::force-hara-kiri-in-y-parent-callback</c>.
    /// </summary>
    /// <param name="daughter">The child grob.</param>
    /// <returns>Zero.</returns>
    public static object ForceHaraKiriInYParentCallback(Grob daughter)
    {
        Grob parent = daughter.GetParent(Axis.Y);
        if (parent != null)
        {
            ForceHaraKiriCallback(parent);
        }

        return 0.0;
    }

    /// <summary>Records that a grob is worth keeping this group alive for.</summary>
    /// <param name="me">The group.</param>
    /// <param name="n">The interesting grob.</param>
    public static void AddInterestingItem(Grob me, Grob n)
        => PointerGroupInterface.AddUnorderedGrob(me, ItemsWorthLivingSymbol, n);

    /// <summary>
    /// A binary search for any value in a sorted vector that falls within a range.
    /// <para>
    /// Upstream's comment is kept in spirit: "there is probably a way that doesn't involve
    /// re-implementing a binary search (I would love some proper closures right now)".
    /// </para>
    /// </summary>
    private static bool FindInRange(object[] vector, int low, int hi, int min, int max)
    {
        if (low >= hi)
        {
            return false;
        }

        int mid = low + ((hi - low) / 2);
        long val = SchemeConvert.IsNumber(vector[mid])
            ? (long)SchemeConvert.ToDouble(vector[mid], "important-column-rank")
            : 0;
        if (val >= min && val <= max)
        {
            return true;
        }

        if (val < min)
        {
            return FindInRange(vector, mid + 1, hi, min, max);
        }

        return FindInRange(vector, low, mid, min, max);
    }
}
