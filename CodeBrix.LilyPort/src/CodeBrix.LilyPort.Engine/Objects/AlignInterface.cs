/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2000--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/align-interface.cc, lily/include/align-interface.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// Stacks grobs — staves, lyric lines, whole systems' worth of material — along one
/// axis, each moved just far enough from the previous one that their skylines clear.
/// <para>
/// This is what turns a pile of <c>VerticalAxisGroup</c>s all sitting at y = 0 into
/// staves one below another. The element order IS the stacking order, which is why
/// <c>Axis_group_interface::add_element</c>'s comment insists the element list stays
/// ordered.
/// </para>
/// <para>
/// NAMING: upstream's <c>Align_interface::axis</c> is <see cref="GetAxis"/> here — a
/// method named <c>Axis</c> would shadow the <c>Axis</c> type for the whole class.
/// </para>
/// </summary>
public static class AlignInterface
{
    private static readonly Symbol PositioningDoneSymbol = Symbol.Intern("positioning-done");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol AxesSymbol = Symbol.Intern("axes");
    private static readonly Symbol StackingDirSymbol = Symbol.Intern("stacking-dir");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol MinimumDistanceSymbol = Symbol.Intern("minimum-distance");
    private static readonly Symbol BasicDistanceSymbol = Symbol.Intern("basic-distance");
    private static readonly Symbol MinimumTranslationsAlistSymbol
        = Symbol.Intern("minimum-translations-alist");

    /// <summary>
    /// The <c>ly:align-interface::align-to-minimum-distances</c> callback: mark the
    /// positioning as done FIRST — the translation reads the elements' offsets, and
    /// without the marker each read would re-enter this callback — then stack the
    /// elements at their minimum distances.
    /// </summary>
    /// <param name="me">The alignment grob.</param>
    public static void AlignToMinimumDistances(Grob me)
    {
        me.SetProperty(PositioningDoneSymbol, true);

        AlignElementsToMinimumDistances(me, GetAxis(me));
    }

    /// <summary>
    /// The <c>ly:align-interface::align-to-ideal-distances</c> callback.
    /// </summary>
    /// <param name="me">The alignment grob.</param>
    public static void AlignToIdealDistances(Grob me)
    {
        me.SetProperty(PositioningDoneSymbol, true);

        AlignElementsToIdealDistances(me);
    }

    /* Return upper and lower skylines for VerticalAxisGroup g. If the extent
       is non-empty but there is no skyline available (or pure is true), just
       create a flat skyline from the bounding box */
    private static SkylinePair GetSkylines(
        Grob g,
        Axis a,
        Grob otherCommon,
        bool pure,
        int start,
        int end)
    {
        if (!pure)
        {
            // The read goes through ReadSkylinePair, which stands in for the
            // constructor-default skyline callbacks upstream can always rely on.
            SkylinePair skylines = AxisGroupInterfaceVertical.ReadSkylinePair(g, a);

            /* This skyline was calculated relative to the grob g. In order to compare it to
            skylines belonging to other grobs, we need to shift it so that it is relative
            to the common reference. */
            double offset = g.RelativeCoordinate(otherCommon, OtherAxis(a));
            skylines.Shift(offset);

            return skylines;
        }

        // Upstream first asks Hara_kiri_group_spanner::request_suicide whether the
        // group vanishes over [start, end); suicide is deliberately unported (the
        // EPG3 hara-kiri note), so no group ever requests it here.
        //
        // The rest of the pure branch reads g->pure_y_extent and refines it through
        // Axis_group_interface::rest_of_line_pure_height /
        // begin_of_line_pure_height — all EPG15 pure machinery. The port answers the
        // ORDINARY extent as one flat box, which is upstream's own shape for a grob
        // with no pure story. Recorded in PORT-COVERAGE.
        Interval extent = g.Extent(g, Axis.Y);
        List<Box> boxes = new List<Box>();

        if (!extent.IsEmpty)
        {
            boxes.Add(new Box(
                new Interval(0, double.PositiveInfinity), extent));
        }

        return new SkylinePair(boxes, Axis.X);
    }

    /// <summary>Returns the stacked positions of a set of elements, minimum distances
    /// and fixed spacing included.</summary>
    /// <param name="me">The alignment grob.</param>
    /// <param name="allGrobs">The elements, in stacking order.</param>
    /// <param name="a">The axis to stack along.</param>
    /// <returns>One translation per element.</returns>
    public static List<double> GetMinimumTranslations(
        Grob me,
        IReadOnlyList<Grob> allGrobs,
        Axis a)
    {
        return InternalGetMinimumTranslations(me, allGrobs, a, true, false, 0, 0);
    }

