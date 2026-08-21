// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeBrix.LilyPort.ConvertLy; //was previously: convertrules.py's module-level helpers;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// What the conversion rules stand on: the diagnostic sink, and the handful of helper
/// functions <c>convertrules.py</c> defines beside its rules.
/// </summary>
/// <remarks>
/// The rules themselves are in two files. <c>ConvertRules.g.cs</c> is generated from
/// upstream's own source by <c>tools/convertrules-port/</c> and holds the 267 whose
/// bodies translate mechanically; <c>ConvertRules.Manual.cs</c> holds the 59 that do
/// not. Both are parts of this one class, and the generated TABLE names all 326, so a
/// rule that is missing from either file is a compile error.
/// </remarks>
internal static partial class ConvertRules
{
    /// <summary>
    /// Where the running conversion's messages go. Upstream's rules call
    /// <c>stderr_write</c> and a command-line tool prints them; here a rule's remarks
    /// belong to the DOCUMENT being converted, so an editor can show them beside it.
    /// </summary>
    [ThreadStatic]
    private static List<string> _messages;

    /// <summary>Begins collecting messages for one conversion.</summary>
    /// <returns>The list the rules will write into.</returns>
    internal static List<string> BeginCollecting()
    {
        _messages = new List<string>();
        return _messages;
    }

    /// <summary>Stops collecting.</summary>
    internal static void EndCollecting() => _messages = null;

    /// <summary>convertrules.py's <c>stderr_write</c>.</summary>
    /// <param name="message">The message.</param>
    /// <returns>Nothing; shaped as a value so generated code can call it in expression
    /// position the way python does.</returns>
    internal static object StdErr(string message)
    {
        _messages?.Add(message);
        return null;
    }

    /// <summary>convertrules.py's <c>warning</c>.</summary>
    /// <param name="message">The message.</param>
    /// <returns>Nothing.</returns>
    internal static object Warning(string message) => StdErr("warning: " + message);

    /// <summary>
    /// convertrules.py's <c>paren_matcher</c>: "poor man's matched paren scanning,
    /// gives up after n+1 levels. Matches any string with balanced parens inside; add
    /// the outer parens yourself if needed. Nongreedy." — upstream's own comment.
    /// </summary>
    /// <param name="n">How deep the nesting may go.</param>
    /// <returns>The pattern.</returns>
    /// <remarks>
    /// Built exactly as upstream builds it, by repeating two fragments <c>n</c> times
    /// around a third. .NET has balancing groups and python does not, but a recursive
    /// expression would match text this one gives up on — and "gives up after n+1
    /// levels" is part of what the rules were written against.
    /// </remarks>
    internal static string ParenMatcher(int n)
        => Repeat("[^()]*?(?:\\(", n) + "[^()]*?" + Repeat("\\)[^()]*?)*?", n);

    /// <summary>
    /// lilylib's <c>brace_matcher</c>: the same construction over <c>{}</c>.
    /// </summary>
    /// <param name="n">How deep the nesting may go.</param>
    /// <returns>The pattern.</returns>
    internal static string BraceMatcher(int n)
        => Repeat("[^{}]*?(?:{", n) + "[^{}]*?" + Repeat("}[^{}]*?)*?", n);

    /// <summary>python's <c>text * n</c>.</summary>
    /// <param name="text">The text to repeat.</param>
    /// <param name="n">How many times.</param>
    /// <returns>The repeated text.</returns>
    private static string Repeat(string text, int n)
        => n <= 0 ? string.Empty : string.Concat(System.Linq.Enumerable.Repeat(text, n));

    /// <summary>
    /// convertrules.py's <c>regularize_id</c>: makes an identifier out of ASCII letters
    /// only — a digit becomes the letter that far after <c>A</c>, anything else that is
    /// not a letter becomes <c>x</c>, an underscore is dropped and CAPITALISES the
    /// letter after it.
    /// </summary>
    /// <param name="identifier">The identifier.</param>
    /// <returns>The regularized identifier.</returns>
    /// <remarks>
    /// ⚠ ASCII, deliberately: upstream tests against <c>string.ascii_letters</c> and
    /// <c>string.digits</c>, so an accented letter becomes <c>x</c> here exactly as it
    /// does there. <c>char.IsLetter</c> would quietly keep it.
    /// </remarks>
    internal static string RegularizeId(string identifier)
    {
        StringBuilder result = new StringBuilder();
        char last = '\0';
        foreach (char original in identifier ?? string.Empty)
        {
            char c = original;
            if (c == '_')
            {
                last = c;
                continue;
            }

            if (c >= '0' && c <= '9')
            {
                c = (char)(c - '0' + 'A');
            }
            else if (!IsAsciiLetter(c))
            {
                c = 'x';
            }
            else if (c >= 'a' && c <= 'z' && last == '_')
            {
                c = char.ToUpperInvariant(c);
            }

            result.Append(c);
            last = c;
        }

        return result.ToString();
    }

    /// <summary>python's <c>string.ascii_letters</c> membership.</summary>
    /// <param name="c">The character.</param>
    /// <returns>Whether it is an ASCII letter.</returns>
    private static bool IsAsciiLetter(char c)
        => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
}
