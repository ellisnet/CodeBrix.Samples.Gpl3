/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
  Copyright (C) 2007--2026 Han-Wen Nienhuys <hanwen@lilypond.org>

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
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/dot-configuration.cc, lily/include/dot-configuration.hh, lily/dot-formatting-problem.cc, lily/include/dot-formatting-problem.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// One dot in a dot column: where it wants to sit, which way it prefers to move, and
/// the grob itself.
/// </summary>
public struct DotPosition
{
    /// <summary>The staff position the dot asked for.</summary>
    public int Pos;

    /// <summary>The direction the dot prefers to move when displaced.</summary>
    public Direction Dir;

    /// <summary>The dot grob.</summary>
    public Grob Dot;

    /// <summary>The dot's extents. Carried as upstream carries it; nothing reads it.</summary>
    public Box DotExtents;

    /// <summary>The parent note's X extent. Carried as upstream carries it.</summary>
    public Interval XExtent;
}

/// <summary>
/// What the dots are placed against: the right-facing skyline of the heads, stems and
/// flags they must clear.
/// </summary>
public sealed class DotFormattingProblem
{
    private readonly Skyline _headSkyline;

    /// <summary>Builds the problem from the boxes to clear and the heads' own extent.</summary>
    /// <param name="boxes">The boxes of everything a dot must not touch.</param>
    /// <param name="baseX">The X extent of the main note heads.</param>
    public DotFormattingProblem(IReadOnlyList<Box> boxes, Interval baseX)
    {
        _headSkyline = new Skyline(boxes, Axis.Y, Direction.Positive);
        _headSkyline.SetMinimumHeight(baseX[Direction.Positive]);
    }

    /// <summary>Gets the skyline the dots are placed against.</summary>
    public Skyline HeadSkyline => _headSkyline;
}

/// <summary>
/// An assignment of dots to staff positions — a decades-tuned placement algorithm.
/// <para>
/// Upstream this privately inherits <c>std::map&lt;int, Dot_position&gt;</c>; the port
/// composes a <see cref="SortedDictionary{TKey, TValue}"/> and exposes the same chosen
/// subset, the same device Objects/GrobArray.cs records for private inheritance.
/// Iteration is in ascending key order, exactly as a <c>std::map</c> iterates.
/// </para>
/// </summary>
public sealed class DotConfiguration
{
    private readonly SortedDictionary<int, DotPosition> _entries
        = new SortedDictionary<int, DotPosition>();

    /// <summary>Initializes a configuration for a problem.</summary>
    /// <param name="problem">The problem the dots are being placed against.</param>
    public DotConfiguration(DotFormattingProblem problem)
    {
        Problem = problem;
    }

    /// <summary>Gets the problem the dots are being placed against.</summary>
    public DotFormattingProblem Problem { get; }

    /// <summary>Gets the entries in ascending staff-position order.</summary>
    public IEnumerable<KeyValuePair<int, DotPosition>> Entries => _entries;

    /// <summary>Gets or sets the dot at a staff position.</summary>
    /// <param name="position">The staff position.</param>
    /// <returns>The dot at that position.</returns>
    public DotPosition this[int position]
    {
        get => _entries[position];
        set => _entries[position] = value;
    }

    /// <summary>Determines whether a staff position is occupied.</summary>
    /// <param name="position">The staff position.</param>
    /// <returns><see langword="true"/> when a dot sits there.</returns>
    public bool Contains(int position) => _entries.ContainsKey(position);

    /// <summary>
    /// Scores the configuration: the square of every dot's displacement, doubled, plus
    /// a penalty for moving against the dot's own direction — or, lacking one, for
    /// moving anywhere but up.
    /// </summary>
    /// <returns>The badness; smaller is better.</returns>
    public int Badness()
    {
        int t = 0;
        foreach (KeyValuePair<int, DotPosition> ent in _entries)
        {
            int p = ent.Key;
            int displacement = p - ent.Value.Pos;
            int demerit = displacement * displacement * 2;

            Direction dotMoveDir = new Direction((long)displacement);
            if (ent.Value.Dir.IsNonZero && dotMoveDir != ent.Value.Dir)
            {
                demerit += 2;
            }
            else if (dotMoveDir != Direction.Positive)
            {
                demerit += 1;
            }

            t += demerit;
        }

        return t;
    }

