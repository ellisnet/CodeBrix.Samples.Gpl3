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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/stencil.cc, lily/include/stencil.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/** a group of individually translated symbols. You can add stencils
    to the top, to the right, etc.

    It is implemented as a "tree" of scheme expressions, as in

    Expr = combine Expr-list
    | translate Offset Expr
    | origin (ORIGIN) Expr
    | no-origin Expr
    | (SCHEME)
    ;

    SCHEME is a Scheme expression that --when eval'd-- produces the
    desired output.

    Notes:

    * Because of the way that Stencil is implemented, it is the most
    efficient to add "fresh" stencils to what you're going to build.

    * Do not create Stencil objects on the heap or attempt to share
    or transfer ownership by pointer.  Either copy Stencil objects
    or use SCM references.

    * Empty stencils have empty dimensions.  If add_at_edge is used to
    init the stencil, we assume that

    DIMENSIONS = (Interval (0, 0), Interval (0, 0)
*/

/// <summary>
/// A device-independent output expression: what to draw, plus how much room it takes.
/// <para>
/// The expression is Scheme data whose head is a registered stencil head -- one of the
/// roughly forty procedures the output backends implement. Nothing here interprets it;
/// the backends do. That separation is what lets one engraving run render to PostScript,
/// SVG or a canvas without the engine knowing which.
/// </para>
/// <para>
/// This is a mutable VALUE type, which is a deliberate divergence recorded in
/// PORT-COVERAGE. Upstream's <c>Stencil</c> is a class copied by value at every
/// assignment — <c>Stencil toadd (s);</c> appears throughout <c>stencil.cc</c> — and
/// its header states outright that C++ must not modify a stencil shared with Scheme.
/// A C# class would have made all of those copies into aliases and silently changed
/// the meaning of the combining routines.
/// </para>
/// </summary>
public struct Stencil : IEquatable<Stencil>
{
    private static readonly Symbol CombineStencil = Symbol.Intern("combine-stencil");
    private static readonly Symbol TranslateStencil = Symbol.Intern("translate-stencil");
    private static readonly Symbol RotateStencil = Symbol.Intern("rotate-stencil");
    private static readonly Symbol ScaleStencil = Symbol.Intern("scale-stencil");
    private static readonly Symbol WithOutlineHead = Symbol.Intern("with-outline");
    private static readonly Symbol ColorSymbol = Symbol.Intern("color");

    private Box _dim;
    private object _expr;

    // Set by every constructor; false only for `default(Stencil)`, which must read
    // back as the empty stencil rather than as a zero-extent one at the origin.
    private bool _initialized;

    /// <summary>Initializes a stencil from its extents and its output expression.</summary>
    /// <param name="box">The extent box.</param>
    /// <param name="expression">The output expression.</param>
    public Stencil(Box box, object expression)
    {
        _dim = box;
        _expr = expression ?? Nil.Instance;
        _initialized = true;
    }

    /// <summary>Initializes a stencil from separate extents and an output expression.</summary>
    /// <param name="expression">The output expression.</param>
    /// <param name="xExtent">The horizontal extent.</param>
    /// <param name="yExtent">The vertical extent.</param>
    public Stencil(object expression, Interval xExtent, Interval yExtent)
        : this(new Box(xExtent, yExtent), expression)
    {
    }

    /// <summary>Gets the empty stencil: no expression, empty extents.</summary>
    public static Stencil Empty
    {
        get
        {
            Stencil s = default;
            s.EnsureInitialized();
            return s;
        }
    }

    /// <summary>Gets the output expression. Never null; the empty list means "nothing".</summary>
    public object Expression
    {
        get
        {
            EnsureInitialized();
            return _expr;
        }
    }

    /// <summary>Gets the horizontal extent.</summary>
    public Interval XExtent => ExtentBox[Axis.X];

    /// <summary>Gets the vertical extent.</summary>
    public Interval YExtent => ExtentBox[Axis.Y];

    /// <summary>Gets the extent box.</summary>
    public Box ExtentBox
    {
        get
        {
            EnsureInitialized();
            return _dim;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the stencil draws nothing — either it has no
    /// expression, or its extents are empty on both axes.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            EnsureInitialized();
            return IsNullExpression(_expr) || _dim.IsEmpty;
        }
    }

