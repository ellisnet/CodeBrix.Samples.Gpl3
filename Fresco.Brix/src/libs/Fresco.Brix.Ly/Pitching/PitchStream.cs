// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;

namespace Fresco.Brix.Ly.Pitching;

/// <summary>
/// A pull-style reader over <see cref="PitchIterator.Pitches"/>, standing in
/// for python's <c>next(psource)</c>: <see cref="Next"/> answers the next item
/// or throws <see cref="StopIterationSignal"/> when the stream is spent, and
/// <see cref="Prepend"/> is the <c>itertools.chain</c> the pitch tools use to
/// push a consumed token back in front. New plumbing, not an upstream class.
/// </summary>
internal sealed class PitchStream
{
    private readonly IEnumerator<object> _source;
    private readonly Queue<object> _pending = new Queue<object>();

    /// <summary>Reads over a mixed token/pitch stream.</summary>
    /// <param name="source">The stream.</param>
    internal PitchStream(IEnumerable<object> source) => _source = source.GetEnumerator();

    /// <summary>Puts an item in front of the stream.</summary>
    /// <param name="item">The item.</param>
    internal void Prepend(object item) => _pending.Enqueue(item);

    /// <summary>Answers the next item.</summary>
    /// <returns>The item.</returns>
    /// <exception cref="StopIterationSignal">When the stream is spent.</exception>
    internal object Next()
    {
        if (_pending.Count > 0) { return _pending.Dequeue(); }

        if (_source.MoveNext()) { return _source.Current; }

        throw new StopIterationSignal();
    }
}
