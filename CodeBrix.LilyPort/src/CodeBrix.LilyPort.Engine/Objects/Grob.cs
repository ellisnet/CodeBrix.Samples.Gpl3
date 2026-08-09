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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/grob.cc, lily/grob-property.cc, lily/include/grob.hh, lily/include/dimension-cache.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/*
  XY offset/refpoint/extent structure.
*/

/// <summary>
/// One axis's worth of geometry cache for a grob: its extent, its offset from its
/// parent, and which grob that parent is.
/// </summary>
public sealed class DimensionCache
{
    /// <summary>Gets or sets the cached extent, or null when not yet computed.</summary>
    public Interval? Extent { get; set; }

    /// <summary>Gets or sets the cached offset from the parent, or null when not yet computed.</summary>
    public double? Offset { get; set; }

    /// <summary>Gets or sets the reference point on this axis.</summary>
    public Grob Parent { get; set; }

    /// <summary>
    /// Clears the cached geometry. The parent is deliberately NOT cleared — upstream
    /// says so in a comment, and refpoint fixups depend on it surviving.
    /// </summary>
    public void Clear()
    {
        Extent = null;
        Offset = null;

        // note that parent_ is not nullified
    }

    /// <summary>Returns a copy of this cache.</summary>
    /// <returns>The copy.</returns>
    public DimensionCache Copy()
        => new DimensionCache { Extent = Extent, Offset = Offset, Parent = Parent };
}

/// <summary>
/// A graphical object: the thing that eventually gets drawn.
/// <para>
/// Every note head, stem, clef, slur and staff line is one of these. A grob carries
/// three alists — immutable defaults shared with every grob of its type, mutable
/// overrides of its own, and an <c>object</c> alist holding links to OTHER grobs —
/// plus a per-axis geometry cache.
/// </para>
/// <para>
/// Almost every property may hold a PROCEDURE instead of a value. Reading such a
/// property calls it and caches the answer, which is what lets the layout be
/// described declaratively and computed lazily. The
/// <c>calculation-in-progress</c> marker planted during that call is how a cyclic
/// dependency gets reported instead of overflowing the stack.
/// </para>
/// </summary>
public abstract class Grob : IDiagnostics
{
    private static readonly Symbol AlignInterfaceSymbol = Symbol.Intern("align-interface");
    private static readonly Symbol MetaSymbol = Symbol.Intern("meta");
    private static readonly Symbol InterfacesSymbol = Symbol.Intern("interfaces");
    private static readonly Symbol ObjectCallbacksSymbol = Symbol.Intern("object-callbacks");
    private static readonly Symbol XExtentSymbol = Symbol.Intern("X-extent");
    private static readonly Symbol YExtentSymbol = Symbol.Intern("Y-extent");
    private static readonly Symbol MinimumXExtentSymbol = Symbol.Intern("minimum-X-extent");
    private static readonly Symbol MinimumYExtentSymbol = Symbol.Intern("minimum-Y-extent");
    private static readonly Symbol XOffsetSymbol = Symbol.Intern("X-offset");
    private static readonly Symbol YOffsetSymbol = Symbol.Intern("Y-offset");
    private static readonly Symbol PureYOffsetInProgressSymbol
        = Symbol.Intern("pure-Y-offset-in-progress");
    private static readonly Symbol StencilSymbol = Symbol.Intern("stencil");
    private static readonly Symbol TransparentSymbol = Symbol.Intern("transparent");
    private static readonly Symbol RotationSymbol = Symbol.Intern("rotation");
    private static readonly Symbol ColorSymbol = Symbol.Intern("color");
    private static readonly Symbol OutputAttributesSymbol = Symbol.Intern("output-attributes");
    private static readonly Symbol GrobCauseSymbol = Symbol.Intern("grob-cause");
    private static readonly Symbol CauseSymbol = Symbol.Intern("cause");
    private static readonly Symbol NameSymbol = Symbol.Intern("name");
    private static readonly Symbol BackendTypeSymbol = Symbol.Intern("backend-type?");
    private static readonly Symbol CalculationInProgress = Symbol.Intern("calculation-in-progress");
    private static readonly Symbol VerticalSkylinesSymbol = Symbol.Intern("vertical-skylines");
    private static readonly Symbol HorizontalSkylinesSymbol
        = Symbol.Intern("horizontal-skylines");
    private static readonly Symbol StencilWidthSymbol = Symbol.Intern("ly:grob::stencil-width");
    private static readonly Symbol StencilHeightSymbol
        = Symbol.Intern("ly:grob::stencil-height");
    private static readonly Symbol PureStencilHeightSymbol
        = Symbol.Intern("ly:grob::pure-stencil-height");
    private static readonly Symbol SimpleVerticalSkylinesSymbol
        = Symbol.Intern("ly:grob::simple-vertical-skylines-from-extents");
    private static readonly Symbol PureSimpleVerticalSkylinesSymbol
        = Symbol.Intern("ly:grob::pure-simple-vertical-skylines-from-extents");
    private static readonly Symbol SimpleHorizontalSkylinesSymbol
        = Symbol.Intern("ly:grob::simple-horizontal-skylines-from-extents");
    private static readonly Symbol PureSimpleHorizontalSkylinesSymbol
        = Symbol.Intern("ly:grob::pure-simple-horizontal-skylines-from-extents");

    private readonly DimensionCache[] _dimensionCache =
    {
        new DimensionCache(),
        new DimensionCache(),
    };

    private object _immutablePropertyAlist;
    private object _mutablePropertyAlist;
    private object _objectAlist;
    private object _interfaces;

