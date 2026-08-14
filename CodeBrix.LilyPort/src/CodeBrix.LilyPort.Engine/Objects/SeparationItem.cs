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

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/separation-item.cc, lily/include/separation-item.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.
// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - boxes reads each item's PURE Y extent, as upstream. The ordinary read it carried
//     ran during horizontal spacing and CACHED stencils (the StaffSymbol's among them)
//     computed over still-unplaced columns -- the root of the collapsed-staff-line
//     defect. See PORT-COVERAGE.

/// <summary>
/// Collects the items a paper column has to keep clear of its neighbours.
/// <para>
/// A column's horizontal extent and its horizontal skylines are both computed from
/// this set, which is why an item that never reaches it costs the column all of its
/// width — the column then looks empty to the spacing pipeline.
/// </para>
/// <para>
/// The skylines are the whole point: two columns may come as close as their FACING
/// skylines touch, which is far closer than their bounding boxes would allow. A flat
/// sign under a note head and a dot above one nest together precisely because the
/// distance is measured profile-to-profile.
/// </para>
/// </summary>
public static class SeparationItem
{
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol ConditionalElements = Symbol.Intern("conditional-elements");
    private static readonly Symbol HorizontalSkylines = Symbol.Intern("horizontal-skylines");
    private static readonly Symbol SkylineVerticalPadding
        = Symbol.Intern("skyline-vertical-padding");

    private static readonly Symbol ExtraSpacingWidth = Symbol.Intern("extra-spacing-width");
    private static readonly Symbol ExtraSpacingHeight = Symbol.Intern("extra-spacing-height");
    private static readonly Symbol AxisGroupInterfaceSymbol = Symbol.Intern("axis-group-interface");
    private static readonly Symbol AccidentalPlacementInterface
        = Symbol.Intern("accidental-placement-interface");

    /// <summary>Records an item as something the column must make room for.</summary>
    /// <param name="column">The column.</param>
    /// <param name="item">The item.</param>
    public static void AddItem(Grob column, Item item)
    {
        if (column == null || item == null)
        {
            return;
        }

        PointerGroupInterface.AddGrob(column, ElementsSymbol, item);
    }

    /// <summary>
    /// Records an item whose contribution to the column's width DEPENDS on what sits to
    /// the left of it — an accidental that may or may not be printed, or an arpeggio.
    /// </summary>
    /// <param name="me">The column.</param>
    /// <param name="element">The conditional item.</param>
    public static void AddConditionalItem(Grob me, Grob element)
    {
        if (me == null || element == null)
        {
            return;
        }

        PointerGroupInterface.AddGrob(me, ConditionalElements, element);
    }

    /// <summary>
    /// Records the rod that keeps two items apart, and returns the distance it stated.
    /// <para>
    /// The right item's skyline is merged with its CONDITIONAL skyline as seen from the
    /// left item, because whether its accidentals count towards the distance depends on
    /// what is over there.
    /// </para>
    /// </summary>
    /// <param name="l">The left item.</param>
    /// <param name="r">The right item.</param>
    /// <param name="padding">The padding to insist on beyond touching.</param>
    /// <returns>The distance, never negative.</returns>
    public static double SetDistance(Item l, Item r, double padding)
    {
        SkylinePair leftLines = SkylinesOf(l);
        SkylinePair rightLines = SkylinesOf(r);

        Skyline right = ConditionalSkyline(r, l);
        right.Merge(rightLines[Direction.Negative]);

        double dist = padding + leftLines[Direction.Positive].Distance(right);
        if (dist > 0)
        {
            Rod rod = new Rod(l, r) { Distance = dist };
            rod.AddToColumns();
        }

        return Math.Max(dist, 0.0);
    }

    /// <summary>Determines whether a separation item occupies no width at all.</summary>
    /// <param name="me">The separation item.</param>
    /// <returns><see langword="true"/> when its skylines are empty.</returns>
    public static bool IsEmpty(Grob me) => SkylinesOf(me).IsEmpty;

    /// <summary>
    /// Returns the width of a separation item as seen from something on its left — the
    /// skyline built from its CONDITIONAL elements only.
    /// </summary>
    /// <param name="me">The separation item.</param>
    /// <param name="left">The grob on the left.</param>
    /// <returns>The conditional skyline.</returns>
    public static Skyline ConditionalSkyline(Grob me, Grob left)
    {
        List<Box> bs = Boxes(me, left);
        return new Skyline(bs, Axis.Y, Direction.Negative);
    }

    /// <summary>
    /// Computes a separation item's pair of horizontal skylines: the profile it shows to
    /// the left and the one it shows to the right.
    /// <para>
    /// Registered as <c>ly:separation-item::calc-skylines</c>, which is what every paper
    /// column's <c>horizontal-skylines</c> resolves to.
    /// </para>
    /// </summary>
    /// <param name="me">The separation item.</param>
    /// <returns>The skyline pair, in its Scheme cons form.</returns>
    public static object CalcSkylines(Grob me)
    {
        if (me == null)
        {
            throw new ArgumentNullException(nameof(me));
        }

        List<Box> bs = Boxes(me, null);
        SkylinePair sp = new SkylinePair(bs, Axis.Y);