    /// <summary>Returns the stacked positions as seen BEFORE line breaking.</summary>
    /// <param name="me">The alignment grob.</param>
    /// <param name="allGrobs">The elements, in stacking order.</param>
    /// <param name="a">The axis to stack along.</param>
    /// <param name="start">The starting column rank of the pure range.</param>
    /// <param name="end">The ending column rank of the pure range.</param>
    /// <returns>One translation per element.</returns>
    public static List<double> GetPureMinimumTranslations(
        Grob me,
        IReadOnlyList<Grob> allGrobs,
        Axis a,
        int start,
        int end)
    {
        return InternalGetMinimumTranslations(me, allGrobs, a, true, true, start, end);
    }

    /// <summary>Returns the stacked positions without the fixed-spacing constraints.</summary>
    /// <param name="me">The alignment grob.</param>
    /// <param name="allGrobs">The elements, in stacking order.</param>
    /// <param name="a">The axis to stack along.</param>
    /// <returns>One translation per element.</returns>
    public static List<double> GetMinimumTranslationsWithoutMinDist(
        Grob me,
        IReadOnlyList<Grob> allGrobs,
        Axis a)
    {
        return InternalGetMinimumTranslations(me, allGrobs, a, false, false, 0, 0);
    }

    // If include_fixed_spacing is false, the only constraints that will be measured
    // here are those that result from collisions (+ padding) and the spacing spec
    // between adjacent staves.
    // If include_fixed_spacing is true, constraints from line-break-system-details,
    // basic-distance+stretchable=0, and staff-staff-spacing of spaceable staves
    // with loose lines in between, are included as well.
    // - If you want to find the minimum height of a system, include_fixed_spacing should be true.
    // - If you're going to actually lay out the page, then it should be false (or
    //   else centered dynamics will break when there is a fixed alignment).

