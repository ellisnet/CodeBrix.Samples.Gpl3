// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Snippets; //was previously: frescobaldi/snippet/snippets.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One snippet as it ships with the application.</summary>
public sealed class BuiltinSnippet
{
    /// <summary>Creates a built-in snippet.</summary>
    /// <param name="name">Its stable name.</param>
    /// <param name="title">Its English title — the verbatim upstream msgid.</param>
    /// <param name="text">Its template text.</param>
    public BuiltinSnippet(string name, string title, string text)
    {
        Name = name;
        Title = title;
        Text = text;
    }

    /// <summary>Gets the stable name.</summary>
    public string Name { get; }

    /// <summary>Gets the English title.</summary>
    public string Title { get; }

    /// <summary>Gets the template text.</summary>
    public string Text { get; }
}

/// <summary>The snippets that ship with the application.</summary>
public static partial class BuiltinSnippets
{
    /// <summary>Gets the built-in snippets, by name.</summary>
    public static IReadOnlyDictionary<string, BuiltinSnippet> ByName
        => _byName ??= Data.ToDictionary(s => s.Name, StringComparer.Ordinal);

    /// <summary>Gets the built-in snippets.</summary>
    public static IReadOnlyList<BuiltinSnippet> All => Data;

    private static IReadOnlyDictionary<string, BuiltinSnippet> _byName;
}

/// <summary>
/// A snippet's text split from the variables declared above it.
/// </summary>
/// <remarks>
/// A snippet may begin with lines of the form <c>-*- name: value; name2;</c>.
/// Those lines are the snippet's VARIABLES and are not part of its text; a
/// name with no value means <c>yes</c>.
/// </remarks>
public sealed class SnippetText
{
    /// <summary>Creates a parsed snippet.</summary>
    /// <param name="text">The template text.</param>
    /// <param name="variables">The declared variables.</param>
    public SnippetText(string text, IReadOnlyDictionary<string, string> variables)
    {
        Text = text ?? string.Empty;
        Variables = variables
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Gets the template text, without the variable lines.</summary>
    public string Text { get; }

    /// <summary>Gets the declared variables.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; }

    /// <summary>Gets a variable's value, or the empty string.</summary>
    /// <param name="name">The variable.</param>
    /// <returns>The value.</returns>
    public string Variable(string name)
        => Variables.TryGetValue(name, out string value) ? value : string.Empty;

    /// <summary>Answers whether a variable's value contains a word.</summary>
    /// <param name="name">The variable.</param>
    /// <param name="word">The word.</param>
    /// <returns>Whether it does.</returns>
    public bool VariableHas(string name, string word)
        => Variable(name).Contains(word, StringComparison.Ordinal);
}

/// <summary>One piece of a snippet as it is expanded.</summary>
public readonly struct SnippetPart
{
    /// <summary>Creates a piece.</summary>
    /// <param name="text">The literal text before the expansion.</param>
    /// <param name="expansion">The expansion name, or the empty string.</param>
    public SnippetPart(string text, string expansion)
    {
        Text = text ?? string.Empty;
        Expansion = expansion ?? string.Empty;
    }

    /// <summary>Gets the literal text.</summary>
    public string Text { get; }

    /// <summary>Gets the expansion name, or the empty string.</summary>
    public string Expansion { get; }
}

/// <summary>
/// Reading a snippet: its variables, its title, and the expansions in its
/// text.
/// </summary>
public static class SnippetParser
{
    /// <summary>Matches a variable declaration in a <c>-*- </c> line.</summary>
    private static readonly Regex VariableExpression = new Regex(
        @"\s*?([a-z]+(?:-[a-z]+)*)(?::[ \t]*(.*?))?;", RegexOptions.Compiled);

    /// <summary>
    /// Matches <c>$$</c>, <c>$NAME</c> and <c>${text}</c>, the last of which
    /// may hold an escaped right brace.
    /// </summary>
    private static readonly Regex ExpansionExpression = new Regex(
        @"\$(?:\{(?<braced>(?:\\\}|[^\}])*)\}|(?<plain>\$|[A-Z]+(?:_[A-Z]+)*))",
        RegexOptions.Compiled);

    /// <summary>Splits a snippet's text from its variables.</summary>
    /// <param name="text">The snippet text.</param>
    /// <returns>The parsed snippet.</returns>
    public static SnippetText Parse(string text)
    {
        string[] lines = (text ?? string.Empty).Split('\n');
        int start = 0;
        while (start < lines.Length
            && lines[start].StartsWith("-*- ", StringComparison.Ordinal))
        {
            start++;
        }

        Dictionary<string, string> variables
            = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < start; i++)
        {
            foreach (Match match in VariableExpression.Matches(lines[i]))
            {
                //A name with no value means "yes" — upstream's groups(True).
                variables[match.Groups[1].Value] = match.Groups[2].Success
                    ? match.Groups[2].Value
                    : "yes";
            }
        }

        return new SnippetText(string.Join("\n", lines.Skip(start)), variables);
    }

    /// <summary>
    /// Splits a snippet's text into literal pieces and expansions.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The pieces, in order.</returns>
    public static IEnumerable<SnippetPart> Expand(string text)
    {
        text ??= string.Empty;
        int position = 0;
        foreach (Match match in ExpansionExpression.Matches(text))
        {
            string expansion = match.Groups["braced"].Success
                ? match.Groups["braced"].Value.Replace("\\}", "}")
                : match.Groups["plain"].Value;
            yield return new SnippetPart(
                text.Substring(position, match.Index - position), expansion);
            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            yield return new SnippetPart(text.Substring(position), string.Empty);
        }
    }

    /// <summary>
    /// Abridges a snippet's text into something usable as a title: its first
    /// and last non-blank lines, with the expansions replaced by an ellipsis.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The title.</returns>
    public static string MakeTitle(string text)
    {
        string[] lines = ExpansionExpression
            .Replace(text ?? string.Empty, " ... ")
            .Split('\n');
        if (lines.Length == 0) { return string.Empty; }

        int start = 0;
        int end = lines.Length - 1;
        while (start < end && string.IsNullOrWhiteSpace(lines[start])) { start++; }

        while (end > start && string.IsNullOrWhiteSpace(lines[end])) { end--; }

        return end == start ? lines[start] : lines[start] + " ... " + lines[end];
    }
}
