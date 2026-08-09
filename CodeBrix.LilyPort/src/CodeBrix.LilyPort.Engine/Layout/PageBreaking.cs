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

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/page-breaking.cc, lily/include/page-breaking.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - upstream's vsize is unsigned and VPOS is its largest value; the port uses int with
//     NoPosition (-1), matching ConstrainedBreaking. Every ordering test that upstream
//     writes against VPOS is written here against NoPosition EXPLICITLY, because -1 sorts
//     BELOW every real index where VPOS sorts above -- see BreakPosition.IsBefore.
//   - System_spec and Break_position are structs upstream and classes here. Neither is
//     mutated after construction, which is what makes that safe: find_chunks_and_breaks
//     stamps forced_line_count_ on a fresh position before pushing it and nothing writes
//     to one afterwards.

/// <summary>
/// One entry in the book's system list: a score, a markup or a header. Exactly one of the
/// two is set; both are unset only for the dummy entry an empty book gets.
/// </summary>
public sealed class SystemSpec
{
    /// <summary>Initializes a spec over a score.</summary>
    /// <param name="paperScore">The score.</param>
    public SystemSpec(PaperScore paperScore)
    {
        PaperScore = paperScore;
        Prob = null;
    }

    /// <summary>Initializes a spec over a markup or header.</summary>
    /// <param name="prob">The markup's property object.</param>
    public SystemSpec(Prob prob)
    {
        Prob = prob;
        PaperScore = null;
    }

    /// <summary>Initializes the dummy spec an empty book gets.</summary>
    public SystemSpec()
    {
        PaperScore = null;
        Prob = null;
    }

    /// <summary>Gets the score, or <see langword="null"/> when this is not one.</summary>
    public PaperScore PaperScore { get; }

    /// <summary>Gets the markup, or <see langword="null"/> when this is not one.</summary>
    public Prob Prob { get; }
}

/// <summary>
/// A place a page break may fall. With N systems there are N+1 of these around them.
/// </summary>
public sealed class BreakPosition
{
    /// <summary>Initializes a break position.</summary>
    /// <param name="systemSpecIndex">
    /// The index into the system list, or <see cref="PageBreaking.NoPosition"/> for the
    /// start of the book.
    /// </param>
    /// <param name="scoreBreak">Which of the score's own page-break points this is.</param>
    /// <param name="column">The broken column, when the spec indexes a score.</param>
    /// <param name="scoreEnder">Whether this position ends its score.</param>
    public BreakPosition(
        int systemSpecIndex = PageBreaking.NoPosition,
        int scoreBreak = PageBreaking.NoPosition,
        Grob column = null,
        bool scoreEnder = false)
    {
        SystemSpecIndex = systemSpecIndex;
        ScoreBreak = scoreBreak;
        Column = column;
        ScoreEnder = scoreEnder;
        ForcedLineCount = 0;
    }

    /// <summary>Gets the index into the system list, or the start-of-book sentinel.</summary>
    public int SystemSpecIndex { get; }

    /// <summary>Gets which of the score's page-break points this is.</summary>
    public int ScoreBreak { get; }

    /// <summary>Gets the broken column, when this position indexes a score.</summary>
    public Grob Column { get; }

    /// <summary>Gets whether this position ends its score.</summary>
    public bool ScoreEnder { get; }

    /// <summary>
    /// Gets or sets the fixed, uncompressed number of lines between this position and the
    /// previous one, or zero when the count is not fixed.
    /// <para>Even in the breaks list this counts from the start of the CHUNK, not from the
    /// previous break — upstream says so in a comment, because
    /// <c>system_count_bounds</c> mixes positions from both lists.</para>
    /// </summary>
    public int ForcedLineCount { get; set; }

    /// <summary>
    /// Upstream's <c>operator&lt;</c>: lexicographic in (spec index, score break), with the
    /// start-of-book sentinel sorting FIRST.
    /// <para>The sentinel case has to be spelled out because upstream's VPOS is the LARGEST
    /// unsigned value and this port's is -1, so the plain numeric comparison that upstream
    /// needs an explicit clause to defeat would come out the other way round here.</para>
    /// </summary>
    /// <param name="other">The position to compare against.</param>
    /// <returns><see langword="true"/> when this sorts before the other.</returns>
    public bool IsBefore(BreakPosition other)
    {
        if (SystemSpecIndex == PageBreaking.NoPosition)
        {
            return other.SystemSpecIndex != PageBreaking.NoPosition;
        }

        if (other.SystemSpecIndex == PageBreaking.NoPosition)
        {
            return false;
        }

        return SystemSpecIndex < other.SystemSpecIndex
            || (SystemSpecIndex == other.SystemSpecIndex && ScoreBreak < other.ScoreBreak);
    }

    /// <summary>Upstream's <c>operator&lt;=</c>.</summary>
    /// <param name="other">The position to compare against.</param>
    /// <returns><see langword="true"/> when this sorts at or before the other.</returns>
    public bool IsNotAfter(BreakPosition other)
    {
        if (SystemSpecIndex == PageBreaking.NoPosition)
        {
            return true;
        }

        if (other.SystemSpecIndex == PageBreaking.NoPosition)
        {
            return false;
        }

        return SystemSpecIndex < other.SystemSpecIndex
            || (SystemSpecIndex == other.SystemSpecIndex && ScoreBreak <= other.ScoreBreak);
    }
}

/// <summary>
/// The shared machinery every page-breaking strategy is built on. Subclasses differ only
/// in <see cref="Solve"/>.
/// <para>
/// Two ideas carry most of the complexity and are worth knowing before reading anything
/// here. COMPRESSED LINES: <c>\noPageBreak</c> is handled once, up front, by concatenating
/// the systems around it into a single line, so that no spacing routine ever has to think
/// about it; the solution is uncompressed again at the end. CHUNKS: the book is divided so
/// that the number of systems in each piece can be varied independently, which is what lets
/// a strategy ask for "N systems" and get every sensible way of distributing them.
/// </para>
/// <para>
/// A warning upstream gives and this port keeps: <see cref="SetCurrentBreakpoints"/> is
/// EXPONENTIALLY SLOW unless it is given bounds. Optimal page breaking passes the previous
/// best division as a lower bound for exactly that reason.
/// </para>
/// </summary>
public abstract class PageBreaking
{
    /// <summary>The sentinel for "no index" — upstream's <c>VPOS</c>.</summary>
    public const int NoPosition = -1;

    private static readonly Symbol ForceSymbol = Symbol.Intern("force");
    private static readonly Symbol MinimumDistanceSymbol = Symbol.Intern("minimum-distance");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");

    private readonly List<BreakPosition> _breaks = new List<BreakPosition>();
    private readonly List<BreakPosition> _chunks = new List<BreakPosition>();
    private readonly List<ConstrainedBreaking> _lineBreaking = new List<ConstrainedBreaking>();
    private readonly List<double> _pageHeightCache = new List<double>();
    private readonly List<double> _lastPageHeightCache = new List<double>();

    private int _systemsPerPage;
    private int _maxSystemsPerPage;
    private int _minSystemsPerPage;
    private int _systemCount;

    private List<List<int>> _currentConfigurations = new List<List<int>>();
    private List<BreakPosition> _currentChunks = new List<BreakPosition>();
    private int _currentStartBreakpoint;
    private int _currentEndBreakpoint;

    private int _cachedConfigurationIndex = NoPosition;
    private List<LineDetails> _cachedLineDetails = new List<LineDetails>();
    private List<LineDetails> _uncompressedLineDetails = new List<LineDetails>();

