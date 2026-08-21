// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeBrix.LilyPort.ConvertLy; //was previously: python's `re' module, as convertrules.py uses it;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The slice of python's <c>re</c> module that LilyPond's conversion rules are written
/// against, over <see cref="Regex"/>.
/// <para>
/// The rules ARE their regular expressions — 282 of the 326 are one or more
/// <c>re.sub</c> calls — so the port carries every pattern and every replacement
/// VERBATIM, exactly the text upstream wrote, and translates the handful of places
/// where python's spelling and .NET's differ. Rewriting 1,500 patterns by hand into
/// .NET's dialect would be the single most error-prone thing this port could do, and
/// byte-parity with <c>convert-ly</c> is the bar.
/// </para>
/// </summary>
public static class PythonRegex
{
    private static readonly Dictionary<(string Pattern, RegexOptions Options), Regex> Cache
        = new Dictionary<(string, RegexOptions), Regex>();

    private static readonly object CacheGate = new object();

    /// <summary>
    /// How long one match may run before it is abandoned.
    /// </summary>
    /// <remarks>
    /// ⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14). Some of these patterns
    /// backtrack catastrophically on real input — the nested <c>paren_matcher(25)</c>
    /// alternations in rule 2.15.18 are the worst — and python's <c>re</c> has no
    /// timeout at all, so <c>convert-ly</c> simply NEVER RETURNS. MEASURED: LilyPond's
    /// own <c>input/regression/markup-finger-figuredbass-fontsize.ly</c> hangs upstream
    /// indefinitely. A hang is not an answer, and this engine runs inside an editor
    /// where it would take the application with it, so a match that cannot finish
    /// throws <see cref="RegexMatchTimeoutException"/> and the conversion stops at the
    /// last rule that succeeded — which is exactly what upstream does for a rule that
    /// gives up deliberately.
    /// </remarks>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(10);

    /// <summary>python's <c>re.M</c>.</summary>
    public const RegexOptions Multiline = RegexOptions.Multiline;

    /// <summary>python's <c>re.S</c> / <c>re.DOTALL</c>.</summary>
    public const RegexOptions DotAll = RegexOptions.Singleline;

    /// <summary>python's <c>re.I</c>.</summary>
    public const RegexOptions IgnoreCase = RegexOptions.IgnoreCase;

    /// <summary>python's <c>re.X</c> / <c>re.VERBOSE</c>.</summary>
    public const RegexOptions Verbose = RegexOptions.IgnorePatternWhitespace;

    /// <summary>Compiles a pattern, caching it as python's module-level cache does.</summary>
    /// <param name="pattern">The pattern, in python's spelling.</param>
    /// <param name="options">The flags.</param>
    /// <returns>The compiled expression.</returns>
    public static Regex Compile(string pattern, RegexOptions options = RegexOptions.None)
    {
        lock (CacheGate)
        {
            if (Cache.TryGetValue((pattern, options), out Regex cached))
            {
                return cached;
            }

            Regex compiled = new Regex(TranslatePattern(pattern), options, MatchTimeout);
            Cache[(pattern, options)] = compiled;
            return compiled;
        }
    }

    /// <summary>python's <c>re.sub</c>: replaces every non-overlapping match.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="replacement">The replacement, in python's spelling.</param>
    /// <param name="text">The text to search.</param>
    /// <param name="options">The flags.</param>
    /// <returns>The result.</returns>
    public static string Sub(
        string pattern, string replacement, string text,
        RegexOptions options = RegexOptions.None)
        => Compile(pattern, options)
            .Replace(text ?? string.Empty, TranslateReplacement(replacement));

    /// <summary>
    /// python's <c>re.sub</c> with a FUNCTION for the replacement — the rules use it
    /// wherever a match has to be taken apart and put back together.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="replacement">The function called for each match.</param>
    /// <param name="text">The text to search.</param>
    /// <param name="options">The flags.</param>
    /// <returns>The result.</returns>
    public static string Sub(
        string pattern, MatchEvaluator replacement, string text,
        RegexOptions options = RegexOptions.None)
        => Compile(pattern, options).Replace(text ?? string.Empty, replacement);

    /// <summary>
    /// python's <c>re.sub</c> with a replacement function that also needs the SUBJECT.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="replacement">The function, given the match and the text searched.</param>
    /// <param name="text">The text to search.</param>
    /// <param name="options">The flags.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// Several rules reach past their match into the text around it — python's
    /// <c>m.string[m.start(0):m.start(1)]</c> — and .NET's <see cref="Match"/> does not
    /// carry the string it was made against. Passing it explicitly keeps that
    /// dependency visible instead of hiding it in thread state.
    /// </remarks>
    public static string Sub(
        string pattern, System.Func<Match, string, string> replacement, string text,
        RegexOptions options = RegexOptions.None)
    {
        string subject = text ?? string.Empty;
        return Compile(pattern, options).Replace(subject, m => replacement(m, subject));
    }

