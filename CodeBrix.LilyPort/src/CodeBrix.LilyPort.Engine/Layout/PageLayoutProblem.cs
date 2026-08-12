/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2009--2026 Joe Neeman <joeneeman@gmail.com>

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
using System.Runtime.InteropServices;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/page-layout-problem.cc;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - the SPACING-SPEC readers of this file -- is_spaceable, read_spacing_spec,
//     get_spacing_spec, get_fixed_spacing and add_stretchability -- landed with EPG7 in
//     Layout/PageLayoutSpacing.cs, because Align_interface reads every adjacent pair of
//     staves through them. They are NOT duplicated here; this file calls them.
//   - Element is a struct-with-two-shapes upstream ("a union in spirit"); it is a sealed
//     class here with the same invariant, that staves is empty or prob is null.
// Modified by Jeremy Ellis on 2026-08-11 as part of the CodeBrix port:
//   - every upstream `springs_.back ().foo ()' goes through LastSpring(), which answers a
//     REFERENCE. Spring is a struct here, so `_springs[_springs.Count - 1].Foo ()' compiles
//     cleanly, mutates a temporary copy and discards it -- see that method's remarks.
//   - alter_spring_from_spacing_spec takes its Spring by `ref' (upstream: Spring*). The
//     STAFF-LINES session found it taking the struct BY VALUE, which is the same trap
//     through a parameter: no spacing spec ever reached any page spring, and every
//     system sat at the skyline minimum instead of basic-distance.

/// <summary>
/// The vertical spacing problem for ONE page: a rod-and-spring system over the systems and
/// titles that page carries, solved to give each of them a y offset.
/// <para>
/// The sign conventions are genuinely confusing and upstream says so. The CONFIGURATION
/// this produces has zero at the top of the page and grows DOWNWARD, as does the solution
/// vector; but within a staff, positive is UP. Every conversion between the two is spelled
/// out at the point it happens.
/// </para>
/// </summary>
public sealed class PageLayoutProblem
{
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol BasicDistanceSymbol = Symbol.Intern("basic-distance");
    private static readonly Symbol MinimumDistanceSymbol = Symbol.Intern("minimum-distance");
    private static readonly Symbol StretchabilitySymbol = Symbol.Intern("stretchability");
    private static readonly Symbol AlignmentDistancesSymbol = Symbol.Intern("alignment-distances");
    private static readonly Symbol BottomPaddingSymbol = Symbol.Intern("bottom-padding");
    private static readonly Symbol StaffAffinitySymbol = Symbol.Intern("staff-affinity");
    private static readonly Symbol VerticalSkylinesSymbol = Symbol.Intern("vertical-skylines");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol FootnotesAfterLineBreakingSymbol
        = Symbol.Intern("footnotes-after-line-breaking");

    private static readonly string[] LilyModule = { "lily" };

    private readonly List<Spring> _springs = new List<Spring>();
    private readonly List<Element> _elements = new List<Element>();
    private List<double> _solution = new List<double>();
    private Skyline _bottomSkyline;
    private double _bottomLooseBaseline;
    private double _pageHeight;
    private double _headerHeight;
    private double _footerHeight;
    private double _headerPadding;
    private double _footerPadding;
    private double _inNotePadding;
    private double _inNoteSystemPadding;
    private Direction _inNoteDirection = Direction.Positive;

