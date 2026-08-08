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

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/tie-formatting-problem.cc, lily/include/tie-formatting-problem.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port.
//
// SCORER — standing rule 2 applies to this whole file. The demerit weights, the order the
// variations are generated in and the 1-opt search are decades-tuned; translate, never
// improve. Deliberate divergences are listed in PORT-COVERAGE under EPG11.
//
//   - upstream's Box default constructor leaves both intervals EMPTY, where C#'s
//     default(Box) is a point at the origin. Every map entry that upstream default-builds
//     on first touch (stem_extents_, head_extents_) is therefore created explicitly empty
//     here; getting that wrong would silently unite a real extent with a phantom point at
//     zero.
//   - upstream's chord_outlines_ etc. are std::map, whose operator[] default-inserts. The
//     port uses Dictionary and writes the insert out, so the reads that upstream does with
//     find() stay reads and the writes stay writes.

/// <summary>
/// The tie scorer: everything known about a chord's ties, and the search that places them.
/// </summary>
/// <remarks>
/// The problem is stated once (which note heads, which stems, which dots, which staff) and
/// then a small space of candidate placements is enumerated and scored. The search is
/// 1-opt: a base configuration, then every single-tie substitution tried against it.
/// </remarks>
public sealed class TieFormattingProblem
{
    private readonly Dictionary<(int Rank, Direction Dir), Skyline> _chordOutlines
        = new Dictionary<(int, Direction), Skyline>();

    private readonly Dictionary<(int Rank, Direction Dir), Box> _stemExtents
        = new Dictionary<(int, Direction), Box>();

    private readonly Dictionary<(int Rank, Direction Dir), Box> _headExtents
        = new Dictionary<(int, Direction), Box>();

    private readonly Dictionary<int, Slice> _headPositions = new Dictionary<int, Slice>();

    private readonly SortedSet<int> _dotPositions = new SortedSet<int>();
    private Interval _dotX = Interval.Empty;
    private readonly List<TieSpecification> _specifications = new List<TieSpecification>();
    private bool _useHorizontalSpacing;

    private readonly Dictionary<(int Pos, Direction Dir, int Left, int Right), TieConfiguration>
        _possibilities
            = new Dictionary<(int, Direction, int, int), TieConfiguration>();

    private Grob _xRefpoint;
    private Grob _yRefpoint;

    private static readonly Symbol StemSymbol = Symbol.Intern("stem");
    private static readonly Symbol NoteHeadsSymbol = Symbol.Intern("note-heads");
    private static readonly Symbol AccidentalGrobSymbol = Symbol.Intern("accidental-grob");
    private static readonly Symbol AfterLineBreakingSymbol = Symbol.Intern("after-line-breaking");
    private static readonly Symbol NoteHeadInterface = Symbol.Intern("note-head-interface");
    private static readonly Symbol AnnotationSymbol = Symbol.Intern("annotation");
    private static readonly Symbol DebugTieScoringSymbol = Symbol.Intern("debug-tie-scoring");

    /// <summary>The tunables this problem scores against.</summary>
    public TieDetails Details = new TieDetails();

    /// <summary>Initializes an empty problem.</summary>
    public TieFormattingProblem()
    {
        _xRefpoint = null;
        _yRefpoint = null;
        _useHorizontalSpacing = true;
    }

    /// <summary>Gets the reference point every horizontal coordinate here is measured from.</summary>
    /// <returns>The reference grob.</returns>
    public Grob CommonXRefpoint() => _xRefpoint;

    /// <summary>Gets one tie's specification.</summary>
    /// <param name="i">Which tie.</param>
    /// <returns>The specification.</returns>
    public TieSpecification GetTieSpecification(int i) => _specifications[i];

    /// <summary>Returns where a tie at a height can attach on each side.</summary>
    /// <param name="y">The height to attach at.</param>
    /// <param name="columns">The paper-column ranks on each side.</param>
    /// <returns>The horizontal attachment interval.</returns>
    public Interval GetAttachment(double y, DrulArray<int> columns)
    {
        Interval attachments = new Interval(0, 0);

        foreach (Direction d in Both)
        {
            if (_chordOutlines.TryGetValue((columns[d], d), out Skyline outline))
            {
                attachments[d] = outline.Height(y);
            }
            else
            {
                Warn.ProgrammingError("Cannot find chord outline");
            }
        }

        return attachments;
    }

