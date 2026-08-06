/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/paper-column.cc, lily/include/paper-column.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// One horizontal position in the score: everything sounding at the same moment
/// hangs off the same column.
/// <para>
/// Columns are numbered sequentially on creation, and are created IN PAIRS — a
/// non-musical column always has an even rank and the musical one that follows it an
/// odd rank. Horizontal spacing is solved between columns, not between the grobs
/// themselves, which is what keeps the spacing problem small.
/// </para>
/// </summary>
public class PaperColumn : Item
{
    /// <summary>The rank of a column that has not been given one yet.</summary>
    public const int InvalidRank = -1;

    private static readonly Symbol PaperColumnInterface = Symbol.Intern("paper-column-interface");
    private static readonly Symbol WhenSymbol = Symbol.Intern("when");
    private static readonly Symbol ShortestStarterDuration = Symbol.Intern("shortest-starter-duration");
    private static readonly Symbol LineBreakPermission = Symbol.Intern("line-break-permission");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol BoundedByMe = Symbol.Intern("bounded-by-me");
    private static readonly Symbol UsedSymbol = Symbol.Intern("used");
    private static readonly Symbol LabelsSymbol = Symbol.Intern("labels");
    private static readonly Symbol HorizontalSkylines = Symbol.Intern("horizontal-skylines");
    private static readonly Symbol BreakAlignmentSymbol = Symbol.Intern("break-alignment");
    private static readonly Symbol RhythmicHeadInterface = Symbol.Intern("rhythmic-head-interface");

    /* after line breaking, `System` indicates in which line this column is */
    private SystemGrob _system;

    /// <summary>Initializes a paper column from its type's basic property alist.</summary>
    /// <param name="basicProperties">The immutable alist for this grob type.</param>
    public PaperColumn(object basicProperties)
        : base(basicProperties)
    {
        _system = null;
        Rank = InvalidRank;
        AddInterface(PaperColumnInterface);
    }

    /// <summary>Initializes a copy of another paper column.</summary>
    /// <param name="source">The column to copy.</param>
    protected PaperColumn(PaperColumn source)
        : base(source)
    {
        // The clone is not yet in any system; the rank carries over because a
        // prebroken piece belongs at the same horizontal position as its original.
        _system = null;
        Rank = source.Rank;
    }

    /// <summary>Gets the C++ class name this grob corresponds to.</summary>
    public override string ClassName => "Paper_column";

    /// <summary>Gets or sets this column's position in the score, counting from zero.</summary>
    public int Rank { get; set; }

    /// <summary>Gets the column this one was broken off from.</summary>
    public new PaperColumn Original => (PaperColumn)base.Original;

    /// <summary>Returns an independent copy of this column.</summary>
    /// <returns>The clone.</returns>
    public override Grob Clone() => new PaperColumn(this);

    /// <summary>Gets or sets the system this column ended up on after line breaking.</summary>
    public SystemGrob System
    {
        get => _system;
        set => _system = value;
    }

    /// <summary>Gets this column. A column is its own column.</summary>
    /// <returns>This column.</returns>
    public override PaperColumn GetColumn() => this;

    /// <summary>
    /// Gets the system this column ended up on, which is <see langword="null"/> until
    /// line breaking assigns one.
    /// <para>
    /// This is where the recursion in the item/spanner chain STOPS: a column answers
    /// from its own field rather than from its parent, which is the system. Without
    /// that, asking a system for its system would ask its bounding column, which would
    /// ask the system again.
    /// </para>
    /// </summary>
    /// <returns>The system, or <see langword="null"/>.</returns>
    public override SystemGrob GetSystem() => _system;

    /// <summary>Returns the prebroken piece for one side.</summary>
    /// <param name="direction">The side to select.</param>
    /// <returns>The piece, or <see langword="null"/>.</returns>
    public new PaperColumn FindPrebrokenPiece(Direction direction)
        => (PaperColumn)base.FindPrebrokenPiece(direction);

    /// <summary>Orders columns by rank.</summary>
    /// <param name="a">The first column.</param>
    /// <param name="b">The second column.</param>
    /// <returns><see langword="true"/> when the first comes earlier.</returns>
    public static bool RankLess(PaperColumn a, PaperColumn b) => a.Rank < b.Rank;

    /// <summary>Returns the musical moment a column sits at.</summary>
    /// <param name="grob">The column.</param>
    /// <returns>The moment, or zero when unset.</returns>
    public static Moment WhenMoment(Grob grob)
        => grob.GetProperty(WhenSymbol) is Moment moment ? moment : new Moment(0);

    /// <summary>
    /// Determines whether a column carries music, as opposed to being a bar line or
    /// clef position.
    /// </summary>
    /// <param name="grob">The column.</param>
    /// <returns><see langword="true"/> when something starts here.</returns>
    public static bool IsMusical(Grob grob)
    {
        object shortest = grob.GetProperty(ShortestStarterDuration);
        Rational value = ToRational(shortest);
        return value.IsNonZero;
    }

    /// <summary>
    /// Determines whether a line may be broken at a column. Only columns with a
    /// <c>line-break-permission</c> are candidates.
    /// </summary>
    /// <param name="grob">The column.</param>
    /// <returns><see langword="true"/> when a break is allowed here.</returns>
    public static bool IsBreakable(Grob grob) => grob.GetProperty(LineBreakPermission) is Symbol;

