/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com>

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
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/optimal-page-breaking.cc, lily/minimal-page-breaking.cc, lily/one-page-breaking.cc, lily/one-line-page-breaking.cc, lily/one-line-auto-height-breaking.cc;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - the five strategies share one file because each is a single `solve' over
//     PageBreaking's machinery, and the port keeps a family of small related types
//     together rather than spreading five thirty-line files across the folder (the
//     BreakForbidEngravers.cs precedent). The `was previously' line names all five.

/// <summary>
/// The default strategy: search for the page count and the line division that together
/// score best.
/// <para>
/// The search runs OUTWARD from the line breaker's ideal system count — first trying fewer
/// systems, then more — and each direction bounds the next configuration by the best one
/// found so far, because <c>SetCurrentBreakpoints</c> is exponential without a bound.
/// </para>
/// </summary>
public sealed class OptimalPageBreaking : PageBreaking
{
    /// <summary>Initializes the strategy over a book.</summary>
    /// <param name="book">The book to break.</param>
    public OptimalPageBreaking(PaperBook book)
        : base(book, null, null)
    {
    }

    /// <inheritdoc/>
    public override object Solve()
    {
        int end = LastBreakPosition();
        int maxSysCount = MaxSystemCount(0, end);
        int firstPageNum = SchemeConvert.ToInt(Book.Paper.CVariable("first-page-number"), 1);

        SetToIdealLineConfiguration(0, end);

        PageSpacingResult best = new PageSpacingResult();
        object forcedPageCount = Book.Paper.CVariable("page-count");
        bool pageCountIsForced = SchemeConvert.IsNumber(forcedPageCount);
        int pageCount = 0;
        List<int> idealLineDivision = CurrentConfiguration(0);
        List<int> bestDivision = idealLineDivision;
        int minSysCount = 1;

        // SystemCount counts NON-TITLE systems only.
        int idealSysCount = SystemCount();

        if (pageCountIsForced)
        {
            if (SchemeConvert.ToInt(forcedPageCount, 0) <= 0)
            {
                Warn.Warning("page-count must be positive");
                pageCountIsForced = false;
            }
            else
            {
                pageCount = SchemeConvert.ToInt(forcedPageCount, 1);
            }
        }

        if (pageCountIsForced)
        {
            // With both systems-per-page and page-count given, the system count is known
            // exactly -- unless the two cannot both be satisfied, in which case
            // systems-per-page is the one that yields.
            if (SystemsPerPage() > 0)
            {
                idealSysCount = SystemsPerPage() * pageCount;

                if (idealSysCount > MaxSystemCount(0, end)
                    || idealSysCount < MinSystemCount(0, end))
                {
                    Warn.Warning("could not satisfy systems-per-page and page-count "
                        + "at the same time, ignoring systems-per-page");
                    idealSysCount = SystemCount();
                    minSysCount = pageCount;
                }
                else
                {
                    SetCurrentBreakpoints(0, end, idealSysCount);
                    minSysCount = maxSysCount = idealSysCount;
                    idealLineDivision = bestDivision = CurrentConfiguration(0);
                }
            }
            else
            {
                minSysCount = pageCount;
            }

            best = SpaceSystemsOnNPages(0, pageCount, firstPageNum);
        }
        else
        {
            Warn.Message("Finding the ideal number of pages...");

            best = SpaceSystemsOnBestPages(0, firstPageNum);

            pageCount = best.SystemsPerPage.Count;
            if (pageCount == 0)
            {
                minSysCount = 0;
            }
            else
            {
                minSysCount = idealSysCount - best.SystemsPerPage[pageCount - 1];

                if (pageCount > 1 && best.SystemsPerPage[pageCount - 2] > 1)
                {
                    minSysCount -= best.SystemsPerPage[pageCount - 2];
                }

                // Upstream's first test is "subtraction wrapped around" on an unsigned
                // type; with int it can only be the <= 0 half, and both are kept so the
                // condition reads the same against the original.
                if (minSysCount > idealSysCount || minSysCount <= 0)
                {
                    minSysCount = 1;
                }
            }
        }

        if (pageCount == 1)
        {
            Warn.Message("Fitting music on 1 page...");
        }
        else if (SchemeConvert.IsNumber(forcedPageCount) || pageCount == 0)
        {
            Warn.Message("Fitting music on " + pageCount + " pages...");
        }
        else
        {
            Warn.Message("Fitting music on " + (pageCount - 1) + " or " + pageCount + " pages...");
        }

        // Try FEWER systems than the line breaker's ideal.
        List<int> bound = idealLineDivision;
        for (int sysCount = idealSysCount; sysCount >= minSysCount; sysCount--)
        {
            PageSpacingResult bestForThisSysCount = new PageSpacingResult();
            SetCurrentBreakpoints(0, end, sysCount, new List<int>(), bound);

            for (int i = 0; i < CurrentConfigurationCount(); i++)
            {
                PageSpacingResult cur = SchemeConvert.IsNumber(forcedPageCount)
                    ? SpaceSystemsOnNPages(i, pageCount, firstPageNum)
                    : SpaceSystemsOnBestPages(i, firstPageNum);

                if (cur.Demerits < bestForThisSysCount.Demerits)
                {
                    bestForThisSysCount = cur;
                    bound = CurrentConfiguration(i);
                }
            }

            if (bestForThisSysCount.Demerits < best.Demerits)
            {
                best = bestForThisSysCount;
                bestDivision = bound;
            }

            // Two ways of telling we already have TOO FEW systems: one page fewer than
            // wanted with the pages stretched on average, or spacing worse than
            // BAD_SPACING_PENALTY. Either way, keep going if max-systems-per-page still
            // demands fewer.
            if (!best.SystemCountStatus.HasFlag(SystemCountStatus.TooMany))
            {
                if (bestForThisSysCount.PageCount() < pageCount
                    && bestForThisSysCount.AverageForce() > 0)
                {
                    break;
                }

                if (bestForThisSysCount.Demerits >= PageSpacingPenalties.BadSpacing)
                {
                    break;
                }
            }
        }

        // Try MORE systems than the ideal. Upstream calls this more or less copy-and-paste
        // of the loop above, and it is kept that way rather than factored: the two differ
        // in which bound they carry and in their exit test.
        bound = idealLineDivision;
        int prevActualSysCount = 0;
        for (int sysCount = idealSysCount + 1; sysCount <= maxSysCount; sysCount++)
        {
            double bestDemeritsForThisSysCount = double.PositiveInfinity;
            SetCurrentBreakpoints(0, end, sysCount, bound);

            for (int i = 0; i < CurrentConfigurationCount(); i++)
            {
                int minPCount = MinPageCount(i, firstPageNum);

                if (minPCount > pageCount)
                {
                    continue;
                }

                PageSpacingResult cur = SchemeConvert.IsNumber(forcedPageCount)
                    ? SpaceSystemsOnNPages(i, pageCount, firstPageNum)
                    : SpaceSystemsOnBestPages(i, firstPageNum);

                if (cur.Demerits < best.Demerits)
                {
                    best = cur;
                    bestDivision = CurrentConfiguration(i);
                }

                if (cur.Demerits < bestDemeritsForThisSysCount)
                {
                    bestDemeritsForThisSysCount = cur.Demerits;
                    bound = CurrentConfiguration(i);
                }
            }

            int actualSysCount = 0;
            foreach (int v in best.SystemsPerPage)
            {
                actualSysCount += v;
            }

            // Infinitely bad results stop the search when either we do NOT have too few
            // systems -- so there is no point asking for more -- or we do, but asking for
            // more is not actually producing more.
            if (bestDemeritsForThisSysCount >= PageSpacingPenalties.BadSpacing
                && (!best.SystemCountStatus.HasFlag(SystemCountStatus.TooFew)
                    || actualSysCount == prevActualSysCount))
            {
                break;
            }

            prevActualSysCount = actualSysCount;
        }

        Warn.Message("Drawing systems...");
        BreakIntoPieces(0, end, bestDivision);
        object lines = Systems();
        return MakePages(best.SystemsPerPage, lines);
    }
}

