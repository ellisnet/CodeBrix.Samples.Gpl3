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

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/page-turn-page-breaking.cc, lily/include/page-turn-page-breaking.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - Break_node is a struct upstream and a class here. It is only ever read after being
//     stored, and put_systems_on_pages builds a fresh one per call, so no copy is needed.
//   - print_break_node is NOT ported: it exists only behind
//     -ddebug-page-breaking-scoring, and the port has no such option wired up. Recorded
//     in the Engine PORT-COVERAGE.

/// <summary>
/// The strategy that keeps page TURNS where the player can make them.
/// <para>
/// Every place a turn is allowed is a break, and the problem is solved as a dynamic program
/// over those breaks. The parity rule is what the whole thing is for: a stretch that starts
/// on a left-hand page must END so that the next stretch starts on a left-hand page too,
/// and a blank page is inserted when that is the cheaper way to arrange it.
/// </para>
/// </summary>
public sealed class PageTurnPageBreaking : PageBreaking
{
    private static readonly Symbol ForceSymbol = Symbol.Intern("force");

    private readonly List<BreakNode> _state = new List<BreakNode>();

    /// <summary>Initializes the strategy over a book.</summary>
    /// <param name="book">The book to break.</param>
    public PageTurnPageBreaking(PaperBook book)
        : base(book, IsBreakGrob, IsBreakProb)
    {
    }

    /// <inheritdoc/>
    public override object Solve()
    {
        _state.Clear();
        Warn.Message("Calculating page and line breaks (" + LastBreakPosition()
            + " possible page breaks)...");
        for (int i = 0; i < LastBreakPosition(); i++)
        {
            CalcSubproblem(i);
        }

        List<BreakNode> breaking = new List<BreakNode>();
        int index = _state.Count - 1;
        while (index >= 0)
        {
            breaking.Add(_state[index]);
            index = _state[index].Prev;
        }

        breaking.Reverse();

        Warn.Message("Drawing systems...");
        object systems = MakeLines(breaking);
        return MakeTurnPages(breaking, systems);
    }

    /// <summary>
    /// Whether a column allows a page TURN.
    /// <para>A turnable place that is not also page-breakable and line-breakable is a
    /// contradiction, and upstream reports it rather than trusting it — a turn there would
    /// be a break the line breaker never agreed to.</para>
    /// </summary>
    private static bool IsBreakGrob(Grob g) => IsBreakAt(
        g.GetProperty("page-turn-permission"),
        g.GetProperty("page-break-permission"),
        g.GetProperty("line-break-permission"));

    private static bool IsBreakProb(Prob p) => IsBreakAt(
        p.GetProperty("page-turn-permission"),
        p.GetProperty("page-break-permission"),
        p.GetProperty("line-break-permission"));

    private static bool IsBreakAt(object turn, object pageBreak, object lineBreak)
    {
        bool turnable = turn is Symbol;

        if (turnable)
        {
            bool pageBreakable = pageBreak is Symbol;
            bool lineBreakable = lineBreak is Symbol;
            if (!pageBreakable || !lineBreakable)
            {
                Warn.ProgrammingError("found a page-turnable place which was not breakable");
                turnable = false;
            }
        }

        return turnable;
    }

    /// <summary>
    /// Spaces the systems between two breaks onto pages, and scores the result together
    /// with everything before it.
    /// </summary>
    private BreakNode PutSystemsOnPages(int start, int end, int configuration, int pageNumber)
    {
        int minPCount = MinPageCount(configuration, pageNumber);
        bool autoFirst = SchemeUtilities.ToBool(
            Book.Paper.CVariable("auto-first-page-number"));

        // When the stretch holds no intermediate breakpoint, a bad turn may be the only
        // option, so the page count is not allowed to veto it.
        if (start < end - 1 && minPCount + (autoFirst ? 0 : pageNumber % 2) > 2)
        {
            return new BreakNode();
        }

        // An ODD page number means starting on a right-hand page, which offers an even
        // number of pages plus a blank or an odd number; an even one offers the mirror.
        // Either way, take the option whose parity matches minPCount -- when it already
        // matches, the blank-page option is not even considered.
        PageSpacingResult result;
        if (start == 0 && autoFirst)
        {
            result = minPCount % 2 != 0
                ? SpaceSystemsOnNOrOneMorePages(configuration, minPCount, pageNumber, 0)
                : SpaceSystemsOnNPages(configuration, minPCount, pageNumber);
        }
        else if ((pageNumber % 2 == 0) == (minPCount % 2 == 0))
        {
            result = SpaceSystemsOnNPages(configuration, minPCount, pageNumber);
        }
        else
        {
            result = SpaceSystemsOnNOrOneMorePages(
                configuration, minPCount, pageNumber, BlankPagePenalty());
        }

        BreakNode ret = new BreakNode
        {
            Prev = start - 1,
            BreakPos = end,
            PageCount = result.Force.Count,
            FirstPageNumber = pageNumber,
            Div = CurrentConfiguration(configuration),
            SystemCount = new List<int>(result.SystemsPerPage),
            TooManyLines = AllLinesStretched(configuration),
            Demerits = result.Demerits,
        };

        if (autoFirst && start == 0 && ret.PageCount % 2 == 0)
        {
            ret.FirstPageNumber += 1;
        }

        if (start > 0)
        {
            ret.Demerits += _state[start - 1].Demerits;
        }

        return ret;
    }

    /// <summary>How many pages a node occupies, the blank one included.</summary>
    private static int TotalPageCount(BreakNode b)
    {
        int end = b.FirstPageNumber + b.PageCount;
        return end - 1 + (end % 2) - b.FirstPageNumber;
    }