    /// <summary>Prints the configuration, for debugging. Upstream's own debug aid.</summary>
    public void Print()
    {
        Console.Write("dotconf { ");
        foreach (KeyValuePair<int, DotPosition> ent in _entries)
        {
            Console.Write(ent.Key + ", ");
        }

        Console.WriteLine("}");
    }

    /*
      Shift K and following (preceding) entries up (down) as necessary to
      prevent staffline collisions if D is up (down).

      If K is in CFG, then do nothing.
    */

    /// <summary>
    /// Returns a copy with the dot at one position — and everything it pushes into —
    /// shifted one way.
    /// </summary>
    /// <param name="k">The staff position to clear.</param>
    /// <param name="d">The direction to shift in.</param>
    /// <returns>The shifted configuration.</returns>
    public DotConfiguration Shifted(int k, Direction d)
    {
        DotConfiguration newCfg = new DotConfiguration(Problem);
        int offset = 0;

        void ProcessEntry(KeyValuePair<int, DotPosition> ent)
        {
            int p = ent.Key;
            if (p == k)
            {
                if (StaffSymbolReferencer.OnLine(ent.Value.Dot, p))
                {
                    p += d.Value;
                }
                else
                {
                    p += 2 * d.Value;
                }

                offset = 2 * d.Value;

                newCfg[p] = ent.Value;
            }
            else
            {
                if (!newCfg.Contains(p))
                {
                    offset = 0;
                }

                newCfg[p + offset] = ent.Value;
            }
        }

        if (d > Direction.Center)
        {
            foreach (KeyValuePair<int, DotPosition> ent in _entries)
            {
                ProcessEntry(ent);
            }
        }
        else
        {
            // Reverse iteration order, as upstream walks rbegin()..rend().
            List<KeyValuePair<int, DotPosition>> reversed
                = new List<KeyValuePair<int, DotPosition>>(_entries);
            for (int i = reversed.Count; i-- > 0;)
            {
                ProcessEntry(reversed[i]);
            }
        }

        return newCfg;
    }

    /*
      Remove the collision in CFG either by shifting up or down, whichever
      is best.
    */

    /// <summary>Clears a staff position by shifting up or down, whichever scores better.</summary>
    /// <param name="p">The staff position to clear.</param>
    public void RemoveCollision(int p)
    {
        bool collide = _entries.ContainsKey(p);

        if (collide)
        {
            DotConfiguration cfgUp = Shifted(p, Direction.Positive);
            DotConfiguration cfgDown = Shifted(p, Direction.Negative);

            int bUp = cfgUp.Badness();
            int bDown = cfgDown.Badness();

            Swap(bUp < bDown ? cfgUp : cfgDown);
        }
    }

    /// <summary>Returns how far right of the origin the whole dot column must sit.</summary>
    /// <returns>The X offset.</returns>
    public double XOffset()
    {
        double off = 0.0;
        foreach (KeyValuePair<int, DotPosition> ent in _entries)
        {
            off = Math.Max(off, Problem.HeadSkyline.Height(ent.Key));
        }

        return off;
    }

    private void Swap(DotConfiguration other)
    {
        List<KeyValuePair<int, DotPosition>> mine
            = new List<KeyValuePair<int, DotPosition>>(_entries);
        _entries.Clear();
        foreach (KeyValuePair<int, DotPosition> ent in other._entries)
        {
            _entries[ent.Key] = ent.Value;
        }

        other._entries.Clear();
        foreach (KeyValuePair<int, DotPosition> ent in mine)
        {
            other._entries[ent.Key] = ent.Value;
        }
    }
}
