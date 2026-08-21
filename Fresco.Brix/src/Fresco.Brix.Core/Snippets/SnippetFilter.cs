// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Snippets; //was previously: frescobaldi/snippet/widget.py (updateFilter)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>What the snippet list's search box matched.</summary>
public sealed class SnippetFilterResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="names">The snippets that stay visible.</param>
    /// <param name="exactMatch">The snippet whose <c>name</c> variable is
    /// exactly the search text, or null.</param>
    public SnippetFilterResult(IReadOnlyList<string> names, string exactMatch)
    {
        Names = names;
        ExactMatch = exactMatch;
    }

    /// <summary>Gets the snippets that stay visible.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>Gets the exactly-matched snippet, or null.</summary>
    public string ExactMatch { get; }
}

/// <summary>
/// The snippet list's search box.
/// </summary>
/// <remarks>
/// Three ways of matching, and the order is upstream's: text that is exactly a
/// snippet's <c>name</c> variable SELECTS it; text starting with <c>:</c>
/// filters on a declared variable (and after a space, on that variable's
/// value); anything else matches the start of a name or any part of a title.
/// </remarks>
public static class SnippetFilter
{
    /// <summary>The variable names the search box offers to complete.</summary>
    public static readonly IReadOnlyList<string> VariableNames = new[]
    {
        ":icon", ":indent", ":menu", ":name", ":python", ":selection",
        ":set", ":symbol", ":template", ":template-run",
    };

    /// <summary>Filters the snippet list.</summary>
    /// <param name="library">The library.</param>
    /// <param name="names">The snippets to filter, in display order.</param>
    /// <param name="text">The search text.</param>
    /// <returns>What matched.</returns>
    public static SnippetFilterResult Apply(
        SnippetLibrary library, IReadOnlyList<string> names, string text)
    {
        text ??= string.Empty;
        string lowered = text.ToLowerInvariant();

        if (text.StartsWith(':'))
        {
            string rest = text.Substring(1);
            int space = rest.IndexOf(' ');
            string variable = space < 0 ? rest.Trim() : rest.Substring(0, space);
            string wanted = space < 0 ? null : rest.Substring(space + 1);

            return new SnippetFilterResult(
                names.Where(n =>
                {
                    string value = library.Get(n).Variable(variable);
                    if (wanted == null) { return value.Length > 0; }

                    //With a value asked for, a bare "yes" does not count: the
                    //user is looking for a variable that HOLDS something.
                    return value.Length > 0
                        && !string.Equals(value, "yes", StringComparison.Ordinal)
                        && value.Contains(wanted, StringComparison.Ordinal);
                }).ToList(),
                null);
        }

        string exact = null;
        List<string> visible = new List<string>();
        foreach (var name in names)
        {
            string actionName = library.ActionName(name);
            if (text.Length > 0
                && string.Equals(actionName, text, StringComparison.Ordinal))
            {
                exact = name;
                visible.Add(name);
            }
            else if (actionName.ToLowerInvariant().StartsWith(lowered, StringComparison.Ordinal))
            {
                visible.Add(name);
            }
            else if (library.Title(name).ToLowerInvariant()
                .Contains(lowered, StringComparison.Ordinal))
            {
                visible.Add(name);
            }
        }

        return new SnippetFilterResult(visible, exact);
    }

    /// <summary>
    /// Gets the snippets that go in a menu, grouped by the value of a
    /// variable.
    /// </summary>
    /// <param name="library">The library.</param>
    /// <param name="variable">The variable — <c>menu</c> or
    /// <c>template</c>.</param>
    /// <returns>The groups, in the order the menu shows them.</returns>
    public static IReadOnlyList<(string Group, IReadOnlyList<string> Names)> Grouped(
        SnippetLibrary library, string variable)
    {
        Dictionary<string, List<string>> groups
            = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var name in library.Names().OrderBy(n => n, StringComparer.Ordinal))
        {
            string group = library.Get(name).Variable(variable);
            if (group.Length == 0) { continue; }

            if (!groups.TryGetValue(group, out var list))
            {
                list = new List<string>();
                groups[group] = list;
            }

            list.Add(name);
        }

        //A variable declared with no value sorts first — upstream's
        //`'' if g is True else g`.
        return groups
            .OrderBy(
                g => string.Equals(g.Key, "yes", StringComparison.Ordinal)
                    ? string.Empty
                    : g.Key,
                StringComparer.Ordinal)
            .Select(g => (g.Key, (IReadOnlyList<string>)g.Value))
            .ToList();
    }
}