    /// <summary>Initializes a grob from its type's basic property alist.</summary>
    /// <param name="basicProperties">
    /// The immutable alist for this grob type, as built by
    /// <c>scm/define-grobs.scm</c>.
    /// </param>
    protected Grob(object basicProperties)
    {
        /* FIXME: default should be no callback.  */
        Layout = null;
        Original = null;
        _interfaces = Nil.Instance;
        _immutablePropertyAlist = basicProperties ?? Nil.Instance;
        _mutablePropertyAlist = Nil.Instance;
        _objectAlist = Nil.Instance;

        object meta = GetProperty(MetaSymbol);
        if (meta is Pair)
        {
            Pair interfaces = SchemeUtilities.Assq(InterfacesSymbol, meta);
            if (interfaces != null)
            {
                _interfaces = interfaces.Cdr;
            }

            Pair objectCallbacks = SchemeUtilities.Assq(ObjectCallbacksSymbol, meta);
            if (objectCallbacks != null)
            {
                object cursor = objectCallbacks.Cdr;
                while (cursor is Pair pair)
                {
                    if (pair.Car is Pair entry && entry.Car is Symbol key)
                    {
                        SetObject(key, entry.Cdr);
                    }

                    cursor = pair.Cdr;
                }
            }
        }

        /*
          EPG11/EPG12 (2026-08-08) carried these four. grob.cc's ledger row has said
          `ported' since EPG0, but the constructor stopped after the meta block and never
          installed upstream's DEFAULT extent and skyline callbacks. The primitives
          themselves were all registered — only the defaulting was missing.

          It was silent because most grobs name X-extent and Y-extent explicitly in
          scm/define-grobs.scm. NoteHead does NOT name X-extent, so every note head in
          every score has been answering an EMPTY horizontal extent, and nothing asked
          until Tie_formatting_problem did: it feeds head extents straight into a Skyline,
          where an empty interval becomes a NaN roof height.
        */
        if (GetPropertyData(XExtentSymbol) is Nil)
        {
            object stencilWidth = LilyPondScheme.LookupProcedure(StencilWidthSymbol);
            if (stencilWidth != null)
            {
                SetProperty(XExtentSymbol, stencilWidth);
            }
        }

        if (GetPropertyData(YExtentSymbol) is Nil)
        {
            object height = LilyPondScheme.LookupProcedure(StencilHeightSymbol);
            object pureHeight = LilyPondScheme.LookupProcedure(PureStencilHeightSymbol);
            if (height != null && pureHeight != null)
            {
                SetProperty(YExtentSymbol, new UnpurePureContainer(height, pureHeight));
            }
        }

        if (GetPropertyData(VerticalSkylinesSymbol) is Nil)
        {
            object skylines = LilyPondScheme.LookupProcedure(SimpleVerticalSkylinesSymbol);
            object pureSkylines
                = LilyPondScheme.LookupProcedure(PureSimpleVerticalSkylinesSymbol);
            if (skylines != null && pureSkylines != null)
            {
                SetProperty(
                    VerticalSkylinesSymbol, new UnpurePureContainer(skylines, pureSkylines));
            }
        }

        if (GetPropertyData(HorizontalSkylinesSymbol) is Nil)
        {
            object skylines = LilyPondScheme.LookupProcedure(SimpleHorizontalSkylinesSymbol);
            object pureSkylines
                = LilyPondScheme.LookupProcedure(PureSimpleHorizontalSkylinesSymbol);
            if (skylines != null && pureSkylines != null)
            {
                SetProperty(
                    HorizontalSkylinesSymbol, new UnpurePureContainer(skylines, pureSkylines));
            }
        }
    }

    /// <summary>Initializes a copy of another grob, which becomes its original.</summary>
    /// <param name="source">The grob to copy.</param>
    protected Grob(Grob source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        Original = source;

        _immutablePropertyAlist = source._immutablePropertyAlist;
        _mutablePropertyAlist = Nil.Instance;

        for (int a = 0; a < Axes.Count; a++)
        {
            _dimensionCache[a] = source._dimensionCache[a].Copy();
        }

        _interfaces = source._interfaces;
        _objectAlist = Nil.Instance;

        Layout = null;

        // A shallow alist copy, not a deep one: the VALUES are shared with the
        // original, which is what upstream's ly_alist_copy does.
        _mutablePropertyAlist = AlistCopy(source._mutablePropertyAlist);
    }

    /// <summary>Gets or sets the output definition this grob is laid out under.</summary>
    public OutputDef Layout { get; set; }

    /// <summary>
    /// Gets the grob this one was broken off from, or <see langword="null"/> when it
    /// is the original.
    /// </summary>
    public Grob Original { get; }

    /// <summary>Gets the list of interfaces this grob implements.</summary>
    public object Interfaces => _interfaces;

    /// <summary>Gets the object alist: this grob's links to other grobs.</summary>
    public object ObjectAlist => _objectAlist;

    /// <summary>Gets the mutable property alist.</summary>
    public object MutablePropertyAlist => _mutablePropertyAlist;

    /// <summary>Gets the immutable property alist, shared by every grob of this type.</summary>
    public object ImmutablePropertyAlist => _immutablePropertyAlist;

    /// <summary>Gets the C++ class name this grob corresponds to.</summary>
    public virtual string ClassName => "Grob";

    /// <summary>
    /// Gets a value indicating whether the grob is still alive. A grob that has
    /// committed suicide keeps its identity but loses every property.
    /// </summary>
    public bool IsLive => _immutablePropertyAlist is Pair;

    /// <summary>
    /// The range of paper-column ranks this grob spans —
    /// <c>Grob::spanned_column_rank_interval</c>.
    /// <para>
    /// Upstream declares this pure virtual, so only <see cref="Item"/> and
    /// <see cref="Spanner"/> answer meaningfully; a bare grob spans nothing.
    /// </para>
    /// </summary>
    /// <returns>The rank range.</returns>
    public virtual Slice SpannedColumnRankInterval() => Slice.Empty;

    /// <summary>
    /// Finds the piece of this grob that lives on a given system —
    /// <c>Grob::find_broken_piece</c>.
    /// </summary>
    /// <param name="system">The system to look on.</param>
    /// <returns>The piece, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// A bare grob has no pieces, so upstream's base answers null and only
    /// <see cref="Item"/> and <see cref="Spanner"/> override it. Added 2026-08-08 by EPG14:
    /// the method is declared in <c>grob.hh</c> and defined in all three files, every one of
    /// which the ledger already called <c>ported</c>, but it had never been carried — nothing
    /// asked until <c>Line_spanner::calc_bound_info</c> needed to follow a cross-staff
    /// glissando's bound onto another system.
    /// </remarks>
    public virtual Grob FindBrokenPiece(SystemGrob system) => null;

    /// <summary>
    /// A grob's PURE vertical extent relative to a reference point, falling back to the
    /// single point it sits at when it has none — <c>robust_relative_pure_y_extent</c>.
    /// </summary>
    /// <param name="me">The grob.</param>
    /// <param name="refpoint">The reference point.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The extent, never empty.</returns>
    /// <remarks>
    /// The pure counterpart of <c>robust_relative_extent</c>; a free function in
    /// <c>lily/grob.cc</c> like its sibling. Added 2026-08-08 by EPG14 for
    /// <c>Balloon_interface::pure_height</c>.
    /// </remarks>
    public static Interval RobustRelativePureYExtent(
        Grob me, Grob refpoint, int start, int end)
    {
        Interval ext = me.PureYExtent(refpoint, start, end);
        if (ext.IsEmpty)
        {
            ext.AddPoint(me.PureRelativeYCoordinate(refpoint, start, end));
        }

        return ext;
    }