/// <summary>
/// The cheap strategy: take the line breaker's ideal configuration and pack the systems
/// onto as few pages as they will go, without searching.
/// </summary>
public sealed class MinimalPageBreaking : PageBreaking
{
    /// <summary>Initializes the strategy over a book.</summary>
    /// <param name="book">The book to break.</param>
    public MinimalPageBreaking(PaperBook book)
        : base(book, null, null)
    {
    }

    /// <inheritdoc/>
    public override object Solve()
    {
        int end = LastBreakPosition();

        Warn.Message("Calculating line breaks...");
        SetToIdealLineConfiguration(0, end);
        BreakIntoPieces(0, end, CurrentConfiguration(0));

        Warn.Message("Calculating page breaks...");
        int firstPageNum = SchemeConvert.ToInt(Book.Paper.CVariable("first-page-number"), 1);
        PageSpacingResult res = PackSystemsOnLeastPages(0, firstPageNum);
        object lines = Systems();
        return MakePages(res.SystemsPerPage, lines);
    }
}

/// <summary>
/// Puts everything on ONE page and grows the paper to fit.
/// <para>
/// It works in three moves: set the page height to something enormous, break lines and
/// pages as usual, then compute the height the result actually needs and set that. The
/// enormous value is 1e6 and not larger for a concrete reason upstream records —
/// <c>Stencil::translate</c> raises a programming error beyond it.
/// </para>
/// </summary>
public sealed class OnePageBreaking : PageBreaking
{
    /// <summary>Initializes the strategy over a book.</summary>
    /// <param name="book">The book to break.</param>
    public OnePageBreaking(PaperBook book)
        : base(book, null, null)
    {
    }

