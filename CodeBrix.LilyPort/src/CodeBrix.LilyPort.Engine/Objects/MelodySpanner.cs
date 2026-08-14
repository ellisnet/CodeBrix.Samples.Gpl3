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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/melody-spanner.cc, lily/include/melody-spanner.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - vsize is unsigned upstream and VPOS is its largest value, which is what makes
//     `last_nonneutral + 1' wrap to 0 on the first pass and `last_nonneutral--' step back
//     to VPOS. The port keeps the same algorithm on a SIGNED index with -1 standing in for
//     VPOS, which reproduces both of those wraps as ordinary arithmetic; the loop
//     conditions are rewritten to match, and nothing else about the walk changes.

/*
  TODO: this could be either item or spanner. For efficiency reasons,
  let's take item for now.
*/

/// <summary>
/// Decides the direction of stems that have no direction of their own, by INTERPOLATING
/// between the nearest stems on each side that do.
/// <para>
/// A run of neutral stems between two stems pointing the same way follows them; a run
/// bounded by disagreeing stems, or by nothing at all, falls back to
/// <c>neutral-direction</c>. This is what stops a melody's stems from flickering up and
/// down through a passage that sits on the middle line.
/// </para>
/// <para>
/// The callback is asked about ONE stem but decides a whole run at once, so it writes the
/// answer onto the other stems of the run directly and returns only its own.
/// </para>
/// </summary>
public static class MelodySpanner
{
    private static readonly Symbol CalcNeutralStemDirectionProcSymbol
        = Symbol.Intern("ly:melody-spanner::calc-neutral-stem-direction");

    private static readonly Symbol DefaultDirectionSymbol = Symbol.Intern("default-direction");
    private static readonly Symbol MelodySpannerSymbol = Symbol.Intern("melody-spanner");
    private static readonly Symbol NeutralDirectionSymbol = Symbol.Intern("neutral-direction");
    private static readonly Symbol StemsSymbol = Symbol.Intern("stems");

    /*
      Interpolate stem directions for neutral stems.
     */

    /// <summary>The <c>neutral-direction</c> callback on a stem: the direction the run
    /// this stem belongs to should take.</summary>
    /// <param name="stem">The stem being asked about.</param>
    /// <returns>The direction, or the empty list when this stem is not in a neutral run.</returns>
    public static object CalcNeutralStemDirection(Grob stem)
    {
        Grob me = stem.GetObject(MelodySpannerSymbol) as Grob;
        if (me == null || !me.IsLive)
        {
            return (long)Direction.Negative.Value;
        }

        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, StemsSymbol);

        List<Direction> dirs = new List<Direction>();
        for (int i = 0; i < stems.Count; i++)
        {
            dirs.Add(DirectionalElementInterface.FromScheme(
                stems[i].GetProperty(DefaultDirectionSymbol), Direction.Center));
        }

        int lastNonneutral = -1; // upstream's VPOS
        int nextNonneutral = 0;
        while (nextNonneutral != -1 && nextNonneutral < dirs.Count
               && dirs[nextNonneutral] == Direction.Center)
        {
            nextNonneutral++;
        }

        object retval = Nil.Instance;
        while (lastNonneutral == -1 || lastNonneutral + 1 < dirs.Count)
        {
            Direction d1 = Direction.Center;
            Direction d2 = Direction.Center;
            if (lastNonneutral != -1)
            {
                d1 = dirs[lastNonneutral];
            }

            if (nextNonneutral < dirs.Count)
            {
                d2 = dirs[nextNonneutral];
            }

            Direction total;
            if (d1 != Direction.Center && d1 == d2)
            {
                total = d1;
            }
            else if (d1 != Direction.Center && d2 == Direction.Center)
            {
                total = d1;
            }
            else if (d2 != Direction.Center && d1 == Direction.Center)
            {
                total = d2;
            }
            else
            {
                total = DirectionalElementInterface.FromScheme(
                    me.GetProperty(NeutralDirectionSymbol), Direction.Center);
            }

            for (int i = lastNonneutral + 1; i < nextNonneutral; i++)
            {
                if (ReferenceEquals(stems[i], stem))
                {
                    retval = (long)total.Value;
                }
                else
                {
                    stems[i].SetProperty(NeutralDirectionSymbol, (long)total.Value);
                }
            }

            lastNonneutral = nextNonneutral;
            while (lastNonneutral < dirs.Count && dirs[lastNonneutral] != Direction.Center)
            {
                lastNonneutral++;
            }

            nextNonneutral = lastNonneutral;
            lastNonneutral--;

            while (nextNonneutral < dirs.Count && dirs[nextNonneutral] == Direction.Center)
            {
                nextNonneutral++;
            }
        }

        return retval;
    }

    /// <summary>Adds a stem to the melody span and points the stem back at it.</summary>
    /// <param name="me">The melody item.</param>
    /// <param name="stem">The stem to add.</param>
    public static void AddStem(Grob me, Grob stem)
    {
        PointerGroupInterface.AddGrob(me, StemsSymbol, stem);
        stem.SetObject(MelodySpannerSymbol, me);
        stem.SetProperty(
            NeutralDirectionSymbol,
            LilyPondScheme.LookupProcedure(CalcNeutralStemDirectionProcSymbol));
    }
}