    /// <summary>Builds the outline of one paper column's worth of note heads.</summary>
    /// <param name="bounds">The note heads in that column.</param>
    /// <param name="dir">Which side of the tie this column is.</param>
    /// <param name="columnRank">The paper-column rank.</param>
    public void SetColumnChordOutline(List<Item> bounds, Direction dir, int columnRank)
    {
        double staffSpace = StaffSymbolReferencer.StaffSpace(bounds[0]);

        List<Box> boxes = new List<Box>();
        List<Box> headBoxes = new List<Box>();

        Grob stem = null;
        for (int i = 0; i < bounds.Count; i++)
        {
            Grob head = bounds[i];
            if (!head.HasInterface(NoteHeadInterface))
            {
                continue;
            }

            if (stem == null)
            {
                stem = head.GetObject(StemSymbol) as Grob;
            }

            double p = StaffSymbolReferencer.GetPosition(head);
            Interval y = new Interval((p - 1) * 0.5 * staffSpace, (p + 1) * 0.5 * staffSpace);

            Interval x = head.Extent(_xRefpoint, Axis.X);
            headBoxes.Add(new Box(x, y));
            boxes.Add(new Box(x, y));

            Grob dots = RhythmicHead.GetDots(head);
            if (dir == Direction.Negative && dots != null)
            {
                Interval dotXExtent = dots.Extent(_xRefpoint, Axis.X);
                int dotPosition = (int)StaffSymbolReferencer.GetPosition(dots);

                // TODO: shouldn't this use column-rank dependent key?
                _dotPositions.Add(dotPosition);
                _dotX.Unite(dotXExtent);

                Interval dotY = dots.Extent(dots, Axis.Y);
                dotY.Translate(dotPosition * staffSpace * 0.5);

                boxes.Add(new Box(dotXExtent, dotY));
            }
        }

        (int, Direction) key = (columnRank, dir);

        if (stem != null)
        {
            if (Stem.IsNormalStem(stem))
            {
                Interval x = Interval.Empty;
                x.AddPoint(stem.RelativeCoordinate(_xRefpoint, Axis.X));
                x.Widen(staffSpace / 20); // ugh.
                Interval y = Interval.Empty;

                double stemEndPosition = 0.0;
                if (Stem.IsCrossStaff(stem))
                {
                    stemEndPosition = (int)DirectionalElementInterface.GetGrobDirection(stem)
                                      * double.PositiveInfinity;
                }
                else
                {
                    if (_useHorizontalSpacing || Stem.GetBeam(stem) == null)
                    {
                        stemEndPosition = stem.Extent(stem, Axis.Y)[
                            DirectionalElementInterface.GetGrobDirection(stem)];
                    }
                    else
                    {
                        // May want to change this to the stem's pure height...
                        stemEndPosition = Stem.HeadPositions(stem)[
                                              DirectionalElementInterface.GetGrobDirection(stem)]
                                          * staffSpace * .5;
                    }
                }

                y.AddPoint(stemEndPosition);

                Direction stemdir = DirectionalElementInterface.GetGrobDirection(stem);
                y.AddPoint(Stem.HeadPositions(stem)[-stemdir] * staffSpace * .5);

                // add extents of stem.
                boxes.Add(new Box(x, y));

                Box stemExtent = _stemExtents.TryGetValue(key, out Box existing)
                    ? existing
                    : new Box(Interval.Empty, Interval.Empty);
                stemExtent.Unite(new Box(x, y));
                _stemExtents[key] = stemExtent;

                if (dir == Direction.Negative)
                {
                    Grob flag = Stem.FlagGrob(stem);
                    if (flag != null)
                    {
                        Grob commony = stem.CommonRefpoint(flag, Axis.Y);
                        boxes.Add(new Box(
                            flag.Extent(_xRefpoint, Axis.X), flag.Extent(commony, Axis.Y)));
                    }
                }
            }
            else
            {
                Grob head = Stem.SupportHead(stem);

                // In case of invisible stem, don't pass x-center of heads.
                double xCenter = head.Extent(_xRefpoint, Axis.X).Center;
                Interval xExt = Interval.Empty;
                xExt[-dir] = xCenter;
                xExt[dir] = double.PositiveInfinity * (int)dir;
                Interval yExt = Interval.Empty;
                for (int j = 0; j < headBoxes.Count; j++)
                {
                    yExt.Unite(headBoxes[j][Axis.Y]);
                }

                boxes.Add(new Box(xExt, yExt));
            }

            IReadOnlyList<Grob> heads = PointerGroupInterface.ExtractGrobSet(stem, NoteHeadsSymbol);
            for (int i = 0; i < heads.Count; i++)
            {
                if (!(heads[i] is Item headItem) || !bounds.Contains(headItem))
                {
                    // other untied notes in the same chord.
                    Interval y = StaffSymbolReferencer.ExtentInStaff(heads[i]);
                    Interval x = heads[i].Extent(_xRefpoint, Axis.X);
                    boxes.Add(new Box(x, y));
                }

                Grob acc = heads[i].GetObject(AccidentalGrobSymbol) as Grob;
                if (acc != null)
                {
                    // trigger tie-related suicide
                    acc.GetProperty(AfterLineBreakingSymbol);
                }

                if (acc != null && acc.IsLive && dir == Direction.Positive)
                {
                    boxes.Add(new Box(
                        acc.Extent(_xRefpoint, Axis.X),
                        StaffSymbolReferencer.ExtentInStaff(acc)));
                }

                Slice slice = _headPositions.TryGetValue(columnRank, out Slice existingSlice)
                    ? existingSlice
                    : Slice.Empty;
                slice.AddPoint((int)StaffSymbolReferencer.GetPosition(heads[i]));
                _headPositions[columnRank] = slice;
            }
        }

        foreach (Direction updowndir in DownUp)
        {
            Interval x = Interval.Empty;
            Interval y = Interval.Empty;
            if (headBoxes.Count > 0)
            {
                Box b = Boundary(headBoxes, updowndir, 0);
                x = b[Axis.X];
                // upstream writes `-dir / 2` where dir is a Direction, so this is INTEGER
            // division and the argument is 0 for both sides — the interval's CENTRE.
            x[-dir] = b[Axis.X].LinearCombination(-(int)dir / 2);
                y[-updowndir] = b[Axis.Y][updowndir];
                y[updowndir] = (int)updowndir * double.PositiveInfinity;
            }

            if (!x.IsEmpty)
            {
                boxes.Add(new Box(x, y));
            }
        }

        Skyline outlineSkyline = new Skyline(boxes, Axis.Y, -dir).Padded(Details.SkylinePadding);
        _chordOutlines[key] = outlineSkyline;

        if (bounds[0].BreakStatusDirection() != Direction.Center)
        {
            Interval iv = AxisGroupInterface.StaffExtent(
                bounds[0], _xRefpoint, Axis.X, _yRefpoint, Axis.Y);
            if (iv.IsEmpty)
            {
                iv.AddPoint(bounds[0].RelativeCoordinate(_xRefpoint, Axis.X));
            }

            outlineSkyline.SetMinimumHeight(iv[-dir]);
        }
        else
        {
            Interval x = Interval.Empty;
            for (int j = 0; j < headBoxes.Count; j++)
            {
                x.Unite(headBoxes[j][Axis.X]);
            }

            outlineSkyline.SetMinimumHeight(x[dir]);
        }

        Box heads2 = new Box(Interval.Empty, Interval.Empty);
        heads2.SetEmpty();
        for (int i = 0; i < headBoxes.Count; i++)
        {
            heads2.Unite(headBoxes[i]);
        }

        _headExtents[key] = heads2;
    }

