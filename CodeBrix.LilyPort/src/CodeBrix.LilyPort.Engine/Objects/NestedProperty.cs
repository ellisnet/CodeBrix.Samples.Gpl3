/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2010--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/nested-property.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Alist surgery for nested grob properties — the operations
/// <c>\override Beam.details.beamed-stem-lengths = …</c> is built out of.
/// <para>
/// Everything here depends on IDENTITY, not on equality. A grob's property alist is a
/// list whose TAIL is physically shared with the alist it was based on in the enclosing
/// context, and the whole override/revert mechanism works by copying only the spine
/// before a known shared tail. Comparing by value instead of by reference would still
/// produce a correct-looking list and would silently break the sharing that makes
/// <c>\revert</c> find what it has to undo.
/// </para>
/// </summary>
public static class NestedProperty
{
    /// <summary>
    /// Reverses a list destructively onto a tail. Upstream's <c>fast_reverse_x</c>:
    /// <c>reverse!</c> without the checks.
    /// </summary>
    /// <param name="list">The list to reverse.</param>
    /// <param name="tail">The tail to append.</param>
    /// <returns>The reversed list.</returns>
    public static object FastReverse(object list, object tail)
    {
        while (list is Pair pair)
        {
            object next = pair.Cdr;
            pair.Cdr = tail;
            tail = pair;
            list = next;
        }

        return tail;
    }

    /// <summary>
    /// Copies the spine of a list up to but not including a tail, then appends a new
    /// tail.
    /// </summary>
    /// <param name="list">The list to copy.</param>
    /// <param name="tail">Where to stop, compared by identity.</param>
    /// <param name="newTail">What to append instead.</param>
    /// <returns>The new list.</returns>
    public static object PartialListCopy(object list, object tail, object newTail)
    {
        object copied = Nil.Instance;
        while (!ReferenceEquals(list, tail) && list is Pair pair)
        {
            copied = new Pair(pair.Car, copied);
            list = pair.Cdr;
        }

        return FastReverse(copied, newTail);
    }

    /// <summary>
    /// Returns the sublist whose first entry has a key, searching only as far as a
    /// known tail.
    /// </summary>
    /// <param name="key">The key to find.</param>
    /// <param name="alist">The association list.</param>
    /// <param name="basedOn">Where to stop, compared by identity.</param>
    /// <returns>The sublist, or <see langword="false"/> when the key is absent.</returns>
    public static object AssocTail(object key, object alist, object basedOn = null)
    {
        object stop = basedOn ?? Nil.Instance;
        for (object cursor = alist; !ReferenceEquals(cursor, stop); )
        {
            if (!(cursor is Pair pair))
            {
                break;
            }

            if (pair.Car is Pair entry && KeysMatch(entry.Car, key))
            {
                return cursor;
            }

            cursor = pair.Cdr;
        }

        return false;
    }

    /// <summary>
    /// Reads a value out of nested alists by following a property path, which is
    /// upstream's <c>nested_property</c>: each path key selects an entry whose value
    /// is the alist the next key searches.
    /// </summary>
    /// <param name="alist">The association list to read.</param>
    /// <param name="propertyPath">The keys to follow, outermost first.</param>
    /// <param name="fallback">What a missing key returns; null means the empty list,
    /// matching upstream's <c>SCM_EOL</c> default.</param>
    /// <returns>The value the path reaches, or the fallback.</returns>
    public static object Get(object alist, object propertyPath, object fallback = null)
    {
        object result = alist;
        for (object path = propertyPath; path is Pair pair; path = pair.Cdr)
        {
            object tail = AssocTail(pair.Car, result);
            if (!(tail is Pair tailPair))
            {
                return fallback ?? Nil.Instance;
            }

            result = ((Pair)tailPair.Car).Cdr;
        }

        return result;
    }

    /// <summary>
    /// Removes the first entry with a key from an association list, returning both the
    /// entry and the shortened list.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <param name="alist">The association list, replaced by the shortened one.</param>
    /// <returns>The removed entry, or <see langword="false"/> when the key is absent.</returns>
    public static object AssqPop(object key, ref object alist)
    {
        object previous = null;
        object cursor = alist;
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry && ReferenceEquals(entry.Car, key))
            {
                if (previous is Pair previousPair)
                {
                    previousPair.Cdr = pair.Cdr;
                }
                else
                {
                    alist = pair.Cdr;
                }

                return entry;
            }