    /// <summary>
    /// States the page's vertical spacing problem: one spring per gap, from the top of the
    /// printable area, through every system and title, to the bottom.
    /// </summary>
    /// <param name="book">The book, for the paper and the footnote settings.</param>
    /// <param name="page">The page prob.</param>
    /// <param name="systems">The systems on this page, as a Scheme list.</param>
    public PageLayoutProblem(PaperBook book, object page, object systems)
    {
        _bottomSkyline = new Skyline(Direction.Negative);
        _bottomLooseBaseline = 0;
        _headerHeight = 0;
        _footerHeight = 0;
        _headerPadding = 0;
        _footerPadding = 0;
        _pageHeight = 100;
        Force = 0;

        if (page is Prob pageProb)
        {
            Stencil footStencil = pageProb.GetProperty("foot-stencil") is Stencil foot
                ? foot
                : Stencil.Empty;

            if (book != null && book.Paper != null)
            {
                object footnotes = GetFootnotesFromLines(systems);
                footStencil = AddFootnotesToFooter(footnotes, footStencil, book);
            }
            else
            {
                Warn.Warning("A page layout problem has been initiated that cannot "
                    + "accommodate footnotes.");
            }

            _headerHeight = pageProb.GetProperty("head-stencil") is Stencil head
                ? head.Extent(Axis.Y).Length
                : 0;
            _footerHeight = footStencil.Extent(Axis.Y).Length;
            _pageHeight = SchemeConvert.ToDouble(pageProb.GetProperty("paper-height"), 100);
        }

        // The bottom skyline starts out representing the TOP of the page, and is made
        // solid so the first system is forced below the top of the printable area.
        _bottomSkyline.SetMinimumHeight(-_headerHeight);

        object systemSystemSpacing = Nil.Instance;
        object scoreSystemSpacing = Nil.Instance;
        object markupSystemSpacing = Nil.Instance;
        object scoreMarkupSpacing = Nil.Instance;
        object markupMarkupSpacing = Nil.Instance;

        // top-system-spacing is the spring from the top of the printable area to the first
        // STAFF, not to the top of the first system -- which is what lets a user control
        // where the music starts rather than where its skyline starts.
        object topSystemSpacing = Nil.Instance;
        object lastBottomSpacing = Nil.Instance;

        if (book != null && book.Paper != null)
        {
            OutputDef paper = book.Paper;
            systemSystemSpacing = paper.CVariable("system-system-spacing");
            scoreSystemSpacing = paper.CVariable("score-system-spacing");
            markupSystemSpacing = paper.CVariable("markup-system-spacing");
            scoreMarkupSpacing = paper.CVariable("score-markup-spacing");
            markupMarkupSpacing = paper.CVariable("markup-markup-spacing");
            lastBottomSpacing = paper.CVariable("last-bottom-spacing");
            topSystemSpacing = paper.CVariable("top-system-spacing");
            if (systems is Pair firstPair && firstPair.Car is Prob)
            {
                topSystemSpacing = paper.CVariable("top-markup-spacing");
            }

            // The page height here does NOT reserve space for headers and footers, because
            // the top-system-spacing spring is anchored at the TOP of the header.
            _pageHeight -= SchemeConvert.ToDouble(paper.CVariable("top-margin"), 0)
                + SchemeConvert.ToDouble(paper.CVariable("bottom-margin"), 0);

            PageLayoutSpacing.ReadSpacingSpec(topSystemSpacing, PaddingSymbol, ref _headerPadding);
            PageLayoutSpacing.ReadSpacingSpec(lastBottomSpacing, PaddingSymbol, ref _footerPadding);
            _inNotePadding = SchemeConvert.ToDouble(paper.CVariable("in-note-padding"), 0.0);
            _inNoteSystemPadding
                = SchemeConvert.ToDouble(paper.CVariable("in-note-system-padding"), 0.5);
            _inNoteDirection = ToDirection(paper.CVariable("in-note-direction"), Direction.Positive);
        }

        bool lastSystemWasTitle = false;
        List<object> systemList = Pair.ToList(systems);

        for (int i = 0; i < systemList.Count; i++)
        {
            bool first = i == 0;
            object entry = systemList[i];

            if (entry is SystemGrob sys)
            {
                object spec = systemSystemSpacing;
                if (first)
                {
                    spec = topSystemSpacing;
                }
                else if (lastSystemWasTitle)
                {
                    spec = markupSystemSpacing;
                }
                else if (sys.GetBound(Direction.Negative) != null
                    && sys.GetBound(Direction.Negative).Rank == 0)
                {
                    spec = scoreSystemSpacing;
                }

                Spring spring = new Spring(0, 0);
                double padding = 0.0;
                double indent = ConstrainedBreaking.LineDimensionInterval(
                    sys.PaperScore.Layout, sys.Rank)[Direction.Negative];
                AlterSpringFromSpacingSpec(spec, ref spring);
                PageLayoutSpacing.ReadSpacingSpec(spec, PaddingSymbol, ref padding);

                AppendSystem(sys, spring, indent, padding);
                lastSystemWasTitle = false;
            }
            else if (entry is Prob p)
            {
                object spec = first
                    ? topSystemSpacing
                    : lastSystemWasTitle ? markupMarkupSpacing : scoreMarkupSpacing;
                Spring spring = new Spring(0, 0);
                double padding = 0.0;
                AlterSpringFromSpacingSpec(spec, ref spring);
                PageLayoutSpacing.ReadSpacingSpec(spec, PaddingSymbol, ref padding);

                AppendProb(p, spring, padding);
                lastSystemWasTitle = true;
            }
            else
            {
                Warn.ProgrammingError("got a system that was neither a Grob nor a Prob");
            }
        }

        Spring lastSpring = new Spring(0, 0);
        double lastPadding = 0;
        AlterSpringFromSpacingSpec(lastBottomSpacing, ref lastSpring);
        PageLayoutSpacing.ReadSpacingSpec(lastBottomSpacing, PaddingSymbol, ref lastPadding);
        lastSpring.EnsureMinDistance(lastPadding - _bottomSkyline.MaxHeight() + _footerHeight);
        _springs.Add(lastSpring);

        if (_elements.Count != 0)
        {
            double bottomPadding = 0;
            Element last = _elements[_elements.Count - 1];

            if (last.Prob != null)
            {
                bottomPadding = SchemeConvert.ToDouble(last.Prob.GetProperty("bottom-padding"), 0);
            }
            else if (last.Staves.Count != 0)
            {
                object details = GetDetails(last);
                bottomPadding = SchemeConvert.ToDouble(
                    SchemeUtilities.LyAssocGet(BottomPaddingSymbol, details, false), 0.0);
            }

            _pageHeight -= bottomPadding;
        }
    }

    /// <summary>Gets the force the page was solved at.</summary>
    public double Force { get; private set; }

    /// <summary>Sets the header height, which the top spring is anchored against.</summary>
    /// <param name="height">The height.</param>
    public void SetHeaderHeight(double height) => _headerHeight = height;

    /// <summary>Sets the footer height, which the bottom spring is anchored against.</summary>
    /// <param name="height">The height.</param>
    public void SetFooterHeight(double height) => _footerHeight = height;

    /// <summary>Solves the page and returns the system offsets.</summary>
    /// <param name="ragged">Whether the page's bottom is ragged.</param>
    /// <returns>The offsets, as a Scheme list.</returns>
    public object Solution(bool ragged)
    {
        SolveRodSpringProblem(ragged, double.NegativeInfinity);
        return FindSystemOffsets();
    }

    /// <summary>
    /// Solves the page at a GIVEN force, and falls back to filling the page if that force
    /// does not fit.
    /// <para>This is what keeps a ragged-last page from looking much less compressed than
    /// the page before it.</para>
    /// </summary>
    /// <param name="force">The force to try.</param>
    /// <returns>The offsets, as a Scheme list.</returns>
    public object FixedForceSolution(double force)
    {
        SolveRodSpringProblem(true, force);
        return FindSystemOffsets();
    }

    /// <summary>Counts the footnotes belonging to a page's lines.</summary>
    /// <param name="lines">The lines, as a Scheme list.</param>
    /// <returns>The footnote count.</returns>
    public static int GetFootnoteCount(object lines) => GetFootnoteGrobs(lines).Count;

    /// <summary>
    /// Collects the footnote grobs of a page's lines.
    /// <para>A markup line contributes a NULL entry per footnote rather than a grob, which
    /// is why the list is of grobs and yet may hold nulls — the count is what most callers
    /// want, and a markup's footnote has no grob to offer.</para>
    /// </summary>
    /// <param name="lines">The lines, as a Scheme list.</param>
    /// <returns>The footnote grobs.</returns>
    public static List<Grob> GetFootnoteGrobs(object lines)
    {
        List<Grob> footnotes = new List<Grob>();
        foreach (object entry in Pair.ToList(lines))
        {
            if (entry is Grob g)
            {
                if (!(g is SystemGrob sys))
                {
                    Warn.ProgrammingError("got a grob for footnotes that wasn't a System");
                    continue;
                }

                GrobArray footnoteGrobs
                    = PointerGroupInterface.GetGrobArray(sys, FootnotesAfterLineBreakingSymbol);
                for (int i = 0; i < footnoteGrobs.Count; i++)
                {
                    footnotes.Add(footnoteGrobs[i]);
                }
            }
            else if (entry is Prob p)
            {
                object stencils = p.GetProperty("footnotes");
                if (stencils is Nil)
                {
                    continue;
                }

                foreach (object unused in Pair.ToList(stencils))
                {
                    footnotes.Add(null);
                }
            }
        }

        return footnotes;
    }