    /// <summary>The stacking loop itself. See the source comment for the two modes.</summary>
    /// <param name="me">The alignment grob.</param>
    /// <param name="elems">The elements, in stacking order.</param>
    /// <param name="a">The axis to stack along.</param>
    /// <param name="includeFixedSpacing">Whether forced alignment distances participate.</param>
    /// <param name="pure">Whether this is a pure (pre-line-breaking) computation.</param>
    /// <param name="start">The starting column rank of the pure range.</param>
    /// <param name="end">The ending column rank of the pure range.</param>
    /// <returns>One translation per element.</returns>
    public static List<double> InternalGetMinimumTranslations(
        Grob me,
        IReadOnlyList<Grob> elems,
        Axis a,
        bool includeFixedSpacing,
        bool pure,
        int start,
        int end)
    {
        if (!pure && a == Axis.Y && me is Spanner && me.GetSystem() == null)
        {
            Warn.ProgrammingError("vertical alignment called before line-breaking");
        }

        // check the cache
        if (pure)
        {
            object cached = AssocGetEqual(
                new Pair((long)start, (long)end),
                me.GetProperty(MinimumTranslationsAlistSymbol));
            if (!(cached is Nil))
            {
                return SchemeDoubleList(cached);
            }
        }

        // If include_fixed_spacing is true, we look at things like system-system-spacing
        // and alignment-distances, which only make sense for the toplevel
        // VerticalAlignment. If we aren't toplevel, we're working on something like
        // BassFigureAlignment and so we definitely don't want to include
        // alignment-distances!
        if (!(me.GetParent(Axis.Y) is SystemGrob))
        {
            includeFixedSpacing = false;
        }

        Direction stackingDir = DirectionalElementInterface.FromScheme(
            me.GetProperty(StackingDirSymbol), Direction.Negative);

        Grob otherCommon = AxisGroupInterface.CommonRefpointOfArray(elems, me, OtherAxis(a));

        double where = 0;
        double defaultPadding = NumberOr(me.GetProperty(PaddingSymbol), 0.0);
        List<double> translates = new List<double>();
        Skyline downSkyline = new Skyline(stackingDir);
        Grob lastNonemptyElement = null;
        double lastSpaceableElementPos = 0;
        Grob lastSpaceableElement = null;
        Skyline lastSpaceableSkyline = new Skyline(stackingDir);
        int spaceableCount = 0;
        for (int j = 0; j < elems.Count; j++)
        {
            double dy = 0;
            double padding = defaultPadding;

            SkylinePair skyline = GetSkylines(elems[j], a, otherCommon, pure, start, end);

            if (skyline.IsEmpty)
            {
                translates.Add(where);
                continue;
            }

            if (lastNonemptyElement == null)
            {
                dy = skyline[-stackingDir].MaxHeight() + padding;
                for (int k = j; k-- > 0;)
                {
                    translates[k] = stackingDir * dy;
                }
            }
            else
            {
                object spec = PageLayoutSpacing.GetSpacingSpec(
                    lastNonemptyElement, elems[j], pure, start, end);
                PageLayoutSpacing.ReadSpacingSpec(spec, PaddingSymbol, ref padding);

                dy = downSkyline.Distance(skyline[-stackingDir]) + padding;

                double specDistance = 0;
                if (PageLayoutSpacing.ReadSpacingSpec(
                        spec, MinimumDistanceSymbol, ref specDistance))
                {
                    dy = System.Math.Max(dy, specDistance);
                }

                // Consider the likely final spacing when estimating distance between
                // staves of the full score
                if (int.MaxValue == end && 0 == start
                    && PageLayoutSpacing.ReadSpacingSpec(
                        spec, BasicDistanceSymbol, ref specDistance))
                {
                    dy = System.Math.Max(dy, specDistance);
                }

                if (includeFixedSpacing && PageLayoutSpacing.IsSpaceable(elems[j])
                    && lastSpaceableElement != null)
                {
                    // Spaceable staves may have
                    // constraints coming from the previous spaceable staff
                    // as well as from the previous staff.
                    spec = PageLayoutSpacing.GetSpacingSpec(
                        lastSpaceableElement, elems[j], pure, start, end);
                    double spaceablePadding = 0;
                    PageLayoutSpacing.ReadSpacingSpec(
                        spec, PaddingSymbol, ref spaceablePadding);
                    dy = System.Math.Max(
                        dy,
                        lastSpaceableSkyline.Distance(skyline[-stackingDir])
                        + (stackingDir * (lastSpaceableElementPos - where))
                        + spaceablePadding);

                    double spaceableMinDistance = 0;
                    if (PageLayoutSpacing.ReadSpacingSpec(
                            spec, MinimumDistanceSymbol, ref spaceableMinDistance))
                    {
                        dy = System.Math.Max(
                            dy,
                            spaceableMinDistance
                            + (stackingDir * (lastSpaceableElementPos - where)));
                    }

                    dy = System.Math.Max(dy, PageLayoutSpacing.GetFixedSpacing(
                        lastSpaceableElement, elems[j], spaceableCount, pure, start,
                        end));
                }
            }

            dy = System.Math.Max(0.0, dy);
            downSkyline.Raise(-stackingDir * dy);
            downSkyline.Merge(skyline[stackingDir]);
            where += stackingDir * dy;
            translates.Add(where);

            if (PageLayoutSpacing.IsSpaceable(elems[j]))
            {
                spaceableCount++;
                lastSpaceableElement = elems[j];
                lastSpaceableElementPos = where;

                // Upstream copies by value here; Skyline is a class in this port, so
                // the copy has to be spelled out or the running merge would show
                // through the snapshot.
                lastSpaceableSkyline = downSkyline.Copy();
            }

            lastNonemptyElement = elems[j];
        }

        if (pure)
        {
            object mta = me.GetProperty(MinimumTranslationsAlistSymbol);
            mta = new Pair(
                new Pair(new Pair((long)start, (long)end), ToSchemeList(translates)),
                mta is Nil ? (object)Nil.Instance : mta);
            me.SetProperty(MinimumTranslationsAlistSymbol, mta);
        }

        return translates;
    }

    /// <summary>
    /// Stacks the elements at their IDEAL distances.
    /// <para>
    /// Upstream builds a <c>Page_layout_problem</c> over the system and takes its
    /// solution, which stretches every staff-to-staff spring toward its
    /// <c>basic-distance</c>. That solver is EPG16's; until it lands the port takes
    /// the MINIMUM distances instead — the same staves in the same order, packed
    /// rather than stretched. DELIBERATE STAND-IN, recorded in PORT-COVERAGE.
    /// </para>
    /// <para>
    /// Upstream also refuses outright when the alignment has no system yet, and can
    /// afford to: nothing upstream reads a staff's unpure Y position before line
    /// breaking — the early readers all go through the pure machinery (EPG15). This
    /// port's horizontal spacing DOES read early (the recorded EPG4 divergence in
    /// <c>Spacing_interface::skylines</c>), a grob's offset is computed exactly
    /// once, and a refusal would burn every staff's one-shot offset at zero. So the
    /// early call takes upstream's own currency for before-line-breaking questions:
    /// the PURE stacking, whose skylines are flat boxes over the groups' Y extents
    /// and which therefore computes no stencil whose shape depends on the
    /// not-yet-solved horizontal spacing. The full-score pure range also brings
    /// <c>basic-distance</c> in, exactly as upstream's pure estimate does. Recorded
    /// in PORT-COVERAGE.
    /// </para>
    /// </summary>
    /// <param name="me">The alignment grob.</param>
    public static void AlignElementsToIdealDistances(Grob me)
    {
        if (me.GetSystem() != null)
        {
            AlignElementsToMinimumDistances(me, Axis.Y);
            return;
        }

        IReadOnlyList<Grob> allGrobs = AxisGroupInterface.Elements(me);
        List<double> translates = GetPureMinimumTranslations(
            me, allGrobs, Axis.Y, 0, int.MaxValue);
        if (translates.Count > 0)
        {
            for (int j = 0; j < allGrobs.Count; j++)
            {
                allGrobs[j].TranslateAxis(translates[j], Axis.Y);
            }
        }
    }

