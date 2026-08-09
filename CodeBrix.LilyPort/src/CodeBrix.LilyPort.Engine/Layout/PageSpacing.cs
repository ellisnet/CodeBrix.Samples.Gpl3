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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/page-spacing.cc, lily/include/page-spacing.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - upstream's vsize is unsigned and its VPOS sentinel is the largest such value; the
//     port uses int with PageBreaking.NoPosition (-1), the same convention
//     ConstrainedBreaking already uses. Every place upstream relies on unsigned
//     wraparound to produce VPOS -- `cur.prev_ = page_start - 1' at page_start 0 above
//     all -- produces -1 here, which is the same sentinel by a different route.

/// <summary>
/// The penalty for a page-breaking solution that is bad but not bad enough to discard —
/// systems that would not fit the requested page count, or a page that overflows.
/// <para>
/// Upstream's own reasoning for the magnitude: large enough to dominate any reasonable
/// penalty, small enough that summing several will not overflow to infinity, so a solution
/// carrying two of them can still be told from one carrying three.
/// </para>
/// </summary>
public static class PageSpacingPenalties
{
    /// <summary>The penalty for a page whose SPACING is bad.</summary>
    public const double BadSpacing = 1e6;

    /// <summary>
    /// The penalty for disregarding a USER OVERRIDE, such as failing to satisfy
    /// <c>min-systems-per-page</c>. Deliberately two orders larger than
    /// <see cref="BadSpacing"/>: upstream ranks an ignored instruction as worse than
    /// ugly spacing.
    /// </summary>
    public const double TerribleSpacing = 1e8;
}

/// <summary>
/// The force calculation for one page's worth of rods and springs.
/// <para>
/// The vertical problem is simpler than the horizontal one — each line has rods only to
/// its predecessor and successor — so the totals can be accumulated as lines are added
/// rather than solved as a system.
/// </para>
/// </summary>
public sealed class PageSpacing
{
    private readonly PageBreaking _breaker;

    /// <summary>Initializes a page's spacing problem.</summary>
    /// <param name="pageHeight">The height available on the page.</param>
    /// <param name="breaker">The breaker, which owns the paper-level padding figures.</param>
    public PageSpacing(double pageHeight, PageBreaking breaker)
    {
        PageHeight = pageHeight;
        _breaker = breaker;
        HasFootnotes = false;
        Clear();
    }

    /// <summary>Gets the force this page's springs are stretched or compressed to.</summary>
    public double Force { get; internal set; }

    /// <summary>Gets the height available on the page.</summary>
    public double PageHeight { get; private set; }

    /// <summary>Gets the accumulated incompressible height of the lines on the page.</summary>
    public double RodHeight { get; private set; }

    /// <summary>Gets the accumulated natural length of the springs between them.</summary>
    public double SpringLength { get; private set; }

    /// <summary>Gets the accumulated inverse spring constant.</summary>
    public double InverseSpringK { get; private set; }

    /// <summary>Gets whether any line on the page has carried a footnote.</summary>
    public bool HasFootnotes { get; private set; }

    /// <summary>Gets the last line appended to the page.</summary>
    public LineDetails LastLine { get; private set; } = new LineDetails();

    /// <summary>Gets the first line on the page.</summary>
    public LineDetails FirstLine { get; private set; } = new LineDetails();

    /// <summary>
    /// Recomputes <see cref="Force"/> from the accumulated totals.
    /// <para>An overfull page is NEGATIVE infinity, not a large negative number: it is
    /// rejected outright rather than ranked.</para>
    /// </summary>
    public void CalcForce()
    {
        double height = PageHeight
            - _breaker.MinWhitespaceAtTopOfPage(FirstLine)
            - _breaker.MinWhitespaceAtBottomOfPage(LastLine);

        if (RodHeight + LastLine.BottomPadding >= height)
        {
            Force = double.NegativeInfinity;
        }
        else
        {
            Force = (height - RodHeight - LastLine.BottomPadding - SpringLength)
                / Math.Max(0.1, InverseSpringK);
        }
    }

    /// <summary>Changes the page height and recomputes the force.</summary>
    /// <param name="newHeight">The new page height.</param>
    public void Resize(double newHeight)
    {
        PageHeight = newHeight;
        CalcForce();
    }

