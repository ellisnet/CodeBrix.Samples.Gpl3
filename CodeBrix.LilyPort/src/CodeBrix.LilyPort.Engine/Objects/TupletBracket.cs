/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Jan Nieuwenhuizen <janneke@gnu.org>
  Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/tuplet-bracket.cc, lily/include/tuplet-bracket.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// A bracket with a number in the middle, used for tuplets. When the bracket spans a
/// line break, <c>break-overshoot</c> determines how far past the bounds it reaches.
/// </summary>
public static class TupletBracket
{
    private static readonly Symbol AvoidScriptsSymbol = Symbol.Intern("avoid-scripts");
    private static readonly Symbol BeamSymbol = Symbol.Intern("beam");
    private static readonly Symbol BracketFlareSymbol = Symbol.Intern("bracket-flare");
    private static readonly Symbol BracketVisibilitySymbol = Symbol.Intern("bracket-visibility");
    private static readonly Symbol BreakOvershootSymbol = Symbol.Intern("break-overshoot");
    private static readonly Symbol ConnectToNeighborSymbol = Symbol.Intern("connect-to-neighbor");
    private static readonly Symbol CrossStaffSymbol = Symbol.Intern("cross-staff");
    private static readonly Symbol DashDefinitionSymbol = Symbol.Intern("dash-definition");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol EdgeHeightSymbol = Symbol.Intern("edge-height");
    private static readonly Symbol EdgeTextSymbol = Symbol.Intern("edge-text");
    private static readonly Symbol FullLengthPaddingSymbol = Symbol.Intern("full-length-padding");
    private static readonly Symbol FullLengthToExtentSymbol = Symbol.Intern("full-length-to-extent");
    private static readonly Symbol IfNoBeamSymbol = Symbol.Intern("if-no-beam");
    private static readonly Symbol KneeSymbol = Symbol.Intern("knee");
    private static readonly Symbol LineThicknessSymbol = Symbol.Intern("line-thickness");
    private static readonly Symbol MaxSlopeFactorSymbol = Symbol.Intern("max-slope-factor");
    private static readonly Symbol NoteColumnInterfaceSymbol = Symbol.Intern("note-column-interface");
    private static readonly Symbol NoteColumnsSymbol = Symbol.Intern("note-columns");
    private static readonly Symbol OutsideStaffPrioritySymbol
        = Symbol.Intern("outside-staff-priority");

    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol PositionsSymbol = Symbol.Intern("positions");
    private static readonly Symbol PotentialBeamSymbol = Symbol.Intern("potential-beam");
    private static readonly Symbol QuantizedPositionsSymbol = Symbol.Intern("quantized-positions");
    private static readonly Symbol ScriptsSymbol = Symbol.Intern("scripts");
    private static readonly Symbol ShortenPairSymbol = Symbol.Intern("shorten-pair");
    private static readonly Symbol SlurSymbol = Symbol.Intern("slur");
    private static readonly Symbol SpanAllNoteHeadsSymbol = Symbol.Intern("span-all-note-heads");
    private static readonly Symbol StaffPaddingSymbol = Symbol.Intern("staff-padding");
    private static readonly Symbol StemSymbol = Symbol.Intern("stem");
    private static readonly Symbol StencilSymbol = Symbol.Intern("stencil");
    private static readonly Symbol TupletNumberSymbol = Symbol.Intern("tuplet-number");
    private static readonly Symbol TupletSlurSymbol = Symbol.Intern("tuplet-slur");
    private static readonly Symbol TupletsSymbol = Symbol.Intern("tuplets");
    private static readonly Symbol VisibleOverNoteHeadsSymbol
        = Symbol.Intern("visible-over-note-heads");

    private static readonly Symbol WhenSymbol = Symbol.Intern("when");
    private static readonly Symbol XPositionsSymbol = Symbol.Intern("X-positions");

    /// <summary>The <c>X-positions</c> callback.</summary>
    /// <param name="me">The tuplet bracket spanner.</param>
    /// <returns>The horizontal span, relative to the left bound.</returns>
    public static object CalcXPositions(Spanner me)
    {
        IReadOnlyList<Grob> columns = PointerGroupInterface.ExtractGrobSet(me, NoteColumnsSymbol);

        Grob commonx = me.GetSystem();
        if (commonx == null)
        {
            Warn.ProgrammingError("TupletBracket.X-positions requested before line breaking");
            return new Pair(0.0, 0.0);
        }

        Direction dir = DirectionalElementInterface.GetGrobDirection(me);

        DrulArray<Item> bounds = new DrulArray<Item>(
            GetXBoundItem(me, Direction.Negative, dir),
            GetXBoundItem(me, Direction.Positive, dir));