    /// <summary>Returns the extent along an axis.</summary>
    /// <param name="axis">The axis to measure.</param>
    /// <returns>The extent.</returns>
    public Interval Extent(Axis axis) => ExtentBox[axis];

    /// <summary>Determines whether the stencil has no extent on one axis.</summary>
    /// <param name="axis">The axis to test.</param>
    /// <returns><see langword="true"/> when that axis carries the empty sentinels.</returns>
    public bool IsEmptyOn(Axis axis)
    {
        EnsureInitialized();
        return _dim.IsEmptyOn(axis);
    }

    /// <summary>
    /// Sets the extents to empty, or to a zero-length interval at the origin.
    /// </summary>
    /// <param name="empty">
    /// <see langword="true"/> for genuinely empty extents; <see langword="false"/> for
    /// the point extents that <see cref="AddAtEdge"/> assumes when it initialises.
    /// </param>
    public void SetEmpty(bool empty)
    {
        EnsureInitialized();

        if (empty)
        {
            _dim.X = Interval.Empty;
            _dim.Y = Interval.Empty;
        }
        else
        {
            _dim.X = new Interval(0, 0);
            _dim.Y = new Interval(0, 0);
        }
    }

    /// <summary>Combines another stencil into this one, without moving either.</summary>
    /// <param name="other">The stencil to add.</param>
    public void AddStencil(Stencil other)
    {
        EnsureInitialized();

        object cs = CombineStencil;
        object otherExpr = other.Expression;

        if (IsNullExpression(_expr))
        {
            _expr = otherExpr;
        }
        else if (IsNullExpression(otherExpr))
        {
            // Nothing to add.
        }
        else if (_expr is Pair selfPair && ReferenceEquals(cs, selfPair.Car))
        {
            _expr = otherExpr is Pair otherPair && ReferenceEquals(cs, otherPair.Car)
                ? Append(otherExpr, selfPair.Cdr)
                : new Pair(cs, new Pair(otherExpr, selfPair.Cdr));
        }
        else
        {
            _expr = otherExpr is Pair otherHead && ReferenceEquals(cs, otherHead.Car)
                ? Append(otherExpr, Pair.List(_expr))
                : Pair.List(cs, otherExpr, _expr);
        }

        _dim.Unite(other.ExtentBox);
    }

    /// <summary>Moves the stencil.</summary>
    /// <param name="offset">The distance to move by.</param>
    public void Translate(Offset offset)
    {
        EnsureInitialized();

        double x = offset.X;
        double y = offset.Y;

        // ugh, hardcoded.
        if (double.IsInfinity(x) || double.IsNaN(x) || Math.Abs(x) > 1e6)
        {
            ReportImprobableOffset(x);
            x = 0.0;
        }

        if (double.IsInfinity(y) || double.IsNaN(y) || Math.Abs(y) > 1e6)
        {
            ReportImprobableOffset(y);
            y = 0.0;
        }

        Offset sane = new Offset(x, y);

        if (!IsNullExpression(_expr))
        {
            _expr = Pair.List(TranslateStencil, OffsetToScm(sane), _expr);
        }

        _dim.Translate(sane);
    }

    /// <summary>Moves the stencil along one axis.</summary>
    /// <param name="amount">The distance to move by.</param>
    /// <param name="axis">The axis to move along.</param>
    public void TranslateAxis(double amount, Axis axis)
        => Translate(Offset.Zero.With(axis, amount));

    /// <summary>Returns a moved copy, leaving this stencil alone.</summary>
    /// <param name="offset">The distance to move by.</param>
    /// <returns>The moved stencil.</returns>
    public Stencil Translated(Offset offset)
    {
        Stencil s = this;
        s.Translate(offset);
        return s;
    }

    /// <summary>Scales the stencil about the origin.</summary>
    /// <param name="x">The horizontal factor.</param>
    /// <param name="y">The vertical factor.</param>
    public void Scale(double x, double y)
    {
        EnsureInitialized();
        _expr = Pair.List(ScaleStencil, Pair.List(x, y), _expr);
        _dim.X *= x;
        _dim.Y *= y;
    }

