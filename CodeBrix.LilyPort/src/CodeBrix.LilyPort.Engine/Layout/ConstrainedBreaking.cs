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

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/constrained-breaking.cc, lily/include/constrained-breaking.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - UNSIGNED SENTINEL. Upstream indexes with vsize (unsigned) and uses VPOS, its
//     all-ones value, both as "no position" and as the loop terminator: `for (vsize i = n;
//     i != VPOS; i--)' relies on 0-- wrapping. This port indexes with int and spells those
//     loops `for (int i = n; i >= 0; i--)', which is the same iteration; NoPosition (-1) is
//     the "no position" marker. The two uses are separated deliberately, because conflating
//     them is how an off-by-one becomes a two-billion-iteration loop.
//   - Matrix<T> is Flower's and is COLUMN-MAJOR like upstream's, so at (row, col) indexing
//     carries over unchanged.
//   - Line_details is a class here (see LineDetails.cs); the state table therefore holds
//     references, and no cell is mutated after it is stored.

/// <summary>
/// The line breaker: a dynamic program that chooses where a score's lines end.
/// <para>
/// The rule it optimises is stated in upstream's own notation. Writing W for the weight of
/// a set of breaks, the optimal set for k+1 systems ending at breakpoint m is built from
/// the best set for k systems ending at some earlier j, with m appended:
/// <c>A_{k+1,m} = min over k &lt; j &lt; m of W (A_{k,j} :: m)</c>. Every cell of the
/// table stores the breakpoint the previous line ended at, so the chosen path can be
/// walked back out at the end.
/// </para>
/// <para>
/// The cost of a line is its spacing FORCE — how far it had to stretch or compress —
/// squared, plus, when the score is not ragged, the squared difference from the previous
/// line's force. That second term is what makes lines look consistent with each other
/// rather than merely each acceptable on its own.
/// </para>
/// <para>
/// The table is filled lazily. <see cref="Resize"/> extends it a few system counts at a
/// time and stops early on a row that cannot be broken at all, because a configuration too
/// cramped for k systems is too cramped for every larger k as well.
/// </para>
/// </summary>
public sealed class ConstrainedBreaking
{
    /// <summary>The "no position" marker — upstream's <c>VPOS</c>, in its non-loop use.</summary>
    public const int NoPosition = -1;

    private static readonly Symbol RaggedRightSymbol = Symbol.Intern("ragged-right");
    private static readonly Symbol RaggedLastSymbol = Symbol.Intern("ragged-last");
    private static readonly Symbol ForceSymbol = Symbol.Intern("force");
    private static readonly Symbol AllowSymbol = Symbol.Intern("allow");
    private static readonly Symbol BasicDistanceSymbol = Symbol.Intern("basic-distance");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol MinimumDistanceSymbol = Symbol.Intern("minimum-distance");
    private static readonly Symbol LineBreakPenaltySymbol = Symbol.Intern("line-break-penalty");
    private static readonly Symbol PageBreakPenaltySymbol = Symbol.Intern("page-break-penalty");
    private static readonly Symbol PageTurnPenaltySymbol = Symbol.Intern("page-turn-penalty");
    private static readonly Symbol LineBreakPermissionSymbol
        = Symbol.Intern("line-break-permission");

    private static readonly Symbol PageBreakPermissionSymbol
        = Symbol.Intern("page-break-permission");

    private static readonly Symbol PageTurnPermissionSymbol
        = Symbol.Intern("page-turn-permission");

    private PaperScore _pscore;
    private int _validSystems;
    private int _systems;
    private bool _raggedRight;
    private bool _raggedLast;

    private double _systemSystemMinDistance;
    private double _systemSystemPadding;
    private double _systemSystemSpace;
    private double _systemMarkupSpace;
    private double _scoreSystemMinDistance;
    private double _scoreSystemPadding;
    private double _scoreMarkupMinDistance;
    private double _scoreMarkupPadding;

    /* the (i,j)th entry is the configuration for breaking between columns i and j */
    private Matrix<LineDetails> _lines;

    /* the [i](j,k)th entry is the score for fitting the first k bars onto the
       first j systems, starting at the i'th allowed starting column */
    private List<Matrix<ConstrainedBreakNode>> _state;

