// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// Compares a semantic value that may be a SCHEME STRING against the text it should hold.
/// <para>
/// The lexer produces <see cref="MutableString"/> for every value upstream builds with
/// <c>to_scm (str)</c>, because that is what the Scheme layer's <c>string?</c> — and so
/// <c>markup?</c> and everything above it — recognises. <see cref="MutableString"/> is a
/// MUTABLE type and deliberately compares by identity, so an assertion about what a token
/// SAYS goes through here rather than through equality.
/// </para>
/// </summary>
internal static class SchemeTextAssertions
{
    /// <summary>Presents a value as its text when it is a Scheme string, unchanged otherwise.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The text, or the value itself.</returns>
    internal static object AsText(this object value)
        => value is MutableString text ? text.ToString() : value;

    /// <summary>Presents every element of a list the same way.</summary>
    /// <param name="values">The values.</param>
    /// <returns>The presented values.</returns>
    internal static IReadOnlyList<object> AsText(this IReadOnlyList<object> values)
    {
        if (values == null)
        {
            return null;
        }

        List<object> presented = new List<object>(values.Count);
        foreach (object value in values)
        {
            presented.Add(value.AsText());
        }

        return presented;
    }

    /// <summary>Presents every element of a Scheme list the same way.</summary>
    /// <param name="list">The list, as pairs.</param>
    /// <returns>The presented elements.</returns>
    internal static IReadOnlyList<object> ListAsText(object list)
        => ((IReadOnlyList<object>)Pair.ToList(list)).AsText();
}