    /// <summary>
    /// Collects the footnote stencils already attached to a page's lines.
    /// <para>This REFUSES to work before <see cref="AddFootnotesToLines"/> has run, and
    /// says so: an empty answer at that point would silently drop every footnote on the
    /// page.</para>
    /// </summary>
    /// <param name="lines">The lines, as a Scheme list.</param>
    /// <returns>The stencils, as a Scheme list.</returns>
    public static object GetFootnotesFromLines(object lines)
    {
        if (!(lines is Pair firstPair))
        {
            return Nil.Instance;
        }

        bool footnotesAdded;
        if (firstPair.Car is Grob g)
        {
            footnotesAdded = !(g.GetProperty("footnote-stencil") is Nil);
        }
        else if (firstPair.Car is Prob p)
        {
            footnotesAdded = !(p.GetProperty("footnote-stencil") is Nil);
        }
        else
        {
            Warn.ProgrammingError("Systems on a page must be a prob or grob.");
            return Nil.Instance;
        }

        if (!footnotesAdded)
        {
            Warn.ProgrammingError("Footnotes must be added to lines before they are retrieved.");
            return Nil.Instance;
        }

        List<object> outList = new List<object>();
        foreach (object entry in Pair.ToList(lines))
        {
            if (entry is Grob grob)
            {
                outList.Add(grob.GetProperty("footnote-stencil"));
            }
            else if (entry is Prob prob)
            {
                outList.Add(prob.GetProperty("footnote-stencil"));
            }
            else
            {
                Warn.ProgrammingError("Systems on a page must be a prob or grob.");
            }
        }

        return Pair.ListFrom(outList);
    }