    /* the columns at which we might be asked to start breaking */
    private List<int> _start;

    /* the corresponding index in _breaks */
    private List<int> _startingBreakpoints;

    private List<PaperColumn> _all;
    private List<int> _breaks;

    /// <summary>Initializes a breaker over a whole score.</summary>
    /// <param name="ps">The paper score to break.</param>
    public ConstrainedBreaking(PaperScore ps)
    {
        List<int> start = new List<int> { 0 };
        Initialize(ps, start);
    }

    /// <summary>
    /// Initializes a breaker that may be asked to start at any of several columns — which
    /// is what the PAGE breaker needs, since it re-breaks each page's worth of music from
    /// wherever the previous page ended.
    /// </summary>
    /// <param name="ps">The paper score to break.</param>
    /// <param name="start">The columns breaking may start at.</param>
    public ConstrainedBreaking(PaperScore ps, IReadOnlyList<int> start)
        => Initialize(ps, start);

    /// <summary>
    /// Solves for an exact number of systems, falling back as gracefully as it can when
    /// no solution satisfies the constraints.
    /// <para>
    /// When the best path does not reach the end, upstream warns, splices on a final line
    /// covering the remainder, and returns that — a visibly wrong answer in preference to
    /// none. That behaviour is reproduced, warning included.
    /// </para>
    /// </summary>
    /// <param name="start">Index into the starting columns.</param>
    /// <param name="end">Index of the ending column, or <see cref="NoPosition"/>.</param>
    /// <param name="sysCount">How many systems are wanted.</param>
    /// <returns>One solved configuration per line.</returns>
    public List<ColumnXPositions> Solve(int start, int end, int sysCount)
    {
        int startBrk = _startingBreakpoints[start];
        int endBrk = PrepareSolution(start, end, sysCount);

        Matrix<ConstrainedBreakNode> st = _state[start];
        List<ColumnXPositions> ret = new List<ColumnXPositions>();

        /* find the first solution that satisfies constraints */
        for (int sys = sysCount - 1; sys >= 0; sys--)
        {
            for (int brk = endBrk; brk >= 0; brk--)
            {
                if (!double.IsInfinity(st[brk, sys].Details.Force))
                {
                    if (brk != endBrk)
                    {
                        brk = st[brk, sys].Prev;
                        sys--;
                        Warn.Warning("cannot find line breaking that satisfies constraints");
                        ret.Add(SpaceLine(brk, endBrk));
                    }

                    /* build up the good part of the solution */
                    for (int curSys = sys; curSys >= 0; curSys--)
                    {
                        int prevBrk = st[brk, curSys].Prev;
                        if (brk == NoPosition)
                        {
                            Warn.ProgrammingError("no breakpoint in line-breaking solution");
                            break;
                        }

                        ret.Add(SpaceLine(prevBrk + startBrk, brk + startBrk));
                        brk = prevBrk;
                    }

                    ret.Reverse();
                    return ret;
                }
            }
        }

        /* if we get to here, just put everything on one line */
        if (sysCount > 0)
        {
            Warn.Warning("cannot find line breaking that satisfies constraints");
            ret.Add(SpaceLine(0, endBrk));
        }

        return ret;
    }

    /// <summary>
    /// Finds the system count with the lowest total demerits.
    /// <para>
    /// It walks up from the minimum possible count and stops EARLY once a count is both
    /// worse than the best so far and has no compressed line left in it — because past
    /// that point every extra system can only stretch the music further, and upstream
    /// takes that as the signal that adding lines has stopped helping.
    /// </para>
    /// </summary>
    /// <param name="start">Index into the starting columns.</param>
    /// <param name="end">Index of the ending column, or <see cref="NoPosition"/>.</param>
    /// <returns>One solved configuration per line.</returns>
    public List<ColumnXPositions> BestSolution(int start, int end)
    {
        int minSystems = MinSystemCount(start, end);
        int maxSystems = MaxSystemCount(start, end);
        double bestDemerits = double.PositiveInfinity;
        List<ColumnXPositions> bestSoFar = null;

        for (int i = minSystems; i <= maxSystems; i++)
        {
            int brk = PrepareSolution(start, end, i);
            double dem = _state[start][brk, i - 1].Demerits;

            if (dem < bestDemerits)
            {
                bestDemerits = dem;
                bestSoFar = Solve(start, end, i);
            }
            else
            {
                List<ColumnXPositions> cur = Solve(start, end, i);
                bool tooManyLines = true;

                for (int j = 0; j < cur.Count; j++)
                {
                    if (cur[j].Force < 0)
                    {
                        tooManyLines = false;
                        break;
                    }
                }

                if (tooManyLines)
                {
                    return bestSoFar ?? new List<ColumnXPositions>();
                }
            }
        }

        if (bestSoFar != null && bestSoFar.Count > 0)
        {
            return bestSoFar;
        }

        return Solve(start, end, maxSystems);
    }