        DrulArray<bool> connectToOther
            = SchemeConvert.ToDrulBool(me.GetProperty(ConnectToNeighborSymbol));

        bool spanNoteHeads = me.GetProperty(SpanAllNoteHeadsSymbol) is bool span && span;
        bool bracketVisibility = false;

        if (spanNoteHeads)
        {
            bracketVisibility = BracketBasicVisibility(me);
        }

        Interval xSpan = default;
        foreach (Direction d in Directions)
        {
            Item bound = bounds[d];
            if (bound == null)
            {
                continue;
            }

            Grob xParent = bound.GetParent(Axis.X);
            if (bracketVisibility
                && xParent != null
                && xParent.HasInterface(NoteColumnInterfaceSymbol))
            {
                xSpan[d] = AxisGroupInterfaceVertical.GenericBoundExtent(
                    xParent, commonx, Axis.X)[d];
            }
            else
            {
                xSpan[d] = AxisGroupInterfaceVertical.GenericBoundExtent(
                    bound, commonx, Axis.X)[d];
            }

            if (connectToOther[d])
            {
                Interval overshoot = ReadInterval(
                    me.GetProperty(BreakOvershootSymbol), new Interval(-0.5, 0.0));

                if (d == Direction.Positive)
                {
                    xSpan[d] += (int)d * overshoot[d];
                }
                else
                {
                    xSpan[d] = (bound.BreakStatusDirection() != Direction.Center
                            ? AxisGroupInterfaceVertical.GenericBoundExtent(
                                bound, commonx, Axis.X)[-d]
                            : LooseColumns.RobustRelativeExtent(bound, commonx, Axis.X)[-d])
                        - overshoot[Direction.Negative];
                }
            }
            else if (d == Direction.Positive
                     && (columns.Count == 0
                         || !ReferenceEquals(
                             bound.GetColumn(),
                             (columns[columns.Count - 1] as Item)?.GetColumn())))
            {
                // We're connecting to a column, for the last bit of a broken fullLength
                // bracket.
                double padding = ReadDouble(me.GetProperty(FullLengthPaddingSymbol), 1.0);

                if (bound.BreakStatusDirection() != Direction.Center)
                {
                    padding = 0.0;
                }

                double coord = bound.RelativeCoordinate(commonx, Axis.X);
                if (me.GetProperty(FullLengthToExtentSymbol) is bool toExtent && toExtent)
                {
                    coord = LooseColumns.RobustRelativeExtent(
                        bound, commonx, Axis.X)[Direction.Negative];
                }

                coord = Math.Max(coord, xSpan[Direction.Negative]);

                xSpan[d] = coord - padding;
            }
        }

        double origin = me.GetBound(Direction.Negative)?.RelativeCoordinate(commonx, Axis.X) ?? 0.0;
        return new Pair(xSpan.Left - origin, xSpan.Right - origin);
    }

    /// <summary>The <c>stencil</c> callback.</summary>
    /// <param name="me">The tuplet bracket spanner.</param>
    /// <returns>The stencil, or the empty list when the grob killed itself.</returns>
    public static object Print(Spanner me)
    {
        Stencil mol = new Stencil();

        bool tupletSlur = SchemeUtilities.IsSchemeTrue(me.GetProperty(TupletSlurSymbol));

        bool bracketVisibility = BracketBasicVisibility(me);
        object bracketVisProperty = me.GetProperty(BracketVisibilitySymbol);

        // Don't print a tuplet bracket and number if no X or Y positions were calculated.
        object schemeXSpan = me.GetProperty(XPositionsSymbol);
        object schemePositions = me.GetProperty(PositionsSymbol);
        if (!(schemeXSpan is Pair) || !(schemePositions is Pair))
        {
            me.Suicide();
            return Nil.Instance;
        }

        Interval xSpan = ReadInterval(schemeXSpan, new Interval(0.0, 0.0));
        Interval positions = ReadInterval(schemePositions, new Interval(0.0, 0.0));

        DrulArray<Offset> points = new DrulArray<Offset>(
            new Offset(xSpan[Direction.Negative], positions[Direction.Negative]),
            new Offset(xSpan[Direction.Positive], positions[Direction.Positive]));

        Grob numberGrob = me.GetObject(TupletNumberSymbol) as Grob;