    /// <summary>
    /// Initializes the breaker over a book, reading every paper-level figure it will need
    /// and then dividing the book into systems, chunks and breaks.
    /// </summary>
    /// <param name="book">The book to break.</param>
    /// <param name="isBreak">
    /// Which columns count as break points, or <see langword="null"/> for none. A grob
    /// predicate rather than a fixed rule because the page-turn breaker and the optimal
    /// breaker want different columns.
    /// </param>
    /// <param name="probIsBreak">Which markups count as break points, or <see langword="null"/>.</param>
    protected PageBreaking(PaperBook book, Func<Grob, bool> isBreak, Func<Prob, bool> probIsBreak)
    {
        Book = book;
        _systemCount = 0;
        PaperHeight = SchemeConvert.ToDouble(book.Paper.CVariable("paper-height"), 1.0);
        Ragged = SchemeUtilities.IsSchemeTrue(book.Paper.CVariable("ragged-bottom"));
        RaggedLast = SchemeUtilities.IsSchemeTrue(book.Paper.CVariable("ragged-last-bottom"));
        _systemsPerPage = Math.Max(0, SchemeConvert.ToInt(book.Paper.CVariable("systems-per-page"), 0));
        _maxSystemsPerPage
            = Math.Max(0, SchemeConvert.ToInt(book.Paper.CVariable("max-systems-per-page"), 0));
        _minSystemsPerPage
            = Math.Max(0, SchemeConvert.ToInt(book.Paper.CVariable("min-systems-per-page"), 0));
        OrphanPenalty = SchemeConvert.ToInt(book.Paper.CVariable("orphan-penalty"), 100000);

        Stencil footnoteSeparator = PageLayoutProblem.GetFootnoteSeparatorStencil(book.Paper);
        FootnoteSeparatorStencilHeight
            = footnoteSeparator.IsEmpty ? 0.0 : footnoteSeparator.Extent(Axis.Y).Length;

        FootnotePadding = SchemeConvert.ToDouble(book.Paper.CVariable("footnote-padding"), 0.0);
        InNotePadding = SchemeConvert.ToDouble(book.Paper.CVariable("in-note-padding"), 0.0);
        InNoteSystemPadding
            = SchemeConvert.ToDouble(book.Paper.CVariable("in-note-system-padding"), 0.0);
        FootnoteFooterPadding
            = SchemeConvert.ToDouble(book.Paper.CVariable("footnote-footer-padding"), 0.0);
        FootnoteNumberRaise
            = SchemeConvert.ToDouble(book.Paper.CVariable("footnote-number-raise"), 0.0);

        if (_systemsPerPage != 0 && (_maxSystemsPerPage != 0 || _minSystemsPerPage != 0))
        {
            Warn.Warning("ignoring min-systems-per-page and max-systems-per-page "
                + "because systems-per-page was set");
            _minSystemsPerPage = _maxSystemsPerPage = 0;
        }

        if (_maxSystemsPerPage != 0 && _minSystemsPerPage > _maxSystemsPerPage)
        {
            Warn.Warning("min-systems-per-page is larger than max-systems-per-page, "
                + "ignoring both values");
            _minSystemsPerPage = _maxSystemsPerPage = 0;
        }

        CreateSystemList();
        FindChunksAndBreaks(isBreak, probIsBreak);
    }

    /// <summary>
    /// Determines the page breaking and breaks the scores into lines to match. This is the
    /// only entry point a caller needs; everything else on this class exists for the
    /// strategies themselves to query the problem.
    /// </summary>
    /// <returns>The list of pages, as Scheme.</returns>
    public abstract object Solve();

    /// <summary>Gets the book being broken.</summary>
    protected PaperBook Book { get; }

    /// <summary>Gets the book's system list.</summary>
    protected List<SystemSpec> SystemSpecs { get; } = new List<SystemSpec>();

    /// <summary>Gets whether the bottom of every page is ragged.</summary>
    public bool Ragged { get; }

    /// <summary>Gets whether the bottom of the LAST page is ragged.</summary>
    public bool RaggedLast { get; }

    /// <summary>Gets the total paper height, margins and header space included.</summary>
    public double PaperHeight { get; }

    /// <summary>Gets the height of the footnote separator's stencil.</summary>
    public double FootnoteSeparatorStencilHeight { get; }

    /// <summary>Gets the padding between footnotes.</summary>
    public double FootnotePadding { get; }

    /// <summary>Gets the padding between in-notes.</summary>
    public double InNotePadding { get; }

    /// <summary>Gets the padding between an in-note and its system.</summary>
    public double InNoteSystemPadding { get; }

    /// <summary>Gets the padding between the footnotes and the footer.</summary>
    public double FootnoteFooterPadding { get; }

    /// <summary>Gets how far a footnote number is raised.</summary>
    public double FootnoteNumberRaise { get; }

    /// <summary>Gets the penalty for a widow or orphan line.</summary>
    public int OrphanPenalty { get; }

    /// <summary>Gets how many systems the current request asked for.</summary>
    public int SystemCount() => _systemCount;

    /// <summary>Gets the fixed systems-per-page setting, or zero when unset.</summary>
    public int SystemsPerPage() => _systemsPerPage;

    /// <summary>Gets the maximum systems per page, which a fixed setting overrides.</summary>
    public int MaxSystemsPerPage() => _systemsPerPage != 0 ? _systemsPerPage : _maxSystemsPerPage;

    /// <summary>Gets the minimum systems per page, which a fixed setting overrides.</summary>
    public int MinSystemsPerPage() => _systemsPerPage != 0 ? _systemsPerPage : _minSystemsPerPage;

    /// <summary>Determines whether a page holds more systems than allowed.</summary>
    /// <param name="lineCount">The page's system count.</param>
    /// <returns><see langword="true"/> when there are too many.</returns>
    public bool TooManyLines(int lineCount)
        => MaxSystemsPerPage() > 0 && lineCount > MaxSystemsPerPage();

    /// <summary>Determines whether a page holds fewer systems than wanted.</summary>
    /// <param name="lineCount">The page's system count.</param>
    /// <returns><see langword="true"/> when there are too few.</returns>
    public bool TooFewLines(int lineCount) => lineCount < MinSystemsPerPage();

    /// <summary>
    /// The penalty for a page's system count. Both directions are charged at
    /// <see cref="PageSpacingPenalties.TerribleSpacing"/> PER SYSTEM out of bounds, so the
    /// solver prefers being one system wrong to being two.
    /// </summary>
    /// <param name="lineCount">The page's system count.</param>
    /// <returns>The penalty.</returns>
    public double LineCountPenalty(int lineCount)
    {
        if (TooManyLines(lineCount))
        {
            return (lineCount - MaxSystemsPerPage()) * PageSpacingPenalties.TerribleSpacing;
        }

        if (TooFewLines(lineCount))
        {
            return (MinSystemsPerPage() - lineCount) * PageSpacingPenalties.TerribleSpacing;
        }

        return 0;
    }

    /// <summary>Classifies a page's system count.</summary>
    /// <param name="lineCount">The page's system count.</param>
    /// <returns>The status flags.</returns>
    public SystemCountStatus LineCountStatus(int lineCount)
    {
        if (TooManyLines(lineCount))
        {
            return SystemCountStatus.TooMany;
        }

        if (TooFewLines(lineCount))
        {
            return SystemCountStatus.TooFew;
        }

        return SystemCountStatus.Ok;
    }

    /// <summary>Gets whether the current end breakpoint is the end of the book.</summary>
    /// <returns><see langword="true"/> when this is the last stretch.</returns>
    public bool IsLast() => _currentEndBreakpoint == LastBreakPosition();

    /// <summary>Gets whether the current end breakpoint ends a score.</summary>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool EndsScore() => _breaks[_currentEndBreakpoint].ScoreEnder;

    /// <summary>Gets the index of the last break position.</summary>
    /// <returns>The index.</returns>
    public int LastBreakPosition() => _breaks.Count - 1;

    /// <summary>
    /// The printable height of a page, cached per page number.
    /// <para>A negative cache entry means "not computed yet", so a genuinely negative
    /// height would be recomputed every time — upstream accepts that and says why: it is
    /// rare enough not to matter.</para>
    /// </summary>
    /// <param name="pageNum">The page number.</param>
    /// <param name="last">Whether this is the bookpart's last page.</param>
    /// <returns>The printable height.</returns>
    public double PageHeight(int pageNum, bool last)
    {
        List<double> cache = last ? _lastPageHeightCache : _pageHeightCache;
        if (pageNum >= 0 && cache.Count > pageNum && cache[pageNum] >= 0)
        {
            return cache[pageNum];
        }

        object page = MakePage(pageNum, last);
        double height = SchemeConvert.ToDouble(
            SchemeUtilities.CallCallback(
                LilyPondScheme.PublicRef(PageModule, "calc-printable-height"), page),
            0.0);

        if (pageNum >= 0)
        {
            while (cache.Count <= pageNum)
            {
                cache.Add(-1);
            }

            cache[pageNum] = height;
        }

        return height;
    }

    /// <summary>
    /// The minimum whitespace between the top of the printable area and the topmost
    /// system's extent box.
    /// </summary>
    /// <param name="line">The topmost line.</param>
    /// <returns>The whitespace.</returns>
    public double MinWhitespaceAtTopOfPage(LineDetails line)
    {
        object firstSystemSpacing = line.IsTitle
            ? Book.Paper.CVariable("top-markup-spacing")
            : Book.Paper.CVariable("top-system-spacing");

        double minDistance = double.NegativeInfinity;
        double padding = 0;

        PageLayoutSpacing.ReadSpacingSpec(firstSystemSpacing, MinimumDistanceSymbol, ref minDistance);
        PageLayoutSpacing.ReadSpacingSpec(firstSystemSpacing, PaddingSymbol, ref padding);

        double translate = Math.Max(line.Shape.Begin[Direction.Positive], line.Shape.Rest[Direction.Positive]);
        return Math.Max(0.0, Math.Max(padding, minDistance - translate));
    }

