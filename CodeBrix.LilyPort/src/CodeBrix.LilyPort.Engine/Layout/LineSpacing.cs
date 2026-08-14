/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1999--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/simple-spacer.cc (get_line_forces, get_line_configuration);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Turns a run of paper columns into a spacing problem and solves it.
/// <para>
/// This is the half of <c>simple-spacer.cc</c> that reads the score rather than doing
/// the arithmetic: it collects each column's spring and rods, hands them to a
/// <see cref="SimpleSpacer"/>, and reports back either one line's positions or the
/// cost of every possible line.
/// </para>
/// </summary>
public static class LineSpacing
{
    private static readonly Symbol BetweenCols = Symbol.Intern("between-cols");
    private static readonly Symbol KeepInsideLine = Symbol.Intern("keep-inside-line");
    private static readonly Symbol LineBreakPermission = Symbol.Intern("line-break-permission");
    private static readonly Symbol ForceSymbol = Symbol.Intern("force");

    /// <summary>
    /// One column's contribution to the spacing problem: the spring to its neighbour
    /// and the rods that reach forward from it.
    /// </summary>
    private sealed class ColumnDescription
    {
        internal Spring Spring { get; set; } = Spring.Default;

        internal Spring EndSpring { get; set; } = Spring.Default;

        internal List<(int Right, double Distance)> Rods { get; } = new List<(int, double)>();

        /* use these if they end at the last column of the line */
        internal List<(int Right, double Distance)> EndRods { get; } = new List<(int, double)>();

        internal object BreakPermission { get; set; } = Nil.Instance;

        internal Interval KeepInsideLine { get; set; } = Interval.Empty;
    }

    /// <summary>
    /// Determines whether a column is loose: positioned relative to its neighbours
    /// rather than solved for.
    /// </summary>
    /// <param name="grob">The column.</param>
    /// <returns><see langword="true"/> when the column is loose.</returns>
    public static bool IsLoose(Grob grob) => grob?.GetObject(BetweenCols) is Pair;