    /// <summary>
    /// Builds each system's footnote stencil, numbering the footnotes across the page.
    /// <para>
    /// The numbering is computed FIRST, for the whole page, and the number stencils are
    /// then translated to a common right edge — which is why the maximum width is measured
    /// before any of them is placed. Numbering per system instead would leave the numbers
    /// ragged against each other.
    /// </para>
    /// </summary>
    /// <param name="lines">The lines, as a Scheme list.</param>
    /// <param name="counter">The number the first footnote on this page takes.</param>
    /// <param name="book">The book, for the paper and the numbering function.</param>
    public static void AddFootnotesToLines(object lines, int counter, PaperBook book)
    {
        OutputDef paper = book.Paper;

        if (paper == null)
        {
            Warn.ProgrammingError("Cannot get footnotes because there is no valid paper block.");
            return;
        }

        object numberFootnoteTable = book.TopPaper().CVariable("number-footnote-table");
        if (!(numberFootnoteTable is Pair))
        {
            numberFootnoteTable = Nil.Instance;
        }

        object numberingFunction = paper.CVariable("footnote-numbering-function");
        object props = SchemeUtilities.CallCallback(
            LilyPondScheme.PublicRef(LilyModule, "layout-extract-page-properties"), paper);
        double padding = SchemeConvert.ToDouble(paper.CVariable("footnote-padding"), 0.0);
        double inNotePadding = SchemeConvert.ToDouble(paper.CVariable("in-note-padding"), 0.0);
        double numberRaise = SchemeConvert.ToDouble(paper.CVariable("footnote-number-raise"), 0.0);

        List<Grob> fnGrobs = GetFootnoteGrobs(lines);
        int fnCount = fnGrobs.Count;

        List<object> numbers = new List<object>();
        List<object> inTextNumbers = new List<object>();

        double maxLength = double.NegativeInfinity;

        for (int i = 0; i < fnCount; i++)
        {
            if (fnGrobs[i] != null)
            {
                object assertionFunction = fnGrobs[i].GetProperty("numbering-assertion-function");
                if (SchemeUtilities.IsProcedure(assertionFunction))
                {
                    SchemeUtilities.CallCallback(assertionFunction, SchemeConvert.FromInt(counter));
                }
            }

            object markup = SchemeUtilities.CallCallback(
                numberingFunction, SchemeConvert.FromInt(counter));
            object stencil = TextInterface.InterpretMarkup(paper, props, markup);
            if (!(stencil is Stencil st))
            {
                Warn.ProgrammingError("Your numbering function needs to return a stencil.");
                markup = Nil.Instance;
                st = new Stencil(new Box(new Interval(0, 0), new Interval(0, 0)), Nil.Instance);
                stencil = st;
            }

            inTextNumbers.Add(markup);
            numbers.Add(stencil);

            if (!st.Extent(Axis.X).IsEmpty)
            {
                maxLength = Math.Max(maxLength, st.Extent(Axis.X)[Direction.Positive]);
            }

            counter++;
        }

        // Translate each number stencil so they all reach the same right edge.
        for (int i = 0; i < numbers.Count; i++)
        {
            if (numbers[i] is Stencil orig && !orig.Extent(Axis.X).IsEmpty)
            {
                // Stencil is a STRUCT here where upstream's is a value copied by
                // assignment; `trans' is that copy, and TranslateAxis mutates it in place.
                Stencil trans = orig;
                trans.TranslateAxis(maxLength - orig.Extent(Axis.X)[Direction.Positive], Axis.X);
                numbers[i] = trans;
            }
        }

        int numberIndex = 0;

        foreach (object entry in Pair.ToList(lines))
        {
            if (entry is Grob g)
            {
                if (!(g is SystemGrob sys))
                {
                    Warn.ProgrammingError("got a grob for footnotes that wasn't a System");
                    continue;
                }

                Stencil mol = Stencil.Empty;
                Stencil inNoteMol = Stencil.Empty;
                GrobArray footnoteGrobs
                    = PointerGroupInterface.GetGrobArray(sys, FootnotesAfterLineBreakingSymbol);
                for (int i = 0; i < footnoteGrobs.Count; i++)
                {
                    Grob footnote = footnoteGrobs[i];
                    object footnoteMarkup = footnote.GetProperty("footnote-text");
                    if (footnote is Spanner origSpanner && origSpanner.IsBroken)
                    {
                        footnoteMarkup
                            = origSpanner.BrokenIntos[0].GetProperty("footnote-text");
                    }

                    object lineProps = SchemeUtilities.CallCallback(
                        LilyPondScheme.PublicRef(LilyModule, "layout-extract-page-properties"),
                        paper);

                    Stencil footnoteStencil
                        = TextInterface.InterpretMarkup(paper, lineProps, footnoteMarkup)
                            is Stencil interpreted
                            ? interpreted
                            : Stencil.Empty;

                    bool doNumbering = SchemeUtilities.ToBool(
                        footnote.GetProperty("automatically-numbered"));
                    if (footnote is Spanner broken && broken.IsBroken)
                    {
                        for (int j = 0; j < broken.BrokenIntos.Count; j++)
                        {
                            doNumbering = doNumbering
                                || SchemeUtilities.ToBool(
                                    broken.BrokenIntos[j].GetProperty("automatically-numbered"));
                        }
                    }

                    if (doNumbering && numberIndex < numbers.Count)
                    {
                        object annotationScm = inTextNumbers[numberIndex];
                        footnote.SetProperty("text", annotationScm);
                        if (footnote is Spanner textSpanner)
                        {
                            textSpanner.SetProperty("text", annotationScm);
                            if (textSpanner.IsBroken)
                            {
                                for (int j = 0; j < textSpanner.BrokenIntos.Count; j++)
                                {
                                    textSpanner.BrokenIntos[j].SetProperty("text", annotationScm);
                                }
                            }
                        }

                        Stencil annotation = (Stencil)numbers[numberIndex];
                        annotation.TranslateAxis(
                            footnoteStencil.Extent(Axis.Y)[Direction.Positive] + numberRaise
                                - annotation.Extent(Axis.Y)[Direction.Positive],
                            Axis.Y);
                        footnoteStencil.AddAtEdge(Axis.X, Direction.Negative, annotation, 0.0);
                        numberIndex++;
                    }

                    if (!footnoteStencil.IsEmpty)
                    {
                        if (SchemeUtilities.ToBool(footnote.GetProperty("footnote")))
                        {
                            mol.AddAtEdge(Axis.Y, Direction.Negative, footnoteStencil, padding);
                        }
                        else
                        {
                            inNoteMol.AddAtEdge(
                                Axis.Y, Direction.Negative, footnoteStencil, inNotePadding);
                        }
                    }
                }

                sys.SetProperty("in-note-stencil", inNoteMol);
                sys.SetProperty("footnote-stencil", mol);
            }
            else if (entry is Prob p)
            {
                object stencils = p.GetProperty("footnotes");
                Stencil mol = Stencil.Empty;

                foreach (object st in Pair.ToList(stencils))
                {
                    List<object> parts = Pair.ToList(st);
                    if (parts.Count < 3 || !(parts[2] is Stencil footnoteStencil))
                    {
                        continue;
                    }

                    bool doNumbering = SchemeUtilities.ToBool(parts[1]);
                    object inTextStencil = Nil.Instance;
                    if (doNumbering && numberIndex < numbers.Count)
                    {
                        Stencil annotation = (Stencil)numbers[numberIndex];
                        object inTextAnnotation = inTextNumbers[numberIndex];
                        inTextStencil = TextInterface.InterpretMarkup(
                            paper, props, inTextAnnotation);
                        if (!(inTextStencil is Stencil))
                        {
                            inTextStencil = Nil.Instance;
                        }

                        annotation.TranslateAxis(
                            footnoteStencil.Extent(Axis.Y)[Direction.Positive] + numberRaise
                                - annotation.Extent(Axis.Y)[Direction.Positive],
                            Axis.Y);
                        footnoteStencil.AddAtEdge(Axis.X, Direction.Negative, annotation, 0.0);
                        numberIndex++;
                    }
                    else
                    {
                        inTextStencil = Stencil.Empty;
                    }

                    numberFootnoteTable = new Pair(
                        new Pair(parts[0], inTextStencil), numberFootnoteTable);
                    if (!footnoteStencil.IsEmpty)
                    {
                        mol.AddAtEdge(Axis.Y, Direction.Negative, footnoteStencil, padding);
                    }
                }

                p.SetProperty("footnote-stencil", mol);
            }
        }

        // A no-op unless numbering is turned on.
        book.TopPaper().SetVariable("number-footnote-table", numberFootnoteTable);
    }

    /// <summary>Builds the footnote separator stencil from the paper's markup.</summary>
    /// <param name="paper">The paper definition.</param>
    /// <returns>The separator, empty when there is no markup for it.</returns>
    public static Stencil GetFootnoteSeparatorStencil(OutputDef paper)
    {
        object props = SchemeUtilities.CallCallback(
            LilyPondScheme.PublicRef(LilyModule, "layout-extract-page-properties"), paper);

        object markup = paper.CVariable("footnote-separator-markup");

        if (!TextInterface.IsMarkup(markup))
        {
            return Stencil.Empty;
        }

        return TextInterface.InterpretMarkup(paper, props, markup) is Stencil interpreted
            ? interpreted
            : Stencil.Empty;
    }

    /// <summary>
    /// Stacks a page's footnotes above its footer, separator included.
    /// <para>The FIRST footnote added takes the footer padding and every later one takes
    /// the footnote padding — the footnotes are built upward, so the first one placed is
    /// the one nearest the footer.</para>
    /// </summary>
    /// <param name="footnotes">The footnote stencils, as a Scheme list.</param>
    /// <param name="foot">The footer stencil.</param>
    /// <param name="book">The book, for the paddings.</param>
    /// <returns>The footer with the footnotes on top.</returns>
    public static Stencil AddFootnotesToFooter(object footnotes, Stencil foot, PaperBook book)
    {
        bool footnotesFound = false;
        double footnotePadding
            = SchemeConvert.ToDouble(book.Paper.CVariable("footnote-padding"), 0.0);
        double footnoteFooterPadding
            = SchemeConvert.ToDouble(book.Paper.CVariable("footnote-footer-padding"), 0.0);

        List<object> reversed = Pair.ToList(footnotes);
        reversed.Reverse();

        foreach (object entry in reversed)
        {
            if (!(entry is Stencil stencil))
            {
                continue;
            }

            if (!stencil.IsEmpty)
            {
                foot.AddAtEdge(
                    Axis.Y,
                    Direction.Positive,
                    stencil,
                    !footnotesFound ? footnoteFooterPadding : footnotePadding);
                footnotesFound = true;
            }
        }

        if (footnotesFound)
        {
            Stencil separator = GetFootnoteSeparatorStencil(book.Paper);
            if (!separator.IsEmpty)
            {
                foot.AddAtEdge(Axis.Y, Direction.Positive, separator, footnotePadding);
            }
        }

        return foot;
    }

