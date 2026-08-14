/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2011--2026 Mike Solomon <mike@mikesolomon.org>
  Jan Nieuwenhuizen <janneke@gnu.org>

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
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/interval-minefield.cc, lily/include/interval-minefield.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Finds where a beam of a given thickness may be placed, given a set of intervals it
/// must not overlap. The feasible interval is pushed epsilon clear of each collision
/// and the search re-runs until nothing moves.
/// </summary>
public sealed class IntervalMinefield
{
    private readonly List<Interval> _forbiddenIntervals = new List<Interval>();
    private Interval _feasiblePlacements;
    private readonly double _bulk;

    /// <summary>Initializes the minefield.</summary>
    /// <param name="feasiblePlacements">The interval the beam may start in.</param>
    /// <param name="bulk">The thickness that must clear each forbidden interval.</param>
    public IntervalMinefield(Interval feasiblePlacements, double bulk)
    {
        _feasiblePlacements = feasiblePlacements;
        _bulk = bulk;
    }

    /// <summary>Adds an interval the beam may not overlap.</summary>
    /// <param name="forbidden">The forbidden interval.</param>
    public void AddForbiddenInterval(Interval forbidden)
        => _forbiddenIntervals.Add(forbidden);

    /// <summary>Gets the interval the beam may currently be placed in.</summary>
    /// <returns>The feasible placements.</returns>
    public Interval FeasiblePlacements() => _feasiblePlacements;

    /*
      forbidden_intervals_ contains a vector of intervals in which
      the beam cannot start.  it iterates through these intervals,
      pushing feasible_placements_ epsilon over or epsilon under a
      collision.  when this type of change happens, the loop is marked
      as "dirty" and re-iterated.

      TODO: figure out a faster ways that this loop can happen via
      a better search algorithm.
    */

    /// <summary>Runs the search until the feasible interval stops moving.</summary>
    public void Solve()
    {
        const double epsilon = 1.0e-10;
        bool dirty;
        do
        {
            dirty = false;
            for (int i = 0; i < _forbiddenIntervals.Count; i++)
            {
                foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
                {
                    Interval feasibleWidened
                        = new Interval(_feasiblePlacements[d], _feasiblePlacements[d]);
                    feasibleWidened.Widen(_bulk / 2.0);

                    if (_forbiddenIntervals[i][d] == d.Value * Interval.MaxSentinel)
                    {
                        _feasiblePlacements[d] = d.Value * Interval.MaxSentinel;
                    }
                    else if (_forbiddenIntervals[i].Contains(feasibleWidened[d])
                             || _forbiddenIntervals[i].Contains(feasibleWidened[-d])
                             || feasibleWidened.Contains(_forbiddenIntervals[i][d])
                             || feasibleWidened.Contains(_forbiddenIntervals[i][-d]))
                    {
                        _feasiblePlacements[d]
                            = _forbiddenIntervals[i][d] + (d.Value * (epsilon + (_bulk / 2)));
                        dirty = true;
                    }
                }
            }
        }
        while (dirty);
    }
}
