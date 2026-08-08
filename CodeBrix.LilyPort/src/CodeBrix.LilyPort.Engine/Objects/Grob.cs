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