    /// <summary>
    /// Solves one line and returns where every column goes.
    /// <para>
    /// The first and last columns must already be prebroken, which is the state
    /// <see cref="SystemGrob.PreProcessing"/> leaves every breakable column in. When
    /// one is not, the original column is used in its place and a programming error
    /// names it — see PORT-COVERAGE, "A TEST-FIXTURE TRAP WORTH KNOWING".
    /// </para>
    /// </summary>
    /// <param name="columns">The columns on the line, first and last included.</param>
    /// <param name="lineLength">The width available.</param>
    /// <param name="indent">How far the line is indented.</param>
    /// <param name="ragged">Whether the line is ragged-right.</param>
    /// <returns>The solved positions.</returns>
    public static ColumnXPositions GetLineConfiguration(
        IReadOnlyList<PaperColumn> columns,
        double lineLength,
        double indent,
        bool ragged)
    {
        if (columns == null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        ColumnXPositions result = new ColumnXPositions();
        if (columns.Count == 0)
        {
            return result;
        }

        WarnAboutUnbrokenBreakColumns(columns, false, "get_line_configuration");

        result.Columns.Add(PrebrokenOr(columns[0], Direction.Positive));
        for (int i = 1; i + 1 < columns.Count; i++)
        {
            if (IsLoose(columns[i]))
            {
                result.LooseColumns.Add(columns[i]);
            }
            else
            {
                result.Columns.Add(columns[i]);
            }
        }

        result.Columns.Add(PrebrokenOr(columns[columns.Count - 1], Direction.Negative));

        /* since we've already put our line-ending column in the column list, we can
           ignore the end_XXX_ fields of our column_description */
        SimpleSpacer spacer = new SimpleSpacer();
        List<ColumnDescription> descriptions = new List<ColumnDescription>();
        for (int i = 0; i + 1 < result.Columns.Count; i++)
        {
            ColumnDescription description = Describe(result.Columns, i, i == 0);
            descriptions.Add(description);
            spacer.AddSpring(description.Spring);
        }

        for (int i = 0; i < descriptions.Count; i++)
        {
            foreach ((int Right, double Distance) rod in descriptions[i].Rods)
            {
                spacer.AddRod(i, rod.Right, rod.Distance);
            }

            if (!descriptions[i].KeepInsideLine.IsEmpty)
            {
                spacer.AddRod(i, descriptions.Count, descriptions[i].KeepInsideLine.Right);
                spacer.AddRod(0, i, -descriptions[i].KeepInsideLine.Left);
            }
        }

        SpacerSolution solution = spacer.Solve(lineLength, ragged);
        result.Force = spacer.ForcePenalty(lineLength, solution.Force, ragged);
        result.Configuration = spacer.SpringPositions(solution.Force, ragged);
        for (int i = 0; i < result.Configuration.Count; i++)
        {
            result.Configuration[i] += indent;
        }

        result.SatisfiesConstraints = solution.Fits;

        /*
          Check if breaking constraints are met.
        */
        for (int i = 1; i + 1 < result.Columns.Count; i++)
        {
            if (ReferenceEquals(result.Columns[i].GetProperty(LineBreakPermission), ForceSymbol))
            {
                result.SatisfiesConstraints = false;
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the cost of every possible line, as a square matrix indexed by
    /// (start break, end break).
    /// <para>
    /// A combination that does not fit costs infinity — EXCEPT when it is a single
    /// span between adjacent break points, which is scored -200000 instead. That
    /// number is upstream's: a line that cannot be broken any further must remain
    /// selectable, or the breaker would have nothing to choose.
    /// </para>
    /// <para>
    /// Every column that can begin or end a line — the first, the last, and each
    /// breakable column between them — must already be prebroken, which is the state
    /// <see cref="SystemGrob.PreProcessing"/> leaves them in. When one is not, the
    /// last spring of every candidate line ending there silently becomes a default
    /// spring and every force comes out wrong, so a programming error names the
    /// column — see PORT-COVERAGE, "A TEST-FIXTURE TRAP WORTH KNOWING".
    /// </para>
    /// </summary>
    /// <param name="columns">Every column in the score.</param>
    /// <param name="lineLength">The width available.</param>
    /// <param name="indent">How far the first line is indented.</param>
    /// <param name="ragged">Whether lines are ragged-right.</param>
    /// <returns>The cost matrix, row-major.</returns>
    public static List<double> GetLineForces(
        IReadOnlyList<PaperColumn> columns,
        double lineLength,
        double indent,
        bool ragged)
    {
        if (columns == null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        List<PaperColumn> nonLoose = new List<PaperColumn>();
        foreach (PaperColumn column in columns)
        {
            if (!IsLoose(column) || PaperColumn.IsBreakable(column))
            {
                nonLoose.Add(column);
            }
        }

        WarnAboutUnbrokenBreakColumns(nonLoose, true, "get_line_forces");

        List<int> breaks = new List<int> { 0 };
        List<ColumnDescription> descriptions = new List<ColumnDescription> { new ColumnDescription() };

        for (int i = 1; i + 1 < nonLoose.Count; i++)
        {
            if (PaperColumn.IsBreakable(nonLoose[i]))
            {
                breaks.Add(descriptions.Count);
            }

            descriptions.Add(Describe(nonLoose, i, false));
        }

        breaks.Add(descriptions.Count);

        List<double> force = new List<double>(breaks.Count * breaks.Count);
        for (int i = 0; i < breaks.Count * breaks.Count; i++)
        {
            force.Add(double.PositiveInfinity);
        }

        for (int b = 0; b + 1 < breaks.Count; b++)
        {
            descriptions[breaks[b]] = Describe(nonLoose, breaks[b], true);
            int start = breaks[b];

            for (int c = b + 1; c < breaks.Count; c++)
            {
                int end = breaks[c];
                SimpleSpacer spacer = new SimpleSpacer();

                for (int i = breaks[b]; i < end - 1; i++)
                {
                    spacer.AddSpring(descriptions[i].Spring);
                }

                spacer.AddSpring(descriptions[end - 1].EndSpring);

                for (int i = breaks[b]; i < end; i++)
                {
                    foreach ((int Right, double Distance) rod in descriptions[i].Rods)
                    {
                        if (rod.Right < end)
                        {
                            spacer.AddRod(i - start, rod.Right - start, rod.Distance);
                        }
                    }

                    foreach ((int Right, double Distance) rod in descriptions[i].EndRods)
                    {
                        if (rod.Right == end)
                        {
                            spacer.AddRod(i - start, end - start, rod.Distance);
                        }
                    }

                    if (!descriptions[i].KeepInsideLine.IsEmpty)
                    {
                        spacer.AddRod(i - start, end - start, descriptions[i].KeepInsideLine.Right);
                        spacer.AddRod(0, i - start, -descriptions[i].KeepInsideLine.Left);
                    }
                }

                SpacerSolution solution = spacer.Solve(b == 0 ? lineLength - indent : lineLength, ragged);
                force[(b * breaks.Count) + c] = spacer.ForcePenalty(lineLength, solution.Force, ragged);

                if (!solution.Fits)
                {
                    force[(b * breaks.Count) + c] = c == b + 1 ? -200000 : double.PositiveInfinity;
                    break;
                }

                if (end < descriptions.Count
                    && ReferenceEquals(descriptions[end].BreakPermission, ForceSymbol))
                {
                    break;
                }
            }
        }

        return force;
    }

    private static ColumnDescription Describe(
        IReadOnlyList<PaperColumn> columns,
        int index,
        bool lineStarter)
    {
        PaperColumn column = columns[index];
        if (lineStarter)
        {
            column = PrebrokenOr(column, Direction.Positive);
        }

        ColumnDescription description = new ColumnDescription();

        PaperColumn next = NextSpaceableColumn(columns, index);
        if (next != null)
        {
            description.Spring = SpaceableGrob.GetSpring(column, next);
        }

        if (index + 1 < columns.Count)
        {
            PaperColumn endColumn = columns[index + 1].FindPrebrokenPiece(Direction.Negative);
            if (endColumn != null)
            {
                description.EndSpring = SpaceableGrob.GetSpring(column, endColumn);
            }
        }

        object cursor = SpaceableGrob.GetMinimumDistances(column);
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry && entry.Car is PaperColumn other)
            {
                int found = IndexOfRank(columns, index, other);
                if (found >= 0)
                {
                    double distance = entry.Cdr is double value ? value : 0.0;
                    if (ReferenceEquals(columns[found], other))
                    {
                        description.Rods.Add((found, distance));
                    }
                    else
                    {
                        /* it must end at the LEFT prebroken_piece */
                        description.EndRods.Add((found, distance));
                    }
                }
            }
            else
            {
                Warn.ProgrammingError(
                    "minimum-distances holds an object that is not a paper column");
            }

            cursor = pair.Cdr;
        }

        if (!lineStarter && SchemeUtilities.ToBool(column.GetProperty(KeepInsideLine)))
        {
            description.KeepInsideLine = column.Extent(column, Axis.X);
        }

        description.BreakPermission = column.GetProperty(LineBreakPermission);
        return description;
    }

    /// <summary>
    /// Reports break-relevant columns that were never prebroken. The real pipeline
    /// cannot reach this state — <see cref="SystemGrob.PreProcessing"/> prebreaks
    /// every breakable column before spacing runs — so reaching it means a hand-built
    /// fixture, whose springs then silently fall back to defaults and whose forces
    /// all come out wrong in a way that looks like a solver bug. Upstream has no such
    /// check because it never needs one; the diagnostic is new in the port, and it is
    /// diagnostic ONLY — results are unchanged. See PORT-COVERAGE.
    /// </summary>
    /// <param name="columns">The columns about to be spaced.</param>
    /// <param name="interiorBreakables">Whether interior breakable columns can also
    /// end a line, as in get_line_forces; the boundary columns always can.</param>
    /// <param name="caller">The entry-point name for the message.</param>
    private static void WarnAboutUnbrokenBreakColumns(
        IReadOnlyList<PaperColumn> columns,
        bool interiorBreakables,
        string caller)
    {
        List<int> ranks = new List<int>();
        for (int i = 0; i < columns.Count; i++)
        {
            bool breakRelevant = i == 0
                || i == columns.Count - 1
                || (interiorBreakables && PaperColumn.IsBreakable(columns[i]));
            if (breakRelevant && !columns[i].IsBroken)
            {
                ranks.Add(columns[i].Rank);
            }
        }

        if (ranks.Count > 0)
        {
            Warn.ProgrammingError(
                caller + ": column(s) with rank(s) " + string.Join(", ", ranks)
                + " can begin or end a line but have never been prebroken, so their"
                + " springs fall back to defaults and the spacing comes out wrong."
                + " SystemGrob.PreProcessing prebreaks first in the real pipeline;"
                + " test fixtures must do the same (see PORT-COVERAGE.txt,"
                + " 'A TEST-FIXTURE TRAP WORTH KNOWING').");
        }
    }

    private static PaperColumn PrebrokenOr(PaperColumn column, Direction direction)
        => column.FindPrebrokenPiece(direction) ?? column;

    private static PaperColumn NextSpaceableColumn(IReadOnlyList<PaperColumn> columns, int starting)
    {
        for (int i = starting + 1; i < columns.Count; i++)
        {
            if (!IsLoose(columns[i]))
            {
                return columns[i];
            }
        }

        return null;
    }

    private static int IndexOfRank(IReadOnlyList<PaperColumn> columns, int from, PaperColumn target)
    {
        // Upstream uses lower_bound over the rank-ordered column list, then checks the
        // ranks match. Same answer, without needing the list to be a random-access
        // range of the exact same objects.
        for (int i = from; i < columns.Count; i++)
        {
            if (columns[i].Rank == target.Rank)
            {
                return i;
            }
        }

        return -1;
    }
}