    /// <summary>Reads a spanner's <c>line-break-system-details</c>, off its LEFT bound.</summary>
    /// <param name="spanner">The spanner.</param>
    /// <returns>The details alist.</returns>
    public static object GetDetails(Spanner spanner)
        => spanner?.GetBound(Direction.Negative)?.GetProperty("line-break-system-details")
            ?? Nil.Instance;

    /// <summary>
    /// Drops the grobs that have killed themselves, giving every hara-kiri group the chance
    /// to do so first.
    /// </summary>
    private static List<Grob> FilterDeadElements(GrobArray input)
    {
        List<Grob> output = new List<Grob>();
        for (int i = 0; i < input.Count; i++)
        {
            if (input[i].HasInterface("hara-kiri-group-spanner-interface"))
            {
                HaraKiriGroupSpanner.ConsiderSuicide(input[i]);
            }

            if (input[i].IsLive)
            {
                output.Add(input[i]);
            }
        }

        return output;
    }

    private static object GetDetails(Element elt)
    {
        if (elt.Staves.Count == 0)
        {
            return Nil.Instance;
        }

        return GetDetails(elt.Staves[elt.Staves.Count - 1].GetSystem());
    }

    /// <summary>Answers a REFERENCE to the spring added most recently.</summary>
    /// <returns>A reference to the last spring in the problem.</returns>
    /// <remarks>
    /// This is upstream's <c>springs_.back ()</c>, and it exists because the two languages
    /// disagree about what that expression means. <see cref="Spring"/> is a STRUCT, so
    /// <c>_springs[_springs.Count - 1]</c> answers a COPY: a mutation through it compiles
    /// without a warning, changes a temporary, and is thrown away. Upstream's
    /// <c>springs_.back ()</c> is a reference and mutates the spring that is actually in the
    /// vector. Every such call site must go through this method.
    /// </remarks>
    private ref Spring LastSpring()
        => ref CollectionsMarshal.AsSpan(_springs)[_springs.Count - 1];

    private static void MarkAsSpaceable(Grob g) => g.SetProperty(StaffAffinitySymbol, false);

    private static Interval ProbExtent(Prob p)
        => p.GetProperty("stencil") is Stencil sten ? sten.Extent(Axis.Y) : new Interval(0, 0);

    /// <summary>
    /// Reads the three spacing fields out of a spec and onto a spring.
    /// <para>The ORDER matters: <c>SetDefaultStrength</c> runs between the distances and
    /// the stretchability, because it derives strength from the distances and would
    /// overwrite an explicit stretchability set before it.</para>
    /// </summary>
    private static void AlterSpringFromSpacingSpec(object spec, ref Spring spring)
    {
        double space = 0;
        double stretch = 0;
        double minDist = 0;

        if (PageLayoutSpacing.ReadSpacingSpec(spec, BasicDistanceSymbol, ref space))
        {
            spring.SetIdealDistance(space);
        }

        if (PageLayoutSpacing.ReadSpacingSpec(spec, MinimumDistanceSymbol, ref minDist))
        {
            spring.SetMinDistance(minDist);
        }

        spring.SetDefaultStrength();

        if (PageLayoutSpacing.ReadSpacingSpec(spec, StretchabilitySymbol, ref stretch))
        {
            spring.SetInverseStretchStrength(stretch);
        }
    }

    /// <summary>
    /// Upstream's <c>ly_is_list</c>: a PROPER list, so a dotted pair answers false. That
    /// distinction is the whole point at the one call site — an override written
    /// <c>staff-staff-spacing.padding</c> leaves a dotted pair, and treating it as an
    /// alist would read its cdr as an entry.
    /// </summary>
    private static bool IsProperList(object value)
    {
        object cursor = value;
        while (cursor is Pair pair)
        {
            cursor = pair.Cdr;
        }

        return cursor is Nil;
    }

    private static Direction ToDirection(object value, Direction fallback)
    {
        if (!SchemeConvert.IsNumber(value))
        {
            return fallback;
        }

        int d = SchemeConvert.ToInt(value, (int)fallback);
        return d < 0 ? Direction.Negative : d > 0 ? Direction.Positive : fallback;
    }

    /// <summary>
    /// Builds a system's upper and lower skylines.
    /// <para>
    /// The staves' positions within the system are not known yet, so both skylines are made
    /// as CONSERVATIVE as possible: the upper one pretends every staff is packed close to
    /// the top, the lower one that every staff is packed close to the bottom. The upper
    /// skyline ends up relative to the top spaceable staff and the lower one relative to
    /// the bottom spaceable staff.
    /// </para>
    /// </summary>
    private static void BuildSystemSkyline(
        List<Grob> staves, List<double> minimumTranslations, Skyline up, Skyline down)
    {
        if (minimumTranslations.Count == 0)
        {
            return;
        }

        double firstTranslation = minimumTranslations[0];
        double lastSpaceableDy = 0;
        double firstSpaceableDy = 0;
        bool foundSpaceableStaff = false;

        for (int i = 0; i < staves.Count && i < minimumTranslations.Count; i++)
        {
            double dy = minimumTranslations[i] - firstTranslation;
            Grob g = staves[i];
            object skyScm = g.GetProperty(VerticalSkylinesSymbol);
            if (SkylinePair.FromScheme(skyScm) is SkylinePair sky)
            {
                up.Raise(-dy);
                up.Merge(sky[Direction.Positive]);
                up.Raise(dy);

                down.Raise(-dy);
                down.Merge(sky[Direction.Negative]);
                down.Raise(dy);
            }

            if (PageLayoutSpacing.IsSpaceable(staves[i]))
            {
                if (!foundSpaceableStaff)
                {
                    foundSpaceableStaff = true;
                    firstSpaceableDy = dy;
                }

                lastSpaceableDy = dy;
            }
        }

        up.Raise(-firstSpaceableDy);
        down.Raise(-lastSpaceableDy);
    }