    /// <summary>Splits a tie's bounds by paper column and outlines each one.</summary>
    /// <param name="bounds">Every note head on one side of the ties.</param>
    /// <param name="dir">Which side of the ties these are.</param>
    public void SetChordOutline(List<Item> bounds, Direction dir)
    {
        List<int> ranks = new List<int>();
        for (int i = 0; i < bounds.Count; i++)
        {
            ranks.Add(bounds[i].GetColumn().Rank);
        }

        ranks.Sort();
        List<int> uniqueRanks = new List<int>();
        foreach (int rank in ranks)
        {
            if (uniqueRanks.Count == 0 || uniqueRanks[uniqueRanks.Count - 1] != rank)
            {
                uniqueRanks.Add(rank);
            }
        }

        foreach (int rank in uniqueRanks)
        {
            List<Item> colItems = new List<Item>();
            for (int j = 0; j < bounds.Count; j++)
            {
                if (bounds[j].GetColumn().Rank == rank)
                {
                    colItems.Add(bounds[j]);
                }
            }

            SetColumnChordOutline(colItems, dir, rank);
        }
    }

    /// <summary>States the problem for a single tie.</summary>
    /// <param name="tie">The tie.</param>
    public void FromTie(Spanner tie)
    {
        List<Spanner> ties = new List<Spanner> { tie };
        FromTies(ties);

        Details.FromGrob(tie);
    }

    /// <summary>States the problem for a chord's worth of ties.</summary>
    /// <param name="ties">The ties, already sorted by position.</param>
    public void FromTies(IReadOnlyList<Spanner> ties)
    {
        if (ties.Count == 0)
        {
            return;
        }

        _xRefpoint = ties[0];
        _yRefpoint = ties[0];
        foreach (Spanner tie in ties)
        {
            DrulArray<Item> bounds = tie.GetBounds();
            Item l = bounds[Direction.Negative];
            Item r = bounds[Direction.Positive];

            _xRefpoint = l.CommonRefpoint(_xRefpoint, Axis.X);
            _xRefpoint = r.CommonRefpoint(_xRefpoint, Axis.X);

            if (l.BreakStatusDirection() == Direction.Center)
            {
                _yRefpoint = l.CommonRefpoint(_yRefpoint, Axis.Y);
            }

            if (r.BreakStatusDirection() == Direction.Center)
            {
                _yRefpoint = r.CommonRefpoint(_yRefpoint, Axis.Y);
            }
        }

        Details.FromGrob(ties[0]);

        foreach (Direction d in Both)
        {
            List<Item> bounds = new List<Item>();

            foreach (Spanner tie in ties)
            {
                Item it = tie.GetBound(d);
                if (it.BreakStatusDirection() != Direction.Center)
                {
                    it = it.GetColumn();
                }

                bounds.Add(it);
            }

            SetChordOutline(bounds, d);
        }

        foreach (Spanner tie in ties)
        {
            TieSpecification spec = new TieSpecification();
            spec.FromGrob(tie);

            foreach (Direction d in Both)
            {
                spec.NoteHeadDrul[d] = Tie.Head(tie, d);
                spec.ColumnRanks[d] = Tie.GetColumnRank(tie, d);
            }

            _specifications.Add(spec);
        }
    }

    /// <summary>States the problem for a column of laissez-vibrer or repeat ties.</summary>
    /// <param name="semiTies">The semi-ties.</param>
    /// <param name="headDir">Which side of the note head they hang off.</param>
    public void FromSemiTies(IReadOnlyList<Item> semiTies, Direction headDir)
    {
        if (semiTies.Count == 0)
        {
            return;
        }

        _useHorizontalSpacing = false;
        Details.FromGrob(semiTies[0]);
        List<Item> heads = new List<Item>();

        int columnRank = 0;
        foreach (Item semiTie in semiTies)
        {
            TieSpecification spec = new TieSpecification();
            Item head = SemiTie.Head(semiTie);

            if (head == null)
            {
                Warn.ProgrammingError("LV tie without head?!");
            }

            if (head != null)
            {
                spec.Position = (int)StaffSymbolReferencer.GetPosition(head);
            }

            spec.FromGrob(semiTie);

            spec.NoteHeadDrul[headDir] = head;
            columnRank = SemiTie.GetColumnRank(semiTie);
            spec.ColumnRanks = new DrulArray<int>(columnRank, columnRank);
            heads.Add(head);
            _specifications.Add(spec);
        }

        _xRefpoint = semiTies[0];
        _yRefpoint = semiTies[0];

        for (int i = 0; i < semiTies.Count; i++)
        {
            _xRefpoint = semiTies[i].CommonRefpoint(_xRefpoint, Axis.X);
            _yRefpoint = semiTies[i].CommonRefpoint(_yRefpoint, Axis.Y);
        }

        for (int i = 0; i < heads.Count; i++)
        {
            _xRefpoint = heads[i].CommonRefpoint(_xRefpoint, Axis.X);
            _yRefpoint = heads[i].CommonRefpoint(_yRefpoint, Axis.Y);
        }

        SetChordOutline(heads, headDir);

        (int, Direction) headKey = (columnRank, headDir);
        (int, Direction) openKey = (columnRank, -headDir);
        double extremal = _chordOutlines[headKey].MaxHeight();

        Skyline open = new Skyline(headDir);
        open.SetMinimumHeight(extremal - ((int)headDir * 1.5));
        _chordOutlines[openKey] = open;
    }

    /// <summary>Returns a configuration, generating and caching it when it is new.</summary>
    /// <param name="pos">The staff position.</param>
    /// <param name="dir">The direction to bend.</param>
    /// <param name="columns">The paper-column ranks.</param>
    /// <param name="tuneDy">Whether the vertical placement may be nudged.</param>
    /// <returns>The configuration, owned by this problem.</returns>
    private TieConfiguration GetConfiguration(
        int pos, Direction dir, DrulArray<int> columns, bool tuneDy)
    {
        (int, Direction, int, int) key
            = (pos, dir, columns[Direction.Negative], columns[Direction.Positive]);

        if (_possibilities.TryGetValue(key, out TieConfiguration found))
        {
            return found;
        }

        TieConfiguration conf = GenerateConfiguration(pos, dir, columns, tuneDy);
        _possibilities[key] = conf;
        return conf;
    }