    /// <summary>
    /// Determines whether a column does anything.
    /// <para>
    /// Unused columns are filtered out before spacing is solved, because an empty
    /// column contributes a spring and a rod to the problem while constraining
    /// nothing.
    /// </para>
    /// </summary>
    /// <param name="grob">The column.</param>
    /// <returns><see langword="true"/> when the column matters.</returns>
    public static bool IsUsed(Grob grob)
    {
        if (PointerGroupInterface.ExtractGrobSet(grob, ElementsSymbol).Count > 0)
        {
            return true;
        }

        if (PointerGroupInterface.ExtractGrobSet(grob, BoundedByMe).Count > 0)
        {
            return true;
        }

        if (IsBreakable(grob))
        {
            return true;
        }

        if (SchemeUtilities.ToBool(grob.GetProperty(UsedSymbol)))
        {
            return true;
        }

        return grob.GetProperty(LabelsSymbol) is Pair;
    }

    /// <summary>
    /// Determines whether a musical column is one a ligature engraver left behind: it
    /// thinks it holds note heads, but every one of them was claimed by another column.
    /// <para>
    /// Such a column reports a width it does not really occupy, which is why packed
    /// spacing has to leave it out of the packing.
    /// </para>
    /// </summary>
    /// <param name="grob">The column.</param>
    /// <returns><see langword="true"/> when the column is extraneous.</returns>
    public static bool IsExtraneousColumnFromLigature(Grob grob)
    {
        if (!IsMusical(grob))
        {
            return false;
        }

        // If all the note-heads that I think are my children actually belong
        // to another column, then I am extraneous.
        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(grob, ElementsSymbol);
        bool hasNotehead = false;
        for (int i = 0; i < elts.Count; i++)
        {
            if (elts[i].HasInterface(RhythmicHeadInterface))
            {
                hasNotehead = true;
                if (ReferenceEquals((elts[i] as Item)?.GetColumn(), grob))
                {
                    return false;
                }
            }
        }

        return hasNotehead;
    }

    /// <summary>
    /// Returns the horizontal extent of a break-align group in a non-musical column,
    /// measured against the system — where a clef, a key signature or a bar line
    /// actually sits within the column that holds it.
    /// <para>
    /// DIVERGENCE, recorded in PORT-COVERAGE: the search for a NAMED break-align group
    /// needs <c>Break_alignment_interface</c>, which is not ported yet. The
    /// <c>break-alignment</c> case and every no-alignment case are exact; a named
    /// symbol falls back to the column's own coordinate, which is what upstream also
    /// answers when no group matches.
    /// </para>
    /// </summary>
    /// <param name="grob">The column.</param>
    /// <param name="alignSymbols">A break-align symbol, or a list of them.</param>
    /// <returns>The extent, relative to the system.</returns>
    public static Interval BreakAlignWidth(Grob grob, object alignSymbols)
    {
        Grob system = grob.GetParent(Axis.X);
        double coordinate = grob.RelativeCoordinate(system, Axis.X);

        if (alignSymbols is Symbol single)
        {
            alignSymbols = Pair.List(single);
        }

        if (IsMusical(grob))
        {
            Warn.ProgrammingError("tried to get break-align-width of a musical column");
            return new Interval(coordinate, coordinate);
        }

        if (!(grob.GetObject(BreakAlignmentSymbol) is Item breakAlignment))
        {
            return new Interval(coordinate, coordinate);
        }

        Grob align = null;
        object cursor = alignSymbols;
        while (cursor is Pair pair)
        {
            if (ReferenceEquals(pair.Car, BreakAlignmentSymbol))
            {
                align = breakAlignment;

                // No need for an X-extent check: if the whole BreakAlignment has
                // empty extent, none of its BreakAlignGroups will have non-empty
                // extent.
                break;
            }

            cursor = pair.Cdr;
        }

        if (align == null)
        {
            return new Interval(coordinate, coordinate);
        }

        Interval extent = align.Extent(grob, Axis.X);
        if (extent.IsEmpty)
        {
            return new Interval(coordinate, coordinate);
        }

        return extent + coordinate;
    }

    /// <summary>
    /// Returns how close two columns may come: the distance at which their facing
    /// horizontal skylines touch, never less than zero.
    /// </summary>
    /// <param name="left">The left column.</param>
    /// <param name="right">The right column.</param>
    /// <returns>The minimum distance.</returns>
    public static double MinimumDistance(Grob left, Grob right)
    {
        // The LEFT column presents its RIGHT-facing skyline and vice versa, which is
        // why each side reads the pair member opposite to its own direction.
        Skyline leftSky = SkylineFacing(left, Direction.Positive);
        Skyline rightSky = SkylineFacing(right, Direction.Negative);

        return Math.Max(0.0, leftSky.Distance(rightSky));
    }

    private static Skyline SkylineFacing(Grob column, Direction facing)
    {
        SkylinePair pair = SkylinePair.FromScheme(column?.GetProperty(HorizontalSkylines));
        return pair != null ? pair[facing] : new Skyline(facing);
    }

    /// <summary>
    /// Reads a duration-shaped property as a rational, with upstream's zero fallback for
    /// anything that is not a number.
    /// <para>
    /// The INEXACT cases matter and are easy to miss: a duration of
    /// <c>Rational::infinity</c> reaches Scheme as <c>+inf.0</c>, a real — which is what
    /// the spacing engraver writes for a column where no note starts — and reading that
    /// back as zero would quietly reclassify a musical column as non-musical.
    /// </para>
    /// </summary>
    private static Rational ToRational(object value)
    {
        switch (value)
        {
            case Rational rational:
                return rational;
            case Moment moment:
                return moment.MainPart;
            default:
                return Bootstrap.SchemeConvert.IsNumber(value)
                    ? Bootstrap.SchemeConvert.ToRational(value, "duration")
                    : Rational.Zero;
        }
    }
}
