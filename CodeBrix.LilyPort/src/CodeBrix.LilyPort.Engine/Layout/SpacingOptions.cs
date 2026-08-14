/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2006--2026 Han-Wen Nienhuys <hanwen@lilypond.org>

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
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/spacing-options.cc, lily/include/spacing-options.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/*
  Various options for spacing. Usually inited from SpacingSpanner, but sometimes
  from GraceSpacing.
 */

/// <summary>
/// The knobs the horizontal spacing algorithm turns, read off a grob.
/// <para>
/// Usually initialised from a <c>SpacingSpanner</c>, but a <c>GraceSpacing</c> supplies
/// its own set — which is why this is a value carried around beside the grob rather
/// than a set of properties read from one place.
/// </para>
/// </summary>
public sealed class SpacingOptions
{
    private static readonly Symbol SpacingIncrement = Symbol.Intern("spacing-increment");
    private static readonly Symbol PackedSpacing = Symbol.Intern("packed-spacing");
    private static readonly Symbol UniformStretching = Symbol.Intern("uniform-stretching");
    private static readonly Symbol StrictNoteSpacing = Symbol.Intern("strict-note-spacing");
    private static readonly Symbol StrictGraceSpacing = Symbol.Intern("strict-grace-spacing");
    private static readonly Symbol ShortestDurationSpaceSymbol
        = Symbol.Intern("shortest-duration-space");

    private static readonly Symbol CommonShortestDuration
        = Symbol.Intern("common-shortest-duration");

    /// <summary>Initializes the options at their built-in defaults.</summary>
    public SpacingOptions()
    {
        Packed = false;
        StretchUniformly = false;
        FloatNonmusicalColumns = false;
        FloatGraceColumns = false;

        ShortestDurationSpace = 2.0;
        Increment = 1.2;

        GlobalShortest = new Rational(1, 8);
    }

    /// <summary>Gets or sets whether notes are packed as tightly as they will go.</summary>
    public bool Packed { get; set; }

    /// <summary>Gets or sets whether every spring stretches by the same proportion.</summary>
    public bool StretchUniformly { get; set; }

    /// <summary>Gets or sets whether non-musical columns float between musical ones.</summary>
    public bool FloatNonmusicalColumns { get; set; }

    /// <summary>Gets or sets whether grace columns float between their neighbours.</summary>
    public bool FloatGraceColumns { get; set; }

    /// <summary>Gets or sets the duration that the spacing is measured against.</summary>
    public Rational GlobalShortest { get; set; }

    /// <summary>Gets or sets the width one doubling of a duration adds — a note head.</summary>
    public double Increment { get; set; }

    /// <summary>Gets or sets how much space the most common shortest note gets.</summary>
    public double ShortestDurationSpace { get; set; }

    /// <summary>Reads the options off a grob.</summary>
    /// <param name="me">The grob carrying the spacing properties.</param>
    public void InitFromGrob(Grob me)
    {
        if (me == null)
        {
            throw new ArgumentNullException(nameof(me));
        }

        Increment = RobustDouble(me.GetProperty(SpacingIncrement), 1.0);

        Packed = SchemeUtilities.ToBool(me.GetProperty(PackedSpacing));
        StretchUniformly = SchemeUtilities.ToBool(me.GetProperty(UniformStretching));
        FloatNonmusicalColumns = SchemeUtilities.ToBool(me.GetProperty(StrictNoteSpacing));
        FloatGraceColumns = SchemeUtilities.ToBool(me.GetProperty(StrictGraceSpacing));
        ShortestDurationSpace = RobustDouble(me.GetProperty(ShortestDurationSpaceSymbol), 1.0);

        Moment shortestDuration = me.GetProperty(CommonShortestDuration) is Moment moment
            ? moment
            : new Moment(new Rational(1, 8), new Rational(1, 16));

        GlobalShortest = shortestDuration.MainPart.IsNonZero
            ? shortestDuration.MainPart
            : shortestDuration.GracePart;
    }

    /// <summary>
    /// Returns how much space a note of a given duration is worth.
    /// <para>
    /// Above the reference duration the space grows with the LOGARITHM of the duration
    /// ratio — Gourlay's rule — so that doubling a note's length adds one increment
    /// rather than doubling its space. Below it the growth is linear instead, because
    /// logarithmic shrinkage of very short notes stretches the long ones out of all
    /// proportion.
    /// </para>
    /// </summary>
    /// <param name="d">The duration.</param>
    /// <returns>The space, in staff spaces.</returns>
    public double GetDurationSpace(Rational d)
    {
        double ratio = (double)(d / GlobalShortest);

        if (ratio < 1.0)
        {
            /*
              We don't space really short notes using the log of the
              duration, since it would disproportionally stretches the long
              notes in a piece. In stead, we use geometric spacing with constant 0.5
              (i.e. linear.)

              This should probably be tunable, to use other base numbers.

              In Mozart hrn3 by EB., we have 8th note = 3.9 mm (total), 16th note =
              3.6 mm (total).  head-width = 2.4, so we 1.2mm for 16th, 1.5
              mm for 8th. (white space), suggesting that we use

              (1.2 / 1.5)^{-log2(duration ratio)}
            */

            return (ShortestDurationSpace + ratio - 1) * Increment;
        }

        /*
          John S. Gourlay. ``Spacing a Line of Music, '' Technical
          Report OSU-CISRC-10/87-TR35, Department of Computer and
          Information Science, The Ohio State University, 1987.
        */

        return (ShortestDurationSpace + Math.Log2(ratio)) * Increment;
    }

    /// <summary>Returns an independent copy of these options.</summary>
    /// <returns>The copy.</returns>
    public SpacingOptions Copy() => new SpacingOptions
    {
        Packed = Packed,
        StretchUniformly = StretchUniformly,
        FloatNonmusicalColumns = FloatNonmusicalColumns,
        FloatGraceColumns = FloatGraceColumns,
        GlobalShortest = GlobalShortest,
        Increment = Increment,
        ShortestDurationSpace = ShortestDurationSpace,
    };

    private static double RobustDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "spacing option")
            : fallback;
}