        // Don't print the bracket when it would be smaller than the number, unless the
        // user has coded bracket-visibility = #t.
        double gap = 0.0;
        if (bracketVisibility && numberGrob != null)
        {
            Interval ext = numberGrob.Extent(numberGrob, Axis.X);
            if (!ext.IsEmpty)
            {
                gap = ext.Length + 1.0;

                if (!(bracketVisProperty is bool visible && visible) && gap > xSpan.Length)
                {
                    bracketVisibility = false;
                }
            }
        }

        if (bracketVisibility)
        {
            DrulArray<double> zero = new DrulArray<double>(0.0, 0.0);

            DrulArray<double> shorten = SchemeConvert.ToDrulDouble(
                me.GetProperty(ShortenPairSymbol), zero);

            double staffSpace = StaffSymbolReferencer.StaffSpace(me);
            ScaleDrul(ref shorten, staffSpace);

            if (tupletSlur)
            {
                mol.AddStencil(MakeTupletSlur(
                    me, points[Direction.Negative], points[Direction.Positive], shorten));
            }
            else
            {
                DrulArray<Stencil> edgeStencils
                    = new DrulArray<Stencil>(new Stencil(), new Stencil());

                DrulArray<double> height = SchemeConvert.ToDrulDouble(
                    me.GetProperty(EdgeHeightSymbol), zero);

                DrulArray<double> flare = SchemeConvert.ToDrulDouble(
                    me.GetProperty(BracketFlareSymbol), zero);

                Direction dir = DirectionalElementInterface.GetGrobDirection(me);

                ScaleDrul(ref height, -staffSpace * (int)dir);
                ScaleDrul(ref flare, staffSpace);

                DrulArray<bool> connectToOther
                    = SchemeConvert.ToDrulBool(me.GetProperty(ConnectToNeighborSymbol));

                foreach (Direction d in Directions)
                {
                    if (connectToOther[d])
                    {
                        height[d] = 0.0;
                        flare[d] = 0.0;
                        shorten[d] = 0.0;

                        object edgeText = me.GetProperty(EdgeTextSymbol);

                        if (edgeText is Pair edgePair)
                        {
                            object text = d == Direction.Negative ? edgePair.Car : edgePair.Cdr;
                            if (TextInterface.IsMarkup(text))
                            {
                                Stencil es = TextInterface.GrobInterpretMarkup(me, text);
                                es.TranslateAxis(
                                    xSpan[d] - xSpan[Direction.Negative], Axis.X);

                                edgeStencils[d] = es;
                            }
                        }
                    }
                }

                Stencil brack = Bracket.MakeBracket(
                    me,
                    Axis.Y,
                    points[Direction.Positive] - points[Direction.Negative],
                    height,

                    // 0.1 = more space at right due to italics.
                    new Interval(-0.5 * gap + 0.1, (0.5 * gap) + 0.1),
                    flare,
                    shorten);

                foreach (Direction d in Directions)
                {
                    if (!edgeStencils[d].IsEmpty)
                    {
                        brack.AddStencil(edgeStencils[d]);
                    }
                }

                mol.AddStencil(brack);
                mol.Translate(points[Direction.Negative]);
            }
        }

        return mol;
    }

    /// <summary>The <c>direction</c> callback.</summary>
    /// <param name="me">The tuplet bracket.</param>
    /// <returns>The default direction, as a Scheme integer.</returns>
    public static object CalcDirection(Grob me) => (long)(int)GetDefaultDirection(me);

    /// <summary>The <c>positions</c> callback.</summary>
    /// <param name="me">The tuplet bracket spanner.</param>
    /// <returns>The two vertical endpoints, as a Scheme pair.</returns>
    public static object CalcPositions(Spanner me)
    {
        CalcPositionAndHeight(me, out double offset, out double dy);
        return new Pair(offset, offset + dy);
    }

