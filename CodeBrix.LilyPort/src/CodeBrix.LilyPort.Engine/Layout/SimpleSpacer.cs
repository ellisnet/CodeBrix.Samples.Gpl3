/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1999--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

  TODO:
  - add support for different stretch/shrink constants?

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

/*
  A simple spacing constraint solver. The approach:

  Stretch the line uniformly until none of the constraints (rods)
  block.  It then is very wide.

  Compress until the next constraint blocks,

  Mark the springs over the constrained part to be non-active.

  Repeat with the smaller set of non-active constraints, until all
  constraints blocked, or until the line is as short as desired.

  This is much simpler, and much much faster than full scale
  Constrained QP. On the other hand, a situation like this will not
  be typeset as dense as possible, because

  c4                   c4           c4                  c4
  veryveryverylongsyllable2         veryveryverylongsyllable2
  " "4                 veryveryverylongsyllable2        syllable4


  can be further compressed to


  c4    c4                        c4   c4
  veryveryverylongsyllable2       veryveryverylongsyllable2
  " "4  veryveryverylongsyllable2      syllable4


  Perhaps this is not a bad thing, because the 1st looks better anyway.  */

/*
  positive force = expanding, negative force = compressing.
*/

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/simple-spacer.cc, lily/include/simple-spacer.hh, lily/column-x-positions.cc, lily/include/column-x-positions.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// The result of solving a spacing problem: the force to apply, and whether the
/// constraints were actually satisfiable at that force.
/// </summary>
public readonly struct SpacerSolution
{
    /// <summary>Initializes the solution.</summary>
    /// <param name="force">The force to apply. Positive stretches, negative compresses.</param>
    /// <param name="fits">Whether the constraints were met.</param>
    public SpacerSolution(double force, bool fits)
    {
        Force = force;
        Fits = fits;
    }

    /// <summary>Gets the force to apply. Positive expands the line, negative compresses it.</summary>
    public double Force { get; }

    /// <summary>Gets a value indicating whether the line's constraints were satisfied.</summary>
    public bool Fits { get; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The force and the fit.</returns>
    public override string ToString()
        => string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "SpacerSolution(force {0}, fits {1})",
            Force,
            Fits);
}

/// <summary>
/// The line-spacing solver: a chain of <see cref="Spring"/>s plus rods, solved for the
/// single force that makes the chain the requested length.
/// <para>
/// The rods are hard minimum distances between two positions in the chain. Adding one
/// does not record a constraint to be solved later — it immediately raises the
/// blocking force of every spring it spans, which is what keeps the solver linear.
/// </para>
/// </summary>
public sealed class SimpleSpacer
{
    private readonly List<Spring> _springs = new List<Spring>();

    /// <summary>Gets the springs in the chain, in order.</summary>
    public IReadOnlyList<Spring> Springs => _springs;

    /// <summary>Appends a spring to the chain.</summary>
    /// <param name="spring">The spring to add.</param>
    public void AddSpring(Spring spring) => _springs.Add(spring);

    /// <summary>
    /// Adds a hard minimum distance between two positions in the chain.
    /// <para>
    /// A rod that is already satisfied is dropped. One that cannot be satisfied by any
    /// force — because the springs it spans are infinitely stiff — instead widens
    /// those springs' ideal distances directly.
    /// </para>
    /// </summary>
    /// <param name="left">The index of the left end.</param>
    /// <param name="right">The index of the right end.</param>
    /// <param name="distance">The minimum distance to enforce.</param>
    public void AddRod(int left, int right, double distance)
    {
        if (!double.IsFinite(distance))
        {
            Warn.ProgrammingError("ignoring weird minimum distance");
            return;
        }

        if (RangeLength(left, right, double.NegativeInfinity) > distance)
        {
            return;
        }

        double blockForce = RodForce(left, right, distance);
        if (double.IsInfinity(blockForce))
        {
            double springDistance = RangeIdealLength(left, right);
            if (springDistance < distance)
            {
                double factor = springDistance != 0.0
                    ? distance / springDistance
                    : distance / (right - left);

                for (int i = left; i < right; i++)
                {
                    Spring s = _springs[i];
                    if (springDistance > 0)
                    {
                        s.SetIdealDistance(s.IdealDistance * factor);
                    }
                    else
                    {
                        s.SetIdealDistance(factor);
                    }

                    _springs[i] = s;
                }
            }

            return;
        }

        for (int i = left; i < right; i++)
        {
            Spring s = _springs[i];
            s.SetBlockingForce(Math.Max(blockForce, s.BlockingForce));
            _springs[i] = s;
        }
    }