            previous = cursor;
            cursor = pair.Cdr;
        }

        return false;
    }

    /// <summary>Drops a key from the portion of a list before a known tail.</summary>
    /// <param name="key">The key to drop.</param>
    /// <param name="alist">The association list.</param>
    /// <param name="alistEnd">Where the shared tail begins.</param>
    /// <returns>The list without that key, or the original list when it was absent.</returns>
    public static object EvictFromAlist(object key, object alist, object alistEnd)
    {
        object found = AssocTail(key, alist, alistEnd);
        return found is Pair pair ? PartialListCopy(alist, pair, pair.Cdr) : alist;
    }

    /// <summary>
    /// Builds the nested alist a property path implies. The same as
    /// <see cref="NestedPropertyAlist"/> over an empty list, but faster.
    /// </summary>
    /// <param name="propertyPath">The path, outermost key first.</param>
    /// <param name="value">The value to store at the end of the path.</param>
    /// <returns>The nested alist.</returns>
    public static object NestedCreateAlist(object propertyPath, object value)
    {
        if (!(propertyPath is Pair pair))
        {
            return value;
        }

        return new Pair(
            new Pair(pair.Car, NestedCreateAlist(pair.Cdr, value)),
            Nil.Instance);
    }

    /// <summary>
    /// Writes a value into a grob property at a nested path — <c>set_nested_property</c>.
    /// </summary>
    /// <param name="me">The grob.</param>
    /// <param name="bigToSmall">
    /// The path, outermost property first: its head names the grob property to rewrite and
    /// its tail is the path within that property's alist.
    /// </param>
    /// <param name="value">The value to write.</param>
    /// <remarks>
    /// Added for <c>Tweak_engraver</c>. The body was already present,
    /// written out inline inside <c>ly:grob-set-nested-property!</c>; that binding now
    /// calls this, so there is one implementation rather than two.
    /// </remarks>
    public static void SetNestedProperty(Grob me, object bigToSmall, object value)
    {
        if (!(bigToSmall is Pair path) || !(path.Car is Symbol head))
        {
            return;
        }

        object alist = me.GetProperty(head);
        alist = NestedPropertyAlist(alist, path.Cdr, value);
        me.SetProperty(head, alist);
    }

    /// <summary>
    /// Replaces a nested property in an alist, returning the new alist. The path is
    /// ordered outermost key first.
    /// <para>
    /// Repeated overrides of the same path are deliberately NOT coalesced: upstream
    /// judges them rare enough that detecting them costs more than carrying them.
    /// </para>
    /// </summary>
    /// <param name="alist">The association list.</param>
    /// <param name="propertyPath">The path, outermost key first.</param>
    /// <param name="value">The value to store.</param>
    /// <returns>The new alist.</returns>
    public static object NestedPropertyAlist(object alist, object propertyPath, object value)
    {
        if (!(propertyPath is Pair path))
        {
            return alist;
        }

        object key = path.Car;
        object rest = path.Cdr;

        if (rest is Pair)
        {
            object where = AssocTail(key, alist);
            if (!(where is Pair wherePair))
            {
                return new Pair(new Pair(key, NestedCreateAlist(rest, value)), alist);
            }

            object inner = wherePair.Car is Pair entry ? entry.Cdr : Nil.Instance;
            return new Pair(
                new Pair(key, NestedPropertyAlist(inner, rest, value)),
                PartialListCopy(alist, wherePair, wherePair.Cdr));
        }

        return new Pair(new Pair(key, value), alist);
    }

    /// <summary>Reads a nested property out of an alist.</summary>
    /// <param name="alist">The association list.</param>
    /// <param name="propertyPath">The path, outermost key first.</param>
    /// <param name="fallback">What to answer when the path is not present.</param>
    /// <returns>The value, or the fallback.</returns>
    public static object NestedProperty_(object alist, object propertyPath, object fallback)
    {
        object cursor = propertyPath;
        while (cursor is Pair path)
        {
            object tail = AssocTail(path.Car, alist);
            if (!(tail is Pair tailPair) || !(tailPair.Car is Pair entry))
            {
                return fallback;
            }

            alist = entry.Cdr;
            cursor = path.Cdr;
        }

        return alist;
    }

    /// <summary>
    /// Flattens an alist that carries unexpanded nested overrides into a plain one.
    /// <para>
    /// The number of nested entries is known in advance, so everything up to the last
    /// one is copied and the tail is shared — which is the whole point, because that
    /// shared tail is what an enclosing context's alist is.
    /// </para>
    /// <para>
    /// The first index of a nested entry must be a symbol: the conversion relies on
    /// identity comparison and reserves the non-symbol keys for its own purposes —
    /// <see langword="true"/> and <see langword="false"/> mark a temporary override and
    /// a temporary revert, and a pair marks a nested override. Sub-indexes below the
    /// first may be anything comparable.
    /// </para>
    /// </summary>
    /// <param name="nalist">The alist, possibly carrying nested entries.</param>
    /// <param name="nested">How many nested entries it carries.</param>
    /// <returns>A plain association list.</returns>
    public static object NalistToAlist(object nalist, int nested)
    {
        if (nested == 0)
        {
            return nalist;
        }

        object copied = Nil.Instance;
        object partials = Nil.Instance;

        while (nested > 0 && nalist is Pair listPair)
        {
            object element = listPair.Car;
            nalist = listPair.Cdr;

            if (!(element is Pair entry))
            {
                continue;
            }

            object key = entry.Car;
            if (!(key is Symbol))
            {
                nested--;
            }

            if (key is bool flag)
            {
                if (!flag)
                {
                    // A temporary revert: drop it.
                    continue;
                }

                // A temporary override: the real entry is inside.
                if (!(entry.Cdr is Pair inner))
                {
                    continue;
                }

                element = inner;
                entry = inner;
                key = inner.Car;
            }

            if (key is Pair nestedKey)
            {
                // A nested override: record it against its outermost key.
                object pair = SchemeUtilities.Assq(nestedKey.Car, partials);
                if (pair is Pair existing)
                {
                    existing.Cdr = new Pair(element, existing.Cdr);
                }
                else
                {
                    partials = new Pair(
                        new Pair(nestedKey.Car, new Pair(element, Nil.Instance)),
                        partials);
                }

                continue;
            }

            // A plain override: apply any partials already recorded for it.
            object popped = AssqPop(key, ref partials);
            if (popped is Pair poppedPair)
            {
                object value = entry.Cdr;
                object cursor = poppedPair.Cdr;
                while (cursor is Pair partialPair)
                {
                    if (partialPair.Car is Pair partial && partial.Car is Pair partialKey)
                    {
                        value = NestedPropertyAlist(value, partialKey.Cdr, partial.Cdr);
                    }

                    cursor = partialPair.Cdr;
                }

                copied = new Pair(new Pair(key, value), copied);
            }
            else
            {
                copied = new Pair(element, copied);
            }
        }

        // Work off the remaining partials. They are all unique, so they can be pushed
        // straight onto the result without losing anything.
        while (partials is Pair partialsPair)
        {
            if (partialsPair.Car is Pair pair)
            {
                object key = pair.Car;
                Pair element = SchemeUtilities.Assq(key, nalist);
                object value = element != null ? element.Cdr : Nil.Instance;

                object cursor = pair.Cdr;
                while (cursor is Pair listPair)
                {
                    if (listPair.Car is Pair partial && partial.Car is Pair partialKey)
                    {
                        value = NestedPropertyAlist(value, partialKey.Cdr, partial.Cdr);
                    }

                    cursor = listPair.Cdr;
                }

                copied = new Pair(new Pair(key, value), copied);
            }

            partials = partialsPair.Cdr;
        }

        return FastReverse(copied, nalist);
    }

    private static bool KeysMatch(object candidate, object key)
    {
        // Upstream picks assq, assv or assoc by the key's type. Identity first, because
        // it is what symbols need and what the sharing depends on; equality after, for
        // the numbers and characters a sub-index may be.
        return ReferenceEquals(candidate, key)
               || (!(key is Symbol) && SchemeUtilities.IsEqual(candidate, key));
    }
}
