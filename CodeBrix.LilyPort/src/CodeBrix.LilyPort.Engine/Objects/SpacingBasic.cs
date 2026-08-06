/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/spacing-basic.cc;

// Modified by Jeremy Ellis on 2026-08-05 as part of the CodeBrix port.

/*
  LilyPond spaces by taking a simple-minded spacing algorithm, and
  adding subtle adjustments to that. This file does the simple-minded
  spacing routines.
*/

/// <summary>
/// The simple-minded half of the spacing algorithm: the springs that come from
/// DURATION alone, before any wish has had a say.
/// <para>
/// NAMING, recorded in PORT-COVERAGE: upstream's <c>Spacing_spanner::note_spacing</c>
/// is <see cref="NoteSpacingSpring"/> here, because <c>NoteSpacing</c> is the name of
/// the grob interface beside it and a method of that name would shadow the type for
/// every line in the class.
/// </para>
/// </summary>
public static partial class SpacingSpanner
{
    private static readonly Symbol SpacingIncrement = Symbol.Intern("spacing-increment");
    private static readonly Symbol ShortestPlayingDuration
        = Symbol.Intern("shortest-playing-duration");

    private static readonly Symbol ColumnsSymbol = Symbol.Intern("columns");

    /*
      The one-size-fits all spacing. It doesn't take into account
      different spacing wishes from one to the next column.
    */

    /// <summary>
    /// Returns the spring between two columns when nothing more specific is known.
    /// <para>
    /// Between two BREAKABLE columns the spring is a whole measure's worth of space,
    /// and its stretchability deliberately excludes the minimum distance: an empty first
    /// measure carries a clef, so its minimum is large, and letting that inflate the
    /// stretch would make the first measure of a line grow far more than a later one.
    /// </para>
    /// </summary>
    /// <param name="me">The spacing spanner.</param>
    /// <param name="l">The left column.</param>
    /// <param name="r">The right column.</param>
    /// <param name="options">The spacing options.</param>
    /// <returns>The spring.</returns>
    public static Spring StandardBreakableColumnSpacing(
        Grob me,
        Item l,
        Item r,
        SpacingOptions options)
    {
        double minDist = Math.Max(0.0, PaperColumn.MinimumDistance(l, r));

        if (PaperColumn.IsBreakable(l) && PaperColumn.IsBreakable(r))
        {
            Moment mlen = l.GetProperty(MeasureLength) is Moment dt ? dt : new Moment(1);

            double incr = RobustDouble(me.GetProperty(SpacingIncrement), 1.0);
            double space = incr * (double)(mlen.MainPart / options.GlobalShortest) * 0.8;
            Spring spring = new Spring(minDist + space, minDist);

            /*
              By default, the spring will have an inverse_stretch_strength of space+min_dist.
              However, we don't want stretchability to scale with min_dist or else an
              empty first measure on a line (which has a large min_dist because of the clef)
              will stretch much more than an empty measure later in the line.
            */
            spring.SetInverseStretchStrength(space);
            return spring;
        }

        Moment delta = PaperColumn.WhenMoment(r) - PaperColumn.WhenMoment(l);
        double ideal;

        if (delta == Moment.Zero)
        {
            /*
              In this case, Staff_spacing should handle the job,
              using dt when it is 0 is silly.
            */
            ideal = minDist + 0.5;
        }
        else
        {
            ideal = minDist + options.GetDurationSpace(delta.MainPart);
        }

        return new Spring(ideal, minDist);
    }

    /* Basic spring based on duration alone */