    /// <inheritdoc/>
    public override object Solve()
    {
        Book.Paper.SetVariable("paper-height", 1e6);

        Warn.Message("Calculating line breaks...");
        int end = LastBreakPosition();
        SetToIdealLineConfiguration(0, end);
        BreakIntoPieces(0, end, CurrentConfiguration(0));

        Warn.Message("Fitting music on 1 page...");
        int firstPageNum = SchemeConvert.ToInt(Book.Paper.CVariable("first-page-number"), 1);
        PageSpacingResult res = SpaceSystemsOnNPages(0, 1, firstPageNum);
        object lines = Systems();
        object pages = MakePages(res.SystemsPerPage, lines);

        if (!(pages is Pair pagesPair) || !(pagesPair.Car is Prob pagePb))
        {
            return pages;
        }

        // Larger values are LOWER on the page, and the last line is not necessarily the
        // lowest, so the whole configuration has to be scanned.
        List<double> linePosns = new List<double>();
        double lowestLinePos = 0;

        object config = pagePb.GetProperty("configuration");
        foreach (object thisPos in Pair.ToList(config))
        {
            double value = SchemeConvert.ToDouble(thisPos, 0.0);
            linePosns.Add(value);
            if (value > lowestLinePos)
            {
                lowestLinePos = value;
            }
        }

        List<double> lineHeights = new List<double>();
        for (int i = 0; i < SystemSpecs.Count; i++)
        {
            if (SystemSpecs[i].PaperScore != null)
            {
                List<SystemGrob> broken = SystemSpecs[i].PaperScore.RootSystem.BrokenSystems();
                for (int s = 0; s < broken.Count; s++)
                {
                    SystemGrob system = broken[s];
                    lineHeights.Add(system.Extent(system, Axis.Y).Length);
                }
            }
            else if (SystemSpecs[i].Prob != null
                && SystemSpecs[i].Prob.GetProperty("stencil") is Stencil stil)
            {
                lineHeights.Add(stil.Extent(Axis.Y).Length);
            }
        }

        double lowestBound = 0;
        for (int i = 0; i < lineHeights.Count && i < linePosns.Count; i++)
        {
            double lowBound = lineHeights[i] + linePosns[i];
            if (lowBound > lowestBound)
            {
                lowestBound = lowBound;
            }
        }

        object lastBottom = Book.Paper.CVariable("last-bottom-spacing");

        lowestBound += ReadSpacingAlist(lastBottom, Symbol.Intern("padding"));

        double basicDist = ReadSpacingAlist(lastBottom, Symbol.Intern("basic-distance"));
        double minimumDist = ReadSpacingAlist(lastBottom, Symbol.Intern("minimum-distance"));
        double maxDist = Math.Max(basicDist, minimumDist);

        // A musical system's refpoint sits above its upper bound; a top-level markup's
        // refpoint is zero.
        double refpointDist = 0;

        List<object> linesProbs = Pair.ToList(pagePb.GetProperty("lines"));
        if (linesProbs.Count != 0 && linesProbs[linesProbs.Count - 1] is Prob lastLinePb)
        {
            object refpointExtent = lastLinePb.GetProperty("staff-refpoint-extent");
            if (refpointExtent is Pair refPair && SchemeConvert.IsNumber(refPair.Car))
            {
                refpointDist = -SchemeConvert.ToDouble(refPair.Car, 0.0);
            }
        }

        double lastBottomBound = lowestLinePos + refpointDist + maxDist;
        if (lastBottomBound > lowestBound)
        {
            lowestBound = lastBottomBound;
        }

        double footHeight = pagePb.GetProperty("foot-stencil") is Stencil footStil
            ? footStil.Extent(Axis.Y).Length
            : 0.0;

        double topMargin = SchemeConvert.ToDouble(Book.Paper.CVariable("top-margin"), 0.0);
        double bottomMargin = SchemeConvert.ToDouble(Book.Paper.CVariable("bottom-margin"), 0.0);
        double pprHeight = topMargin + bottomMargin + lowestBound + footHeight;

        Book.Paper.SetVariable("paper-height", pprHeight);

        // bottom-edge is what places the footer: tagline, footnotes and the rest.
        pagePb.SetProperty("bottom-edge", pprHeight - bottomMargin);

        return pages;
    }

