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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/spanner.cc, lily/include/spanner.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/** A symbol which is attached between two columns. A spanner is a
    symbol which spans across several columns, so its final appearance
    can only be calculated after the breaking problem is solved.

    Examples

    * (de)crescendo
    * slur
    * beam
    * bracket

    Spanner should know about the items which it should consider:
    e.g. slurs should be steep enough to "enclose" all those items. This
    is absolutely necessary for beams, since they have to adjust the
    length of stems of notes they encompass.
*/

/// <summary>
/// A grob attached between two points: a slur, a beam, a hairpin, a staff symbol.
/// <para>
/// Unlike an <see cref="Item"/>, a spanner's final appearance cannot be known until
/// line breaking is solved, because it may be split across systems. Its two ends are
/// held as <see cref="GetBound"/> items, and the pieces it is broken into are kept in
/// <see cref="BrokenIntos"/>.
/// </para>
/// </summary>
public class Spanner : Grob
{
    private static readonly Symbol SpannerInterface = Symbol.Intern("spanner-interface");
    private static readonly Symbol XPositionsSymbol = Symbol.Intern("X-positions");
    private static readonly Symbol LeftBoundInfoSymbol = Symbol.Intern("left-bound-info");
    private static readonly Symbol RightBoundInfoSymbol = Symbol.Intern("right-bound-info");
    private static readonly Symbol XSymbol = Symbol.Intern("X");
    private static readonly Symbol MinimumLengthSymbol = Symbol.Intern("minimum-length");
    private static readonly Symbol MinimumLengthAfterBreakSymbol
        = Symbol.Intern("minimum-length-after-break");
    private static readonly Symbol NormalizedEndpointsSymbol
        = Symbol.Intern("normalized-endpoints");

    private DrulArray<Item> _spannedDrul;
    private Dictionary<(Symbol, int, int), object> _purePropertyCache;

    /// <summary>Initializes a spanner from its type's basic property alist.</summary>
    /// <param name="basicProperties">The immutable alist for this grob type.</param>
    public Spanner(object basicProperties)
        : base(basicProperties)
    {
        _spannedDrul = new DrulArray<Item>(null, null);
        BreakIndex = 0;
        AddInterface(SpannerInterface);
    }

    /// <summary>Initializes a copy of another spanner.</summary>
    /// <param name="source">The spanner to copy.</param>
    protected Spanner(Spanner source)
        : base(source)
    {
        _spannedDrul = new DrulArray<Item>(null, null);
        BreakIndex = 0;
    }

    /// <summary>Gets the C++ class name this grob corresponds to.</summary>
    public override string ClassName => "Spanner";

    /// <summary>Gets the spanner this one was broken off from.</summary>
    public new Spanner Original => (Spanner)base.Original;

    /// <summary>Gets the pieces this spanner was broken into, one per system.</summary>
    public List<Spanner> BrokenIntos { get; } = new List<Spanner>();

    /// <summary>Gets or sets which broken piece this is, counting from the first.</summary>
    public int BreakIndex { get; set; }

    /// <summary>Gets a value indicating whether this spanner has been broken.</summary>
    public bool IsBroken => BrokenIntos.Count > 0;

    /// <summary>Returns an independent copy of this spanner.</summary>
    /// <returns>The clone.</returns>
    public override Grob Clone() => new Spanner(this);

    /// <summary>Returns the item at one end of the spanner.</summary>
    /// <param name="direction">Which end.</param>
    /// <returns>The bound item, or <see langword="null"/> when unset.</returns>
    public Item GetBound(Direction direction) => _spannedDrul[direction];

    /// <summary>Gets both bounds at once.</summary>
    /// <returns>The two bounds.</returns>
    public DrulArray<Item> GetBounds() => _spannedDrul;

    /// <summary>
    /// Returns the system this spanner lies on: the one BOTH its bounds are on.
    /// <para>
    /// A spanner whose ends landed on different lines has no single system — that is
    /// the state a spanner is in until it has been broken into its pieces — and the
    /// answer is <see langword="null"/> rather than either end's.
    /// </para>
    /// </summary>
    /// <returns>The system, or <see langword="null"/>.</returns>
    public override SystemGrob GetSystem()
    {
        Item left = GetBound(Direction.Negative);
        Item right = GetBound(Direction.Positive);
        if (left == null || right == null)
        {
            return null;
        }

        SystemGrob system = left.GetSystem();
        return system != null && ReferenceEquals(system, right.GetSystem()) ? system : null;
    }