    private TieConfiguration GenerateConfiguration(
        int pos, Direction dir, DrulArray<int> columns, bool yTune)
    {
        TieConfiguration conf = new TieConfiguration();
        conf.Position = pos;
        conf.Dir = dir;

        conf.ColumnRanks = columns;

        double y = conf.Position * 0.5 * Details.StaffSpace;

        if (_dotPositions.Contains(pos))
        {
            conf.DeltaY += (int)dir * 0.25 * Details.StaffSpace;
            yTune = false;
        }

        if (yTune
            && Math.Max(
                   Math.Abs(GetHeadExtent(columns[Direction.Negative], Direction.Negative, Axis.Y)[dir] - y),
                   Math.Abs(GetHeadExtent(columns[Direction.Positive], Direction.Positive, Axis.Y)[dir] - y))
               < 0.25
            && !StaffSymbolReferencer.OnLine(Details.StaffSymbolReferencerGrob, pos))
        {
            conf.DeltaY
                = (GetHeadExtent(columns[Direction.Negative], Direction.Negative, Axis.Y)[dir] - y)
                  + ((int)dir * Details.OuterTieVerticalGap);
        }

        if (yTune)
        {
            conf.AttachmentX = GetAttachment(y + conf.DeltaY, conf.ColumnRanks);
            double h = conf.Height(Details);

            /*
              TODO:

              - should make sliding criterion, should flatten ties if

              - they're just the wrong (ie. touching line at top & bottom)
              size.
             */
            Interval staffSpan = StaffSymbolReferencer.StaffSpan(Details.StaffSymbolReferencerGrob);
            staffSpan.Widen(-1);
            bool withinStaff = staffSpan.Contains(pos);
            if (HeadPositionsSlice(columns[Direction.Negative]).Contains(pos)
                || HeadPositionsSlice(columns[Direction.Positive]).Contains(pos)
                || withinStaff)
            {
                if (h < Details.IntraSpaceThreshold * 0.5 * Details.StaffSpace)
                {
                    if (StaffSymbolReferencer.OnLine(Details.StaffSymbolReferencerGrob, pos))
                    {
                        conf.DeltaY += (int)dir * Details.TipStaffLineClearance
                                       * 0.5 * Details.StaffSpace;
                    }
                    else if (withinStaff)
                    {
                        conf.CenterTieVertically(Details);
                    }
                }
                else
                {
                    double topY = y + conf.DeltaY + ((int)conf.Dir * h);
                    double topPos = topY / (0.5 * Details.StaffSpace);
                    int roundPos = (int)LibcExtension.RoundHalfwayUp(topPos);

                    // TODO: should use other variable?
                    double clearance = Details.CenterStaffLineClearance;
                    if (Math.Abs(topPos - roundPos) < clearance
                        && StaffSymbolReferencer.OnStaffLine(
                            Details.StaffSymbolReferencerGrob, roundPos))
                    {
                        double newY = (roundPos + (clearance * (int)conf.Dir))
                                      * 0.5 * Details.StaffSpace;
                        conf.DeltaY = newY - topY;
                    }
                }
            }
        }

        conf.AttachmentX = GetAttachment(y + conf.DeltaY, conf.ColumnRanks);
        if (conf.Height(Details) < Details.IntraSpaceThreshold * 0.5 * Details.StaffSpace)
        {
            // This is less sensible for long ties, since those are more horizontal.
            Interval closeBy = GetAttachment(
                y + conf.DeltaY
                  + ((int)dir * Details.IntraSpaceThreshold * 0.25 * Details.StaffSpace),
                conf.ColumnRanks);

            Interval attachment = conf.AttachmentX;
            attachment.Intersect(closeBy);
            conf.AttachmentX = attachment;
        }

        Interval widened = conf.AttachmentX;
        widened.Widen(-Details.XGap);
        conf.AttachmentX = widened;

        if (conf.ColumnSpanLength() != 0)
        {
            /*
              avoid the stems that we attach to as well. We don't do this
              for semities (span length = 0)

              It would be better to check D against HEAD-DIRECTION if
              applicable.
            */
            foreach (Direction d in Both)
            {
                double stemY = (conf.Position * Details.StaffSpace * 0.5) + conf.DeltaY;
                if (GetStemExtent(conf.ColumnRanks[d], d, Axis.X).IsEmpty
                    || !GetStemExtent(conf.ColumnRanks[d], d, Axis.Y).Contains(stemY))
                {
                    continue;
                }

                Interval attachment = conf.AttachmentX;
                attachment[d]
                    = (int)d
                      * Math.Min(
                          (int)d * attachment[d],
                          (int)d
                            * (GetStemExtent(conf.ColumnRanks[d], d, Axis.X)[-d]
                               - ((int)d * Details.StemGap)));
                conf.AttachmentX = attachment;
            }
        }

        return conf;
    }

    /// <summary>Returns the extent of a column's note heads on one axis.</summary>
    /// <param name="col">The paper-column rank.</param>
    /// <param name="d">Which side of the tie.</param>
    /// <param name="a">The axis.</param>
    /// <returns>The extent, empty when that column is unknown.</returns>
    public Interval GetHeadExtent(int col, Direction d, Axis a)
        => _headExtents.TryGetValue((col, d), out Box box) ? box[a] : Interval.Empty;

    /// <summary>Returns the extent of a column's stem on one axis.</summary>
    /// <param name="col">The paper-column rank.</param>
    /// <param name="d">Which side of the tie.</param>
    /// <param name="a">The axis.</param>
    /// <returns>The extent, empty when that column has no stem.</returns>
    public Interval GetStemExtent(int col, Direction d, Axis a)
        => _stemExtents.TryGetValue((col, d), out Box box) ? box[a] : Interval.Empty;

