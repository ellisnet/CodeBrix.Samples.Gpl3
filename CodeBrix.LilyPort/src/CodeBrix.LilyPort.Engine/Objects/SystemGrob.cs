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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/system.cc, lily/include/system.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

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
    private static readonly Symbol BeforeLineBreakingSymbol
        = Symbol.Intern("before-line-breaking");

    private static readonly Symbol SpringsAndRodsSymbol = Symbol.Intern("springs-and-rods");

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
    /// Accepts only paper columns as bounds. A system spans from one column to
    /// another, never from an arbitrary item.
    /// <para>
    /// The base implementation is what refuses to make the bound this system's
    /// horizontal parent — see <see cref="Spanner.SetBound"/> for why that matters.
    /// </para>
    /// </summary>
    /// <param name="direction">Which end.</param>
    /// <param name="grob">The grob to attach to.</param>
    public override void SetBound(Direction direction, Grob grob)
    {
        if (!(grob is PaperColumn))
        {
            Warn.ProgrammingError("system bound must be a paper column");
            return;
        }

        base.SetBound(direction, grob);
    }

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
    /// does. EPG22 landed it (2026-08-07) after the earlier note here — that it "only
    /// matters once lines are actually broken" — was DISPROVEN by measurement: a clone
    /// starts with an empty object alist, and <c>ly:span-bar::before-line-breaking</c>
    /// reads a SpanBar's <c>elements</c> with no default, so 87 files died in this very
    /// method. Its substitution half is <see cref="BreakSubstitution"/>.
    /// </para>
    /// <para>
    /// DIVERGENCE, recorded in PORT-COVERAGE: upstream also runs a <c>fixup_refpoint</c>
    /// pass here, which re-points parents at the prebroken pieces they belong with. That
    /// one does only matter once lines are actually broken — EPG15's subsystem.
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
    /// layout expects a line to start.
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

        List<Grob> ordered = new List<Grob>();
        foreach (Grob grob in AllElements)
        {
            ordered.Add(grob);
        }

        ordered.Sort((a, b) => LayerOf(a).CompareTo(LayerOf(b)));

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
    /// DIVERGENCE, recorded in PORT-COVERAGE: <c>staff-refpoint-extent</c> is left
    /// unset. Upstream computes it from the vertical alignment's spaceable staves, which
    /// is <c>Page_layout_problem::is_spaceable</c> — EPG16's file. An absent property is
    /// an honest "not computed"; a zero interval would read as "the staves are all at
    /// the origin", which is a different and wrong claim.
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

        paperSystem.SetProperty(SystemGrobSymbol, this);
        return paperSystem;
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
}