    /// <summary>
    /// Attaches one end of the spanner to an item, and normally makes the LEFT bound
    /// the spanner's horizontal reference point.
    /// <para>
    /// Two exceptions, and both are load bearing. A <see cref="SystemGrob"/> never
    /// takes its bound as parent — a system's left bound is one of its own columns, and
    /// a column's horizontal parent is the system, so taking it would close a cycle
    /// that hangs the first walk up the parent chain. And a spanner whose horizontal
    /// parent is already another SPANNER keeps it: that parent is split at a line break
    /// too, and the original is what later alignment measures against.
    /// </para>
    /// </summary>
    /// <param name="direction">Which end.</param>
    /// <param name="grob">The item to attach to.</param>
    public virtual void SetBound(Direction direction, Grob grob)
    {
        if (!(grob is Item item))
        {
            Warn.ProgrammingError("must have Item for spanner bound of " + Name);
            return;
        }

        _spannedDrul[direction] = item;

        /*
          We check for System to prevent the column -> line_of_score
          -> column -> line_of_score -> etc situation
        */
        if (direction == Direction.Negative && !(this is SystemGrob))
        {
            /*
              If the X-parent is a spanner, it will be split across linebreaks, too,
              so we shouldn't have to overwrite it with the bound. Also, we need
              original parent for alignment.
              This happens e.g. for MultiMeasureRestNumbers and PercentRepeatCounters.
            */
            if (!(GetParent(Axis.X) is Spanner))
            {
                SetParent(item, Axis.X);
            }
        }
    }

    /// <summary>
    /// Returns the neighbouring broken piece on one side, or <see langword="null"/>
    /// when this piece is at the end.
    /// </summary>
    /// <param name="direction">Which side.</param>
    /// <returns>The neighbouring piece.</returns>
    public Spanner BrokenNeighbor(Direction direction)
    {
        Spanner original = Original;
        if (original == null)
        {
            return null;
        }

        int index = BreakIndex + direction.Value;
        if (index < 0 || index >= original.BrokenIntos.Count)
        {
            return null;
        }

        return original.BrokenIntos[index];
    }

    /// <summary>
    /// Finds the piece of this spanner that lives on a given system —
    /// <c>Spanner::find_broken_piece</c>.
    /// </summary>
    /// <param name="system">The system to look on.</param>
    /// <returns>The piece, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// The pieces are contiguous by system rank, so the system's rank minus the first
    /// piece's indexes straight into the list. Added 2026-08-08 by EPG14 — see
    /// <see cref="Grob"/>.
    /// </remarks>
    public override Grob FindBrokenPiece(SystemGrob system)
    {
        if (system == null || BrokenIntos.Count == 0)
        {
            return null;
        }

        int rank = system.Rank;
        Spanner first = BrokenIntos[0];
        SystemGrob firstSystem = first.GetSystem();
        if (firstSystem == null)
        {
            return null;
        }

        int delta = rank - firstSystem.Rank;
        return delta >= 0 && delta < BrokenIntos.Count ? BrokenIntos[delta] : null;
    }

    /// <summary>
    /// The range of paper-column ranks this spanner covers —
    /// <c>Spanner::spanned_column_rank_interval</c>.
    /// </summary>
    /// <returns>The rank range.</returns>
    public override Slice SpannedColumnRankInterval()
    {
        Slice iv = new Slice(0, 0);
        foreach (Direction d in BothDirections)
        {
            Item b = GetBound(d);
            if (b != null)
            {
                PaperColumn col = b.GetColumn();
                if (col != null)
                {
                    iv[d] = col.Rank;
                }
            }
        }

        return iv;
    }