    /// <summary>
    /// Reads one number out of a spacing alist, answering ZERO when it is absent — which is
    /// upstream's own fallback here and is NOT the same as the <c>-infinity</c> that
    /// <c>read_spacing_spec</c> leaves a caller's variable at.
    /// </summary>
    private static double ReadSpacingAlist(object spec, Symbol sym)
    {
        if (SchemeUtilities.Assq(sym, spec) is Pair pair && SchemeConvert.IsNumber(pair.Cdr))
        {
            return SchemeConvert.ToDouble(pair.Cdr, 0.0);
        }

        return 0;
    }
}

/// <summary>
/// Puts every score on a single page WIDE ENOUGH to hold it on one line, ignoring line and
/// page breaks entirely, and widens <c>paper-width</c> to match.
/// </summary>
public class OneLinePageBreaking : PageBreaking
{
    /// <summary>Initializes the strategy over a book.</summary>
    /// <param name="book">The book to break.</param>
    public OneLinePageBreaking(PaperBook book)
        : base(book, null, null)
    {
    }

    /// <inheritdoc/>
    public override object Solve()
    {
        double unusedMaxHeight = 0;
        return SolveAndProvideMaxHeight(ref unusedMaxHeight);
    }

    /// <summary>
    /// Solves, and reports the tallest system's height — which the auto-height variant
    /// needs and this one throws away.
    /// </summary>
    /// <param name="maxHeight">Receives the tallest system's height.</param>
    /// <returns>The pages, as a Scheme list.</returns>
    protected object SolveAndProvideMaxHeight(ref double maxHeight)
    {
        double maxWidth = 0;
        List<object> allPages = new List<object>();

        for (int i = 0; i < SystemSpecs.Count; ++i)
        {
            if (SystemSpecs[i].PaperScore == null)
            {
                continue;
            }

            PaperScore ps = SystemSpecs[i].PaperScore;
            List<PaperColumn> cols = ps.RootSystem.UsedColumns();

            // No indent, "infinite" line width, ragged.
            ColumnXPositions pos = LineSpacing.GetLineConfiguration(
                cols, double.MaxValue, 0, true);
            List<ColumnXPositions> positions = new List<ColumnXPositions> { pos };

            ps.RootSystem.BreakIntoPieces(positions);
            ps.RootSystem.DoBreakSubstitutionAndFixupRefpoints();
            List<SystemGrob> broken = ps.RootSystem.BrokenSystems();
            if (broken.Count == 0)
            {
                continue;
            }

            SystemGrob system = broken[0];

            List<int> linesPerPage = new List<int> { 1 };
            object systems = Pair.List(system);
            object pages = MakePages(linesPerPage, systems);

            maxWidth = Math.Max(maxWidth, system.Extent(system, Axis.X).Length);
            maxHeight = Math.Max(maxHeight, system.Extent(system, Axis.Y).Length);
            if (pages is Pair pagesPair)
            {
                allPages.Add(pagesPair.Car);
            }
        }

        // Widen the paper so every system fits. Upstream notes that per-page widths would
        // be nicer and would need backend support.
        double rightMargin = SchemeConvert.ToDouble(Book.Paper.CVariable("right-margin"), 0.0);
        double leftMargin = SchemeConvert.ToDouble(Book.Paper.CVariable("left-margin"), 0.0);
        Book.Paper.SetVariable("paper-width", maxWidth + rightMargin + leftMargin);

        return Pair.ListFrom(allPages);
    }
}

/// <summary>
/// <see cref="OneLinePageBreaking"/>, and grows <c>paper-height</c> to the tallest system
/// as well as the width.
/// </summary>
public sealed class OneLineAutoHeightBreaking : OneLinePageBreaking
{
    /// <summary>Initializes the strategy over a book.</summary>
    /// <param name="book">The book to break.</param>
    public OneLineAutoHeightBreaking(PaperBook book)
        : base(book)
    {
    }

    /// <inheritdoc/>
    public override object Solve()
    {
        double maxHeight = 0;
        object pages = SolveAndProvideMaxHeight(ref maxHeight);

        double topMargin = SchemeConvert.ToDouble(Book.Paper.CVariable("top-margin"), 0.0);
        double bottomMargin = SchemeConvert.ToDouble(Book.Paper.CVariable("bottom-margin"), 0.0);
        Book.Paper.SetVariable("paper-height", maxHeight + topMargin + bottomMargin);

        return pages;
    }
}
