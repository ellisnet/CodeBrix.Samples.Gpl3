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

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/constrained-breaking.cc, lily/include/constrained-breaking.hh (Line_shape and Line_details);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - Line_shape and Line_details are STRUCTS upstream and CLASSES here, deliberately.
//     Line_details is thirty-odd fields and is copied into and out of a dynamic-programming
//     table by value in C++; the port stores references in that table instead and never
//     mutates a cell after storing it, which is the same behaviour without thirty-field
//     copies at every one of the millions of cell reads calc_subproblem performs.
//   - Splitting them out of ConstrainedBreaking.cs follows the port's own convention of one
//     type per file for types with a real surface; the `was previously' line names both.

/// <summary>
/// The crude silhouette of a line: what it occupies at its BEGINNING and what it occupies
/// across the REST of itself.
/// <para>
/// Upstream calls this the "begin/rest-of-line hack" and says plainly that it is a crude
/// approximation of a <c>Skyline</c> — but a better one than a rectangle. It exists
/// because the start of a line carries a clef, a key signature and often an instrument
/// name, so a line is systematically taller at its left edge, and stacking lines as plain
/// boxes would waste that difference on every page.
/// </para>
/// </summary>
public sealed class LineShape
{
    /// <summary>Initializes an empty shape.</summary>
    public LineShape()
    {
        Begin = Interval.Empty;
        Rest = Interval.Empty;
    }

    /// <summary>Initializes a shape from its two parts.</summary>
    /// <param name="begin">What the start of the line occupies.</param>
    /// <param name="rest">What the remainder occupies.</param>
    public LineShape(Interval begin, Interval rest)
    {
        Begin = begin;
        Rest = rest;
    }

    /// <summary>Gets or sets what the start of the line occupies.</summary>
    public Interval Begin { get; set; }

    /// <summary>Gets or sets what the remainder of the line occupies.</summary>
    public Interval Rest { get; set; }

    /// <summary>
    /// Stacks another shape on top of this one, raising it just enough that neither part
    /// collides, plus padding.
    /// <para>
    /// The elevation is computed ONCE from whichever part is tighter and then applied to
    /// both, which is what keeps the mounted line rigid: raising its two halves
    /// independently would shear it.
    /// </para>
    /// </summary>
    /// <param name="mount">The shape to stack on top.</param>
    /// <param name="padding">The gap to leave.</param>
    /// <returns>The combined shape.</returns>
    public LineShape Piggyback(LineShape mount, double padding)
    {
        double elevation = Math.Max(
            Begin.Right - mount.Begin.Left,
            Rest.Right - mount.Rest.Left);
        Interval begin = new Interval(Begin.Left, elevation + mount.Begin.Right + padding);
        Interval rest = new Interval(Rest.Left, elevation + mount.Rest.Right + padding);
        return new LineShape(begin, rest);
    }
}

/// <summary>
/// Everything the line breaker and the page breaker need to know about ONE candidate
/// line: how badly it is stretched, how tall it is, what it costs to break there, and how
/// it may be spaced against its neighbours.
/// <para>
/// The horizontal half — <see cref="Force"/> — comes from the spacing solver. Everything
/// else is filled in by <c>Constrained_breaking::fill_line_details</c> from the paper
/// variables and the system's pure heights, which is why a
/// <see cref="LineDetails"/> can be built for a line that has never been laid out.
/// </para>
/// </summary>
public sealed class LineDetails
{
    private static readonly Symbol AllowSymbol = Symbol.Intern("allow");
    private static readonly Symbol FootnotesSymbol = Symbol.Intern("footnotes");
    private static readonly Symbol StencilSymbol = Symbol.Intern("stencil");
    private static readonly Symbol PageBreakPermissionSymbol
        = Symbol.Intern("page-break-permission");

    private static readonly Symbol PageTurnPermissionSymbol
        = Symbol.Intern("page-turn-permission");

    private static readonly Symbol PageBreakPenaltySymbol = Symbol.Intern("page-break-penalty");
    private static readonly Symbol PageTurnPenaltySymbol = Symbol.Intern("page-turn-penalty");
    private static readonly Symbol IsTitleSymbol = Symbol.Intern("is-title");
    private static readonly Symbol LastMarkupLineSymbol = Symbol.Intern("last-markup-line");
    private static readonly Symbol FirstMarkupLineSymbol = Symbol.Intern("first-markup-line");
    private static readonly Symbol TightSpacingSymbol = Symbol.Intern("tight-spacing");
    private static readonly Symbol BasicDistanceSymbol = Symbol.Intern("basic-distance");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol MinimumDistanceSymbol = Symbol.Intern("minimum-distance");

