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

using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/context.cc (the measure_length / measure_position / scaled_measure_position free functions only);

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// The timing readers upstream keeps as free functions beside <c>Context</c>: measure
/// length and normalized measure position. They live in their own file here because
/// <c>Translation/Context.cs</c> predates them and this session may not edit it; the
/// divergence is recorded in PORT-COVERAGE.
/// </summary>
public static class MeasureTiming
{
    private static readonly Symbol MeasureLengthSymbol = Symbol.Intern("measureLength");
    private static readonly Symbol MeasurePositionSymbol = Symbol.Intern("measurePosition");
    private static readonly Symbol MeterScalingFactorSymbol = Symbol.Intern("meterScalingFactor");

    /// <summary>Reads <c>measureLength</c>, defaulting to one whole note.</summary>
    /// <param name="context">The context to read from.</param>
    /// <returns>The measure length in whole notes.</returns>
    public static Rational MeasureLength(Context context)
    {
        // TODO: Consider changing the default to Moment::infinity().
        return Epg8Support.ToRational(context?.GetProperty(MeasureLengthSymbol), Rational.One);
    }

    /// <summary>
    /// Returns the Euclidean remainder of a measure position after division by a
    /// measure length. The grace part of the position is not modified.
    /// </summary>
    /// <param name="context">The context, needed to deal robustly with unexpected values.</param>
    /// <param name="position">The measure position to normalize.</param>
    /// <param name="length">The measure length to divide by.</param>
    /// <returns>The normalized position.</returns>
    public static Moment MeasurePosition(Context context, Moment position, Rational length)
    {
        // The property infrastructure is supposed to prevent the actual measureLength
        // property from being set <= 0, but in case the provided length came from
        // elsewhere ...
        if (!length.IsNonZero)
        {
            Warn.ProgrammingError("cannot divide by zero measure length");

            // not really in [0, length), but pretty close
            return new Moment(Rational.Zero, position.GracePart);
        }

        // A negative measurePosition is the effect of \partial at the start of a
        // piece or (less likely) a \partial with a length longer than the measure
        // length.  Using the Euclidean remainder is good for things that should work
        // as if they are completing the latter part of a measure (e.g., automatic
        // beaming).
        if (length.IsFinite)
        {
            return new Moment(
                Rational.EuclideanRemainder(position.MainPart, length), position.GracePart);
        }

        // senza misura
        if (position.MainPart >= Rational.Zero)
        {
            // OK: There's no upper limit on measurePosition.
            return position;
        }

        // We can't be sure what is intended from \partial when there is no
        // measureLength.  Timing_translator warns if it is seen.  In mid piece, it
        // also ignores the \partial, so this branch should not be reached.  At the
        // start of the piece, returning the position from the start seems sane.
        return context != null ? context.NowMoment : position;
    }

    /// <summary>
    /// Returns the Euclidean remainder of the current <c>measurePosition</c> after
    /// division by the given measure length.
    /// </summary>
    /// <param name="context">The context to read from.</param>
    /// <param name="length">The measure length to divide by.</param>
    /// <returns>The normalized position.</returns>
    public static Moment MeasurePosition(Context context, Rational length)
        => MeasurePosition(
            context,
            Epg8Support.ToMoment(context?.GetProperty(MeasurePositionSymbol), Moment.Zero),
            length);

    /// <summary>
    /// Returns the Euclidean remainder of the current <c>measurePosition</c> after
    /// division by the current <c>measureLength</c>.
    /// </summary>
    /// <param name="context">The context to read from.</param>
    /// <returns>The normalized position.</returns>
    public static Moment MeasurePosition(Context context)
        => MeasurePosition(context, MeasureLength(context));

    /// <summary>
    /// Like <see cref="MeasurePosition(Context)"/>, but using the
    /// <c>meterScalingFactor</c> property to support cases of polymeter with aligned
    /// measures. The provided measure length and the returned measure position are in
    /// terms of the nominal meter.
    /// </summary>
    /// <param name="context">The context to read from.</param>
    /// <param name="scaledMeasureLength">The nominal measure length.</param>
    /// <returns>The position in terms of the nominal meter.</returns>
    public static Moment ScaledMeasurePosition(Context context, Rational scaledMeasureLength)
    {
        Rational factor = Epg8Support.ToRational(
            context?.GetProperty(MeterScalingFactorSymbol), Rational.One);
        Rational actualLength = scaledMeasureLength * factor;
        Moment actualPosition = MeasurePosition(context, actualLength);
        return actualPosition / factor;
    }
}
