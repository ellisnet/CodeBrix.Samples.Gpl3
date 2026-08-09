/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2002--2026 Juergen Reuter <reuter@ipd.uka.de>

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

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/cluster.cc, lily/include/cluster.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - upstream's free function brew_cluster_piece is a private static here; it has no
//     header declaration and no caller outside this file.
//   - Cluster_beacon is declared in cluster.cc's own body rather than in a header, and
//     is a separate static class here for the same reason.

/// <summary>
/// A graphically drawn musical cluster.
/// </summary>
/// <remarks>
/// <para>
/// <c>padding</c> adds to the vertical extent of the shape (top and bottom).
/// </para>
/// <para>
/// The property <c>style</c> controls the shape of cluster segments. Valid values include
/// <c>leftsided-stairs</c>, <c>rightsided-stairs</c>, <c>centered-stairs</c> and
/// <c>ramp</c>.
/// </para>
/// </remarks>
public static class Cluster
{
    private static readonly Symbol ColumnsSymbol = Symbol.Intern("columns");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");

    /// <summary>
    /// The <c>cross-staff</c> callback: a cluster is cross-staff when its columns do not
    /// all share its own vertical parent.
    /// </summary>
    /// <param name="me">The cluster spanner.</param>
    /// <returns><c>#t</c> when it reaches outside its own staff.</returns>
    public static object CalcCrossStaff(Grob me)
    {
        IReadOnlyList<Grob> cols = PointerGroupInterface.ExtractGrobSet(me, ColumnsSymbol);
        Grob commony = AxisGroupInterface.CommonRefpointOfArray(cols, me, Axis.Y);

        return commony != me.YParent;
    }

    /// <summary>The <c>stencil</c> callback.</summary>
    /// <param name="me">The cluster spanner.</param>
    /// <returns>The stencil, or <c>'()</c> when the cluster has no columns.</returns>
    public static object Print(Grob me)
    {
        if (!(me is Spanner spanner))
        {
            return Nil.Instance;
        }

        DrulArray<Item> bounds = spanner.GetBounds();
        Item leftBound = bounds[Direction.Negative];
        Item rightBound = bounds[Direction.Positive];
        Grob commonx = leftBound.CommonRefpoint(rightBound, Axis.X);

        IReadOnlyList<Grob> cols = PointerGroupInterface.ExtractGrobSet(me, ColumnsSymbol);
        if (cols.Count == 0)
        {
            me.Warning("junking empty cluster");
            me.Suicide();

            return Nil.Instance;
        }

        commonx = AxisGroupInterface.CommonRefpointOfArray(cols, commonx, Axis.X);
        Grob commony = AxisGroupInterface.CommonRefpointOfArray(cols, me, Axis.Y);
        List<Offset> bottomPoints = new List<Offset>();
        List<Offset> topPoints = new List<Offset>();

        double leftCoord = leftBound.RelativeCoordinate(commonx, Axis.X);

        /*
          TODO: should we move the cluster a little to the right to be in
          line with the center of the note heads?

        */
        for (int i = 0; i < cols.Count; i++)
        {
            Grob col = cols[i];

            Interval yext = col.Extent(commony, Axis.Y);

            double x = col.RelativeCoordinate(commonx, Axis.X) - leftCoord;
            bottomPoints.Add(new Offset(x, yext[Direction.Negative]));
            topPoints.Add(new Offset(x, yext[Direction.Positive]));
        }

        /*
          Across a line break we anticipate on the next pitches.
        */
        Spanner next = spanner.BrokenNeighbor(Direction.Positive);
        if (next != null)
        {
            IReadOnlyList<Grob> nextCols
                = PointerGroupInterface.ExtractGrobSet(next, ColumnsSymbol);
            if (nextCols.Count > 0)
            {
                Grob nextCommony
                    = AxisGroupInterface.CommonRefpointOfArray(nextCols, next, Axis.Y);
                Grob col = nextCols[0];

                Interval v = col.Extent(nextCommony, Axis.Y);
                double x = rightBound.RelativeCoordinate(commonx, Axis.X) - leftCoord;

                bottomPoints.Add(new Offset(x, v[Direction.Negative]));
                topPoints.Add(new Offset(x, v[Direction.Positive]));
            }
        }

        Stencil outStencil = BrewClusterPiece(me, bottomPoints, topPoints);
        outStencil.TranslateAxis(-me.RelativeCoordinate(commony, Axis.Y), Axis.Y);
        return outStencil;
    }