    /// <summary>Initializes a line with upstream's defaults.</summary>
    public LineDetails()
    {
        LastColumn = null;
        Force = double.PositiveInfinity;
        Padding = 0;
        TitlePadding = 0;
        BottomPadding = 0;
        MinDistance = 0;
        TitleMinDistance = 0;
        Space = 0;
        TitleSpace = 0;
        InverseHooke = 1;
        TightSpacing = false;
        BreakPermission = AllowSymbol;
        PagePermission = AllowSymbol;
        TurnPermission = AllowSymbol;
        BreakPenalty = 0;
        PagePenalty = 0;
        TurnPenalty = 0;
        IsTitle = false;
        CompressedLinesCount = 1;
        CompressedNontitleLinesCount = 1;
        LastMarkupLine = false;
        FirstMarkupLine = false;
        Tallness = 0;
        RefpointExtent = new Interval(0, 0);
        Shape = new LineShape();
        FootnoteHeights = new List<double>();
        InNoteHeights = new List<double>();
    }

    /// <summary>
    /// Initializes a line from a <c>paper-system</c> prob — a TITLE or other markup line
    /// rather than a system of music.
    /// <para>
    /// Such a line has no columns and no force to speak of: its shape is its stencil's
    /// vertical extent used for BOTH halves, because upstream pretends it goes all the way
    /// across, and its spacing comes from the markup spacing variables rather than the
    /// system ones.
    /// </para>
    /// </summary>
    /// <param name="pb">The paper-system prob.</param>
    /// <param name="paper">The output definition to read spacing specs from.</param>
    public LineDetails(Prob pb, OutputDef paper)
        : this()
    {
        object spec = paper.CVariable("markup-system-spacing");
        object titleSpec = paper.CVariable("markup-markup-spacing");
        double padding = 0;
        double titlePadding = 0;
        double minDistance = 0;
        double titleMinDistance = 0;
        double space = 0;
        double titleSpace = 0;
        PageLayoutSpacing.ReadSpacingSpec(spec, BasicDistanceSymbol, ref space);
        PageLayoutSpacing.ReadSpacingSpec(titleSpec, BasicDistanceSymbol, ref titleSpace);
        PageLayoutSpacing.ReadSpacingSpec(spec, PaddingSymbol, ref padding);
        PageLayoutSpacing.ReadSpacingSpec(titleSpec, PaddingSymbol, ref titlePadding);
        PageLayoutSpacing.ReadSpacingSpec(spec, MinimumDistanceSymbol, ref minDistance);
        PageLayoutSpacing.ReadSpacingSpec(titleSpec, MinimumDistanceSymbol, ref titleMinDistance);

        Padding = padding;
        TitlePadding = titlePadding;
        MinDistance = minDistance;
        TitleMinDistance = titleMinDistance;
        Space = space;
        TitleSpace = titleSpace;

        object footnotes = pb.GetProperty(FootnotesSymbol);

        if (footnotes is Pair)
        {
            for (object s = footnotes; s is Pair pair; s = pair.Cdr)
            {
                // Stencil is a STRUCT in this port, so upstream's null-pointer test
                // becomes a type test. Same two outcomes, and the diagnostic is kept
                // verbatim so a log line still matches upstream's.
                if (!(Caddar(pair) is Stencil sten))
                {
                    Warn.ProgrammingError("expecting stencil, got empty pointer");
                    continue;
                }

                FootnoteHeights.Add(sten.Extent(Axis.Y).Length);
            }
        }

        LastColumn = null;
        Force = 0;
        // Upstream dereferences this without a null check; a Stencil is a struct here, so
        // a property holding something else falls to the same zero interval an empty
        // stencil gives rather than to a crash.
        Interval stencilExtent
            = pb.GetProperty(StencilSymbol) is Stencil st && !st.IsEmptyOn(Axis.Y)
                ? st.Extent(Axis.Y)
                : new Interval(0, 0);

        // pretend it goes all the way across
        Shape = new LineShape(stencilExtent, stencilExtent);
        Tallness = 0;
        BottomPadding = 0;
        InverseHooke = 1.0;
        BreakPermission = AllowSymbol;
        PagePermission = pb.GetProperty(PageBreakPermissionSymbol);
        TurnPermission = pb.GetProperty(PageTurnPermissionSymbol);
        BreakPenalty = 0;
        PagePenalty = ToDoubleOrZero(pb.GetProperty(PageBreakPenaltySymbol));
        TurnPenalty = ToDoubleOrZero(pb.GetProperty(PageTurnPenaltySymbol));
        IsTitle = SchemeUtilities.ToBool(pb.GetProperty(IsTitleSymbol));
        CompressedLinesCount = 1;
        CompressedNontitleLinesCount = IsTitle ? 0 : 1;
        LastMarkupLine = SchemeUtilities.ToBool(pb.GetProperty(LastMarkupLineSymbol));
        FirstMarkupLine = SchemeUtilities.ToBool(pb.GetProperty(FirstMarkupLineSymbol));
        TightSpacing = SchemeUtilities.ToBool(pb.GetProperty(TightSpacingSymbol));
        RefpointExtent = new Interval(0, 0);
    }

