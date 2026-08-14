// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;

namespace CodeBrix.LilyPort.Engine.Objects;

// NEW IN FAMILY — no upstream file. This is a replica of libstdc++'s heap algorithm, which
// is what std::priority_queue is built on. It was first written privately inside BeamQuanting for
// beam configurations; the ties-and-slurs work generalized it in place rather than
// re-deriving it, because Slur_score_state::get_best_curve runs the identical pattern over
// Slur_configuration with the identical inverting comparator.

/// <summary>
/// A faithful replica of <c>std::priority_queue</c> over libstdc++'s <c>push_heap</c> and
/// <c>pop_heap</c>.
/// </summary>
/// <remarks>
/// <para>
/// The point is TIE-BREAKING, not speed. A heap is not a sort: when two configurations
/// carry equal demerits, which one ends up on top is decided entirely by the heap's
/// internal element order, and two different heap implementations need not agree. Both
/// scorers reach exact ties routinely — symmetric music produces symmetric candidates — so
/// a merely-correct priority queue would place beams and slurs differently from upstream on
/// exactly the music where the difference is most visible.
/// </para>
/// <para>
/// Upstream's comparators (<c>Beam_configuration_less</c>, <c>Slur_configuration_less</c>)
/// both invert, so the top of the heap is the SMALLEST score. Callers pass that same
/// inverting predicate here.
/// </para>
/// </remarks>
/// <typeparam name="T">The configuration type being queued.</typeparam>
internal sealed class ConfigurationHeap<T>
{
    private readonly List<T> _items = new List<T>();
    private readonly Func<T, T, bool> _less;

    /// <summary>Initializes the heap with upstream's comparator.</summary>
    /// <param name="less">
    /// The strict weak ordering, passed exactly as upstream declares it — already inverted,
    /// so that the heap's top is the configuration the scorer considers best.
    /// </param>
    internal ConfigurationHeap(Func<T, T, bool> less)
        => _less = less ?? throw new ArgumentNullException(nameof(less));

    /// <summary>Gets how many configurations are queued.</summary>
    internal int Count => _items.Count;

    /// <summary>Returns the configuration at the top of the heap.</summary>
    /// <returns>The top configuration.</returns>
    internal T Top() => _items[0];

    /// <summary>Adds a configuration.</summary>
    /// <param name="value">The configuration to add.</param>
    internal void Push(T value)
    {
        _items.Add(value);
        PushHeap(_items.Count - 1, 0, value);
    }

    /// <summary>Removes the configuration at the top of the heap.</summary>
    internal void Pop()
    {
        int len = _items.Count - 1;
        T value = _items[len];
        _items[len] = _items[0];
        AdjustHeap(0, len, value);
        _items.RemoveAt(_items.Count - 1);
    }

    private void PushHeap(int holeIndex, int topIndex, T value)
    {
        int parent = (holeIndex - 1) / 2;
        while (holeIndex > topIndex && _less(_items[parent], value))
        {
            _items[holeIndex] = _items[parent];
            holeIndex = parent;
            parent = (holeIndex - 1) / 2;
        }

        _items[holeIndex] = value;
    }

    private void AdjustHeap(int holeIndex, int len, T value)
    {
        int topIndex = holeIndex;
        int secondChild = holeIndex;
        while (secondChild < (len - 1) / 2)
        {
            secondChild = 2 * (secondChild + 1);
            if (_less(_items[secondChild], _items[secondChild - 1]))
            {
                secondChild--;
            }

            _items[holeIndex] = _items[secondChild];
            holeIndex = secondChild;
        }

        if ((len & 1) == 0 && secondChild == (len - 2) / 2)
        {
            secondChild = 2 * (secondChild + 1);
            _items[holeIndex] = _items[secondChild - 1];
            holeIndex = secondChild - 1;
        }

        PushHeap(holeIndex, topIndex, value);
    }
}