    /*
      TODO: Add support for cubic spline segments.
     */
    private static Stencil BrewClusterPiece(
        Grob me, IReadOnlyList<Offset> bottomPoints, IReadOnlyList<Offset> topPoints)
    {
        double blotdiameter = StaffSymbolReferencer.StaffSpace(me) / 2;

        double padding = SchemeConvert.IsNumber(me.GetProperty(PaddingSymbol))
            ? SchemeConvert.ToDouble(me.GetProperty(PaddingSymbol), "cluster")
            : 0.0;

        Offset vpadding = new Offset(0, padding);
        Offset hpadding = new Offset(0.5 * blotdiameter, 0);
        Offset hvpadding = (0.5 * hpadding) + vpadding;

        object shapeScm = me.GetProperty(StyleSymbol);
        string shape;

        if (shapeScm is Symbol shapeSymbol)
        {
            shape = shapeSymbol.Name;
        }
        else
        {
            Warn.ProgrammingError("ClusterSpanner.style should be defined as a symbol.");
            me.Suicide();
            return new Stencil();
        }

        Stencil outStencil = new Stencil();
        List<Offset> points = new List<Offset>();
        int size = bottomPoints.Count;
        if (size <= 0)
        {
            Warn.ProgrammingError("no points provided");
        }
        else if (shape == "leftsided-stairs")
        {
            for (int i = 0; i < size - 1; i++)
            {
                Box box = new Box(Interval.Empty, Interval.Empty);
                box.AddPoint(bottomPoints[i] - hvpadding);
                box.AddPoint(
                    new Offset(topPoints[i + 1][Axis.X], topPoints[i][Axis.Y]) + hvpadding);
                outStencil.AddStencil(Lookup.RoundFilledBox(box, blotdiameter));
            }
        }
        else if (shape == "rightsided-stairs")
        {
            for (int i = 0; i < size - 1; i++)
            {
                Box box = new Box(Interval.Empty, Interval.Empty);
                box.AddPoint(
                    new Offset(bottomPoints[i][Axis.X], bottomPoints[i + 1][Axis.Y]) - hvpadding);
                box.AddPoint(topPoints[i + 1] + hvpadding);
                outStencil.AddStencil(Lookup.RoundFilledBox(box, blotdiameter));
            }
        }
        else if (shape == "centered-stairs")
        {
            double leftXmid = bottomPoints[0][Axis.X];
            for (int i = 0; i < size - 1; i++)
            {
                double rightXmidInner
                    = 0.5 * (bottomPoints[i][Axis.X] + bottomPoints[i + 1][Axis.X]);
                Box box = new Box(Interval.Empty, Interval.Empty);
                box.AddPoint(new Offset(leftXmid, bottomPoints[i][Axis.Y]) - hvpadding);
                box.AddPoint(new Offset(rightXmidInner, topPoints[i][Axis.Y]) + hvpadding);
                outStencil.AddStencil(Lookup.RoundFilledBox(box, blotdiameter));
                leftXmid = rightXmidInner;
            }

            double rightXmid = bottomPoints[size - 1][Axis.X];
            Box lastBox = new Box(Interval.Empty, Interval.Empty);
            lastBox.AddPoint(new Offset(leftXmid, bottomPoints[size - 1][Axis.Y]) - hvpadding);
            lastBox.AddPoint(new Offset(rightXmid, topPoints[size - 1][Axis.Y]) + hvpadding);
            outStencil.AddStencil(Lookup.RoundFilledBox(lastBox, blotdiameter));
        }
        else if (shape == "ramp")
        {
            points.Add(bottomPoints[0] - vpadding + hpadding);
            for (int i = 1; i < size - 1; i++)
            {
                points.Add(bottomPoints[i] - vpadding);
            }

            points.Add(bottomPoints[size - 1] - vpadding - hpadding);
            points.Add(topPoints[size - 1] + vpadding - hpadding);
            if (size >= 2)
            {
                for (int i = size - 2; i > 0; i--)
                {
                    points.Add(topPoints[i] + vpadding);
                }
            }

            points.Add(topPoints[0] + vpadding + hpadding);
            outStencil.AddStencil(Lookup.RoundPolygon(points, blotdiameter, -1.0));
        }
        else
        {
            me.Warning("unknown cluster style `" + shape + "'");
        }

        return outStencil;
    }
}

/// <summary>
/// A place holder for the cluster spanner to determine the vertical extents of a cluster
/// spanner at this X position.
/// </summary>
public static class ClusterBeacon
{
    private static readonly Symbol PositionsSymbol = Symbol.Intern("positions");

    /// <summary>The <c>Y-extent</c> callback: the beacon's pitch span, in staff spaces.</summary>
    /// <param name="me">The beacon.</param>
    /// <returns>The height, as a Scheme pair.</returns>
    /// <remarks>
    /// The fallback here is <c>Interval (0, 0)</c> and NOT the empty interval every other
    /// caller in this group passes — upstream writes it out, and a beacon whose positions
    /// are missing must still contribute a point rather than nothing.
    /// </remarks>
    public static object Height(Grob me)
    {
        Interval v = SchemeConvert.ToInterval(
            me.GetProperty(PositionsSymbol), new Interval(0, 0));
        Interval height = v * (StaffSymbolReferencer.StaffSpace(me) * 0.5);
        return new Pair(height.Left, height.Right);
    }
}
