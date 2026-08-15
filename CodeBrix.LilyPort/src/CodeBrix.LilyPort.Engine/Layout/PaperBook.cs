/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2004--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/paper-book.cc, lily/include/paper-book.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - upstream keeps its lists as SCM with a tail pointer, appending in O(1) through
//     SCM_CDRLOC. The port keeps a List<object> and converts at the boundary, which is
//     the same order with the same complexity and no pointer into a cons cell.
//   - the OUTPUT half -- output, classic_output, output_stencil(s), dump_header_fields --
//     is NOT ported; see the Engine PORT-COVERAGE entry. Those dispatch into
//     `lily framework-<backend>' modules and act on the -dclip-systems / -dpreview /
//     -dcrop / -daux-files program options, which are the CLI surface D14 replaced with
//     Lily.Shell and the PS/Cairo backends D19 replaced. Page ASSEMBLY, which is what
//     page layout is for and what every caller in this port needs, is Pages() and is here.

/// <summary>
/// Collects a book's headers, scores and texts and turns them into PAGES.
/// <para>
/// This is the object the page breakers are handed: it knows the paper, it can produce the
/// book's system specs, and its <see cref="Pages"/> is what actually invokes the breaker
/// named by the <c>page-breaking</c> paper variable.
/// </para>
/// </summary>
public sealed class PaperBook
{
    private static readonly Symbol ForceSymbol = Symbol.Intern("force");
    private static readonly Symbol PageBreakPermissionSymbol
        = Symbol.Intern("page-break-permission");

    private static readonly Symbol LineBreakPermissionSymbol
        = Symbol.Intern("line-break-permission");

    private static readonly Symbol BreakBeforeSymbol = Symbol.Intern("breakbefore");
    private static readonly Symbol AllowSymbol = Symbol.Intern("allow");
    private static readonly Symbol FirstPageNumberSymbol = Symbol.Intern("first-page-number");
    private static readonly Symbol IsLastBookpartSymbol = Symbol.Intern("is-last-bookpart");

    private static readonly Symbol BookpartLevelPageNumberingSymbol
        = Symbol.Intern("bookpart-level-page-numbering");

    private static readonly string[] LilyModule = { "lily" };
    private static readonly string[] PageModule = { "lily", "page" };

    private readonly List<object> _printElements = new List<object>();
    private readonly List<object> _performances = new List<object>();
    private bool _printBookparts;
    private object _systems;
    private object _pages;

    /// <summary>
    /// Initializes a paper book over a paper block.
    /// <para>
    /// THE PAPER IS SCALED HERE, IN THE CONSTRUCTOR, and that ordering is load-bearing:
    /// everything downstream is laid out in staff spaces rather than millimetres, and
    /// <c>Book::process</c> normalizes the result afterwards because line-width is computed
    /// from dimensions that must already be in output units.
    /// </para>
    /// </summary>
    /// <param name="paper">The paper definition.</param>
    /// <param name="parentPart">The enclosing bookpart's book, or <see langword="null"/>.</param>
    public PaperBook(OutputDef paper, PaperBook parentPart)
    {
        Header = Nil.Instance;
        Header0 = Nil.Instance;
        _pages = false;
        _systems = false;

        double scale = SchemeConvert.ToDouble(paper.CVariable("output-scale"), 1.0);
        Paper = paper.ScaledClone(scale);

        if (parentPart != null)
        {
            Parent = parentPart;
            Paper.Parent = parentPart.Paper;
        }
    }

    /// <summary>Gets or sets the book's header module.</summary>
    public object Header { get; set; }

    /// <summary>Gets or sets the FIRST header seen, which classic output titles with.</summary>
    public object Header0 { get; set; }

    /// <summary>Gets the book's paper, already scaled.</summary>
    public OutputDef Paper { get; }

    /// <summary>Gets the enclosing bookpart's book, or <see langword="null"/>.</summary>
    public PaperBook Parent { get; }

    /// <summary>Gets the outermost paper definition.</summary>
    /// <returns>The top paper.</returns>
    public OutputDef TopPaper()
    {
        OutputDef paper = Paper;
        while (paper.Parent != null)
        {
            paper = paper.Parent;
        }

        return paper;
    }

    /// <summary>Adds a score, a header module or a markup list to the book.</summary>
    /// <param name="score">The element to add.</param>
    public void AddScore(object score) => _printElements.Add(score);

