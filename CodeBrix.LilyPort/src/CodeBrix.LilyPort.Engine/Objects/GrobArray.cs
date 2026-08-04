/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using System.Collections;
using System.Collections.Generic;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/grob-array.cc, lily/include/grob-array.hh, lily/pointer-group-interface.cc, lily/include/pointer-group-interface.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// A list of grobs held in a grob's object alist.
/// <para>
/// Every grob-to-grob link that is one-to-MANY goes through one of these:
/// <c>elements</c>, <c>columns</c>, <c>all-elements</c>, <c>note-heads</c> and the
/// rest. The <see cref="IsOrdered"/> flag matters — an unordered array may be
/// deduplicated and reordered freely, an ordered one may not, and
/// <c>Axis_group_interface</c> depends on <c>elements</c> staying ordered because
/// <c>Align_interface</c> reads the same array.
/// </para>
/// </summary>
public sealed class GrobArray : IEnumerable<Grob>
{
    private readonly List<Grob> _grobs = new List<Grob>();

    /// <summary>Initializes an empty, ordered array.</summary>
    public GrobArray() => IsOrdered = true;

    /// <summary>
    /// Gets or sets a value indicating whether the order of this array is meaningful.
    /// </summary>
    public bool IsOrdered { get; set; }

    /// <summary>Gets the number of grobs.</summary>
    public int Count => _grobs.Count;

    /// <summary>Gets a value indicating whether the array is empty.</summary>
    public bool IsEmpty => _grobs.Count == 0;

    /// <summary>Gets the grobs, in order.</summary>
    public IReadOnlyList<Grob> Array => _grobs;

    /// <summary>Gets or sets the grob at an index.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The grob.</returns>
    public Grob this[int index]
    {
        get => _grobs[index];
        set => _grobs[index] = value;
    }

    /// <summary>Appends a grob.</summary>
    /// <param name="grob">The grob to add.</param>
    public void Add(Grob grob) => _grobs.Add(grob);

    /// <summary>Removes every grob.</summary>
    public void Clear() => _grobs.Clear();

    /// <summary>Replaces the contents.</summary>
    /// <param name="source">The grobs to hold.</param>
    public void SetArray(IEnumerable<Grob> source)
    {
        _grobs.Clear();
        if (source != null)
        {
            _grobs.AddRange(source);
        }
    }

    /// <summary>
    /// Removes duplicate grobs, keeping the first of each. Upstream sorts by address
    /// and uniquifies; the port preserves order instead, which is a superset of what
    /// the callers need and is deterministic.
    /// </summary>
    public void RemoveDuplicates()
    {
        HashSet<Grob> seen = new HashSet<Grob>(ReferenceEqualityComparer.Instance as IEqualityComparer<Grob>);
        List<Grob> unique = new List<Grob>(_grobs.Count);
        foreach (Grob grob in _grobs)
        {
            if (seen.Add(grob))
            {
                unique.Add(grob);
            }
        }

        _grobs.Clear();
        _grobs.AddRange(unique);
    }

    /// <summary>Returns an enumerator over the grobs.</summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<Grob> GetEnumerator() => _grobs.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Returns the external representation.</summary>
    /// <returns>The grob count.</returns>
    public override string ToString() => "#<Grob_array " + _grobs.Count + ">";
}

/// <summary>
/// Reads and writes the grob arrays hanging off a grob's object alist.
/// </summary>
public static class PointerGroupInterface
{
    private static readonly IReadOnlyList<Grob> EmptyArray = new List<Grob>();

    /// <summary>Returns how many grobs a link holds.</summary>
    /// <param name="grob">The grob holding the link.</param>
    /// <param name="symbol">The link name.</param>
    /// <returns>The count, or zero when the link is unset.</returns>
    public static int Count(Grob grob, Symbol symbol)
        => grob?.GetObject(symbol) is GrobArray array ? array.Count : 0;

    /// <summary>
    /// Returns the grob array a link holds, CREATING it when the link is unset.
    /// </summary>
    /// <param name="grob">The grob holding the link.</param>
    /// <param name="symbol">The link name.</param>
    /// <returns>The array.</returns>
    public static GrobArray GetGrobArray(Grob grob, Symbol symbol)
    {
        if (grob == null)
        {
            throw new ArgumentNullException(nameof(grob));
        }

        if (grob.GetObject(symbol) is GrobArray existing)
        {
            return existing;
        }

        GrobArray array = new GrobArray();
        grob.SetObject(symbol, array);
        return array;
    }

    /// <summary>Appends a grob to a link.</summary>
    /// <param name="grob">The grob holding the link.</param>
    /// <param name="symbol">The link name.</param>
    /// <param name="added">The grob to add.</param>
    public static void AddGrob(Grob grob, Symbol symbol, Grob added)
        => GetGrobArray(grob, symbol).Add(added);

    /// <summary>Appends a grob to a link and marks the link unordered.</summary>
    /// <param name="grob">The grob holding the link.</param>
    /// <param name="symbol">The link name.</param>
    /// <param name="added">The grob to add.</param>
    public static void AddUnorderedGrob(Grob grob, Symbol symbol, Grob added)
    {
        GrobArray array = GetGrobArray(grob, symbol);
        array.Add(added);
        array.IsOrdered = false;
    }

    /// <summary>Marks a link ordered or unordered.</summary>
    /// <param name="grob">The grob holding the link.</param>
    /// <param name="symbol">The link name.</param>
    /// <param name="ordered">Whether the order is meaningful.</param>
    public static void SetOrdered(Grob grob, Symbol symbol, bool ordered)
        => GetGrobArray(grob, symbol).IsOrdered = ordered;

    /// <summary>
    /// Returns the grobs a link holds WITHOUT creating the link when it is unset.
    /// <para>
    /// Upstream's <c>extract_grob_set</c>. Reading must not have the side effect of
    /// creating an empty array, because a great many callers read links that most
    /// grobs never have.
    /// </para>
    /// </summary>
    /// <param name="grob">The grob holding the link.</param>
    /// <param name="symbol">The link name.</param>
    /// <returns>The grobs, or an empty list.</returns>
    public static IReadOnlyList<Grob> ExtractGrobSet(Grob grob, Symbol symbol)
        => grob?.GetObject(symbol) is GrobArray array ? array.Array : EmptyArray;

    /// <summary>Returns the grobs a link holds, by link name.</summary>
    /// <param name="grob">The grob holding the link.</param>
    /// <param name="name">The link name.</param>
    /// <returns>The grobs, or an empty list.</returns>
    public static IReadOnlyList<Grob> ExtractGrobSet(Grob grob, string name)
        => ExtractGrobSet(grob, Symbol.Intern(name));

    /// <summary>
    /// Returns the grobs a link holds, keeping only those of one subtype and
    /// reporting the rest as a programming error, as upstream does.
    /// </summary>
    /// <typeparam name="T">The grob subtype to keep.</typeparam>
    /// <param name="grob">The grob holding the link.</param>
    /// <param name="symbol">The link name.</param>
    /// <returns>The matching grobs.</returns>
    public static List<T> ExtractGrobSet<T>(Grob grob, Symbol symbol)
        where T : Grob
    {
        List<T> result = new List<T>();
        foreach (Grob candidate in ExtractGrobSet(grob, symbol))
        {
            if (candidate is T specific)
            {
                result.Add(specific);
            }
            else
            {
                CodeBrix.LilyPort.Flower.Warn.ProgrammingError(
                    "unexpected grob subtype in grob array");
            }
        }

        return result;
    }
}