    /// <summary>
    /// Returns the spring between two musical columns from their durations alone.
    /// <para>
    /// The spring is a FRACTION of the space the ruling note's whole duration is worth —
    /// the fraction of that duration the gap actually covers — so that a run of short
    /// notes inside a longer one divides that note's space rather than adding to it.
    /// </para>
    /// </summary>
    /// <param name="me">The spacing spanner.</param>
    /// <param name="lc">The left column.</param>
    /// <param name="rc">The right column.</param>
    /// <param name="options">The spacing options.</param>
    /// <returns>The spring.</returns>
    public static Spring NoteSpacingSpring(
        Grob me,
        PaperColumn lc,
        Grob rc,
        SpacingOptions options)
    {
        Rational shortestPlayingLen = RobustRational(
            lc.GetProperty(ShortestPlayingDuration), Rational.Zero);
        if (shortestPlayingLen <= Rational.Zero)
        {
            Warn.ProgrammingError(
                "cannot find a ruling note at: " + PaperColumn.WhenMoment(lc));
            shortestPlayingLen = Rational.One;
        }

        Moment lwhen = PaperColumn.WhenMoment(lc);
        Moment rwhen = PaperColumn.WhenMoment(rc);

        Moment deltaT = rwhen - lwhen;

        /*
          when toying with mmrests, it is possible to have musical
          column on the left and non-musical on the right, spanning
          several measures.

          TODO: efficiency: measure length can be cached, or stored as
          property in paper-column.
        */
        {
            Moment mlen = GetMeasureLength(lc);
            if (mlen < deltaT)
            {
                deltaT = mlen;
            }

            /*
              The following is an extra safety measure, such that
              the length of a mmrest event doesn't cause havoc.
            */
            shortestPlayingLen = Min(shortestPlayingLen, mlen.MainPart);
        }

        Spring ret = Spring.Default;
        if (deltaT.MainPart.IsNonZero && !lwhen.GracePart.IsNonZero)
        {
            // A spring of length and stiffness based on the controlling duration
            double len = options.GetDurationSpace(shortestPlayingLen);
            double min = options.Increment; // canonical notehead width

            // The portion of that spring proportional to the time between lc and rc
            double fraction = (double)(deltaT.MainPart / shortestPlayingLen);
            ret = new Spring(fraction * len, fraction * min);

            // Stretch proportional to the space between canonical bare noteheads
            ret.SetInverseStretchStrength(fraction * Math.Max(0.1, len - min));
        }
        else if (deltaT.GracePart.IsNonZero)
        {
            Grob graceSpacing = lc.GetObject(GraceSpacing) as Grob;
            if (graceSpacing != null)
            {
                SpacingOptions graceOpts = new SpacingOptions();
                graceOpts.InitFromGrob(graceSpacing);
                double len = graceOpts.GetDurationSpace(deltaT.GracePart);
                double min = graceOpts.Increment;
                ret = new Spring(len, min);

                // Grace notes should not stretch very much
                ret.SetInverseStretchStrength(graceOpts.Increment / 2.0);
            }
            else
            {
                // Fallback to the old grace spacing: half that of the shortest note
                ret = new Spring(
                    options.GetDurationSpace(options.GlobalShortest) / 2.0,
                    options.Increment / 2.0);
            }
        }

        return ret;
    }

    /// <summary>
    /// Returns the length of the measure a column sits in, by walking BACK through the
    /// system's columns to the most recent one that declares a measure length.
    /// </summary>
    /// <param name="column">The column.</param>
    /// <returns>The measure length, or infinity when none is declared.</returns>
    private static Moment GetMeasureLength(PaperColumn column)
    {
        Grob sys = column.GetParent(Axis.X);
        if (sys == null)
        {
            return Moment.Infinity;
        }

        IReadOnlyList<Grob> cols = PointerGroupInterface.ExtractGrobSet(sys, ColumnsSymbol);

        int colIdx = column.Rank;
        if (colIdx >= cols.Count)
        {
            colIdx = cols.Count - 1;
        }

        while (colIdx >= 0)
        {
            if (cols[colIdx].GetProperty(MeasureLength) is Moment len)
            {
                return len;
            }

            colIdx--;
        }

        return Moment.Infinity;
    }
}
