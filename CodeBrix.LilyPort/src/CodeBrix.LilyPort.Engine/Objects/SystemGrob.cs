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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/system.cc, lily/include/system.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/*
  If you keep following offset reference points, you will always end
  up at the root object. This root object is called System, and it
  represents a system (i.e. a line of music).
*/

/// <summary>
/// One line of music, and the root of the grob tree.
/// <para>
/// Named <c>SystemGrob</c>, not <c>System</c>. A class called <c>System</c> in this
/// namespace shadows the ROOT NAMESPACE for every file beside it, so
/// <c>System.Math</c>, <c>System.Collections</c> and <c>System.Numerics</c> all stop
/// resolving — in files that never mention this type. The divergence is recorded in
/// PORT-COVERAGE, along with the same decision for <c>MusicObject</c>.
/// </para>
/// <para>
/// Follow any grob's reference points far enough and you arrive here. A system owns
/// every grob on its line through <c>all-elements</c>, and the paper columns that fix
/// their horizontal positions through <c>columns</c>.
/// </para>
/// <para>
/// Before line breaking there is exactly ONE system holding the whole score; breaking
/// clones it into one system per line. That is why <see cref="Rank"/> exists and why
/// a column records which system it ended up on.
/// </para>
/// </summary>
public class SystemGrob : Spanner
{
    private static readonly Symbol SystemInterface = Symbol.Intern("system-interface");
    private static readonly Symbol AllElementsSymbol = Symbol.Intern("all-elements");
    private static readonly Symbol ColumnsSymbol = Symbol.Intern("columns");
    private static readonly Symbol LayerSymbol = Symbol.Intern("layer");
    private static readonly Symbol ExtraOffsetSymbol = Symbol.Intern("extra-offset");
    private static readonly Symbol CombineStencilSymbol = Symbol.Intern("combine-stencil");
    private static readonly Symbol LineBreakSystemDetailsSymbol
        = Symbol.Intern("line-break-system-details");

    private static readonly Symbol VerticalSkylinesSymbol = Symbol.Intern("vertical-skylines");
    private static readonly Symbol PageBreakPermissionSymbol
        = Symbol.Intern("page-break-permission");

    private static readonly Symbol PageTurnPermissionSymbol
        = Symbol.Intern("page-turn-permission");

    private static readonly Symbol PageBreakPenaltySymbol = Symbol.Intern("page-break-penalty");
    private static readonly Symbol PageTurnPenaltySymbol = Symbol.Intern("page-turn-penalty");
    private static readonly Symbol LastInScoreSymbol = Symbol.Intern("last-in-score");
    private static readonly Symbol SystemGrobSymbol = Symbol.Intern("system-grob");
    private static readonly Symbol StaffRefpointExtentSymbol
        = Symbol.Intern("staff-refpoint-extent");
    private static readonly Symbol BeforeLineBreakingSymbol
        = Symbol.Intern("before-line-breaking");

    private static readonly Symbol SpringsAndRodsSymbol = Symbol.Intern("springs-and-rods");
    private static readonly Symbol AfterLineBreakingSymbol
        = Symbol.Intern("after-line-breaking");
    private static readonly Symbol PureYExtentSymbol = Symbol.Intern("pure-Y-extent");
    private static readonly Symbol LabelsSymbol = Symbol.Intern("labels");
    private static readonly Symbol VerticalAlignmentSymbol
        = Symbol.Intern("vertical-alignment");

    // NOT A TYPO HERE — IT IS UPSTREAM'S, AND IT IS REPRODUCED ON PURPOSE (rule 2).
    // lily/system.cc reads the alignment object at seven places and spells it
    // `vertical-alignment' at six of them; `get_maybe_spaceable_staves' (system.cc:1023)
    // is the seventh and spells it `vertical_alignment', with an UNDERSCORE. Nothing
    // declares or writes that name -- scm/define-grob-properties.scm declares
    // `vertical-alignment' and scm/define-grobs.scm gives System the
    // `ly:system::get-vertical-alignment' callback under the hyphen -- so the read finds
    // nothing, `align' is null, and ALL THREE `ly:system::get-*-staves' callbacks answer
    // the EMPTY LIST for every system LilyPond has ever engraved.
    // Its one visible consequence is that `annotate-spacing' draws no staff-level
    // annotation: scm/paper-system.scm guards both maps with `(< 1 (length ...))'.
    // MEASURED, not inferred -- the pinned oracle draws ZERO `padding' annotations
    // across all 2,316 reference pages. See PORT-COVERAGE, PARITY 17.
    private static readonly Symbol UpstreamMisspelledVerticalAlignmentSymbol
        = Symbol.Intern("vertical_alignment");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol AxisGroupInterfaceSymbol
        = Symbol.Intern("axis-group-interface");

    /// <summary>Initializes a system from its type's basic property alist.</summary>
    /// <param name="basicProperties">The immutable alist for this grob type.</param>
    public SystemGrob(object basicProperties)
        : base(basicProperties)
    {
        InitElements();
        AddInterface(SystemInterface);
    }

    /// <summary>Initializes a copy of another system.</summary>
    /// <param name="source">The system to copy.</param>
    protected SystemGrob(SystemGrob source)
        : base(source)
    {
        // A broken piece starts with an EMPTY element list of its own: the grobs are
        // handed to it by break substitution, not inherited wholesale.
        InitElements();
    }

    /// <summary>Gets the C++ class name this grob corresponds to.</summary>
    public override string ClassName => "System";

    /// <summary>Gets or sets which line this system is, counting from zero.</summary>
    public int Rank { get; set; }

    /// <summary>Gets or sets the paper score this system belongs to.</summary>
    public Layout.PaperScore PaperScore { get; set; }

    /// <summary>Gets the system this one was broken off from.</summary>
    public new SystemGrob Original => (SystemGrob)base.Original;

    /// <summary>Returns an independent copy of this system.</summary>
    /// <returns>The clone.</returns>
    public override Grob Clone() => new SystemGrob(this);

    /// <summary>Gets every grob on this system.</summary>
    public GrobArray AllElements => PointerGroupInterface.GetGrobArray(this, AllElementsSymbol);

    /// <summary>Gets the number of grobs on this system.</summary>
    public int ElementCount => AllElements.Count;