    /// <summary>
    /// The minimum whitespace between the bottommost system's extent box and the bottom of
    /// the printable area.
    /// </summary>
    /// <param name="line">The bottommost line.</param>
    /// <returns>The whitespace.</returns>
    public double MinWhitespaceAtBottomOfPage(LineDetails line)
    {
        object lastSystemSpacing = Book.Paper.CVariable("last-bottom-spacing");
        double minDistance = double.NegativeInfinity;
        double padding = 0;

        PageLayoutSpacing.ReadSpacingSpec(lastSystemSpacing, MinimumDistanceSymbol, ref minDistance);
        PageLayoutSpacing.ReadSpacingSpec(lastSystemSpacing, PaddingSymbol, ref padding);

        double translate = Math.Min(line.Shape.Begin[Direction.Negative], line.Shape.Rest[Direction.Negative]);
        return Math.Max(0.0, Math.Max(padding, minDistance + translate));
    }

    private static readonly string[] PageModule = { "lily", "page" };
    private static readonly string[] LilyModule = { "lily" };

    private object MakePage(int pageNum, bool last)
        => SchemeUtilities.CallCallback(
            LilyPondScheme.PublicRef(PageModule, "make-page"),
            Book,
            SchemeConvert.FromInt(pageNum),
            last);

    /// <summary>
    /// Turns a break index into the index of the system that starts the next page.
    /// <para>The subtlety is the middle case: when a score OVERFLOWS the previous page the
    /// next page continues the SAME system spec, so the index does not advance.</para>
    /// </summary>
    /// <param name="breakPos">The break position.</param>
    /// <returns>The system index.</returns>
    protected int NextSystem(BreakPosition breakPos)
    {
        int sys = breakPos.SystemSpecIndex;

        if (sys == NoPosition)
        {
            return 0;
        }

        if (SystemSpecs[sys].PaperScore != null && !breakPos.ScoreEnder)
        {
            return sys;
        }

        return sys + 1;
    }

    private void CreateSystemList()
    {
        foreach (object spec in Pair.ToList(Book.GetSystemSpecs()))
        {
            if (spec is PaperScore paperScore)
            {
                SystemSpecs.Add(new SystemSpec(paperScore));
            }
            else if (spec is Prob prob)
            {
                SystemSpecs.Add(new SystemSpec(prob));
            }
        }

        if (SystemSpecs.Count == 0)
        {
            SystemSpecs.Add(new SystemSpec());
        }
    }

    /// <summary>
    /// Divides the book into chunks and breaks.
    /// <para>
    /// The page-turn breaker needs a line breaker between any two columns that permit a
    /// page turn; the optimal breaker needs one between any two columns whose
    /// <c>page-break-permission</c> is <c>force</c>. One predicate accommodates both.
    /// </para>
    /// </summary>
    private void FindChunksAndBreaks(Func<Grob, bool> isBreak, Func<Prob, bool> probIsBreak)
    {
        _chunks.Add(new BreakPosition());
        _breaks.Add(new BreakPosition());

        for (int i = 0; i < SystemSpecs.Count; i++)
        {
            if (SystemSpecs[i].PaperScore != null)
            {
                List<PaperColumn> cols = SystemSpecs[i].PaperScore.RootSystem.UsedColumns();
                List<PaperColumn> forcedLineBreakCols = new List<PaperColumn>();

                object systemCount = SystemSpecs[i].PaperScore.Layout.CVariable("system-count");
                if (SchemeConvert.IsNumber(systemCount))
                {
                    // With system-count given the line configuration is FIXED, so chunk
                    // boundaries may only fall at the line breaks that configuration has.
                    ConstrainedBreaking breaking = new ConstrainedBreaking(SystemSpecs[i].PaperScore);
                    List<LineDetails> details = breaking.GetLineDetails(
                        0, NoPosition, SchemeConvert.ToInt(systemCount, 0));

                    for (int j = 0; j < details.Count; j++)
                    {
                        forcedLineBreakCols.Add(details[j].LastColumn);
                    }
                }

                int lastForcedLineBreakIdx = 0;
                int forcedLineBreakIdx = 0;
                List<int> lineBreakerColumns = new List<int> { 0 };

                for (int j = 0; j < cols.Count; j++)
                {
                    if (forcedLineBreakCols.Count != 0)
                    {
                        if (forcedLineBreakIdx >= forcedLineBreakCols.Count
                            || !ReferenceEquals(forcedLineBreakCols[forcedLineBreakIdx], cols[j]))
                        {
                            continue;
                        }

                        forcedLineBreakIdx++;
                    }

                    bool last = j == cols.Count - 1;
                    bool breakPoint = isBreak != null && j > 0 && isBreak(cols[j]);
                    bool chunkEnd = ReferenceEquals(
                        cols[j].GetProperty("page-break-permission"), ForceSymbol);
                    BreakPosition curPos
                        = new BreakPosition(i, lineBreakerColumns.Count, cols[j], last);

                    if (SchemeConvert.IsNumber(systemCount))
                    {
                        curPos.ForcedLineCount = forcedLineBreakIdx - lastForcedLineBreakIdx;
                    }

                    if (breakPoint || (i == SystemSpecs.Count - 1 && last))
                    {
                        _breaks.Add(curPos);
                    }

                    if (chunkEnd || last)
                    {
                        _chunks.Add(curPos);
                        lastForcedLineBreakIdx = forcedLineBreakIdx;
                    }

                    if ((breakPoint || chunkEnd) && !last)
                    {
                        lineBreakerColumns.Add(j);
                    }
                }

                _lineBreaking.Add(
                    new ConstrainedBreaking(SystemSpecs[i].PaperScore, lineBreakerColumns));
            }
            else if (SystemSpecs[i].Prob != null)
            {
                bool breakPoint = probIsBreak != null && probIsBreak(SystemSpecs[i].Prob);
                if (breakPoint || i == SystemSpecs.Count - 1)
                {
                    _breaks.Add(new BreakPosition(i));
                }

                _chunks.Add(new BreakPosition(i));

                // Upstream pushes a Constrained_breaking over a null score here and notes
                // in a FIXME that a dummy breaker would be tidier. Kept as-is: the entry
                // exists only to keep the list index aligned with the system index, and
                // nothing ever asks it to break anything.
                _lineBreaking.Add(new ConstrainedBreaking(null));
            }
        }
    }

    private List<BreakPosition> ChunkList(int startIndex, int endIndex)
    {
        BreakPosition start = _breaks[startIndex];
        BreakPosition end = _breaks[endIndex];

        int i = 0;
        for (; i < _chunks.Count && _chunks[i].IsNotAfter(start); i++)
        {
        }

        List<BreakPosition> ret = new List<BreakPosition> { start };
        for (; i < _chunks.Count && _chunks[i].IsBefore(end); i++)
        {
            ret.Add(_chunks[i]);
        }

        ret.Add(end);
        return ret;
    }

    /// <summary>Translates break indices into start/end arguments for a line breaker.</summary>
    private void LineBreakerArgs(
        int sys, BreakPosition start, BreakPosition end, out int lineBreakerStart, out int lineBreakerEnd)
    {
        lineBreakerStart = start.SystemSpecIndex == sys ? start.ScoreBreak : 0;
        lineBreakerEnd = end.SystemSpecIndex == sys ? end.ScoreBreak : NoPosition;
    }

    /// <summary>Gets the minimum number of NON-TITLE lines between two breaks.</summary>
    /// <param name="start">The starting break index.</param>
    /// <param name="end">The ending break index.</param>
    /// <returns>The minimum line count.</returns>
    protected int MinSystemCount(int start, int end)
    {
        List<int> div = SystemCountBounds(ChunkList(start, end), true);
        int ret = 0;
        for (int i = 0; i < div.Count; i++)
        {
            ret += div[i];
        }

        return ret;
    }

    /// <summary>Gets the maximum number of NON-TITLE lines between two breaks.</summary>
    /// <param name="start">The starting break index.</param>
    /// <param name="end">The ending break index.</param>
    /// <returns>The maximum line count.</returns>
    protected int MaxSystemCount(int start, int end)
    {
        List<int> div = SystemCountBounds(ChunkList(start, end), false);
        int ret = 0;
        for (int i = 0; i < div.Count; i++)
        {
            ret += div[i];
        }

        return ret;
    }

    private List<int> SystemCountBounds(List<BreakPosition> chunks, bool min)
    {
        List<int> ret = new List<int>();
        for (int i = 0; i + 1 < chunks.Count; i++)
        {
            ret.Add(0);
        }

        for (int i = 0; i + 1 < chunks.Count; i++)
        {
            int sys = NextSystem(chunks[i]);

            if (chunks[i + 1].ForcedLineCount != 0)
            {
                ret[i] = chunks[i + 1].ForcedLineCount;
            }
            else if (SystemSpecs[sys].PaperScore != null)
            {
                LineBreakerArgs(sys, chunks[i], chunks[i + 1], out int start, out int end);
                ret[i] = min
                    ? _lineBreaking[sys].MinSystemCount(start, end)
                    : _lineBreaking[sys].MaxSystemCount(start, end);
            }
        }

        return ret;
    }

