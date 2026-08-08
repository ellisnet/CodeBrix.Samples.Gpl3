/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2010--2026 Carl Sorensen <c_sorensen@byu.edu>

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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/articulations.cc, lily/include/articulations.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/*
  Return an articulation list given a note_events vector and an
  articulation_events vector.

  This is necessary, because the articulations come as events if
  they are entered outside of a chord structure, and as articulations
  if they are inside the chord structure.  So potentially we need to
  combine the two types.
*/

/// <summary>
/// Pairs a timestep's note events with the articulations that belong to them, from
/// whichever of the two places the articulation was written.
/// <para>
/// The awkwardness is upstream's and is faithfully kept: a string number written
/// INSIDE a chord (<c>&lt;c\3 e\5&gt;</c>) arrives as an articulation ON the note event,
/// while one written OUTSIDE (<c>c\3</c>) arrives as an event of its own. The two have
/// to end up in one list, positionally aligned with the notes.
/// </para>
/// </summary>
public static class Articulations
{
    private static readonly Symbol ArticulationsSymbol = Symbol.Intern("articulations");

    /// <summary>
    /// Returns one articulation per note event, matched by articulation class.
    /// </summary>
    /// <param name="noteEvents">The timestep's note events, in order.</param>
    /// <param name="articulationEvents">The free-standing articulation events, in order.</param>
    /// <param name="articulationName">The event class to match, for example <c>string-number-event</c>.</param>
    /// <returns>
    /// A list as long as <paramref name="noteEvents"/>, holding the matching event or
    /// the empty list for each note.
    /// </returns>
    public static object ArticulationList(
        IReadOnlyList<StreamEvent> noteEvents,
        IReadOnlyList<StreamEvent> articulationEvents,
        Symbol articulationName)
    {
        object articulations = Nil.Instance;
        int j = 0;

        int articulationCount = articulationEvents?.Count ?? 0;

        for (int index = 0; index < (noteEvents?.Count ?? 0); index++)
        {
            StreamEvent ev = noteEvents[index];
            StreamEvent articulationEvent = null;

            /*
              For notes inside a chord construct, string indications are
              stored as articulations on the note, so we check through
              the notes
            */
            object cursor = ev?.GetProperty(ArticulationsSymbol);
            while (cursor is Pair pair)
            {
                if (pair.Car is StreamEvent art && art.IsInEventClass(articulationName))
                {
                    articulationEvent = art;
                    break;
                }

                cursor = pair.Cdr;
            }

            /*
              For string indications listed outside a chord construct,
              a string_number_event is generated, so if there was no string
              in the articulations, we check for string events outside
              the chord construct
            */
            if (articulationEvent == null && j < articulationCount)
            {
                articulationEvent = articulationEvents[j];
                if (j + 1 < articulationCount)
                {
                    j++;
                }
            }

            articulations = new Pair(
                articulationEvent != null ? (object)articulationEvent : Nil.Instance,
                articulations);
        }

        return NestedProperty.FastReverse(articulations, Nil.Instance);
    }
}