    /// <summary>
    /// Moves the stencil so that the given relative point on one axis sits at the
    /// origin. A value of -1 aligns the left or bottom edge, 1 the right or top.
    /// </summary>
    /// <param name="axis">The axis to align on.</param>
    /// <param name="position">The relative point, from -1 to 1.</param>
    public void AlignTo(Axis axis, double position)
    {
        EnsureInitialized();

        if (IsEmptyOn(axis))
        {
            return;
        }

        Interval i = Extent(axis);
        TranslateAxis(-i.LinearCombination(position), axis);
    }

    /// <summary>
    /// Rotates the stencil about a point given relative to its own extents, where -1
    /// is the left or bottom edge and 1 the right or top.
    /// </summary>
    /// <param name="degrees">The rotation, anticlockwise.</param>
    /// <param name="relativeOffset">The centre of rotation, relative to the extents.</param>
    public void Rotate(double degrees, Offset relativeOffset)
        => RotateDegrees(degrees, relativeOffset);

    /// <summary>
    /// Rotates the stencil about a point given relative to its own extents.
    /// </summary>
    /// <param name="degrees">The rotation, anticlockwise.</param>
    /// <param name="relativeOffset">The centre of rotation, relative to the extents.</param>
    public void RotateDegrees(double degrees, Offset relativeOffset)
    {
        EnsureInitialized();

        double x = Extent(Axis.X).LinearCombination(relativeOffset.X);
        double y = Extent(Axis.Y).LinearCombination(relativeOffset.Y);
        RotateDegreesAbsolute(degrees, new Offset(x, y));
    }

    /// <summary>Rotates the stencil about a point in its own coordinate system.</summary>
    /// <param name="degrees">The rotation, anticlockwise.</param>
    /// <param name="absoluteOffset">The centre of rotation.</param>
    public void RotateDegreesAbsolute(double degrees, Offset absoluteOffset)
    {
        EnsureInitialized();

        double x = absoluteOffset.X;
        double y = absoluteOffset.Y;

        // Build scheme expression (processed in stencil-interpret.cc).
        _expr = Pair.List(
            RotateStencil,
            Pair.List(degrees, new Pair(x, y)),
            _expr);

        // Calculate the new bounding box.
        Box shiftedBox = _dim;
        shiftedBox.Translate(-absoluteOffset);

        List<Offset> points = new List<Offset>
        {
            new Offset(shiftedBox.X.Left, shiftedBox.Y.Left),
            new Offset(shiftedBox.X.Right, shiftedBox.Y.Left),
            new Offset(shiftedBox.X.Right, shiftedBox.Y.Right),
            new Offset(shiftedBox.X.Left, shiftedBox.Y.Right),
        };

        Offset rotation = Offset.Directed(degrees);
        _dim.SetEmpty();
        for (int i = 0; i < points.Count; i++)
        {
            _dim.AddPoint(Offset.ComplexMultiply(points[i], rotation) + absoluteOffset);
        }
    }

    /// <summary>
    /// Adds another stencil at one edge of this one, leaving a padding gap.
    /// <para>
    /// Material that is empty on the orthogonal axis is spacing, and spacing is exempt
    /// from the padding — that exemption is what makes negative spacing meaningful.
    /// </para>
    /// </summary>
    /// <param name="axis">The axis to add along.</param>
    /// <param name="direction">The edge to add at.</param>
    /// <param name="other">The stencil to add.</param>
    /// <param name="padding">The gap to leave.</param>
    public void AddAtEdge(Axis axis, Direction direction, Stencil other, double padding)
    {
        EnsureInitialized();

        // Material that is empty in the axis of reference has only limited
        // usefulness for combining.  We still retain as much information as
        // available since there may be uses like setting page links or
        // background color or watermarks, and off-axis extents.
        if (IsEmptyOn(axis))
        {
            AddStencil(other);
            return;
        }

        Interval firstExtent = Extent(axis);

        if (other.IsEmptyOn(axis))
        {
            Stencil toAdd = other;

            // translation does not affect axis-empty extent box.
            toAdd.TranslateAxis(firstExtent[direction], axis);
            AddStencil(toAdd);
            return;
        }

        Interval nextExtent = other.Extent(axis);

        bool firstIsSpacing = IsEmptyOn(Axes.Other(axis));
        bool nextIsSpacing = other.IsEmptyOn(Axes.Other(axis));

        double offset = firstExtent[direction] - nextExtent[-direction];

        if (!(firstIsSpacing || nextIsSpacing))
        {
            offset += direction * padding;
        }

        Stencil translated = other;
        translated.TranslateAxis(offset, axis);
        AddStencil(translated);
    }