    /// <summary>
    /// Asks for a particular number of systems between two breaks, and stores every way of
    /// achieving it.
    /// <para>
    /// Only FIVE configurations are kept, chosen by demerits. Upstream calls the constant
    /// arbitrary and gives the reason for having one at all: without it, a book of many
    /// small scores makes this unusably slow.
    /// </para>
    /// </summary>
    /// <param name="start">The starting break index.</param>
    /// <param name="end">The ending break index.</param>
    /// <param name="systemCount">How many systems to distribute.</param>
    /// <param name="lowerBound">A per-chunk lower bound, or empty to compute one.</param>
    /// <param name="upperBound">A per-chunk upper bound, or empty to compute one.</param>
    protected void SetCurrentBreakpoints(
        int start,
        int end,
        int systemCount,
        List<int> lowerBound = null,
        List<int> upperBound = null)
    {
        _systemCount = systemCount;
        _currentChunks = ChunkList(start, end);
        _currentStartBreakpoint = start;
        _currentEndBreakpoint = end;
        ClearLineDetailsCache();

        if (lowerBound == null || lowerBound.Count == 0)
        {
            lowerBound = SystemCountBounds(_currentChunks, true);
        }

        if (upperBound == null || upperBound.Count == 0)
        {
            upperBound = SystemCountBounds(_currentChunks, false);
        }

        List<int> workInProgress = new List<int>();
        _currentConfigurations.Clear();
        LineDivisionsRec(systemCount, lowerBound, upperBound, workInProgress);

        if (_currentConfigurations.Count > 5)
        {
            List<KeyValuePair<double, int>> demsAndIndices = new List<KeyValuePair<double, int>>();

            for (int i = 0; i < _currentConfigurations.Count; i++)
            {
                CacheLineDetails(i);
                double dem = 0;
                for (int j = 0; j < _cachedLineDetails.Count; j++)
                {
                    dem += (_cachedLineDetails[j].Force * _cachedLineDetails[j].Force)
                        + _cachedLineDetails[j].BreakPenalty;
                }

                demsAndIndices.Add(new KeyValuePair<double, int>(dem, i));
            }

            // std::sort over a pair sorts by demerits and then by INDEX, so ties keep the
            // order the configurations were generated in. A comparison on demerits alone
            // would leave ties to the sort's own discretion and make the chosen five
            // depend on the sort implementation.
            demsAndIndices.Sort((a, b) => a.Key != b.Key
                ? a.Key.CompareTo(b.Key)
                : a.Value.CompareTo(b.Value));

            List<List<int>> best5Configurations = new List<List<int>>();
            for (int i = 0; i < 5; i++)
            {
                best5Configurations.Add(_currentConfigurations[demsAndIndices[i].Value]);
            }

            ClearLineDetailsCache();
            _currentConfigurations = best5Configurations;
        }
    }

    /// <summary>
    /// Asks the line breaker what IT would choose, and takes that as the one configuration.
    /// </summary>
    /// <param name="start">The starting break index.</param>
    /// <param name="end">The ending break index.</param>
    protected void SetToIdealLineConfiguration(int start, int end)
    {
        _currentChunks = ChunkList(start, end);
        _currentStartBreakpoint = start;
        _currentEndBreakpoint = end;
        ClearLineDetailsCache();
        _systemCount = 0;

        List<int> div = new List<int>();
        for (int i = 0; i + 1 < _currentChunks.Count; i++)
        {
            int sys = NextSystem(_currentChunks[i]);

            if (_currentChunks[i + 1].ForcedLineCount != 0)
            {
                div.Add(_currentChunks[i + 1].ForcedLineCount);
            }
            else if (SystemSpecs[sys].PaperScore != null)
            {
                LineBreakerArgs(
                    sys, _currentChunks[i], _currentChunks[i + 1], out int s, out int e);
                div.Add(_lineBreaking[sys].BestSolution(s, e).Count);
            }
            else
            {
                div.Add(0);
            }

            _systemCount += div[div.Count - 1];
        }

        _currentConfigurations.Clear();
        _currentConfigurations.Add(div);
    }

    /// <summary>Gets how many configurations the last request produced.</summary>
    /// <returns>The configuration count.</returns>
    protected int CurrentConfigurationCount() => _currentConfigurations.Count;

    /// <summary>Gets one of the configurations the last request produced.</summary>
    /// <param name="configurationIndex">Which one.</param>
    /// <returns>The per-chunk line counts.</returns>
    protected List<int> CurrentConfiguration(int configurationIndex)
        => _currentConfigurations[configurationIndex];

    private void CacheLineDetails(int configurationIndex)
    {
        if (_cachedConfigurationIndex == configurationIndex)
        {
            return;
        }

        _cachedConfigurationIndex = configurationIndex;

        List<int> div = _currentConfigurations[configurationIndex];
        _uncompressedLineDetails = new List<LineDetails>();
        for (int i = 0; i + 1 < _currentChunks.Count; i++)
        {
            int sys = NextSystem(_currentChunks[i]);
            if (SystemSpecs[sys].PaperScore != null)
            {
                LineBreakerArgs(
                    sys, _currentChunks[i], _currentChunks[i + 1], out int start, out int end);

                // COPIED, not shared. GetLineDetails answers the line breaker's own table
                // entries; upstream copies them into this vector by value, and without the
                // copy CalcLineHeights writes tallness straight back into that table.
                List<LineDetails> details = _lineBreaking[sys].GetLineDetails(start, end, div[i]);
                foreach (LineDetails line in details)
                {
                    _uncompressedLineDetails.Add(line.Copy());
                }
            }
            else
            {
                _uncompressedLineDetails.Add(
                    SystemSpecs[sys].Prob != null
                        ? new LineDetails(SystemSpecs[sys].Prob, Book.Paper)
                        : new LineDetails());
            }
        }

        _cachedLineDetails = CompressLines(_uncompressedLineDetails);
        CalcLineHeights();
    }

    private void ClearLineDetailsCache()
    {
        _cachedConfigurationIndex = NoPosition;
        _cachedLineDetails = new List<LineDetails>();
        _uncompressedLineDetails = new List<LineDetails>();
    }

    private void LineDivisionsRec(
        int systemCount, List<int> minSys, List<int> maxSys, List<int> curDivision)
    {
        int myIndex = curDivision.Count;
        int othersMin = 0;
        int othersMax = 0;

        for (int i = myIndex + 1; i < minSys.Count; i++)
        {
            othersMin += minSys[i];
            othersMax += maxSys[i];
        }

        othersMax = Math.Min(othersMax, systemCount);
        int realMin = Math.Max(minSys[myIndex], systemCount - othersMax);

        // Both of these mean the problem was unsolvable as posed, which upstream asserts
        // can only happen at the top of the recursion; an empty result is the answer.
        if (systemCount < othersMin)
        {
            return;
        }

        int realMax = Math.Min(maxSys[myIndex], systemCount - othersMin);

        if (realMin > realMax)
        {
            return;
        }

        for (int i = realMin; i <= realMax; i++)
        {
            curDivision.Add(i);
            if (myIndex == minSys.Count - 1)
            {
                _currentConfigurations.Add(new List<int>(curDivision));
            }
            else
            {
                LineDivisionsRec(systemCount - i, minSys, maxSys, curDivision);
            }

            curDivision.RemoveAt(curDivision.Count - 1);
        }
    }

    /// <summary>
    /// Computes each line's TALLNESS: how much further down the page it pushes the next one.
    /// <para>
    /// The refpoint hanging position is the y coordinate of the system's ORIGIN, which is
    /// not the same as the top of its extent — that is the refpoint of the first spaceable
    /// staff. Confusing the two puts every system with a non-staff line above it in the
    /// wrong place.
    /// </para>
    /// </summary>
    private void CalcLineHeights()
    {
        double prevHanging = 0;
        double prevHangingBegin = 0;
        double prevHangingRest = 0;
        double prevRefpointHanging = 0;

        for (int i = 0; i < _cachedLineDetails.Count; i++)
        {
            LineDetails cur = _cachedLineDetails[i];
            LineShape shape = cur.Shape;
            double a = shape.Begin[Direction.Positive];
            double b = shape.Rest[Direction.Positive];
            bool title = cur.IsTitle;
            double refpointHanging = Math.Max(prevHangingBegin + a, prevHangingRest + b);

            if (i > 0)
            {
                double padding = 0;
                LineDetails prev = _cachedLineDetails[i - 1];
                if (!cur.TightSpacing)
                {
                    padding = title ? prev.TitlePadding : prev.Padding;
                }

                double minDist = title ? prev.TitleMinDistance : prev.MinDistance;
                refpointHanging = Math.Max(
                    refpointHanging + padding,
                    prevRefpointHanging - prev.RefpointExtent[Direction.Negative]
                        + cur.RefpointExtent[Direction.Positive] + minDist);
            }

            double hangingBegin = refpointHanging - shape.Begin[Direction.Negative];
            double hangingRest = refpointHanging - shape.Rest[Direction.Negative];
            double hanging = Math.Max(hangingBegin, hangingRest);
            cur.Tallness = hanging - prevHanging;
            prevHanging = hanging;
            prevHangingBegin = hangingBegin;
            prevHangingRest = hangingRest;
            prevRefpointHanging = refpointHanging;
        }
    }