    /// <summary>
    /// Returns the per-line details of a solution without solving its horizontal spacing —
    /// what the page breaker needs to decide how the lines stack.
    /// </summary>
    /// <param name="start">Index into the starting columns.</param>
    /// <param name="end">Index of the ending column, or <see cref="NoPosition"/>.</param>
    /// <param name="sysCount">How many systems are wanted.</param>
    /// <returns>One <see cref="LineDetails"/> per line.</returns>
    public List<LineDetails> GetLineDetails(int start, int end, int sysCount)
    {
        int endBrk = PrepareSolution(start, end, sysCount);
        Matrix<ConstrainedBreakNode> st = _state[start];
        List<LineDetails> ret = new List<LineDetails>();

        /* This loop structure is copied from Solve (). */
        /* find the first solution that satisfies constraints */
        for (int sys = sysCount - 1; sys >= 0; sys--)
        {
            for (int brk = endBrk; brk >= 0; brk--)
            {
                if (!double.IsInfinity(st[brk, sys].Details.Force))
                {
                    if (brk != endBrk)
                    {
                        /*
                          During Initialize (), we only fill out a
                          LineDetails for lines that are valid (ie. not too
                          long), otherwise line breaking becomes O(n^3).
                          In case sysCount is such that no valid solution
                          is found, we need to fill in the LineDetails.
                        */
                        LineDetails details = new LineDetails();
                        brk = st[brk, sys].Prev;
                        sys--;
                        FillLineDetails(details, brk, endBrk);
                        ret.Add(details);
                    }

                    /* build up the good part of the solution */
                    for (int curSys = sys; curSys >= 0; curSys--)
                    {
                        int prevBrk = st[brk, curSys].Prev;
                        if (brk == NoPosition)
                        {
                            Warn.ProgrammingError("no breakpoint in line-breaking solution");
                            break;
                        }

                        ret.Add(st[brk, curSys].Details);
                        brk = prevBrk;
                    }

                    ret.Reverse();
                    return ret;
                }
            }
        }

        /* if we get to here, just put everything on one line */
        if (sysCount > 0)
        {
            LineDetails details = new LineDetails();
            FillLineDetails(details, 0, endBrk);
            ret.Add(details);
        }

        return ret;
    }

    /// <summary>
    /// The fewest systems the music can be broken into without violating a constraint.
    /// </summary>
    /// <param name="start">Index into the starting columns.</param>
    /// <param name="end">Index of the ending column, or <see cref="NoPosition"/>.</param>
    /// <returns>The minimum system count.</returns>
    public int MinSystemCount(int start, int end)
    {
        int brk = PrepareSolution(start, end, 1);
        int rank = _breaks.Count - _startingBreakpoints[start];
        Matrix<ConstrainedBreakNode> st = _state[start];

        /* sysCount < rank : rank is the # of breakpoints, we can't have more systems */
        for (int sysCount = 0; sysCount < rank; sysCount++)
        {
            if (sysCount >= _validSystems)
            {
                Resize(sysCount + 3);
                st = _state[start];
            }

            if (!double.IsInfinity(st[brk, sysCount].Details.Force))
            {
                return sysCount + 1;
            }
        }

        /* no possible breaks satisfy constraints */
        return 1;
    }