    /// <summary>
    /// Orders two grobs by where they START — <c>Grob::less</c>. Used to walk grobs
    /// and beams in parallel in horizontal order.
    /// </summary>
    /// <param name="g1">The first grob.</param>
    /// <param name="g2">The second grob.</param>
    /// <returns><see langword="true"/> when the first starts before the second.</returns>
    public static bool Less(Grob g1, Grob g2)
        => g1.SpannedColumnRankInterval()[Direction.Negative]
           < g2.SpannedColumnRankInterval()[Direction.Negative];

    /// <summary>Gets the grob's type name, from its <c>name</c> meta property.</summary>
    public string Name
    {
        get
        {
            object meta = GetProperty(MetaSymbol);
            Pair entry = SchemeUtilities.Assq(NameSymbol, meta);
            return entry?.Cdr is Symbol symbol ? symbol.Name : ClassName;
        }
    }

    /// <summary>Returns an independent copy of this grob.</summary>
    /// <returns>The clone.</returns>
    public abstract Grob Clone();

    /// <summary>Gets the reference point on one axis.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>The parent grob, or <see langword="null"/>.</returns>
    public Grob GetParent(Axis axis) => _dimensionCache[(int)axis].Parent;

    /// <summary>Sets the reference point on one axis.</summary>
    /// <param name="parent">The parent grob.</param>
    /// <param name="axis">The axis.</param>
    public void SetParent(Grob parent, Axis axis) => _dimensionCache[(int)axis].Parent = parent;

    /// <summary>Gets or sets the horizontal reference point.</summary>
    public Grob XParent
    {
        get => GetParent(Axis.X);
        set => SetParent(value, Axis.X);
    }

    /// <summary>Gets or sets the vertical reference point.</summary>
    public Grob YParent
    {
        get => GetParent(Axis.Y);
        set => SetParent(value, Axis.Y);
    }

    /// <summary>Reads a property, running and caching its callback if it holds one.</summary>
    /// <param name="symbol">The property name.</param>
    /// <returns>The value, or the empty list when unset.</returns>
    public object GetProperty(Symbol symbol)
    {
        object value = GetPropertyData(symbol);

        if (ReferenceEquals(value, CalculationInProgress))
        {
            Warn.ProgrammingError(
                "cyclic dependency: calculation-in-progress encountered for "
                + Name
                + "."
                + symbol.Name);
            return Nil.Instance;
        }

        if (value is UnpurePureContainer container)
        {
            value = container.Unpure;
        }

        if (value is Procedure)
        {
            value = TryCallbackOnAlist(symbol, value);
        }

        return value;
    }

    /// <summary>Reads a property by name.</summary>
    /// <param name="name">The property name.</param>
    /// <returns>The value.</returns>
    public object GetProperty(string name) => GetProperty(Symbol.Intern(name));

    /// <summary>
    /// Reads a property WITHOUT running any callback it holds — the raw stored datum.
    /// </summary>
    /// <param name="symbol">The property name.</param>
    /// <returns>The stored value, or the empty list when unset.</returns>
    public object GetPropertyData(Symbol symbol)
    {
        Pair handle = SchemeUtilities.Assq(symbol, _mutablePropertyAlist);
        if (handle != null)
        {
            return handle.Cdr;
        }

        handle = SchemeUtilities.Assq(symbol, _immutablePropertyAlist);
        return handle == null ? Nil.Instance : handle.Cdr;
    }

    /// <summary>Writes a property into the mutable alist.</summary>
    /// <param name="symbol">The property name.</param>
    /// <param name="value">The value to store.</param>
    public void SetProperty(Symbol symbol, object value)
    {
        /* Perhaps we simply do the assq_set, but what the heck. */
        if (!IsLive)
        {
            return;
        }

        if (!(value is Procedure)
            && !(value is UnpurePureContainer)
            && !ReferenceEquals(value, CalculationInProgress))
        {
            SchemeUtilities.TypeCheckAssignment(symbol, value, BackendTypeSymbol);
        }

        // grob-interface.cc's check, ported by EPG22. Upstream runs it beside the type
        // check under do_internal_type_checking_global; the port keeps its own gate on
        // check-internal-types, because the check walks every interface of every grob on
        // every assignment. (That the TYPE check above is ungated where upstream gates
        // it is a separate, older divergence — see PORT-COVERAGE.)
        if (GrobInterface.IsCheckingEnabled())
        {
            GrobInterface.CheckInterfacesForProperty(this, symbol);
        }

        _mutablePropertyAlist = SchemeUtilities.AssqSet(_mutablePropertyAlist, symbol, value);
    }

    /// <summary>Writes a property by name.</summary>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value to store.</param>
    public void SetProperty(string name, object value) => SetProperty(Symbol.Intern(name), value);

    /// <summary>Removes a property from the mutable alist.</summary>
    /// <param name="symbol">The property name.</param>
    public void DeleteProperty(Symbol symbol)
        => _mutablePropertyAlist = SchemeUtilities.AssqRemove(_mutablePropertyAlist, symbol);

    /// <summary>Reads a link to another grob.</summary>
    /// <param name="symbol">The link name.</param>
    /// <returns>The linked value, or the empty list when unset.</returns>
    public object GetObject(Symbol symbol)
    {
        Pair handle = SchemeUtilities.Assq(symbol, _objectAlist);
        object value = handle == null ? Nil.Instance : handle.Cdr;

        if (value is UnpurePureContainer container)
        {
            value = container.Unpure;
        }

        if (value is Procedure)
        {
            value = TryCallbackOnObjectAlist(symbol, value);
        }

        return value;
    }

    /// <summary>Reads a link to another grob by name.</summary>
    /// <param name="name">The link name.</param>
    /// <returns>The linked value.</returns>
    public object GetObject(string name) => GetObject(Symbol.Intern(name));

    /// <summary>Writes a link to another grob.</summary>
    /// <param name="symbol">The link name.</param>
    /// <param name="value">The value to store.</param>
    public void SetObject(Symbol symbol, object value)
    {
        /* Perhaps we simply do the assq_set, but what the heck. */
        if (!IsLive)
        {
            return;
        }

        _objectAlist = SchemeUtilities.AssqSet(_objectAlist, symbol, value);
    }

    /// <summary>Writes a link to another grob by name.</summary>
    /// <param name="name">The link name.</param>
    /// <param name="value">The value to store.</param>
    public void SetObject(string name, object value) => SetObject(Symbol.Intern(name), value);

    /// <summary>Determines whether this grob implements an interface.</summary>
    /// <param name="interfaceName">The interface symbol.</param>
    /// <returns><see langword="true"/> when the interface is listed.</returns>
    public bool HasInterface(Symbol interfaceName)
    {
        object cursor = _interfaces;
        while (cursor is Pair pair)
        {
            if (ReferenceEquals(pair.Car, interfaceName))
            {
                return true;
            }

            cursor = pair.Cdr;
        }

        return false;
    }

