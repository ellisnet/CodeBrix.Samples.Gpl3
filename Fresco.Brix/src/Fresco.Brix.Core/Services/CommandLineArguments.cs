// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Fresco.Brix.Services; //was previously: frescobaldi/__main__.py (the argparse block)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// What the process was started with: the files to open, and the four options
/// that change how they are opened.
/// </summary>
/// <remarks>
/// <para>
/// //was previously: the whole of upstream's <c>argparse</c> block. Only the
/// options FD5's protocol carries are ported here, because they are the ones
/// <c>remote/api.py</c>'s <c>command_line()</c> writes down the socket and the
/// ones upstream's own local path honours as well:
/// <c>-e</c>/<c>--encoding</c>, <c>-l</c>/<c>--line</c>, <c>-c</c>/<c>--column</c>
/// and <c>-n</c>/<c>--new</c>. Upstream's other five —
/// <c>--version</c>, <c>--version-debug</c>, <c>--start</c>,
/// <c>--list-sessions</c> and <c>--python-ly</c> — are outside this wave (and
/// <c>--python-ly</c> has nothing to point at: there is no python-ly here).
/// </para>
/// <para>
/// Anything unrecognised is treated as a FILE, which is what keeps a path
/// beginning with a dash usable and what a desktop file manager passes.
/// </para>
/// </remarks>
public sealed class CommandLineArguments
{
    private readonly List<string> _files = new List<string>();

    /// <summary>Gets the files named on the command line, in order.</summary>
    public IReadOnlyList<string> Files => _files;

    /// <summary>Gets the encoding to read the files in, or null.</summary>
    public string Encoding { get; private set; }

    /// <summary>Gets the line to go to, counted from 1, or null.</summary>
    public int? Line { get; private set; }

    /// <summary>Gets the column to go to, counted from 1, or null.</summary>
    public int? Column { get; private set; }

    /// <summary>Gets whether a new instance was demanded (<c>-n</c>).</summary>
    public bool New { get; private set; }

    /// <summary>Reads a command line.</summary>
    /// <param name="arguments">The arguments, without the program name.</param>
    /// <returns>What they said.</returns>
    public static CommandLineArguments Parse(IReadOnlyList<string> arguments)
    {
        CommandLineArguments parsed = new CommandLineArguments();
        if (arguments == null) { return parsed; }

        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i] ?? string.Empty;
            switch (argument)
            {
                case "-n":
                case "--new":
                    parsed.New = true;
                    continue;
                case "-e":
                case "--encoding":
                    parsed.Encoding = Next(arguments, ref i);
                    continue;
                case "-l":
                case "--line":
                    parsed.Line = ParseNumber(Next(arguments, ref i));
                    continue;
                case "-c":
                case "--column":
                    parsed.Column = ParseNumber(Next(arguments, ref i));
                    continue;
            }

            //`--option=value' is argparse's other spelling of the same thing.
            if (TrySplit(argument, "--encoding", out string encoding))
            {
                parsed.Encoding = encoding;
            }
            else if (TrySplit(argument, "--line", out string line))
            {
                parsed.Line = ParseNumber(line);
            }
            else if (TrySplit(argument, "--column", out string column))
            {
                parsed.Column = ParseNumber(column);
            }
            else if (!string.IsNullOrEmpty(argument))
            {
                parsed._files.Add(argument);
            }
        }

        return parsed;
    }

    private static string Next(IReadOnlyList<string> arguments, ref int index)
        => index + 1 < arguments.Count ? arguments[++index] : null;

    private static int? ParseNumber(string text)
        => int.TryParse(
            text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;

    private static bool TrySplit(string argument, string name, out string value)
    {
        string prefix = name + "=";
        if (argument.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = argument.Substring(prefix.Length);
            return true;
        }

        value = null;
        return false;
    }
}
