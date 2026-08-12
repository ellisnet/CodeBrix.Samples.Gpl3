/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/self-alignment-interface.cc, lily/include/self-alignment-interface.hh, lily/paper-column.cc (get_interface_extent only);

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.
// Modified by Jeremy Ellis on 2026-08-11 as part of the CodeBrix port:
//   - aligned_on_self reads the MAYBE-PURE extent, as upstream; the EPG15-era
//     ordinary stand-in retired with its class. See PORT-COVERAGE, STAFF-LINES.

/// <summary>
/// Positions a grob on its own extent, on its parent's, or on both: the
/// <c>self-alignment-X/Y</c> and <c>parent-alignment-X/Y</c> properties are linear
/// combinations over the two extents, with -1 the left/bottom edge, 0 the centre and
/// 1 the right/top edge.
/// <para>
/// <c>Paper_column::get_interface_extent</c> is CARRIED HERE: the port's
/// <c>Objects/PaperColumn.cs</c> predates it and standing rules keep already-ported
/// files closed in this pass, so its one missing static lives beside its only caller.
/// Recorded in PORT-COVERAGE.
/// </para>
/// </summary>
public static class SelfAlignmentInterface
{
    private static readonly Symbol SelfAlignmentX = Symbol.Intern("self-alignment-X");
    private static readonly Symbol SelfAlignmentY = Symbol.Intern("self-alignment-Y");
    private static readonly Symbol ParentAlignmentX = Symbol.Intern("parent-alignment-X");
    private static readonly Symbol ParentAlignmentY = Symbol.Intern("parent-alignment-Y");
    private static readonly Symbol XAlignmentExtent = Symbol.Intern("X-alignment-extent");
    private static readonly Symbol XAlignOnMainNoteheads
        = Symbol.Intern("X-align-on-main-noteheads");

    private static readonly Symbol MainExtent = Symbol.Intern("main-extent");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol PaperColumnInterface = Symbol.Intern("paper-column-interface");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol AlignedOnXParentSymbol
        = Symbol.Intern("ly:self-alignment-interface::aligned-on-x-parent");

    private static readonly Symbol AlignedOnYParentSymbol
        = Symbol.Intern("ly:self-alignment-interface::aligned-on-y-parent");

    /// <summary>The <c>x-aligned-on-self</c> callback body.</summary>
    /// <param name="me">The grob.</param>
    /// <returns>The offset.</returns>
    public static double XAlignedOnSelf(Grob me)
        => AlignedOnSelf(me, Axis.X, false, 0, 0);

    /// <summary>The <c>y-aligned-on-self</c> callback body.</summary>
    /// <param name="me">The grob.</param>
    /// <returns>The offset.</returns>
    public static double YAlignedOnSelf(Grob me)
        => AlignedOnSelf(me, Axis.Y, false, 0, 0);

    /// <summary>The <c>pure-y-aligned-on-self</c> callback body.</summary>
    /// <param name="me">The grob.</param>
    /// <param name="start">The starting column rank of the pure range.</param>
    /// <param name="end">The ending column rank of the pure range.</param>
    /// <returns>The offset.</returns>
    public static double PureYAlignedOnSelf(Grob me, int start, int end)
        => AlignedOnSelf(me, Axis.Y, true, start, end);

    /// <summary>
    /// Aligns a grob on its own extent: the offset that puts the
    /// <c>self-alignment</c> point of the extent at the reference point.
    /// <para>
    /// The extent read is MAYBE-PURE, as upstream's
    /// <c>me-&gt;maybe_pure_extent (me, a, pure, start, end)</c>: the EPG15-era
    /// ordinary stand-in retired with the STAFF-LINES session (2026-08-11), because
    /// an ordinary read in the pure branch asks for a stencil during spacing and
    /// caches it over still-unplaced columns.
    /// </para>
    /// </summary>
    /// <param name="me">The grob.</param>
    /// <param name="a">The axis to align on.</param>
    /// <param name="pure">Whether this is a pure lookup.</param>
    /// <param name="start">The starting column rank of the pure range.</param>
    /// <param name="end">The ending column rank of the pure range.</param>
    /// <returns>The offset.</returns>
    public static double AlignedOnSelf(Grob me, Axis a, bool pure, int start, int end)
    {
        object align = a == Axis.X
            ? me.GetProperty(SelfAlignmentX)
            : me.GetProperty(SelfAlignmentY);
        if (SchemeConvert.IsNumber(align))
        {
            Interval ext = me.MaybePureExtent(me, a, pure, start, end);

            // Empty extent doesn't mean an error - we simply don't align such grobs.
            if (!ext.IsEmpty)
            {
                return -ext.LinearCombination(
                    SchemeConvert.ToDouble(align, "self-alignment"));
            }
        }

        return 0.0;
    }