    /// <summary>Solves the whole chain for a target length.</summary>
    /// <param name="lineLength">The length the chain should occupy.</param>
    /// <param name="ragged">Whether the line is ragged-right, which forbids compression.</param>
    /// <returns>The solution.</returns>
    public SpacerSolution Solve(double lineLength, bool ragged)
        => RangeSolve(0, _springs.Count, lineLength, ragged);

    /// <summary>Returns the chain's length under a given force.</summary>
    /// <param name="force">The force to apply.</param>
    /// <returns>The total length.</returns>
    public double ConfigurationLength(double force) => RangeLength(0, _springs.Count, force);

    /// <summary>
    /// Returns the position of every spring boundary, starting at zero. The result has
    /// one more entry than there are springs.
    /// </summary>
    /// <param name="force">The force to apply.</param>
    /// <param name="ragged">Whether the line is ragged-right, which suppresses stretching.</param>
    /// <returns>The positions.</returns>
    public List<double> SpringPositions(double force, bool ragged)
    {
        List<double> result = new List<double> { 0.0 };

        for (int i = 0; i < _springs.Count; i++)
        {
            result.Add(result[result.Count - 1] + _springs[i].Length(ragged && force > 0 ? 0.0 : force));
        }

        return result;
    }

    /// <summary>
    /// Returns the badness of a solution, for the line breaker to minimise.
    /// <para>
    /// Ragged lines are scored on leftover whitespace rather than on force, because a
    /// ragged line is never stretched and its force therefore says nothing useful.
    /// </para>
    /// </summary>
    /// <param name="lineLength">The target line length.</param>
    /// <param name="force">The solved force.</param>
    /// <param name="ragged">Whether the line is ragged-right.</param>
    /// <returns>The penalty.</returns>
    public double ForcePenalty(double lineLength, double force, bool ragged)
    {
        /* If we are ragged-right, we don't want to penalise according to the force,
           but according to the amount of whitespace that is present after the end
           of the line. */
        if (ragged)
        {
            return Math.Max(0.0, lineLength - ConfigurationLength(0.0));
        }

        /* Use a convex compression penalty. */
        double f = force;
        return f - (f < 0 ? f * f * f * f * 2 : 0);
    }

    private SpacerSolution RangeSolve(int left, int right, double lineLength, bool ragged)
    {
        double maxBlockForce = RangeMaxBlockForce(left, right);
        double maxBlockForceLength = RangeLength(left, right, maxBlockForce);

        SpacerSolution sol;
        if (maxBlockForceLength < lineLength)
        {
            sol = ExpandLine(left, right, lineLength, maxBlockForceLength, maxBlockForce);
        }
        else if (maxBlockForceLength > lineLength)
        {
            sol = CompressLine(left, right, lineLength, maxBlockForceLength, maxBlockForce);
        }
        else
        {
            sol = new SpacerSolution(maxBlockForce, true);
        }

        if (ragged && sol.Force < 0)
        {
            sol = new SpacerSolution(sol.Force, false);
        }

        return sol;
    }

    private double RodForce(int left, int right, double distance)
    {
        double idealLength = RangeIdealLength(left, right);
        double stiffness = RangeStiffness(left, right, distance > idealLength);

        if (double.IsInfinity(stiffness))
        {
            // nothing we can do here
            return stiffness;
        }

        SpacerSolution sol = RangeSolve(left, right, distance, false);
        return sol.Force;
    }

    private double RangeLength(int left, int right, double force)
    {
        double d = 0.0;
        for (int i = left; i < right; i++)
        {
            d += _springs[i].Length(force);
        }

        return d;
    }

    private double RangeIdealLength(int left, int right)
    {
        double d = 0.0;
        for (int i = left; i < right; i++)
        {
            d += _springs[i].IdealDistance;
        }

        return d;
    }

    private double RangeStiffness(int left, int right, bool stretch)
    {
        double den = 0.0;
        for (int i = left; i < right; i++)
        {
            den += stretch ? _springs[i].InverseStretchStrength : _springs[i].InverseCompressStrength;
        }

        return 1 / den;
    }

    private double RangeMaxBlockForce(int left, int right)
    {
        double result = 0.0;
        for (int i = left; i < right; i++)
        {
            result = Math.Max(result, _springs[i].BlockingForce);
        }

        return result;
    }

    private SpacerSolution ExpandLine(
        int left,
        int right,
        double lineLength,
        double maxBlockForceLength,
        double maxBlockForce)
    {
        double invHooke = 0;
        for (int i = left; i < right; i++)
        {
            invHooke += _springs[i].InverseStretchStrength;
        }

        if (invHooke == 0.0)
        {
            /* avoid division by zero. If springs are infinitely stiff
               then report a very large stretching force */
            invHooke = 1e-6;
        }

        return new SpacerSolution(((lineLength - maxBlockForceLength) / invHooke) + maxBlockForce, true);
    }

