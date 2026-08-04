/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com>

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
using System.Globalization;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/skyline.cc, lily/include/skyline.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/* A skyline is a sequence of non-overlapping buildings: something like
   this:
                   _______
                  |       \                                 ________
                  |        \                       ________/        \
        /\        |          \                    /                  \
       /  --------             \                 /                    \
      /                          \              /                      \
     /                             ------------/                        ----
   --
   Each building has a starting position, and ending position, a starting
   height and an ending height.

   The following invariants are observed:
    - the start of the first building is at -infinity
    - the end of the last building is at infinity
    - if a building has infinite length (ie. the first and last buildings),
      then its starting height and ending height are equal
    - the end of one building is the same as the beginning of the next
      building

   We also allow skylines to point down (the structure is exactly the same,
   but we think of the part above the line as being filled with mass and the
   part below as being empty). ::distance finds the minimum distance between
   an UP skyline and a DOWN skyline.

   Note that we store DOWN skylines upside-down. That is, in order to compare
   a DOWN skyline with an UP skyline, we need to flip the DOWN skyline first.
   This means that the merging routine doesn't need to be aware of direction,
   but the distance routine does.

   From 2007 through 2012, buildings of width less than EPS were discarded,
   citing numerical accuracy concerns.  We remember that floating point
   comparisons of nearly-equal values can be affected by rounding error.
   Also, some target machines use the x87 floating point unit, which provides
   extended precision for intermediate results held in registers. On this type
   of hardware comparisons such as
     double c = 1.0/3.0; boolean compare = (c == 1.0/3.0)
   could go either way because the 1.0/3.0 is allowed to be kept
   higher precision than the variable 'c'.
   Alert to these considerations, we now accept buildings of zero-width.
*/

/// <summary>
/// One segment of a skyline: a sloped roof over a horizontal span.
/// <para>
/// Stored in slope-intercept form rather than as two endpoints, because the merge
/// routine asks for the height at arbitrary abscissae far more often than it asks for
/// the endpoints.
/// </para>
/// </summary>
public struct Building
{
    /// <summary>The horizontal span the building covers.</summary>
    public Interval X;

    /// <summary>The roof line's intercept at x = 0.</summary>
    public double YIntercept;

    /// <summary>The roof line's slope.</summary>
    public double Slope;

    /// <summary>Initializes a building from its span and its two roof heights.</summary>
    /// <param name="start">The left edge.</param>
    /// <param name="startHeight">The roof height at the left edge.</param>
    /// <param name="endHeight">The roof height at the right edge.</param>
    /// <param name="end">The right edge.</param>
    public Building(double start, double startHeight, double endHeight, double end)
    {
        X = new Interval(start, end);
        YIntercept = 0.0;
        Slope = 0.0;

        if ((double.IsInfinity(start) || double.IsInfinity(end)) && startHeight != endHeight)
        {
            throw new ArgumentException(
                "An infinitely long building must have equal start and end heights.",
                nameof(startHeight));
        }

        Precompute(startHeight, endHeight);
    }

    /// <summary>
    /// Initializes a flat building from a box, at the box's extreme edge on the axis
    /// the skyline points along.
    /// </summary>
    /// <param name="box">The box to cover.</param>
    /// <param name="horizonAxis">The axis the skyline runs along.</param>
    /// <param name="sky">Which way the skyline points.</param>
    public Building(Box box, Axis horizonAxis, Direction sky)
    {
        X = box[horizonAxis];
        YIntercept = 0.0;
        Slope = 0.0;

        double height = sky * box[Axes.Other(horizonAxis)][sky];
        Precompute(height, height);
    }

    private void Precompute(double startHeight, double endHeight)
    {
        // if they were both infinite, we would get nan, not 0, from the prev line
        Slope = 0.0;
        if (startHeight != endHeight)
        {
            Slope = (endHeight - startHeight) / X.Length;
        }

        if (!double.IsFinite(Slope))
        {
            throw new InvalidOperationException("Skyline building slope is not finite.");
        }

        if (double.IsInfinity(X.Left))
        {
            YIntercept = startHeight;
        }
        else if (Math.Abs(Slope) > 1e6)
        {
            // too steep to be stored in slope-intercept form, given round-off error
            Slope = 0.0;
            YIntercept = Math.Max(startHeight, endHeight);
        }
        else
        {
            YIntercept = startHeight - (Slope * X.Left);
        }
    }