    /// <summary>The most systems the music could possibly be broken into.</summary>
    /// <param name="start">Index into the starting columns.</param>
    /// <param name="end">Index of the ending column, or <see cref="NoPosition"/>.</param>
    /// <returns>The maximum system count.</returns>
    public int MaxSystemCount(int start, int end)
    {
        int brk = end >= _start.Count || end == NoPosition
            ? _breaks.Count - 1
            : _startingBreakpoints[end];
        return brk - _startingBreakpoints[start];
    }

    /// <summary>
    /// The subproblem: the best way to fit the music up to breakpoint <paramref name="brk"/>
    /// onto <paramref name="sys"/>+1 systems.
    /// <para>
    /// It walks candidate previous breakpoints DOWNWARD and stops at the first line too
    /// long to space, since every earlier candidate makes a longer line still. That break
    /// is what keeps the whole algorithm out of cubic time.
    /// </para>
    /// </summary>
    /// <param name="start">Index into the starting columns.</param>
    /// <param name="sys">The system index, counted from zero.</param>
    /// <param name="brk">The breakpoint index.</param>
    /// <returns><see langword="true"/> when a solution was found.</returns>
    private bool CalcSubproblem(int start, int sys, int brk)
    {
        bool foundSomething = false;
        int startCol = _startingBreakpoints[start];
        Matrix<ConstrainedBreakNode> st = _state[start];
        int maxIndex = brk - startCol;

        for (int j = maxIndex - 1; j >= sys; j--)
        {
            if (sys == 0 && j > 0)
            {
                /* the first line cannot have its first break after the beginning */
                continue;
            }

            LineDetails cur = _lines[brk, j + startCol];
            if (double.IsInfinity(cur.Force))
            {
                break;
            }

            double prevF = 0;
            double prevDem = 0;

            if (sys > 0)
            {
                prevF = st[j, sys - 1].Details.Force;
                prevDem = st[j, sys - 1].Demerits;
            }

            if (double.IsInfinity(prevDem))
            {
                continue;
            }

            double dem = CombineDemerits(cur.Force, prevF) + prevDem + cur.BreakPenalty;
            ConstrainedBreakNode n = st[maxIndex, sys];
            if (dem < n.Demerits)
            {
                foundSomething = true;
                n.Demerits = dem;
                n.Details = cur;
                n.Prev = j;
                st[maxIndex, sys] = n;
            }
        }

        return foundSomething;
    }

    /// <summary>Solves the horizontal spacing of one line.</summary>
    /// <param name="i">The breakpoint the line starts at.</param>
    /// <param name="j">The breakpoint the line ends at.</param>
    /// <returns>The solved positions.</returns>
    private ColumnXPositions SpaceLine(int i, int j)
    {
        object raggedRightValue = _pscore.Layout.CVariable("ragged-right");
        object raggedLastValue = _pscore.Layout.CVariable("ragged-last");
        bool raggedRight = SchemeUtilities.ToBool(raggedRightValue);
        bool raggedLast = SchemeUtilities.ToBool(raggedLastValue);

        List<PaperColumn> line = new List<PaperColumn>();
        for (int k = _breaks[i]; k <= _breaks[j] && k < _all.Count; k++)
        {
            line.Add(_all[k]);
        }

        Interval lineDims = LineDimensionInterval(_pscore.Layout, i);
        bool last = j == _breaks.Count - 1;
        bool ragged = raggedRight || (last && raggedLast);

        /* As a special case, if there is only one line in the score and ragged-right
           hasn't been specifically forbidden and the line is stretched, use
           ragged spacing. */
        if (last && i == 0 && _lines[i, j].Force >= 0
            && !IsBoolean(raggedRightValue)
            && !IsBoolean(raggedLastValue))
        {
            ragged = true;
        }

        return LineSpacing.GetLineConfiguration(
            line, lineDims.Right - lineDims.Left, lineDims.Left, ragged);
    }

    /// <summary>
    /// Grows the state table to hold <paramref name="systems"/> systems and fills the new
    /// rows.
    /// </summary>
    /// <param name="systems">The wanted system count.</param>
    private void Resize(int systems)
    {
        _systems = systems;

        if (_pscore != null && _systems > _validSystems)
        {
            for (int i = 0; i < _state.Count; i++)
            {
                _state[i].Resize(
                    _breaks.Count - _startingBreakpoints[i],
                    _systems,
                    ConstrainedBreakNode.Fresh());
            }

            /* fill out the matrices */
            for (int i = 0; i < _state.Count; i++)
            {
                for (int j = _validSystems; j < _systems; j++)
                {
                    for (int k = _startingBreakpoints[i] + j + 1; k < _breaks.Count; k++)
                    {
                        if (!CalcSubproblem(i, j, k))
                        {
                            /* if we couldn't break this, it is too cramped already */
                            break;
                        }
                    }
                }
            }

            _validSystems = _systems;
        }
    }