    /// <summary>Returns the centre of a grob's own extent.</summary>
    /// <param name="me">The grob.</param>
    /// <param name="a">The axis to measure.</param>
    /// <returns>The centre.</returns>
    public static double CenteredOnSelf(Grob me, Axis a)
    {
        return LooseColumns.RobustRelativeExtent(me, me, a).Center;
    }

    /// <summary>The <c>centered-on-x-parent</c> callback body.</summary>
    /// <param name="me">The grob.</param>
    /// <returns>The offset.</returns>
    public static double CenteredOnXParent(Grob me)
        => CenteredOnSelf(me.GetParent(Axis.X), Axis.X);

    /// <summary>The <c>centered-on-y-parent</c> callback body.</summary>
    /// <param name="me">The grob.</param>
    /// <returns>The offset.</returns>
    public static double CenteredOnYParent(Grob me)
        => CenteredOnSelf(me.GetParent(Axis.Y), Axis.Y);

    /// <summary>
    /// Aligns a grob's reference point on its parent's, adjusting each side by its own
    /// alignment property.
    /// </summary>
    /// <param name="me">The grob.</param>
    /// <param name="a">The axis to align on.</param>
    /// <returns>The offset.</returns>
    public static double AlignedOnParent(Grob me, Axis a)
    {
        Grob him = me.GetParent(a);
        Interval he = Interval.Empty;
        if (him.HasInterface(PaperColumnInterface))
        {
            /*
              PaperColumn extents aren't reliable (they depend on size and alignment
              of PaperColumn's children), so we align on combined note heads instead.
              If there are no note heads, we use a placeholder extent, see regtest
              `paper-column-grob-alignment.ly` for more details.

              This situation happens for lyrics without `associatedVoice`, for
              example.
            */
            he = PaperColumnInterfaceExtent(him, NoteColumnInterface, a);
            if (he.IsEmpty && a == Axis.X)
            {
                object extScm = him.GetProperty(XAlignmentExtent);
                if (Grob.TryNumberPair(extScm, out Interval fallback))
                {
                    he = fallback;
                }
            }
        }
        else
        {
            if (SchemeUtilities.ToBool(me.GetProperty(XAlignOnMainNoteheads))
                && him.HasInterface(NoteColumnInterface))
            {
                Grob.TryNumberPair(him.GetProperty(MainExtent), out he);
            }
            else
            {
                he = him.Extent(him, a);
            }
        }

        object selfAlign = a == Axis.X
            ? me.GetProperty(SelfAlignmentX)
            : me.GetProperty(SelfAlignmentY);

        object parAlign = a == Axis.X
            ? me.GetProperty(ParentAlignmentX)
            : me.GetProperty(ParentAlignmentY);

        if (parAlign is Nil)
        {
            parAlign = selfAlign;
        }

        double x = 0.0;
        Interval ext = me.Extent(me, a);

        if (SchemeConvert.IsNumber(selfAlign))
        {
            // Empty extent doesn't mean an error - we simply don't align such grobs.
            if (!ext.IsEmpty)
            {
                x -= ext.LinearCombination(
                    SchemeConvert.ToDouble(selfAlign, "self-alignment"));
            }
        }

        if (SchemeConvert.IsNumber(parAlign))
        {
            if (!he.IsEmpty)
            {
                x += he.LinearCombination(
                    SchemeConvert.ToDouble(parAlign, "parent-alignment"));
            }
        }

        return x;
    }

    /// <summary>Adds the aligned-on-parent callback to a grob's offset on one axis.</summary>
    /// <param name="me">The grob.</param>
    /// <param name="a">The axis to align on.</param>
    public static void SetAlignedOnParent(Grob me, Axis a)
    {
        object proc = Bootstrap.LilyPondScheme.LookupProcedure(
            a == Axis.X ? AlignedOnXParentSymbol : AlignedOnYParentSymbol);
        if (proc == null)
        {
            Warn.ProgrammingError(
                "aligned-on-parent requested before the engine primitives were installed");
            return;
        }

        GrobClosure.AddOffsetCallback(me, proc, a);
    }

    /// <summary>
    /// <c>Paper_column::get_interface_extent</c>: the union of the extents of a
    /// column's elements carrying one interface — each measured relative to ITSELF,
    /// which is upstream 2.27.2's own arithmetic, oddity included.
    /// </summary>
    /// <param name="column">The paper column.</param>
    /// <param name="iface">The interface to filter by.</param>
    /// <param name="a">The axis to measure.</param>
    /// <returns>The extent.</returns>
    public static Interval PaperColumnInterfaceExtent(Grob column, Symbol iface, Axis a)
    {
        Interval extent = Interval.Empty;

        foreach (Grob element in PointerGroupInterface.ExtractGrobSet(column, ElementsSymbol))
        {
            if (element.HasInterface(iface))
            {
                extent.Unite(LooseColumns.RobustRelativeExtent(element, element, a));
            }
        }

        return extent;
    }
}