    /// <summary>
    /// Adds one system to the problem: its spring from the previous element, and the
    /// springs between its own spaceable staves.
    /// </summary>
    private void AppendSystem(SystemGrob sys, Spring spring, double indent, double padding)
    {
        if (!(sys.GetObject("vertical-alignment") is Grob align))
        {
            return;
        }

        align.SetProperty("positioning-done", true);

        GrobArray allElts = PointerGroupInterface.GetGrobArray(align, ElementsSymbol);
        List<Grob> elts = FilterDeadElements(allElts);
        List<double> minimumOffsets
            = AlignInterface.GetMinimumTranslationsWithoutMinDist(align, elts, Axis.Y);
        List<double> minimumOffsetsWithMinDist
            = AlignInterface.GetMinimumTranslations(align, elts, Axis.Y);

        Skyline upSkyline = new Skyline(Direction.Positive);
        Skyline downSkyline = new Skyline(Direction.Negative);
        BuildSystemSkyline(elts, minimumOffsetsWithMinDist, upSkyline, downSkyline);
        upSkyline.Shift(indent);
        downSkyline.Shift(indent);

        if (sys.GetProperty("in-note-stencil") is Stencil inNoteStencil
            && inNoteStencil.Extent(Axis.Y).Length > 0)
        {
            sys.SetProperty("in-note-padding", _inNotePadding);
            sys.SetProperty("in-note-system-padding", _inNoteSystemPadding);
            sys.SetProperty("in-note-direction", SchemeConvert.FromInt((int)_inNoteDirection));
            Skyline sky = _inNoteDirection == Direction.Positive ? upSkyline : downSkyline;
            sky.SetMinimumHeight(
                sky.MaxHeight()
                + ((int)_inNoteDirection
                    * (_inNoteSystemPadding + inNoteStencil.Extent(Axis.Y).Length)));
        }

        // The distance is measured WITH skyline-horizontal-padding, because that padding is
        // not applied when an individual staff's skyline is built -- it belongs to the
        // system, so it is added here, at the moment the system joins the page.
        double minimumDistance = upSkyline.Distance(
            _bottomSkyline,
            SchemeConvert.ToDouble(sys.GetProperty("skyline-horizontal-padding"), 0)) + padding;

        Spring springCopy = spring;
        springCopy.EnsureMinDistance(minimumDistance);
        _springs.Add(springCopy);

        if (elts.Count != 0 && !PageLayoutSpacing.IsSpaceable(elts[0]))
        {
            // A loose first line: store the minimum distance measured against the indents.
            Skyline firstSkyline = new Skyline(Direction.Positive);
            object skyScm = elts[0].GetProperty(VerticalSkylinesSymbol);
            if (SkylinePair.FromScheme(skyScm) is SkylinePair sky)
            {
                firstSkyline.Merge(sky[Direction.Positive]);
            }

            firstSkyline.Shift(indent);
            minimumDistance = firstSkyline.Distance(_bottomSkyline) - _bottomLooseBaseline;
        }

        _bottomSkyline = downSkyline;
        _elements.Add(new Element(elts, minimumOffsets, minimumDistance, padding));

        // Now the springs BETWEEN this system's own vertical axis groups. If the user gave
        // explicit alignment distances the springs are fixed at them; otherwise they
        // stretch.
        object details = GetDetails(_elements[_elements.Count - 1]);
        object manualDists
            = SchemeUtilities.LyAssocGet(AlignmentDistancesSymbol, details, Nil.Instance);
        int lastSpaceableStaff = 0;
        bool foundSpaceableStaff = false;
        for (int i = 0; i < elts.Count; ++i)
        {
            if (!PageLayoutSpacing.IsSpaceable(elts[i]))
            {
                continue;
            }

            if (!foundSpaceableStaff)
            {
                // Leave room for any loose lines above this system.
                if (i > 0)
                {
                    LastSpring().EnsureMinDistance(
                        _bottomLooseBaseline - minimumOffsetsWithMinDist[i] + padding);
                }

                foundSpaceableStaff = true;
                lastSpaceableStaff = i;

                // No spring for the FIRST staff: these are the springs BETWEEN staves.
                continue;
            }

            Spring staffSpring = new Spring(0.5, 0.0);
            object spec = elts[lastSpaceableStaff].GetProperty("staff-staff-spacing");

            // An override of the form staff-staff-spacing.some-property leaves a non-list
            // pair with an unpure-pure container as its cdr; drop that before going on.
            if (spec is Pair specPair && !IsProperList(spec))
            {
                spec = new Pair(specPair.Car, Nil.Instance);
            }

            object defaultSpacing = elts[lastSpaceableStaff].GetProperty(
                "default-staff-staff-spacing");
            if (defaultSpacing is Pair && IsProperList(spec))
            {
                foreach (object s in Pair.ToList(defaultSpacing))
                {
                    if (s is Pair entry && !(SchemeUtilities.Assq(entry.Car, spec) is Pair))
                    {
                        spec = new Pair(entry, spec);
                    }
                }
            }

            AlterSpringFromSpacingSpec(spec, ref staffSpring);

            _springs.Add(staffSpring);
            double minDistance = (foundSpaceableStaff
                    ? minimumOffsetsWithMinDist[lastSpaceableStaff]
                    : 0)
                - minimumOffsetsWithMinDist[i];
            LastSpring().EnsureMinDistance(minDistance);

            if (manualDists is Pair manualPair)
            {
                if (SchemeConvert.IsNumber(manualPair.Car))
                {
                    double dy = SchemeConvert.ToDouble(manualPair.Car, 0.0);

                    LastSpring().SetIdealDistance(dy);
                    LastSpring().SetMinDistance(dy);
                    LastSpring().SetInverseStretchStrength(0);
                }

                manualDists = manualPair.Cdr;
            }

            lastSpaceableStaff = i;
        }

        _bottomLooseBaseline = foundSpaceableStaff
            ? minimumOffsetsWithMinDist[lastSpaceableStaff]
                - minimumOffsetsWithMinDist[minimumOffsetsWithMinDist.Count - 1]
            : 0;

        // Corner case: one staff, and it was not spaceable.
        if (!foundSpaceableStaff && elts.Count != 0)
        {
            MarkAsSpaceable(elts[0]);
        }
    }