    private int PrepareSolution(int start, int end, int sysCount)
    {
        Resize(sysCount);
        if (end == _start.Count)
        {
            end = NoPosition;
        }

        int brk = end == NoPosition ? _breaks.Count - 1 : _startingBreakpoints[end];
        brk -= _startingBreakpoints[start];
        return brk;
    }

    private double CombineDemerits(double force, double prevForce)
    {
        if (_raggedRight)
        {
            return force * force;
        }

        return (force * force) + ((prevForce - force) * (prevForce - force));
    }

    /// <summary>
    /// Takes the STRICTER of two break permissions.
    /// <para>
    /// The rule is a lattice, not a comparison: <c>force</c> yields to anything,
    /// <c>allow</c> yields to anything that is not <c>force</c>, and everything else —
    /// which in practice means a forbidden break — wins outright. It is what keeps a page
    /// turn from being permitted where a page break is not, and a page break where a line
    /// break is not.
    /// </para>
    /// </summary>
    internal static object MinPermission(object perm1, object perm2)
    {
        if (ReferenceEquals(perm1, ForceSymbol))
        {
            return perm2;
        }

        if (ReferenceEquals(perm1, AllowSymbol) && !ReferenceEquals(perm2, ForceSymbol))
        {
            return perm2;
        }

        return Nil.Instance;
    }