    /// <summary>Stacks the elements at their minimum distances and moves each one there.</summary>
    /// <param name="me">The alignment grob.</param>
    /// <param name="a">The axis to stack along.</param>
    public static void AlignElementsToMinimumDistances(Grob me, Axis a)
    {
        IReadOnlyList<Grob> allGrobs = AxisGroupInterface.Elements(me);

        List<double> translates = GetMinimumTranslations(me, allGrobs, a);
        if (translates.Count > 0)
        {
            for (int j = 0; j < allGrobs.Count; j++)
            {
                allGrobs[j].TranslateAxis(translates[j], a);
            }
        }
    }

    /// <summary>
    /// Returns the pure translation of ONE child, which is how the pure-height
    /// machinery asks where a staff will roughly sit before line breaking.
    /// </summary>
    /// <param name="me">The alignment grob.</param>
    /// <param name="ch">The child to look for.</param>
    /// <param name="start">The starting column rank of the pure range.</param>
    /// <param name="end">The ending column rank of the pure range.</param>
    /// <returns>The translation, or zero.</returns>
    public static double GetPureChildYTranslation(Grob me, Grob ch, int start, int end)
    {
        IReadOnlyList<Grob> allGrobs = AxisGroupInterface.Elements(me);
        List<double> translates = GetPureMinimumTranslations(
            me, allGrobs, Axis.Y, start, end);

        if (translates.Count > 0)
        {
            for (int i = 0; i < allGrobs.Count; i++)
            {
                if (ReferenceEquals(allGrobs[i], ch))
                {
                    return translates[i];
                }
            }
        }
        else
        {
            return 0;
        }

        Warn.ProgrammingError(
            "tried to get a translation for something that is no child of mine");
        return 0;
    }

    /// <summary>Returns the axis an alignment stacks along: the first entry of its
    /// <c>axes</c> property.</summary>
    /// <param name="me">The alignment grob.</param>
    /// <returns>The axis.</returns>
    public static Axis GetAxis(Grob me)
    {
        object axes = me.GetProperty(AxesSymbol);
        if (axes is Pair pair)
        {
            switch (pair.Car)
            {
                case long l:
                    return l == 0 ? Axis.X : Axis.Y;
                case int i:
                    return i == 0 ? Axis.X : Axis.Y;
                default:
                    break;
            }
        }

        return Axis.X;
    }

    /// <summary>
    /// Adds an element to an alignment: plant the parent-positioning callback as its
    /// offset on the stacking axis — reading the element's position is then what runs
    /// the alignment — and hand the rest to
    /// <see cref="AxisGroupInterface.AddElement"/>.
    /// </summary>
    /// <param name="me">The alignment grob.</param>
    /// <param name="element">The element to add.</param>
    public static void AddElement(Grob me, Grob element)
    {
        Axis a = GetAxis(me);
        Symbol sym = GrobClosure.AxisOffsetSymbol(a);
        object proc = GrobClosure.AxisParentPositioning(a);

        if (proc != null)
        {
            element.SetProperty(sym, proc);
        }
        else
        {
            Warn.ProgrammingError(
                "axis parent positioning requested before the engine primitives "
                + "were installed");
        }

        AxisGroupInterface.AddElement(me, element);
    }

    private static Axis OtherAxis(Axis axis) => axis == Axis.X ? Axis.Y : Axis.X;

    private static double NumberOr(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "align-interface")
            : fallback;

    /// <summary>Looks a key up in an alist by <c>equal?</c>, answering the CDR.</summary>
    private static object AssocGetEqual(object key, object alist)
    {
        object cursor = alist;
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry && SchemeUtilities.IsEqual(entry.Car, key))
            {
                return entry.Cdr;
            }

            cursor = pair.Cdr;
        }

        return Nil.Instance;
    }

    private static List<double> SchemeDoubleList(object list)
    {
        List<double> result = new List<double>();
        object cursor = list;
        while (cursor is Pair pair)
        {
            result.Add(SchemeConvert.ToDouble(pair.Car, "minimum-translations-alist"));
            cursor = pair.Cdr;
        }

        return result;
    }

    private static object ToSchemeList(List<double> values)
    {
        object result = Nil.Instance;
        for (int i = values.Count - 1; i >= 0; i--)
        {
            result = new Pair(values[i], result);
        }

        return result;
    }
}