    /// <summary>Adds one title or markup line to the problem.</summary>
    private void AppendProb(Prob prob, Spring spring, double padding)
    {
        object skyScm = prob.GetProperty(VerticalSkylinesSymbol);
        double minimumDistance = 0;
        bool tightSpacing = SchemeUtilities.ToBool(prob.GetProperty("tight-spacing"));

        if (SkylinePair.FromScheme(skyScm) is SkylinePair sky)
        {
            minimumDistance = Math.Max(
                sky[Direction.Positive].Distance(_bottomSkyline), _bottomLooseBaseline);
            _bottomSkyline = sky[Direction.Negative];
        }
        else if (prob.GetProperty("stencil") is Stencil sten)
        {
            Interval iv = sten.Extent(Axis.Y);
            minimumDistance = iv[Direction.Positive] - _bottomSkyline.MaxHeight();

            _bottomSkyline.Clear();
            _bottomSkyline.SetMinimumHeight(iv[Direction.Negative]);
        }

        _bottomLooseBaseline = 0.0;

        Spring springCopy = spring;
        if (tightSpacing)
        {
            springCopy.SetMinDistance(minimumDistance);
            springCopy.SetInverseStretchStrength(0.0);
            springCopy.SetIdealDistance(0.0);
        }
        else
        {
            springCopy.EnsureMinDistance(minimumDistance + padding);
        }

        _springs.Add(springCopy);
        _elements.Add(new Element(prob, padding));
    }

