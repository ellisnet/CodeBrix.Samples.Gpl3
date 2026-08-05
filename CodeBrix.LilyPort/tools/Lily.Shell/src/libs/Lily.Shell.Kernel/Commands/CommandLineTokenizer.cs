// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.Text;

namespace Lily.Shell.Kernel.Commands;

/// <summary>
/// Splits a command line into tokens. Tokens are separated by whitespace;
/// double quotes group text (including whitespace) into one token, and inside
/// quotes a backslash escapes a quote or another backslash. Outside quotes a
/// backslash is a literal character, so Windows-style paths pass through
/// unmangled. An unterminated quote runs to the end of the line.
/// </summary>
public static class CommandLineTokenizer
{
    /// <summary>Tokenizes the line. An empty or all-whitespace line yields no tokens.</summary>
    public static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(line)) { return tokens; }

        var current = new StringBuilder();
        var inToken = false;
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '\\' && i + 1 < line.Length &&
                    (line[i + 1] == '"' || line[i + 1] == '\\'))
                {
                    current.Append(line[i + 1]);
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
                inToken = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }
            }
            else
            {
                current.Append(c);
                inToken = true;
            }
        }

        if (inToken) { tokens.Add(current.ToString()); }

        return tokens;
    }
}