    /// <summary>Determines whether this grob implements an interface.</summary>
    /// <param name="interfaceName">The interface name.</param>
    /// <returns><see langword="true"/> when the interface is listed.</returns>
    public bool HasInterface(string interfaceName) => HasInterface(Symbol.Intern(interfaceName));

    /// <summary>Adds an interface to this grob.</summary>
    /// <param name="interfaceName">The interface symbol.</param>
    public void AddInterface(Symbol interfaceName)
        => _interfaces = new Pair(interfaceName, _interfaces);

    /// <summary>
    /// Kills the grob: it keeps its identity so that references to it can still be
    /// rearranged, but loses every property except its cause, which is preserved so a
    /// later diagnostic can still name a source location.
    /// </summary>
    public void Suicide()
    {
        if (!IsLive)
        {
            return;
        }

        for (int a = 0; a < Axes.Count; a++)
        {
            _dimensionCache[a].Clear();
        }

        // Preserve the cause for debugging.  For example, using a dead grob might
        // trigger a programming error, and it could be very helpful to know a source
        // location.
        Pair causeEntry = SchemeUtilities.Assq(CauseSymbol, _mutablePropertyAlist);
        _mutablePropertyAlist = causeEntry != null
            ? Pair.List(causeEntry)
            : Nil.Instance;
        _objectAlist = Nil.Instance;
        _immutablePropertyAlist = Nil.Instance;
        _interfaces = Nil.Instance;
    }

    /// <summary>Moves the grob along one axis, relative to its parent.</summary>
    /// <param name="amount">The distance to move.</param>
    /// <param name="axis">The axis to move along.</param>
    public void TranslateAxis(double amount, Axis axis)
    {
        if (!double.IsFinite(amount))
        {
            Warn.ProgrammingError("Infinity or NaN encountered");
            return;
        }

        DimensionCache cache = _dimensionCache[(int)axis];
        cache.Offset = cache.Offset.HasValue ? cache.Offset.Value + amount : amount;
    }

    /// <summary>Returns this grob's offset on one axis relative to a reference point.</summary>
    /// <param name="reference">The reference grob, or null for the root.</param>
    /// <param name="axis">The axis to measure on.</param>
    /// <returns>The offset.</returns>
    public double RelativeCoordinate(Grob reference, Axis axis)
    {
        // refp should really always be non-null, but this
        // does not hold currently.
        double result = 0.0;
        for (Grob ancestor = this; !ReferenceEquals(ancestor, reference); ancestor = ancestor.GetParent(axis))
        {
            // !ancestor here means that we asked for a coordinate
            // relative to something that is not a reference point.
            if (ancestor == null)
            {
                break;
            }

            result += ancestor.GetOffset(axis);
        }

        return result;
    }

    /// <summary>
    /// Returns the system this grob ended up on. A plain grob belongs to none; items
    /// and spanners answer through their columns and bounds respectively, which is why
    /// the answer is <see langword="null"/> until line breaking has assigned them.
    /// </summary>
    /// <returns>The system, or <see langword="null"/>.</returns>
    public virtual SystemGrob GetSystem() => null;

    /// <summary>
    /// Returns the system a grob is typeset into, by walking horizontal parents to the
    /// root.
    /// <para>
    /// More reliable than <see cref="GetSystem()"/> BEFORE line breaking, when no grob
    /// has been assigned to a line yet and the only thing connecting a grob to its
    /// system is the parent chain.
    /// </para>
    /// </summary>
    /// <param name="me">The grob to start from.</param>
    /// <returns>The system, or <see langword="null"/> when the chain does not end at one.</returns>
    public static SystemGrob SystemOf(Grob me)
    {
        if (me == null)
        {
            return null;
        }

        Grob parent = me.GetParent(Axis.X);
        return parent != null ? SystemOf(parent) : me as SystemGrob;
    }

    /// <summary>Returns this grob's parent's offset relative to a reference point.</summary>
    /// <param name="reference">The reference grob.</param>
    /// <param name="axis">The axis to measure on.</param>
    /// <returns>The offset, or zero when there is no parent.</returns>
    public double ParentRelative(Grob reference, Axis axis)
    {
        Grob parent = GetParent(axis);
        return parent != null ? parent.RelativeCoordinate(reference, axis) : 0.0;
    }

    /// <summary>
    /// Calls a property value as a PURE function — upstream's free function
    /// <c>call_pure_function</c> from <c>lily/grob-property.cc</c>.
    /// <para>
    /// An unpure-pure container whose pure half is omitted answers through its unpure
    /// half; otherwise the pure half is called with <c>(grob, start, end, rest…)</c>.
    /// A value that is not a procedure answers itself, and a BARE procedure — one with
    /// no pure half declared — answers <see langword="false"/> rather than being
    /// called, because calling it would not be pure.
    /// </para>
    /// </summary>
    /// <param name="value">The property value to call.</param>
    /// <param name="args">The arguments, the first of which is the grob.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The pure value.</returns>
    public static object CallPureFunction(object value, IReadOnlyList<object> args, int start, int end)
    {
        if (value is UnpurePureContainer upc)
        {
            if (upc.IsPureOmitted)
            {
                // Don't bother forming an Unpure_pure_call here.
                object unpure = upc.Unpure;

                return SchemeUtilities.IsProcedure(unpure)
                    ? SchemeUtilities.CallCallback(unpure, ToArray(args))
                    : unpure;
            }

            object pure = upc.Pure;
            if (SchemeUtilities.IsProcedure(pure))
            {
                object[] pureArgs = new object[args.Count + 2];
                pureArgs[0] = args.Count > 0 ? args[0] : null;
                pureArgs[1] = (long)start;
                pureArgs[2] = (long)end;
                for (int i = 1; i < args.Count; i++)
                {
                    pureArgs[i + 2] = args[i];
                }

                return SchemeUtilities.CallCallback(pure, pureArgs);
            }

            return pure;
        }

        if (!SchemeUtilities.IsProcedure(value))
        {
            return value;
        }

        return false;
    }

    private static object[] ToArray(IReadOnlyList<object> args)
    {
        object[] result = new object[args.Count];
        for (int i = 0; i < args.Count; i++)
        {
            result[i] = args[i];
        }

        return result;
    }