    /// <summary>Returns the roof height at an abscissa.</summary>
    /// <param name="x">The abscissa, which may be infinite.</param>
    /// <returns>The height.</returns>
    public readonly double Height(double x)
        => double.IsInfinity(x) ? YIntercept : (Slope * x) + YIntercept;

    /// <summary>
    /// Returns the abscissa at which two roof lines cross.
    /// <para>
    /// Only meaningful when the buildings do intersect. Upstream deliberately does not
    /// assert that, because numerical inaccuracy can make a true intersection read as
    /// false, and nearly-parallel roofs are collapsed to the later left edge rather
    /// than divided by a near-zero slope difference.
    /// </para>
    /// </summary>
    /// <param name="other">The building to cross with.</param>
    /// <returns>The crossing abscissa.</returns>
    public readonly double IntersectionX(Building other)
    {
        double slopeDelta = other.Slope - Slope;

        // If the slopes are really close (for example, if we happen to try merging
        // two identical buildings), avoid numerical inaccuracies related to dividing
        // by a small number.
        if (Math.Abs(slopeDelta) < 1e-4)
        {
            return Math.Max(X.Left, other.X.Left);
        }

        return (YIntercept - other.YIntercept) / slopeDelta;
    }

    /// <summary>Determines whether this building's roof is above another's at an abscissa.</summary>
    /// <param name="other">The building to compare with.</param>
    /// <param name="x">The abscissa.</param>
    /// <returns><see langword="true"/> when this roof is higher.</returns>
    public readonly bool Above(Building other, double x)
        => double.IsInfinity(YIntercept) || double.IsInfinity(other.YIntercept) || double.IsInfinity(x)
            ? YIntercept > other.YIntercept
            : (((Slope - other.Slope) * x) + YIntercept) > other.YIntercept;

    /// <summary>Returns the external representation, in upstream's debug wording.</summary>
    /// <returns>The slope-intercept form and the span.</returns>
    public override readonly string ToString()
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0:F6} x + {1:F6} from {2:F6} to {3:F6}",
            Slope,
            YIntercept,
            X.Left,
            X.Right);
}

/// <summary>
/// The outline of everything on one side of a line: a run of non-overlapping
/// <see cref="Building"/>s spanning the whole real line.
/// <para>
/// This is the geometry every collision-avoidance decision in LilyPond leans on.
/// <see cref="Distance"/> answers "how far can these two objects approach before they
/// touch", which is what vertical spacing, outside-staff placement and system
/// stacking are all built from.
/// </para>
/// <para>
/// A DOWN skyline is stored upside-down — heights are multiplied by the direction on
/// the way in and on the way out. That is what lets <see cref="Merge"/> ignore
/// direction entirely; only <see cref="Distance"/> has to know.
/// </para>
/// </summary>
public sealed class Skyline
{
    private List<Building> _buildings = new List<Building>();
    private Direction _sky;

    /// <summary>Initializes an empty upward skyline.</summary>
    /// <remarks>
    /// Upstream flags this constructor as an attractive nuisance — it makes it easy to
    /// forget to set a direction — but keeps it because <c>std::map</c>'s
    /// <c>operator[]</c> needs it. The same applies here for dictionary use.
    /// </remarks>
    public Skyline()
    {
        _sky = Direction.Positive;
        EmptySkyline(_buildings);
    }

    /// <summary>Initializes an empty skyline pointing one way.</summary>
    /// <param name="sky">The direction the skyline faces.</param>
    public Skyline(Direction sky)
    {
        _sky = sky;
        EmptySkyline(_buildings);
    }