    /// <summary>
    /// python's <c>re.sub</c> with a <c>count</c>: replaces at most that many matches.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="replacement">The replacement, in python's spelling.</param>
    /// <param name="text">The text to search.</param>
    /// <param name="count">The most replacements to make.</param>
    /// <param name="options">The flags.</param>
    /// <returns>The result.</returns>
    public static string Sub(
        string pattern, string replacement, string text, int count,
        RegexOptions options = RegexOptions.None)
        => Compile(pattern, options)
            .Replace(text ?? string.Empty, TranslateReplacement(replacement), count);

    /// <summary>
    /// python's <c>Match.lastindex</c>: the number of the last group that took part in
    /// the match, or 0 when none did.
    /// </summary>
    /// <param name="match">The match.</param>
    /// <returns>The group number.</returns>
    /// <remarks>
    /// Used to tell which arm of a long alternation fired. python answers
    /// <see langword="None"/> for no group and this answers 0, which is the same test
    /// in both languages (<c>if m.lastindex</c>).
    /// </remarks>
    public static int LastIndex(Match match)
    {
        int last = 0;
        for (int i = 1; i < match.Groups.Count; i++)
        {
            if (match.Groups[i].Success) { last = i; }
        }

        return last;
    }

    /// <summary>python's <c>re.search</c>: the first match anywhere, or a failed one.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="text">The text to search.</param>
    /// <param name="options">The flags.</param>
    /// <returns>The match.</returns>
    public static Match Search(
        string pattern, string text, RegexOptions options = RegexOptions.None)
        => Compile(pattern, options).Match(text ?? string.Empty);

    /// <summary>
    /// python's <c>re.match</c>: a match ANCHORED at the start of the text — which is
    /// not the same as <c>re.search</c> with <c>\A</c> only in that it is the default.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="text">The text to search.</param>
    /// <param name="options">The flags.</param>
    /// <returns>The match.</returns>
    public static Match MatchAt(
        string pattern, string text, RegexOptions options = RegexOptions.None)
    {
        Match match = Compile(pattern, options).Match(text ?? string.Empty);
        return match.Success && match.Index == 0 ? match : Match.Empty;
    }

    /// <summary>
    /// python's <c>re.findall</c>: every match's text, or — when the pattern has
    /// exactly one group — every match's first group.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="text">The text to search.</param>
    /// <param name="options">The flags.</param>
    /// <returns>The matches.</returns>
    public static List<string> FindAll(
        string pattern, string text, RegexOptions options = RegexOptions.None)
    {
        Regex expression = Compile(pattern, options);
        List<string> found = new List<string>();
        foreach (Match match in expression.Matches(text ?? string.Empty))
        {
            found.Add(
                expression.GetGroupNumbers().Length == 2
                    ? match.Groups[1].Value
                    : match.Value);
        }

        return found;
    }

    /// <summary>
    /// python's <c>re.escape</c>, for the one thing it is used for here: quoting text
    /// that is going into a pattern.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The quoted text.</returns>
    public static string Escape(string text) => Regex.Escape(text ?? string.Empty);

    /// <summary>
    /// Rewrites the three pieces of pattern syntax python spells differently from .NET.
    /// </summary>
    /// <param name="pattern">The pattern, in python's spelling.</param>
    /// <returns>The pattern in .NET's spelling.</returns>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>(?P&lt;name&gt;…)</c> is python's named group; .NET writes
    /// <c>(?&lt;name&gt;…)</c>.</item>
    /// <item><c>(?P=name)</c> is python's named backreference; .NET writes
    /// <c>\k&lt;name&gt;</c>.</item>
    /// <item><c>\Z</c> is python's absolute end of string; .NET spells that
    /// <c>\z</c> and gives <c>\Z</c> the other meaning (end, or before a final
    /// newline), so leaving it alone would silently change what matches.</item>
    /// </list>
    /// Nothing else is touched. The scan tracks escapes and character classes, because
    /// none of these rewrites may fire inside <c>[…]</c> or after a backslash.
    /// </remarks>
    internal static string TranslatePattern(string pattern)
    {
        if (pattern == null)
        {
            return string.Empty;
        }

        StringBuilder result = new StringBuilder(pattern.Length);
        bool inClass = false;
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            if (c == '\\' && i + 1 < pattern.Length)
            {
                // \Z means the absolute end in python and something else in .NET.
                result.Append(c);
                result.Append(pattern[i + 1] == 'Z' && !inClass ? 'z' : pattern[i + 1]);
                i++;
                continue;
            }

            if (inClass)
            {
                inClass = c != ']';
                result.Append(c);
                continue;
            }

            if (c == '[')
            {
                inClass = true;
                result.Append(c);
                continue;
            }

            if (c == '(' && i + 3 < pattern.Length
                && pattern[i + 1] == '?' && pattern[i + 2] == 'P')
            {
                if (pattern[i + 3] == '<')
                {
                    // (?P<name> -> (?<name>
                    result.Append("(?<");
                    i += 3;
                    continue;
                }

                if (pattern[i + 3] == '=')
                {
                    // (?P=name) -> \k<name>
                    int close = pattern.IndexOf(')', i + 4);
                    if (close > 0)
                    {
                        result.Append("\\k<")
                            .Append(pattern, i + 4, close - (i + 4))
                            .Append('>');
                        i = close;
                        continue;
                    }
                }
            }

            result.Append(c);
        }