    /// <summary>
    /// Adds the spacing rods a spanner's <c>minimum-length</c> implies —
    /// <c>ly:spanner::set-spacing-rods</c>.
    /// <para>
    /// THREE rods, not one, and the third is the subtle one. The first two cover the
    /// case where the spanner is broken: one from the left bound to the prebroken piece
    /// that ends the line, one from the piece that starts the next line to the right
    /// bound, the second widened by <c>minimum-length-after-break</c> when it is set.
    /// Note that upstream ADDS the difference rather than assigning, because
    /// <c>add_to_cols</c> may already have raised <c>distance_</c>, and that is
    /// reproduced here.
    /// </para>
    /// <para>
    /// The third rod is added TWICE — once for the central column and once against the
    /// right bound's left prebroken piece — because at this point nobody knows yet
    /// whether the spanner will end up broken. Upstream's own comment explains why that
    /// is safe: end rods and ordinary rods are never both used for one spacing
    /// configuration, and after line breaking a grob exists in only one of the two forms.
    /// </para>
    /// <para>
    /// Was a returns-nothing seam from EPG10 until EPG15 (2026-08-08); it was the
    /// SECOND most demanded unported entry point in the sweep, at 961 calls.
    /// </para>
    /// </summary>
    /// <param name="me">The spanner.</param>
    /// <returns>Unspecified.</returns>
    public static object SetSpacingRods(Spanner me)
    {
        object numLength = me.GetProperty(MinimumLengthSymbol);
        object brokenLength = me.GetProperty(MinimumLengthAfterBreakSymbol);

        if (Bootstrap.SchemeConvert.IsNumber(numLength)
            || Bootstrap.SchemeConvert.IsNumber(brokenLength))
        {
            SystemGrob root = SystemGrob.GetRootSystem(me);
            Item lb = me.GetBound(Direction.Negative);
            Item rb = me.GetBound(Direction.Positive);
            if (lb == null || rb == null || root == null)
            {
                return Unspecified.Instance;
            }

            double numLengthValue = Bootstrap.SchemeConvert.IsNumber(numLength)
                ? Bootstrap.SchemeConvert.ToDouble(numLength, "minimum-length")
                : 0.0;

            List<PaperColumn> cols = root.BrokenColumnRange(lb.GetColumn(), rb.GetColumn());

            if (cols.Count > 0)
            {
                Rod r = default;
                r.ItemDrul = new DrulArray<Item>(
                    lb, cols[0].FindPrebrokenPiece(Direction.Negative));
                r.Distance = numLengthValue;
                r.AddToColumns();

                r.ItemDrul = new DrulArray<Item>(
                    cols[cols.Count - 1].FindPrebrokenPiece(Direction.Positive), rb);
                if (Bootstrap.SchemeConvert.IsNumber(brokenLength))
                {
                    /*
                      r.Distance may have been modified by AddToColumns () above. For
                      treatment of minimum-distance-after-break consistent with
                      minimum-distance (which will use the changed value), we cannot
                      directly reset r.Distance to brokenLength.
                    */
                    r.Distance += Bootstrap.SchemeConvert.ToDouble(
                        brokenLength, "minimum-length-after-break") - numLengthValue;
                }

                r.AddToColumns();
            }

            Rod central = default;

            /* As central is a fresh rod, we can set Distance with no complication. */
            central.Distance = numLengthValue;
            central.ItemDrul = new DrulArray<Item>(lb, rb);
            central.AddToColumns();

            /*
              We do not know yet if the spanner is going to have a bound that is broken.
              To account for this uncertainty, we add the rod twice: once for the central
              column (see above) and once for the left column (see below). As end rods are
              never used when ordinary rods are used and vice versa, this rod will only be
              accessed once for each spacing configuration before line breaking. Then, as
              a grob never exists in both unbroken and broken forms after line breaking,
              only one of these two rods will be in the column vector used for spacing in
              SimpleSpacer's GetLineConfiguration.
            */
            Item leftPbp = rb.FindPrebrokenPiece(Direction.Negative);
            if (leftPbp != null)
            {
                central.ItemDrul[Direction.Positive] = leftPbp;
                central.AddToColumns();
            }
        }

        return Unspecified.Instance;
    }