    private SpacerSolution CompressLine(
        int left,
        int right,
        double lineLength,
        double maxBlockForceLength,
        double maxBlockForce)
    {
        /* just because we are in compress_line () doesn't mean that the line
           will actually be compressed (as in, a negative force) because
           we start out with a stretched line. Here, we check whether we
           will be compressed or stretched (so we know which spring constant to use) */
        double neutralLength = RangeLength(left, right, 0.0);
        bool compressed = neutralLength > lineLength;

        double curForce = compressed ? 0.0 : maxBlockForce;
        double curLength = compressed ? neutralLength : maxBlockForceLength;

        // Upstream sorts pointers with std::sort, whose tie order is unspecified. The
        // port sorts values with a stable order so a run is reproducible; ties are
        // equivalent to the algorithm either way.
        List<Spring> sortedSprings = new List<Spring>(right - left);
        for (int i = left; i < right; i++)
        {
            sortedSprings.Add(_springs[i]);
        }

        StableSortByBlockingForceDescending(sortedSprings);

        /* inv_hooke is the total flexibility of currently-active springs */
        double invHooke = 0;
        int index = sortedSprings.Count;
        for (; index > 0 && sortedSprings[index - 1].BlockingForce < curForce; index--)
        {
            invHooke += compressed
                ? sortedSprings[index - 1].InverseCompressStrength
                : sortedSprings[index - 1].InverseStretchStrength;
        }

        /* i now indexes the first active spring, so */
        for (; index < sortedSprings.Count; index++)
        {
            Spring sp = sortedSprings[index];

            if (double.IsInfinity(sp.BlockingForce))
            {
                break;
            }

            double blockDistance = (curForce - sp.BlockingForce) * invHooke;
            if (curLength - blockDistance < lineLength)
            {
                curForce += (lineLength - curLength) / invHooke;
                return new SpacerSolution(curForce, true);
            }

            curLength -= blockDistance;
            invHooke -= compressed ? sp.InverseCompressStrength : sp.InverseStretchStrength;
            curForce = sp.BlockingForce;
        }

        return new SpacerSolution(curForce, false);
    }

    private static void StableSortByBlockingForceDescending(List<Spring> springs)
    {
        Spring[] array = springs.ToArray();
        Spring[] scratch = new Spring[array.Length];
        MergeSort(array, scratch, 0, array.Length);
        springs.Clear();
        springs.AddRange(array);
    }

    private static void MergeSort(Spring[] array, Spring[] scratch, int start, int end)
    {
        if (end - start < 2)
        {
            return;
        }

        int mid = start + ((end - start) / 2);
        MergeSort(array, scratch, start, mid);
        MergeSort(array, scratch, mid, end);

        int i = start;
        int j = mid;
        int k = start;
        while (i < mid && j < end)
        {
            if (array[j].BlockingForce > array[i].BlockingForce)
            {
                scratch[k++] = array[j++];
            }
            else
            {
                scratch[k++] = array[i++];
            }
        }

        while (i < mid)
        {
            scratch[k++] = array[i++];
        }

        while (j < end)
        {
            scratch[k++] = array[j++];
        }

        Array.Copy(scratch, start, array, start, end - start);
    }
}

/// <summary>
/// The solved horizontal positions for one line: which columns are on it, where each
/// one goes, and whether the constraints were actually met.
/// </summary>
public sealed class ColumnXPositions
{
    /// <summary>Initializes an empty, satisfied solution.</summary>
    public ColumnXPositions()
    {
        SatisfiesConstraints = true;
        Force = 0;
    }

    /// <summary>Gets the spaceable columns on this line, in order.</summary>
    public List<Objects.PaperColumn> Columns { get; } = new List<Objects.PaperColumn>();

    /// <summary>
    /// Gets the loose columns on this line — those with a <c>between-cols</c> link,
    /// which are positioned relative to their neighbours rather than solved for.
    /// </summary>
    public List<Objects.PaperColumn> LooseColumns { get; } = new List<Objects.PaperColumn>();

    /// <summary>Gets the solved position of each column boundary, including the indent.</summary>
    public List<double> Configuration { get; internal set; } = new List<double>();

    /// <summary>Gets or sets the badness of this solution, for the line breaker to minimise.</summary>
    public double Force { get; set; }

    /// <summary>Gets or sets a value indicating whether the constraints were met.</summary>
    public bool SatisfiesConstraints { get; set; }
}
