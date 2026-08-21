// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Documents; //was previously: frescobaldi/variables.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The per-document settings a user writes into a comment near the top or
/// bottom of a LilyPond file, in the Emacs-style form
/// <c>% -*- indent-width: 2; coding: utf-8; -*-</c>.
/// <para>
/// Only the first and last few lines are scanned, so the cost stays flat no
/// matter how long the document is.
/// </para>
/// </summary>
public static class DocumentVariables
{
    /// <summary>How many lines from the top and the bottom are scanned.</summary>
    public const int ScannedLines = 5; //was previously: _LINES

    private static readonly Regex VariableRegex = new Regex(
        @"\G\s*?([a-z]+(?:-[a-z]+)*):[ \t]*(.*?);", RegexOptions.Compiled);

    private static readonly Regex MarkerRegex = new Regex(
        @"(\S*)\s*-\*-", RegexOptions.Compiled);

    /// <summary>Reads every variable a document declares.</summary>
    /// <param name="text">The document text.</param>
    /// <returns>The variables, by name.</returns>
    public static IReadOnlyDictionary<string, string> Read(string text)
    {
        Dictionary<string, string> variables
            = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] lines = SplitLines(text);
        int start = 0;
        if (lines.Length > 2 * ScannedLines)
        {
            foreach (var found in Positions(lines.Take(ScannedLines)))
            {
                variables[found.Name] = found.Value;
            }

            start = lines.Length - ScannedLines;
        }

        foreach (var found in Positions(lines.Skip(start)))
        {
            variables[found.Name] = found.Value;
        }

        return variables;
    }

    /// <summary>Reads one variable.</summary>
    /// <param name="text">The document text.</param>
    /// <param name="name">The variable name.</param>
    /// <param name="defaultValue">The value when the document does not set it.</param>
    /// <returns>The value.</returns>
    public static string Get(string text, string name, string defaultValue = null)
        => Read(text).TryGetValue(name, out var value) ? value : defaultValue;

    /// <summary>Reads one variable as a flag.</summary>
    /// <param name="text">The document text.</param>
    /// <param name="name">The variable name.</param>
    /// <param name="defaultValue">The value when unset or unreadable.</param>
    /// <returns>The value.</returns>
    public static bool GetBool(string text, string name, bool defaultValue)
    {
        string value = Get(text, name);
        if (value == null) { return defaultValue; }

        return value.ToLowerInvariant() switch
        {
            "true" or "yes" or "on" or "t" or "1" => true,
            "false" or "no" or "off" or "f" or "0" => false,
            _ => defaultValue,
        };
    }

    /// <summary>Reads one variable as a number.</summary>
    /// <param name="text">The document text.</param>
    /// <param name="name">The variable name.</param>
    /// <param name="defaultValue">The value when unset or unreadable.</param>
    /// <returns>The value.</returns>
    public static int GetInt(string text, string name, int defaultValue)
    {
        string value = Get(text, name);
        return value != null
            && int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var number)
            ? number
            : defaultValue;
    }

    /// <summary>
    /// Finds the variables in a run of lines, in the order they appear.
    /// </summary>
    /// <param name="lines">The lines to scan.</param>
    /// <returns>The line number (from 0), name and value of each variable.</returns>
    /// <remarks>
    /// Scanning starts at the first <c>-*-</c> marker; whatever non-space text
    /// preceded it on that line becomes the comment prefix later lines may
    /// repeat. Scanning stops again as soon as a line holds anything that is
    /// not another <c>name: value;</c> pair.
    /// </remarks>
    public static IEnumerable<(int LineNumber, string Name, string Value)> Positions(
        IEnumerable<string> lines)
    {
        string commentStart = string.Empty;
        bool interesting = false;
        int lineNumber = -1;

        foreach (var text in lines)
        {
            lineNumber++;
            int start = 0;
            if (interesting)
            {
                //Skip the comment prefix this document uses, if repeated.
                Match prefix = Regex.Match(
                    text, @"\G\s*" + Regex.Escape(commentStart));
                if (prefix.Success)
                {
                    start = prefix.Index + prefix.Length;
                }
            }
            else
            {
                Match marker = MarkerRegex.Match(text);
                if (marker.Success)
                {
                    interesting = true;
                    commentStart = marker.Groups[1].Value;
                    start = marker.Index + marker.Length;
                }
            }

            if (!interesting) { continue; }

            while (true)
            {
                Match variable = VariableRegex.Match(text, start);
                if (variable.Success)
                {
                    yield return (lineNumber,
                        variable.Groups[1].Value, variable.Groups[2].Value);
                    start = variable.Index + variable.Length;
                    continue;
                }

                //Anything else on the line ends the run of variables.
                if (start < text.Length && !IsAllWhitespace(text, start))
                {
                    interesting = false;
                }

                break;
            }
        }
    }

    /// <summary>
    /// Splits text into lines the way python's <c>splitlines()</c> does — a
    /// trailing newline does NOT produce a final empty line, which is what
    /// keeps the "how many lines are there" test agreeing with upstream.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The lines.</returns>
    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text)) { return Array.Empty<string>(); }

        string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        if (normalized.EndsWith("\n", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(0, normalized.Length - 1);
        }

        return normalized.Split('\n');
    }

    private static bool IsAllWhitespace(string text, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return false;
            }
        }

        return true;
    }
}