    /// <summary>
    /// Breaks this spanner into one piece per line it crosses —
    /// <c>Spanner::do_break_processing</c>.
    /// <para>
    /// The single-column case is not a degenerate one to skip: upstream breaks a spanner
    /// that spans ONE column anyway, because the pieces may be needed as a parent for
    /// another item. That is the first branch.
    /// </para>
    /// <para>
    /// The general branch walks the break points between the two bounds, and refuses to
    /// build a piece whose bounds fall outside the range its X or Y parent spans — an
    /// orphaned part, which upstream reports and drops rather than laying out.
    /// </para>
    /// </summary>
    public override void DoBreakProcessing()
    {
        // break_into_pieces
        Item left = GetBound(Direction.Negative);
        Item right = GetBound(Direction.Positive);

        if (left == null || right == null || !IsLive)
        {
            return;
        }

        if (GetSystem() != null || IsBroken)
        {
            return;
        }

        if (ReferenceEquals(left, right))
        {
            /*
              If we have a spanner spanning one column, we must break it
              anyway because it might provide a parent for another item.
            */
            foreach (Direction d in BothDirections)
            {
                Item bound = left.FindPrebrokenPiece(d);
                if (bound == null)
                {
                    ProgrammingError("no broken bound");
                }
                else if (bound.GetSystem() != null)
                {
                    Spanner span = (Spanner)Clone();
                    span.SetBound(Direction.Negative, bound);
                    span.SetBound(Direction.Positive, bound);

                    span.GetSystem().TypesetGrob(span);
                    BrokenIntos.Add(span);
                }
            }
        }
        else
        {
            SystemGrob root = SystemGrob.GetRootSystem(this);
            List<PaperColumn> breakPoints = root.BrokenColumnRange(left, right);

            List<Item> points = new List<Item> { left };
            foreach (PaperColumn column in breakPoints)
            {
                points.Add(column);
            }

            points.Add(right);

            Slice parentRankSlice = Slice.Longest;

            /*
              Check if our parent in X-direction spans equally wide
              or wider than we do.
            */
            foreach (Axis a in new[] { Axis.X, Axis.Y })
            {
                if (GetParent(a) is Spanner parent)
                {
                    parentRankSlice.Intersect(parent.SpannedColumnRankInterval());
                }
            }

            for (int i = 1; i < points.Count; i++)
            {
                DrulArray<Item> bounds = new DrulArray<Item>(points[i - 1], points[i]);
                foreach (Direction d in BothDirections)
                {
                    if (bounds[d].GetSystem() == null)
                    {
                        bounds[d] = bounds[d].FindPrebrokenPiece(-d);
                    }
                }

                if (bounds[Direction.Negative] == null || bounds[Direction.Positive] == null)
                {
                    ProgrammingError("bounds of this piece aren't breakable.");
                    continue;
                }

                bool ok = parentRankSlice.Contains(
                    bounds[Direction.Negative].GetColumn().Rank);
                ok = ok
                    && parentRankSlice.Contains(bounds[Direction.Positive].GetColumn().Rank);

                if (!ok)
                {
                    ProgrammingError(
                        "Spanner `" + Name + "' is not fully contained in parent spanner."
                        + "  Ignoring orphaned part");
                    continue;
                }

                Spanner span = (Spanner)Clone();
                span.SetBound(Direction.Negative, bounds[Direction.Negative]);
                span.SetBound(Direction.Positive, bounds[Direction.Positive]);

                SystemGrob leftSystem = bounds[Direction.Negative].GetSystem();
                SystemGrob rightSystem = bounds[Direction.Positive].GetSystem();
                if (leftSystem == null || rightSystem == null
                    || !ReferenceEquals(leftSystem, rightSystem))
                {
                    ProgrammingError("bounds of spanner are invalid");
                    span.Suicide();
                }
                else
                {
                    leftSystem.TypesetGrob(span);
                    BrokenIntos.Add(span);
                }
            }
        }

        BrokenIntos.Sort(static (a, b) => Less(a, b) ? -1 : (Less(b, a) ? 1 : 0));
        for (int i = BrokenIntos.Count; i-- > 0;)
        {
            BrokenIntos[i].BreakIndex = i;
        }
    }

    /// <summary>
    /// A spanner is always its own relevant piece — a <c>final override</c> upstream,
    /// which answers <c>this</c> unconditionally. Only items have prebroken pieces to
    /// choose between.
    /// </summary>
    /// <param name="start">The starting column rank, unused.</param>
    /// <param name="end">The ending column rank, unused.</param>
    /// <returns>This spanner.</returns>
    public override Grob PureFindVisiblePrebrokenPiece(int start, int end) => this;