        return result.ToString();
    }

    /// <summary>
    /// Rewrites a python replacement TEMPLATE into .NET's.
    /// </summary>
    /// <param name="replacement">The replacement, in python's spelling.</param>
    /// <returns>The replacement in .NET's spelling.</returns>
    /// <remarks>
    /// python writes a group reference as <c>\1</c> or <c>\g&lt;1&gt;</c> or
    /// <c>\g&lt;name&gt;</c> and treats <c>$</c> as an ordinary character; .NET writes
    /// <c>$1</c> / <c>${name}</c> and gives <c>$</c> its own meaning. python also
    /// processes the ordinary string escapes (<c>\n</c>, <c>\t</c>, <c>\\</c>) in a
    /// replacement, which .NET does not. ⚠ <c>\g&lt;1&gt;</c> exists precisely for the
    /// case <c>\1</c> cannot express — a group reference with a DIGIT right after it —
    /// so the braced form is what both of them become.
    /// </remarks>
    internal static string TranslateReplacement(string replacement)
    {
        if (replacement == null)
        {
            return string.Empty;
        }

        StringBuilder result = new StringBuilder(replacement.Length);
        for (int i = 0; i < replacement.Length; i++)
        {
            char c = replacement[i];

            if (c == '$')
            {
                result.Append("$$");
                continue;
            }

            if (c != '\\' || i + 1 >= replacement.Length)
            {
                result.Append(c);
                continue;
            }

            char next = replacement[++i];
            if (next >= '0' && next <= '9')
            {
                int start = i;
                while (i + 1 < replacement.Length
                    && replacement[i + 1] >= '0' && replacement[i + 1] <= '9'
                    && (i - start) < 1)
                {
                    i++;
                }

                result.Append("${")
                    .Append(replacement, start, i - start + 1)
                    .Append('}');
                continue;
            }

            if (next == 'g' && i + 1 < replacement.Length && replacement[i + 1] == '<')
            {
                int close = replacement.IndexOf('>', i + 2);
                if (close > 0)
                {
                    result.Append("${")
                        .Append(replacement, i + 2, close - (i + 2))
                        .Append('}');
                    i = close;
                    continue;
                }
            }

            switch (next)
            {
                case 'n': result.Append('\n'); break;
                case 't': result.Append('\t'); break;
                case 'r': result.Append('\r'); break;
                case 'f': result.Append('\f'); break;
                case 'v': result.Append('\v'); break;
                case 'a': result.Append('\a'); break;
                case 'b': result.Append('\b'); break;
                case '\\': result.Append('\\'); break;
                default:
                    // python raises on an unknown escape in a replacement; the rules
                    // contain none, and reproducing the raise would turn a data problem
                    // into a crash in a tool whose job is to salvage old documents.
                    result.Append('\\').Append(next);
                    break;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// python's <c>Match.expand</c>: fills a replacement TEMPLATE from a match that has
    /// already been made.
    /// </summary>
    /// <param name="match">The match.</param>
    /// <param name="template">The template, in python's spelling.</param>
    /// <returns>The expanded text.</returns>
    public static string Expand(Match match, string template)
        => match.Result(TranslateReplacement(template));

    /// <summary>python's <c>int()</c> over the text a group captured.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The number.</returns>
    /// <remarks>
    /// Invariant culture, and a leading <c>+</c> accepted, because the patterns that
    /// feed this allow one (<c>#\+?([0-9-]+)</c>). A text that is not a number at all
    /// cannot reach here: every caller is handed a group matched by a digit class.
    /// </remarks>
    public static int ToInt(string text)
        => int.Parse(
            (text ?? "0").TrimStart('+'), NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture);

    /// <summary>
    /// python's <c>%</c> string formatting, for the <c>%s</c> and <c>%d</c> the rules'
    /// messages use.
    /// </summary>
    /// <param name="format">The format string.</param>
    /// <param name="arguments">The values.</param>
    /// <returns>The formatted text.</returns>
    public static string Format(string format, params object[] arguments)
    {
        if (format == null)
        {
            return string.Empty;
        }

        StringBuilder result = new StringBuilder(format.Length);
        int next = 0;
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '%' || i + 1 >= format.Length)
            {
                result.Append(format[i]);
                continue;
            }

            char kind = format[++i];
            if (kind == '%')
            {
                result.Append('%');
                continue;
            }

            object value = arguments != null && next < arguments.Length
                ? arguments[next++]
                : null;
            result.Append(
                value is IFormattable formattable
                    ? formattable.ToString(null, CultureInfo.InvariantCulture)
                    : value?.ToString() ?? string.Empty);
        }

        return result.ToString();
    }
}