    /// <summary>
    /// Stacks another stencil onto this one, which is how lines and columns of
    /// stencils are assembled.
    /// <para>
    /// Unlike <see cref="AddAtEdge"/>, the added stencil's REFERENCE POINT normally
    /// lands on this stencil's edge, not its own edge — unless it protrudes backwards,
    /// in which case room is made. Spacing is applied without padding.
    /// </para>
    /// </summary>
    /// <param name="axis">The axis to stack along.</param>
    /// <param name="direction">The side to stack on.</param>
    /// <param name="other">The stencil to stack.</param>
    /// <param name="padding">The gap to leave.</param>
    /// <param name="minimumDistance">The smallest step the stack may take.</param>
    public void Stack(
        Axis axis,
        Direction direction,
        Stencil other,
        double padding,
        double minimumDistance)
    {
        EnsureInitialized();

        // Material that is empty in the axis of reference can't be sensibly
        // stacked.  We just revert to add_at_edge behavior then.
        if (IsEmptyOn(axis))
        {
            Stencil toAdd = other;
            toAdd.AddStencil(this);
            _expr = toAdd.Expression;
            _dim = toAdd.ExtentBox;
            return;
        }

        Interval firstExtent = Extent(axis);

        if (other.IsEmptyOn(axis))
        {
            Stencil toAdd = other;
            toAdd.TranslateAxis(firstExtent[direction], axis);
            toAdd.AddStencil(this);
            _expr = toAdd.Expression;
            _dim = toAdd.ExtentBox;
            return;
        }

        Interval nextExtent = other.Extent(axis);

        // It is somewhat tedious to special-case all spacing, but it turns
        // out that not doing so makes it astonishingly hard to make the
        // code do the correct thing.

        // If first is spacing, we translate second accordingly without
        // letting this affect its backward edge.
        if (IsEmptyOn(Axes.Other(axis)))
        {
            Stencil toAdd = other;

            // Spacing assigns meaning to "intervals" with negative extent,
            // so we cannot use first_extent.length () here
            double spacingOffset = firstExtent[direction] - firstExtent[-direction];
            toAdd.TranslateAxis(spacingOffset, axis);
            toAdd.AddStencil(this);
            _expr = toAdd.Expression;
            _dim = toAdd.ExtentBox;

            Interval updated = _dim[axis];
            updated[-direction] = nextExtent[-direction];
            updated[direction] = nextExtent[direction] + spacingOffset;
            _dim[axis] = updated;
            return;
        }

        // If next is spacing, similar action:
        if (other.IsEmptyOn(Axes.Other(axis)))
        {
            Stencil toAdd = other;
            double spacingOffset = firstExtent[direction];
            toAdd.TranslateAxis(spacingOffset, axis);
            toAdd.AddStencil(this);
            _expr = toAdd.Expression;
            _dim = toAdd.ExtentBox;

            Interval updated = _dim[axis];
            updated[-direction] = firstExtent[-direction];
            updated[direction] = firstExtent[direction] + nextExtent[direction] - nextExtent[-direction];
            _dim[axis] = updated;
            return;
        }

        double offset = firstExtent[direction];

        // If the added stencil has a backwardly protruding edge, we make
        // room for it when combining.
        if (direction * nextExtent[-direction] < 0)
        {
            offset -= nextExtent[-direction];
        }

        offset += direction * padding;

        if (offset * direction < minimumDistance)
        {
            offset = direction * minimumDistance;
        }

        Stencil stacked = other;
        stacked.TranslateAxis(offset, axis);
        stacked.AddStencil(this);
        _expr = stacked.Expression;
        _dim = stacked.ExtentBox;

        Interval final = _dim[axis];
        final[-direction] = firstExtent[-direction];
        final[direction] = nextExtent[direction] + offset;
        _dim[axis] = final;
    }