    /// <summary>Adds a line to the BOTTOM of the page.</summary>
    /// <param name="line">The line to add.</param>
    public void AppendSystem(LineDetails line)
    {
        // The rod height test is against zero rather than against a line count, which is
        // upstream's own way of asking "is this the first line?" -- and it keeps that
        // meaning here only because FullHeight is what the first line contributes and
        // Tallness is what every later one does.
        if (RodHeight != 0.0)
        {
            RodHeight += line.Tallness;
            SpringLength += LastLine.SpringLength(line);
        }
        else
        {
            RodHeight += line.FullHeight();
            FirstLine = line;
        }

        RodHeight += AccountForFootnotes(line);
        InverseSpringK += line.InverseHooke;

        LastLine = line;

        CalcForce();
    }

    /// <summary>Adds a line to the TOP of the page.</summary>
    /// <param name="line">The line to add.</param>
    public void PrependSystem(LineDetails line)
    {
        if (RodHeight != 0.0)
        {
            SpringLength += line.SpringLength(FirstLine);
        }
        else
        {
            LastLine = line;
        }

        RodHeight -= FirstLine.FullHeight();
        RodHeight += FirstLine.Tallness;
        RodHeight += line.FullHeight();
        RodHeight += AccountForFootnotes(line);
        InverseSpringK += line.InverseHooke;

        FirstLine = line;

        CalcForce();
    }

    /// <summary>Empties the page.</summary>
    public void Clear()
    {
        Force = 0;
        RodHeight = 0;
        SpringLength = 0;
        InverseSpringK = 0;
        HasFootnotes = false;
    }

    /// <summary>
    /// The extra height a line's footnotes and in-notes demand at the foot of the page.
    /// <para>
    /// The shape of both loops is the same and is worth reading once: the separator (or
    /// the system padding) is charged ONCE, on the first note of the page, and the padding
    /// is charged after EVERY note — so the tail correction subtracts one padding back off
    /// and adds the footer padding in its place. Charging the separator per note, which is
    /// what the obvious reading of the loop body gives, silently grows the footer with
    /// every footnote on the page.
    /// </para>
    /// </summary>
    /// <param name="line">The line whose notes are being accounted for.</param>
    /// <returns>The height to add to the page's rods.</returns>
    public double AccountForFootnotes(LineDetails line)
    {
        double footnoteHeight = 0.0;
        double inNoteHeight = 0.0;
        bool hasInNotes = false;
        for (int i = 0; i < line.InNoteHeights.Count; i++)
        {
            inNoteHeight += hasInNotes ? 0.0 : _breaker.InNoteSystemPadding;
            hasInNotes = true;
            inNoteHeight += line.InNoteHeights[i];
            inNoteHeight += _breaker.InNotePadding;
        }

        for (int i = 0; i < line.FootnoteHeights.Count; i++)
        {
            footnoteHeight += HasFootnotes
                ? 0.0
                : _breaker.FootnoteSeparatorStencilHeight
                    + _breaker.FootnotePadding
                    + _breaker.FootnoteNumberRaise;
            HasFootnotes = true;
            footnoteHeight += line.FootnoteHeights[i];
            footnoteHeight += _breaker.FootnotePadding;
        }

        return inNoteHeight
            + (hasInNotes ? -_breaker.InNotePadding + _breaker.InNoteSystemPadding : 0.0)
            + footnoteHeight
            + (HasFootnotes ? -_breaker.FootnotePadding + _breaker.FootnoteFooterPadding : 0.0);
    }
}

/// <summary>
/// The dynamic-programming solver that distributes a fixed list of lines over pages.
/// <para>
/// Same shape as <see cref="ConstrainedBreaking"/>: intermediate results are kept so that
/// several page counts can be asked for without recomputing from scratch.
/// </para>
/// </summary>
public sealed class PageSpacer
{
    private static readonly Symbol ForceSymbol = Symbol.Intern("force");

    private readonly PageBreaking _breaker;
    private readonly int _firstPageNum;
    private readonly List<LineDetails> _lines;
    private readonly bool _ragged;
    private readonly bool _raggedLast;

