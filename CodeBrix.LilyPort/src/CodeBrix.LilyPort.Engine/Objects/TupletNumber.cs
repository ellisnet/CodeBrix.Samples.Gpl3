/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/tuplet-number.cc, lily/include/tuplet-number.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// The number that goes with a tuplet bracket. It normally rides on the bracket, but for
/// a kneed beam with no visible bracket it is placed against the beam instead, which is
/// what most of this file is about.
/// </summary>
public static class TupletNumber
{
    private static readonly Symbol AccidentalGrobSymbol = Symbol.Intern("accidental-grob");
    private static readonly Symbol BracketSymbol = Symbol.Intern("bracket");
    private static readonly Symbol BracketVisibilitySymbol = Symbol.Intern("bracket-visibility");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol KneeSymbol = Symbol.Intern("knee");
    private static readonly Symbol KneeToBeamSymbol = Symbol.Intern("knee-to-beam");
    private static readonly Symbol NoteColumnInterfaceSymbol = Symbol.Intern("note-column-interface");
    private static readonly Symbol NoteColumnsSymbol = Symbol.Intern("note-columns");
    private static readonly Symbol NoteHeadsSymbol = Symbol.Intern("note-heads");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol PositionsSymbol = Symbol.Intern("positions");
    private static readonly Symbol QuantizedPositionsSymbol = Symbol.Intern("quantized-positions");
    private static readonly Symbol TupletsSymbol = Symbol.Intern("tuplets");
    private static readonly Symbol XPositionsSymbol = Symbol.Intern("X-positions");

    /// <summary>
    /// The <c>stencil</c> callback: the markup, centred on both axes. A number whose
    /// bracket has died dies with it.
    /// </summary>
    /// <param name="me">The tuplet number spanner.</param>
    /// <returns>The stencil, or the empty list when the grob killed itself.</returns>
    public static object Print(Spanner me)
    {
        Spanner tuplet = me.GetObject(BracketSymbol) as Spanner;

        if (tuplet == null || !tuplet.IsLive)
        {
            me.Suicide();
            return Nil.Instance;
        }

        Stencil stc = TextInterface.Print(me);

        stc.AlignTo(Axis.X, 0.0);
        stc.AlignTo(Axis.Y, 0.0);

        return stc;
    }

    /// <summary>The <c>X-offset</c> callback.</summary>
    /// <param name="me">The tuplet number spanner.</param>
    /// <returns>The horizontal offset.</returns>
    public static double CalcXOffset(Spanner me)
    {
        DrulArray<Item> bounds = me.GetBounds();
        Item leftBound = bounds[Direction.Negative];

        Spanner tuplet = me.GetObject(BracketSymbol) as Spanner;

        Grob commonx = me.GetSystem();
        if (commonx == null)
        {
            Warn.ProgrammingError("TupletNumber.X-offset accessed before line breaking");
            return 0;
        }

        DrulArray<Grob> boundGrobs = new DrulArray<Grob>(
            bounds[Direction.Negative], bounds[Direction.Positive]);

        Interval boundPositions = default;
        foreach (Direction d in Directions)
        {
            Grob bound = boundGrobs[d];
            if (bound != null
                && bound.HasInterface(NoteColumnInterfaceSymbol)
                && NoteColumn.GetStem(bound) != null)
            {
                bound = NoteColumn.GetStem(bound);
                boundGrobs[d] = bound;
            }

            boundPositions[d] = bound == null
                ? 0.0
                : AxisGroupInterfaceVertical.GenericBoundExtent(bound, commonx, Axis.X)[-d];
        }

        IReadOnlyList<Grob> cols = PointerGroupInterface.ExtractGrobSet(tuplet, NoteColumnsSymbol);
        Grob refStem = SelectReferenceStem(me, cols);

        // Return bracket-based positioning.
        if (refStem == null || !KneePositionAgainstBeam(me, refStem))
        {
            Interval xPositions = ReadInterval(
                tuplet?.GetProperty(XPositionsSymbol), new Interval(0.0, 0.0));

            return xPositions.Center;
        }

        // Horizontally centre the number on the beam.
        double colPos = leftBound == null ? 0.0 : leftBound.RelativeCoordinate(commonx, Axis.X);
        double xOffset = boundPositions.Center - colPos;

        // Consider possible collisions with adjacent note columns.
        DrulArray<Grob> adjacentColumns = AdjacentNoteColumns(me, refStem);
        Interval numberExtent = me.Extent(commonx, Axis.X);
        numberExtent.Translate(xOffset);
        double padding = ReadDouble(me.GetProperty(PaddingSymbol), 0.5);
        numberExtent.Widen(padding);

        Interval correction = new Interval(0.0, 0.0);

        foreach (Direction d in Directions)
        {
            if (adjacentColumns[d] != null)
            {
                Interval columnExtent = adjacentColumns[d].Extent(commonx, Axis.X);
                Interval overlap = columnExtent;
                overlap.Intersect(numberExtent);
                if (!overlap.IsEmpty)
                {
                    correction[d] = overlap.Length * -(int)d;
                }

                xOffset += correction[d];
            }
        }

        return xOffset;
    }