    /// <summary>
    /// Finds the force of every possible line and caches the paper's spacing variables.
    /// <para>
    /// The forces come from ONE call to the spacing solver over every column, which is
    /// what makes the whole table affordable. A line whose force is infinite ends the
    /// inner walk immediately, and its <see cref="LineDetails"/> is deliberately left
    /// unfilled — filling it would make line breaking cubic, and upstream says so in
    /// <see cref="GetLineDetails"/>.
    /// </para>
    /// </summary>
    private void Initialize(PaperScore ps, IReadOnlyList<int> pagebreakColIndices)
    {
        _validSystems = _systems = 0;
        _pscore = ps;
        _lines = new Matrix<LineDetails>();
        _state = new List<Matrix<ConstrainedBreakNode>>();
        _start = new List<int>();
        _startingBreakpoints = new List<int>();
        _all = new List<PaperColumn>();
        _breaks = new List<int>();

        _systemSystemSpace = 0;
        _systemMarkupSpace = 0;
        _systemSystemPadding = 0;
        _systemSystemMinDistance = 0;
        _scoreSystemPadding = 0;
        _scoreSystemMinDistance = 0;
        _scoreMarkupPadding = 0;
        _scoreMarkupMinDistance = 0;

        if (_pscore == null)
        {
            _raggedRight = false;
            _raggedLast = false;
            return;
        }

        _raggedRight = SchemeUtilities.ToBool(_pscore.Layout.CVariable("ragged-right"));
        _raggedLast = SchemeUtilities.ToBool(_pscore.Layout.CVariable("ragged-last"));

        OutputDef l = _pscore.Layout;

        object spacingSpec = l.CVariable("system-system-spacing");
        object betweenScoresSpec = l.CVariable("score-system-spacing");
        object titleSpec = l.CVariable("score-markup-spacing");
        object pageBreakingSpacingSpec = l.CVariable("page-breaking-system-system-spacing");

        PageLayoutSpacing.ReadSpacingSpec(
            spacingSpec, BasicDistanceSymbol, ref _systemSystemSpace);
        PageLayoutSpacing.ReadSpacingSpec(
            pageBreakingSpacingSpec, BasicDistanceSymbol, ref _systemSystemSpace);
        PageLayoutSpacing.ReadSpacingSpec(
            titleSpec, BasicDistanceSymbol, ref _systemMarkupSpace);

        PageLayoutSpacing.ReadSpacingSpec(spacingSpec, PaddingSymbol, ref _systemSystemPadding);
        PageLayoutSpacing.ReadSpacingSpec(
            betweenScoresSpec, PaddingSymbol, ref _scoreSystemPadding);
        PageLayoutSpacing.ReadSpacingSpec(
            pageBreakingSpacingSpec, PaddingSymbol, ref _systemSystemPadding);
        PageLayoutSpacing.ReadSpacingSpec(titleSpec, PaddingSymbol, ref _scoreMarkupPadding);

        PageLayoutSpacing.ReadSpacingSpec(
            betweenScoresSpec, MinimumDistanceSymbol, ref _scoreSystemMinDistance);
        PageLayoutSpacing.ReadSpacingSpec(
            spacingSpec, MinimumDistanceSymbol, ref _systemSystemMinDistance);
        PageLayoutSpacing.ReadSpacingSpec(
            pageBreakingSpacingSpec, MinimumDistanceSymbol, ref _systemSystemMinDistance);
        PageLayoutSpacing.ReadSpacingSpec(
            titleSpec, MinimumDistanceSymbol, ref _scoreMarkupMinDistance);

        Interval firstLine = LineDimensionInterval(_pscore.Layout, 0);
        Interval otherLines = LineDimensionInterval(_pscore.Layout, 1);

        /* do all the rod/spring problems */
        _breaks.AddRange(_pscore.GetBreakIndices());
        _all.AddRange(_pscore.RootSystem.UsedColumns());

        _lines.Resize(_breaks.Count, _breaks.Count, null);
        for (int i = 0; i < _breaks.Count; i++)
        {
            for (int j = 0; j < _breaks.Count; j++)
            {
                _lines[i, j] = new LineDetails();
            }
        }

        if (_breaks.Count == 0)
        {
            _state.Clear();
            return;
        }

        List<double> forces = LineSpacing.GetLineForces(
            _all,
            otherLines.Length,
            otherLines.Length - firstLine.Length,
            _raggedRight);

        for (int i = 0; i + 1 < _breaks.Count; i++)
        {
            for (int j = i + 1; j < _breaks.Count; j++)
            {
                bool last = j == _breaks.Count - 1;
                bool ragged = _raggedRight || (last && _raggedLast);
                LineDetails line = _lines[j, i];

                int forceIndex = (i * _breaks.Count) + j;
                line.Force = forceIndex < forces.Count
                    ? forces[forceIndex]
                    : double.PositiveInfinity;

                if (ragged && last && !double.IsInfinity(line.Force))
                {
                    line.Force = line.Force < 0 && j > i + 1 ? double.PositiveInfinity : 0;
                }

                if (double.IsInfinity(line.Force))
                {
                    break;
                }

                FillLineDetails(line, i, j);
            }
        }

        /* work out all the starting indices */
        foreach (int pbCol in pagebreakColIndices)
        {
            /* it would seem logical to require that pagebreakColIndices
               is strictly increasing, but repeated entries can happen,
               eg. when starting a score with a \pageBreak
             */
            int j;
            for (j = 0; j + 1 < _breaks.Count && _breaks[j] < pbCol; j++)
            {
            }

            _startingBreakpoints.Add(j);
            _start.Add(_breaks[j]);
        }

        _state.Clear();
        for (int i = 0; i < _start.Count; i++)
        {
            _state.Add(new Matrix<ConstrainedBreakNode>());
        }
    }