    /// <summary>
    /// Returns this grob's PURE vertical offset relative to a reference point —
    /// <c>Grob::pure_relative_y_coordinate</c>.
    /// <para>
    /// Positioning-done is simulated when this grob is the child of a vertical
    /// alignment, but only when there is no cached offset: a cached offset means the
    /// alignment was fixed and the translation has already been folded in.
    /// </para>
    /// </summary>
    /// <param name="refp">The reference grob.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The pure offset.</returns>
    public double PureRelativeYCoordinate(Grob refp, int start, int end)
    {
        if (ReferenceEquals(refp, this))
        {
            return 0.0;
        }

        double off;
        DimensionCache cache = _dimensionCache[(int)Axis.Y];
        bool hadCachedOffset = cache.Offset.HasValue;

        if (hadCachedOffset)
        {
            if (SchemeUtilities.ToBool(GetProperty(PureYOffsetInProgressSymbol)))
            {
                Warn.ProgrammingError("cyclic chain in pure-Y-offset callbacks");
            }

            off = cache.Offset.Value;
        }
        else
        {
            object proc = GetPropertyData(YOffsetSymbol);

            cache.Offset = 0.0;
            SetProperty(PureYOffsetInProgressSymbol, true);
            off = ToDoubleOrZero(CallPureFunction(proc, new object[] { this }, start, end));
            DeleteProperty(PureYOffsetInProgressSymbol);
            cache.Offset = null;
        }

        /* we simulate positioning-done if we are the child of a VerticalAlignment,
           but only if we don't have a cached offset. If we do have a cached offset,
           it probably means that the Alignment was fixed and it has already been
           calculated.
        */
        Grob p = GetParent(Axis.Y);
        if (p != null)
        {
            double trans = 0.0;
            if (p.HasInterface(AlignInterfaceSymbol) && !hadCachedOffset)
            {
                trans = AlignInterface.GetPureChildYTranslation(p, this, start, end);
            }

            return off + trans + p.PureRelativeYCoordinate(refp, start, end);
        }

        return off;
    }

    /// <summary>
    /// Returns this grob's PURE vertical extent relative to a reference point — the
    /// extent it would have if the line broke where the caller says.
    /// </summary>
    /// <remarks>
    /// EPG12 (2026-08-08) carried this: <c>grob.cc</c>'s ledger row has said
    /// <c>ported</c> since EPG0, but this function had never come across, because
    /// <c>Slur::pure_height</c> and <c>Slur::pure_outside_slur_callback</c> are the
    /// port's first callers. It reads <c>Y-extent</c> through
    /// <see cref="CallPureFunction"/>, which is no longer a stand-in: EPG15 landed
    /// <c>unpure-pure-container.cc</c>, so a grob with a genuine pure callback is now
    /// measured by it rather than by its ordinary extent.
    /// </remarks>
    /// <param name="refp">The reference grob.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The pure extent.</returns>
    public Interval PureYExtent(Grob refp, int start, int end)
    {
        object ivScm = CallPureFunction(
            GetPropertyData(YExtentSymbol), new object[] { this }, start, end);
        Interval iv = TryNumberPair(ivScm, out Interval read) ? read : Interval.Empty;
        double offset = PureRelativeYCoordinate(refp, start, end);

        object minExt = GetProperty(MinimumYExtentSymbol);

        /* we don't add minimum-Y-extent if the extent is empty. This solves
           a problem with Hara-kiri spanners. They would request_suicide and
           return empty extents, but we would force them here to be large. */
        if (!iv.IsEmpty && TryNumberPair(minExt, out Interval minInterval))
        {
            iv.Unite(minInterval);
        }

        if (!iv.IsEmpty)
        {
            iv.Translate(offset);
        }

        return iv;
    }

    /// <summary>
    /// Reads a property the PURE way, over a range of columns —
    /// <c>Grob::internal_get_pure_property</c>.
    /// <para>
    /// A procedure is called through <see cref="CallPureFunction"/>; an
    /// unpure-pure container whose pure half was omitted is read as an ORDINARY
    /// property, which is upstream's own caching shortcut for a function that ignores
    /// its two column arguments; anything else is the value itself.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Another half of <c>grob-property.cc</c> that had never come across, found by
    /// EPG15's close-out (2026-08-08) when the real pure path in
    /// <c>Align_interface::get_skylines</c> started demanding
    /// <c>ly:grob-pure-property</c>. Nothing had asked for it while every pure read was
    /// answered by an ordinary extent.
    /// </remarks>
    /// <param name="sym">The property to read.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The pure value.</returns>
    public object GetPureProperty(Symbol sym, int start, int end)
    {
        object val = GetPropertyData(sym);

        if (SchemeUtilities.IsProcedure(val))
        {
            return CallPureFunction(val, new object[] { this }, start, end);
        }

        if (val is UnpurePureContainer upc)
        {
            // Do cache, if the function ignores 'start' and 'end'.
            return upc.IsPureOmitted
                ? GetProperty(sym)
                : CallPureFunction(val, new object[] { this }, start, end);
        }

        return val;
    }

    /// <summary>
    /// Returns the offset from this grob's parent, running the offset callback once
    /// and folding its answer into the cache.
    /// </summary>
    /// <param name="axis">The axis to measure on.</param>
    /// <returns>The offset.</returns>
    public double GetOffset(Axis axis)
    {
        DimensionCache cache = _dimensionCache[(int)axis];
        if (cache.Offset.HasValue)
        {
            return cache.Offset.Value;
        }

        Symbol symbol = axis == Axis.X ? XOffsetSymbol : YOffsetSymbol;
        cache.Offset = 0.0;

        // The callback may itself call TranslateAxis, which is why the cache is
        // seeded with zero first and the callback's answer is ADDED afterwards.
        double off = ToDoubleOrZero(GetProperty(symbol));
        if (cache.Offset.HasValue)
        {
            cache.Offset = cache.Offset.Value + off;
            DeleteProperty(symbol);
            return cache.Offset.Value;
        }

        return 0.0;
    }

    /// <summary>Returns this grob's extent on one axis, relative to a reference point.</summary>
    /// <param name="reference">The reference grob.</param>
    /// <param name="axis">The axis to measure on.</param>
    /// <returns>The extent.</returns>
    public Interval Extent(Grob reference, Axis axis)
    {
        double offset = RelativeCoordinate(reference, axis);
        DimensionCache cache = _dimensionCache[(int)axis];
        Interval realExtent;

        if (cache.Extent.HasValue)
        {
            realExtent = cache.Extent.Value;
        }
        else
        {
            realExtent = Interval.Empty;

            /*
              Order is significant: ?-extent may trigger suicide.
             */
            object ext = GetProperty(axis == Axis.X ? XExtentSymbol : YExtentSymbol);
            if (TryNumberPair(ext, out Interval extInterval))
            {
                realExtent.Unite(extInterval);
            }

            object minExt = GetProperty(axis == Axis.X ? MinimumXExtentSymbol : MinimumYExtentSymbol);
            if (TryNumberPair(minExt, out Interval minInterval))
            {
                realExtent.Unite(minInterval);
            }

            cache.Extent = realExtent;
        }

        // We never want nan, so we avoid shifting infinite values.
        if (!double.IsInfinity(offset))
        {
            realExtent.Translate(offset);
        }
        else
        {
            Warn.Warning("ignored infinite " + (axis == Axis.X ? "X" : "Y") + "-offset");
        }

        return realExtent;
    }