    /// <summary>
    /// Scores how well a configuration serves the tie that ASKED for it — the note heads
    /// it must reach, and the direction its stems imply.
    /// </summary>
    /// <param name="conf">The configuration to score.</param>
    /// <param name="spec">What the tie asked for.</param>
    /// <param name="tiesConf">The whole-chord configuration, when there is one.</param>
    /// <param name="tieIdx">Which tie this is.</param>
    /// <returns>The demerits, when they are not being recorded on a chord configuration.</returns>
    private double ScoreAptitude(
        TieConfiguration conf, TieSpecification spec, TiesConfiguration tiesConf, int tieIdx)
    {
        double penalty = 0.0;
        double curveY = (conf.Position * Details.StaffSpace * 0.5) + conf.DeltaY;
        double tieY = spec.Position * Details.StaffSpace * 0.5;
        if (SignDirection(curveY - tieY) != conf.Dir)
        {
            double p = Details.WrongDirectionOffsetPenalty;
            if (tiesConf != null)
            {
                tiesConf.AddTieScore(p, tieIdx, "wrong dir");
            }
            else
            {
                penalty += p;
            }
        }

        {
            double relevantDist = Math.Max(Math.Abs(curveY - tieY) - 0.5, 0.0);
            double p = Details.VerticalDistancePenaltyFactor
                       * Misc.ConvexAmplifier(1.0, 0.9, relevantDist);
            if (tiesConf != null)
            {
                tiesConf.AddTieScore(p, tieIdx, "vdist");
            }
            else
            {
                penalty += p;
            }
        }

        foreach (Direction d in Both)
        {
            if (spec.NoteHeadDrul[d] == null)
            {
                continue;
            }

            Interval headX = spec.NoteHeadDrul[d].Extent(_xRefpoint, Axis.X);
            double dist = headX.Distance(conf.AttachmentX[d]);

            // TODO: flatten with log or sqrt.
            double p = Details.HorizontalDistancePenaltyFactor
                       * Misc.ConvexAmplifier(1.25, 1.0, dist);
            if (tiesConf != null)
            {
                tiesConf.AddTieScore(p, tieIdx, d == Direction.Negative ? "lhdist" : "rhdist");
            }
            else
            {
                penalty += p;
            }
        }

        if (tiesConf != null && tiesConf.Count == 1)
        {
            DrulArray<Grob> stems = new DrulArray<Grob>(null, null);
            foreach (Direction d in Both)
            {
                if (spec.NoteHeadDrul[d] == null)
                {
                    continue;
                }

                Grob stem = spec.NoteHeadDrul[d].GetObject(StemSymbol) as Grob;
                if (stem != null && Stem.IsNormalStem(stem))
                {
                    stems[d] = stem;
                }
            }

            bool tieStemDirOk = true;
            bool tiePositionDirOk = true;
            Grob leftStem = stems[Direction.Negative];
            Grob rightStem = stems[Direction.Positive];
            if (leftStem != null && rightStem == null)
            {
                tieStemDirOk = conf.Dir != DirectionalElementInterface.GetGrobDirection(leftStem);
            }
            else if (leftStem == null && rightStem != null)
            {
                tieStemDirOk = conf.Dir != DirectionalElementInterface.GetGrobDirection(rightStem);
            }
            else if (leftStem != null && rightStem != null
                     && DirectionalElementInterface.GetGrobDirection(leftStem)
                        == DirectionalElementInterface.GetGrobDirection(rightStem))
            {
                tieStemDirOk = conf.Dir != DirectionalElementInterface.GetGrobDirection(leftStem);
            }
            else if (spec.Position != 0)
            {
                tiePositionDirOk = conf.Dir == SignDirection(spec.Position);
            }

            if (!tieStemDirOk)
            {
                tiesConf.AddScore(Details.SameDirAsStemPenalty, "tie/stem dir");
            }

            if (!tiePositionDirOk)
            {
                tiesConf.AddScore(Details.SameDirAsStemPenalty, "tie/pos dir");
            }
        }

        return penalty;
    }

    private Slice HeadPositionsSlice(int rank)
        => _headPositions.TryGetValue(rank, out Slice slice) ? slice : Slice.Empty;

    /// <summary>
    /// Scores a configuration on its own merits — how the curve looks, without regard to
    /// the note heads it should connect.
    /// </summary>
    private void ScoreConfiguration(TieConfiguration conf)
    {
        if (conf.IsScored())
        {
            return;
        }

        double length = conf.AttachmentX.Length;

        double lengthPenalty
            = Misc.PeakAround(0.33 * Details.MinLength, Details.MinLength, length);
        conf.AddScore(Details.MinLengthPenaltyFactor * lengthPenalty, "minlength");

        double tipPos = conf.Position + (conf.DeltaY / 0.5 * Details.StaffSpace);
        double tipY = tipPos * Details.StaffSpace * 0.5;
        double height = conf.Height(Details);

        double topY = tipY + ((int)conf.Dir * height);
        double topPos = 2 * topY / Details.StaffSpace;
        double roundTopPos = Math.Round(topPos, MidpointRounding.ToEven);
        Interval staffSpan = StaffSymbolReferencer.StaffSpan(Details.StaffSymbolReferencerGrob);
        if (StaffSymbolReferencer.OnLine(Details.StaffSymbolReferencerGrob, (int)roundTopPos)
            && staffSpan[Direction.Positive] * 0.5 > topY)
        {
            conf.AddScore(
                Details.StaffLineCollisionPenalty
                  * Misc.PeakAround(
                      0.1 * Details.CenterStaffLineClearance,
                      Details.CenterStaffLineClearance,
                      Math.Abs(topPos - roundTopPos)),
                "line center");
        }

        int roundedTipPos = (int)Math.Round(tipPos, MidpointRounding.ToEven);
        staffSpan.Widen(-1);
        if (StaffSymbolReferencer.OnLine(Details.StaffSymbolReferencerGrob, roundedTipPos)
            && (HeadPositionsSlice(conf.ColumnRanks[Direction.Negative]).Contains(roundedTipPos)
                || HeadPositionsSlice(conf.ColumnRanks[Direction.Positive]).Contains(roundedTipPos)
                || staffSpan.Contains(roundedTipPos)))
        {
            conf.AddScore(
                Details.StaffLineCollisionPenalty
                  * Misc.PeakAround(
                      0.1 * Details.TipStaffLineClearance,
                      Details.TipStaffLineClearance,
                      Math.Abs(tipPos - Math.Round(tipPos, MidpointRounding.ToEven))),
                "tipline");
        }

        if (!_dotX.IsEmpty)
        {
            // use left edge?
            double x = _dotX.Center;

            Bezier b = conf.GetTransformedBezier(Details);
            if (b.ControlPointExtent(Axis.X).Contains(x))
            {
                double y = b.GetOtherCoordinate(Axis.X, x);

                foreach (int dotPos in _dotPositions)
                {
                    conf.AddScore(
                        Details.DotCollisionPenalty
                          * Misc.PeakAround(
                              .1 * Details.DotCollisionClearance,
                              Details.DotCollisionClearance,
                              Math.Abs((dotPos * Details.StaffSpace * 0.5) - y)),
                        "dot collision");
                }
            }
        }

        conf.SetScored();
    }