    /// <summary>The <c>Y-offset</c> callback.</summary>
    /// <param name="me">The tuplet number spanner.</param>
    /// <returns>The vertical offset.</returns>
    public static double CalcYOffset(Spanner me)
    {
        Spanner tuplet = me.GetObject(BracketSymbol) as Spanner;
        DrulArray<double> positions = SchemeConvert.ToDrulDouble(
            tuplet?.GetProperty(PositionsSymbol), new DrulArray<double>(0.0, 0.0));

        double toBracket = (positions[Direction.Negative] + positions[Direction.Positive]) / 2.0;

        Grob commonx = me.GetSystem();
        if (commonx == null)
        {
            Warn.ProgrammingError("TupletBracket.Y-offset accessed before line breaking");
            return 0;
        }

        double xCoordinate = me.RelativeCoordinate(commonx, Axis.X);
        IReadOnlyList<Grob> columns = PointerGroupInterface.ExtractGrobSet(tuplet, NoteColumnsSymbol);
        Grob refStem = SelectReferenceStem(me, columns);

        if (refStem == null || !KneePositionAgainstBeam(me, refStem))
        {
            return toBracket;
        }

        // First, we calculate the Y-offset of the tuplet number as if it is positioned at
        // the reference stem.
        Grob commony = AxisGroupInterface.CommonRefpointOfArray(columns, tuplet, Axis.Y);
        commony = commony.CommonRefpoint(me, Axis.Y);
        IReadOnlyList<Grob> tuplets = PointerGroupInterface.ExtractGrobSet(me, TupletsSymbol);
        commony = AxisGroupInterface.CommonRefpointOfArray(tuplets, commony, Axis.Y);
        Grob staffSymbol = StaffSymbolReferencer.GetStaffSymbol(me);
        if (staffSymbol != null)
        {
            commony = staffSymbol.CommonRefpoint(commony, Axis.Y);
        }

        Interval refStemExtent = refStem.Extent(commony, Axis.Y);
        double tupletY = tuplet.RelativeCoordinate(commony, Axis.Y);
        Direction refStemDirection = DirectionalElementInterface.GetGrobDirection(refStem);

        double yOffset = refStemExtent[refStemDirection] - tupletY;
        double padding = ReadDouble(me.GetProperty(PaddingSymbol), 0.5);
        double numberHeight = me.Extent(commony, Axis.Y).Length;

        yOffset += (int)refStemDirection * (padding + (numberHeight / 2.0));

        // Now we adjust the vertical position of the number to reflect its actual
        // horizontal placement along the beam.
        double refStemX = refStem.RelativeCoordinate(commonx, Axis.X);
        yOffset += CalcBeamYShift(refStem, xCoordinate - refStemX);

        // Check if the number is between the beam and the staff. If so, it will collide
        // with ledger lines. Move it into the staff.
        Grob stemStaff = StaffSymbolReferencer.GetStaffSymbol(refStem);
        if (stemStaff != null)
        {
            Interval staffExtentY = stemStaff.Extent(commony, Axis.Y);
            bool move = refStemDirection == Direction.Negative
                ? refStemExtent[Direction.Negative] > staffExtentY[Direction.Positive]
                : staffExtentY[Direction.Negative] > refStemExtent[Direction.Positive];

            if (move)
            {
                Interval ledgerDomain = new Interval(
                    System.Math.Min(
                        staffExtentY[Direction.Positive], refStemExtent[Direction.Positive]),
                    System.Math.Max(
                        staffExtentY[Direction.Negative], refStemExtent[Direction.Negative]));

                Interval numberY = me.Extent(commony, Axis.Y);
                numberY.Translate(yOffset);
                Interval numberLedgerOverlap = numberY;
                numberLedgerOverlap.Intersect(ledgerDomain);
                double lineThickness = StaffSymbolReferencer.LineThickness(stemStaff);
                double staffSpace = StaffSymbolReferencer.StaffSpace(stemStaff);

                // Number will touch outer staff line.
                if (!numberLedgerOverlap.IsEmpty
                    && numberLedgerOverlap.Length > (staffSpace / 2.0))
                {
                    yOffset += staffExtentY[-refStemDirection] - numberY[-refStemDirection]
                        + (lineThickness * (int)refStemDirection);
                }
            }
        }

        // Now consider possible collisions with accidentals on the right. We move the
        // accidental away from the beam.
        DrulArray<Grob> adjacentColumns = AdjacentNoteColumns(me, refStem);

        if (adjacentColumns[Direction.Positive] == null)
        {
            return yOffset;
        }

        // Collect Y-extents of accidentals that overlap the number along the X-axis.
        IReadOnlyList<Grob> heads = PointerGroupInterface.ExtractGrobSet(
            adjacentColumns[Direction.Positive], NoteHeadsSymbol);

        Interval collidingAccidentalExtentY = Interval.Empty;

        for (int i = 0; i < heads.Count; i++)
        {
            if (heads[i].GetObject(AccidentalGrobSymbol) is Grob accidental)
            {
                commony = commony.CommonRefpoint(accidental, Axis.Y);
                Interval accidentalExtentY = accidental.Extent(commony, Axis.Y);

                Interval numberExtentX = me.Extent(commonx, Axis.X);
                numberExtentX.Widen(padding);
                Interval overlapX = numberExtentX;
                Interval accidentalX = accidental.Extent(commonx, Axis.X);
                overlapX.Intersect(accidentalX);

                if (!overlapX.IsEmpty)
                {
                    collidingAccidentalExtentY.Unite(accidentalExtentY);
                }
            }
        }

        // Does our number intersect vertically with the accidental Y-extents we combined
        // above? If so, move it.
        Interval overlapAccidentalY = collidingAccidentalExtentY;
        Interval numberExtentY = me.Extent(commony, Axis.Y);
        numberExtentY.Translate(yOffset);
        overlapAccidentalY.Intersect(numberExtentY);

        if (!overlapAccidentalY.IsEmpty)
        {
            yOffset += collidingAccidentalExtentY[refStemDirection]
                - numberExtentY[-refStemDirection]
                + (padding * (int)refStemDirection);
        }

        return yOffset;
    }