    /// <summary>Adds a bookpart, which switches the book into bookpart mode.</summary>
    /// <param name="part">The bookpart's own paper book.</param>
    public void AddBookpart(object part)
    {
        _printBookparts = true;
        _printElements.Add(part);
    }

    /// <summary>Adds a performance, for the MIDI half.</summary>
    /// <param name="performance">The performance.</param>
    public void AddPerformance(object performance) => _performances.Add(performance);

    /// <summary>Gets the performances, as a Scheme list.</summary>
    /// <returns>The performances.</returns>
    public object Performances() => Pair.ListFrom(_performances);

    /// <summary>Gets the print elements, as a Scheme list.</summary>
    /// <returns>The elements.</returns>
    public object PrintElements() => Pair.ListFrom(_printElements);

    /// <summary>Gets whether this book holds bookparts rather than scores directly.</summary>
    public bool PrintBookparts => _printBookparts;

    /// <summary>
    /// Walks the book and its bookparts in output order, telling each part WHERE its
    /// pages start and WHETHER it is the last one, then forces its pages.
    /// <para>
    /// This is <c>Paper_book::output_aux</c>, and it is the step that makes a book of
    /// several bookparts one book rather than several. Two paper variables are written
    /// here and NOWHERE ELSE, and both are read back through the page's property chain by
    /// <c>ly/titling-init.ly</c> and <c>scm/page.scm</c>:
    /// </para>
    /// <list type="bullet">
    /// <item><c>first-page-number</c> — carried FORWARD across parts, so the second part
    /// goes on numbering where the first stopped. Skipped when the paper asks for
    /// <c>bookpart-level-page-numbering</c>, which is the whole point of that
    /// variable.</item>
    /// <item><c>is-last-bookpart</c> — <see langword="true"/> only for the last part of
    /// the last book, which is what <c>on-last-page</c> tests before printing the
    /// tagline.</item>
    /// </list>
    /// <para>
    /// Leaving them unwritten is not a quiet no-op, and that is worth stating: an unset
    /// paper variable answers the UNSET marker, <c>page.scm</c> puts it on the page's
    /// property alist regardless, and <c>chain-assoc-get</c> then finds a key whose value
    /// is TRUTHY. So <c>on-last-page</c> was true on the last page of EVERY bookpart, and
    /// the port printed a tagline the oracle prints once per book.
    /// </para>
    /// <para>
    /// DIVERGENCE: upstream also writes the performances' MIDI from here, threading a
    /// <c>first-performance-number</c> alongside. The port's MIDI half is driven
    /// separately and is closed (G2), so this carries the page half only.
    /// </para>
    /// </summary>
    /// <param name="isLast">Whether this book is the last part of the last book.</param>
    /// <param name="firstPageNumber">
    /// The number the next page gets, advanced past the pages this book produces.
    /// </param>
    /// <returns>The number of pages this book produced.</returns>
    public int OutputAux(bool isLast, ref int firstPageNumber)
    {
        int pageNumber = 0;

        if (_printBookparts)
        {
            for (int i = 0; i < _printElements.Count; i++)
            {
                if (_printElements[i] is PaperBook bookpart)
                {
                    // Upstream tests the raw list tail, so the LAST ELEMENT is what
                    // carries is_last -- not the last bookpart, which is the same thing
                    // only while the list holds nothing else.
                    bool isLastPart = isLast && i == _printElements.Count - 1;
                    pageNumber += bookpart.OutputAux(isLastPart, ref firstPageNumber);
                }
            }

            return pageNumber;
        }

        if (_printElements.Count == 0)
        {
            return 0;
        }

        bool numberPerBookpart = SchemeUtilities.ToBool(
            Paper.LookupVariable(BookpartLevelPageNumberingSymbol));

        if (!numberPerBookpart)
        {
            Paper.SetVariable(FirstPageNumberSymbol, (long)firstPageNumber);
        }

        Paper.SetVariable(IsLastBookpartSymbol, isLast);

        // Generate all stencils to trigger font loads -- upstream's comment, and the
        // reason this counts pages rather than asking for a count.
        pageNumber = Pair.ToList(Pages()).Count;

        if (!numberPerBookpart)
        {
            firstPageNumber += pageNumber;
        }

        return pageNumber;
    }

    /// <summary>
    /// Runs <see cref="OutputAux"/> over a whole book, seeded from the paper's own
    /// <c>first-page-number</c> — which is <c>Paper_book::output</c>'s opening step.
    /// </summary>
    public void Output()
    {
        int firstPageNumber = SchemeConvert.ToInt(Paper.CVariable("first-page-number"), 1);

        OutputAux(true, ref firstPageNumber);
    }

