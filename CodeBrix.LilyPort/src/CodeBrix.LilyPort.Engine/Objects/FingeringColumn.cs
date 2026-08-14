/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2011--2026 Mike Solomon <mike@apollinemike.com>

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

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/fingering-column.cc, lily/include/fingering-column.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - upstream's Fingering_and_offset struct becomes a value tuple; it exists only to be
//     sorted by its offset, and the sort is stable here where std::sort is not — see the
//     note on SortByOffset.

/// <summary>
/// Makes sure that fingerings placed laterally do not collide and that they are flush
/// if necessary.
/// </summary>
public static class FingeringColumn
{
    private static readonly Symbol FingeringsSymbol = Symbol.Intern("fingerings");
    private static readonly Symbol PositioningDoneSymbol = Symbol.Intern("positioning-done");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol SnapRadiusSymbol = Symbol.Intern("snap-radius");
    private static readonly Symbol YOffsetSymbol = Symbol.Intern("Y-offset");
    private static readonly Symbol XParentPositioningSymbol
        = Symbol.Intern("ly:grob::x-parent-positioning");

    /// <summary>
    /// The <c>positioning-done</c> callback: stack the fingerings clear of each other,
    /// then flush the ones that nearly line up.
    /// </summary>
    /// <param name="me">The fingering column.</param>
    /// <returns>Always <see langword="true"/>, as upstream.</returns>
    public static object CalcPositioningDone(Grob me)
    {
        if (!me.IsLive)
        {
            return true;
        }

        me.SetProperty(PositioningDoneSymbol, true);

        DoYPositioning(me);
        DoXPositioning(me);

        return true;
    }

    /// <summary>Stacks the fingerings vertically so they do not overlap.</summary>
    /// <param name="me">The fingering column.</param>
    public static void DoYPositioning(Grob me)
    {
        IReadOnlyList<Grob> constFingerings
            = PointerGroupInterface.ExtractGrobSet(me, FingeringsSymbol);
        if (constFingerings.Count < 2)
        {
            me.ProgrammingError("This FingeringColumn should have never been created.");
            return;
        }

        List<Grob> fingerings = new List<Grob>(constFingerings);

        Grob[] common =
        {
            AxisGroupInterface.CommonRefpointOfArray(fingerings, me, Axis.X),
            AxisGroupInterface.CommonRefpointOfArray(fingerings, me, Axis.Y),
        };

        double padding = ToDouble(me.GetProperty(PaddingSymbol), 0.2);

        // order the fingerings from bottom to top
        fingerings.Sort((a, b) =>
            StaffSymbolReferencer.PurePositionLess(a, b) ? -1
            : StaffSymbolReferencer.PurePositionLess(b, a) ? 1
            : 0);

        double[] shift = new double[fingerings.Count];

        // Try stacking the fingerings top-to-bottom, and then bottom-to-top.
        // Use the average of the resulting stacked locations as the final positions
        foreach (Direction d in new[] { Direction.Positive, Direction.Negative })
        {
            double stackEnd = -(int)d * double.PositiveInfinity;
            Interval prevXExt = Interval.Empty;
            for (int i = d == Direction.Positive ? 0 : fingerings.Count - 1;
                 i >= 0 && i < fingerings.Count;
                 i += (int)d)
            {
                Interval xExt = LooseColumns.RobustRelativeExtent(
                    fingerings[i], common[(int)Axis.X], Axis.X);
                Interval yExt = LooseColumns.RobustRelativeExtent(
                    fingerings[i], fingerings[i], Axis.Y);
                double parentY = fingerings[i].ParentRelative(common[(int)Axis.Y], Axis.Y);

                // Checking only between sequential neighbors, seems good enough
                if (!Interval.Intersection(xExt, prevXExt).IsEmpty)
                {
                    stackEnd += (int)d * (yExt.Length + padding);
                }

                // MinMax returns whichever is further along in direction d
                stackEnd = Direction.MinMax(d, stackEnd, parentY);
                shift[i] += 0.5 * (stackEnd - yExt[d] - parentY);
                prevXExt = xExt;
            }
        }

        for (int i = 0; i < fingerings.Count; i++)
        {
            fingerings[i].TranslateAxis(shift[i], Axis.Y);
        }
    }

    /// <summary>Flushes the fingerings that nearly share a horizontal position.</summary>
    /// <param name="me">The fingering column.</param>
    public static void DoXPositioning(Grob me)
    {
        IReadOnlyList<Grob> fingerings
            = PointerGroupInterface.ExtractGrobSet(me, FingeringsSymbol);
        if (fingerings.Count == 0)
        {
            return;
        }

        Grob commonX = AxisGroupInterface.CommonRefpointOfArray(fingerings, me, Axis.X);
        double snap = ToDouble(me.GetProperty(SnapRadiusSymbol), 0.3);

        List<(Grob Fingering, double Offset)> fos
            = new List<(Grob, double)>(fingerings.Count);
        for (int i = 0; i < fingerings.Count; i++)
        {
            fos.Add((fingerings[i], fingerings[i].RelativeCoordinate(commonX, Axis.X)));
        }

        SortByOffset(fos);

        Direction dir = DirectionalElementInterface.GetGrobDirection(fingerings[0]);
        if (dir == Direction.Positive)
        {
            fos.Reverse();
        }

        double prev = double.PositiveInfinity * (int)dir;
        const double Eps = 1.0e-5;
        for (int i = 0; i < fos.Count; i++)
        {
            if (Math.Abs(fos[i].Offset - prev) < snap
                && Math.Abs(fos[i].Offset - prev) > Eps)
            {
                fos[i] = (fos[i].Fingering, prev);
            }

            prev = fos[i].Offset;
        }

        for (int i = 0; i < fos.Count; i++)
        {
            fos[i].Fingering.TranslateAxis(
                fos[i].Offset - fos[i].Fingering.RelativeCoordinate(commonX, Axis.X),
                Axis.X);
        }
    }

    /// <summary>Adds a fingering to a column, and hangs it off the column horizontally.</summary>
    /// <param name="fc">The fingering column.</param>
    /// <param name="f">The fingering.</param>
    public static void AddFingering(Grob fc, Grob f)
    {
        PointerGroupInterface.AddGrob(fc, FingeringsSymbol, f);
        f.XParent = fc;

        object parentPositioning = LilyPondScheme.LookupProcedure(XParentPositioningSymbol);
        if (parentPositioning == null)
        {
            Warn.ProgrammingError("ly:grob::x-parent-positioning is not defined");
            return;
        }

        f.SetProperty(YOffsetSymbol, parentPositioning);
    }

    // std::sort with fingering_and_offset_less. An insertion sort is stable where
    // std::sort is not, so equal offsets keep the order the grob array gave them rather
    // than an unspecified one — which is the only behaviour a second implementation could
    // reproduce, and the flushing loop below reads neighbours in order.
    private static void SortByOffset(List<(Grob Fingering, double Offset)> items)
    {
        for (int i = 1; i < items.Count; i++)
        {
            (Grob Fingering, double Offset) current = items[i];
            int j = i - 1;
            while (j >= 0 && current.Offset < items[j].Offset)
            {
                items[j + 1] = items[j];
                j--;
            }

            items[j + 1] = current;
        }
    }

    private static double ToDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "fingering-column")
            : fallback;
}