    /// <summary>
    /// Chooses the stem the number is placed opposite to, when it rides on a beam rather
    /// than on its bracket.
    /// </summary>
    /// <param name="me">The tuplet number spanner.</param>
    /// <param name="cols">The tuplet's note columns.</param>
    /// <returns>The reference stem, or <see langword="null"/> when there is none.</returns>
    public static Grob SelectReferenceStem(Spanner me, IReadOnlyList<Grob> cols)
    {
        int columnCount = cols?.Count ?? 0;

        if (columnCount == 0)
        {
            return null;
        }

        // When we have an odd number of stems, we choose the middle stem as our reference.
        Grob refStem = NoteColumn.GetStem(cols[columnCount / 2]);

        if (columnCount % 2 == 1)
        {
            return refStem;
        }

        // When we have an even number of stems, we choose between the central two stems.
        Direction meDirection = DirectionalElementInterface.FromScheme(
            me.GetProperty(DirectionSymbol), Direction.Positive);

        DrulArray<Grob> boundingStems = new DrulArray<Grob>(
            NoteColumn.GetStem(cols[(columnCount / 2) - 1]),
            NoteColumn.GetStem(cols[columnCount / 2]));

        foreach (Direction d in Directions)
        {
            if (boundingStems[d] == null)
            {
                return boundingStems[-d];
            }
        }

        // If the central stems point in opposite directions, the number may be placed on
        // either side unless there is a fractional beam, in which case the number goes
        // opposite to the partial beam.
        //
        // When there is an option, we use the setting of TupletNumber.direction.
        //
        // If the central stems are in the same direction, it doesn't matter which is used
        // as the reference. We use the one on the left.
        Direction directionLeft = DirectionalElementInterface.GetGrobDirection(
            boundingStems[Direction.Negative]);

        Direction directionRight = DirectionalElementInterface.GetGrobDirection(
            boundingStems[Direction.Positive]);

        if (directionLeft == directionRight)
        {
            refStem = boundingStems[Direction.Negative];
        }
        else
        {
            int beamCountLeftRight = Stem.GetBeaming(
                boundingStems[Direction.Negative], Direction.Positive);

            int beamCountRightLeft = Stem.GetBeaming(
                boundingStems[Direction.Positive], Direction.Negative);

            if (beamCountLeftRight == beamCountRightLeft)
            {
                refStem = directionLeft == meDirection
                    ? boundingStems[Direction.Negative]
                    : boundingStems[Direction.Positive];
            }
            else
            {
                refStem = beamCountLeftRight > beamCountRightLeft
                    ? boundingStems[Direction.Negative]
                    : boundingStems[Direction.Positive];
            }
        }

        return refStem;
    }