    /// <summary>
    /// Initializes a skyline from a set of boxes. Boxes empty on either axis are
    /// ignored.
    /// </summary>
    /// <param name="boxes">The boxes to cover.</param>
    /// <param name="horizonAxis">The axis the skyline runs along.</param>
    /// <param name="sky">The direction the skyline faces.</param>
    public Skyline(IReadOnlyList<Box> boxes, Axis horizonAxis, Direction sky)
    {
        if (boxes == null)
        {
            throw new ArgumentNullException(nameof(boxes));
        }

        List<Building> buildings = new List<Building>(boxes.Count);
        _sky = sky;

        foreach (Box box in boxes)
        {
            if (!box.IsEmptyOn(Axis.X) && !box.IsEmptyOn(Axis.Y))
            {
                buildings.Add(new Building(box, horizonAxis, sky));
            }
        }

        _buildings = InternalBuildSkyline(buildings);
    }

    /// <summary>
    /// Initializes a skyline from a set of line segments. Segments given right to left
    /// are stored left to right.
    /// </summary>
    /// <param name="segments">The segments to cover.</param>
    /// <param name="horizonAxis">The axis the skyline runs along.</param>
    /// <param name="sky">The direction the skyline faces.</param>
    public Skyline(IReadOnlyList<DrulArray<Offset>> segments, Axis horizonAxis, Direction sky)
    {
        if (segments == null)
        {
            throw new ArgumentNullException(nameof(segments));
        }

        List<Building> buildings = new List<Building>(segments.Count);
        _sky = sky;

        foreach (DrulArray<Offset> segment in segments)
        {
            Offset left = segment.Negative;
            Offset right = segment.Positive;

            if (left[horizonAxis] > right[horizonAxis])
            {
                (left, right) = (right, left);
            }

            double x1 = left[horizonAxis];
            double x2 = right[horizonAxis];
            double y1 = left[Axes.Other(horizonAxis)] * sky;
            double y2 = right[Axes.Other(horizonAxis)] * sky;

            if (x1 < x2)
            {
                buildings.Add(new Building(x1, y1, y2, x2));
            }
        }

        _buildings = InternalBuildSkyline(buildings);
    }

    /// <summary>Initializes a skyline as the merge of one side of several pairs.</summary>
    /// <param name="skyPairs">The pairs to merge.</param>
    /// <param name="sky">Which side of each pair to take, and the resulting direction.</param>
    public Skyline(IReadOnlyList<SkylinePair> skyPairs, Direction sky)
    {
        if (skyPairs == null)
        {
            throw new ArgumentNullException(nameof(skyPairs));
        }

        _sky = sky;

        Queue<Skyline> partials = new Queue<Skyline>();
        foreach (SkylinePair pair in skyPairs)
        {
            partials.Enqueue(pair[sky].Copy());
        }

        while (partials.Count > 1)
        {
            Skyline one = partials.Dequeue();
            Skyline two = partials.Dequeue();

            one.Merge(two);
            partials.Enqueue(one);
        }

        if (partials.Count > 0)
        {
            _buildings = partials.Dequeue()._buildings;
        }
        else
        {
            _buildings.Clear();
        }
    }

    /// <summary>Initializes a skyline covering one box.</summary>
    /// <param name="box">The box to cover.</param>
    /// <param name="horizonAxis">The axis the skyline runs along.</param>
    /// <param name="sky">The direction the skyline faces.</param>
    public Skyline(Box box, Axis horizonAxis, Direction sky)
    {
        _sky = sky;
        if (!box.IsEmptyOn(Axis.X) && !box.IsEmptyOn(Axis.Y))
        {
            SingleSkyline(new Building(box, horizonAxis, sky), _buildings);
        }
    }

    private Skyline(Direction sky, List<Building> buildings)
    {
        _sky = sky;
        _buildings = buildings;
    }

    /// <summary>Gets the direction the skyline faces.</summary>
    public Direction Sky => _sky;

    /// <summary>Gets the buildings, left to right. Exposed for the backends and tests.</summary>
    public IReadOnlyList<Building> Buildings => _buildings;

    /// <summary>Returns an independent copy.</summary>
    /// <returns>The copy.</returns>
    public Skyline Copy() => new Skyline(_sky, new List<Building>(_buildings));

