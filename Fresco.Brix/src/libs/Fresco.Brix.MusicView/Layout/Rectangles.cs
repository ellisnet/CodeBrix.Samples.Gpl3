// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.MusicView; //was previously: qpageview/rectangles.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A list of rectangular things that can be asked, quickly, which of them a
/// point touches, which lie inside a rectangle and which intersect one.
/// </summary>
/// <typeparam name="T">What is being indexed — a page, or a link.</typeparam>
/// <remarks>
/// <para>
/// Four lists of the same objects, each sorted on one of the four sides. A
/// query is then four binary searches and a set intersection. Upstream chose
/// this over a quadtree because the lists are built once and read many times,
/// which is exactly how a page of music behaves: the engine writes the links,
/// and the view asks about them on every mouse move.
/// </para>
/// <para>
/// Adding in bulk clears the indexes, which are rebuilt on the first query
/// after that. Adding or removing one at a time keeps them, which is slower
/// per item.
/// </para>
/// </remarks>
public abstract class Rectangles<T> : IEnumerable<T>
{
    private const int Left = 0;
    private const int Top = 1;
    private const int Right = 2;
    private const int Bottom = 3;

    private readonly Dictionary<T, float[]> _items = new Dictionary<T, float[]>();
    private readonly Dictionary<int, (List<float> Indices, List<T> Objects)> _index
        = new Dictionary<int, (List<float>, List<T>)>();

    /// <summary>Creates an empty index.</summary>
    protected Rectangles()
    {
    }

    /// <summary>Creates an index over the given objects.</summary>
    /// <param name="objects">What to index.</param>
    protected Rectangles(IEnumerable<T> objects)
    {
        if (objects != null) { BulkAdd(objects); }
    }

    /// <summary>Gets how many objects are indexed.</summary>
    public int Count => _items.Count;

    /// <summary>
    /// Returns the coordinates of an object as (left, top, right, bottom),
    /// with left below right and top below bottom. Asked once per object.
    /// </summary>
    /// <param name="obj">The object.</param>
    /// <returns>Its rectangle.</returns>
    protected abstract (float Left, float Top, float Right, float Bottom) GetCoords(T obj);

    /// <summary>Adds one object, keeping the indexes usable.</summary>
    /// <param name="obj">The object to add.</param>
    public void Add(T obj)
    {
        if (_items.ContainsKey(obj)) { return; }

        float[] coords = ToArray(GetCoords(obj));
        _items[obj] = coords;
        foreach (var entry in _index)
        {
            int i = BisectLeft(entry.Value.Indices, coords[entry.Key]);
            entry.Value.Indices.Insert(i, coords[entry.Key]);
            entry.Value.Objects.Insert(i, obj);
        }
    }

    /// <summary>
    /// Adds many objects. The indexes are dropped and rebuilt on the first
    /// query that needs them.
    /// </summary>
    /// <param name="objects">The objects to add.</param>
    public void BulkAdd(IEnumerable<T> objects)
    {
        foreach (T obj in objects) { _items[obj] = ToArray(GetCoords(obj)); }

        _index.Clear();
    }

    /// <summary>Removes an object, keeping the indexes usable.</summary>
    /// <param name="obj">The object to remove.</param>
    public void Remove(T obj)
    {
        if (!_items.Remove(obj)) { return; }

        foreach (var entry in _index)
        {
            int i = entry.Value.Objects.IndexOf(obj);
            if (i >= 0)
            {
                entry.Value.Objects.RemoveAt(i);
                entry.Value.Indices.RemoveAt(i);
            }
        }
    }

    /// <summary>Empties the index.</summary>
    public void Clear()
    {
        _items.Clear();
        _index.Clear();
    }

    /// <summary>Returns whether an object is indexed.</summary>
    /// <param name="obj">The object.</param>
    /// <returns>Whether it is here.</returns>
    public bool Contains(T obj) => _items.ContainsKey(obj);

    /// <summary>Returns the objects a point touches.</summary>
    /// <param name="x">The point's x.</param>
    /// <param name="y">The point's y.</param>
    /// <returns>The objects, in no particular order.</returns>
    public ISet<T> At(float x, float y)
        => Test((false, Top, y), (true, Bottom, y), (false, Left, x), (true, Right, x));

    /// <summary>Returns the objects wholly inside a rectangle.</summary>
    /// <param name="left">The rectangle's left.</param>
    /// <param name="top">The rectangle's top.</param>
    /// <param name="right">The rectangle's right.</param>
    /// <param name="bottom">The rectangle's bottom.</param>
    /// <returns>The objects, in no particular order.</returns>
    public ISet<T> Inside(float left, float top, float right, float bottom)
        => Test((true, Top, top), (false, Bottom, bottom), (true, Left, left), (false, Right, right));

    /// <summary>Returns the objects a rectangle touches.</summary>
    /// <param name="left">The rectangle's left.</param>
    /// <param name="top">The rectangle's top.</param>
    /// <param name="right">The rectangle's right.</param>
    /// <param name="bottom">The rectangle's bottom.</param>
    /// <returns>The objects, in no particular order.</returns>
    public ISet<T> Intersecting(float left, float top, float right, float bottom)
        => Test((false, Top, bottom), (true, Bottom, top), (false, Left, right), (true, Right, left));

    /// <summary>Returns an object's width — the key <c>At</c> results are sorted by.</summary>
    /// <param name="obj">The object.</param>
    /// <returns>Its width.</returns>
    public float Width(T obj)
    {
        float[] c = _items[obj];
        return c[Right] - c[Left];
    }

