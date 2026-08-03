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

using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/stencil.cc, lily/include/stencil.hh;

// Modified by Jeremy Ellis on 2026-08-02 as part of the CodeBrix port.

/// <summary>
/// A device-independent output expression: what to draw, plus how much room it takes.
/// <para>
/// The expression is Scheme data whose head is a registered stencil head -- one of the
/// roughly forty procedures the output backends implement. Nothing here interprets it;
/// the backends do. That separation is what lets one engraving run render to PostScript,
/// SVG or a canvas without the engine knowing which.
/// </para>
/// </summary>
public sealed class Stencil
{
    /// <summary>Initializes a stencil.</summary>
    /// <param name="expression">The output expression.</param>
    /// <param name="xExtent">The horizontal extent.</param>
    /// <param name="yExtent">The vertical extent.</param>
    public Stencil(object expression, Interval xExtent, Interval yExtent)
    {
        Expression = expression;
        XExtent = xExtent;
        YExtent = yExtent;
    }

    /// <summary>Initializes an empty stencil.</summary>
    public Stencil()
        : this(null, Interval.Empty, Interval.Empty)
    {
    }

    /// <summary>Gets the output expression.</summary>
    public object Expression { get; }

    /// <summary>Gets the horizontal extent.</summary>
    public Interval XExtent { get; }

    /// <summary>Gets the vertical extent.</summary>
    public Interval YExtent { get; }

    /// <summary>Gets a value indicating whether the stencil draws nothing.</summary>
    public bool IsEmpty => Expression == null;

    /// <summary>Returns the extent along an axis.</summary>
    /// <param name="axis">The axis to measure.</param>
    /// <returns>The extent.</returns>
    public Interval Extent(Axis axis) => axis == Axis.X ? XExtent : YExtent;

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description naming the extents.</returns>
    public override string ToString() => "#<Stencil " + XExtent + " " + YExtent + ">";
}