    /// <summary>
    /// Fills in everything about a line except its horizontal spacing.
    /// </summary>
    private void FillLineDetails(LineDetails outDetails, int start, int end)
    {
        int startRank = _all[_breaks[start]].Rank;
        int endRank = _all[_breaks[end]].Rank;
        SystemGrob sys = _pscore.RootSystem;
        Interval beginOfLineExtent = sys.BeginOfLinePureHeight(startRank, endRank);
        Interval restOfLineExtent = sys.RestOfLinePureHeight(startRank, endRank);
        bool last = end == _breaks.Count - 1;

        PaperColumn c = _all[_breaks[end]];
        outDetails.LastColumn = c;
        outDetails.BreakPenalty = ToDoubleOrZero(c.GetProperty(LineBreakPenaltySymbol));
        outDetails.PagePenalty = ToDoubleOrZero(c.GetProperty(PageBreakPenaltySymbol));
        outDetails.TurnPenalty = ToDoubleOrZero(c.GetProperty(PageTurnPenaltySymbol));
        outDetails.BreakPermission = c.GetProperty(LineBreakPermissionSymbol);
        outDetails.PagePermission = c.GetProperty(PageBreakPermissionSymbol);
        outDetails.TurnPermission = c.GetProperty(PageTurnPermissionSymbol);

        /* turn permission should always be stricter than page permission
           and page permission should always be stricter than line permission */
        outDetails.PagePermission
            = MinPermission(outDetails.BreakPermission, outDetails.PagePermission);
        outDetails.TurnPermission
            = MinPermission(outDetails.PagePermission, outDetails.TurnPermission);

        beginOfLineExtent = beginOfLineExtent.IsEmpty
            || double.IsNaN(beginOfLineExtent.Left)
            || double.IsNaN(beginOfLineExtent.Right)
                ? new Interval(0, 0)
                : beginOfLineExtent;
        restOfLineExtent = restOfLineExtent.IsEmpty
            || double.IsNaN(restOfLineExtent.Left)
            || double.IsNaN(restOfLineExtent.Right)
                ? new Interval(0, 0)
                : restOfLineExtent;

        outDetails.Shape = new LineShape(beginOfLineExtent, restOfLineExtent);
        outDetails.Padding = last ? _scoreSystemPadding : _systemSystemPadding;
        outDetails.TitlePadding = _scoreMarkupPadding;
        outDetails.MinDistance = last ? _scoreSystemMinDistance : _systemSystemMinDistance;
        outDetails.TitleMinDistance = _scoreMarkupMinDistance;
        outDetails.Space = _systemSystemSpace;
        outDetails.TitleSpace = _systemMarkupSpace;
        outDetails.InverseHooke = outDetails.FullHeight() + _systemSystemSpace;

        outDetails.RefpointExtent = sys.PureRefpointExtent(startRank, endRank);
        if (outDetails.RefpointExtent.IsEmpty)
        {
            outDetails.RefpointExtent = new Interval(0, 0);
        }
    }

    /// <summary>
    /// The horizontal span available to line <paramref name="n"/> — upstream's free
    /// function <c>line_dimension_interval</c> from <c>output-def.cc</c>, which has no
    /// other caller in the port.
    /// </summary>
    /// <param name="def">The output definition.</param>
    /// <param name="n">The line index; line 0 uses <c>indent</c>, the rest <c>short-indent</c>.</param>
    /// <returns>The available interval.</returns>
    public static Interval LineDimensionInterval(OutputDef def, int n)
    {
        double lw = def.GetDimension(Symbol.Intern("line-width"));
        double ind = n != 0
            ? def.GetDimension(Symbol.Intern("short-indent"))
            : def.GetDimension(Symbol.Intern("indent"));
        return new Interval(ind, lw);
    }

    private static bool IsBoolean(object value) => value is bool;

    private static double ToDoubleOrZero(object value)
        => SchemeConvert.IsNumber(value) ? SchemeConvert.ToDouble(value, "penalty") : 0.0;

    /// <summary>
    /// One cell of the dynamic-programming table: how good the best path to here is, and
    /// which breakpoint the previous line ended at so the path can be walked back.
    /// </summary>
    public struct ConstrainedBreakNode
    {
        /// <summary>Gets or sets the breakpoint the previous line ended at.</summary>
        public int Prev { get; set; }

        /// <summary>
        /// Gets or sets the sum of all demerits up to AND INCLUDING this line — unlike
        /// the Gourlay breaker, which stores them per line.
        /// </summary>
        public double Demerits { get; set; }

        /// <summary>Gets or sets this line's details.</summary>
        public LineDetails Details { get; set; }

        /// <summary>Returns a cell in its initial, unreachable state.</summary>
        /// <returns>The fresh cell.</returns>
        public static ConstrainedBreakNode Fresh() => new ConstrainedBreakNode
        {
            Prev = NoPosition,
            Demerits = double.PositiveInfinity,
            Details = new LineDetails(),
        };
    }
}
