/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2011--2026 Joe Neeman <joeneeman@gmail.com>

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

namespace CodeBrix.LilyPort.Flower; //was previously: flower/interval-set.cc, flower/include/interval-set.hh;
// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - translated from C++17 to C# targeting net10.0
//   - std::upper_bound over a sorted vector becomes an explicit binary search, so
//     that the iterator arithmetic upstream relies on (i - 1, i != begin) maps onto
//     an index without ambiguity

/// <summary>
/// A set of disjoint, sorted intervals. LilyPond uses these for skyline and spacing
/// work, where "the regions that are occupied" has to be queried quickly.
/// </summary>
public sealed class IntervalSet
{
    private readonly List<Interval> _intervals;

    /// <summary>Initializes an empty set.</summary>
    public IntervalSet()
    {
        _intervals = new List<Interval>();
    }

    private IntervalSet(List<Interval> intervals)
    {
        _intervals = intervals;
    }

    /// <summary>Gets the intervals, sorted by left bound and pairwise disjoint.</summary>
    public IReadOnlyList<Interval> Intervals => _intervals;

    /// <summary>
    /// Builds the union of a collection of intervals, merging any that touch or
    /// overlap. Note that adjacent intervals sharing an endpoint are merged, because
    /// upstream tests <c>last.Right &gt;= interval.Left</c> rather than a strict
    /// inequality.
    /// </summary>
    /// <param name="intervals">The intervals to unite.</param>
    /// <returns>The union as a disjoint sorted set.</returns>
    public static IntervalSet IntervalUnion(IEnumerable<Interval> intervals)
    {
        List<Interval> sorted = new List<Interval>(intervals ?? Array.Empty<Interval>());
        sorted.Sort((a, b) => a.Left.CompareTo(b.Left));

        IntervalSet result = new IntervalSet();
        if (sorted.Count == 0)
        {
            return result;
        }

        result._intervals.Add(sorted[0]);
        for (int i = 1; i < sorted.Count; i++)
        {
            Interval current = sorted[i];
            Interval last = result._intervals[result._intervals.Count - 1];
            if (last.Right >= current.Left)
            {
                last.Right = Math.Max(last.Right, current.Right);
                result._intervals[result._intervals.Count - 1] = last;
            }
            else if (!current.IsEmpty)
            {
                result._intervals.Add(current);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the index of the first interval whose left bound is strictly greater
    /// than <paramref name="x"/> — the equivalent of <c>std::upper_bound</c>.
    /// </summary>
    /// <param name="x">The value to search for.</param>
    /// <returns>An index in 0..Count.</returns>
    public int UpperBound(double x)
    {
        int low = 0;
        int high = _intervals.Count;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (_intervals[middle].Left <= x)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>
    /// Returns the point in the set nearest to <paramref name="x"/>, optionally
    /// restricted to one side. When <paramref name="x"/> already lies inside an
    /// interval, it is returned unchanged.
    /// </summary>
    /// <param name="x">The value to search from.</param>
    /// <param name="direction">
    /// Positive searches rightwards only, negative leftwards only, and centre takes
    /// whichever is closer.
    /// </param>
    /// <returns>The nearest occupied point, possibly infinite when none exists.</returns>
    public double NearestPoint(double x, Direction direction)
    {
        double left = double.NegativeInfinity;
        double right = double.PositiveInfinity;

        int index = UpperBound(x);
        if (index != _intervals.Count)
        {
            right = _intervals[index].Left;
        }

        if (index != 0)
        {
            Interval leftInterval = _intervals[index - 1];
            if (leftInterval.Right >= x)
            {
                return x;
            }

            left = leftInterval.Right;
        }

        if (direction > Direction.Center)
        {
            return right;
        }

        if (direction < Direction.Center)
        {
            return left;
        }

        return (right - x) < (x - left) ? right : left;
    }

    /// <summary>Returns the nearest point in either direction.</summary>
    /// <param name="x">The value to search from.</param>
    /// <returns>The nearest occupied point.</returns>
    public double NearestPoint(double x) => NearestPoint(x, Direction.Center);

    /// <summary>Returns the complement of this set over the whole real line.</summary>
    /// <returns>The gaps between and around this set's intervals.</returns>
    public IntervalSet Complement()
    {
        IntervalSet result = new IntervalSet();

        if (_intervals.Count == 0)
        {
            result._intervals.Add(new Interval(double.NegativeInfinity, double.PositiveInfinity));
            return result;
        }

        if (_intervals[0].Left > double.NegativeInfinity)
        {
            result._intervals.Add(new Interval(double.NegativeInfinity, _intervals[0].Left));
        }

        for (int i = 1; i < _intervals.Count; i++)
        {
            result._intervals.Add(new Interval(_intervals[i - 1].Right, _intervals[i].Left));
        }

        if (_intervals[_intervals.Count - 1].Right < double.PositiveInfinity)
        {
            result._intervals.Add(
                new Interval(_intervals[_intervals.Count - 1].Right, double.PositiveInfinity));
        }

        return result;
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The intervals in order.</returns>
    public override string ToString() => "{" + string.Join(" ", _intervals) + "}";
}

/// <summary>
/// A dense two-dimensional array stored COLUMN-MAJOR, matching upstream's
/// <c>data_[col * rows + row]</c> indexing. LilyPond uses it for the page-breaking
/// dynamic-programming tables.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public sealed class Matrix<T>
{
    private T[] _data;
    private int _rows;

    /// <summary>Initializes an empty matrix.</summary>
    public Matrix()
    {
        _data = Array.Empty<T>();
        _rows = 0;
    }

    /// <summary>Initializes a matrix filled with a value.</summary>
    /// <param name="rows">The row count.</param>
    /// <param name="columns">The column count.</param>
    /// <param name="fill">The initial value for every cell.</param>
    public Matrix(int rows, int columns, T fill)
    {
        _rows = rows;
        _data = new T[rows * columns];
        for (int i = 0; i < _data.Length; i++)
        {
            _data[i] = fill;
        }
    }

    /// <summary>Gets the row count.</summary>
    public int Rows => _rows;

    /// <summary>Gets the column count.</summary>
    public int Columns => _rows == 0 ? 0 : _data.Length / _rows;

    /// <summary>Gets or sets a cell.</summary>
    /// <param name="row">The zero-based row.</param>
    /// <param name="column">The zero-based column.</param>
    /// <returns>The cell value.</returns>
    public T this[int row, int column]
    {
        get => _data[(column * _rows) + row];
        set => _data[(column * _rows) + row] = value;
    }

    /// <summary>
    /// Resizes the matrix, preserving the overlapping region. When the row count is
    /// unchanged this is a simple grow, matching upstream's fast path.
    /// </summary>
    /// <param name="rows">The new row count.</param>
    /// <param name="columns">The new column count.</param>
    /// <param name="fill">The value for newly created cells.</param>
    public void Resize(int rows, int columns, T fill)
    {
        if (rows == _rows)
        {
            int wanted = rows * columns;
            if (wanted == _data.Length)
            {
                return;
            }

            T[] grown = new T[wanted];
            int copy = Math.Min(_data.Length, wanted);
            Array.Copy(_data, grown, copy);
            for (int i = copy; i < wanted; i++)
            {
                grown[i] = fill;
            }

            _data = grown;
            return;
        }

        T[] replacement = new T[rows * columns];
        for (int i = 0; i < replacement.Length; i++)
        {
            replacement[i] = fill;
        }

        int currentColumns = _rows != 0 ? _data.Length / _rows : 0;
        int copyColumns = Math.Min(columns, currentColumns);
        int copyRows = Math.Min(rows, _rows);
        for (int column = 0; column < copyColumns; column++)
        {
            Array.Copy(_data, column * _rows, replacement, column * rows, copyRows);
        }

        _rows = rows;
        _data = replacement;
    }
}
