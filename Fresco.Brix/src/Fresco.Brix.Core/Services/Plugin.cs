// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Fresco.Brix.Services; //was previously: frescobaldi/plugin.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A component that extends another object without that object knowing about
/// it: created on first request for an owner, kept only as long as the owner
/// lives, and returned again for every later request.
/// <para>
/// Upstream keeps the instances in a <c>WeakKeyDictionary</c> keyed by plugin
/// class; here a per-subclass <see cref="ConditionalWeakTable{TKey,TValue}"/>
/// does the same job, and the same rule applies: NEVER hold a plugin in a
/// field that outlives its owner.
/// </para>
/// </summary>
/// <typeparam name="TOwner">The type the plugin extends.</typeparam>
/// <typeparam name="TSelf">The concrete plugin type.</typeparam>
public abstract class Plugin<TOwner, TSelf>
    where TOwner : class
    where TSelf : Plugin<TOwner, TSelf>
{
    private static readonly ConditionalWeakTable<TOwner, TSelf> Instances
        = new ConditionalWeakTable<TOwner, TSelf>();

    //Upstream's instances() iterates the living plugins; ConditionalWeakTable
    //is not enumerable across frameworks, so the live set is tracked beside it
    //with weak references and pruned on every walk.
    private static readonly List<WeakReference<TSelf>> Live
        = new List<WeakReference<TSelf>>();

    private readonly WeakReference<TOwner> _owner;

    /// <summary>Creates the plugin for an owner.</summary>
    /// <param name="owner">The object being extended.</param>
    protected Plugin(TOwner owner)
        => _owner = new WeakReference<TOwner>(
            owner ?? throw new ArgumentNullException(nameof(owner)));

    /// <summary>Gets the owner, or null once it has been collected.</summary>
    protected TOwner Owner
        => _owner.TryGetTarget(out var owner) ? owner : null;

    /// <summary>
    /// Gets the plugin for an owner, creating it on first request.
    /// </summary>
    /// <param name="owner">The object being extended.</param>
    /// <param name="factory">Creates the plugin for the owner.</param>
    /// <returns>The plugin instance.</returns>
    protected static TSelf Instance(TOwner owner, Func<TOwner, TSelf> factory)
    {
        if (owner == null) { throw new ArgumentNullException(nameof(owner)); }

        if (Instances.TryGetValue(owner, out var existing))
        {
            return existing;
        }

        TSelf created = factory(owner);
        //A concurrent creator wins; answer whatever ended up in the table.
        if (!Instances.TryAdd(owner, created))
        {
            return Instances.TryGetValue(owner, out var raced) ? raced : created;
        }

        lock (Live)
        {
            Live.Add(new WeakReference<TSelf>(created));
        }

        return created;
    }

    /// <summary>Enumerates the living plugin instances of this type.</summary>
    /// <returns>The instances.</returns>
    protected static IEnumerable<TSelf> LiveInstances()
    {
        lock (Live)
        {
            List<TSelf> alive = new List<TSelf>();
            Live.RemoveAll(reference =>
            {
                if (reference.TryGetTarget(out var plugin))
                {
                    alive.Add(plugin);
                    return false;
                }

                return true;
            });

            return alive;
        }
    }

    /// <summary>Forgets every instance — the seam the tests reset with.</summary>
    internal static void ClearInstances()
    {
        lock (Live)
        {
            foreach (var plugin in LiveInstances().ToList())
            {
                var owner = plugin.Owner;
                if (owner != null)
                {
                    Instances.Remove(owner);
                }
            }

            Live.Clear();
        }
    }
}
