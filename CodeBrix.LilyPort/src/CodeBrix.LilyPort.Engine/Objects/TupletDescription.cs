/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2002--2026 Juergen Reuter <reuter@ipd.uka.de>

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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/tuplet-description.cc, lily/include/tuplet-description.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - the smob becomes an ordinary reference type; mark_smob and the GC plumbing have
//     no counterpart and are dropped, while the type predicate ly:tuplet-description?
//     stays, registered in Bootstrap/TypePredicates.cs as the bindings rule requires.

/// <summary>
/// One tuplet the <c>Tuplet_engraver</c> is currently in: the event that started it, the
/// grobs it is building, and the span of time it covers.
/// </summary>
/// <remarks>
/// Tuplets nest, so a description keeps a <see cref="Parent"/> and the engraver holds a
/// stack of these rather than a single current tuplet.
/// </remarks>
public sealed class TupletDescription : IEquatable<TupletDescription>, ISchemeEqual
{
    private static readonly Symbol DenominatorSymbol = Symbol.Intern("denominator");
    private static readonly Symbol LengthSymbol = Symbol.Intern("length");
    private static readonly Symbol NumeratorSymbol = Symbol.Intern("numerator");

    /// <summary>Initializes a description from the event that started the tuplet.</summary>
    /// <param name="streamEvent">The <c>TupletSpanEvent</c> that started it.</param>
    /// <param name="now">The moment the tuplet starts.</param>
    public TupletDescription(StreamEvent streamEvent, Moment now)
    {
        Event = streamEvent;
        StartMoment = now;

        Moment length = streamEvent?.GetProperty(LengthSymbol) is Moment m ? m : Moment.Zero;

        // A tuplet that starts in grace time but whose length carries no grace part is
        // measured in grace time all the same — otherwise its end would land a whole
        // note's worth of main time away from its start.
        StopMoment = now + (StartMoment.GracePart.IsNonZero && !length.GracePart.IsNonZero
            ? new Moment(Rational.Zero, length.MainPart)
            : length);

        Numerator = ReadCount(streamEvent?.GetProperty(NumeratorSymbol));
        Denominator = ReadCount(streamEvent?.GetProperty(DenominatorSymbol));
    }

    /// <summary>Gets the event that started the tuplet.</summary>
    public StreamEvent Event { get; }

    /// <summary>Gets or sets the bracket grob being built for this tuplet.</summary>
    public Spanner Bracket { get; set; }

    /// <summary>Gets or sets the number grob being built for this tuplet.</summary>
    public Spanner Number { get; set; }

    /// <summary>Gets or sets whether the bracket runs the full length of the tuplet.</summary>
    public bool FullLength { get; set; }

    /// <summary>Gets or sets whether the full length reaches to the following note.</summary>
    public bool FullLengthNote { get; set; }

    /// <summary>Gets the moment the tuplet starts.</summary>
    public Moment StartMoment { get; }

    /// <summary>Gets the moment the tuplet ends.</summary>
    public Moment StopMoment { get; }

    /// <summary>Gets or sets the enclosing tuplet, when this one is nested.</summary>
    public TupletDescription Parent { get; set; }

    /// <summary>Gets the tuplet's numerator, e.g. 3 in a triplet.</summary>
    public uint Numerator { get; }

    /// <summary>Gets the tuplet's denominator, e.g. 2 in a triplet.</summary>
    public uint Denominator { get; }

    /// <summary>
    /// Gets when the tuplet starts, in whichever clock it is measured in — grace time
    /// when either end carries a grace part, main time otherwise.
    /// </summary>
    public Rational TupletStart
        => StartMoment.GracePart.IsNonZero || StopMoment.GracePart.IsNonZero
            ? StartMoment.GracePart
            : StartMoment.MainPart;

    /// <summary>Gets when the tuplet ends, in the same clock as <see cref="TupletStart"/>.</summary>
    public Rational TupletStop
        => StartMoment.GracePart.IsNonZero || StopMoment.GracePart.IsNonZero
            ? StopMoment.GracePart
            : StopMoment.MainPart;

    /// <summary>Gets how long the tuplet lasts, in the same clock.</summary>
    public Rational TupletLength => TupletStop - TupletStart;

    /// <summary>Compares two descriptions.</summary>
    /// <param name="left">The first description.</param>
    /// <param name="right">The second description.</param>
    /// <returns><see langword="true"/> when they describe the same tuplet.</returns>
    public static bool operator ==(TupletDescription left, TupletDescription right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Compares two descriptions.</summary>
    /// <param name="left">The first description.</param>
    /// <param name="right">The second description.</param>
    /// <returns><see langword="true"/> when they describe different tuplets.</returns>
    public static bool operator !=(TupletDescription left, TupletDescription right)
        => !(left == right);

    /// <summary>Compares this description with another.</summary>
    /// <param name="other">The description to compare against.</param>
    /// <returns><see langword="true"/> when they describe the same tuplet.</returns>
    public bool Equals(TupletDescription other)
        => other != null
           && StartMoment == other.StartMoment
           && StopMoment == other.StopMoment
           && ReferenceEquals(Parent, other.Parent)
           && Numerator == other.Numerator
           && Denominator == other.Denominator;

    /// <summary>Compares this description with another object.</summary>
    /// <param name="obj">The object to compare against.</param>
    /// <returns><see langword="true"/> when it is an equal description.</returns>
    public override bool Equals(object obj) => Equals(obj as TupletDescription);

    /// <summary>Returns a hash code for the description.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
        => HashCode.Combine(StartMoment, StopMoment, Parent, Numerator, Denominator);

    private static uint ReadCount(object value)
        => SchemeConvert.IsNumber(value)
            ? (uint)SchemeConvert.ToLong(value, "tuplet-description")
            : 0u;

    /// <summary>
    /// Compares by VALUE for Scheme's <c>equal?</c>.
    /// <para>Upstream: <c>Tuplet_description::equal_p</c>, the smob equality handler
    /// <c>scm_equal_p</c> dispatches to. Without it two distinct objects holding the
    /// same value answer <c>#f</c>, which is identity, not equality.</para>
    /// </summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns><see langword="true"/> when the two are equal by value.</returns>
    public bool SchemeEquals(object other) => Equals(other);

}
