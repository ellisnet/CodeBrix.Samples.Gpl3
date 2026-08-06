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
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/spacing-loose-columns.cc;

// Modified by Jeremy Ellis on 2026-08-05 as part of the CodeBrix port.

/// <summary>
/// Puts the loose columns back after the spacing problem has been solved without them.
/// <para>
/// A loose column was pruned out of the solve and told which two columns it lives
/// between. Here the chain of loose columns between one solved column and the next is
/// collected into a CLIQUE and draped from the right-hand end backwards, each one
/// placed at its own spacing distance from the one after it.
/// </para>
/// </summary>
public static class LooseColumns
{
    private static readonly Symbol BetweenCols = Symbol.Intern("between-cols");
    private static readonly Symbol SpacingSymbol = Symbol.Intern("spacing");
    private static readonly Symbol GraceSpacing = Symbol.Intern("grace-spacing");
    private static readonly Symbol NoteSpacingInterface = Symbol.Intern("note-spacing-interface");

    /* Find the loose columns in POSNS, and drape them around the columns
       specified in BETWEEN-COLS.  */

    /// <summary>
    /// Positions every loose column on a solved line, relative to the solved columns on
    /// either side of it.
    /// <para>
    /// Two distance sets are kept per clique, the ideal and the tight, and the columns
    /// are placed on a SCALED blend of them: the rods that were laid for loose columns
    /// are tight ones, but other rods may have widened the gap, and in that case a
    /// crammed placement inside a roomy gap would look wrong. The blend aims for
    /// non-crammed and falls back on crammed exactly as far as it has to.
    /// </para>
    /// </summary>
    /// <param name="which">The system the line belongs to.</param>
    /// <param name="posns">The solved line.</param>
    public static void SetLooseColumns(SystemGrob which, ColumnXPositions posns)
    {
        if (which == null)
        {
            throw new ArgumentNullException(nameof(which));
        }

        if (posns == null)
        {
            throw new ArgumentNullException(nameof(posns));
        }

        int looseColCount = posns.LooseColumns.Count;
        if (looseColCount == 0)
        {
            return;
        }

        for (int i = 0; i < looseColCount; i++)
        {
            posns.LooseColumns[i].System = which;
        }

        for (int i = 0; i < looseColCount; i++)
        {
            PaperColumn loose = posns.LooseColumns[i];

            PaperColumn left = null;
            PaperColumn right = null;

            List<PaperColumn> clique = new List<PaperColumn>();
            while (true)
            {
                if (!(loose.GetObject(BetweenCols) is Pair between))
                {
                    break;
                }

                /* If the line was broken at one of the loose columns, split
                   the clique at that column. */
                if (loose.GetSystem() == null)
                {
                    break;
                }

                PaperColumn le = between.Car as PaperColumn;
                PaperColumn re = between.Cdr as PaperColumn;

                if (le == null || re == null)
                {
                    break;
                }

                if (left == null)
                {
                    left = le.GetColumn();
                    if (left.GetSystem() == null)
                    {
                        left = left.FindPrebrokenPiece(Direction.Positive);
                    }

                    clique.Add(left);
                }

                clique.Add(loose);

                right = re.GetColumn();
                loose = right;
            }

            if (right == null)
            {
                Warn.ProgrammingError(
                    "Can't attach loose column sensibly.  Attaching to end of system.");
                right = which.GetBound(Direction.Positive);
            }

            if (right == null || clique.Count == 0)
            {
                continue;
            }

            if (right.GetSystem() != null)
            {
                /* do nothing */
            }
            else if (right.FindPrebrokenPiece(Direction.Negative) != null
                     && ReferenceEquals(
                         right.FindPrebrokenPiece(Direction.Negative).GetSystem(), which))
            {
                right = right.FindPrebrokenPiece(Direction.Negative);
            }
            else if (which.GetBound(Direction.Positive).Rank < right.Rank)
            {
                right = which.GetBound(Direction.Positive);
            }
            else
            {
                Warn.ProgrammingError("Loose column does not have right side to attach to.");
                SystemGrob baseSystem = which.Original ?? which;
                int j = clique[clique.Count - 1].Rank + 1;
                int endRank = which.GetBound(Direction.Positive).Rank;
                IReadOnlyList<Grob> baseCols = baseSystem.Columns;
                for (; j < endRank && j < baseCols.Count; j++)
                {
                    if (baseCols[j] is PaperColumn candidate
                        && ReferenceEquals(candidate.GetSystem(), which))
                    {
                        right = candidate;
                    }
                }
            }

            Grob common = right.CommonRefpoint(left, Axis.X);

            clique.Add(right);

            /*
              We use two vectors to keep track of loose column spacing:
                clique_spacing keeps track of ideal spaces.
                clique_tight_spacing keeps track of minimum spaces.
              Below, a scale factor is applied to the shifting of loose columns that
              aims to preserve clique_spacing but gets closer to clique_tight_spacing as the
              space becomes smaller.  This is used because the rods placed for loose columns
              are tight (meaning they use minimum distances - see set_distances_for_loose_columns).
              However, other rods may widen this distance, in which case we don't want a crammed score.
              Thus, we aim for non-crammed, and fall back on crammed as needed.
            */
            List<double> cliqueSpacing = new List<double> { 0.0 };
            List<double> cliqueTightSpacing = new List<double> { 0.0 };
            for (int j = 1; j + 1 < clique.Count; j++)
            {
                PaperColumn cliqueCol = clique[j];

                PaperColumn looseCol = clique[j];
                PaperColumn nextCol = clique[j + 1];

                Grob spacing = cliqueCol.GetObject(SpacingSymbol) as Grob;
                if (cliqueCol.GetObject(GraceSpacing) is Grob graceSpacing)
                {
                    spacing = graceSpacing;
                }

                SpacingOptions options = new SpacingOptions();
                if (spacing != null)
                {
                    options.InitFromGrob(spacing);
                }
                else
                {
                    Warn.ProgrammingError("Column without spacing object");
                }

                Spring spring = Spring.Default;
                if (PaperColumn.IsMusical(nextCol) && PaperColumn.IsMusical(looseCol))
                {
                    if (spacing != null && spacing.HasInterface(NoteSpacingInterface))
                    {
                        spring = NoteSpacing.GetSpacing(spacing, nextCol, spring, options.Increment);
                    }
                    else
                    {
                        spring = SpacingSpanner.NoteSpacingSpring(spacing, looseCol, nextCol, options);
                    }
                }
                else
                {
                    spring = SpacingSpanner.StandardBreakableColumnSpacing(
                        spacing, looseCol, nextCol, options);
                }

                double baseNoteSpace = spring.IdealDistance;
                double tightNoteSpace = spring.MinDistance;

                double looseColHorizontalLength = looseCol.Extent(looseCol, Axis.X).Length;
                baseNoteSpace = Math.Max(baseNoteSpace, looseColHorizontalLength);
                tightNoteSpace = Math.Max(tightNoteSpace, looseColHorizontalLength);

                cliqueSpacing.Add(baseNoteSpace);
                cliqueTightSpacing.Add(tightNoteSpace);
            }

            double permissibleDistance
                = clique[clique.Count - 1].RelativeCoordinate(common, Axis.X)
                  - RobustRelativeExtent(clique[0], common, Axis.X).Right;
            Grob finishedRightColumn = clique[clique.Count - 1];

            double sumTightSpacing = 0;
            double sumSpacing = 0;

            // currently a magic number - what would be a good grob to hold this property?
            double leftPadding = 0.15;
            for (int j = 0; j < cliqueSpacing.Count; j++)
            {
                sumTightSpacing += cliqueTightSpacing[j];
                sumSpacing += cliqueSpacing[j];
            }

            double scaleFactor = Math.Max(
                0.0,
                Math.Min(
                    1.0,
                    (permissibleDistance - leftPadding - sumTightSpacing)
                        / (sumSpacing - sumTightSpacing)));
            for (int j = clique.Count - 2; j > 0; j--)
            {
                PaperColumn cliqueCol = clique[j];

                double rightPoint = finishedRightColumn.RelativeCoordinate(common, Axis.X);

                double distanceToNext
                    = cliqueTightSpacing[j]
                      + ((cliqueSpacing[j] - cliqueTightSpacing[j]) * scaleFactor);

                double myOffset = rightPoint - distanceToNext;

                cliqueCol.TranslateAxis(
                    myOffset - cliqueCol.RelativeCoordinate(common, Axis.X), Axis.X);

                finishedRightColumn = cliqueCol;
            }
        }
    }

    /// <summary>
    /// Returns a grob's extent relative to a reference point, falling back to the single
    /// point the grob sits at when it has no extent of its own.
    /// </summary>
    /// <param name="me">The grob.</param>
    /// <param name="refpoint">The reference point.</param>
    /// <param name="a">The axis.</param>
    /// <returns>The extent, never empty.</returns>
    public static Interval RobustRelativeExtent(Grob me, Grob refpoint, Axis a)
    {
        Interval ext = me.Extent(refpoint, a);
        if (ext.IsEmpty)
        {
            ext.AddPoint(me.RelativeCoordinate(refpoint, a));
        }

        return ext;
    }
}