    /// <summary>
    /// Gets the header scopes, OUTERMOST FIRST — the parent's before this book's, so that a
    /// bookpart's own header overrides the book's.
    /// </summary>
    /// <returns>The scopes, as a Scheme list.</returns>
    public object GetScopes()
    {
        List<object> scopes = new List<object>();
        if (Parent != null)
        {
            scopes.AddRange(Pair.ToList(Parent.GetScopes()));
        }

        if (Header is SchemeModule)
        {
            scopes.Insert(0, Header);
        }

        return Pair.ListFrom(scopes);
    }

    /// <summary>Builds the book's title stencil from the <c>book-title</c> paper procedure.</summary>
    /// <returns>The title, empty when there is none.</returns>
    public Stencil BookTitle()
    {
        object titleFunc = Paper.LookupVariable(Symbol.Intern("book-title"));
        Stencil title = Stencil.Empty;

        List<object> scopes = new List<object>();
        if (Header is SchemeModule)
        {
            scopes.Add(Header);
        }

        object tit = Nil.Instance;
        if (SchemeUtilities.IsProcedure(titleFunc))
        {
            tit = SchemeUtilities.CallCallback(titleFunc, Paper, Pair.ListFrom(scopes));
        }

        if (tit is Stencil st)
        {
            title = st;
        }

        if (!title.IsEmpty)
        {
            title.AlignTo(Axis.Y, Direction.Positive.Value);
        }

        return title;
    }

    /// <summary>Builds a score's title stencil from the <c>score-title</c> paper procedure.</summary>
    /// <param name="header">The score's own header module.</param>
    /// <returns>The title, empty when there is none.</returns>
    public Stencil ScoreTitle(object header)
    {
        object titleFunc = Paper.LookupVariable(Symbol.Intern("score-title"));
        Stencil title = Stencil.Empty;

        // The SCORE's header goes on FRONT of the book's, so it wins.
        List<object> scopes = new List<object>();
        if (Header is SchemeModule)
        {
            scopes.Add(Header);
        }

        if (header is SchemeModule)
        {
            scopes.Insert(0, header);
        }

        object tit = Nil.Instance;
        if (SchemeUtilities.IsProcedure(titleFunc))
        {
            tit = SchemeUtilities.CallCallback(titleFunc, Paper, Pair.ListFrom(scopes));
        }

        if (tit is Stencil st)
        {
            title = st;
        }

        if (!title.IsEmpty)
        {
            title.AlignTo(Axis.Y, Direction.Positive.Value);
        }

        return title;
    }

    private object GetScoreTitle(object header)
    {
        Stencil title = ScoreTitle(header);
        if (!title.IsEmpty)
        {
            object props = Paper.LookupVariable(Symbol.Intern("score-title-properties"));
            Prob ps = PaperSystem.Make(props);
            PaperSystem.SetStencil(ps, title);
            return ps;
        }

        return false;
    }

    /// <summary>
    /// Builds the book's system specs: the flat, ordered list of scores, titles and markup
    /// lines the page breaker distributes over pages.
    /// <para>
    /// The loop carries three pieces of state forward, and each exists for a reason worth
    /// knowing. HEADER is the most recent header module, consumed by the next score.
    /// LABELS accumulate from page markers and attach to the NEXT element.
    /// LAST_SYSTEM_SPEC is what a page marker's permission and a score's
    /// <c>breakbefore</c> are applied to — the element BEFORE the one that asked for them,
    /// because a break belongs to the boundary and not to what follows it.
    /// </para>
    /// </summary>
    /// <returns>The system specs, as a Scheme list.</returns>
    public object GetSystemSpecs()
    {
        List<object> systemSpecs = new List<object>();
        object lastSystemSpec = false;

        Stencil title = BookTitle();
        if (!title.IsEmpty)
        {
            object props = Paper.LookupVariable(Symbol.Intern("book-title-properties"));
            Prob ps = PaperSystem.Make(props);
            PaperSystem.SetStencil(ps, title);

            systemSpecs.Add(ps);
            lastSystemSpec = ps;
        }

        object pageProperties = SchemeUtilities.CallCallback(
            LilyPondScheme.PublicRef(LilyModule, "layout-extract-page-properties"), Paper);

        object header = Nil.Instance;
        object labels = Nil.Instance;