    /// <summary>Discards the cached extent on one axis, and its parents' too.</summary>
    /// <param name="axis">The axis to flush.</param>
    public void FlushExtentCache(Axis axis)
    {
        DimensionCache cache = _dimensionCache[(int)axis];
        if (cache.Extent.HasValue)
        {
            /*
              Ugh, this is not accurate; will flush property, causing
              callback to be called if.
             */
            DeleteProperty(axis == Axis.X ? XExtentSymbol : YExtentSymbol);
            cache.Extent = null;
            GetParent(axis)?.FlushExtentCache(axis);
        }
    }

    /// <summary>
    /// Determines whether this grob spans staves, by looking for a vertical alignment
    /// between it and a common reference point. An <c>align-interface</c> grob on that
    /// chain is what separates one staff from another, so meeting one means the two ends
    /// live on different staves.
    /// </summary>
    /// <param name="commony">The common vertical reference point.</param>
    /// <returns><see langword="true"/> when the grob is cross-staff.</returns>
    public bool CheckCrossStaff(Grob commony)
    {
        if (commony != null && commony.HasInterface(AlignInterfaceSymbol))
        {
            return true;
        }

        for (Grob g = this; g != null && !ReferenceEquals(g, commony); g = g.GetParent(Axis.Y))
        {
            if (g.HasInterface(AlignInterfaceSymbol))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the nearest grob that is an ancestor of both this one and another, on
    /// one axis.
    /// </summary>
    /// <param name="other">The other grob.</param>
    /// <param name="axis">The axis whose parent chain to walk.</param>
    /// <returns>The common reference point, or <see langword="null"/> when there is none.</returns>
    public Grob CommonRefpoint(Grob other, Axis axis)
    {
        /* Catching the trivial cases is likely costlier than just running
           through: one can't avoid going to the respective chain ends
           anyway. */
        int balance = 0;
        Grob c;
        Grob d;

        for (c = this; c != null; balance++)
        {
            c = c.GetParent(axis);
        }

        for (d = other; d != null; balance--)
        {
            d = d.GetParent(axis);
        }

        /* Cut down ancestry to same size */
        for (c = this; balance > 0; balance--)
        {
            c = c.GetParent(axis);
        }

        for (d = other; balance < 0; balance++)
        {
            d = d.GetParent(axis);
        }

        /* Now find point where our lineages converge */
        while (!ReferenceEquals(c, d))
        {
            c = c?.GetParent(axis);
            d = d?.GetParent(axis);
        }

        return c;
    }

    /// <summary>Determines whether a grob lies on this one's parent chain.</summary>
    /// <param name="possibleAncestor">The grob to look for.</param>
    /// <param name="axis">The axis whose parent chain to walk.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public bool HasInAncestry(Grob possibleAncestor, Axis axis)
    {
        for (Grob g = this; g != null; g = g.GetParent(axis))
        {
            if (ReferenceEquals(g, possibleAncestor))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets a value indicating whether this grob is drawn.</summary>
    public bool IsTransparent => SchemeUtilities.ToBool(GetProperty(TransparentSymbol));

    /// <summary>Returns the grob's stencil, or null when it has none or is dead.</summary>
    /// <returns>The stencil.</returns>
    public Stencil? GetStencil()
    {
        if (!IsLive)
        {
            return null;
        }

        object stencil = GetProperty(StencilSymbol);
        return stencil is Stencil s ? s : (Stencil?)null;
    }

    /// <summary>
    /// Returns the stencil actually handed to the backend: the grob's own stencil
    /// wrapped in its cause, rotation, colour and output attributes.
    /// <para>
    /// The <c>grob-cause</c> wrapper is what makes point-and-click possible — it hands
    /// the backend the grob that produced each piece of geometry, at draw time.
    /// </para>
    /// </summary>
    /// <returns>The stencil to draw.</returns>
    public Stencil GetPrintStencil()
    {
        object stencilValue = GetProperty(StencilSymbol);

        Stencil result = Stencil.Empty;
        if (!(stencilValue is Stencil stencil))
        {
            return result;
        }

        result = stencil;
        bool transparent = IsTransparent;

        if (transparent)
        {
            result = new Stencil(stencil.ExtentBox, Nil.Instance);
        }
        else
        {
            object expr = Pair.List(GrobCauseSymbol, this, result.Expression);
            result = new Stencil(result.ExtentBox, expr);
        }

        object rotation = GetProperty(RotationSymbol);
        if (rotation is Pair)
        {
            List<object> parts = Pair.ToList(rotation);
            if (parts.Count >= 3)
            {
                double angle = SchemeConvert.ToDouble(parts[0], "rotation");
                double x = SchemeConvert.ToDouble(parts[1], "rotation");
                double y = SchemeConvert.ToDouble(parts[2], "rotation");
                result.RotateDegrees(angle, new Offset(x, y));
            }
        }

        /* color support... see interpret_stencil_expression () for more... */
        object color = GetProperty(ColorSymbol);
        if (color is string cssColor)
        {
            result = result.InColor(cssColor);
        }
        else if (color is MutableString mutableColor)
        {
            result = result.InColor(mutableColor.ToString());
        }
        else if (color is Pair)
        {
            List<object> components = Pair.ToList(color);
            if (components.Count >= 3)
            {
                result = result.InColor(
                    SchemeConvert.ToDouble(components[0], "color"),
                    SchemeConvert.ToDouble(components[1], "color"),
                    SchemeConvert.ToDouble(components[2], "color"),
                    components.Count > 3 ? SchemeConvert.ToDouble(components[3], "color") : 1.0);
            }
        }

        object attributes = GetProperty(OutputAttributesSymbol);
        if (attributes is Pair)
        {
            object expr = Pair.List(OutputAttributesSymbol, attributes, result.Expression);
            result = new Stencil(result.ExtentBox, expr);
        }

        return result;
    }

    /// <summary>Returns the event that caused this grob, if any.</summary>
    /// <returns>The stream event, or <see langword="null"/>.</returns>
    public StreamEvent EventCause()
    {
        object cause = GetProperty(CauseSymbol);
        return cause as StreamEvent;
    }

    /// <summary>
    /// Returns the event that ultimately caused this grob, following the chain through
    /// any grob causes on the way.
    /// </summary>
    /// <returns>The stream event, or <see langword="null"/>.</returns>
    public StreamEvent UltimateEventCause()
    {
        object cause = GetProperty(CauseSymbol);
        while (cause is Grob grob)
        {
            cause = grob.GetProperty(CauseSymbol);
        }

        return cause as StreamEvent;
    }

    /// <summary>
    /// Gives a prebroken piece the object links that belong with its side of the break.
    /// <para>
    /// Upstream keeps this on <see cref="Grob"/> rather than in the derived class
    /// deliberately — its own comment says so — because it is the one place that reaches
    /// into another grob's object alist.
    /// </para>
    /// </summary>
    public virtual void HandlePrebrokenDependencies()
    {
        /* Don't do this in the derived method, since we want to keep access to
           object_alist_ centralized.  */
        if (Original is Grob original && this is Item item)
        {
            _objectAlist = BreakSubstitution.SubstituteObjectAlist(
                item.BreakStatusDirection(), original._objectAlist);
        }
    }

    /// <summary>
    /// The range of SYSTEM ranks this grob is alive on.
    /// <para>
    /// ABSTRACT, exactly as upstream declares it (<c>= 0</c> in <c>grob.hh</c>): an item
    /// answers from its own system or from its prebroken pieces', a spanner from the
    /// pieces it was broken into, and there is no meaningful answer for a bare grob.
    /// <see cref="Grob"/> being abstract in this port is what lets that translate
    /// literally rather than as a virtual with an invented default.
    /// </para>
    /// </summary>
    /// <returns>The system-rank range.</returns>
    public abstract Slice SpannedSystemRankInterval();

    /// <summary>
    /// Gets the piece of this grob that is relevant to a line running from
    /// <paramref name="start"/> to <paramref name="end"/>, or <see langword="null"/> when
    /// it would not be visible on such a line.
    /// <para>
    /// ABSTRACT, as upstream declares it. A spanner always answers itself; an ITEM sitting
    /// exactly on either end of the line answers the prebroken piece facing INTO the line,
    /// and then only if that piece is break-visible — which is how a clef that only prints
    /// at the start of a line stops being measured in the middle of one.
    /// </para>
    /// </summary>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The relevant piece, or <see langword="null"/>.</returns>
    public abstract Grob PureFindVisiblePrebrokenPiece(int start, int end);

    /// <summary>
    /// Breaks this grob into the pieces line breaking asks for.
    /// <para>
    /// Empty on <see cref="Grob"/>, exactly as upstream: an ITEM is broken before line
    /// breaking by <c>System::break_breakable_item</c>, so only <see cref="Spanner"/>
    /// overrides this.
    /// </para>
    /// </summary>
    public virtual void DoBreakProcessing()
    {
    }

    /// <summary>
    /// Re-points this grob's parents at the pieces that live on its own system.
    /// <para>
    /// Two separate fixups, and both are needed. A parent on a DIFFERENT system is
    /// replaced by its piece on this one; and an ITEM whose parent is an item with a
    /// different break direction is replaced by that parent's prebroken piece for this
    /// side of the break.
    /// </para>
    /// </summary>
    public void FixupRefpoint()
    {
        foreach (Axis ax in new[] { Axis.X, Axis.Y })
        {
            Grob parent = GetParent(ax);

            if (parent == null)
            {
                continue;
            }

            if (!ReferenceEquals(parent.GetSystem(), GetSystem()) && GetSystem() != null)
            {
                Grob newParent = parent.FindBrokenPiece(GetSystem());
                SetParent(newParent, ax);
            }

            if (this is Item i)
            {
                if (parent is Item parenti)
                {
                    Direction myDir = i.BreakStatusDirection();
                    if (myDir != parenti.BreakStatusDirection())
                    {
                        Item newParent = parenti.FindPrebrokenPiece(myDir);
                        SetParent(newParent, ax);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Re-points this grob's object links at the pieces that live on its own system, and
    /// kills the grob when it turns out not to belong to any.
    /// <para>
    /// Three cases, in upstream's order. A spanner missing a bound is suicided outright —
    /// upstream's own comment says this is what keeps the corner case out of the
    /// algorithm. A spanner that is itself a broken PIECE does nothing, because its
    /// original does the work for it. And an original spanner substitutes each of its
    /// mutable object properties into every piece, through
    /// <see cref="Spanner.SubstituteOneMutableProperty"/> rather than the generic walk,
    /// because those lists get enormous in orchestral scores.
    /// </para>
    /// <para>
    /// What is left after that is this grob's OWN links, which are substituted for its
    /// own system — and if it has no system, or does not share a reference point with
    /// one on both axes, it has been removed from everything that referred to it and is
    /// junked.
    /// </para>
    /// </summary>
    public virtual void HandleBrokenDependencies()
    {
        Spanner sp = this as Spanner;

        /* Skipping break substitution in case sp is lacking a bound
           allows not to have to care about this corner case in the
           algorithm.
         */
        if (sp != null
            && !(sp.GetBound(Direction.Negative) != null && sp.GetBound(Direction.Positive) != null))
        {
            Suicide();
            return;
        }

        if (Original != null && sp != null)
        {
            return;
        }

        if (sp != null)
        {
            /* THIS, SP is the original spanner. We use a special function
               because some Spanners have enormously long lists in their
               properties, and a special function fixes FOO */
            for (object s = _objectAlist; s is Pair pair; s = pair.Cdr)
            {
                if (pair.Car is Pair entry)
                {
                    sp.SubstituteOneMutableProperty(entry.Car as Symbol, entry.Cdr);
                }
            }
        }

        SystemGrob system = GetSystem();

        if (IsLive
            && system != null
            && CommonRefpoint(system, Axis.X) != null
            && CommonRefpoint(system, Axis.Y) != null)
        {
            SubstituteObjectLinks(system, _objectAlist);
        }
        else
        {
            /* THIS element is `invalid'; it has been removed from all
               dependencies, so let's junk the element itself. */
            Suicide();
        }
    }

    /// <summary>
    /// Replaces this grob's object alist with one whose links point at the pieces living
    /// on a given system — <c>Grob::substitute_object_links (System *, SCM)</c>.
    /// </summary>
    /// <param name="crit">The system to substitute for.</param>
    /// <param name="orig">The alist to substitute through.</param>
    internal void SubstituteObjectLinks(SystemGrob crit, object orig)
        => _objectAlist = BreakSubstitution.SubstituteObjectAlist(crit, orig);

    /// <summary>
    /// Replaces this grob's object alist with one whose links point at the prebroken
    /// pieces for a given break direction — <c>Grob::substitute_object_links (Direction,
    /// SCM)</c>.
    /// </summary>
    /// <param name="crit">The break direction to substitute for.</param>
    /// <param name="orig">The alist to substitute through.</param>
    internal void SubstituteObjectLinks(Direction crit, object orig)
        => _objectAlist = BreakSubstitution.SubstituteObjectAlist(crit, orig);

    /// <summary>
    /// Reads a property the pure or the ordinary way, as the caller asks —
    /// <c>Grob::internal_get_maybe_pure_property</c>.
    /// </summary>
    /// <param name="sym">The property to read.</param>
    /// <param name="pure">Whether to read purely.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The value.</returns>
    public object GetMaybePureProperty(Symbol sym, bool pure, int start, int end)
        => pure ? GetPureProperty(sym, start, end) : GetProperty(sym);

    /// <summary>
    /// Returns a coordinate that is PURE when asked for on the Y axis and ordinary
    /// otherwise — <c>Grob::maybe_pure_coordinate</c>.
    /// </summary>
    /// <param name="refp">The reference grob.</param>
    /// <param name="a">The axis.</param>
    /// <param name="pure">Whether a pure answer is wanted.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The coordinate.</returns>
    public double MaybePureCoordinate(Grob refp, Axis a, bool pure, int start, int end)
    {
        if (pure && a != Axis.Y)
        {
            Warn.ProgrammingError("tried to get pure X-offset");
        }

        return pure && a == Axis.Y
            ? PureRelativeYCoordinate(refp, start, end)
            : RelativeCoordinate(refp, a);
    }

    /// <summary>
    /// Returns an extent that is PURE when asked for on the Y axis and ordinary
    /// otherwise — <c>Grob::maybe_pure_extent</c>.
    /// </summary>
    /// <param name="refp">The reference grob.</param>
    /// <param name="a">The axis.</param>
    /// <param name="pure">Whether a pure answer is wanted.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The extent.</returns>
    public Interval MaybePureExtent(Grob refp, Axis a, bool pure, int start, int end)
        => pure && a == Axis.Y ? PureYExtent(refp, start, end) : Extent(refp, a);

    /// <summary>
    /// Gets where in the source this grob ultimately came from — <c>Grob::origin</c>,
    /// the hook upstream's <c>Diagnostics</c> base class reports every grob diagnostic
    /// at.
    /// </summary>
    public Input Origin => UltimateEventCause()?.Origin as Input;

    /// <summary>
    /// Reports a warning at this grob's origin — upstream's
    /// <c>Diagnostics::warning</c> over <c>Grob::origin</c>.
    /// <para>
    /// The location is the point: a bare warning about a grob names no bar and no file,
    /// and upstream's grob diagnostics all carry one.
    /// </para>
    /// </summary>
    /// <param name="message">The message.</param>
    public void Warning(string message)
    {
        Input origin = Origin;
        if (origin != null)
        {
            origin.Warning(message);
        }
        else
        {
            Warn.Warning(message);
        }
    }

    /// <summary>
    /// Gets where this grob came from, for <see cref="IDiagnostics"/>.
    /// <para>
    /// Upstream declares <c>class Grob : public Smob&lt;Grob&gt;, public Diagnostics</c>,
    /// so a grob IS one of these. The port grew Warning/ProgrammingError/Origin as
    /// members without ever declaring the interface, which meant a caller holding a Grob
    /// could not reach the diagnostic surface generically — and, worse, an
    /// <c>(IDiagnostics)</c> cast on a grob COMPILED and threw at run time. EPG18 met
    /// exactly that; the fix is to declare what upstream declares.
    /// </para>
    /// </summary>
    /// <returns>The origin, or <see langword="null"/>.</returns>
    Input IDiagnostics.Origin() => Origin;

    /// <summary>Reports an internal error at this grob's origin.</summary>
    /// <param name="message">The message.</param>
    public void ProgrammingError(string message)
    {
        Input origin = Origin;
        if (origin != null)
        {
            origin.ProgrammingError(message);
        }
        else
        {
            Warn.ProgrammingError(message);
        }
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The grob's name and class.</returns>
    public override string ToString() => "#<Grob " + Name + ">";

    /// <summary>
    /// Returns the property alist chain used for lookups that fall back through a
    /// default alist.
    /// </summary>
    /// <param name="defaults">The default alist to fall back on.</param>
    /// <returns>The three-element chain.</returns>
    public object GetPropertyAlistChain(object defaults)
        => Pair.List(_mutablePropertyAlist, _immutablePropertyAlist, defaults);

    /// <summary>Returns the geometry cache for one axis. Exposed for the layout code.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>The cache.</returns>
    protected DimensionCache Cache(Axis axis) => _dimensionCache[(int)axis];

    private object TryCallbackOnAlist(Symbol symbol, object procedure)
    {
        /*
          need to put a value in SYM to ensure that we don't get a
          cyclic call chain.
        */
        _mutablePropertyAlist = SchemeUtilities.AssqSet(_mutablePropertyAlist, symbol, CalculationInProgress);

        object value = CallProcedure(procedure);

        SetProperty(symbol, value);
        return value;
    }

    private object TryCallbackOnObjectAlist(Symbol symbol, object procedure)
    {
        _objectAlist = SchemeUtilities.AssqSet(_objectAlist, symbol, CalculationInProgress);
        object value = CallProcedure(procedure);
        SetObject(symbol, value);
        return value;
    }

    private object CallProcedure(object procedure)
    {
        Interpreter interpreter = LilyPondScheme.Current;
        if (interpreter == null)
        {
            return Nil.Instance;
        }

        return interpreter.Evaluator.Apply(procedure, new object[] { this });
    }

    private static object AlistCopy(object alist)
    {
        List<object> entries = new List<object>();
        object cursor = alist;
        while (cursor is Pair pair)
        {
            entries.Add(pair.Car is Pair entry ? new Pair(entry.Car, entry.Cdr) : pair.Car);
            cursor = pair.Cdr;
        }

        object result = cursor ?? Nil.Instance;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            result = new Pair(entries[i], result);
        }

        return result;
    }

    private static double ToDoubleOrZero(object value)
    {
        switch (value)
        {
            case double d:
                return d;
            case long l:
                return l;
            case int i:
                return i;
            default:
                return 0.0;
        }
    }

    /// <summary>
    /// Reads a Scheme number pair as an interval, which is how every extent-shaped
    /// property is stored.
    /// </summary>
    /// <param name="value">The value to read.</param>
    /// <param name="interval">The interval read, or the empty interval.</param>
    /// <returns><see langword="true"/> when the value was a pair of real numbers.</returns>
    public static bool TryNumberPair(object value, out Interval interval)
    {
        interval = Interval.Empty;
        if (!(value is Pair pair))
        {
            return false;
        }

        if (!IsRealNumber(pair.Car) || !IsRealNumber(pair.Cdr))
        {
            return false;
        }

        interval = new Interval(
            SchemeConvert.ToDouble(pair.Car, "number-pair"),
            SchemeConvert.ToDouble(pair.Cdr, "number-pair"));
        return true;
    }

    private static bool IsRealNumber(object value)
        => value is double || value is long || value is int
           || value is System.Numerics.BigInteger || value is CodeBrix.LilyScheme.Numeric.Ratio;
}