    private void ScoreTiesAptitude(TiesConfiguration ties)
    {
        if (ties.Count != _specifications.Count)
        {
            Warn.ProgrammingError("Huh? Mismatch between sizes.");
            return;
        }

        for (int i = 0; i < ties.Count; i++)
        {
            ScoreAptitude(ties[i], _specifications[i], ties, i);
        }
    }

    private void ScoreTies(TiesConfiguration ties)
    {
        if (ties.IsScored())
        {
            return;
        }

        ScoreTiesConfiguration(ties);
        ScoreTiesAptitude(ties);
        ties.SetScored();
    }

    private void ScoreTiesConfiguration(TiesConfiguration ties)
    {
        for (int i = 0; i < ties.Count; i++)
        {
            ScoreConfiguration(ties[i]);
            ties.AddTieScore(ties[i].Score(), i, "conf");
        }

        double lastEdge = 0.0;
        double lastCenter = 0.0;
        for (int i = 0; i < ties.Count; i++)
        {
            Bezier b = ties[i].GetTransformedBezier(Details);

            double center = b.CurvePoint(0.5)[Axis.Y];
            double edge = b.CurvePoint(0.0)[Axis.Y];

            if (i != 0)
            {
                if (edge <= lastEdge)
                {
                    ties.AddScore(Details.TieColumnMonotonicityPenalty, "monotone edge");
                }

                if (center <= lastCenter)
                {
                    ties.AddScore(Details.TieColumnMonotonicityPenalty, "monotone cent");
                }

                ties.AddScore(
                    Details.TieTieCollisionPenalty
                      * Misc.PeakAround(
                          0.1 * Details.TieTieCollisionDistance,
                          Details.TieTieCollisionDistance,
                          Math.Abs(center - lastCenter)),
                    "tietie center");
                ties.AddScore(
                    Details.TieTieCollisionPenalty
                      * Misc.PeakAround(
                          0.1 * Details.TieTieCollisionDistance,
                          Details.TieTieCollisionDistance,
                          Math.Abs(edge - lastEdge)),
                    "tietie edge");
            }

            lastEdge = edge;
            lastCenter = center;
        }

        if (ties.Count > 1)
        {
            ties.AddScore(
                Details.OuterTieLengthSymmetryPenaltyFactor
                  * Math.Abs(ties.Front().AttachmentX.Length - ties.Back().AttachmentX.Length),
                "length symm");

            ties.AddScore(
                Details.OuterTieVerticalDistanceSymmetryPenaltyFactor
                  * Math.Abs(
                      Math.Abs((_specifications[0].Position * 0.5 * Details.StaffSpace)
                               - ((ties.Front().Position * 0.5 * Details.StaffSpace)
                                  + ties.Front().DeltaY))
                      - Math.Abs(
                          (_specifications[_specifications.Count - 1].Position * 0.5
                           * Details.StaffSpace)
                          - ((ties.Back().Position * 0.5 * Details.StaffSpace)
                             + ties.Back().DeltaY))),
                "pos symmetry");
        }
    }

    /// <summary>
    /// Rebuilds a chord configuration with real attachments and curves, carrying any
    /// manual vertical offset across.
    /// </summary>
    /// <param name="tiesConfig">The configuration to rebuild.</param>
    /// <returns>The rebuilt configuration.</returns>
    public TiesConfiguration GenerateTiesConfiguration(TiesConfiguration tiesConfig)
    {
        TiesConfiguration copy = new TiesConfiguration();
        for (int i = 0; i < tiesConfig.Count; i++)
        {
            TieConfiguration ptr = GetConfiguration(
                tiesConfig[i].Position,
                tiesConfig[i].Dir,
                tiesConfig[i].ColumnRanks,
                !_specifications[i].HasManualDeltaY);
            if (_specifications[i].HasManualDeltaY)
            {
                // upstream mutates the CACHED configuration here, then copies it.
                ptr.DeltaY = (_specifications[i].ManualPosition - tiesConfig[i].Position)
                             * 0.5 * Details.StaffSpace;
            }

            copy.PushBack(ptr.Copy());
        }

        return copy;
    }

    private TiesConfiguration GenerateBaseChordConfiguration()
    {
        TiesConfiguration tiesConfig = new TiesConfiguration();
        foreach (TieSpecification spec in _specifications)
        {
            TieConfiguration conf = new TieConfiguration();
            if (spec.HasManualDir)
            {
                conf.Dir = spec.ManualDir;
            }

            if (spec.HasManualPosition)
            {
                conf.Position = (int)LibcExtension.RoundHalfwayUp(spec.ManualPosition);
                if (spec.HasManualDeltaY)
                {
                    conf.DeltaY = (spec.ManualPosition - conf.Position) * 0.5 * Details.StaffSpace;
                }
            }
            else
            {
                conf.Position = spec.Position;
            }

            conf.ColumnRanks = spec.ColumnRanks;
            tiesConfig.PushBack(conf);
        }

        SetTiesConfigStandardDirections(tiesConfig);
        for (int i = 0; i < tiesConfig.Count; i++)
        {
            if (_specifications[i].ManualPosition == 0.0)
            {
                tiesConfig[i].Position += (int)tiesConfig[i].Dir;
            }
        }

        tiesConfig = GenerateTiesConfiguration(tiesConfig);

        return tiesConfig;
    }

    private TiesConfiguration FindBestVariation(
        TiesConfiguration baseConfig, List<TieConfigurationVariation> vars)
    {
        TiesConfiguration best = baseConfig;

        /*
          This simply is 1-opt: we have K substitions, and we try applying
          exactly every one for each.
        */
        for (int i = 0; i < vars.Count; i++)
        {
            TiesConfiguration variant = baseConfig.Copy();
            for (int j = 0; j < vars[i].IndexSuggestionPairs.Count; j++)
            {
                variant[vars[i].IndexSuggestionPairs[j].Index]
                    = vars[i].IndexSuggestionPairs[j].Suggestion.Copy();
            }

            variant.ResetScore();
            ScoreTies(variant);

            if (variant.Score() < best.Score())
            {
                best = variant;
            }
        }

        return best;
    }