    private void CalcSubproblem(int endingBreakpoint)
    {
        int end = endingBreakpoint + 1;
        BreakNode best = new BreakNode();
        BreakNode thisStartBest = new BreakNode();
        int prevBestSystemCount = 0;

        for (int start = end; start-- > 0;)
        {
            if (start < end - 1
                && ReferenceEquals(
                    BreakpointProperty(start + 1, "page-turn-permission"), ForceSymbol))
            {
                break;
            }

            if (start > 0 && best.Demerits < _state[start - 1].Demerits)
            {
                continue;
            }

            int pNum = SchemeConvert.ToInt(Book.Paper.CVariable("first-page-number"), 1);
            if (start > 0)
            {
                // Except possibly for the first page, a stretch always starts on an EVEN
                // (left-hand) page.
                pNum = _state[start - 1].FirstPageNumber;
                pNum += _state[start - 1].PageCount;
                pNum += pNum % 2;
            }

            List<int> minDivision = new List<int>();
            List<int> maxDivision = new List<int>();

            int minSysCount = MinSystemCount(start, end);
            int maxSysCount = MaxSystemCount(start, end);
            thisStartBest = new BreakNode();

            bool okPage = true;

            // Having just added a breakpoint, at least as many systems will be needed as
            // before -- a heuristic, and the reason this terminates in reasonable time.
            minSysCount = Math.Max(minSysCount, prevBestSystemCount);
            for (int sysCount = minSysCount; sysCount <= maxSysCount && okPage; sysCount++)
            {
                SetCurrentBreakpoints(start, end, sysCount, minDivision, maxDivision);
                bool found = false;

                for (int i = 0; i < CurrentConfigurationCount(); i++)
                {
                    BreakNode cur = PutSystemsOnPages(start, end, i, pNum);

                    if (double.IsInfinity(cur.Demerits)
                        || (cur.PageCount + (pNum % 2) > 2
                            && !double.IsInfinity(thisStartBest.Demerits)
                            && TotalPageCount(cur) > TotalPageCount(thisStartBest)))
                    {
                        okPage = false;
                        break;
                    }

                    if (cur.Demerits < thisStartBest.Demerits)
                    {
                        found = true;
                        thisStartBest = cur;
                        prevBestSystemCount = sysCount;

                        // Asking for more systems can be bounded below by the best
                        // division found so far.
                        minDivision = CurrentConfiguration(i);
                    }
                }

                if (!found && thisStartBest.TooManyLines)
                {
                    break;
                }
            }

            if (double.IsInfinity(thisStartBest.Demerits))
            {
                break;
            }

            if (start == 0 && end == 1 && thisStartBest.FirstPageNumber == 1
                && thisStartBest.PageCount > 1)
            {
                Warn.Warning("cannot fit the first page turn onto a single page."
                    + "  Consider setting first-page-number to an even number.");
            }

            if (thisStartBest.Demerits < best.Demerits)
            {
                best = thisStartBest;
            }
        }

        _state.Add(best);
    }

    /// <summary>Breaks every score into lines and collects the systems.</summary>
    private object MakeLines(List<BreakNode> soln)
    {
        for (int n = 0; n < soln.Count; n++)
        {
            int start = n > 0 ? soln[n - 1].BreakPos : 0;
            int end = soln[n].BreakPos;

            BreakIntoPieces(start, end, soln[n].Div);
        }

        return Systems();
    }

    /// <summary>
    /// Assembles the pages, inserting a BLANK page wherever a stretch would otherwise leave
    /// the next one starting on the wrong side.
    /// </summary>
    private object MakeTurnPages(List<BreakNode> soln, object systems)
    {
        if (systems is Nil || soln.Count == 0)
        {
            return Nil.Instance;
        }

        List<int> linesPerPage = new List<int>();
        for (int i = 0; i < soln.Count; i++)
        {
            for (int j = 0; j < soln[i].PageCount && j < soln[i].SystemCount.Count; j++)
            {
                linesPerPage.Add(soln[i].SystemCount[j]);
            }

            if (i + 1 < soln.Count && (soln[i].FirstPageNumber + soln[i].PageCount) % 2 != 0)
            {
                linesPerPage.Add(0);
            }
        }

        // This only actually changes anything when auto-first-page-number was true.
        Book.Paper.SetVariable(
            "first-page-number", SchemeConvert.FromInt(soln[0].FirstPageNumber));
        return MakePages(linesPerPage, systems);
    }

    /// <summary>One cell of the page-turn dynamic program.</summary>
    private sealed class BreakNode
    {
        /// <summary>Gets or sets the break this stretch follows.</summary>
        public int Prev { get; set; } = NoPosition;

        /// <summary>Gets or sets the break this stretch ends at.</summary>
        public int BreakPos { get; set; }

        /// <summary>Gets or sets how many pages this stretch takes.</summary>
        public int PageCount { get; set; }

        /// <summary>Gets or sets the page number this stretch starts on.</summary>
        public int FirstPageNumber { get; set; }

        /// <summary>Gets or sets the per-chunk line division this stretch uses.</summary>
        public List<int> Div { get; set; } = new List<int>();

        /// <summary>Gets or sets how many systems go on each of its pages.</summary>
        public List<int> SystemCount { get; set; } = new List<int>();

        /// <summary>Gets or sets whether every line in it came out stretched.</summary>
        public bool TooManyLines { get; set; }

        /// <summary>Gets or sets what this stretch and everything before it costs.</summary>
        public double Demerits { get; set; } = double.PositiveInfinity;
    }
}