    /// <summary>
    /// Moves each bound that has no system of its own onto its prebroken piece —
    /// <c>Spanner::set_my_columns</c>.
    /// </summary>
    public void SetMyColumns()
    {
        foreach (Direction d in BothDirections)
        {
            Item b = GetBound(d);
            if (b != null && b.GetSystem() == null)
            {
                SetBound(d, b.FindPrebrokenPiece(-d));
            }
        }
    }

    /// <summary>
    /// The range of SYSTEM ranks this spanner covers —
    /// <c>Spanner::spanned_system_rank_interval</c>.
    /// <para>
    /// A spanner that lies on one system answers that system's rank twice; a broken one
    /// answers the ranks its first and last pieces landed on. An unbroken spanner with
    /// no system answers the default interval, which reads back empty.
    /// </para>
    /// </summary>
    /// <returns>The system-rank range.</returns>
    public override Slice SpannedSystemRankInterval()
    {
        Slice rv = Slice.Empty;

        SystemGrob st = GetSystem();
        if (st != null)
        {
            rv = new Slice(st.Rank, st.Rank);
        }
        else if (BrokenIntos.Count > 0)
        {
            SystemGrob first = BrokenIntos[0].GetSystem();
            SystemGrob last = BrokenIntos[BrokenIntos.Count - 1].GetSystem();
            if (first != null && last != null)
            {
                rv = new Slice(first.Rank, last.Rank);
            }
        }

        return rv;
    }

    /// <summary>
    /// The span of musical time between this spanner's two bounds —
    /// <c>Spanner::spanned_time</c>.
    /// </summary>
    /// <returns>The moment range.</returns>
    public MomentInterval SpannedTime()
        => Item.SpannedTimeInterval(GetBound(Direction.Negative), GetBound(Direction.Positive));

    /// <summary>
    /// Orders two broken pieces by the rank of the system they landed on —
    /// <c>Spanner::less</c>. This is what puts <see cref="BrokenIntos"/> in line order.
    /// </summary>
    /// <param name="a">The first piece.</param>
    /// <param name="b">The second piece.</param>
    /// <returns><see langword="true"/> when the first is on an earlier system.</returns>
    public static bool Less(Spanner a, Spanner b)
        => a.GetSystem().Rank < b.GetSystem().Rank;

    /// <summary>
    /// Returns the spanner's horizontal length: the distance between its two bounds.
    /// </summary>
    /// <returns>The length.</returns>
    public double SpannerLength()
    {
        // THREE tiers, in upstream's order, and the order is the point: a spanner that
        // has been through line breaking carries its answer in X-positions, one that has
        // only had its bounds resolved carries it in the bound-info alists, and only a
        // spanner with neither falls back on the bounds' own coordinates.
        //
        // The port had ONLY a variant of the third tier until EPG17 (2026-08-07), and
        // that variant referenced each bound to ITS OWN X parent rather than absolutely,
        // so two bounds under different parents produced a meaningless difference and two
        // bounds at the same in-parent offset produced ZERO. VoltaBracket is the first
        // grob to call this in anger; volta-multi-staff-inner-staff.ly died on
        // "Skyline building slope is not finite" because Bracket::make_bracket divides by
        // the length it is handed.
        Interval lr = ReadInterval(GetProperty(XPositionsSymbol), new Interval(1, -1));

        if (lr.IsEmpty)
        {
            DrulArray<object> bounds = new DrulArray<object>(
                GetProperty(LeftBoundInfoSymbol), GetProperty(RightBoundInfoSymbol));

            foreach (Direction d in BothDirections)
            {
                Pair entry = SchemeUtilities.Assq(XSymbol, bounds[d]);
                lr[d] = entry != null && Bootstrap.SchemeConvert.IsNumber(entry.Cdr)
                    ? Bootstrap.SchemeConvert.ToDouble(entry.Cdr, "spanner-length")
                    : -(int)d;
            }
        }

        if (lr.IsEmpty)
        {
            foreach (Direction d in BothDirections)
            {
                Item bound = GetBound(d);
                lr[d] = bound != null ? bound.RelativeCoordinate(null, Axis.X) : -(int)d;
            }
        }

        if (lr.IsEmpty)
        {
            ProgrammingError("spanner with negative length");
        }

        return lr.Length;
    }