        foreach (object elem in _printElements)
        {
            if (elem is SchemeModule)
            {
                header = elem;
                if (Header0 is Nil)
                {
                    Header0 = header;
                }
            }
            else if (elem is PageMarker pageMarker)
            {
                // A page marker either sets break/turn permission on the PREVIOUS element
                // or supplies a bookmarking label for the NEXT one.
                if (pageMarker.PermissionSymbol is Symbol)
                {
                    if (SchemeUtilities.IsSchemeTrue(lastSystemSpec) && !(lastSystemSpec is bool))
                    {
                        SetPagePermission(
                            lastSystemSpec,
                            pageMarker.PermissionSymbol as Symbol,
                            pageMarker.PermissionValue);
                    }
                }

                if (pageMarker.Label is Symbol)
                {
                    labels = new Pair(pageMarker.Label, labels);
                }
            }
            else if (elem is PaperScore pscore)
            {
                object scoreTitle = GetScoreTitle(header);

                if (!(lastSystemSpec is bool))
                {
                    SetSystemPenalty(lastSystemSpec, header);
                }

                if (scoreTitle is Prob titleProb)
                {
                    systemSpecs.Add(titleProb);
                    lastSystemSpec = titleProb;
                }

                header = Nil.Instance;
                systemSpecs.Add(pscore);
                lastSystemSpec = pscore;
                if (labels is Pair)
                {
                    SetLabels(pscore, labels);
                    labels = Nil.Instance;
                }
            }
            else if (elem is MusicOutput)
            {
                // A Performance rather than a Paper_score: MIDI is ignored here.
            }
            else if (TextInterface.IsMarkupList(elem))
            {
                object texts = SchemeUtilities.CallCallback(
                    LilyPondScheme.PublicRef(LilyModule, "interpret-markup-list"),
                    Paper,
                    pageProperties,
                    elem);

                Prob first = null;
                Prob last = null;
                List<object> textList = Pair.ToList(texts);
                for (int i = 0; i < textList.Count; i++)
                {
                    if (!(textList[i] is Stencil t))
                    {
                        continue;
                    }

                    Prob ps = PaperSystem.Make(Nil.Instance);
                    ps.SetProperty("page-break-permission", AllowSymbol);
                    ps.SetProperty("page-turn-permission", AllowSymbol);
                    ps.SetProperty("last-markup-line", false);
                    ps.SetProperty("first-markup-line", false);

                    PaperSystem.SetStencil(ps, t);

                    ps.SetProperty("footnotes", PaperSystem.GetFootnotes(t.Expression));
                    ps.SetProperty("is-title", true);
                    if (i == 0)
                    {
                        first = ps;
                    }
                    else
                    {
                        last = ps;

                        // Placed closely to the previous line, with no stretching: the
                        // lines of one paragraph are not spread apart from each other.
                        ps.SetProperty("tight-spacing", true);
                    }

                    systemSpecs.Add(ps);
                    lastSystemSpec = ps;

                    if (labels is Pair)
                    {
                        SetLabels(ps, labels);
                        labels = Nil.Instance;
                    }
                }

                // Widow and orphan avoidance. A single-line markup list is excluded,
                // because there is no pair of lines to keep together.
                if (first != null && last != null)
                {
                    last.SetProperty("last-markup-line", true);
                    first.SetProperty("first-markup-line", true);
                }
            }
        }

        return Pair.ListFrom(systemSpecs);
    }

    /// <summary>
    /// Gets every system of the book, laid out. Cached, because generating them is what
    /// triggers the font loads.
    /// </summary>
    /// <returns>The systems, as a Scheme list.</returns>
    public object Systems()
    {
        if (SchemeUtilities.IsSchemeTrue(_systems) && !(_systems is bool))
        {
            return _systems;
        }

        List<object> systems = new List<object>();
        if (_printBookparts)
        {
            foreach (object p in _printElements)
            {
                if (p is PaperBook bookpart)
                {
                    systems.AddRange(Pair.ToList(bookpart.Systems()));
                }
            }
        }
        else
        {
            foreach (object s in Pair.ToList(GetSystemSpecs()))
            {
                if (s is PaperScore pscore)
                {
                    foreach (Prob paperSystem in pscore.GetPaperSystems())
                    {
                        systems.Add(paperSystem);
                    }
                }
                else
                {
                    systems.Add(s);
                }
            }
        }

        _systems = Pair.ListFrom(systems);
        return _systems;
    }

