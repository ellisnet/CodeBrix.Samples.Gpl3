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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/axis-group-interface.cc, lily/include/axis-group-interface.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// Groups grobs so that they move together on one or both axes.
/// <para>
/// Adding a grob to an axis group makes the group its reference point on each of the
/// group's axes — but only where the grob does not already have one, which is what
/// lets a note head take its horizontal reference from a paper column and its
/// vertical one from a staff.
/// </para>
/// </summary>
public static class AxisGroupInterface
{
    private static readonly Symbol AxesSymbol = Symbol.Intern("axes");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol AxisGroupParentX = Symbol.Intern("axis-group-parent-X");
    private static readonly Symbol AxisGroupParentY = Symbol.Intern("axis-group-parent-Y");
    private static readonly Symbol CrossStaffSymbol = Symbol.Intern("cross-staff");

    /// <summary>Adds a grob to an axis group.</summary>
    /// <param name="group">The group.</param>
    /// <param name="element">The grob to add.</param>
    public static void AddElement(Grob group, Grob element)
    {
        List<Axis> axes = ReadAxes(group);
        if (axes.Count == 0)
        {
            Warn.ProgrammingError("axes should be nonempty");
        }

        foreach (Axis axis in axes)
        {
            if (element.GetParent(axis) == null)
            {
                element.SetParent(group, axis);
            }

            element.SetObject(axis == Axis.X ? AxisGroupParentX : AxisGroupParentY, group);
        }

        /* must be ordered, because Align_interface also uses
           Axis_group_interface  */
        PointerGroupInterface.AddGrob(group, ElementsSymbol, element);
    }

    /// <summary>Determines whether a group spans an axis.</summary>
    /// <param name="group">The group.</param>
    /// <param name="axis">The axis to test.</param>
    /// <returns><see langword="true"/> when the group covers that axis.</returns>
    public static bool HasAxis(Grob group, Axis axis) => ReadAxes(group).Contains(axis);

    /// <summary>Returns the grobs in a group.</summary>
    /// <param name="group">The group.</param>
    /// <returns>The elements.</returns>
    public static IReadOnlyList<Grob> Elements(Grob group)
        => PointerGroupInterface.ExtractGrobSet(group, ElementsSymbol);

    /// <summary>
    /// Returns a group's extent on one axis: the union of its elements' extents,
    /// measured against a common reference point.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="reference">The reference grob to measure against.</param>
    /// <param name="axis">The axis to measure.</param>
    /// <returns>The extent, empty when the group has no elements with extent.</returns>
    public static Interval RelativeGroupExtent(Grob group, Grob reference, Axis axis)
    {
        Interval result = Interval.Empty;
        foreach (Grob element in Elements(group))
        {
            Interval extent = element.Extent(reference, axis);
            if (!extent.IsEmpty)
            {
                result.Unite(extent);
            }
        }

        return result;
    }

    /// <summary>
    /// The <c>X-extent</c> and <c>Y-extent</c> callback for an axis group: the union
    /// of its elements, measured against the group itself.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="axis">The axis to measure.</param>
    /// <returns>The extent.</returns>
    public static Interval GroupExtent(Grob group, Axis axis)
        => RelativeGroupExtent(group, group, axis);

    /// <summary>
    /// Returns the grob every member of a set shares as a reference point on one axis.
    /// </summary>
    /// <param name="elements">The grobs.</param>
    /// <param name="common">A grob to start from, which is included in the search.</param>
    /// <param name="axis">The axis whose parent chain to walk.</param>
    /// <returns>The common reference point.</returns>
    public static Grob CommonRefpointOfArray(IReadOnlyList<Grob> elements, Grob common, Axis axis)
    {
        Grob result = common;
        foreach (Grob element in elements)
        {
            result = result == null ? element : result.CommonRefpoint(element, axis);
        }

        return result;
    }

    /// <summary>
    /// The real <c>X-extent</c>/<c>Y-extent</c> callback: the union of the elements'
    /// extents, measured against the reference point they all share and then expressed
    /// relative to the group.
    /// <para>
    /// Measuring against the COMMON reference point rather than against the group is
    /// what makes this correct when an element's parent chain does not run through the
    /// group — which happens routinely, since a note head takes its horizontal
    /// reference from a paper column and its vertical one from a staff.
    /// </para>
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="axis">The axis to measure.</param>
    /// <returns>The extent, relative to the group.</returns>
    public static Interval GenericGroupExtent(Grob group, Axis axis)
    {
        IReadOnlyList<Grob> elements = Elements(group);

        Grob common = CommonRefpointOfArray(elements, group, axis);

        double myCoordinate = group.RelativeCoordinate(common, axis);
        Interval r = RelativeGroupExtentOf(elements, common, axis);

        return r - myCoordinate;
    }

    /// <summary>
    /// Returns the union of a set of grobs' extents, measured against a reference
    /// point. Cross-staff grobs are skipped: their extent belongs to whichever staff
    /// they reach into, not to this group.
    /// </summary>
    /// <param name="elements">The grobs.</param>
    /// <param name="common">The reference grob.</param>
    /// <param name="axis">The axis to measure.</param>
    /// <returns>The extent.</returns>
    public static Interval RelativeGroupExtentOf(
        IReadOnlyList<Grob> elements,
        Grob common,
        Axis axis)
    {
        Interval r = Interval.Empty;
        foreach (Grob element in elements)
        {
            if (SchemeUtilities.ToBool(element.GetProperty(CrossStaffSymbol)))
            {
                continue;
            }

            Interval dims = element.Extent(common, axis);
            if (!dims.IsEmpty)
            {
                r.Unite(dims);
            }
        }

        return r;
    }

    /// <summary>
    /// Returns the extent of just the part of a group that belongs to one staff.
    /// </summary>
    /// <remarks>
    /// EPG11 (2026-08-08) carried this: <c>axis-group-interface.cc</c>'s ledger row has
    /// said <c>ported</c> since EPG0, but this function had never come across, because
    /// <c>Tie_formatting_problem::set_column_chord_outline</c> is its only caller in the
    /// whole engine and no tie had ever been formatted.
    /// </remarks>
    /// <param name="me">The group.</param>
    /// <param name="refp">The reference grob to measure against.</param>
    /// <param name="extAxis">The axis to measure.</param>
    /// <param name="staff">The staff whose elements to keep.</param>
    /// <param name="parentAxis">The axis whose parent chain decides staff membership.</param>
    /// <returns>The extent of the elements that descend from the staff.</returns>
    public static Interval StaffExtent(
        Grob me, Grob refp, Axis extAxis, Grob staff, Axis parentAxis)
    {
        IReadOnlyList<Grob> elts = Elements(me);
        List<Grob> newElts = new List<Grob>();

        for (int i = 0; i < elts.Count; i++)
        {
            if (elts[i].HasInAncestry(staff, parentAxis))
            {
                newElts.Add(elts[i]);
            }
        }

        return RelativeGroupExtentOf(newElts, refp, extAxis);
    }

    private static List<Axis> ReadAxes(Grob group)
    {
        List<Axis> axes = new List<Axis>();
        object cursor = group?.GetProperty(AxesSymbol);
        while (cursor is Pair pair)
        {
            switch (pair.Car)
            {
                case long value:
                    axes.Add(value == 0 ? Axis.X : Axis.Y);
                    break;
                case int value:
                    axes.Add(value == 0 ? Axis.X : Axis.Y);
                    break;
                default:
                    break;
            }

            cursor = pair.Cdr;
        }

        return axes;
    }
}