    private static Interval ReadInterval(object value, Interval fallback)
    {
        if (value is Pair pair
            && Bootstrap.SchemeConvert.IsNumber(pair.Car)
            && Bootstrap.SchemeConvert.IsNumber(pair.Cdr))
        {
            return new Interval(
                Bootstrap.SchemeConvert.ToDouble(pair.Car, "spanner-length"),
                Bootstrap.SchemeConvert.ToDouble(pair.Cdr, "spanner-length"));
        }

        return fallback;
    }

    private static Direction[] BothDirections { get; }
        = { Direction.Negative, Direction.Positive };

    /// <summary>
    /// Splits the unit interval between this spanner's broken pieces in proportion to
    /// their lengths — <c>ly:spanner::calc-normalized-endpoints</c>.
    /// <para>
    /// This is how a spanner drawn across a line break knows WHICH PART of its shape it
    /// is: a hairpin broken in two gets (0 . 0.6) and (0.6 . 1) rather than two
    /// full-length hairpins. An unbroken spanner gets the whole interval.
    /// </para>
    /// <para>
    /// It was the MOST demanded unported entry point in the project, at 2,991 calls per
    /// sweep, until EPG15 (2026-08-08).
    /// </para>
    /// </summary>
    /// <param name="me">The spanner.</param>
    /// <returns>This piece's share of the interval.</returns>
    public static object CalcNormalizedEndpoints(Spanner me)
    {
        object result = Nil.Instance;

        Spanner orig = me.Original;

        orig = orig ?? me;

        if (orig.IsBroken)
        {
            double totalWidth = 0.0;
            List<double> spanData = new List<double>();

            for (int i = 0; i < orig.BrokenIntos.Count; i++)
            {
                spanData.Add(orig.BrokenIntos[i].SpannerLength());
            }

            List<Interval> unnormalizedEndpoints = new List<Interval>();

            for (int i = 0; i < spanData.Count; i++)
            {
                unnormalizedEndpoints.Add(new Interval(totalWidth, totalWidth + spanData[i]));
                totalWidth += spanData[i];
            }

            for (int i = 0; i < unnormalizedEndpoints.Count; i++)
            {
                Interval scaled = unnormalizedEndpoints[i];
                scaled.Left = 1 / totalWidth * scaled.Left;
                scaled.Right = 1 / totalWidth * scaled.Right;
                object t = new Pair(scaled.Left, scaled.Right);
                orig.BrokenIntos[i].SetProperty(NormalizedEndpointsSymbol, t);
                if (me.BreakIndex == i)
                {
                    result = t;
                }
            }
        }
        else
        {
            result = new Pair(0.0, 1.0);
            orig.SetProperty(NormalizedEndpointsSymbol, result);
        }

        return result;
    }

    /// <summary>
    /// The horizontal interval between this spanner's two bounds, measured from the
    /// spanner itself — <c>ly:spanner::bounds-width</c>.
    /// </summary>
    /// <param name="me">The spanner.</param>
    /// <returns>The width interval.</returns>
    public static object BoundsWidth(Spanner me)
    {
        Item lb = me.GetBound(Direction.Negative);
        Item rb = me.GetBound(Direction.Positive);
        Grob common = lb.CommonRefpoint(rb, Axis.X);

        Interval w = new Interval(
            lb.RelativeCoordinate(common, Axis.X),
            rb.RelativeCoordinate(common, Axis.X));

        double offset = me.RelativeCoordinate(common, Axis.X);
        w.Left -= offset;
        w.Right -= offset;

        return new Pair(w.Left, w.Right);
    }

    /// <summary>
    /// Kills a spanner that starts a line and covers no time at all —
    /// <c>ly:spanner::kill-zero-spanned-time</c>.
    /// <para>
    /// Upstream's reasoning, kept because it is the whole justification: a line or
    /// hairpin at the START of a line makes no sense for piano voice indicators, and the
    /// second note of a glissando is normally exact, so there is nothing to glide from.
    /// Typographically it also has almost no room to the left of the note.
    /// </para>
    /// </summary>
    /// <param name="me">The spanner.</param>
    /// <returns>Unspecified.</returns>
    public static object KillZeroSpannedTime(Spanner me)
    {
        Item left = me.GetBound(Direction.Negative);
        if (left != null && left.BreakStatusDirection() != Direction.Center)
        {
            MomentInterval moments = me.SpannedTime();
            Moment start = moments.Left;
            moments.Left = new Moment(start.MainPart, Rational.Zero);

            // Interval_t<Moment>::length () has no counterpart on MomentInterval, so the
            // one call is written out as right - left, which is what that method is —
            // the same treatment VowelTransition records for the identical gap.
            if (moments.Right - moments.Left == Moment.Zero)
            {
                me.Suicide();
            }
        }