    /// <summary>
    /// Concatenates the systems around every forbidden page break into one line, so that no
    /// spacing routine below this point ever meets a <c>\noPageBreak</c>.
    /// </summary>
    private static List<LineDetails> CompressLines(List<LineDetails> orig)
    {
        List<LineDetails> ret = new List<LineDetails>();

        for (int i = 0; i < orig.Count; i++)
        {
            if (ret.Count != 0 && !(ret[ret.Count - 1].PagePermission is Symbol))
            {
                LineDetails old = ret[ret.Count - 1];
                LineDetails compressed = orig[i].Copy();

                // The padding between the lines being merged comes from the UPPER one --
                // and tight-spacing means the padding BEFORE a line is ignored, which is
                // why the test is on the lower line and the value from the upper.
                double padding = 0;
                if (!orig[i].TightSpacing)
                {
                    padding = orig[i].IsTitle ? old.TitlePadding : old.Padding;
                }

                compressed.Shape = old.Shape.Piggyback(orig[i].Shape, padding);

                Interval refpoint = compressed.RefpointExtent;
                refpoint[Direction.Positive] = old.RefpointExtent[Direction.Positive];
                refpoint[Direction.Negative] += compressed.Shape.Rest[Direction.Positive]
                    - old.Shape.Rest[Direction.Positive];
                compressed.RefpointExtent = refpoint;

                compressed.Space += old.Space;
                compressed.InverseHooke += old.InverseHooke;

                compressed.CompressedLinesCount = old.CompressedLinesCount + 1;
                compressed.CompressedNontitleLinesCount = old.CompressedNontitleLinesCount
                    + (compressed.IsTitle ? 0 : 1);

                // The merged line counts as a title exactly when the FIRST of the lines
                // merged into it was one.
                compressed.IsTitle = old.IsTitle;

                compressed.FootnoteHeights.InsertRange(0, old.FootnoteHeights);
                compressed.InNoteHeights.InsertRange(0, old.InNoteHeights);

                ret[ret.Count - 1] = compressed;
            }
            else
            {
                // Copied for the same reason the merged branch builds a new object: the
                // compressed list is written to by CalcLineHeights, and the uncompressed
                // one is read afterwards by FinalizeSpacingResult. Upstream keeps them
                // apart by copying; sharing here would give a line one list's tallness
                // and the other's force.
                ret.Add(orig[i].Copy());
            }
        }

        return ret;
    }

    /// <summary>
    /// Translates a systems-per-page solution over COMPRESSED lines back into one over the
    /// original lines.
    /// </summary>
    private static List<int> UncompressSolution(
        List<int> systemsPerPage, List<LineDetails> compressed)
    {
        List<int> ret = new List<int>();
        int startSys = 0;

        for (int i = 0; i < systemsPerPage.Count; i++)
        {
            int compressedCount = 0;
            for (int j = startSys; j < startSys + systemsPerPage[i]; j++)
            {
                compressedCount += compressed[j].CompressedLinesCount - 1;
            }

            ret.Add(systemsPerPage[i] + compressedCount);
            startSys += systemsPerPage[i];
        }

        return ret;
    }

    /// <summary>Gets the compressed line details for a configuration, computing them if needed.</summary>
    /// <param name="configurationIndex">Which configuration.</param>
    /// <returns>The compressed lines.</returns>
    protected List<LineDetails> CachedLineDetails(int configurationIndex)
    {
        CacheLineDetails(configurationIndex);
        return _cachedLineDetails;
    }

    /// <summary>Breaks the scores into lines according to a chosen configuration.</summary>
    /// <param name="startBreak">The starting break index.</param>
    /// <param name="endBreak">The ending break index.</param>
    /// <param name="div">The per-chunk line counts.</param>
    protected void BreakIntoPieces(int startBreak, int endBreak, List<int> div)
    {
        List<BreakPosition> chunks = ChunkList(startBreak, endBreak);
        bool ignoreDiv = false;
        if (chunks.Count != div.Count + 1)
        {
            Warn.ProgrammingError("did not find a valid page breaking configuration");
            ignoreDiv = true;
        }

        for (int i = 0; i + 1 < chunks.Count; i++)
        {
            int sys = NextSystem(chunks[i]);
            if (SystemSpecs[sys].PaperScore != null)
            {
                LineBreakerArgs(sys, chunks[i], chunks[i + 1], out int start, out int end);

                List<ColumnXPositions> pos = ignoreDiv
                    ? _lineBreaking[sys].BestSolution(start, end)
                    : _lineBreaking[sys].Solve(start, end, div[i]);
                SystemSpecs[sys].PaperScore.RootSystem.BreakIntoPieces(pos);
            }
        }
    }

    /// <summary>
    /// Collects every system of the book, as one flat Scheme list, after running break
    /// substitution on each score.
    /// </summary>
    /// <returns>The systems, as a Scheme list.</returns>
    protected object Systems()
    {
        List<object> all = new List<object>();
        for (int sys = 0; sys < SystemSpecs.Count; sys++)
        {
            if (SystemSpecs[sys].PaperScore != null)
            {
                SystemSpecs[sys].PaperScore.RootSystem.DoBreakSubstitutionAndFixupRefpoints();
                foreach (object line in Pair.ToList(
                    SystemSpecs[sys].PaperScore.RootSystem.GetBrokenSystemGrobs()))
                {
                    all.Add(line);
                }
            }
            else if (SystemSpecs[sys].Prob != null)
            {
                all.Add(SystemSpecs[sys].Prob);
            }
        }

        return Pair.ListFrom(all);
    }

    /// <summary>Reads a property off whatever a break position points at.</summary>
    /// <param name="breakpoint">The break index.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The property value, or the empty list at the start of the book.</returns>
    protected object BreakpointProperty(int breakpoint, string name)
    {
        BreakPosition pos = _breaks[breakpoint];

        if (pos.SystemSpecIndex == NoPosition)
        {
            return Nil.Instance;
        }

        if (SystemSpecs[pos.SystemSpecIndex].PaperScore != null)
        {
            return pos.Column.GetProperty(name);
        }

        return SystemSpecs[pos.SystemSpecIndex].Prob.GetProperty(name);
    }

    /// <summary>
    /// The penalty for leaving a page blank here, read from whichever definition owns the
    /// current end breakpoint.
    /// </summary>
    /// <returns>The penalty.</returns>
    protected double BlankPagePenalty()
    {
        Symbol penaltySym;

        if (IsLast())
        {
            penaltySym = Symbol.Intern("blank-last-page-penalty");
        }
        else if (EndsScore())
        {
            penaltySym = Symbol.Intern("blank-after-score-page-penalty");
        }
        else
        {
            penaltySym = Symbol.Intern("blank-page-penalty");
        }

        BreakPosition pos = _breaks[_currentEndBreakpoint];
        if (pos.SystemSpecIndex != NoPosition
            && SystemSpecs[pos.SystemSpecIndex].PaperScore is PaperScore ps)
        {
            return SchemeConvert.ToDouble(ps.Layout.LookupVariable(penaltySym), 0.0);
        }

        return SchemeConvert.ToDouble(Book.Paper.LookupVariable(penaltySym), 0.0);
    }