    /// <summary>Gets or sets the last column on this line.</summary>
    public PaperColumn LastColumn { get; set; }

    /// <summary>
    /// Gets or sets how badly the line is stretched: negative is compressed, positive
    /// stretched, and infinite means the line cannot be spaced at all.
    /// </summary>
    public double Force { get; set; }

    /// <summary>Gets or sets the line's two-part silhouette.</summary>
    public LineShape Shape { get; set; }

    /// <summary>Gets the height of each footnote at the bottom of the page.</summary>
    public List<double> FootnoteHeights { get; }

    /// <summary>Gets the height of each in-note under this system.</summary>
    public List<double> InNoteHeights { get; }

    /// <summary>
    /// Gets or sets the refpoints of the first and last SPACEABLE staff on this line.
    /// Minimum distance is measured from one line's bottom refpoint to the next line's
    /// top refpoint, not from their outer edges.
    /// </summary>
    public Interval RefpointExtent { get; set; }

    /// <summary>Gets or sets the Y extent adjusted for begin/rest-of-line.</summary>
    public double Tallness { get; set; }

    /// <summary>Gets or sets the compulsory space after this system when it is not last on a page.</summary>
    public double Padding { get; set; }

    /// <summary>Gets or sets the compulsory space after this system when a title follows.</summary>
    public double TitlePadding { get; set; }

    /// <summary>Gets or sets the minimum distance to the next line.</summary>
    public double MinDistance { get; set; }

    /// <summary>Gets or sets the minimum distance to a following title.</summary>
    public double TitleMinDistance { get; set; }

    /// <summary>Gets or sets the padding below this line.</summary>
    public double BottomPadding { get; set; }

    /// <summary>Gets or sets the spring length to the next line.</summary>
    public double Space { get; set; }

    /// <summary>Gets or sets the spring length to a following title.</summary>
    public double TitleSpace { get; set; }

    /// <summary>Gets or sets the inverse spring constant.</summary>
    public double InverseHooke { get; set; }

    /// <summary>Gets or sets whether a line break is allowed, forced or forbidden here.</summary>
    public object BreakPermission { get; set; }

    /// <summary>Gets or sets whether a page break is allowed, forced or forbidden here.</summary>
    public object PagePermission { get; set; }

    /// <summary>Gets or sets whether a page turn is allowed, forced or forbidden here.</summary>
    public object TurnPermission { get; set; }

    /// <summary>Gets or sets the penalty for breaking the line here.</summary>
    public double BreakPenalty { get; set; }

    /// <summary>Gets or sets the penalty for breaking the page here.</summary>
    public double PagePenalty { get; set; }

    /// <summary>Gets or sets the penalty for turning the page here.</summary>
    public double TurnPenalty { get; set; }

    /// <summary>Gets or sets whether this line is a title rather than music.</summary>
    public bool IsTitle { get; set; }

    /// <summary>
    /// Gets or sets how many lines this one stands for. The page breaker deals with a
    /// forbidden page break by COMPRESSING two lines into one, and these three fields are
    /// how it keeps track of that.
    /// </summary>
    public int CompressedLinesCount { get; set; }

    /// <summary>Gets or sets how many of the compressed lines are not titles.</summary>
    public int CompressedNontitleLinesCount { get; set; }