    /// <summary>Returns an object's height.</summary>
    /// <param name="obj">The object.</param>
    /// <returns>Its height.</returns>
    public float Height(T obj)
    {
        float[] c = _items[obj];
        return c[Bottom] - c[Top];
    }

    /// <summary>
    /// Returns the object closest to a point that the point does NOT touch.
    /// </summary>
    /// <param name="x">The point's x.</param>
    /// <param name="y">The point's y.</param>
    /// <returns>The nearest object, or the type's default when there are none.</returns>
    /// <remarks>
    /// Upstream's algorithm: take the nearest candidate on each of the four
    /// sides, then consider the ones that only a corner could bring closer.
    /// </remarks>
    public T Nearest(float x, float y)
    {
        List<T> left = Larger(Left, x);      // closest is first
        List<T> right = Smaller(Right, x);   // closest is last
        List<T> top = Larger(Top, y);        // closest is first
        List<T> bottom = Smaller(Bottom, y); // closest is last

        var topSet = new HashSet<T>(top);
        var bottomSet = new HashSet<T>(bottom);
        var leftSet = new HashSet<T>(left);
        var rightSet = new HashSet<T>(right);

        var result = new List<(float Distance, T Object)>();

        int leftOver = 0;
        foreach (T o in left)
        {
            if (!topSet.Contains(o) && !bottomSet.Contains(o))
            {
                result.Add((_items[o][Left] - x, o));
                break;
            }

            leftOver++;
        }

        int topOver = 0;
        foreach (T o in top)
        {
            if (!leftSet.Contains(o) && !rightSet.Contains(o))
            {
                result.Add((_items[o][Top] - y, o));
                break;
            }

            topOver++;
        }

        int rightOver = 0;
        for (int i = right.Count - 1; i >= 0; i--)
        {
            T o = right[i];
            if (!topSet.Contains(o) && !bottomSet.Contains(o))
            {
                result.Add((x - _items[o][Right], o));
                break;
            }

            rightOver++;
        }

        int bottomOver = 0;
        for (int i = bottom.Count - 1; i >= 0; i--)
        {
            T o = bottom[i];
            if (!leftSet.Contains(o) && !rightSet.Contains(o))
            {
                result.Add((y - _items[o][Bottom], o));
                break;
            }

            bottomOver++;
        }

        if (leftOver > 0 && topOver > 0)
        {
            foreach (T o in Head(left, leftOver).Intersect(Head(top, topOver)))
            {
                result.Add((_items[o][Left] - x + _items[o][Top] - y, o));
            }
        }

        if (topOver > 0 && rightOver > 0)
        {
            foreach (T o in Head(top, topOver).Intersect(Tail(right, rightOver)))
            {
                result.Add((_items[o][Top] - y + x - _items[o][Right], o));
            }
        }

        if (leftOver > 0 && bottomOver > 0)
        {
            foreach (T o in Head(left, leftOver).Intersect(Tail(bottom, bottomOver)))
            {
                result.Add((_items[o][Left] - x + y - _items[o][Bottom], o));
            }
        }

        if (bottomOver > 0 && rightOver > 0)
        {
            foreach (T o in Tail(bottom, bottomOver).Intersect(Tail(right, rightOver)))
            {
                result.Add((y - _items[o][Bottom] + x - _items[o][Right], o));
            }
        }

        if (result.Count == 0) { return default; }

        var best = result[0];
        foreach (var candidate in result)
        {
            if (candidate.Distance < best.Distance) { best = candidate; }
        }

        return best.Object;
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => _items.Keys.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static float[] ToArray((float Left, float Top, float Right, float Bottom) c)
        => new[] { c.Left, c.Top, c.Right, c.Bottom };

    private static IEnumerable<T> Head(List<T> list, int count) => list.Take(count);

    private static IEnumerable<T> Tail(List<T> list, int count) => list.Skip(Math.Max(0, list.Count - count));

    private static int BisectLeft(List<float> values, float value)
    {
        int lo = 0;
        int hi = values.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (values[mid] < value) { lo = mid + 1; } else { hi = mid; }
        }

        return lo;
    }

    private static int BisectRight(List<float> values, float value)
    {
        int lo = 0;
        int hi = values.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (value < values[mid]) { hi = mid; } else { lo = mid + 1; }
        }

        return lo;
    }

    private ISet<T> Test(params (bool Larger, int Side, float Value)[] tests)
    {
        var result = new HashSet<T>(tests[0].Larger
            ? Larger(tests[0].Side, tests[0].Value)
            : Smaller(tests[0].Side, tests[0].Value));
        if (result.Count == 0) { return result; }

        for (int i = 1; i < tests.Length; i++)
        {
            result.IntersectWith(tests[i].Larger
                ? Larger(tests[i].Side, tests[i].Value)
                : Smaller(tests[i].Side, tests[i].Value));
            if (result.Count == 0) { break; }
        }

        return result;
    }

    private List<T> Smaller(int side, float value)
    {
        var (indices, objects) = Sorted(side);
        return objects.GetRange(0, BisectRight(indices, value));
    }

    private List<T> Larger(int side, float value)
    {
        var (indices, objects) = Sorted(side);
        int i = BisectLeft(indices, value);
        return objects.GetRange(i, objects.Count - i);
    }

    private (List<float> Indices, List<T> Objects) Sorted(int side)
    {
        if (_index.TryGetValue(side, out var cached)) { return cached; }

        var pairs = _items.Select(kv => (Key: kv.Value[side], Object: kv.Key))
            .OrderBy(p => p.Key)
            .ToList();
        var result = (pairs.Select(p => p.Key).ToList(), pairs.Select(p => p.Object).ToList());
        _index[side] = result;
        return result;
    }
}