    /// <summary>Gets the number of spanners on this system.</summary>
    public int SpannerCount
    {
        get
        {
            int count = 0;
            foreach (Grob grob in AllElements)
            {
                if (grob is Spanner)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Gets the paper columns on this system, in rank order.</summary>
    public IReadOnlyList<Grob> Columns
        => PointerGroupInterface.ExtractGrobSet(this, ColumnsSymbol);

    /// <summary>
    /// Takes ownership of a grob: it joins this system's element list and is laid out
    /// under this system's output definition.
    /// </summary>
    /// <param name="grob">The grob to adopt.</param>
    public void TypesetGrob(Grob grob)
    {
        if (grob == null)
        {
            return;
        }

        if (grob.Layout != null)
        {
            Warn.ProgrammingError("adding element twice");
            return;
        }

        grob.Layout = Layout;
        AllElements.Add(grob);
    }

    /// <summary>
    /// Appends a paper column, giving it the next rank and making this system its
    /// axis-group parent.
    /// </summary>
    /// <param name="column">The column to add.</param>
    public void AddColumn(PaperColumn column)
    {
        GrobArray columns = PointerGroupInterface.GetGrobArray(this, ColumnsSymbol);
        column.Rank = columns.Count;
        columns.Add(column);
        AxisGroupInterface.AddElement(this, column);
    }

    /// <summary>Returns the column at a rank, or null when out of range.</summary>
    /// <param name="index">The rank.</param>
    /// <returns>The column.</returns>
    public PaperColumn Column(int index)
    {
        IReadOnlyList<Grob> columns = Columns;
        return index >= 0 && index < columns.Count ? columns[index] as PaperColumn : null;
    }

    /// <summary>
    /// Returns the columns in a right-open rank range, dropping unused ones.
    /// <para>
    /// The range is also truncated at the LAST BREAKABLE column: anything after it
    /// cannot start a line, so including it would only enlarge the spacing problem.
    /// </para>
    /// </summary>
    /// <param name="start">The first rank to consider.</param>
    /// <param name="end">One past the last rank to consider.</param>
    /// <returns>The usable columns.</returns>
    public List<PaperColumn> UsedColumnsInRange(int start, int end)
    {
        IReadOnlyList<Grob> all = Columns;

        int lastBreakable = all.Count;
        while (lastBreakable-- > 0)
        {
            if (all[lastBreakable] is PaperColumn candidate && PaperColumn.IsBreakable(candidate))
            {
                break;
            }
        }

        if (end > lastBreakable + 1)
        {
            end = lastBreakable + 1;
        }

        List<PaperColumn> columns = new List<PaperColumn>();
        for (int i = Math.Max(start, 0); i < end && i < all.Count; i++)
        {
            if (all[i] is PaperColumn column && PaperColumn.IsUsed(column))
            {
                columns.Add(column);
            }
        }

        return columns;
    }

    /// <summary>Returns every usable column on this system.</summary>
    /// <returns>The usable columns.</returns>
    public List<PaperColumn> UsedColumns() => UsedColumnsInRange(0, int.MaxValue);

    /// <summary>
    /// Returns the breakable columns strictly between two items that have not yet been
    /// assigned to a system — the candidate break points in that span.
    /// </summary>
    /// <param name="leftItem">The item bounding the range on the left.</param>
    /// <param name="rightItem">The item bounding the range on the right.</param>
    /// <returns>The candidate columns.</returns>
    public List<PaperColumn> BrokenColumnRange(Item leftItem, Item rightItem)
    {
        List<PaperColumn> result = new List<PaperColumn>();

        PaperColumn leftColumn = ColumnOf(leftItem);
        PaperColumn rightColumn = ColumnOf(rightItem);
        if (leftColumn == null || rightColumn == null)
        {
            return result;
        }

        IReadOnlyList<Grob> columns = Columns;
        int endRank = Math.Min(rightColumn.Rank, columns.Count);

        for (int i = leftColumn.Rank + 1; i < endRank; i++)
        {
            if (columns[i] is PaperColumn column
                && PaperColumn.IsBreakable(column)
                && column.System == null)
            {
                result.Add(column);
            }
        }

        return result;
    }

    /// <summary>
    /// Refuses a plain item as a bound. A system spans from one COLUMN to another, never
    /// from an arbitrary item.
    /// </summary>
    /// <param name="item">The candidate bound.</param>
    /// <returns><see langword="false"/>, always.</returns>
    /// <remarks>
    /// //was previously: an override of <see cref="Spanner.SetBound"/> that tested
    /// <c>grob is PaperColumn</c> and reported "system bound must be a paper column".
    /// The test was right and the mechanism was not: upstream expresses the same rule
    /// through this pair of acceptance predicates, so the refusal travels through
    /// <c>Spanner::set_bound</c>'s own dispatch and reports upstream's wording,
    /// "cannot set X as bound of Y" (ruling R1, rule 15). Routing it through the base
    /// also means a refusal can no longer skip the <c>bounded-by-me</c> handshake.
    /// </remarks>
    public override bool AcceptsAsBoundItem(Item item) => false;

    /// <summary>Accepts a paper column as a bound.</summary>
    /// <param name="column">The candidate bound.</param>
    /// <returns><see langword="true"/>, always.</returns>
    public override bool AcceptsAsBoundPaperColumn(PaperColumn column) => true;

    /// <summary>Returns the paper column bounding this system on one side.</summary>
    /// <param name="direction">Which end.</param>
    /// <returns>The bounding column.</returns>
    public new PaperColumn GetBound(Direction direction) => base.GetBound(direction) as PaperColumn;

    /// <summary>
    /// Prepares the system for line breaking: every breakable item is prebroken, then
    /// every grob is asked for its <c>before-line-breaking</c> and
    /// <c>springs-and-rods</c> properties.
    /// <para>
    /// Those last two reads ARE the calls. Nothing invokes the spacing spanner directly;
    /// its <c>springs-and-rods</c> property resolves to
    /// <c>ly:spacing-spanner::set-springs</c>, so reading the property is what states the
    /// entire horizontal spacing problem. A grob whose callback is never reached simply
    /// contributes nothing, silently — which is why this loop covers every element
    /// rather than a list of the ones known to care.
    /// </para>
    /// <para>
    /// The prebreak iteration is bounded by the ORIGINAL count on purpose: breaking
    /// appends the clones to the same list, and the clones must not themselves be broken.
    /// </para>
    /// <para>
    /// <c>handle_prebroken_dependencies</c> runs between the two halves, as upstream
    /// does. The iterator group landed it after the earlier note here — that it "only
    /// matters once lines are actually broken" — was DISPROVEN by measurement: a clone
    /// starts with an empty object alist, and <c>ly:span-bar::before-line-breaking</c>
    /// reads a SpanBar's <c>elements</c> with no default, so 87 files died in this very
    /// method. Its substitution half is <see cref="BreakSubstitution"/>.
    /// </para>
    /// <para>
    /// The <c>fixup_refpoint</c> pass is the third step and is NOT optional, though a
    /// recorded divergence used to say it "only matters once lines are actually broken".
    /// That claim was wrong on its own terms and the line-breaking work removed it: the pass runs
    /// immediately after the PREBREAK clones are made, and its second job — re-pointing
    /// an item whose parent is an item with a different break direction — is entirely
    /// about prebroken pieces, which exist well before any line is chosen. It is the
    /// same shape of mistake `handle_prebroken_dependencies' carried when it first landed.
    /// </para>
    /// </summary>
    public void PreProcessing()
    {
        GrobArray all = AllElements;

        int originalCount = all.Count;
        for (int i = 0; i < originalCount; i++)
        {
            BreakBreakableItem(all[i]);
        }

        // Order is significant: the broken pieces were appended above and are handled
        // BEFORE the originals they came from, because an original may kill itself while
        // answering. This BACKWARD walk is upstream's, and it belongs to this pass only.
        for (int i = all.Count; i-- > 0;)
        {
            all[i].HandlePrebrokenDependencies();
        }

        foreach (Grob grob in all)
        {
            grob.FixupRefpoint();
        }

        GetProperty(BeforeLineBreakingSymbol);
        foreach (Grob grob in all)
        {
            grob.GetProperty(BeforeLineBreakingSymbol);
        }

        GetProperty(SpringsAndRodsSymbol);
        foreach (Grob grob in all)
        {
            grob.GetProperty(SpringsAndRodsSymbol);
        }
    }

    /// <summary>
    /// Clones this root system once per line the breaker chose, and moves each line's
    /// columns to where that line's solution puts them —
    /// <c>System::break_into_pieces</c>.
    /// <para>
    /// This is the moment the score stops being one long line. Each piece is bounded by
    /// its first and last column, is given the PURE vertical extent computed for its
    /// column range (page layout reads that before the real extents exist), collects the
    /// <c>labels</c> of every column on it — including the LOOSE ones, which is where a
    /// mid-line mark on an otherwise unused column would otherwise be lost — and gets
    /// its loose columns draped back around the solved ones.
    /// </para>
    /// </summary>
    /// <param name="breaking">One solved configuration per line.</param>
    public void BreakIntoPieces(IReadOnlyList<CodeBrix.LilyPort.Engine.Layout.ColumnXPositions> breaking)
    {
        if (breaking == null)
        {
            throw new System.ArgumentNullException(nameof(breaking));
        }

        for (int i = 0; i < breaking.Count; i++)
        {
            SystemGrob system = (SystemGrob)Clone();

            // set rank
            system.Rank = BrokenIntos.Count;

            List<PaperColumn> c = breaking[i].Columns;
            if (c.Count == 0)
            {
                continue;
            }

            PaperScore.TypesetSystem(system);

            int st = c[0].Rank;
            int end = c[c.Count - 1].Rank;
            Interval iv = PureYExtent(this, st, end);
            system.SetProperty(PureYExtentSymbol, new Pair(iv.Left, iv.Right));

            system.SetBound(Direction.Negative, c[0]);
            system.SetBound(Direction.Positive, c[c.Count - 1]);

            object systemLabels = Nil.Instance;
            for (int j = 0; j < c.Count; j++)
            {
                if (j < breaking[i].Configuration.Count)
                {
                    c[j].TranslateAxis(breaking[i].Configuration[j], Axis.X);
                }

                c[j].System = system;

                /* collect the column labels */
                CollectLabels(c[j], ref systemLabels);
            }

            /*
              Collect labels from any loose columns too: these will be set on
              an empty bar line or a column which is otherwise unused mid-line
            */
            List<PaperColumn> loose = breaking[i].LooseColumns;
            for (int j = 0; j < loose.Count; j++)
            {
                CollectLabels(loose[j], ref systemLabels);
            }

            system.SetProperty(LabelsSymbol, systemLabels);

            CodeBrix.LilyPort.Engine.Layout.LooseColumns.SetLooseColumns(system, breaking[i]);
            BrokenIntos.Add(system);
        }
    }

    /// <summary>
    /// Makes the broken systems independent of each other: every internal link is
    /// re-pointed at the piece living on the same system, and every parent likewise —
    /// <c>System::do_break_substitution_and_fixup_refpoints</c>.
    /// <para>
    /// The order is upstream's and each step depends on the one before. Grobs break
    /// themselves into pieces first. Refpoints are fixed in the BROKEN systems before the
    /// root, because that is where the new elements were put. Only then is break
    /// substitution run, and the root system's own last.
    /// </para>
    /// <para>
    /// The duplicate removal at the end is not tidying: <c>all-elements</c> holds items in
    /// three versions (the original plus two prebroken pieces), so substitution leaves
    /// duplicates behind, and a duplicate becomes a DUPLICATED SYMBOL in the output.
    /// </para>
    /// </summary>
    public void DoBreakSubstitutionAndFixupRefpoints()
    {
        GrobArray allElts = AllElements;
        List<Grob> snapshot = new List<Grob>();
        foreach (Grob g in allElts)
        {
            snapshot.Add(g);
        }

        foreach (Grob g in snapshot)
        {
            g.DoBreakProcessing();
        }

        /*
          fixups must be done in broken line_of_scores, because new elements
          are put over there.
        */
        foreach (SystemGrob child in BrokenSystems())
        {
            GrobArray childElts = child.AllElements;
            foreach (Grob g in ToList(childElts))
            {
                g.FixupRefpoint();
            }
        }

        /*
          needed for doing items.
        */
        foreach (Grob g in ToList(allElts))
        {
            g.FixupRefpoint();
        }

        foreach (Grob g in ToList(allElts))
        {
            g.HandleBrokenDependencies();
        }

        HandleBrokenDependencies();

        /* Because the get_property (all-elements) contains items in 3
           versions, HandleBrokenDependencies () will leave duplicated
           items in all-elements. Strictly speaking this is harmless, but
           it leads to duplicated symbols in the output. RemoveDuplicates makes
           sure that no duplicates are in the list. */
        foreach (SystemGrob child in BrokenSystems())
        {
            GrobArray childElts = child.AllElements;
            childElts.RemoveDuplicates();
            child.GetProperty(AfterLineBreakingSymbol);
            foreach (Grob g in ToList(childElts))
            {
                g.GetProperty(AfterLineBreakingSymbol);
            }
        }
    }

    /// <summary>
    /// Gets the systems this one was broken into, one per line, typed as systems.
    /// <para>
    /// A VIEW over the inherited <see cref="Spanner.BrokenIntos"/>, NOT a second list.
    /// Declaring a shadowing <c>new</c> list here is a defect with a very long fuse: the
    /// breaker fills one list while every inherited Spanner method — <c>FindBrokenPiece</c>,
    /// <c>IsBroken</c>, <c>SpannedSystemRankInterval</c>,
    /// <c>SubstituteOneMutableProperty</c> — reads the other, empty one, so line breaking
    /// appears to work and break substitution silently re-points nothing.
    /// </para>
    /// </summary>
    /// <returns>The broken systems, in line order.</returns>
    public List<SystemGrob> BrokenSystems()
    {
        List<SystemGrob> lines = new List<SystemGrob>(BrokenIntos.Count);
        foreach (Spanner piece in BrokenIntos)
        {
            if (piece is SystemGrob line)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    /// <summary>
    /// Returns the broken systems as a Scheme list — <c>ly:system-print</c>'s and the
    /// page breaker's way in.
    /// </summary>
    /// <returns>The broken systems, in line order.</returns>
    public object GetBrokenSystemGrobs()
    {
        object ret = Nil.Instance;
        for (int i = BrokenIntos.Count; i-- > 0;)
        {
            ret = new Pair(BrokenIntos[i], ret);
        }

        return ret;
    }

    /// <summary>
    /// Returns one <c>paper-system</c> prob per broken line —
    /// <c>System::get_paper_systems</c> (plural).
    /// </summary>
    /// <returns>The paper systems, in line order.</returns>
    public List<Prob> GetPaperSystemsPerLine()
    {
        List<Prob> lines = new List<Prob>();
        for (int i = 0; i < BrokenIntos.Count; i++)
        {
            lines.Add(BrokenSystems()[i].GetPaperSystem());
        }

        return lines;
    }

    /// <summary>
    /// Appends a column's <c>labels</c> to a line's — <c>System::collect_labels</c>.
    /// </summary>
    /// <param name="col">The column to read.</param>
    /// <param name="labels">The list being built.</param>
    private static void CollectLabels(Grob col, ref object labels)
    {
        object colLabels = col.GetProperty(LabelsSymbol);
        if (colLabels is Pair)
        {
            labels = SchemeUtilities.LyAppend(colLabels, labels);
        }
    }

    /// <summary>
    /// The vertical span between the first and last SPACEABLE staff of a line —
    /// <c>System::pure_refpoint_extent</c>.
    /// <para>
    /// Page layout measures the distance between lines from these reference points rather
    /// than from the outer edges, so that a line with a high note on top does not push the
    /// staves apart.
    /// </para>
    /// </summary>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The refpoint extent.</returns>
    public Interval PureRefpointExtent(int start, int end)
    {
        Interval ret = Interval.Empty;
        Grob alignment = GetObject(VerticalAlignmentSymbol) as Grob;
        if (alignment == null)
        {
            return Interval.Empty;
        }

        IReadOnlyList<Grob> staves = PointerGroupInterface.ExtractGrobSet(alignment, ElementsSymbol);
        List<double> offsets = AlignInterface.GetPureMinimumTranslations(
            alignment, staves, Axis.Y, start, end);

        for (int i = 0; i < offsets.Count; ++i)
        {
            if (i < staves.Count && CodeBrix.LilyPort.Engine.Layout.PageLayoutSpacing.IsSpaceable(staves[i]))
            {
                ret.Right = offsets[i];
                break;
            }
        }

        for (int i = offsets.Count; i-- > 0;)
        {
            if (i < staves.Count && CodeBrix.LilyPort.Engine.Layout.PageLayoutSpacing.IsSpaceable(staves[i]))
            {
                ret.Left = offsets[i];
                break;
            }
        }

        return ret;
    }

    /// <summary>
    /// Which of a system's staves the page spacer may space, or may not, or all of them.
    /// <para>
    /// The three <c>ly:system::get-*-staves</c> callbacks are one upstream function under
    /// a filter, and they exist for <c>annotate-spacing</c>: <c>scm/paper-system.scm</c>
    /// asks for them by name and immediately takes their <c>length</c>. Unported, they
    /// answered the inert placeholder, and `\paper { annotate-spacing = ##t }' — two
    /// words in a file — took the whole book down with "Not a proper list", naming
    /// neither the property nor the callback. Ported with the page-layout group.
    /// </para>
    /// <para>
    /// A DEAD stave is skipped, which is not tidiness: hara-kiri suicides empty staves,
    /// and an annotation drawn against one would be measured from a grob with no extent.
    /// </para>
    /// <para>
    /// ⚠ THE ALIGNMENT IS LOOKED UP UNDER UPSTREAM'S MISSPELLED NAME, DELIBERATELY, so
    /// this function answers the EMPTY LIST exactly as upstream's does. See
    /// <see cref="UpstreamMisspelledVerticalAlignmentSymbol"/> for the account. The body
    /// below is otherwise a faithful translation and is kept whole rather than folded
    /// away, because the misspelling is upstream's to fix: the day it is corrected there,
    /// correcting the one symbol here restores the whole function.
    /// </para>
    /// </summary>
    /// <param name="filter">Which staves to keep.</param>
    /// <returns>The staves, as a Scheme list.</returns>
    public object GetMaybeSpaceableStaves(StaffFilter filter)
    {
        List<object> kept = new List<object>();
        if (GetObject(UpstreamMisspelledVerticalAlignmentSymbol) is Grob alignment)
        {
            foreach (Grob stave in PointerGroupInterface.ExtractGrobSet(alignment, ElementsSymbol))
            {
                bool spaceable = CodeBrix.LilyPort.Engine.Layout.PageLayoutSpacing.IsSpaceable(stave);
                if (stave.IsLive
                    && (filter == StaffFilter.All
                        || (filter == StaffFilter.Spaceable && spaceable)
                        || (filter == StaffFilter.NonSpaceable && !spaceable)))
                {
                    kept.Add(stave);
                }
            }
        }

        return Pair.ListFrom(kept);
    }

    /// <summary>
    /// The PURE vertical extent of one part of a line — the beginning of it, or the rest.
    /// <para>
    /// The split exists because the start of a line carries things nothing else does — a
    /// clef, a key signature, an instrument name — so a line is taller at its left edge
    /// than across its body, and the line breaker's <c>Line_shape</c> models exactly that
    /// two-part silhouette.
    /// </para>
    /// </summary>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <param name="begin">Whether the beginning of the line is wanted.</param>
    /// <returns>The extent.</returns>
    public Interval PartOfLinePureHeight(int start, int end, bool begin)
    {
        Grob alignment = GetObject(VerticalAlignmentSymbol) as Grob;
        if (alignment == null)
        {
            return Interval.Empty;
        }

        IReadOnlyList<Grob> staves = PointerGroupInterface.ExtractGrobSet(alignment, ElementsSymbol);
        List<double> offsets = AlignInterface.GetPureMinimumTranslations(
            alignment, staves, Axis.Y, start, end);

        Interval ret = Interval.Empty;
        for (int i = 0; i < staves.Count; ++i)
        {
            Interval iv = begin
                ? AxisGroupInterfacePure.BeginOfLinePureHeight(staves[i], start)
                : AxisGroupInterfacePure.RestOfLinePureHeight(staves[i], start, end);
            if (i < offsets.Count)
            {
                iv.Translate(offsets[i]);
            }

            ret.Unite(iv);
        }

        Interval otherElements = begin
            ? AxisGroupInterfacePure.BeginOfLinePureHeight(this, start)
            : AxisGroupInterfacePure.RestOfLinePureHeight(this, start, end);

        ret.Unite(otherElements);

        return ret;
    }

    /// <summary>
    /// The few elements of a system whose pure heights matter DIRECTLY —
    /// <c>ly:system::calc-pure-relevant-grobs</c>.
    /// <para>
    /// This differs from the axis-group version and upstream says why: here we want only
    /// the elements that are NOT descended from the VerticalAlignment — a RehearsalMark, a
    /// BarLine — because everything under the alignment is measured through the staves
    /// instead. Prebroken item clones are skipped for the same reason they are elsewhere:
    /// the caller asks for the right clone when it needs one.
    /// </para>
    /// </summary>
    /// <param name="me">The system.</param>
    /// <returns>The relevant grobs.</returns>
    public static object CalcPureRelevantGrobs(Grob me)
    {
        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        List<Grob> relevantGrobs = new List<Grob>();

        for (int i = 0; i < elts.Count; ++i)
        {
            if (!elts[i].HasInterface(AxisGroupInterfaceSymbol))
            {
                if (elts[i] is Item it && it.Original != null)
                {
                    continue;
                }

                relevantGrobs.Add(elts[i]);
            }
        }

        GrobArray grobs = new GrobArray();
        grobs.SetArray(relevantGrobs);
        return grobs;
    }

    /// <summary>The pure vertical extent of the START of a line.</summary>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The extent.</returns>
    public Interval BeginOfLinePureHeight(int start, int end)
        => PartOfLinePureHeight(start, end, true);

    /// <summary>The pure vertical extent of the REST of a line.</summary>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The extent.</returns>
    public Interval RestOfLinePureHeight(int start, int end)
        => PartOfLinePureHeight(start, end, false);

    /// <summary>
    /// Returns the column that would bound a line at a given rank —
    /// <c>System::get_pure_bound</c>.
    /// <para>
    /// It is a lookup rather than a computation: the paper score already knows every rank
    /// a line may break at, so the answer is the break column at that exact rank, or none.
    /// </para>
    /// </summary>
    /// <param name="d">Which end of the line.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The bounding column, or <see langword="null"/>.</returns>
    public PaperColumn GetPureBound(Direction d, int start, int end)
    {
        if (PaperScore == null)
        {
            return null;
        }

        IReadOnlyList<int> ranks = PaperScore.GetBreakRanks();
        IReadOnlyList<int> indices = PaperScore.GetBreakIndices();
        IReadOnlyList<PaperColumn> cols = PaperScore.GetColumns();

        int targetRank = d == Direction.Negative ? start : end;

        int lo = 0;
        int hi = ranks.Count;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (ranks[mid] < targetRank)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo < ranks.Count && ranks[lo] == targetRank ? cols[indices[lo]] : null;
    }

    /// <summary>
    /// Returns the pure bound when a pure answer is wanted and the real one otherwise —
    /// <c>System::get_maybe_pure_bound</c>.
    /// </summary>
    /// <param name="d">Which end of the line.</param>
    /// <param name="pure">Whether a pure answer is wanted.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The bounding column.</returns>
    public PaperColumn GetMaybePureBound(Direction d, bool pure, int start, int end)
        => pure ? GetPureBound(d, start, end) : GetBound(d);

    private static List<Grob> ToList(GrobArray array)
    {
        List<Grob> result = new List<Grob>(array.Count);
        foreach (Grob g in array)
        {
            result.Add(g);
        }

        return result;
    }

    /// <summary>
    /// Returns the system a grob is typeset into, by walking VERTICAL parents to the
    /// root.
    /// <para>
    /// The vertical chain is the one that always ends at the system, which is what makes
    /// this reliable before line breaking, when no grob has been assigned to a line yet.
    /// </para>
    /// </summary>
    /// <param name="me">The grob to start from.</param>
    /// <returns>The root system, or <see langword="null"/> when the chain does not end at one.</returns>
    public static SystemGrob GetRootSystem(Grob me)
    {
        Grob systemGrob = me;

        while (systemGrob != null && systemGrob.GetParent(Axis.Y) != null)
        {
            systemGrob = systemGrob.GetParent(Axis.Y);
        }

        return systemGrob as SystemGrob;
    }

    /// <summary>
    /// Moves the system so its top edge sits at the origin, which is where the page
    /// layout expects a line to start, and then computes EVERY grob's stencil.
    /// <para>
    /// Upstream calls the second half "generate all stencils to trigger font loads",
    /// and it is cheap because stencils are cached per grob. But loading fonts is not
    /// the only thing it does, and the other thing is load-bearing: a stencil callback
    /// is allowed to have SIDE EFFECTS ON OTHER GROBS, and the only reason those land
    /// is that this pass runs every callback before the drawing loop consumes anything.
    /// <c>stem-span-stencil</c> (scm/music-functions.scm) is the case that proves it —
    /// it hides the stems it spans by SETTING their <c>stencil</c> to <c>#f</c>, and
    /// without this pass the drawing loop had already committed twelve of them
    /// (<c>cross-staff-stems.ly</c>: 30 rects where the oracle draws 18).
    /// </para>
    /// <para>
    /// Upstream's <c>uniquify</c> sorts by POINTER before removing duplicates, so its
    /// visiting order is arbitrary; the port dedupes by reference in first-seen order
    /// instead. That is a divergence in ORDER only, and it cannot be observed here: a
    /// callback that overwrites another grob's stencil leaves the same value behind
    /// whichever of the two ran first.
    /// </para>
    /// </summary>
    public void PostProcessing()
    {
        Interval extent = Extent(this, Axis.Y);
        if (extent.IsEmpty)
        {
            Warn.ProgrammingError("system with empty extent");
        }
        else
        {
            TranslateAxis(-extent.Right, Axis.Y);
        }

        /* Generate all stencils to trigger font loads.
           This might seem inefficient, but Stencils are cached per grob
           anyway. */
        List<Grob> allElementsSorted = new List<Grob>();
        HashSet<Grob> seen = new HashSet<Grob>(ReferenceEqualityComparer.Instance);
        foreach (Grob grob in AllElements)
        {
            if (seen.Add(grob))
            {
                allElementsSorted.Add(grob);
            }
        }

        GetStencil();
        foreach (Grob grob in allElementsSorted)
        {
            grob.GetStencil();
        }
    }

    /// <summary>
    /// Draws the system: every grob's print stencil, translated to where the grob
    /// actually sits, combined into one stencil.
    /// <para>
    /// Grobs are drawn in <c>layer</c> order, so a paper column's debugging rectangle
    /// (layer 1000) lands over the staff lines (layer 0) rather than under them.
    /// </para>
    /// <para>
    /// The resulting box is the system's own extent UNITED with the extents actually
    /// drawn. The two differ: a ledger line or a bar number can stick out past the
    /// layout extent, and cropping to the layout extent alone would clip it.
    /// </para>
    /// <para>
    /// DIVERGENCE, recorded in PORT-COVERAGE: upstream's
    /// <c>System::get_paper_system</c> wraps this stencil in a <c>Paper_system</c> prob
    /// carrying the page-break permissions and skylines the PAGE breaker needs. Page
    /// layout is not ported, so the port returns the stencil itself; the prob wrapper
    /// belongs with the page breaker when that arrives.
    /// </para>
    /// </summary>
    /// <returns>The system's stencil.</returns>
    public Stencil GetPaperSystemStencil()
    {
        PostProcessing();

        // Upstream builds a vector of Layer_entry {grob, layer} and reads `layer` EXACTLY
        // ONCE PER GROB, into the entry; operator< then compares the cached ints. Reading
        // the property from inside the comparator instead is not merely slower, it is
        // OBSERVABLE: a property that answers `calculation-in-progress` reports a cyclic
        // dependency on EVERY read, so an O(n log n) comparator turns upstream's single
        // report into one per comparison. `debug-property-callbacks` counts them.
        // The sort call is unchanged, and the comparisons yield the same results, so the
        // permutation is the same one the comparator produced before.
        List<LayerEntry> entries = new List<LayerEntry>();
        foreach (Grob grob in AllElements)
        {
            entries.Add(new LayerEntry(grob, LayerOf(grob)));
        }

        entries.Sort((a, b) => a.Layer.CompareTo(b.Layer));

        List<Grob> ordered = new List<Grob>();
        foreach (LayerEntry entry in entries)
        {
            ordered.Add(entry.Grob);
        }

        List<object> expressions = new List<object>();
        Box stencilBox = default;
        stencilBox.SetEmpty();

        foreach (Grob grob in ordered)
        {
            Stencil st = grob.GetPrintStencil();
            if (Stencil.IsNullExpression(st.Expression))
            {
                continue;
            }

            Offset o = new Offset(
                grob.RelativeCoordinate(this, Axis.X),
                grob.RelativeCoordinate(this, Axis.Y));

            object extraValue = grob.GetProperty(ExtraOffsetSymbol);
            Offset extra = extraValue is Pair pair
                ? new Offset(ToDouble(pair.Car), ToDouble(pair.Cdr))
                    * StaffSymbolReferencer.StaffSpace(grob)
                : Offset.Zero;

            /* Must copy the stencil, for we cannot change the stencil
               cached in G.  */
            st.Translate(o + extra);

            // Accumulate the actual drawn stencil extents
            stencilBox.Unite(st.ExtentBox);

            expressions.Add(st.Expression);
        }

        Stencil mine = GetPrintStencil();
        if (!Stencil.IsNullExpression(mine.Expression))
        {
            expressions.Insert(0, mine.Expression);
            stencilBox.Unite(mine.ExtentBox);
        }

        // Start with the System's extent (used for layout calculations)
        Interval x = Extent(this, Axis.X);
        Interval y = Extent(this, Axis.Y);

        // Extend the bounding box to include all drawn stencil content.
        if (!stencilBox[Axis.X].IsEmpty)
        {
            x.Unite(stencilBox[Axis.X]);
        }

        if (!stencilBox[Axis.Y].IsEmpty)
        {
            y.Unite(stencilBox[Axis.Y]);
        }

        List<object> combined = new List<object>(expressions.Count + 1) { CombineStencilSymbol };
        combined.AddRange(expressions);

        return new Stencil(new Box(x, y), Pair.ListFrom(combined));
    }

    /// <summary>
    /// Wraps this system's stencil in the <c>paper-system</c> prob the page breaker and
    /// the backends consume, carrying the page-break permissions off its bounds.
    /// <para>
    /// The permissions come from the RIGHT bound and the layout details from the LEFT
    /// one, which is not symmetry for its own sake: a line's break details were decided
    /// where it started, and whether a page may end after it is decided where it stops.
    /// </para>
    /// <para>
    /// <c>staff-refpoint-extent</c> carries the Y coordinates of the alignment's SPACEABLE
    /// staves, relative to this system. It is what lets a reader ask where the music sits
    /// inside a line rather than where the line's ink begins, which is why the system
    /// separator centres itself on it and one-page breaking measures the last line's
    /// bottom from it.
    /// </para>
    /// </summary>
    /// <returns>The paper system.</returns>
    public Prob GetPaperSystem()
    {
        Stencil systemStencil = GetPaperSystemStencil();

        PaperColumn left = GetBound(Direction.Negative);
        PaperColumn right = GetBound(Direction.Positive);

        object propertyInit = left != null
            ? left.GetProperty(LineBreakSystemDetailsSymbol)
            : Nil.Instance;

        Prob paperSystem = PaperSystem.Make(propertyInit);
        PaperSystem.SetStencil(paperSystem, systemStencil);

        /* information that the page breaker might need */
        paperSystem.SetProperty(VerticalSkylinesSymbol, GetProperty(VerticalSkylinesSymbol));

        if (right != null)
        {
            paperSystem.SetProperty(
                PageBreakPermissionSymbol, right.GetProperty(PageBreakPermissionSymbol));
            paperSystem.SetProperty(
                PageTurnPermissionSymbol, right.GetProperty(PageTurnPermissionSymbol));
            paperSystem.SetProperty(
                PageBreakPenaltySymbol, right.GetProperty(PageBreakPenaltySymbol));
            paperSystem.SetProperty(
                PageTurnPenaltySymbol, right.GetProperty(PageTurnPenaltySymbol));

            SystemGrob source = Original ?? this;
            if (ReferenceEquals(right.Original ?? right, source.GetBound(Direction.Positive)))
            {
                paperSystem.SetProperty(LastInScoreSymbol, true);
            }
        }

        // An interval that collects no point stays EMPTY, and empty is what upstream
        // stores — not zero. Both sides spell it (+inf . -inf), and both readers guard
        // with "is it a pair whose car is a number", so an empty extent contributes
        // -inf to the last line's bottom bound, which is the same "no constraint" the
        // absent property used to give. Zero would have been the wrong claim; empty is
        // not, so there is nothing here to diverge over.
        Interval staffRefpoints = Interval.Empty;
        if (GetObject(VerticalAlignmentSymbol) is Grob align)
        {
            IReadOnlyList<Grob> staves
                = PointerGroupInterface.ExtractGrobSet(align, ElementsSymbol);
            for (int i = 0; i < staves.Count; i++)
            {
                if (staves[i].IsLive
                    && CodeBrix.LilyPort.Engine.Layout.PageLayoutSpacing.IsSpaceable(staves[i]))
                {
                    staffRefpoints.AddPoint(staves[i].RelativeCoordinate(this, Axis.Y));
                }
            }
        }

        paperSystem.SetProperty(
            StaffRefpointExtentSymbol,
            new Pair(staffRefpoints.Left, staffRefpoints.Right));
        paperSystem.SetProperty(SystemGrobSymbol, this);
        return paperSystem;
    }

    /// <summary>
    /// A grob paired with the <c>layer</c> it was read as, which is upstream's
    /// <c>Layer_entry</c>: the read happens once, when the entry is built, and the sort
    /// compares what was read rather than reading again.
    /// </summary>
    private readonly struct LayerEntry
    {
        public LayerEntry(Grob grob, int layer)
        {
            Grob = grob;
            Layer = layer;
        }

        /// <summary>Gets the grob this entry orders.</summary>
        public Grob Grob { get; }

        /// <summary>Gets the layer read from the grob when the entry was built.</summary>
        public int Layer { get; }
    }

    private static int LayerOf(Grob grob)
    {
        object layer = grob.GetProperty(LayerSymbol);
        return Bootstrap.SchemeConvert.IsNumber(layer)
            ? Bootstrap.SchemeConvert.ToInt(layer, "layer")
            : 1;
    }

    private static double ToDouble(object value)
        => Bootstrap.SchemeConvert.IsNumber(value)
            ? Bootstrap.SchemeConvert.ToDouble(value, "extra-offset")
            : 0.0;

    private void InitElements()
    {
        GrobArray all = new GrobArray { IsOrdered = false };
        SetObject(AllElementsSymbol, all);
    }

    private void BreakBreakableItem(Grob grob)
    {
        if (!(grob is Item item) || item.IsBroken || item.Original != null)
        {
            return;
        }

        if (!Item.IsNonMusical(item))
        {
            return;
        }

        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            Item copy = (Item)item.Clone();
            TypesetGrob(copy);
            item.SetPrebrokenPiece(d, copy);
        }
    }

    private static PaperColumn ColumnOf(Item item)
    {
        for (Grob grob = item; grob != null; grob = grob.GetParent(Axis.X))
        {
            if (grob is PaperColumn column)
            {
                return column;
            }
        }

        return null;
    }

    /// <summary>
    /// <c>System::footnotes_before_line_breaking</c>: every element carrying
    /// <c>footnote-interface</c>, collected off <c>all-elements</c>.
    /// </summary>
    /// <param name="me">The system.</param>
    /// <returns>The footnote grobs, as a grob array.</returns>
    /// <remarks>
    /// Reads <c>all-elements</c> and not <c>elements</c>: before line breaking the
    /// footnotes have not been gathered into the system's own element list yet.
    /// </remarks>
    public static GrobArray FootnotesBeforeLineBreaking(Grob me)
    {
        GrobArray result = new GrobArray();
        foreach (Grob element in PointerGroupInterface.ExtractGrobSet(me, AllElementsSymbol))
        {
            if (element.HasInterface(FootnoteInterfaceSymbol))
            {
                result.Add(element);
            }
        }

        return result;
    }

    /// <summary>
    /// <c>System::footnotes_after_line_breaking</c>: the footnotes falling on THIS
    /// system's stretch of columns, in reading order.
    /// </summary>
    /// <param name="me">The system.</param>
    /// <returns>The footnote grobs, sorted, as a grob array.</returns>
    public static GrobArray FootnotesAfterLineBreaking(SystemGrob me)
    {
        Slice ranks = me.SpannedColumnRankInterval();
        List<Grob> footnotes = me.GetFootnoteGrobsInRange(ranks.Left, ranks.Right);
        footnotes.Sort((a, b) => Grob2DLess(a, b) ? -1 : (Grob2DLess(b, a) ? 1 : 0));

        GrobArray result = new GrobArray();
        foreach (Grob footnote in footnotes)
        {
            result.Add(footnote);
        }

        return result;
    }

    /// <summary>
    /// <c>System::get_footnote_heights_in_range</c>: how tall each FOOT-of-page footnote
    /// in a column-rank range interprets to.
    /// </summary>
    /// <param name="start">The first column rank.</param>
    /// <param name="end">The last column rank.</param>
    /// <returns>One height per footnote, in reading order.</returns>
    public List<double> GetFootnoteHeightsInRange(int start, int end)
        => InternalGetNoteHeightsInRange(start, end, true);

    /// <summary>
    /// <c>System::get_in_note_heights_in_range</c>: how tall each IN-note in a column-rank
    /// range interprets to.
    /// </summary>
    /// <param name="start">The first column rank.</param>
    /// <param name="end">The last column rank.</param>
    /// <returns>One height per in-note, in reading order.</returns>
    public List<double> GetInNoteHeightsInRange(int start, int end)
        => InternalGetNoteHeightsInRange(start, end, false);

    /// <summary>
    /// <c>System::internal_get_note_heights_in_range</c>: the interpreted heights of the
    /// notes in a range, keeping the FOOT-of-page ones or the IN-line ones by the
    /// <c>footnote</c> property.
    /// <para>
    /// These are what tell page breaking that a system carries more height than its own
    /// music: <c>Page_spacing</c> adds them to the page's demand. Without them a page of
    /// in-notes measures as if the notes were not there, which is why the port fitted
    /// <c>in-note-configuration</c> onto ONE page where the oracle needs two.
    /// </para>
    /// </summary>
    /// <param name="start">The first column rank.</param>
    /// <param name="end">The last column rank.</param>
    /// <param name="foot">Whether to keep the foot-of-page notes rather than the in-notes.</param>
    /// <returns>One height per kept note, in reading order.</returns>
    private List<double> InternalGetNoteHeightsInRange(int start, int end, bool foot)
    {
        List<Grob> footnoteGrobs = GetFootnoteGrobsInRange(start, end);
        List<double> output = new List<double>();

        // Upstream erases from the BACK of the vector; the surviving order is the same
        // either way, and removing while walking forwards is what a reverse walk avoids.
        for (int i = footnoteGrobs.Count; i-- > 0;)
        {
            bool isFootnote = SchemeUtilities.ToBool(
                footnoteGrobs[i].GetProperty(FootnoteSymbol));
            if (foot ? !isFootnote : isFootnote)
            {
                footnoteGrobs.RemoveAt(i);
            }
        }

        OutputDef layout = PaperScore != null ? PaperScore.Layout : null;
        if (layout == null)
        {
            return output;
        }

        foreach (Grob footnote in footnoteGrobs)
        {
            object footnoteMarkup = footnote.GetProperty(FootnoteTextSymbol);
            if (!TextInterface.IsMarkup(footnoteMarkup))
            {
                continue;
            }

            object props = SchemeUtilities.CallCallback(
                LilyPondScheme.PublicRef(LilyModule, "layout-extract-page-properties"),
                layout);

            if (TextInterface.InterpretMarkup(layout, props, footnoteMarkup) is Stencil sten)
            {
                output.Add(sten.Extent(Axis.Y).Length);
            }
        }

        return output;
    }

    /// <summary>
    /// <c>System::get_footnote_grobs_in_range</c>: the footnotes whose position falls
    /// inside a column-rank range, with the end-of-line/beginning-of-line duplicates
    /// weeded out.
    /// </summary>
    /// <param name="start">The first column rank.</param>
    /// <param name="end">The last column rank.</param>
    /// <returns>The footnotes in range, in <c>footnotes-before-line-breaking</c> order.</returns>
    /// <remarks>
    /// A broken spanner is represented by ONE of its pieces, chosen by
    /// <c>spanner-placement</c> — the first piece for LEFT (and for CENTER, which
    /// upstream folds into LEFT), the last for RIGHT — and its position is then read
    /// from that piece's RIGHT end. The duplicate check at the bottom is upstream's own,
    /// and upstream's comment there says it is working around duplicate entries in
    /// <c>all_elements_</c> rather than being intrinsic.
    /// </remarks>
    public List<Grob> GetFootnoteGrobsInRange(int start, int end)
    {
        List<Grob> output = new List<Grob>();
        foreach (Grob footnote in
                 PointerGroupInterface.ExtractGrobSet(this, FootnotesBeforeLineBreakingSymbol))
        {
            Grob atBat = footnote;
            int position = atBat.SpannedColumnRankInterval()[Direction.Negative];
            bool endOfLineVisible = true;


            if (atBat is Spanner spanner)
            {
                Direction placement = DirectionOf(
                    spanner.GetProperty(SpannerPlacementSymbol), Direction.Negative);
                if (placement == Direction.Center)
                {
                    placement = Direction.Negative;
                }

                position = spanner.SpannedColumnRankInterval()[placement];

                Spanner original = spanner.Original;
                if (original != null && original.BrokenIntos.Count > 0)
                {
                    atBat = placement == Direction.Negative
                        ? original.BrokenIntos[0]
                        : original.BrokenIntos[original.BrokenIntos.Count - 1];
                    position = atBat.SpannedColumnRankInterval()[Direction.Positive];
                }
            }

            if (atBat is Item item)
            {
                // Weeds out grobs falling at the END of a line when the grobs wanted are
                // the ones at the BEGINNING.
                endOfLineVisible = item.BreakStatusDirection() == Direction.Negative;

                if (!item.BreakVisible())
                {
                    continue;
                }

                if (position == start && item.BreakStatusDirection() != Direction.Positive)
                {
                    continue;
                }

                if (position == end && item.BreakStatusDirection() != Direction.Negative)
                {
                    continue;
                }

                if (position != end && position != start
                    && item.BreakStatusDirection() != Direction.Center)
                {
                    continue;
                }
            }

            if (position < start || position > end)
            {
                continue;
            }

            if (position == start && endOfLineVisible)
            {
                continue;
            }

            if (position == end && !endOfLineVisible)
            {
                continue;
            }

            if (!atBat.IsLive)
            {
                continue;
            }

            if (output.Contains(atBat))
            {
                continue;
            }

            output.Add(atBat);
        }

        return output;
    }

    /// <summary>
    /// <c>grob_2D_less</c>: reading order over the page — by column rank first, then top
    /// to bottom within a rank.
    /// </summary>
    /// <param name="g1">The first grob.</param>
    /// <param name="g2">The second grob.</param>
    /// <returns><see langword="true"/> when the first comes first.</returns>
    public static bool Grob2DLess(Grob g1, Grob g2)
    {
        Grob[] grobs = { g1, g2 };
        int[] ranks = { 0, 0 };

        for (int i = 0; i < 2; i++)
        {
            ranks[i] = grobs[i].SpannedColumnRankInterval()[Direction.Negative];
            if (grobs[i] is Spanner spanner)
            {
                if (spanner.BrokenIntos.Count > 0)
                {
                    Direction placement = DirectionOf(
                        spanner.BrokenIntos[0].GetProperty(SpannerPlacementSymbol),
                        Direction.Center);
                    spanner = placement == Direction.Negative
                        ? spanner.BrokenIntos[0]
                        : spanner.BrokenIntos[spanner.BrokenIntos.Count - 1];
                }

                grobs[i] = spanner;

                // A spanner pushed to the right of its own origin is read at its RIGHT
                // end instead — that is what puts an end-of-line footnote after the
                // notes it follows rather than before them.
                //
                // Read through SchemeConvert, which is upstream's own
                // from_scm<double> (…, 0.0): the value routinely arrives as an EXACT
                // integer (a footnote's X-offset is copied straight off the
                // \footnote #'(1 . 1) pair), and a C#-`double`-only test answers "no
                // offset" for every one of them.
                double offset = Bootstrap.SchemeConvert.ToDouble(
                    spanner.GetProperty(XOffsetPropertySymbol), 0.0);
                if (offset > 0)
                {
                    ranks[i] = spanner.SpannedColumnRankInterval()[Direction.Positive];
                }
            }
        }

        return ranks[0] == ranks[1]
            ? VerticalLess(grobs[0], grobs[1])
            : ranks[0] < ranks[1];
    }

    /// <summary>Reads a direction property, falling back when it is unset.</summary>
    /// <param name="value">The property value.</param>
    /// <param name="fallback">The direction to use when unset.</param>
    /// <returns>The direction.</returns>
    private static Direction DirectionOf(object value, Direction fallback)
        => value is double number
            ? new Direction(number)
            : (value is long exact ? new Direction(exact) : fallback);

    private static readonly Symbol FootnoteInterfaceSymbol = Symbol.Intern("footnote-interface");
    private static readonly Symbol FootnotesBeforeLineBreakingSymbol
        = Symbol.Intern("footnotes-before-line-breaking");
    private static readonly Symbol SpannerPlacementSymbol = Symbol.Intern("spanner-placement");
    private static readonly Symbol FootnoteSymbol = Symbol.Intern("footnote");
    private static readonly Symbol FootnoteTextSymbol = Symbol.Intern("footnote-text");
    private static readonly string[] LilyModule = { "lily" };
    private static readonly Symbol XOffsetPropertySymbol = Symbol.Intern("X-offset");
}

/// <summary>
/// Which staves <see cref="SystemGrob.GetMaybeSpaceableStaves"/> keeps. Upstream spells
/// these as three unnamed <c>int</c> constants local to <c>system.cc</c>.
/// </summary>
public enum StaffFilter
{
    /// <summary>Every live stave.</summary>
    All,

    /// <summary>Only the staves the page spacer may space.</summary>
    Spaceable,

    /// <summary>Only the staves it may not.</summary>
    NonSpaceable,
}