    /// <summary>Determines whether every line of a configuration is stretched rather than squashed.</summary>
    /// <param name="configuration">Which configuration.</param>
    /// <returns><see langword="true"/> when no line has negative force.</returns>
    protected bool AllLinesStretched(int configuration)
    {
        CacheLineDetails(configuration);
        for (int i = 0; i < _cachedLineDetails.Count; i++)
        {
            if (_cachedLineDetails[i].Force < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The fewest pages a configuration can possibly occupy.
    /// </summary>
    /// <param name="configuration">Which configuration.</param>
    /// <param name="firstPageNum">The first page number.</param>
    /// <returns>The minimum page count.</returns>
    protected int MinPageCount(int configuration, int firstPageNum)
    {
        int ret = 1;
        int pageStarter = 0;
        double curRodHeight = 0;
        double curSpringHeight = 0;
        double curPageHeight = PageHeight(firstPageNum, false);
        int lineCount = 0;

        CacheLineDetails(configuration);

        if (_cachedLineDetails.Count != 0)
        {
            curPageHeight -= MinWhitespaceAtTopOfPage(_cachedLineDetails[0]);
        }

        for (int i = 0; i < _cachedLineDetails.Count; i++)
        {
            LineDetails cur = _cachedLineDetails[i];
            LineDetails prev = i > 0 ? _cachedLineDetails[i - 1] : null;
            double extLen = curRodHeight > 0 ? cur.Tallness : cur.FullHeight();

            double springLen = i > 0 ? prev.SpringLength(cur) : 0;
            double nextRodHeight = curRodHeight + extLen;
            double nextSpringHeight = curSpringHeight + springLen;
            double nextHeight = nextRodHeight + (Ragged ? nextSpringHeight : 0)
                + MinWhitespaceAtBottomOfPage(cur);
            int nextLineCount = lineCount + cur.CompressedNontitleLinesCount;

            if ((!TooFewLines(lineCount) && nextHeight > curPageHeight && curRodHeight > 0)
                || TooManyLines(nextLineCount)
                || (prev != null && ReferenceEquals(prev.PagePermission, ForceSymbol)))
            {
                lineCount = cur.CompressedNontitleLinesCount;
                curRodHeight = cur.FullHeight();
                curSpringHeight = 0;
                pageStarter = i;

                curPageHeight = PageHeight(firstPageNum + ret, false);
                curPageHeight -= MinWhitespaceAtTopOfPage(cur);

                ret++;
            }
            else
            {
                curRodHeight = nextRodHeight;
                curSpringHeight = nextSpringHeight;
                lineCount = nextLineCount;
            }
        }

        // Two things can go wrong with the LAST page, because it was not known to be the
        // last one while it was being filled: ragged-last may have left a compressed
        // spring on it, and page_height(num, true) may be smaller than page_height(num,
        // false). Either way one more page fixes it, because the last line always fits on
        // a fresh page and the previous page stops being the last.
        if (IsLast() && _cachedLineDetails.Count != 0)
        {
            curPageHeight = PageHeight(firstPageNum + ret - 1, true);
            curPageHeight -= MinWhitespaceAtTopOfPage(_cachedLineDetails[pageStarter]);
            curPageHeight -= MinWhitespaceAtBottomOfPage(
                _cachedLineDetails[_cachedLineDetails.Count - 1]);

            if (!TooFewLines(lineCount
                    - _cachedLineDetails[_cachedLineDetails.Count - 1].CompressedNontitleLinesCount)
                && curRodHeight > curPageHeight
                && curRodHeight > _cachedLineDetails[_cachedLineDetails.Count - 1].FullHeight())
            {
                ret++;
            }
        }

        return ret;
    }

    /// <summary>Spaces a configuration's systems onto exactly N pages.</summary>
    /// <param name="configuration">Which configuration.</param>
    /// <param name="n">The page count.</param>
    /// <param name="firstPageNum">The first page number.</param>
    /// <returns>The spacing result.</returns>
    protected PageSpacingResult SpaceSystemsOnNPages(int configuration, int n, int firstPageNum)
    {
        PageSpacingResult ret;

        if (_systemsPerPage > 0)
        {
            PageSpacingResult fixedResult
                = SpaceSystemsWithFixedNumberPerPage(configuration, firstPageNum);
            fixedResult.Demerits
                += fixedResult.Force.Count == n ? 0 : PageSpacingPenalties.BadSpacing;
            return fixedResult;
        }

        CacheLineDetails(configuration);

        int min = MinPageCount(configuration, firstPageNum);
        int max = _cachedLineDetails.Count;

        bool validN = true;

        if (n < min)
        {
            Warn.Warning("too few pages: " + n + " (should have at least " + min + ")");
            validN = false;
        }

        if (n > max)
        {
            Warn.Warning("too many pages: " + n + " (should have at most " + max + ")");
            validN = false;
        }

        if (n == 1 && validN)
        {
            ret = SpaceSystemsOn1Page(
                _cachedLineDetails,
                PageHeight(firstPageNum, IsLast()),
                Ragged || (IsLast() && RaggedLast));
        }
        else if (n == 2 && validN)
        {
            ret = SpaceSystemsOn2Pages(configuration, firstPageNum);
        }
        else
        {
            PageSpacer ps = new PageSpacer(_cachedLineDetails, firstPageNum, this);
            ret = ps.Solve(n);
        }

        return FinalizeSpacingResult(configuration, ret);
    }

    /// <summary>Spaces a configuration onto N pages or N+1, whichever scores better.</summary>
    /// <param name="configuration">Which configuration.</param>
    /// <param name="n">The smaller page count.</param>
    /// <param name="firstPageNum">The first page number.</param>
    /// <param name="penaltyForFewerPages">What using N rather than N+1 costs.</param>
    /// <returns>The better of the two.</returns>
    protected PageSpacingResult SpaceSystemsOnNOrOneMorePages(
        int configuration, int n, int firstPageNum, double penaltyForFewerPages)
    {
        PageSpacingResult nRes = new PageSpacingResult();
        PageSpacingResult mRes = new PageSpacingResult();

        if (_systemsPerPage > 0)
        {
            PageSpacingResult fixedResult
                = SpaceSystemsWithFixedNumberPerPage(configuration, firstPageNum);
            fixedResult.Demerits += fixedResult.Force.Count == n || fixedResult.Force.Count == n - 1
                ? 0
                : PageSpacingPenalties.BadSpacing;
            return fixedResult;
        }

        CacheLineDetails(configuration);
        int minPCount = MinPageCount(configuration, firstPageNum);
        bool validN = n >= minPCount || n <= _cachedLineDetails.Count;

        if (!validN)
        {
            Warn.ProgrammingError("both page counts are out of bounds");
        }

        if (n == 1 && validN)
        {
            bool rag = Ragged || (IsLast() && RaggedLast);
            double height = PageHeight(firstPageNum, IsLast());

            if (1 >= minPCount)
            {
                nRes = SpaceSystemsOn1Page(_cachedLineDetails, height, rag);
            }

            if (1 < _cachedLineDetails.Count)
            {
                mRes = SpaceSystemsOn2Pages(configuration, firstPageNum);
            }
        }
        else
        {
            PageSpacer ps = new PageSpacer(_cachedLineDetails, firstPageNum, this);

            if (n >= minPCount || !validN)
            {
                nRes = ps.Solve(n);
            }

            if (n < _cachedLineDetails.Count || !validN)
            {
                mRes = ps.Solve(n + 1);
            }
        }

        mRes = FinalizeSpacingResult(configuration, mRes);
        nRes = FinalizeSpacingResult(configuration, nRes);

        double pageSpacingWeight
            = SchemeConvert.ToDouble(Book.Paper.CVariable("page-spacing-weight"), 10);
        nRes.Demerits += penaltyForFewerPages * pageSpacingWeight;

        if (nRes.Force.Count != 0)
        {
            nRes.Force[nRes.Force.Count - 1] += penaltyForFewerPages;
        }

        return mRes.Demerits < nRes.Demerits ? mRes : nRes;
    }

    /// <summary>Spaces a configuration onto whatever page count scores best.</summary>
    /// <param name="configuration">Which configuration.</param>
    /// <param name="firstPageNum">The first page number.</param>
    /// <returns>The spacing result.</returns>
    protected PageSpacingResult SpaceSystemsOnBestPages(int configuration, int firstPageNum)
    {
        if (_systemsPerPage > 0)
        {
            return SpaceSystemsWithFixedNumberPerPage(configuration, firstPageNum);
        }

        CacheLineDetails(configuration);
        PageSpacer ps = new PageSpacer(_cachedLineDetails, firstPageNum, this);

        return FinalizeSpacingResult(configuration, ps.Solve());
    }

    /// <summary>Puts exactly <c>systems-per-page</c> systems on each page.</summary>
    /// <param name="configuration">Which configuration.</param>
    /// <param name="firstPageNum">The first page number.</param>
    /// <returns>The spacing result.</returns>
    protected PageSpacingResult SpaceSystemsWithFixedNumberPerPage(
        int configuration, int firstPageNum)
    {
        PageSpacingResult res = new PageSpacingResult();
        PageSpacing space = new PageSpacing(PageHeight(firstPageNum, false), this);
        int line = 0;
        int pageNum = firstPageNum;
        int pageFirstLine = 0;

        CacheLineDetails(configuration);
        while (line < _cachedLineDetails.Count)
        {
            pageNum++;
            space.Clear();
            space.Resize(PageHeight(pageNum, false));

            int systemCountOnThisPage = 0;
            while (systemCountOnThisPage < _systemsPerPage && line < _cachedLineDetails.Count)
            {
                LineDetails curLine = _cachedLineDetails[line];
                space.AppendSystem(curLine);
                systemCountOnThisPage += curLine.CompressedNontitleLinesCount;
                line++;

                if (ReferenceEquals(curLine.PagePermission, ForceSymbol))
                {
                    break;
                }
            }

            res.SystemsPerPage.Add(line - pageFirstLine);

            res.Force.Add(space.Force);
            res.Penalty += _cachedLineDetails[line - 1].PagePenalty;
            if (systemCountOnThisPage != _systemsPerPage)
            {
                res.Penalty += Math.Abs(systemCountOnThisPage - _systemsPerPage)
                    * PageSpacingPenalties.TerribleSpacing;
                res.SystemCountStatus |= systemCountOnThisPage < _systemsPerPage
                    ? SystemCountStatus.TooFew
                    : SystemCountStatus.TooMany;
            }

            pageFirstLine = line;
        }

        // Recompute the last page's force now that it is known to be the last.
        space.Resize(PageHeight(pageNum, true));
        if (res.Force.Count != 0)
        {
            res.Force[res.Force.Count - 1] = space.Force;
        }

        return FinalizeSpacingResult(configuration, res);
    }

    /// <summary>Packs the systems onto as few pages as they will go.</summary>
    /// <param name="configuration">Which configuration.</param>
    /// <param name="firstPageNum">The first page number.</param>
    /// <returns>The spacing result.</returns>
    protected PageSpacingResult PackSystemsOnLeastPages(int configuration, int firstPageNum)
    {
        PageSpacingResult res = new PageSpacingResult();
        int pageNum = firstPageNum;
        int pageFirstLine = 0;
        PageSpacing space = new PageSpacing(PageHeight(pageNum, false), this);

        CacheLineDetails(configuration);
        for (int line = 0; line < _cachedLineDetails.Count; line++)
        {
            double prevForce = space.Force;
            space.AppendSystem(_cachedLineDetails[line]);
            if (line > pageFirstLine
                && (double.IsInfinity(space.Force)
                    || (line > 0
                        && ReferenceEquals(
                            _cachedLineDetails[line - 1].PagePermission, ForceSymbol))))
            {
                res.SystemsPerPage.Add(line - pageFirstLine);
                res.Force.Add(prevForce);
                res.Penalty += _cachedLineDetails[line - 1].PagePenalty;
                pageNum++;
                space.Resize(PageHeight(pageNum, false));
                space.Clear();
                space.AppendSystem(_cachedLineDetails[line]);
                pageFirstLine = line;
            }

            if (line == _cachedLineDetails.Count - 1)
            {
                // The last page's height was computed before it was known to be the last;
                // if the systems no longer fit, the last one moves to a new page.
                space.Resize(PageHeight(pageNum, true));
                if (line > pageFirstLine && double.IsInfinity(space.Force))
                {
                    res.SystemsPerPage.Add(line - pageFirstLine);
                    res.Force.Add(prevForce);

                    space.Resize(PageHeight(pageNum + 1, true));
                    space.Clear();
                    space.AppendSystem(_cachedLineDetails[line]);
                    res.SystemsPerPage.Add(1);
                    res.Force.Add(space.Force);
                    res.Penalty += _cachedLineDetails[line - 1].PagePenalty;
                    res.Penalty += _cachedLineDetails[line].PagePenalty;
                }
                else
                {
                    res.SystemsPerPage.Add(line + 1 - pageFirstLine);
                    res.Force.Add(space.Force);
                    res.Penalty += _cachedLineDetails[line].PagePenalty;
                }
            }
        }

        return FinalizeSpacingResult(configuration, res);
    }

    /// <summary>
    /// Computes the final demerits and restores the systems-per-page figures to the
    /// UNCOMPRESSED line numbering.
    /// <para>
    /// Page and line forces are SUMMED across pages and not averaged, and upstream records
    /// why after trying the other way: averaging lets the breaker dilute one very bad page
    /// by adding many more pages.
    /// </para>
    /// </summary>
    private PageSpacingResult FinalizeSpacingResult(int configuration, PageSpacingResult res)
    {
        if (res.Force.Count == 0)
        {
            return res;
        }

        CacheLineDetails(configuration);
        List<int> uncompressed = UncompressSolution(res.SystemsPerPage, _cachedLineDetails);
        res.SystemsPerPage.Clear();
        res.SystemsPerPage.AddRange(uncompressed);

        double lineForce = 0;
        double linePenalty = 0;
        double pageDemerits = res.Penalty;
        double pageWeighting
            = SchemeConvert.ToDouble(Book.Paper.CVariable("page-spacing-weight"), 10);

        for (int i = 0; i < _uncompressedLineDetails.Count; i++)
        {
            lineForce += _uncompressedLineDetails[i].Force * _uncompressedLineDetails[i].Force;
            linePenalty += _uncompressedLineDetails[i].BreakPenalty;
        }

        for (int i = Ragged ? res.Force.Count - 1 : 0;
            i < res.Force.Count - (IsLast() && RaggedLast ? 1 : 0);
            i++)
        {
            double f = res.Force[i];
            pageDemerits += Math.Min(f * f, PageSpacingPenalties.BadSpacing);
        }

        res.Demerits = lineForce + linePenalty + (pageDemerits * pageWeighting);
        return res;
    }

    /// <summary>
    /// Spaces a list of lines onto ONE page. Unlike its siblings this takes the lines
    /// directly, because it is also called on SUBSETS of a configuration.
    /// </summary>
    private PageSpacingResult SpaceSystemsOn1Page(
        List<LineDetails> lines, double pageHeight, bool ragged)
    {
        PageSpacing space = new PageSpacing(pageHeight, this);
        PageSpacingResult ret = new PageSpacingResult();
        int lineCount = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            space.AppendSystem(lines[i]);
            lineCount += lines[i].CompressedNontitleLinesCount;
        }

        ret.SystemsPerPage.Add(lines.Count);
        ret.Force.Add(ragged ? Math.Min(space.Force, 0.0) : space.Force);
        ret.Penalty = LineCountPenalty(lineCount)
            + lines[lines.Count - 1].PagePenalty
            + lines[lines.Count - 1].TurnPenalty;
        ret.SystemCountStatus |= LineCountStatus(lineCount);

        // Deliberately NOT finalized: this is an internal helper, and its caller finalizes.
        return ret;
    }

    /// <summary>
    /// Spaces a configuration onto TWO pages. The one-page and two-page cases have O(n)
    /// solutions and are by far the most common, which is why they bypass the solver.
    /// </summary>
    private PageSpacingResult SpaceSystemsOn2Pages(int configuration, int firstPageNum)
    {
        double page1Height = PageHeight(firstPageNum, false);
        double page2Height = PageHeight(firstPageNum + 1, IsLast());
        bool ragged1 = Ragged;
        bool ragged2 = Ragged || (IsLast() && RaggedLast);

        // A forced break reduces this to two one-page problems.
        CacheLineDetails(configuration);
        for (int i = 0; i + 1 < _cachedLineDetails.Count; i++)
        {
            if (ReferenceEquals(_cachedLineDetails[i].PagePermission, ForceSymbol))
            {
                List<LineDetails> lines1 = _cachedLineDetails.GetRange(0, i + 1);
                List<LineDetails> lines2 = _cachedLineDetails.GetRange(
                    i + 1, _cachedLineDetails.Count - i - 1);
                PageSpacingResult p1 = SpaceSystemsOn1Page(lines1, page1Height, ragged1);
                PageSpacingResult p2 = SpaceSystemsOn1Page(lines2, page2Height, ragged2);

                p1.SystemsPerPage.Add(p2.SystemsPerPage[0]);
                p1.Force.Add(p2.Force[0]);
                p1.Penalty += p2.Penalty - _cachedLineDetails[i].TurnPenalty;
                p1.SystemCountStatus |= p2.SystemCountStatus;
                return p1;
            }
        }

        int count = _cachedLineDetails.Count - 1;
        double[] page1Force = new double[count];
        double[] page2Force = new double[count];
        double[] page1Penalty = new double[count];
        double[] page2Penalty = new double[count];
        SystemCountStatus[] page1Status = new SystemCountStatus[count];
        SystemCountStatus[] page2Status = new SystemCountStatus[count];

        for (int i = 0; i < count; i++)
        {
            page1Force[i] = double.PositiveInfinity;
            page2Force[i] = double.PositiveInfinity;
            page1Penalty[i] = double.PositiveInfinity;
            page2Penalty[i] = double.PositiveInfinity;
        }

        PageSpacing page1 = new PageSpacing(page1Height, this);
        PageSpacing page2 = new PageSpacing(page2Height, this);
        int page1LineCount = 0;
        int page2LineCount = 0;

        for (int i = 0; i < count; i++)
        {
            page1.AppendSystem(_cachedLineDetails[i]);
            page2.PrependSystem(_cachedLineDetails[_cachedLineDetails.Count - 1 - i]);
            page1LineCount += _cachedLineDetails[i].CompressedNontitleLinesCount;
            page2LineCount += _cachedLineDetails[_cachedLineDetails.Count - 1 - i]
                .CompressedNontitleLinesCount;

            page1Force[i] = ragged1 && page1.Force < 0 && i > 0
                ? double.PositiveInfinity
                : page1.Force;
            page1Penalty[i] = LineCountPenalty(page1LineCount);
            page1Status[i] = LineCountStatus(page1LineCount);

            if (ragged1)
            {
                page2Force[count - 1 - i]
                    = page2.Force < 0 && i + 1 < count ? double.PositiveInfinity : 0;
            }
            else if (ragged2 && page2.Force > 0)
            {
                page2Force[count - 1 - i] = 0.0;
            }
            else
            {
                page2Force[count - 1 - i] = page2.Force;
            }

            page2Penalty[count - 1 - i] = LineCountPenalty(page2LineCount);
            page2Status[count - 1 - i] = LineCountStatus(page2LineCount);
        }

        int bestSysCount = 1;
        double bestDemerits = double.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            double f = (page1Force[i] * page1Force[i]) + (page2Force[i] * page2Force[i]);

            // min-systems-per-page and max-systems-per-page are SOFT constraints: heavily
            // penalized, never rejected outright.
            double dem = f + page1Penalty[i] + page2Penalty[i]
                + _cachedLineDetails[i + 1].PagePenalty
                + _cachedLineDetails[_cachedLineDetails.Count - 1].PagePenalty
                + _cachedLineDetails[_cachedLineDetails.Count - 1].TurnPenalty;
            if (dem < bestDemerits)
            {
                bestDemerits = dem;
                bestSysCount = i + 1;
            }
        }

        PageSpacingResult ret = new PageSpacingResult();
        ret.SystemsPerPage.Add(bestSysCount);
        ret.SystemsPerPage.Add(_cachedLineDetails.Count - bestSysCount);
        ret.Force.Add(page1Force[bestSysCount - 1]);
        ret.Force.Add(page2Force[bestSysCount - 1]);
        ret.SystemCountStatus = page1Status[bestSysCount - 1] | page2Status[bestSysCount - 1];
        ret.Penalty = _cachedLineDetails[bestSysCount - 1].PagePenalty
            + _cachedLineDetails[_cachedLineDetails.Count - 1].PagePenalty
            + _cachedLineDetails[_cachedLineDetails.Count - 1].TurnPenalty
            + page1Penalty[bestSysCount - 1]
            + page2Penalty[bestSysCount - 1];

        // Deliberately NOT finalized: this is an internal helper.
        return ret;
    }

    /// <summary>
    /// Assembles the pages: lays every system out vertically, then draws.
    /// <para>
    /// The two halves are separated ON PURPOSE and upstream explains why — some grobs look
    /// at their NEIGHBOURS while drawing themselves, so a staff drawn before its neighbours
    /// have been positioned can trigger <c>Align_interface::align_to_ideal_distances</c>
    /// and get a different answer than it should.
    /// </para>
    /// </summary>
    /// <param name="linesPerPage">How many systems go on each page.</param>
    /// <param name="systems">The systems, as a Scheme list.</param>
    /// <returns>The pages, as a Scheme list.</returns>
    protected object MakePages(List<int> linesPerPage, object systems)
    {
        if (systems is Nil)
        {
            return Nil.Instance;
        }

        // The ITERATION ORDER matters for this table: a label straddling a page break must
        // take the LAST page it appears on.
        object labelPageTable = Book.TopPaper().CVariable("label-page-table") ?? Nil.Instance;
        object labelAbsolutePageTable
            = Book.TopPaper().CVariable("label-absolute-page-table") ?? Nil.Instance;
        object labelPageStringTable
            = Book.TopPaper().CVariable("label-page-string-table") ?? Nil.Instance;

        object absoluteCounterScm = Book.TopPaper().CVariable("absolute-page-counter");
        int absolutePageCounter = SchemeConvert.ToInt(absoluteCounterScm, 0);

        int firstPageNumber = SchemeConvert.ToInt(Book.Paper.CVariable("first-page-number"), 1);
        List<object> pages = new List<object>();
        bool resetFootnotesOnNewPage = SchemeUtilities.IsSchemeTrue(
            Book.TopPaper().CVariable("reset-footnotes-on-new-page"));

        int footnoteCount = 0;
        double lastPageForce = 0;

        int pageCount = linesPerPage.Count;
        List<object> remaining = Pair.ToList(systems);
        int consumed = 0;

        for (int i = 0; i < pageCount; i++)
        {
            int pageNum = firstPageNumber + i;
            bool bookpartLastPage = i == pageCount - 1;
            int lineCount = Math.Min(linesPerPage[i], remaining.Count - consumed);
            List<object> lineList = remaining.GetRange(consumed, lineCount);
            object lines = Pair.ListFrom(lineList);

            int rankOnPage = 0;
            foreach (object line in lineList)
            {
                if (line is SystemGrob sys)
                {
                    sys.SetProperty("rank-on-page", SchemeConvert.FromInt(rankOnPage));
                    sys.SetProperty("page-number", SchemeConvert.FromInt(pageNum));
                    rankOnPage++;
                }
            }

            int fnLines = PageLayoutProblem.GetFootnoteCount(lines);
            PageLayoutProblem.AddFootnotesToLines(
                lines, resetFootnotesOnNewPage ? 0 : footnoteCount, Book);

            object page = DrawPage(lines, pageNum, bookpartLastPage, ref lastPageForce);

            object pageNumScm = SchemeConvert.FromInt(pageNum);
            absolutePageCounter++;
            object absolutePageNumScm = SchemeConvert.FromInt(absolutePageCounter);

            object pageNumberType = Book.Paper.CVariable("page-number-type");
            object numberFormat = LilyPondScheme.PublicRef(LilyModule, "number-format");
            object pageString
                = SchemeUtilities.CallCallback(numberFormat, pageNumberType, pageNumScm);

            // Backwards, so the labels come out in the same order as the lines. This
            // matters for PDF bookmarks.
            for (int j = lineList.Count; j-- > 0;)
            {
                object line = lineList[j];
                object labels = Nil.Instance;
                if (line is SystemGrob sys)
                {
                    labels = sys.GetProperty("labels");
                }
                else if (line is Prob prob)
                {
                    labels = prob.GetProperty("labels");
                }

                foreach (object label in Pair.ToList(labels))
                {
                    labelPageTable = new Pair(new Pair(label, pageNumScm), labelPageTable);
                    labelAbsolutePageTable
                        = new Pair(new Pair(label, absolutePageNumScm), labelAbsolutePageTable);
                    labelPageStringTable
                        = new Pair(new Pair(label, pageString), labelPageStringTable);
                }
            }

            pages.Add(page);

            footnoteCount += fnLines;
            consumed += lineCount;
        }

        Book.TopPaper().SetVariable("label-page-table", labelPageTable);
        Book.TopPaper().SetVariable("label-absolute-page-table", labelAbsolutePageTable);
        Book.TopPaper().SetVariable("label-page-string-table", labelPageStringTable);
        Book.TopPaper().SetVariable(
            "absolute-page-counter", SchemeConvert.FromInt(absolutePageCounter));
        return Pair.ListFrom(pages);
    }

    /// <summary>Lays out one page's systems and returns the page.</summary>
    private object DrawPage(object systems, int pageNum, bool last, ref double lastPageForce)
    {
        object page = MakePage(pageNum, last);

        bool rag = Ragged || (last && RaggedLast);
        object config = Nil.Instance;
        PageLayoutProblem layout = new PageLayoutProblem(Book, page, systems);
        if (!(systems is Pair))
        {
            config = Nil.Instance;
        }
        else if (rag && !Ragged)
        {
            // Ragged-last but not ragged: the last page takes the PREVIOUS page's force,
            // so it does not stand out from the ones before it.
            config = layout.FixedForceSolution(lastPageForce);
        }
        else
        {
            config = layout.Solution(rag);
        }

        if ((Ragged && layout.Force < 0.0) || double.IsInfinity(layout.Force))
        {
            Warn.Warning("page " + pageNum + " has been compressed");
        }
        else
        {
            lastPageForce = layout.Force;
        }

        List<object> paperSystems = new List<object>();
        foreach (object s in Pair.ToList(systems))
        {
            object paperSystem = s;
            if (s is SystemGrob sys)
            {
                paperSystem = sys.GetPaperSystem();
            }

            paperSystems.Add(paperSystem);
        }

        Prob p = page as Prob;
        if (p != null)
        {
            p.SetProperty("lines", Pair.ListFrom(paperSystems));
            p.SetProperty("configuration", config);

            Stencil foot = p.GetProperty("foot-stencil") is Stencil footStencil
                ? footStencil
                : Stencil.Empty;

            object footnotes = PageLayoutProblem.GetFootnotesFromLines(systems);
            foot = PageLayoutProblem.AddFootnotesToFooter(footnotes, foot, Book);

            p.SetProperty("foot-stencil", foot);
        }

        return page;
    }
}