    /// <summary>Gets the direction the skyline faces.</summary>
    /// <returns>The direction.</returns>
    public Direction GetDirection() => _sky;

    /// <summary>
    /// Gets a value indicating whether the skyline is empty — one building at minus
    /// infinity spanning everything.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            if (_buildings.Count == 0)
            {
                return true;
            }

            Building b = _buildings[0];
            return b.X.Right == double.PositiveInfinity && b.YIntercept == double.NegativeInfinity;
        }
    }

    /// <summary>Empties the skyline in place.</summary>
    public void Clear()
    {
        _buildings.Clear();
        EmptySkyline(_buildings);
    }

    /// <summary>
    /// Moves the skyline up the page by the given amount.
    /// <para>
    /// The stored intercept is signed by the direction on the way in and read back
    /// signed by it again, so the two signs cancel: a DOWN skyline raised by <c>r</c>
    /// also reports itself <c>r</c> higher. That is what lets a whole
    /// <see cref="SkylinePair"/> be moved without distorting it.
    /// </para>
    /// </summary>
    /// <param name="amount">The distance to raise by.</param>
    public void Raise(double amount)
    {
        for (int i = 0; i < _buildings.Count; i++)
        {
            Building b = _buildings[i];
            b.YIntercept += _sky * amount;
            _buildings[i] = b;
        }
    }

    /// <summary>Moves the skyline along its horizon axis.</summary>
    /// <param name="amount">The distance to shift by.</param>
    public void Shift(double amount)
    {
        for (int i = 0; i < _buildings.Count; i++)
        {
            Building b = _buildings[i];
            b.X.Left += amount;
            b.X.Right += amount;
            b.YIntercept -= amount * b.Slope;
            _buildings[i] = b;
        }
    }

    /// <summary>Merges another skyline of the same direction into this one.</summary>
    /// <param name="other">The skyline to absorb.</param>
    public void Merge(Skyline other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        if (_sky != other._sky)
        {
            throw new ArgumentException("Cannot merge skylines facing opposite ways.", nameof(other));
        }

        if (other.IsEmpty)
        {
            return;
        }

        if (IsEmpty)
        {
            _buildings = new List<Building>(other._buildings);
            return;
        }

        List<Building> otherBuildings = new List<Building>(other._buildings);
        List<Building> dest = new List<Building>();
        InternalMergeSkyline(otherBuildings, _buildings, dest);
        _buildings = dest;
    }

    /// <summary>Merges one box into the skyline.</summary>
    /// <param name="box">The box to add.</param>
    /// <param name="horizonAxis">The axis the skyline runs along.</param>
    public void Insert(Box box, Axis horizonAxis) => Merge(new Skyline(box, horizonAxis, _sky));

    /// <summary>Raises the whole skyline to at least a given height.</summary>
    /// <param name="height">The floor height.</param>
    public void SetMinimumHeight(double height)
    {
        Skyline s = new Skyline(_sky);
        Building b = s._buildings[0];
        b.YIntercept = height * _sky;
        s._buildings[0] = b;
        Merge(s);
    }

    /// <summary>
    /// Returns the closest approach between this skyline and one facing the other way:
    /// the largest sum of the two heights over their common horizon.
    /// </summary>
    /// <param name="other">The opposing skyline.</param>
    /// <param name="horizonPadding">Extra horizontal room to demand on each side.</param>
    /// <returns>The distance, which is negative when the two do not reach each other.</returns>
    public double Distance(Skyline other, double horizonPadding = 0.0)
    {
        double touch;
        return InternalDistance(other, horizonPadding, out touch);
    }

    /// <summary>Returns the abscissa at which two opposing skylines come closest.</summary>
    /// <param name="other">The opposing skyline.</param>
    /// <param name="horizonPadding">Extra horizontal room to demand on each side.</param>
    /// <returns>The abscissa of closest approach.</returns>
    public double TouchingPoint(Skyline other, double horizonPadding = 0.0)
    {
        double touch;
        InternalDistance(other, horizonPadding, out touch);
        return touch;
    }

    /// <summary>Returns the skyline's height at an abscissa, signed by its direction.</summary>
    /// <param name="x">The finite abscissa to sample.</param>
    /// <returns>The height.</returns>
    public double Height(double x)
    {
        if (double.IsInfinity(x))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Skyline height is undefined at infinity.");
        }

        // Upstream uses std::lower_bound over "building lies entirely left of x".
        int index = LowerBound(x);
        if (index >= _buildings.Count)
        {
            throw new InvalidOperationException("Skyline does not span the requested abscissa.");
        }

        return _sky * _buildings[index].Height(x);
    }

    /// <summary>Returns the greatest height the skyline reaches, signed by its direction.</summary>
    /// <returns>The maximum height.</returns>
    public double MaxHeight()
    {
        double ret = double.NegativeInfinity;

        foreach (Building b in _buildings)
        {
            ret = Math.Max(ret, b.Height(b.X.Left));
            ret = Math.Max(ret, b.Height(b.X.Right));
        }

        return _sky * ret;
    }

    /// <summary>Returns the abscissa at which the skyline is highest.</summary>
    /// <returns>The abscissa.</returns>
    public double MaxHeightPosition()
    {
        Skyline s = new Skyline(-_sky);
        s.SetMinimumHeight(0.0);
        return TouchingPoint(s);
    }

    /// <summary>Returns the abscissa where the skyline's first real building starts.</summary>
    /// <returns>The left edge, or positive infinity when the skyline is empty.</returns>
    public double Left()
    {
        foreach (Building b in _buildings)
        {
            if (b.YIntercept > double.NegativeInfinity)
            {
                return b.X.Left;
            }
        }

        return double.PositiveInfinity;
    }

    /// <summary>Returns the abscissa where the skyline's last real building ends.</summary>
    /// <returns>The right edge, or negative infinity when the skyline is empty.</returns>
    public double Right()
    {
        for (int i = _buildings.Count - 1; i >= 0; i--)
        {
            if (_buildings[i].YIntercept > double.NegativeInfinity)
            {
                return _buildings[i].X.Right;
            }
        }

        return double.NegativeInfinity;
    }

    /// <summary>
    /// Returns a copy widened horizontally: every building gains a flat apron and a
    /// sloped ramp on each side.
    /// </summary>
    /// <param name="horizonPadding">The padding width. Zero or less returns this skyline.</param>
    /// <returns>The padded skyline.</returns>
    public Skyline Padded(double horizonPadding)
    {
        if (horizonPadding < 0.0)
        {
            Warn.Warning("Cannot have negative horizon padding.  Junking.");
        }

        if (horizonPadding <= 0.0)
        {
            return this;
        }

        List<Building> padBuildings = new List<Building>(4 * _buildings.Count);
        foreach (Building b in _buildings)
        {
            if (b.X.Left > double.NegativeInfinity)
            {
                double height = b.Height(b.X.Left);
                if (height > double.NegativeInfinity)
                {
                    // Add the sloped building that pads the left side of the current building.
                    double start = b.X.Left - (2 * horizonPadding);
                    double end = b.X.Left - horizonPadding;
                    padBuildings.Add(new Building(start, height - horizonPadding, height, end));

                    // Add the flat building that pads the left side of the current building.
                    start = b.X.Left - horizonPadding;
                    end = b.X.Left;
                    padBuildings.Add(new Building(start, height, height, end));
                }
            }

            if (b.X.Right < double.PositiveInfinity)
            {
                double height = b.Height(b.X.Right);
                if (height > double.NegativeInfinity)
                {
                    // Add the flat building that pads the right side of the current building.
                    double start = b.X.Right;
                    double end = start + horizonPadding;
                    padBuildings.Add(new Building(start, height, height, end));

                    // Add the sloped building that pads the right side of the current building.
                    start = end;
                    end += horizonPadding;
                    padBuildings.Add(new Building(start, height, height - horizonPadding, end));
                }
            }
        }

        // The buildings may be overlapping, so resolve that.
        List<Building> padSkyline = InternalBuildSkyline(padBuildings);

        // Merge the padding with the original, to make a new skyline.
        Skyline padded = new Skyline(_sky);
        InternalMergeSkyline(padSkyline, _buildings, padded._buildings);

        return padded;
    }

    /// <summary>
    /// Returns the skyline as a point sequence, two points per building. On the Y
    /// horizon axis the coordinates are swapped, so the result is always in page space.
    /// </summary>
    /// <param name="horizonAxis">The axis the skyline runs along.</param>
    /// <returns>The points.</returns>
    public List<Offset> ToPoints(Axis horizonAxis)
    {
        List<Offset> result = new List<Offset>(2 * _buildings.Count);

        foreach (Building b in _buildings)
        {
            result.Add(new Offset(b.X.Left, _sky * b.Height(b.X.Left)));
            result.Add(new Offset(b.X.Right, _sky * b.Height(b.X.Right)));
        }

        if (horizonAxis == Axis.Y)
        {
            for (int i = 0; i < result.Count; i++)
            {
                result[i] = result[i].Swapped();
            }
        }

        return result;
    }

    private double InternalDistance(Skyline other, double horizonPadding, out double touchPoint)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        if (horizonPadding == 0.0)
        {
            return InternalDistance(other, out touchPoint);
        }

        // Note that it is not necessary to build a padded version of other,
        // because the same effect can be achieved just by doubling horizon_padding.
        Skyline paddedThis = Padded(horizonPadding);
        return paddedThis.InternalDistance(other, out touchPoint);
    }

    private double InternalDistance(Skyline other, out double touchPoint)
    {
        if (_sky != -other._sky)
        {
            throw new ArgumentException(
                "Distance is only defined between skylines facing opposite ways.",
                nameof(other));
        }

        int i = 0;
        int j = 0;

        double dist = double.NegativeInfinity;
        double start = double.NegativeInfinity;
        double touch = double.NegativeInfinity;
        while (i < _buildings.Count && j < other._buildings.Count)
        {
            Building bi = _buildings[i];
            Building bj = other._buildings[j];

            double end = Math.Min(bi.X.Right, bj.X.Right);
            double startDist = bi.Height(start) + bj.Height(start);
            double endDist = bi.Height(end) + bj.Height(end);
            dist = Math.Max(dist, Math.Max(startDist, endDist));

            if (endDist == dist)
            {
                touch = end;
            }
            else if (startDist == dist)
            {
                touch = start;
            }

            if (bi.X.Right <= bj.X.Right)
            {
                i++;
            }
            else
            {
                j++;
            }

            start = end;
        }

        touchPoint = touch;
        return dist;
    }

    private int LowerBound(double x)
    {
        // The predicate is "this building lies entirely to the left of x", which is
        // monotone over the building list, so a binary search finds the first building
        // whose right edge reaches x.
        int low = 0;
        int high = _buildings.Count;
        while (low < high)
        {
            int mid = low + ((high - low) / 2);
            if (_buildings[mid].X.Right < x)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private void InternalMergeSkyline(
        List<Building> sbp,
        List<Building> scp,
        List<Building> result)
    {
        if (sbp.Count == 0 || scp.Count == 0)
        {
            Warn.ProgrammingError("tried to merge an empty skyline");
            return;
        }

        result.Clear();
        result.Capacity = Math.Max(result.Capacity, Math.Max(sbp.Count, scp.Count));

        // Upstream walks two iterators and swaps both the iterators AND the containers
        // they point into, which keeps a single loop over "whichever list is behind".
        // The C# form keeps an index per list and swaps the index-plus-list pair.
        List<Building> bList = sbp;
        List<Building> cList = scp;
        int bIndex = 0;
        int cIndex = 0;

        Building b = bList[bIndex];
        while (cIndex < cList.Count)
        {
            /* Building b is continuing from the previous pass through the loop.
               Building c is newly-considered, and starts no earlier than b started.
               The comments draw b as if its roof had zero slope ----.
               with dashes where b lies above c.
               The roof of c could rise / or fall \ through the roof of b,
               or the vertical sides | of c could intersect the roof of b.  */
            Building c = cList[cIndex];
            if (b.X.Right < c.X.Right)
            {
                /* finish with b */
                if (b.X.Right <= b.X.Left)
                {
                    // we are already finished with b
                }
                else if (c.Above(b, c.X.Left))
                {
                    /* -|   . | */
                    Building m = b;
                    m.X.Right = c.X.Left;
                    if (m.X.Right > m.X.Left)
                    {
                        result.Add(m);
                    }

                    if (b.Above(c, b.X.Right))
                    {
                        /* -|\--.   */
                        Building n = c;
                        double crossing = b.IntersectionX(c);
                        n.X.Right = crossing;
                        b.X.Left = crossing;
                        result.Add(n);
                        result.Add(b);
                        c.X.Left = b.X.Right;
                    }
                }
                else
                {
                    if (c.Above(b, b.X.Right))
                    {
                        /* ---/ . | */
                        double crossing = b.IntersectionX(c);
                        c.X.Left = crossing;
                        b.X.Right = crossing;
                    }
                    else
                    {
                        /* -----.   */
                        c.X.Left = b.X.Right;
                    }

                    result.Add(b);
                }

                /* 'c' continues further, so move it into 'b' for the next pass. */
                b = c;
                (bIndex, cIndex) = (cIndex, bIndex);
                (bList, cList) = (cList, bList);
            }
            else
            {
                /* b.x_[RIGHT] > c.x_[RIGHT] so finish with c */
                if (c.Above(b, c.X.Left))
                {
                    /* -| |---. */
                    Building m = b;
                    m.X.Right = c.X.Left;
                    if (m.X.Right > m.X.Left)
                    {
                        result.Add(m);
                    }

                    if (b.Above(c, c.X.Right))
                    {
                        /* -| \---. */
                        c.X.Right = b.IntersectionX(c);
                    }
                }
                else if (c.Above(b, c.X.Right))
                {
                    /* ---/|--. */
                    Building m = b;
                    double crossing = b.IntersectionX(c);
                    c.X.Left = crossing;
                    m.X.Right = crossing;
                    result.Add(m);
                }
                else
                {
                    /* c is completely hidden by b */
                    cIndex++;
                    continue;
                }

                result.Add(c);
                b.X.Left = c.X.Right;
            }

            cIndex++;
        }

        if (b.X.Right > b.X.Left)
        {
            result.Add(b);
        }
    }

    private static void EmptySkyline(List<Building> result)
    {
        result.Add(new Building(
            double.NegativeInfinity,
            double.NegativeInfinity,
            double.NegativeInfinity,
            double.PositiveInfinity));
    }

    /// <summary>
    /// Given Building 'b', build a skyline containing only that building.
    /// </summary>
    private static void SingleSkyline(Building b, List<Building> result)
    {
        if (b.X.Right < b.X.Left)
        {
            throw new ArgumentException("A building cannot end before it starts.", nameof(b));
        }

        if (b.X.Left != double.NegativeInfinity)
        {
            result.Add(new Building(
                double.NegativeInfinity,
                double.NegativeInfinity,
                double.NegativeInfinity,
                b.X.Left));
        }

        result.Add(b);

        if (b.X.Right != double.PositiveInfinity)
        {
            result.Add(new Building(
                b.X.Right,
                double.NegativeInfinity,
                double.NegativeInfinity,
                double.PositiveInfinity));
        }
    }

    /// <summary>Partition BUILDINGS into a non-overlapping set of boxes and the rest.</summary>
    private static void NonOverlappingSkyline(
        List<Building> buildings,
        List<Building> trimmed,
        List<Building> result)
    {
        trimmed.Capacity = Math.Max(trimmed.Capacity, buildings.Count / 2);
        result.Capacity = Math.Max(result.Capacity, buildings.Count / 2);
        double lastEnd = double.NegativeInfinity;
        Building lastBuilding = new Building(
            double.NegativeInfinity,
            double.NegativeInfinity,
            double.NegativeInfinity,
            double.PositiveInfinity);

        foreach (Building b in buildings)
        {
            double x1 = b.X.Left;
            double y1 = b.Height(b.X.Left);
            double x2 = b.X.Right;
            double y2 = b.Height(b.X.Right);

            // Drop buildings that will obviously have no effect.
            if (lastBuilding.Height(x1) >= y1
                && lastBuilding.X.Right >= x2
                && lastBuilding.Height(x2) >= y2)
            {
                continue;
            }

            if (x1 < lastEnd)
            {
                trimmed.Add(b);
                continue;
            }

            // Insert empty Buildings into any gaps. (TODO: is this needed? -KOH)
            if (x1 > lastEnd)
            {
                result.Add(new Building(lastEnd, double.NegativeInfinity, double.NegativeInfinity, x1));
            }

            result.Add(b);
            lastBuilding = b;
            lastEnd = b.X.Right;
        }

        if (lastEnd < double.PositiveInfinity)
        {
            result.Add(new Building(
                lastEnd,
                double.NegativeInfinity,
                double.NegativeInfinity,
                double.PositiveInfinity));
        }
    }

    /// <summary>
    /// Orders buildings left edge first, then taller-at-that-edge first. Upstream's
    /// <c>LessThanBuilding</c>.
    /// </summary>
    private static int CompareBuildings(Building b1, Building b2)
    {
        if (b1.X.Left != b2.X.Left)
        {
            return b1.X.Left < b2.X.Left ? -1 : 1;
        }

        double h1 = b1.Height(b1.X.Left);
        double h2 = b2.Height(b1.X.Left);
        if (h1 == h2)
        {
            return 0;
        }

        return h1 > h2 ? -1 : 1;
    }

    /// <summary>
    /// BUILDINGS is a list of buildings, but they could be overlapping and in any
    /// order. The returned list of buildings is ordered and non-overlapping.
    /// </summary>
    private List<Building> InternalBuildSkyline(List<Building> buildings)
    {
        int size = buildings.Count;

        if (size == 0)
        {
            List<Building> result = new List<Building>();
            EmptySkyline(result);
            return result;
        }

        if (size == 1)
        {
            List<Building> result = new List<Building>();
            SingleSkyline(buildings[0], result);
            return result;
        }

        Queue<List<Building>> partials = new Queue<List<Building>>();

        // A stable merge sort, not List<T>.Sort. Upstream's comparator is not a strict
        // weak ordering once heights tie on NaN or infinity, and .NET's introsort
        // validates its comparer and throws where the C++ simply carries on.
        List<Building> working = StableSort(buildings, CompareBuildings);
        while (working.Count > 0)
        {
            List<Building> trimmed = new List<Building>();
            List<Building> partial = new List<Building>();
            NonOverlappingSkyline(working, trimmed, partial);
            partials.Enqueue(partial);
            working = trimmed;
        }

        /* we'd like to say while (partials->size () > 1) but that's O (n).
           Instead, we exit in the middle of the loop */
        while (partials.Count > 0)
        {
            List<Building> one = partials.Dequeue();
            if (partials.Count == 0)
            {
                return one;
            }

            List<Building> two = partials.Dequeue();

            List<Building> merged = new List<Building>();
            InternalMergeSkyline(one, two, merged);
            partials.Enqueue(merged);
        }

        throw new InvalidOperationException("Unreachable: skyline partial merge ran dry.");
    }

    private static List<Building> StableSort(List<Building> source, Comparison<Building> comparison)
    {
        Building[] array = source.ToArray();
        Building[] scratch = new Building[array.Length];
        MergeSort(array, scratch, 0, array.Length, comparison);
        return new List<Building>(array);
    }

    private static void MergeSort(
        Building[] array,
        Building[] scratch,
        int start,
        int end,
        Comparison<Building> comparison)
    {
        if (end - start < 2)
        {
            return;
        }

        int mid = start + ((end - start) / 2);
        MergeSort(array, scratch, start, mid, comparison);
        MergeSort(array, scratch, mid, end, comparison);

        int i = start;
        int j = mid;
        int k = start;
        while (i < mid && j < end)
        {
            // Ask only "does the right element precede the left", which keeps the sort
            // stable and never demands a total order from the comparison.
            if (comparison(array[j], array[i]) < 0)
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
