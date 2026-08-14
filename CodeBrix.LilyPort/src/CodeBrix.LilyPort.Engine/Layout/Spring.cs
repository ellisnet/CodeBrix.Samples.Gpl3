/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2007--2026 Joe Neeman <joeneeman@gmail.com>

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
  Springs help chains of objects, such as the notes in a line of music,
  distribute themselves evenly.
  Each spring decides the length from the reference point of one object
  along the line to the reference point of the next, based on a force
  applied to the entire chain (see Spring::length() for details):
     length = distance_ + flexibility * force

  distance_  is the ideal separation between reference points
  inverse_stretch_strength_ is the flexibility when the force is stretching
  inverse_compress_strength_ is the flexibility when the force is compressing
  min_distance_ sets a lower limit on length

  Typically, the force applied to a list of objects ranges from about
  -1 to about 1, though there are no set limits.
*/

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/spring.cc, lily/include/spring.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The spacing model between two adjacent objects in a line.
/// <para>
/// A spring changes length according to force, by the formula
/// <c>length = max(idealDistance + (force * inverseStrength), minDistance)</c>.
/// The inverse strength is set separately for stretching and for compressing, which
/// is what lets a line of music give more readily in one direction than the other.
/// </para>
/// <para>
/// Every "insane value" path below reports a programming error and keeps the previous
/// value rather than throwing. That is upstream's behaviour, and the spacing engine
/// depends on it — a bad spring degrades the layout, it does not abort the run.
/// </para>
/// </summary>
public struct Spring : IEquatable<Spring>
{
    // Parameters.
    private double _idealDistance;
    private double _minDistance;
    private double _inverseStretchStrength;
    private double _inverseCompressStrength;

    // Derived data.
    private double _blockingForce;

    // Set by every constructor, and false only on `default(Spring)`. A spring's
    // defaults are all 1.0 rather than 0.0, and C# cannot intercept default
    // construction, so the flag is what keeps a zeroed spring from reading back as a
    // zero-length, zero-strength one.
    private bool _initialized;

    /// <summary>Initializes a spring from its ideal and minimum lengths.</summary>
    /// <param name="idealDistance">The separation the spring wants.</param>
    /// <param name="minDistance">The separation it will not go below.</param>
    public Spring(double idealDistance, double minDistance)
    {
        _idealDistance = 1.0;
        _minDistance = 1.0;
        _inverseStretchStrength = 1.0;
        _inverseCompressStrength = 1.0;
        _blockingForce = 0.0;
        _initialized = true;

        SetIdealDistance(idealDistance);
        SetMinDistance(minDistance);
        SetDefaultStrength();
        UpdateBlockingForce();
    }

    /// <summary>Gets a spring with upstream's default-constructed values: everything 1.0.</summary>
    public static Spring Default
    {
        get
        {
            Spring s = default;
            s.EnsureInitialized();
            return s;
        }
    }

    /// <summary>Gets the separation the spring wants.</summary>
    public double IdealDistance
    {
        get
        {
            EnsureInitialized();
            return _idealDistance;
        }
    }

    /// <summary>Gets the separation the spring will not go below.</summary>
    public double MinDistance
    {
        get
        {
            EnsureInitialized();
            return _minDistance;
        }
    }

    /// <summary>Gets the flexibility under a stretching force.</summary>
    public double InverseStretchStrength
    {
        get
        {
            EnsureInitialized();
            return _inverseStretchStrength;
        }
    }

    /// <summary>Gets the flexibility under a compressing force.</summary>
    public double InverseCompressStrength
    {
        get
        {
            EnsureInitialized();
            return _inverseCompressStrength;
        }
    }

    /// <summary>
    /// Gets the force below which the length stops changing. The line spacer relies on
    /// this being the exact boundary.
    /// </summary>
    public double BlockingForce
    {
        get
        {
            EnsureInitialized();
            return _blockingForce;
        }
    }

    /// <summary>Returns the spring's length under a given force.</summary>
    /// <param name="force">The force applied to the whole chain.</param>
    /// <returns>The length, never below the minimum distance.</returns>
    public double Length(double force)
    {
        EnsureInitialized();

        double f = Math.Max(force, _blockingForce);
        double invK = f < 0.0 ? _inverseCompressStrength : _inverseStretchStrength;

        if (double.IsInfinity(f))
        {
            // This only happens for +inf; -inf is impossible, as
            // blocking_force_ is finite.
            Warn.ProgrammingError("cruelty to springs");
            f = 0.0;
        }

        // There is a corner case here: if min_distance_ is larger than
        // distance_ but the spring is fixed, then inv_k will be zero
        // and we need to make sure that we return min_distance_.
        return Math.Max(_minDistance, _idealDistance + (f * invK));
    }