        /*
          TODO: We need to decide if padding is 'intrinsic'
          to a skyline or if it is something that is only added on in
          distance calculations.  Here, we make it intrinsic, which copies
          the behavior from the old code but no longer corresponds to how
          vertical skylines are handled (where padding is not built into
          the skyline).
        */
        double vp = RobustDouble(me.GetProperty(SkylineVerticalPadding), 0.0);
        return new SkylinePair(
            sp[Direction.Negative].Padded(vp),
            sp[Direction.Positive].Padded(vp)).ToScheme();
    }

    /// <summary>
    /// Returns one box per contained grob, which is what the skylines are built from.
    /// <para>
    /// A box PER GROB, never one box around them all: a single bounding box would fill
    /// in every gap the profile is supposed to expose, and the nesting that makes
    /// horizontal skylines worth computing would be lost. That is why axis groups are
    /// skipped — their members are already in the list on their own account.
    /// </para>
    /// <para>
    /// With a non-null <paramref name="left"/> upstream filters the accidentals
    /// through <c>Accidental_placement::get_relevant_accidentals</c>. The split below
    /// does the same: grobs carrying <c>accidental-placement-interface</c> go through
    /// <see cref="RelevantAccidentals"/> and the rest take the unfiltered branch.
    /// </para>
    /// </summary>
    /// <param name="me">The separation item.</param>
    /// <param name="left">The grob on the left, or <see langword="null"/> for the
    /// unconditional elements.</param>
    /// <returns>The boxes.</returns>
    public static List<Box> Boxes(Grob me, Grob left)
    {
        List<Box> output = new List<Box>();
        if (!(me is Item item))
        {
            return output;
        }

        PaperColumn pc = item.GetColumn();
        if (pc == null)
        {
            return output;
        }

        IReadOnlyList<Grob> readOnlyElements = PointerGroupInterface.ExtractGrobSet(
            me, left != null ? ConditionalElements : ElementsSymbol);

        List<Grob> elements;
        if (left != null)
        {
            List<Grob> accidentalElements = new List<Grob>();
            List<Grob> otherElements = new List<Grob>(); // for now only arpeggios
            foreach (Grob element in readOnlyElements)
            {
                if (element.HasInterface(AccidentalPlacementInterface))
                {
                    accidentalElements.Add(element);
                }
                else
                {
                    otherElements.Add(element);
                }
            }

            elements = RelevantAccidentals(accidentalElements, left);
            elements.AddRange(otherElements);
        }
        else
        {
            elements = new List<Grob>(readOnlyElements);
        }

        Grob ycommon = AxisGroupInterface.CommonRefpointOfArray(elements, me, Axis.Y);

        foreach (Grob element in elements)
        {
            if (!(element is Item il) || !ReferenceEquals(pc, il.GetColumn()))
            {
                continue;
            }

            // Exclude groups of grobs, so as to insert a box for each contained grob
            // into the skyline instead of a single box that bounds all of them.
            if (il.HasInterface(AxisGroupInterfaceSymbol))
            {
                continue;
            }

            // Upstream reads the PURE height here (`il->pure_y_extent (ycommon, 0,
            // very_large)`): boxes are built during horizontal spacing, BEFORE line
            // breaking, and the ordinary Y extent drags side-position -> skyline ->
            // stencil in and CACHES stencils computed over still-unplaced columns.
            Interval y = il.PureYExtent(ycommon, 0, int.MaxValue);
            Interval x = il.Extent(pc, Axis.X);

            Interval extraWidth = Grob.TryNumberPair(element.GetProperty(ExtraSpacingWidth), out Interval ew)
                ? ew
                : new Interval(-0.1, 0.1);
            Interval extraHeight = Grob.TryNumberPair(element.GetProperty(ExtraSpacingHeight), out Interval eh)
                ? eh
                : new Interval(0.0, 0.0);

            // The conventional empty extent is (+inf.0 . -inf.0)
            //  but (-inf.0 . +inf.0) is used as extra-spacing-height
            //  on items that must not overlap other note-columns.
            // If these two uses of inf combine, leave the empty extent.
            if (!double.IsInfinity(x.Left))
            {
                x.Left += extraWidth.Left;
            }

            if (!double.IsInfinity(x.Right))
            {
                x.Right += extraWidth.Right;
            }

            if (!double.IsInfinity(y.Left))
            {
                y.Left += extraHeight.Left;
            }

            if (!double.IsInfinity(y.Right))
            {
                y.Right += extraHeight.Right;
            }

            if (!x.IsEmpty && !y.IsEmpty)
            {
                output.Add(new Box(x, y));
            }
        }

        return output;
    }

    /// <summary>
    /// Reads a grob's horizontal skylines, answering an empty pair when it carries none.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The skyline pair.</returns>
    private static SkylinePair SkylinesOf(Grob grob)
        => SkylinePair.FromScheme(grob?.GetProperty(HorizontalSkylines)) ?? new SkylinePair();

    /// <summary>
    /// <c>Accidental_placement::get_relevant_accidentals</c> — the seam closed when
    /// accidental placement landed.
    /// </summary>
    private static List<Grob> RelevantAccidentals(List<Grob> accidentals, Grob left)
        => AccidentalPlacement.GetRelevantAccidentals(accidentals, left);

    private static double RobustDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "separation item")
            : fallback;
}