    /// <summary>
    /// Finds the note columns flanking the number on the same side of the beam, which
    /// bound the space it may occupy.
    /// </summary>
    /// <param name="me">The tuplet number spanner.</param>
    /// <param name="refStem">The reference stem.</param>
    /// <returns>The neighbouring columns; either side may be <see langword="null"/>.</returns>
    public static DrulArray<Grob> AdjacentNoteColumns(Spanner me, Grob refStem)
    {
        Spanner tuplet = me.GetObject(BracketSymbol) as Spanner;

        IReadOnlyList<Grob> columns = PointerGroupInterface.ExtractGrobSet(tuplet, NoteColumnsSymbol);
        Grob refColumn = refStem.GetParent(Axis.X); // X-parent of Stem = NoteColumn
        Direction refStemDirection = DirectionalElementInterface.GetGrobDirection(refStem);
        List<Grob> filtered = new List<Grob>();
        int refPosition = 0;

        for (int i = 0, counter = 0; i < columns.Count; ++i)
        {
            Grob stem = NoteColumn.GetStem(columns[i]);
            if (stem != null
                && DirectionalElementInterface.GetGrobDirection(stem) == -refStemDirection)
            {
                filtered.Add(columns[i]);
                ++counter;
            }

            if (ReferenceEquals(columns[i], refColumn))
            {
                filtered.Add(columns[i]);
                refPosition = counter;
            }
        }

        return new DrulArray<Grob>(
            refPosition > 0 ? filtered[refPosition - 1] : null,
            refPosition < filtered.Count - 1 ? filtered[refPosition + 1] : null);
    }

