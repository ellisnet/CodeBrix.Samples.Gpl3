// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Editor; //was previously: frescobaldi/unicode_blocks.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One named range of the Unicode code space.</summary>
public readonly struct UnicodeBlock
{
    /// <summary>Creates a block.</summary>
    /// <param name="start">The first code point.</param>
    /// <param name="end">The last code point.</param>
    /// <param name="name">The block name.</param>
    public UnicodeBlock(int start, int end, string name)
    {
        Start = start;
        End = end;
        Name = name;
    }

    /// <summary>Gets the first code point.</summary>
    public int Start { get; }

    /// <summary>Gets the last code point.</summary>
    public int End { get; }

    /// <summary>Gets the block name.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>
/// The Unicode blocks, which are what the Special Characters panel offers the
/// user to choose between.
/// </summary>
/// <remarks>
/// The table is generated from the Unicode Character Database's
/// <c>Blocks.txt</c>, by way of Frescobaldi's own copy of it; the generator is
/// <c>tools/unicodeblocks/</c>.
/// </remarks>
public static partial class UnicodeBlocks
{
    /// <summary>Gets the blocks, in code-point order.</summary>
    public static IReadOnlyList<UnicodeBlock> Blocks => Data;

    /// <summary>
    /// Gets the blocks that fit in the Basic Multilingual Plane and the planes
    /// the editor can show.
    /// </summary>
    /// <returns>The blocks.</returns>
    /// <remarks>Upstream stops at the first block past <c>sys.maxunicode</c>,
    /// which on a modern build is every block; the same cut is kept so that a
    /// future narrowing has one place to happen.</remarks>
    public static IReadOnlyList<UnicodeBlock> UsableBlocks()
        => Data.TakeWhile(b => b.End <= 0x10FFFF).ToList();

    /// <summary>Finds the block a code point belongs to.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The block, or null when it is in none.</returns>
    public static UnicodeBlock? BlockOf(int codePoint)
    {
        int low = 0;
        int high = Data.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (Data[middle].End < codePoint) { low = middle + 1; } else { high = middle; }
        }

        return low < Data.Count && Data[low].Start <= codePoint
            ? Data[low]
            : (UnicodeBlock?)null;
    }

    /// <summary>Finds a block by name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The block, or null.</returns>
    public static UnicodeBlock? ByName(string name)
    {
        foreach (var block in Data)
        {
            if (string.Equals(block.Name, name, StringComparison.Ordinal))
            {
                return block;
            }
        }

        return null;
    }
}