    // A page count that was ASKED for uses the matrix, indexed by (line, page). Solving
    // without a constraint uses the flat array, indexed by line alone.
    private Matrix<PageSpacingNode> _state = new Matrix<PageSpacingNode>();
    private List<PageSpacingNode> _simpleState = new List<PageSpacingNode>();
    private int _maxPageCount;

    /// <summary>Initializes the solver over a fixed list of lines.</summary>
    /// <param name="lines">The lines to distribute, already compressed.</param>
    /// <param name="firstPageNum">The number of the first page.</param>
    /// <param name="breaker">The breaker, which owns the page heights and the penalties.</param>
    public PageSpacer(IReadOnlyList<LineDetails> lines, int firstPageNum, PageBreaking breaker)
    {
        _lines = new List<LineDetails>(lines);
        _firstPageNum = firstPageNum;
        _breaker = breaker;
        _maxPageCount = 0;
        _ragged = breaker.Ragged;
        _raggedLast = breaker.IsLast() && breaker.RaggedLast;
    }

    /// <summary>
    /// Solves without constraining the page count — the page-turn algorithm's shape.
    /// </summary>
    /// <returns>The best arrangement found.</returns>
    public PageSpacingResult Solve()
    {
        if (_simpleState.Count == 0)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                _simpleState.Add(new PageSpacingNode());
            }