    /// <summary>
    /// Gets the book's PAGES, running the page breaker named by the <c>page-breaking</c>
    /// paper variable.
    /// <para>
    /// The order here matters and is upstream's: break into pages, then build every page's
    /// stencil, then run any user <c>page-post-process</c>, and only then derive the
    /// systems list from the pages if nothing has asked for it yet.
    /// </para>
    /// </summary>
    /// <returns>The pages, as a Scheme list.</returns>
    public object Pages()
    {
        if (SchemeUtilities.IsSchemeTrue(_pages) && !(_pages is bool))
        {
            return _pages;
        }

        List<object> pages = new List<object>();
        if (_printBookparts)
        {
            foreach (object p in _printElements)
            {
                if (p is PaperBook bookpart)
                {
                    pages.AddRange(Pair.ToList(bookpart.Pages()));
                }
            }

            _pages = Pair.ListFrom(pages);
        }
        else if (_printElements.Count != 0)
        {
            object pageBreaking = Paper.CVariable("page-breaking");
            _pages = SchemeUtilities.CallCallback(pageBreaking, this);

            object pageStencil = LilyPondScheme.PublicRef(PageModule, "page-stencil");
            foreach (object page in Pair.ToList(_pages))
            {
                SchemeUtilities.CallCallback(pageStencil, page);
            }

            object postProcess = Paper.CVariable("page-post-process");
            if (SchemeUtilities.IsProcedure(postProcess))
            {
                SchemeUtilities.CallCallback(postProcess, Paper, _pages);
            }

            if (_systems is bool flag && !flag)
            {
                List<object> systems = new List<object>();
                foreach (object p in Pair.ToList(_pages))
                {
                    if (p is Prob page)
                    {
                        systems.AddRange(Pair.ToList(page.GetProperty("lines")));
                    }
                }

                _systems = Pair.ListFrom(systems);
            }
        }
        else
        {
            _pages = Nil.Instance;
        }

        return _pages;
    }

    /// <summary>
    /// Sets a page permission on a system spec: on a score's LAST column, or on a markup's
    /// prob directly.
    /// <para>The prebroken piece is set too — without it, the permission is lost exactly
    /// when the column is broken, which is the case it exists for.</para>
    /// </summary>
    private static void SetPagePermission(object sys, Symbol symbol, object permission)
    {
        if (sys is PaperScore ps)
        {
            IReadOnlyList<PaperColumn> cols = ps.GetColumns();
            if (cols.Count != 0)
            {
                PaperColumn col = cols[cols.Count - 1];
                col.SetProperty(symbol, permission);
                Item prebroken = col.FindPrebrokenPiece(Direction.Negative);
                prebroken?.SetProperty(symbol, permission);
            }
        }
        else if (sys is Prob pb)
        {
            pb.SetProperty(symbol, permission);
        }
    }

    /// <summary>
    /// Reads a score block's <c>breakbefore</c> header field and sets up the PRECEDING
    /// system spec to honour it.
    /// </summary>
    private static void SetSystemPenalty(object sys, object header)
    {
        if (!(header is SchemeModule module))
        {
            return;
        }

        Variable force = module.Lookup(BreakBeforeSymbol);
        if (force == null || !force.IsBound || !(force.GetValue() is bool wanted))
        {
            return;
        }

        if (wanted)
        {
            SetPagePermission(sys, PageBreakPermissionSymbol, ForceSymbol);
            SetPagePermission(sys, LineBreakPermissionSymbol, ForceSymbol);
        }
        else
        {
            SetPagePermission(sys, PageBreakPermissionSymbol, Nil.Instance);
        }
    }

    /// <summary>
    /// Attaches bookmarking labels to a system spec: to a score's FIRST column, or to a
    /// markup's prob directly.
    /// </summary>
    private static void SetLabels(object sys, object labels)
    {
        if (sys is PaperScore ps)
        {
            IReadOnlyList<PaperColumn> cols = ps.GetColumns();
            if (cols.Count != 0)
            {
                PaperColumn col = cols[0];
                col.SetProperty("labels", SchemeUtilities.LyAppend(col.GetProperty("labels"), labels));
                Item colRight = col.FindPrebrokenPiece(Direction.Positive);
                if (colRight != null)
                {
                    colRight.SetProperty(
                        "labels", SchemeUtilities.LyAppend(colRight.GetProperty("labels"), labels));
                }
            }
        }
        else if (sys is Prob pb)
        {
            pb.SetProperty("labels", SchemeUtilities.LyAppend(pb.GetProperty("labels"), labels));
        }
    }
}
