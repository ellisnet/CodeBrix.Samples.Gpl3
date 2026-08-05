// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Lily.Shell.Services;

/// <summary>
/// A light scanner the Scheme REPL uses to decide whether input is a
/// complete form or the user is mid-expression (so the REPL shows a
/// continuation prompt instead of evaluating). Understands strings, line
/// comments, and #\ character literals; block comments are not handled.
/// </summary>
internal static class SchemeSourceScanner
{
    /// <summary>True when the source has more opening than closing parens/brackets.</summary>
    public static bool HasOpenForms(string source)
    {
        if (string.IsNullOrEmpty(source)) { return false; }

        var depth = 0;
        var inString = false;
        var inComment = false;

        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];

            if (inComment)
            {
                if (c == '\n') { inComment = false; }
                continue;
            }

            if (inString)
            {
                if (c == '\\') { i++; }
                else if (c == '"') { inString = false; }
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;

                case ';':
                    inComment = true;
                    break;

                case '#':
                    //Character literal: #\( and friends must not count as parens
                    if (i + 1 < source.Length && source[i + 1] == '\\') { i += 2; }
                    break;

                case '(':
                case '[':
                    depth++;
                    break;

                case ')':
                case ']':
                    depth--;
                    break;
            }
        }

        return depth > 0;
    }
}