    /// <summary>Sets the separation the spring wants. Insane values are ignored.</summary>
    /// <param name="distance">The new ideal distance.</param>
    public void SetIdealDistance(double distance)
    {
        EnsureInitialized();

        if (distance < 0 || !double.IsFinite(distance))
        {
            Warn.ProgrammingError("insane spring distance requested, ignoring it");
        }
        else
        {
            _idealDistance = distance;
            UpdateBlockingForce();
        }
    }

    /// <summary>Sets the separation the spring will not go below. Insane values are ignored.</summary>
    /// <param name="distance">The new minimum distance.</param>
    public void SetMinDistance(double distance)
    {
        EnsureInitialized();

        if (distance < 0 || !double.IsFinite(distance))
        {
            Warn.ProgrammingError("insane spring min_distance requested, ignoring it");
        }
        else
        {
            _minDistance = distance;
            UpdateBlockingForce();
        }
    }

    /// <summary>Raises the minimum distance to at least the given value.</summary>
    /// <param name="distance">The floor to enforce.</param>
    public void EnsureMinDistance(double distance)
    {
        EnsureInitialized();
        SetMinDistance(Math.Max(distance, _minDistance));
    }

    /// <summary>Sets the stretching flexibility. Insane values are ignored.</summary>
    /// <param name="strength">The new inverse stretch strength.</param>
    public void SetInverseStretchStrength(double strength)
    {
        EnsureInitialized();

        if (!double.IsFinite(strength) || strength < 0)
        {
            Warn.ProgrammingError("insane spring constant");
        }
        else
        {
            _inverseStretchStrength = strength;
        }

        UpdateBlockingForce();
    }

    /// <summary>Sets the compressing flexibility. Insane values are ignored.</summary>
    /// <param name="strength">The new inverse compress strength.</param>
    public void SetInverseCompressStrength(double strength)
    {
        EnsureInitialized();

        if (!double.IsFinite(strength) || strength < 0)
        {
            Warn.ProgrammingError("insane spring constant");
        }
        else
        {
            _inverseCompressStrength = strength;
        }

        UpdateBlockingForce();
    }

    /// <summary>
    /// Pins the spring so that the given force is exactly the blocking force, by
    /// raising the minimum distance to the length that force produces.
    /// </summary>
    /// <param name="force">The force to block at.</param>
    public void SetBlockingForce(double force)
    {
        EnsureInitialized();

        if (!double.IsFinite(force))
        {
            Warn.ProgrammingError("insane blocking force");
            return;
        }

        _blockingForce = double.NegativeInfinity;
        _minDistance = Length(force);
        UpdateBlockingForce();
    }

    /// <summary>Restores both strengths to the defaults derived from the distances.</summary>
    public void SetDefaultStrength()
    {
        EnsureInitialized();
        SetDefaultStretchStrength();
        SetDefaultCompressStrength();
    }

    /// <summary>Restores the compressing flexibility to the room above the minimum.</summary>
    public void SetDefaultCompressStrength()
    {
        EnsureInitialized();
        _inverseCompressStrength = _idealDistance >= _minDistance ? _idealDistance - _minDistance : 0;
        UpdateBlockingForce();
    }

    /// <summary>Restores the stretching flexibility to the ideal distance.</summary>
    public void SetDefaultStretchStrength()
    {
        EnsureInitialized();
        _inverseStretchStrength = _idealDistance;
    }

    /// <summary>
    /// Scales the spring, in a way that does not violate the minimum distance.
    /// </summary>
    /// <param name="factor">The scale factor.</param>
    public void ScaleBy(double factor)
    {
        EnsureInitialized();
        _idealDistance = Math.Max(_minDistance, _idealDistance * factor);
        _inverseCompressStrength = Math.Max(0.0, _idealDistance - _minDistance);
        _inverseStretchStrength *= factor;
        UpdateBlockingForce();
    }

    /// <summary>Scales a spring without violating its minimum distance.</summary>
    /// <param name="spring">The spring to scale.</param>
    /// <param name="factor">The scale factor.</param>
    /// <returns>The scaled spring.</returns>
    public static Spring operator *(Spring spring, double factor)
    {
        spring.ScaleBy(factor);
        return spring;
    }

    /// <summary>Orders springs by blocking force, as upstream's <c>operator&gt;</c> does.</summary>
    /// <param name="left">The first spring.</param>
    /// <param name="right">The second spring.</param>
    /// <returns><see langword="true"/> when the first blocks at a higher force.</returns>
    public static bool operator >(Spring left, Spring right) => left.BlockingForce > right.BlockingForce;