    /// <summary>The <c>cross-staff</c> callback.</summary>
    /// <param name="me">The tuplet bracket spanner.</param>
    /// <returns>Whether the bracket spans staves.</returns>
    public static bool CalcCrossStaff(Spanner me)
    {
        IReadOnlyList<Grob> cols = PointerGroupInterface.ExtractGrobSet(me, NoteColumnsSymbol);
        IReadOnlyList<Grob> tuplets = PointerGroupInterface.ExtractGrobSet(me, TupletsSymbol);

        Grob commony = AxisGroupInterface.CommonRefpointOfArray(cols, me, Axis.Y);
        commony = AxisGroupInterface.CommonRefpointOfArray(tuplets, commony, Axis.Y);
        Grob staff = StaffSymbolReferencer.GetStaffSymbol(me);
        if (staff != null)
        {
            commony = staff.CommonRefpoint(commony, Axis.Y);
        }

        if (me.CheckCrossStaff(commony))
        {
            return true;
        }

        // Whether we want to use a parallel beam depends on whether we are going to be
        // broken. However, we will only have that information after line breaking, while
        // cross-staff needs to be known before line breaking. Therefore we conservatively
        // mark as cross-staff if there is a potential beam.
        Grob parallelBeam = me.GetObject(PotentialBeamSymbol) as Grob;

        if (parallelBeam != null
            && parallelBeam.GetProperty(CrossStaffSymbol) is bool beamCross && beamCross)
        {
            return true;
        }

        for (int i = 0; i < cols.Count; i++)
        {
            if (cols[i].GetObject(StemSymbol) is Grob stem
                && stem.GetProperty(CrossStaffSymbol) is bool stemCross && stemCross)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Records a note column as one the bracket spans.</summary>
    /// <param name="me">The tuplet bracket spanner.</param>
    /// <param name="column">The note column.</param>
    public static void AddColumn(Spanner me, Item column)
    {
        PointerGroupInterface.AddGrob(me, NoteColumnsSymbol, column);
        Spanner.AddBoundItem(me, column);
    }

    /// <summary>Records a script the bracket should avoid.</summary>
    /// <param name="me">The tuplet bracket.</param>
    /// <param name="script">The script item.</param>
    public static void AddScript(Grob me, Item script)
        => PointerGroupInterface.AddGrob(me, ScriptsSymbol, script);

    /// <summary>Records a nested tuplet bracket.</summary>
    /// <param name="me">The outer tuplet bracket.</param>
    /// <param name="bracket">The nested bracket.</param>
    public static void AddTupletBracket(Grob me, Grob bracket)
        => PointerGroupInterface.AddGrob(me, TupletsSymbol, bracket);

    /// <summary>
    /// Returns the outermost note columns that are not rests, which fix the bracket's
    /// slope.
    /// </summary>
    /// <param name="me">The tuplet bracket.</param>
    /// <returns>The two columns; both are <see langword="null"/> when all are rests.</returns>
    public static DrulArray<Grob> GetBounds(Grob me)
    {
        IReadOnlyList<Grob> columns = PointerGroupInterface.ExtractGrobSet(me, NoteColumnsSymbol);
        int l = 0;
        while (l < columns.Count && NoteColumn.HasRests(columns[l]))
        {
            l++;
        }

        int r = columns.Count;
        while (r > l && NoteColumn.HasRests(columns[r - 1]))
        {
            r--;
        }

        return l < r
            ? new DrulArray<Grob>(columns[l], columns[r - 1])
            : new DrulArray<Grob>(null, null);
    }

    /// <summary>
    /// Decides whether the bracket is drawn at all: normally not when a beam of the same
    /// extent already delimits the tuplet.
    /// </summary>
    /// <param name="me">The tuplet bracket spanner.</param>
    /// <returns>Whether to draw the bracket.</returns>
    public static bool BracketBasicVisibility(Spanner me)
    {
        Spanner parallelBeam = me.GetObject(BeamSymbol) as Spanner; // NB may be null
        bool equallyLong = EqualBounds(parallelBeam, me);
        bool bracketVisibility = !(parallelBeam != null && equallyLong);

        object bracketVisProperty = me.GetProperty(BracketVisibilitySymbol);
        bool bracketProperty = SchemeUtilities.IsSchemeTrue(bracketVisProperty);
        bool ifNoBeam = ReferenceEquals(bracketVisProperty, IfNoBeamSymbol);

        if (bracketVisProperty is bool)
        {
            bracketVisibility = bracketProperty;
        }
        else if (ifNoBeam)
        {
            bracketVisibility = parallelBeam == null;
        }

        if (!(bracketVisProperty is bool) && !bracketVisibility)
        {
            bool bracketOverHeads
                = me.GetProperty(VisibleOverNoteHeadsSymbol) is bool over && over;

            if (bracketOverHeads
                && !(parallelBeam?.GetProperty(KneeSymbol) is bool knee && knee))
            {
                Direction defaultDirection = DirectionalElementInterface.FromScheme(
                    parallelBeam?.GetProperty(DirectionSymbol), Direction.Center);

                Direction dir = DirectionalElementInterface.GetGrobDirection(me);

                if (defaultDirection != dir)
                {
                    bracketVisibility = true;
                }
            }
        }

        // If the tuplet does not span any time, i.e. a single-note tuplet, hide the
        // bracket but still let the number be displayed. Only do this if the user has not
        // explicitly specified bracket-visibility = #t.
        if (!(bracketVisProperty is bool explicitVis && explicitVis))
        {
            Item leftBound = me.GetBound(Direction.Negative);
            Item rightBound = me.GetBound(Direction.Positive);
            Grob startColumn = leftBound?.GetColumn();
            Grob endColumn = rightBound?.GetColumn();

            Moment startMoment = startColumn?.GetProperty(WhenSymbol) is Moment sm
                ? sm
                : Moment.Zero;

            Moment endMoment = endColumn?.GetProperty(WhenSymbol) is Moment em
                ? em
                : Moment.Zero;

            if (startMoment == endMoment
                && leftBound != null
                && leftBound.BreakStatusDirection() == Direction.Center)
            {
                bracketVisibility = false;
            }
        }

        return bracketVisibility;
    }

    /// <summary>
    /// Returns the direction most of the tuplet's note columns point, which is the side
    /// the bracket goes on.
    /// </summary>
    /// <param name="me">The tuplet bracket.</param>
    /// <returns>The default direction.</returns>
    public static Direction GetDefaultDirection(Grob me)
    {
        DrulArray<int> dirs = new DrulArray<int>(0, 0);
        IReadOnlyList<Grob> columns = PointerGroupInterface.ExtractGrobSet(me, NoteColumnsSymbol);
        for (int i = 0; i < columns.Count; i++)
        {
            Grob nc = columns[i];
            if (NoteColumn.HasRests(nc))
            {
                continue;
            }

            Direction d = NoteColumn.Dir(nc);
            if (d != Direction.Center)
            {
                dirs[d]++;
            }
        }

        if (dirs[Direction.Positive] == dirs[Direction.Negative])
        {
            if (dirs[Direction.Positive] == 0)
            {
                return Direction.Positive;
            }

            Grob staff = StaffSymbolReferencer.GetStaffSymbol(me);
            if (staff == null)
            {
                return Direction.Positive;
            }

            Interval staffExtent = staff.Extent(staff, Axis.Y);
            Interval extremalPositions = Interval.Empty;
            for (int i = 0; i < columns.Count; i++)
            {
                Direction d = NoteColumn.Dir(columns[i]);
                if (d == Direction.Center)
                {
                    continue;
                }

                double candidate = 1.0 * NoteColumn.HeadPositionsInterval(columns[i])[d];
                extremalPositions[d] = Direction.MinMax(d, candidate, extremalPositions[d]);
            }

            foreach (Direction d in Directions)
            {
                extremalPositions[d] = -(int)d * (staffExtent[d] - extremalPositions[d]);
            }

            return extremalPositions[Direction.Positive] <= extremalPositions[Direction.Negative]
                ? Direction.Positive
                : Direction.Negative;
        }

        return dirs[Direction.Positive] > dirs[Direction.Negative]
            ? Direction.Positive
            : Direction.Negative;
    }

    // Use first -> last note for slope, and then correct for disturbing notes in between.
    private static void CalcPositionAndHeight(Spanner me, out double offset, out double dy)
    {
        offset = 0.0;
        dy = 0.0;

        IReadOnlyList<Grob> columns = PointerGroupInterface.ExtractGrobSet(me, NoteColumnsSymbol);
        IReadOnlyList<Grob> tuplets = PointerGroupInterface.ExtractGrobSet(me, TupletsSymbol);

        Grob commony = AxisGroupInterface.CommonRefpointOfArray(columns, me, Axis.Y);
        commony = AxisGroupInterface.CommonRefpointOfArray(tuplets, commony, Axis.Y);
        Grob staffSymbol = StaffSymbolReferencer.GetStaffSymbol(me);
        if (staffSymbol != null)
        {
            commony = staffSymbol.CommonRefpoint(commony, Axis.Y);
        }

        double myOffset = me.RelativeCoordinate(commony, Axis.Y);

        Grob commonx = me.GetSystem();
        if (commonx == null)
        {
            Warn.ProgrammingError("TupletBracket.positions requested before line breaking");
            return;
        }

        Interval staff = default;

        // staff-padding doesn't work correctly on cross-staff tuplets because it only
        // considers one staff symbol. Until this works, disable it.
        if (staffSymbol != null
            && !(me.GetProperty(CrossStaffSymbol) is bool crossStaff && crossStaff))
        {
            double pad = ReadDouble(me.GetProperty(StaffPaddingSymbol), -1.0);
            if (pad >= 0.0)
            {
                staff = staffSymbol.Extent(commony, Axis.Y);
                staff.Translate(-myOffset);
                staff.Widen(pad);
            }
        }

        Direction dir = DirectionalElementInterface.GetGrobDirection(me);

        Grob parallelBeam = me.GetObject(BeamSymbol) as Grob; // NB may be null

        Item leftGrob = GetXBoundItem(me, Direction.Negative, dir);
        Item rightGrob = GetXBoundItem(me, Direction.Positive, dir);
        double x0 = LooseColumns.RobustRelativeExtent(
            leftGrob, commonx, Axis.X)[Direction.Negative];

        double x1 = LooseColumns.RobustRelativeExtent(
            rightGrob, commonx, Axis.X)[Direction.Positive];

        double maxSlopeFactor = ReadDouble(me.GetProperty(MaxSlopeFactorSymbol), 0);

        bool followBeam = parallelBeam != null
            && DirectionalElementInterface.GetGrobDirection(parallelBeam) == dir
            && !(parallelBeam.GetProperty(KneeSymbol) is bool beamKnee && beamKnee);

        List<Offset> points = new List<Offset>();
        if (columns.Count > 0
            && followBeam
            && NoteColumn.GetStem(columns[0]) != null
            && NoteColumn.GetStem(columns[columns.Count - 1]) != null)
        {
            DrulArray<Grob> stems = new DrulArray<Grob>(
                NoteColumn.GetStem(columns[0]),
                NoteColumn.GetStem(columns[columns.Count - 1]));

            Interval positions = default;
            foreach (Direction side in Directions)
            {
                // Trigger setting of stem lengths if necessary.
                Grob beam = Stem.GetBeam(stems[side]);
                if (beam != null)
                {
                    _ = beam.GetProperty(QuantizedPositionsSymbol);
                }

                positions[side] = stems[side].Extent(stems[side], Axis.Y)[
                        DirectionalElementInterface.GetGrobDirection(stems[side])]
                    + stems[side].ParentRelative(commony, Axis.Y);
            }

            dy = positions[Direction.Positive] - positions[Direction.Negative];

            points.Add(new Offset(
                stems[Direction.Negative].RelativeCoordinate(commonx, Axis.X) - x0,
                positions[Direction.Negative]));

            points.Add(new Offset(
                stems[Direction.Positive].RelativeCoordinate(commonx, Axis.X) - x0,
                positions[Direction.Positive]));
        }
        else
        {
            // Use outer non-rest columns to determine slope.
            DrulArray<Grob> outerColumns = GetBounds(me);
            Grob leftColumn = outerColumns[Direction.Negative];
            Grob rightColumn = outerColumns[Direction.Positive];
            if (leftColumn != null && rightColumn != null)
            {
                Interval rv = NoteColumn.CrossStaffExtent(rightColumn, commony);
                Interval lv = NoteColumn.CrossStaffExtent(leftColumn, commony);
                rv.Unite(staff);
                lv.Unite(staff);

                double graphicalDy = rv[dir] - lv[dir];

                Slice ls = NoteColumn.HeadPositionsInterval(leftColumn);
                Slice rs = NoteColumn.HeadPositionsInterval(rightColumn);

                Interval musicalDy = default;
                musicalDy[Direction.Positive]
                    = rs[Direction.Positive] - ls[Direction.Positive];

                musicalDy[Direction.Negative]
                    = rs[Direction.Negative] - ls[Direction.Negative];

                if (Math.Sign(musicalDy[Direction.Positive])
                    != Math.Sign(musicalDy[Direction.Negative]))
                {
                    dy = 0.0;
                }
                else if (Math.Sign(graphicalDy) != Math.Sign(musicalDy[Direction.Negative]))
                {
                    dy = 0.0;
                }
                else
                {
                    dy = graphicalDy;
                }
            }
            else
            {
                dy = 0;
            }

            double x = 0;
            for (int i = 0; i < columns.Count; i++)
            {
                Interval noteExtent = NoteColumn.CrossStaffExtent(columns[i], commony);
                x = columns[i].RelativeCoordinate(commonx, Axis.X) - x0;

                points.Add(new Offset(x, noteExtent[dir]));
            }

            double lastX = x;

            if (dy != 0.0)
            {
                double slope = Math.Abs(dy / (x1 - x0));
                double maxSlope = 0;
                double maxDy = maxSlopeFactor * lastX * Math.Sign(dy);
                double subX0 = 0;
                double subX1 = 0;

                DrulArray<double> beamPositions = new DrulArray<double>(0.0, 0.0);

                if (parallelBeam != null)
                {
                    beamPositions = SchemeConvert.ToDrulDouble(
                        parallelBeam.GetProperty(QuantizedPositionsSymbol),
                        new DrulArray<double>(0.0, 0.0));
                }
                else
                {
                    for (int i = columns.Count; i-- > 0;)
                    {
                        Grob stem = NoteColumn.GetStem(columns[i]);
                        if (stem != null)
                        {
                            Grob beam = Stem.GetBeam(stem);
                            if (beam != null)
                            {
                                beamPositions = SchemeConvert.ToDrulDouble(
                                    beam.GetProperty(QuantizedPositionsSymbol),
                                    new DrulArray<double>(0.0, 0.0));

                                subX0 = LooseColumns.RobustRelativeExtent(
                                    beam, commonx, Axis.X)[Direction.Negative];

                                subX1 = LooseColumns.RobustRelativeExtent(
                                    beam, commonx, Axis.X)[Direction.Positive];

                                break;
                            }
                        }
                    }
                }

                double beamDy = beamPositions[Direction.Positive]
                    - beamPositions[Direction.Negative];

                if (beamDy != 0.0)
                {
                    double beamSlope = subX1 == 0.0
                        ? Math.Abs(beamDy / (x1 - x0))
                        : Math.Abs(beamDy / (subX1 - subX0));

                    maxSlope = beamSlope != 0.0
                        ? Math.Max(beamSlope, maxSlopeFactor)
                        : maxSlopeFactor;

                    slope = Math.Min(slope, maxSlope);

                    if (Math.Abs(dy) > Math.Abs(maxDy))
                    {
                        dy = Math.Abs(dy * slope) <= Math.Abs(maxDy) ? dy * slope : maxDy;
                    }
                }
                else if (Math.Abs(dy) > Math.Abs(maxDy))
                {
                    dy = maxDy;
                }
            }
        }

        if (!followBeam)
        {
            points.Add(new Offset(x0 - x0, staff[dir]));
            points.Add(new Offset(x1 - x0, staff[dir]));
        }

        // This is a slight hack. We compute two encompass points from the bbox of the
        // smaller tuplets. We assume that the smaller bracket is 1.0 space high.
        for (int i = 0; i < tuplets.Count; i++)
        {
            Interval tupletX = tuplets[i].Extent(commonx, Axis.X);
            Interval tupletY = tuplets[i].Extent(commony, Axis.Y);

            if (!tuplets[i].IsLive)
            {
                continue;
            }

            DrulArray<double> nestedPositions = SchemeConvert.ToDrulDouble(
                tuplets[i].GetProperty(PositionsSymbol), new DrulArray<double>(0.0, 0.0));

            double otherDy = nestedPositions[Direction.Positive]
                - nestedPositions[Direction.Negative];

            foreach (Direction d in Directions)
            {
                double y = tupletY.LinearCombination((int)d * Math.Sign(otherDy));

                // We don't take padding into account for nested tuplets: the edges can
                // come very close to the stems, likewise for nested tuplets?
                points.Add(new Offset(tupletX[d] - x0, y));
            }

            // Check for number-on-bracket collisions.
            if (tuplets[i].GetObject(TupletNumberSymbol) is Grob number)
            {
                Interval xExt = LooseColumns.RobustRelativeExtent(number, commonx, Axis.X);
                Interval yExt = LooseColumns.RobustRelativeExtent(number, commony, Axis.Y);
                points.Add(new Offset(xExt.Center - x0, yExt[dir]));
            }
        }

        if (me.GetProperty(AvoidScriptsSymbol) is bool avoid && avoid
            && !SchemeConvert.IsNumber(me.GetProperty(OutsideStaffPrioritySymbol)))
        {
            IReadOnlyList<Grob> scripts = PointerGroupInterface.ExtractGrobSet(me, ScriptsSymbol);
            for (int i = 0; i < scripts.Count; i++)
            {
                if (!scripts[i].IsLive)
                {
                    continue;
                }

                if (SchemeConvert.IsNumber(scripts[i].GetProperty(OutsideStaffPrioritySymbol)))
                {
                    continue;
                }

                // Assume that if a script is avoiding slurs, it should not get placed
                // under a tuplet bracket.
                if (scripts[i].GetObject(SlurSymbol) is Grob)
                {
                    continue;
                }

                Interval scriptX = LooseColumns.RobustRelativeExtent(
                    scripts[i], commonx, Axis.X);

                Interval scriptY = LooseColumns.RobustRelativeExtent(
                    scripts[i], commony, Axis.Y);

                points.Add(new Offset(scriptX.Center - x0, scriptY[dir]));
            }
        }

        offset = -(int)dir * double.PositiveInfinity;
        double factor = columns.Count > 1 ? 1 / (x1 - x0) : 1.0;
        for (int i = 0; i < points.Count; i++)
        {
            double x = points[i].X;
            double tupletY2 = (dy * x * factor) + myOffset;

            if (points[i].Y * (int)dir > (offset + tupletY2) * (int)dir)
            {
                offset = points[i].Y - tupletY2;
            }
        }

        offset += ReadDouble(me.GetProperty(PaddingSymbol), 0.0) * (int)dir;

        // Horizontal brackets should not collide with staff lines. This doesn't seem to
        // support cross-staff tuplets at the moment.
        if (Math.Abs(dy) < 0.01)
        {
            double staffSpace = StaffSymbolReferencer.StaffSpace(me);

            // Quantize, then do collision check.
            offset /= 0.5 * staffSpace;

            Interval staffSpan = StaffSymbolReferencer.StaffSpan(me);

            // Include in the staff span also tuplet brackets that might collide with the
            // extremal staff lines.
            staffSpan.Widen(staffSpace);

            if (staffSpan.Contains(offset))
            {
                // Round to staff line or middle of staff space.
                offset = Math.Round(offset, MidpointRounding.ToEven);
                if (StaffSymbolReferencer.OnLine(me, (int)offset))
                {
                    offset += (int)dir;
                }
            }

            offset *= 0.5 * staffSpace;
        }
    }

    private static Stencil MakeTupletSlur(
        Grob me, Offset leftControl, Offset rightControl, DrulArray<double> shorten)
    {
        Offset dz = rightControl - leftControl;
        double length = dz.Length;

        leftControl += dz * (shorten[Direction.Negative] / length);
        rightControl -= dz * (shorten[Direction.Positive] / length);

        Offset shortenedDz = rightControl - leftControl;
        double shortenedLength = shortenedDz.Length;

        // First, get a horizontal curve. Will point upwards.
        const double HeightLimit = 1.5;
        const double Ratio = .33;
        Bezier curve = BezierBow.SlurShape(shortenedLength, HeightLimit, Ratio);

        // Flip curve if needed.
        Direction dir = DirectionalElementInterface.GetGrobDirection(me);
        curve.Scale(1, (int)dir);

        // Rotate curve to proper incline.
        double height = rightControl.Y - leftControl.Y;
        double slope = height / shortenedLength;
        curve.Rotate(Math.Atan(slope) * 180 / Math.PI);

        // Move rotated curve to correct starting point.
        curve.Translate(leftControl - curve[0]);

        object dashDefinition = me.GetProperty(DashDefinitionSymbol);
        double lineThickness = me.Layout != null
            ? me.Layout.GetDimension(LineThicknessSymbol)
            : 0.0;

        Stencil mol = Lookup.Slur(curve, lineThickness, lineThickness, dashDefinition);

        if (me.GetObject(TupletNumberSymbol) is Grob numberGrob)
        {
            double padding = ReadDouble(numberGrob.GetProperty(PaddingSymbol), 0.3);
            mol.TranslateAxis(padding * (int)dir, Axis.Y);
        }

        return mol;
    }

    private static Item GetXBoundItem(Spanner me, Direction horizontalDirection, Direction myDir)
    {
        Item g = me.GetBound(horizontalDirection);
        if (g != null
            && g.HasInterface(NoteColumnInterfaceSymbol)
            && NoteColumn.GetStem(g) != null
            && NoteColumn.Dir(g) == myDir)
        {
            Item s = NoteColumn.GetStem(g);
            if (!Stem.IsInvisible(s) && s.GetProperty(StencilSymbol) is Stencil)
            {
                g = s;
            }
        }

        return g;
    }

    private static bool EqualBounds(Spanner s1, Spanner s2)
        => s1 != null
           && s2 != null
           && ReferenceEquals(
               s1.GetBound(Direction.Negative)?.GetColumn(),
               s2.GetBound(Direction.Negative)?.GetColumn())
           && ReferenceEquals(
               s1.GetBound(Direction.Positive)?.GetColumn(),
               s2.GetBound(Direction.Positive)?.GetColumn());

    private static void ScaleDrul(ref DrulArray<double> drul, double factor)
    {
        drul[Direction.Negative] *= factor;
        drul[Direction.Positive] *= factor;
    }

    private static Interval ReadInterval(object value, Interval fallback)
    {
        if (value is Pair pair
            && SchemeConvert.IsNumber(pair.Car)
            && SchemeConvert.IsNumber(pair.Cdr))
        {
            return new Interval(
                SchemeConvert.ToDouble(pair.Car, "tuplet-bracket"),
                SchemeConvert.ToDouble(pair.Cdr, "tuplet-bracket"));
        }

        return fallback;
    }

    private static double ReadDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "tuplet-bracket")
            : fallback;

    private static Direction[] Directions { get; }
        = { Direction.Negative, Direction.Positive };
}