        return Unspecified.Instance;
    }

    /// <summary>
    /// Reads a value out of the PURE property cache, which is keyed by name and by the
    /// column range the answer was computed for.
    /// <para>
    /// The key includes the range because a pure answer is only pure FOR A LINE: the same
    /// property asked about a different pair of columns is a different question. A cache
    /// keyed on the name alone would be silently wrong at every line break, which is
    /// exactly the kind of defect this port keeps finding.
    /// </para>
    /// </summary>
    /// <param name="sym">The property name.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The cached value, or <see langword="null"/> when there is none.</returns>
    public object GetCachedPureProperty(Symbol sym, int start, int end)
    {
        if (_purePropertyCache == null)
        {
            return null;
        }

        return _purePropertyCache.TryGetValue((sym, start, end), out object value) ? value : null;
    }

    /// <summary>Stores a value in the pure property cache.</summary>
    /// <param name="sym">The property name.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <param name="val">The value to cache.</param>
    public void CachePureProperty(Symbol sym, int start, int end, object val)
    {
        _purePropertyCache ??= new Dictionary<(Symbol, int, int), object>();
        _purePropertyCache[(sym, start, end)] = val;
    }

    /// <summary>
    /// Substitutes one mutable object property into every broken piece —
    /// <c>Spanner::substitute_one_mutable_property</c>.
    /// <para>
    /// The fast path exists because these lists get very long in orchestral scores;
    /// <see cref="FastSubstituteGrobArray"/> explains when it applies.
    /// </para>
    /// </summary>
    /// <param name="sym">The property name.</param>
    /// <param name="val">The property value.</param>
    public void SubstituteOneMutableProperty(Symbol sym, object val)
    {
        if (val is GrobArray grobArray && FastSubstituteGrobArray(sym, grobArray))
        {
            return;
        }

        foreach (Spanner sc in BrokenIntos)
        {
            SystemGrob system = sc.GetSystem();

            object newval = BreakSubstitution.DoBreakSubstitution(system, val);
            sc.SetObject(sym, newval);
        }
    }