    /// <summary>Gets or sets whether this is the last line of a markup block.</summary>
    public bool LastMarkupLine { get; set; }

    /// <summary>Gets or sets whether this is the first line of a markup block.</summary>
    public bool FirstMarkupLine { get; set; }

    /// <summary>Gets or sets whether this line is spaced tightly.</summary>
    public bool TightSpacing { get; set; }

    /// <summary>
    /// A private copy of this line — the port's stand-in for upstream's copy on
    /// assignment, since <c>Line_details</c> is a struct there and a class here.
    /// <para>
    /// PAGE BREAKING CANNOT WORK WITHOUT THIS, and the reason is not obvious.
    /// <c>ConstrainedBreaking.GetLineDetails</c> hands back the very objects stored in its
    /// dynamic-programming table, so two page-breaking configurations asking for the same
    /// stretch of music get the SAME instances. <c>Page_breaking</c> then compresses the
    /// list and writes each line's tallness into it — which, without a copy, would write
    /// through into the line breaker's own table and leak one configuration's page layout
    /// into the next. Upstream never meets this because every one of those steps copies.
    /// </para>
    /// <para>
    /// The two lists are copied too, and not shared: <c>compress_lines</c> INSERTS the
    /// upper line's footnotes into the lower line's own list, so a shared list would grow
    /// every time a configuration was compressed.
    /// </para>
    /// </summary>
    /// <returns>The copy.</returns>
    public LineDetails Copy()
    {
        LineDetails copy = new LineDetails
        {
            LastColumn = LastColumn,
            Force = Force,
            Shape = new LineShape(Shape.Begin, Shape.Rest),
            RefpointExtent = RefpointExtent,
            Tallness = Tallness,
            Padding = Padding,
            TitlePadding = TitlePadding,
            MinDistance = MinDistance,
            TitleMinDistance = TitleMinDistance,
            BottomPadding = BottomPadding,
            Space = Space,
            TitleSpace = TitleSpace,
            InverseHooke = InverseHooke,
            BreakPermission = BreakPermission,
            PagePermission = PagePermission,
            TurnPermission = TurnPermission,
            BreakPenalty = BreakPenalty,
            PagePenalty = PagePenalty,
            TurnPenalty = TurnPenalty,
            IsTitle = IsTitle,
            CompressedLinesCount = CompressedLinesCount,
            CompressedNontitleLinesCount = CompressedNontitleLinesCount,
            LastMarkupLine = LastMarkupLine,
            FirstMarkupLine = FirstMarkupLine,
            TightSpacing = TightSpacing,
        };

        copy.FootnoteHeights.AddRange(FootnoteHeights);
        copy.InNoteHeights.AddRange(InNoteHeights);
        return copy;
    }

    /// <summary>Gets the full height of the line: both halves of the shape united.</summary>
    /// <returns>The height.</returns>
    public double FullHeight()
    {
        Interval ret = Interval.Empty;
        ret.Unite(Shape.Begin);
        ret.Unite(Shape.Rest);
        return ret.Length;
    }

    /// <summary>Gets the line's tallness.</summary>
    /// <returns>The tallness.</returns>
    public double GetTallness() => Tallness;

    /// <summary>
    /// The stretchable space between the BOTTOM of this line's extent and the TOP of the
    /// next line's.
    /// <para>
    /// <see cref="Space"/> measures the spring between the two lines' REFPOINTS, so the
    /// distance already taken up between the refpoints and the edges has to come off it —
    /// and the result is clamped at zero, because a spring cannot be shorter than nothing.
    /// </para>
    /// </summary>
    /// <param name="nextLine">The line below this one.</param>
    /// <returns>The spring length.</returns>
    public double SpringLength(LineDetails nextLine)
    {
        double refpointDist = Tallness + RefpointExtent.Left - nextLine.RefpointExtent.Right;
        double space = nextLine.IsTitle ? TitleSpace : Space;
        return Math.Max(0.0, space - refpointDist);
    }

    private static object Caddar(Pair pair)
        => pair.Car is Pair inner && inner.Cdr is Pair second && second.Cdr is Pair third
            ? third.Car
            : null;

    private static double ToDoubleOrZero(object value)
        => SchemeConvert.IsNumber(value) ? SchemeConvert.ToDouble(value, "penalty") : 0.0;
}