    /// <summary>Runs the search and returns the placement that scores best.</summary>
    /// <returns>The chosen configuration.</returns>
    public TiesConfiguration GenerateOptimalConfiguration()
    {
        TiesConfiguration baseConfig = GenerateBaseChordConfiguration();
        ScoreTies(baseConfig);

        List<TieConfigurationVariation> vars;
        if (_specifications.Count > 1)
        {
            vars = GenerateCollisionVariations(baseConfig);
        }
        else
        {
            vars = GenerateSingleTieVariations(baseConfig);
        }

        TiesConfiguration best = FindBestVariation(baseConfig, vars);

        if (_specifications.Count > 1)
        {
            vars = GenerateExtremalTieVariations(best);
            best = FindBestVariation(best, vars);
        }

        return best;
    }

    private void SetTiesConfigStandardDirections(TiesConfiguration tieConfigs)
    {
        if (tieConfigs.IsEmpty)
        {
            return;
        }

        if (tieConfigs.Front().Dir == Direction.Center)
        {
            TieConfiguration front = tieConfigs.Front();

            if (tieConfigs.Count == 1)
            {
                front.Dir = SignDirection(front.Position);
            }

            if (front.Dir == Direction.Center)
            {
                front.Dir = tieConfigs.Count > 1 ? Direction.Negative : Details.NeutralDirection;
            }
        }

        if (tieConfigs.Back().Dir == Direction.Center)
        {
            tieConfigs.Back().Dir = Direction.Positive;
        }

        // Seconds
        for (int i = 1; i < tieConfigs.Count; i++)
        {
            double diff = tieConfigs[i].Position - tieConfigs[i - 1].Position;

            double spanDiff = _specifications[i].ColumnSpan() - _specifications[i - 1].ColumnSpan();
            if (spanDiff != 0.0 && Math.Abs(diff) <= 2)
            {
                if (spanDiff > 0)
                {
                    tieConfigs[i].Dir = Direction.Positive;
                }
                else if (spanDiff < 0)
                {
                    tieConfigs[i - 1].Dir = Direction.Negative;
                }
            }
            else if (Math.Abs(diff) <= 1)
            {
                if (tieConfigs[i - 1].Dir == Direction.Center)
                {
                    tieConfigs[i - 1].Dir = Direction.Negative;
                }

                if (tieConfigs[i].Dir == Direction.Center)
                {
                    tieConfigs[i].Dir = Direction.Positive;
                }
            }
        }

        for (int i = 0; i < tieConfigs.Count; i++)
        {
            TieConfiguration conf = tieConfigs[i];
            if (conf.Dir != Direction.Center)
            {
                continue;
            }

            Direction positionDir = SignDirection(conf.Position);
            if (positionDir == Direction.Center)
            {
                positionDir = Direction.Negative;
            }

            conf.Dir = positionDir;
        }
    }

    private List<TieConfigurationVariation> GenerateExtremalTieVariations(TiesConfiguration ties)
    {
        List<TieConfigurationVariation> vars = new List<TieConfigurationVariation>();
        for (int i = 1; i <= Details.MultiTieRegionSize; i++)
        {
            DrulArray<TieConfiguration> configs = new DrulArray<TieConfiguration>(null, null);
            foreach (Direction d in DownUp)
            {
                TieConfiguration config = Boundary(ties, d, 0);
                if (config.Dir == d && !Boundary(_specifications, d, 0).HasManualPosition)
                {
                    TieConfigurationVariation var = new TieConfigurationVariation();
                    configs[d] = GetConfiguration(
                        config.Position + ((int)d * i), d, config.ColumnRanks, true);
                    var.AddSuggestion(d == Direction.Negative ? 0 : ties.Count - 1, configs[d]);
                    vars.Add(var);
                }
            }

            if (configs[Direction.Negative] != null && configs[Direction.Positive] != null)
            {
                TieConfigurationVariation var = new TieConfigurationVariation();
                var.AddSuggestion(0, configs[Direction.Negative]);
                var.AddSuggestion(ties.Count - 1, configs[Direction.Positive]);
                vars.Add(var);
            }
        }

        return vars;
    }

    private List<TieConfigurationVariation> GenerateSingleTieVariations(TiesConfiguration ties)
    {
        List<TieConfigurationVariation> vars = new List<TieConfigurationVariation>();

        int sz = Details.SingleTieRegionSize;
        if (_specifications[0].HasManualPosition)
        {
            sz = 1;
        }

        for (int i = 0; i < sz; i++)
        {
            foreach (Direction d in Both)
            {
                if (i == 0 && ties[0].Dir == d)
                {
                    continue;
                }

                int p = ties[0].Position + (i * (int)d);

                if (!_specifications[0].HasManualDir || d == _specifications[0].ManualDir)
                {
                    TieConfigurationVariation var = new TieConfigurationVariation();
                    var.AddSuggestion(
                        0,
                        GetConfiguration(
                            p, d, _specifications[0].ColumnRanks,
                            !_specifications[0].HasManualDeltaY));
                    vars.Add(var);
                }
            }
        }

        return vars;
    }