    /// <summary>
    /// Determines whether the number is placed next to the beam independently of its
    /// bracket. That happens when the bracket is invisible, a kneed beam runs above or
    /// below the number, and the number fits between the adjoining note columns.
    /// </summary>
    /// <param name="me">The tuplet number spanner.</param>
    /// <param name="refStem">The reference stem.</param>
    /// <returns>Whether to place the number against the beam.</returns>
    public static bool KneePositionAgainstBeam(Spanner me, Grob refStem)
    {
        Spanner tuplet = me.GetObject(BracketSymbol) as Spanner;
        if (tuplet == null)
        {
            return false;
        }

        bool bracketVisible
            = (me.GetProperty(BracketVisibilitySymbol) is bool visible && visible)
              || !tuplet.Extent(tuplet, Axis.Y).IsEmpty;

        if (bracketVisible
            || !(me.GetProperty(KneeToBeamSymbol) is bool kneeToBeam && kneeToBeam))
        {
            return false;
        }

        Grob beam = Stem.GetBeam(refStem);

        if (beam == null || !(beam.GetProperty(KneeSymbol) is bool knee && knee))
        {
            return false;
        }

        Grob commonx = me.GetSystem();
        if (commonx == null)
        {
            Warn.ProgrammingError(
                "Tuplet_number::knee_position_against_beam called before line breaking");
            return true;
        }

        Interval numberExtent = me.Extent(commonx, Axis.X);

        DrulArray<Grob> adjacentColumns = AdjacentNoteColumns(me, refStem);

        DrulArray<Item> bounds = me.GetBounds();
        if (bounds[Direction.Negative] == null || bounds[Direction.Positive] == null)
        {
            return false;
        }

        Interval availableExtent = default;
        double padding = ReadDouble(me.GetProperty(PaddingSymbol), 0.5);

        // If there is no note column on a given side of the tuplet number, we use a paper
        // column instead to determine the available space. Padding is only considered in
        // the case of a note column.
        foreach (Direction d in Directions)
        {
            if (adjacentColumns[d] != null)
            {
                availableExtent[d] = AxisGroupInterfaceVertical.GenericBoundExtent(
                    adjacentColumns[d], commonx, Axis.X)[-d] + (-(int)d * padding);
            }
            else
            {
                availableExtent[d] = AxisGroupInterfaceVertical.GenericBoundExtent(
                    bounds[d], commonx, Axis.X)[-d];
            }
        }

        if (numberExtent.Length > availableExtent.Length)
        {
            Warn.ProgrammingError("not enough space for tuplet number against beam");
            return false;
        }

        return true;
    }

    // For a given horizontal displacement of the tuplet number, how much vertical shift
    // is necessary to keep it the same distance from the beam?
    private static double CalcBeamYShift(Grob refStem, double dx)
    {
        Grob beam = Stem.GetBeam(refStem);
        if (beam == null)
        {
            return 0.0;
        }

        Interval xPositions = ReadInterval(
            beam.GetProperty(XPositionsSymbol), new Interval(0.0, 0.0));

        Interval yPositions = ReadInterval(
            beam.GetProperty(QuantizedPositionsSymbol), new Interval(0.0, 0.0));

        double beamDx = xPositions.Length;
        double beamDy = yPositions[Direction.Positive] - yPositions[Direction.Negative];
        double slope = beamDx != 0.0 ? beamDy / beamDx : 0.0;

        return slope * dx;
    }

    private static Interval ReadInterval(object value, Interval fallback)
    {
        if (value is Pair pair
            && SchemeConvert.IsNumber(pair.Car)
            && SchemeConvert.IsNumber(pair.Cdr))
        {
            return new Interval(
                SchemeConvert.ToDouble(pair.Car, "tuplet-number"),
                SchemeConvert.ToDouble(pair.Cdr, "tuplet-number"));
        }

        return fallback;
    }

    private static double ReadDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "tuplet-number")
            : fallback;

    private static Direction[] Directions { get; }
        = { Direction.Negative, Direction.Positive };
}