    /// <summary>
    /// The sub-quadratic path for substituting a large UNORDERED grob array.
    /// <para>
    /// The naive substitution is O(systems x grobs), and systems grow with grobs, so it
    /// is quadratic — upstream measured it as one of the top costs in a 50-page score.
    /// This version sorts the items by the first system they touch, indexes the range of
    /// items alive on each system once, and then walks only that range per piece.
    /// Spanners are NOT sorted, deliberately: staff spanners cover the whole score and
    /// would ruin the ordering, so they are simply retried for every piece.
    /// </para>
    /// <para>
    /// It applies only to arrays that are unordered (so the output order does not matter)
    /// and larger than upstream's threshold of 15, whose own comment notes it was chosen
    /// by profiling in 2005 and may deserve revisiting.
    /// </para>
    /// </summary>
    /// <param name="sym">The property name.</param>
    /// <param name="grobArray">The array to substitute.</param>
    /// <returns><see langword="true"/> when the fast path handled it.</returns>
    public bool FastSubstituteGrobArray(Symbol sym, GrobArray grobArray)
    {
        if (grobArray.IsOrdered)
        {
            return false;
        }

        if (grobArray.Count < 15)
        {
            return false;
        }

        Slice systemRange = SpannedSystemRankInterval();

        // Upstream ASSERTS the relationship this checks -- that a spanner being
        // substituted has one broken piece per system it spans, or none and no span. The
        // port declines the fast path instead of asserting, and the difference is not
        // cosmetic: an EMPTY system range makes the normalisation below subtract one
        // sentinel from another, which overflows into a small positive range and indexes
        // off the end of the table. The slow path in SubstituteOneMutableProperty gives
        // the same answer, so refusing here costs only speed.
        if (systemRange.IsEmpty
            || BrokenIntos.Count != systemRange.Length + 1)
        {
            return false;
        }

        List<SubstitutionEntry> items = new List<SubstitutionEntry>();
        List<Grob> spanners = new List<Grob>();
        foreach (Grob g in grobArray)
        {
            if (g is Item it)
            {
                Slice sr = it.SpannedSystemRankInterval();
                sr.Intersect(systemRange);

                // Normalise only a REAL range. An empty one keeps its sentinels, which
                // stay ordered left > right and so skip the indexing loop below; shifting
                // them would not.
                if (!sr.IsEmpty)
                {
                    sr.Left -= systemRange.Left;
                    sr.Right -= systemRange.Left;
                }

                items.Add(new SubstitutionEntry(g, sr));
            }
            else
            {
                spanners.Add(g);
            }
        }

        // A STABLE sort, because upstream's is (std::stable_sort): two items starting on
        // the same system keep the order the array gave them, and that order reaches the
        // output. List<T>.Sort is INTROSORT and is not stable, so it cannot be used here.
        StableSortByLeft(items);

        // Slice.Empty, NOT default(Slice): a default struct reads as (0, 0), which is a
        // NON-empty range holding index zero, so every system would appear to hold the
        // first item whether it does or not.
        List<Slice> itemIndices = new List<Slice>(systemRange.Length + 1);
        for (int i = 0; i <= systemRange.Length; i++)
        {
            itemIndices.Add(Slice.Empty);
        }

        for (int i = 0; i < items.Count; i++)
        {
            for (int j = items[i].Left; j <= items[i].Right; j++)
            {
                Slice slice = itemIndices[j];
                slice.AddPoint(i);
                itemIndices[j] = slice;
            }
        }

        for (int i = 0; i < BrokenIntos.Count && i < itemIndices.Count; ++i)
        {
            Spanner sc = BrokenIntos[i];
            object newval = sc.GetObject(sym);
            GrobArray newArray = newval as GrobArray;
            if (newArray == null)
            {
                newArray = new GrobArray();
                sc.SetObject(sym, newArray);
            }

            SystemGrob system = sc.GetSystem();

            Slice range = itemIndices[i];
            if (!range.IsEmpty)
            {
                for (int j = range.Left; j <= range.Right; ++j)
                {
                    Grob og = items[j].Grob;
                    Grob g = BreakSubstitution.SubstituteGrob(system, og);
                    if (g != null)
                    {
                        newArray.Add(g);
                    }
                }
            }

            foreach (Grob og in spanners)
            {
                Grob g = BreakSubstitution.SubstituteGrob(system, og);
                if (g != null)
                {
                    newArray.Add(g);
                }
            }
        }

        return true;
    }

    private static void StableSortByLeft(List<SubstitutionEntry> entries)
    {
        SubstitutionEntry[] buffer = entries.ToArray();
        int[] order = new int[buffer.Length];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        System.Array.Sort(
            order,
            (a, b) => buffer[a].Left != buffer[b].Left
                ? buffer[a].Left.CompareTo(buffer[b].Left)
                : a.CompareTo(b));

        for (int i = 0; i < order.Length; i++)
        {
            entries[i] = buffer[order[i]];
        }
    }

    /// <summary>
    /// One item in <see cref="FastSubstituteGrobArray"/>'s index: a grob plus the span of
    /// system ranks it is alive on, relative to the spanner's own first system.
    /// </summary>
    private readonly struct SubstitutionEntry
    {
        public SubstitutionEntry(Grob grob, Slice systemRange)
        {
            Grob = grob;
            Left = systemRange.Left;
            Right = systemRange.Right;
        }

        public Grob Grob { get; }

        public int Left { get; }

        public int Right { get; }
    }

    /// <summary>
    /// The free function <c>add_bound_item</c>: the first call sets the left bound,
    /// every later one the right.
    /// </summary>
    /// <param name="sp">The spanner.</param>
    /// <param name="it">The item to bound it with.</param>
    public static void AddBoundItem(Spanner sp, Grob it)
    {
        if (sp.GetBound(Direction.Negative) == null)
        {
            sp.SetBound(Direction.Negative, it);
        }
        else
        {
            sp.SetBound(Direction.Positive, it);
        }
    }
}