            for (int i = 0; i < _lines.Count; i++)
            {
                CalcSubproblem(PageBreaking.NoPosition, i);
            }
        }

        PageSpacingResult ret = new PageSpacingResult();
        if (_simpleState.Count == 0)
        {
            return ret;
        }

        PageSpacingNode last = _simpleState[_simpleState.Count - 1];
        ret.Penalty = last.Penalty
            + _lines[_lines.Count - 1].PagePenalty
            + _lines[_lines.Count - 1].TurnPenalty;
        ret.SystemCountStatus = last.SystemCountStatus;

        // Demerits is NOT reset to zero here, and that asymmetry against Solve(int) is
        // upstream's own: this accumulates onto the constructor's infinity and stays
        // infinite. It is harmless because every caller of the unconstrained solve passes
        // the result through FinalizeSpacingResult, which recomputes demerits outright --
        // and "tidying" it to zero would change what an un-finalized result compares as.
        int system = _lines.Count - 1;
        while (system != PageBreaking.NoPosition)
        {
            PageSpacingNode cur = _simpleState[system];
            int systemCount = cur.Prev == PageBreaking.NoPosition
                ? system + 1
                : system - cur.Prev;

            ret.Force.Add(cur.Force);
            ret.SystemsPerPage.Add(systemCount);
            ret.Demerits += cur.Force * cur.Force;
            system = cur.Prev;
        }

        ret.Force.Reverse();
        ret.SystemsPerPage.Reverse();
        return ret;
    }

    /// <summary>Solves for an exact page count.</summary>
    /// <param name="pageCount">How many pages the lines must go on.</param>
    /// <returns>The best arrangement found, salvaged if the count is unreasonable.</returns>
    public PageSpacingResult Solve(int pageCount)
    {
        if (pageCount > _maxPageCount)
        {
            Resize(pageCount);
        }

        PageSpacingResult ret = new PageSpacingResult();

        int system = _lines.Count - 1;
        int extraSystems = 0;
        int extraPages = 0;

        if (double.IsInfinity(_state[system, pageCount - 1].Demerits))
        {
            Warn.Warning("tried to space systems on a bad number of pages");

            // Usually this means too many systems were crammed into too few pages. Rather
            // than crash, find the largest number of systems that DOES fit properly and
            // tack the rest onto the last page.
            int i;
            for (i = system; i > 0 && double.IsInfinity(_state[i, pageCount - 1].Demerits); i--)
            {
            }

            if (i != 0)
            {
                extraSystems = system - i;
                system = i;
            }
            else
            {
                // Failing that, chop pages off the end.
                int j;
                for (j = pageCount; j != 0 && double.IsInfinity(_state[system, j - 1].Demerits); j--)
                {
                }

                if (j != 0)
                {
                    extraPages = pageCount - j;
                    pageCount = j;
                }
                else
                {
                    return new PageSpacingResult();
                }
            }
        }

        for (int i = 0; i < pageCount; i++)
        {
            ret.Force.Add(0.0);
            ret.SystemsPerPage.Add(0);
        }

        ret.SystemCountStatus = _state[system, pageCount - 1].SystemCountStatus;
        ret.Penalty = _state[system, pageCount - 1].Penalty
            + _lines[_lines.Count - 1].PagePenalty
            + _lines[_lines.Count - 1].TurnPenalty;

        ret.Demerits = 0;
        for (int p = pageCount; p-- > 0;)
        {
            PageSpacingNode ps = _state[system, p];
            ret.Force[p] = ps.Force;
            ret.Demerits += ps.Force * ps.Force;
            ret.SystemsPerPage[p] = p == 0 ? system + 1 : system - ps.Prev;
            system = ps.Prev;
        }

        if (extraSystems != 0)
        {
            ret.SystemsPerPage[ret.SystemsPerPage.Count - 1] += extraSystems;
            ret.Force[ret.Force.Count - 1] = PageSpacingPenalties.BadSpacing;
        }

        if (extraPages != 0)
        {
            for (int i = 0; i < extraPages; i++)
            {
                ret.Force.Add(PageSpacingPenalties.BadSpacing);
                ret.SystemsPerPage.Add(0);
            }
        }

        return ret;
    }

    private void Resize(int pageCount)
    {
        if (_maxPageCount >= pageCount)
        {
            return;
        }

        _state.Resize(_lines.Count, pageCount, null);
        for (int line = 0; line < _lines.Count; line++)
        {
            for (int page = 0; page < pageCount; page++)
            {
                if (_state[line, page] == null)
                {
                    _state[line, page] = new PageSpacingNode();
                }
            }
        }

        for (int page = _maxPageCount; page < pageCount; page++)
        {
            for (int line = page; line < _lines.Count; line++)
            {
                if (!CalcSubproblem(page, line))
                {
                    break;
                }
            }
        }

        _maxPageCount = pageCount;
    }

    /// <summary>
    /// One step of the dynamic program: the best way to put lines 0..LINE onto PAGE+1
    /// pages, given that every smaller subproblem on PAGE pages has already been solved.
    /// <para>
    /// A <paramref name="page"/> of <see cref="PageBreaking.NoPosition"/> works on the
    /// unconstrained state instead, which is the page-turn algorithm's shape. The
    /// subproblems are the same, which is why upstream reuses one routine for both.
    /// </para>
    /// <para>
    /// THE EARLY EXIT IS AGAINST PAPER HEIGHT AND NOT PAGE HEIGHT, deliberately. When the
    /// page number is not yet known the page height is not either, so stopping at the
    /// page height would stop too early on a page with a large header and miss the best
    /// solution — upstream names `page-spacing-tall-headfoot.ly' as the case.
    /// </para>
    /// </summary>
    /// <param name="page">The page index, or <see cref="PageBreaking.NoPosition"/>.</param>
    /// <param name="line">The line index this subproblem ends at.</param>
    /// <returns><see langword="true"/> when a finite solution was found.</returns>
    private bool CalcSubproblem(int page, int line)
    {
        bool last = line == _lines.Count - 1;

        int pageNum = page == PageBreaking.NoPosition ? 0 : page;
        double paperHeight = _breaker.PaperHeight;
        PageSpacing space = new PageSpacing(
            _breaker.PageHeight(_firstPageNum + page, last), _breaker);
        PageSpacingNode cur = page == PageBreaking.NoPosition
            ? _simpleState[line]
            : _state[line, page];
        bool ragged = _ragged || (_raggedLast && last);
        int lineCount = 0;

        for (int pageStart = line + 1; pageStart > pageNum;)
        {
            pageStart--;

            PageSpacingNode prev = null;

            if (page == PageBreaking.NoPosition)
            {
                if (pageStart > 0)
                {
                    prev = _simpleState[pageStart - 1];
                    space.Resize(_breaker.PageHeight(prev.Page + 1, last));
                }
                else
                {
                    space.Resize(_breaker.PageHeight(_firstPageNum, last));
                }
            }
            else if (page > 0)
            {
                prev = _state[pageStart - 1, page - 1];
            }

            space.PrependSystem(_lines[pageStart]);

            bool overfull = space.RodHeight > paperHeight
                || (_ragged && space.RodHeight + space.SpringLength > paperHeight);

            // Read this the way upstream wrote it: the configuration is skipped when
            // overfull UNLESS it is the first one with this start point, or the previous
            // attempt held fewer lines than min-systems-per-page.
            if (!_breaker.TooFewLines(lineCount) && pageStart < line && overfull)
            {
                break;
            }

            lineCount += _lines[pageStart].CompressedNontitleLinesCount;

            // ⚠️ THE UNCONSTRAINED CASE MUST PASS THIS TEST, and upstream's spelling of it
            // relies on VPOS being the LARGEST unsigned value: `page > 0' is TRUE when
            // page is VPOS. Ported literally against NoPosition (-1) the test inverts, so
            // the only cell ever written is pageStart == 0 — every line's best solution
            // becomes "put lines 0..line on ONE page", and the unconstrained solver
            // answers a single page for any book, however tall. That is not a subtle
            // mis-scoring: it is the whole page-breaking decision, silently.
            if (page == PageBreaking.NoPosition || page > 0 || pageStart == 0)
            {
                // A ragged last page is left half-empty rather than balanced: balancing
                // only makes sense when the page is meant to be filled.
                if (line == _lines.Count - 1 && ragged && last && space.Force > 0)
                {
                    space.Force = 0;
                }

                double demerits = space.Force * space.Force;

                // Clamped even when the page is overfull, so that TERRIBLE_SPACING_PENALTY
                // keeps precedence over an overfull page.
                demerits = Math.Min(demerits, PageSpacingPenalties.BadSpacing);
                demerits += prev != null ? prev.Demerits : 0;

                double penalty = _breaker.LineCountPenalty(lineCount);
                if (pageStart > 0)
                {
                    penalty += _lines[pageStart - 1].PagePenalty
                        + (page % 2 == 0 ? _lines[pageStart - 1].TurnPenalty : 0);
                }

                // Widows and orphans: the last line of a paragraph landing first on a new
                // page, and the first line of one landing last on the previous page.
                if (pageStart > 0 && pageStart < _lines.Count && _lines[pageStart].LastMarkupLine)
                {
                    penalty += _breaker.OrphanPenalty;
                }

                if (pageStart > 0 && pageStart < _lines.Count
                    && _lines[pageStart - 1].FirstMarkupLine)
                {
                    penalty += _breaker.OrphanPenalty;
                }

                demerits += penalty;
                if (demerits < cur.Demerits || pageStart == line)
                {
                    cur.Demerits = demerits;
                    cur.Force = space.Force;
                    cur.Penalty = penalty + (prev != null ? prev.Penalty : 0);
                    cur.SystemCountStatus = _breaker.LineCountStatus(lineCount)
                        | (prev != null ? prev.SystemCountStatus : SystemCountStatus.Ok);
                    cur.Prev = pageStart - 1;
                    cur.Page = prev != null ? prev.Page + 1 : _firstPageNum;
                }
            }

            // scm_is_eq, so reference equality on the interned symbol -- the same test
            // ConstrainedBreaking.MinPermission makes, and NOT equal?, which would also
            // answer true for a string spelling "force".
            if (pageStart > 0 && ReferenceEquals(_lines[pageStart - 1].PagePermission, ForceSymbol))
            {
                break;
            }
        }

        return !double.IsInfinity(cur.Demerits);
    }

    /// <summary>One cell of the dynamic-programming table.</summary>
    private sealed class PageSpacingNode
    {
        /// <summary>Gets or sets what the best arrangement ending here costs.</summary>
        public double Demerits { get; set; } = double.PositiveInfinity;

        /// <summary>Gets or sets the force of the page ending here.</summary>
        public double Force { get; set; } = double.PositiveInfinity;

        /// <summary>Gets or sets the accumulated penalty.</summary>
        public double Penalty { get; set; } = double.PositiveInfinity;

        /// <summary>Gets or sets the line the previous page ended at.</summary>
        public int Prev { get; set; } = PageBreaking.NoPosition;

        /// <summary>Gets or sets the page number this cell lands on.</summary>
        public int Page { get; set; }

        /// <summary>Gets or sets whether the counts along this path are within bounds.</summary>
        public SystemCountStatus SystemCountStatus { get; set; } = SystemCountStatus.Ok;
    }
}