    /// <summary>Solves the rod-and-spring problem, compressing the page if it must.</summary>
    private void SolveRodSpringProblem(bool ragged, double fixedForce)
    {
        SimpleSpacer spacer = new SimpleSpacer();

        for (int i = 0; i < _springs.Count; ++i)
        {
            spacer.AddSpring(_springs[i]);
        }

        SpacerSolution sol;
        if (ragged && !double.IsInfinity(fixedForce))
        {
            // The spacer has to be told it is NOT ragged, or it refuses to stretch.
            sol = spacer.Solve(_pageHeight, false);

            if (spacer.ConfigurationLength(fixedForce) <= _pageHeight)
            {
                sol = new SpacerSolution(fixedForce, sol.Fits);
            }

            _solution = spacer.SpringPositions(sol.Force, false);
        }
        else
        {
            sol = spacer.Solve(_pageHeight, ragged);
            Force = sol.Force;
            _solution = spacer.SpringPositions(sol.Force, ragged);
        }

        if (!sol.Fits)
        {
            double overflow = spacer.ConfigurationLength(sol.Force) - _pageHeight;
            if (ragged && overflow < 1e-6)
            {
                Warn.Warning("ragged-bottom was specified, but page must be compressed");
            }
            else
            {
                Warn.Warning("compressing over-full page by "
                    + overflow.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                    + " staff-spaces");
                Force = double.NegativeInfinity;
                int spaceCount = _solution.Count;
                if (spaceCount > 2)
                {
                    double spacingIncrement = overflow / (spaceCount - 2);
                    for (int i = 2; i < spaceCount; i++)
                    {
                        _solution[i] -= (i - 1) * spacingIncrement;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Turns the solved spring positions into per-system offsets, stretching each system's
    /// staves to land where the solution puts them.
    /// </summary>
    private object FindSystemOffsets()
    {
        List<object> systemOffsets = new List<object>();

        // Spring 0 is the top of the page; the interesting ones start at 1.
        int springIdx = 1;
        List<Grob> looseLines = new List<Grob>();
        List<double> looseLineMinDistances = new List<double>();
        Grob lastSpaceableLine = null;
        double lastSpaceableLineTranslation = 0;
        Interval lastTitleExtent = Interval.Empty;

        for (int i = 0; i < _elements.Count; ++i)
        {
            if (_elements[i].Prob != null)
            {
                if (springIdx >= _solution.Count)
                {
                    break;
                }

                systemOffsets.Add(_solution[springIdx]);
                Interval probExtent = ProbExtent(_elements[i].Prob);

                // Lay out any loose lines between this one and the last.
                if (looseLines.Count != 0)
                {
                    Grob back = looseLines[looseLines.Count - 1];
                    Interval looseExtent = back != null
                        ? back.Extent(back, Axis.Y)
                        : Interval.Empty;
                    double minDistance = -looseExtent[Direction.Negative]
                        + probExtent[Direction.Positive] + _elements[i].Padding;

                    looseLineMinDistances.Add(minDistance);
                    looseLines.Add(null);

                    DistributeLooseLines(
                        looseLines,
                        looseLineMinDistances,
                        lastSpaceableLineTranslation,
                        -_solution[springIdx]);
                    looseLines.Clear();
                    looseLineMinDistances.Clear();
                }

                lastSpaceableLine = null;
                lastSpaceableLineTranslation = -_solution[springIdx];
                lastTitleExtent = probExtent;
                springIdx++;
            }
            else
            {
                if (springIdx >= _solution.Count)
                {
                    break;
                }

                // Signs: the configuration returned has zero at the top of the page and
                // grows downward, as does the solution vector. WITHIN a staff, positive is
                // up. Every line below that mixes the two says which it means.
                double firstStaffPosition = _solution[springIdx];
                double firstStaffMinTranslation = _elements[i].MinOffsets.Count != 0
                    ? _elements[i].MinOffsets[0]
                    : 0;
                double systemPosition = firstStaffPosition + firstStaffMinTranslation;

                List<double> minOffsets = _elements[i].MinOffsets;
                bool foundSpaceableStaff = false;
                for (int staffIdx = 0; staffIdx < _elements[i].Staves.Count; ++staffIdx)
                {
                    Grob staff = _elements[i].Staves[staffIdx];
                    staff.SetProperty("system-Y-offset", -systemPosition);

                    if (PageLayoutSpacing.IsSpaceable(staff))
                    {
                        if (springIdx >= _solution.Count)
                        {
                            break;
                        }

                        // Relative to the system, where negative is down.
                        staff.TranslateAxis(systemPosition - _solution[springIdx], Axis.Y);

                        if (looseLines.Count != 0)
                        {
                            if (staffIdx != 0)
                            {
                                looseLineMinDistances.Add(
                                    minOffsets[staffIdx - 1] - minOffsets[staffIdx]);
                            }
                            else
                            {
                                // A null line, to break any staff-affinity carried over
                                // from the previous system.
                                looseLineMinDistances.Add(0.0);
                                looseLines.Add(null);
                                looseLineMinDistances.Add(_elements[i].Padding - minOffsets[0]);
                            }

                            looseLines.Add(staff);

                            DistributeLooseLines(
                                looseLines,
                                looseLineMinDistances,
                                lastSpaceableLineTranslation,
                                -_solution[springIdx]);
                            looseLines.Clear();
                            looseLineMinDistances.Clear();
                        }

                        lastSpaceableLine = staff;
                        lastSpaceableLineTranslation = -_solution[springIdx];
                        foundSpaceableStaff = true;
                        springIdx++;
                    }
                    else
                    {
                        if (staff.Extent(staff, Axis.Y).IsEmpty)
                        {
                            continue;
                        }

                        if (looseLines.Count == 0)
                        {
                            looseLines.Add(lastSpaceableLine);
                        }

                        if (staffIdx != 0)
                        {
                            // Rods only go between ADJACENT lines, which upstream notes is
                            // not the most accurate scheme available.
                            looseLineMinDistances.Add(
                                minOffsets[staffIdx - 1] - minOffsets[staffIdx]);
                        }
                        else
                        {
                            double minDist = 0;
                            if (looseLines[looseLines.Count - 1] != null)
                            {
                                // Distance to the last line of the preceding system,
                                // system-system-spacing padding included.
                                minDist = _elements[i].MinDistance + _elements[i].Padding;
                                looseLineMinDistances.Add(0.0);
                                looseLines.Add(null);
                            }
                            else if (!lastTitleExtent.IsEmpty)
                            {
                                minDist = staff.Extent(staff, Axis.Y)[Direction.Positive]
                                    - lastTitleExtent[Direction.Negative] + _elements[i].Padding;
                            }
                            else
                            {
                                minDist = _headerPadding + _headerHeight
                                    + staff.Extent(staff, Axis.Y)[Direction.Positive];
                            }

                            looseLineMinDistances.Add(minDist);
                        }

                        looseLines.Add(staff);
                    }
                }

                // Corner case: a system with no live staves still takes up one spring, the
                // same as a system with one, so the index has to advance past it.
                if (!foundSpaceableStaff)
                {
                    springIdx++;
                }

                systemOffsets.Add(systemPosition);
            }
        }

        if (looseLines.Count != 0)
        {
            Grob last = looseLines[looseLines.Count - 1];
            Interval lastExt = last != null ? last.Extent(last, Axis.Y) : Interval.Empty;
            looseLineMinDistances.Add(
                -lastExt[Direction.Negative] + _footerHeight + _footerPadding);
            looseLines.Add(null);

            DistributeLooseLines(
                looseLines, looseLineMinDistances, lastSpaceableLineTranslation, -_pageHeight);
        }

        return Pair.ListFrom(systemOffsets);
    }

    /// <summary>
    /// Distributes unspaced lines between two lines that are already placed — the first and
    /// last entries of the list.
    /// <para>Both translations are relative to the PAGE, and offsets DECREASE going down,
    /// which is why the spacer is handed first minus last.</para>
    /// </summary>
    private static void DistributeLooseLines(
        List<Grob> looseLines,
        List<double> minDistances,
        double firstTranslation,
        double lastTranslation)
    {
        SimpleSpacer spacer = new SimpleSpacer();
        for (int i = 0; i + 1 < looseLines.Count && i < minDistances.Count; ++i)
        {
            object spec = PageLayoutSpacing.GetSpacingSpec(
                looseLines[i], looseLines[i + 1], false, 0, int.MaxValue);
            Spring spring = new Spring(1.0, 0.0);
            AlterSpringFromSpacingSpec(spec, ref spring);
            spring.EnsureMinDistance(minDistances[i]);
            spacer.AddSpring(spring);
        }

        SpacerSolution sol = spacer.Solve(firstTranslation - lastTranslation, false);

        List<double> solution = spacer.SpringPositions(sol.Force, false);
        for (int i = 1; i + 1 < solution.Count && i < looseLines.Count; ++i)
        {
            if (looseLines[i] != null)
            {
                double systemOffset = SchemeConvert.ToDouble(
                    looseLines[i].GetProperty("system-Y-offset"), 0.0);
                looseLines[i].TranslateAxis(
                    firstTranslation - solution[i] - systemOffset, Axis.Y);
            }
        }
    }

    /// <summary>
    /// One entry in the page's vertical problem: either a system, as its list of staves, or
    /// a single title prob. Upstream calls it "a union in spirit" — exactly one of the two
    /// shapes is in use.
    /// </summary>
    private sealed class Element
    {
        /// <summary>Initializes a system element.</summary>
        public Element(List<Grob> staves, List<double> minOffsets, double minDistance, double padding)
        {
            Staves = staves;
            MinOffsets = minOffsets;
            MinDistance = minDistance;
            Padding = padding;
            Prob = null;
        }

        /// <summary>Initializes a title element.</summary>
        public Element(Prob prob, double padding)
        {
            Prob = prob;
            Padding = padding;
            Staves = new List<Grob>();
            MinOffsets = new List<double>();
            MinDistance = 0;
        }

        /// <summary>Gets the title, or <see langword="null"/> for a system.</summary>
        public Prob Prob { get; }

        /// <summary>Gets the system's staves, empty for a title.</summary>
        public List<Grob> Staves { get; }

        /// <summary>Gets the staves' minimum offsets within the system.</summary>
        public List<double> MinOffsets { get; }

        /// <summary>Gets the skyline distance from the previous system, indent included.</summary>
        public double MinDistance { get; }

        /// <summary>Gets the padding from the previous system.</summary>
        public double Padding { get; }
    }
}