    private List<TieConfigurationVariation> GenerateCollisionVariations(TiesConfiguration ties)
    {
        double centerDistanceTolerance = 0.25;

        List<TieConfigurationVariation> vars = new List<TieConfigurationVariation>();
        double lastCenter = 0.0;
        for (int i = 0; i < ties.Count; i++)
        {
            Bezier b = ties[i].GetTransformedBezier(Details);

            double center = b.CurvePoint(0.5)[Axis.Y];

            if (i != 0)
            {
                if (center <= lastCenter + centerDistanceTolerance)
                {
                    if (!_specifications[i].HasManualDir)
                    {
                        TieConfigurationVariation var = new TieConfigurationVariation();
                        var.AddSuggestion(
                            i,
                            GetConfiguration(
                                _specifications[i].Position - (int)ties[i].Dir,
                                -ties[i].Dir,
                                ties[i].ColumnRanks,
                                !_specifications[i].HasManualDeltaY));

                        vars.Add(var);
                    }

                    if (!_specifications[i - 1].HasManualDir)
                    {
                        TieConfigurationVariation var = new TieConfigurationVariation();
                        var.AddSuggestion(
                            i - 1,
                            GetConfiguration(
                                _specifications[i - 1].Position - (int)ties[i - 1].Dir,
                                -ties[i - 1].Dir,
                                _specifications[i - 1].ColumnRanks,
                                !_specifications[i - 1].HasManualDeltaY));

                        vars.Add(var);
                    }

                    if (i == 1 && !_specifications[i - 1].HasManualPosition
                        && ties[i - 1].Dir == Direction.Negative)
                    {
                        TieConfigurationVariation var = new TieConfigurationVariation();
                        var.AddSuggestion(
                            i - 1,
                            GetConfiguration(
                                _specifications[i - 1].Position - 1,
                                Direction.Negative,
                                _specifications[i - 1].ColumnRanks,
                                !_specifications[i - 1].HasManualDeltaY));
                        vars.Add(var);
                    }

                    // NOTE: upstream writes `i == ties.size ()`, which cannot hold inside a
                    // loop bounded by `i < ties.size ()`. The branch is therefore DEAD
                    // upstream, and is reproduced dead here rather than "fixed" — making it
                    // live would add a variation upstream never scores. See PORT-COVERAGE.
                    if (i == ties.Count && !_specifications[i].HasManualPosition
                        && ties[i].Dir == Direction.Positive)
                    {
                        TieConfigurationVariation var = new TieConfigurationVariation();
                        var.AddSuggestion(
                            i,
                            GetConfiguration(
                                _specifications[i].Position + 1,
                                Direction.Positive,
                                _specifications[i].ColumnRanks,
                                !_specifications[i].HasManualDeltaY));
                        vars.Add(var);
                    }
                }
                else if (_dotPositions.Contains(ties[i].Position)
                         && !_specifications[i].HasManualPosition)
                {
                    TieConfigurationVariation var = new TieConfigurationVariation();
                    var.AddSuggestion(
                        i,
                        GetConfiguration(
                            ties[i].Position + (int)ties[i].Dir,
                            ties[i].Dir,
                            ties[i].ColumnRanks,
                            !_specifications[i].HasManualDeltaY));
                    vars.Add(var);
                }
            }

            lastCenter = center;
        }

        return vars;
    }

    /// <summary>Applies a user-supplied <c>tie-configuration</c> list to the specifications.</summary>
    /// <param name="manualConfigs">The list.</param>
    public void SetManualTieConfiguration(object manualConfigs)
    {
        int k = 0;
        object s = manualConfigs;
        while (s is Pair pair && k < _specifications.Count)
        {
            object entry = pair.Car;
            if (entry is Pair entryPair)
            {
                TieSpecification spec = _specifications[k];

                if (SchemeConvert.IsNumber(entryPair.Car))
                {
                    spec.HasManualPosition = true;
                    spec.ManualPosition = SchemeConvert.ToDouble(entryPair.Car, "tie-configuration");

                    // TODO: check whether inexact? is an appropriate condition here
                    spec.HasManualDeltaY = entryPair.Car is double;
                }

                if (SchemeConvert.IsNumber(entryPair.Cdr))
                {
                    spec.HasManualDir = true;
                    spec.ManualDir = new Direction(
                        (int)SchemeConvert.ToLong(entryPair.Cdr, "tie-configuration"));
                }
            }

            k++;
            s = pair.Cdr;
        }
    }

    /// <summary>Writes each tie's score card onto its grob when tie-score debugging is on.</summary>
    /// <param name="baseConfig">The chosen configuration.</param>
    public void SetDebugScoring(TiesConfiguration baseConfig)
    {
        if (SchemeUtilities.ToBool(_xRefpoint?.Layout?.LookupVariable(DebugTieScoringSymbol)))
        {
            for (int i = 0; i < baseConfig.Count; i++)
            {
                string card = baseConfig.CompleteTieCard(i);
                _specifications[i].TieGrob.SetProperty(AnnotationSymbol, card);
            }
        }
    }

    /// <summary>Prints a configuration, for use from a debugger.</summary>
    /// <param name="ties">The configuration.</param>
    public void PrintTiesConfiguration(TiesConfiguration ties)
    {
        for (int i = 0; i < ties.Count; i++)
        {
            string manPos = _specifications[i].HasManualPosition ? "(M)" : string.Empty;
            string manDir = _specifications[i].HasManualDir ? "(M)" : string.Empty;
            string dir = ties[i].Dir == Direction.Positive ? "up" : "dn";

            Console.Write("(P" + ties[i].Position + manPos + ", " + dir + manDir + ") ");
        }

        Console.WriteLine();
    }

    // upstream's `for (const auto d : {LEFT, RIGHT})` and `{DOWN, UP}`. LEFT and DOWN are
    // both -1, so the two arrays are the same values under different names; they are kept
    // separate here for the same reason upstream keeps them separate — readability at the
    // call site.
    private static readonly Direction[] Both = { Direction.Negative, Direction.Positive };

    private static readonly Direction[] DownUp = { Direction.Negative, Direction.Positive };

    // upstream's boundary() template: element i counting inward from the named side.
    private static T Boundary<T>(IReadOnlyList<T> v, Direction dir, int i)
        => v[dir == Direction.Negative ? i : v.Count - 1 - i];

    private static TieConfiguration Boundary(TiesConfiguration v, Direction dir, int i)
        => v[dir == Direction.Negative ? i : v.Count - 1 - i];

    // upstream's `Direction (Real)` / `Direction (int)`: BOTH collapse to the sign, and
    // the port's Direction constructors do the same, so this is just the named form.
    private static Direction SignDirection(double value) => new Direction(value);
}

/// <summary>One 1-opt substitution: replace some ties' configurations and re-score.</summary>
public sealed class TieConfigurationVariation
{
    /// <summary>The substitutions this variation applies.</summary>
    public List<(int Index, TieConfiguration Suggestion)> IndexSuggestionPairs { get; }
        = new List<(int, TieConfiguration)>();

    /// <summary>Adds one substitution.</summary>
    /// <param name="index">Which tie to replace.</param>
    /// <param name="suggestion">What to replace it with.</param>
    public void AddSuggestion(int index, TieConfiguration suggestion)
        => IndexSuggestionPairs.Add((index, suggestion));
}