    /// <summary>Returns a copy wrapped in a colour instruction.</summary>
    /// <param name="red">The red component, from 0 to 1.</param>
    /// <param name="green">The green component, from 0 to 1.</param>
    /// <param name="blue">The blue component, from 0 to 1.</param>
    /// <param name="alpha">The opacity, from 0 to 1.</param>
    /// <returns>The coloured stencil.</returns>
    public Stencil InColor(double red, double green, double blue, double alpha = 1.0)
        => WithColorExpression(Pair.List(red, green, blue, alpha));

    /// <summary>Returns a copy wrapped in a colour instruction naming a CSS colour.</summary>
    /// <param name="cssColor">The CSS colour name.</param>
    /// <returns>The coloured stencil.</returns>
    public Stencil InColor(string cssColor) => WithColorExpression(cssColor);

    /// <summary>
    /// Returns a copy that draws this stencil's contents but presents another
    /// stencil's shape for collision purposes.
    /// </summary>
    /// <param name="outline">The stencil whose shape and extents to present.</param>
    /// <returns>The combined stencil.</returns>
    public Stencil WithOutline(Stencil outline)
        => new Stencil(
            outline.ExtentBox,
            Pair.List(WithOutlineHead, outline.Expression, Expression));

    /// <summary>Determines whether two stencils have the same expression and extents.</summary>
    /// <param name="other">The stencil to compare with.</param>
    /// <returns><see langword="true"/> when both match.</returns>
    public bool Equals(Stencil other)
        => ReferenceEquals(Expression, other.Expression) && ExtentBox.Equals(other.ExtentBox);

    /// <summary>Determines whether this equals another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the object is an equal stencil.</returns>
    public override bool Equals(object obj) => obj is Stencil other && Equals(other);

    /// <summary>Returns a hash code.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => ExtentBox.GetHashCode();

    /// <summary>Tests equality.</summary>
    /// <param name="left">The first stencil.</param>
    /// <param name="right">The second stencil.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(Stencil left, Stencil right) => left.Equals(right);

    /// <summary>Tests inequality.</summary>
    /// <param name="left">The first stencil.</param>
    /// <param name="right">The second stencil.</param>
    /// <returns><see langword="true"/> when not equal.</returns>
    public static bool operator !=(Stencil left, Stencil right) => !left.Equals(right);

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description naming the extents.</returns>
    public override string ToString() => "#<Stencil " + XExtent + " " + YExtent + ">";

    /// <summary>Builds the Scheme representation of an offset: a pair of reals.</summary>
    /// <param name="offset">The offset to convert.</param>
    /// <returns>The pair <c>(x . y)</c>.</returns>
    public static object OffsetToScm(Offset offset) => new Pair(offset.X, offset.Y);

    /// <summary>Determines whether a Scheme value counts as "no expression".</summary>
    /// <param name="expression">The value to test.</param>
    /// <returns><see langword="true"/> for null or the empty list.</returns>
    public static bool IsNullExpression(object expression)
        => expression == null || expression is Nil;

    private Stencil WithColorExpression(object color)
    {
        // Upstream routes this through Scheme's stencil-with-color, which wraps the
        // expression in a `color` head. The head is what the backends implement, so
        // building it directly here keeps the engine off the Scheme call path without
        // changing the output expression at all.
        Stencil result = this;
        result.EnsureInitialized();

        if (IsNullExpression(result._expr))
        {
            return result;
        }

        result._expr = Pair.List(ColorSymbol, color, result._expr);
        return result;
    }

    private static object Append(object list, object tail)
    {
        List<object> items = Pair.ToList(list);
        object result = tail;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            result = new Pair(items[i], result);
        }

        return result;
    }

    private static void ReportImprobableOffset(double amount)
        => Warn.ProgrammingError(
            string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Improbable offset for stencil: {0:F6} staff space",
                amount)
            + "\n"
            + "Setting to zero.");

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _expr = Nil.Instance;
        _dim.SetEmpty();
    }
}