    /// <summary>Orders springs by blocking force.</summary>
    /// <param name="left">The first spring.</param>
    /// <param name="right">The second spring.</param>
    /// <returns><see langword="true"/> when the first blocks at a lower force.</returns>
    public static bool operator <(Spring left, Spring right) => left.BlockingForce < right.BlockingForce;

    /// <summary>
    /// Merges springs by averaging them, leaving a little headroom above the largest
    /// minimum distance so that things do not get too cramped.
    /// </summary>
    /// <param name="springs">The springs to merge. Must not be empty.</param>
    /// <returns>The merged spring.</returns>
    public static Spring Merge(IReadOnlyList<Spring> springs)
    {
        if (springs == null)
        {
            throw new ArgumentNullException(nameof(springs));
        }

        if (springs.Count == 0)
        {
            throw new ArgumentException("Cannot merge an empty set of springs.", nameof(springs));
        }

        double avgDistance = 0;
        double minDistance = 0;
        double avgStretch = 0;
        double avgCompress = 0;

        for (int i = 0; i < springs.Count; i++)
        {
            avgDistance += springs[i].IdealDistance;
            avgStretch += springs[i].InverseStretchStrength;
            avgCompress += 1 / springs[i].InverseCompressStrength;
            minDistance = Math.Max(springs[i].MinDistance, minDistance);
        }

        avgStretch /= springs.Count;
        avgCompress /= springs.Count;
        avgDistance /= springs.Count;
        avgDistance = Math.Max(minDistance + 0.3, avgDistance);

        Spring result = new Spring(avgDistance, minDistance);
        result.SetInverseStretchStrength(avgStretch);
        result.SetInverseCompressStrength(1 / avgCompress);

        return result;
    }

    /// <summary>Determines whether two springs have identical parameters.</summary>
    /// <param name="other">The spring to compare with.</param>
    /// <returns><see langword="true"/> when every parameter matches.</returns>
    public bool Equals(Spring other)
        => IdealDistance == other.IdealDistance
           && MinDistance == other.MinDistance
           && InverseStretchStrength == other.InverseStretchStrength
           && InverseCompressStrength == other.InverseCompressStrength;

    /// <summary>Determines whether this equals another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the object is an equal spring.</returns>
    public override bool Equals(object obj) => obj is Spring other && Equals(other);

    /// <summary>Returns a hash code.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
        => HashCode.Combine(IdealDistance, MinDistance, InverseStretchStrength, InverseCompressStrength);

    /// <summary>Tests equality.</summary>
    /// <param name="left">The first spring.</param>
    /// <param name="right">The second spring.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(Spring left, Spring right) => left.Equals(right);

    /// <summary>Tests inequality.</summary>
    /// <param name="left">The first spring.</param>
    /// <param name="right">The second spring.</param>
    /// <returns><see langword="true"/> when not equal.</returns>
    public static bool operator !=(Spring left, Spring right) => !left.Equals(right);

    /// <summary>Returns the external representation.</summary>
    /// <returns>The spring's parameters.</returns>
    public override string ToString()
        => string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Spring(ideal {0}, min {1}, stretch {2}, compress {3}, blocking {4})",
            IdealDistance,
            MinDistance,
            InverseStretchStrength,
            InverseCompressStrength,
            BlockingForce);

    private void UpdateBlockingForce()
    {
        // blocking_force_ is the value of force
        //   below which length(force) is constant, and
        //   above which length(force) varies according to inverse_*_strength.
        // Simple_spacer::compress_line() depends on the condition above.
        // We assume inverse_*_strength are non-negative.
        if (_minDistance > _idealDistance)
        {
            if (_inverseStretchStrength > 0.0)
            {
                _blockingForce = (_minDistance - _idealDistance) / _inverseStretchStrength;
            }
            else
            {
                // Conceptually, this should be +inf, but 0.0 meets the requirements
                //  of Simple_spacer and creates fewer cases of 0.0*inf to handle.
                _blockingForce = 0.0;
            }
        }
        else if (_inverseCompressStrength > 0.0)
        {
            _blockingForce = (_minDistance - _idealDistance) / _inverseCompressStrength;
        }
        else
        {
            _blockingForce = 0.0;
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _idealDistance = 1.0;
        _minDistance = 1.0;
        _inverseStretchStrength = 1.0;
        _inverseCompressStrength = 1.0;
        UpdateBlockingForce();
    }
}
