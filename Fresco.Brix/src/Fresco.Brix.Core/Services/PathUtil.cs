// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Services; //was previously: frescobaldi/util.py (the file-name half)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The file-name helpers the engrave pipeline needs from upstream's
/// <c>util</c> module: finding the files a run produced, keeping only the ones
/// newer than the source, gathering them by extension, and the temporary area
/// a modified document is engraved from.
/// </summary>
/// <remarks>
/// Upstream's <c>util</c> is a 300-line grab bag; the board ports it
/// selectively, as each wave needs a piece. This is W3's piece.
/// </remarks>
public static class PathUtil
{
    private static readonly object TempGate = new object();
    private static string _tempRoot;

    /// <summary>
    /// Creates a new temporary directory inside a per-process root that is
    /// removed when the application exits.
    /// </summary>
    /// <returns>The directory path.</returns>
    public static string TempDir()
    {
        lock (TempGate)
        {
            if (_tempRoot == null)
            {
                _tempRoot = Path.Combine(
                    Path.GetTempPath(),
                    AppInfo.Name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(_tempRoot);

                //Upstream registers an atexit hook; the CLR equivalent is the
                //process-exit event, and the same "never mind if it fails" rule
                //applies — a leftover temporary directory is not worth a crash.
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    try { Directory.Delete(_tempRoot, recursive: true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                };
            }
        }

        string directory = Path.Combine(
            _tempRoot, Guid.NewGuid().ToString("N").Substring(0, 12));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// Finds the existing files whose names start with one of the base names
    /// and end with the given extension.
    /// </summary>
    /// <param name="baseNames">The base names, without extension.</param>
    /// <param name="extension">
    /// The extension to match, which may itself be a glob (<c>*</c> for any,
    /// <c>.svg*</c> for the compressed variant too).
    /// </param>
    /// <returns>The matching files, in file-name order.</returns>
    /// <remarks>
    /// Both <c>&lt;base&gt;&lt;ext&gt;</c> and <c>&lt;base&gt;-&lt;n&gt;&lt;ext&gt;</c>
    /// are matched, because a multi-page or multi-book run numbers its output.
    /// The base name itself is matched LITERALLY — a document living in a
    /// directory with a <c>[</c> in its name still finds its own output.
    /// </remarks>
    public static IReadOnlyList<string> Files(
        IEnumerable<string> baseNames, string extension = ".*")
    {
        List<string> found = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var baseName in baseNames ?? Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(baseName)) { continue; }

            //macOS stores file names decomposed; upstream globs for both forms
            //and so do we, because either may be what is on disk.
            foreach (var name in Distinct(baseName, baseName.Normalize(NormalizationForm.FormD)))
            {
                string directory = Path.GetDirectoryName(name);
                string stem = Path.GetFileName(name);
                if (string.IsNullOrEmpty(directory)) { directory = "."; }

                if (!Directory.Exists(directory)) { continue; }

                Regex plain = GlobRegex(Escape(stem) + extension);
                Regex numbered = GlobRegex(Escape(stem) + "-*[0-9]" + extension);

                foreach (var path in Directory.EnumerateFiles(directory))
                {
                    string fileName = Path.GetFileName(path);
                    if ((plain.IsMatch(fileName) || numbered.IsMatch(fileName))
                        && seen.Add(path))
                    {
                        found.Add(path);
                    }
                }
            }
        }

        found.Sort(CompareFileNames);
        return found;
    }

    /// <summary>Keeps only the files modified at or after a moment.</summary>
    /// <param name="files">The files.</param>
    /// <param name="time">The moment.</param>
    /// <returns>The files that are that new.</returns>
    public static IReadOnlyList<string> NewerFiles(
        IEnumerable<string> files, DateTime time)
        => (files ?? Array.Empty<string>())
            .Where(f => File.Exists(f) && File.GetLastWriteTimeUtc(f) >= time.ToUniversalTime())
            .ToList();

    /// <summary>
    /// Gathers file names by extension, in the order the groups are given.
    /// </summary>
    /// <param name="names">The file names.</param>
    /// <param name="groups">
    /// One string per group, holding the extensions (no period) it accepts,
    /// separated by spaces. A leading <c>!</c> negates the group.
    /// </param>
    /// <returns>One list per group; a name lands in the FIRST group that
    /// accepts it.</returns>
    public static IReadOnlyList<IReadOnlyList<string>> GroupFiles(
        IEnumerable<string> names, IEnumerable<string> groups)
    {
        List<(List<string> Files, Func<string, bool> Accepts)> all
            = new List<(List<string>, Func<string, bool>)>();

        foreach (var group in groups ?? Array.Empty<string>())
        {
            bool negated = group.StartsWith("!", StringComparison.Ordinal);
            string[] extensions = (negated ? group.Substring(1) : group)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            all.Add((new List<string>(),
                extension => negated
                    ? !extensions.Contains(extension)
                    : extensions.Contains(extension)));
        }

        foreach (var name in names ?? Array.Empty<string>())
        {
            //Invariant-culture lowering: a Turkish locale must not turn a
            //".MIDI" into something the "midi" group misses (standing rule 7).
            string extension = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
            foreach (var (files, accepts) in all)
            {
                if (accepts(extension))
                {
                    files.Add(name);
                    break;
                }
            }
        }

        return all.Select(entry => (IReadOnlyList<string>)entry.Files).ToList();
    }

    /// <summary>
    /// Normalizes a path, keeping the separators forward on every platform.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The normalized path.</returns>
    public static string NormPath(string path)
    {
        if (string.IsNullOrEmpty(path)) { return path; }

        string full = Path.GetFullPath(path);
        return OperatingSystem.IsWindows() ? full.Replace('\\', '/') : full;
    }

    /// <summary>Compares two paths the way the file system does.</summary>
    /// <param name="first">One path.</param>
    /// <param name="second">The other.</param>
    /// <returns>Whether they name the same place.</returns>
    /// <remarks>Case- and separator-insensitive on Windows, exact
    /// elsewhere — upstream's own split.</remarks>
    public static bool EqualPaths(string first, string second)
        => OperatingSystem.IsWindows()
            ? string.Equals(
                (first ?? string.Empty).Replace('\\', '/'),
                (second ?? string.Empty).Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase)
            : string.Equals(first, second, StringComparison.Ordinal);

    /// <summary>
    /// Orders file names the way a person would: the digit runs compare as
    /// numbers, so <c>score-9.svg</c> comes before <c>score-10.svg</c>.
    /// </summary>
    /// <param name="first">One file name.</param>
    /// <param name="second">The other.</param>
    /// <returns>The comparison.</returns>
    public static int CompareFileNames(string first, string second)
    {
        int byStem = CompareNaturally(
            Path.Combine(
                Path.GetDirectoryName(first) ?? string.Empty,
                Path.GetFileNameWithoutExtension(first)),
            Path.Combine(
                Path.GetDirectoryName(second) ?? string.Empty,
                Path.GetFileNameWithoutExtension(second)));
        return byStem != 0
            ? byStem
            : string.CompareOrdinal(Path.GetExtension(first), Path.GetExtension(second));
    }

    /// <summary>Compares two strings with their digit runs read as numbers.</summary>
    /// <param name="first">One string.</param>
    /// <param name="second">The other.</param>
    /// <returns>The comparison.</returns>
    public static int CompareNaturally(string first, string second)
    {
        string[] left = SplitDigits(first);
        string[] right = SplitDigits(second);

        for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            bool leftIsNumber = IsDigits(left[i]);
            bool rightIsNumber = IsDigits(right[i]);

            //Python compares an int with a str by raising; the split alternates
            //text and digits from index 0, so the kinds always line up here and
            //the mixed case cannot arise. Ordering it anyway keeps the compare
            //total, which a sort needs and python's does not have to be.
            if (leftIsNumber != rightIsNumber)
            {
                return leftIsNumber ? -1 : 1;
            }

            int comparison = leftIsNumber
                ? ParseRun(left[i]).CompareTo(ParseRun(right[i]))
                : string.CompareOrdinal(left[i], right[i]);
            if (comparison != 0) { return comparison; }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static IEnumerable<string> Distinct(string first, string second)
    {
        yield return first;
        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            yield return second;
        }
    }

    private static bool IsDigits(string text)
        => text.Length > 0 && text.All(char.IsDigit);

    private static long ParseRun(string text)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out long value)
            ? value
            : long.MaxValue;

    private static string[] SplitDigits(string text)
        => Regex.Split(text ?? string.Empty, "([0-9]+)");

    /// <summary>Escapes the glob metacharacters in a literal name part.</summary>
    private static string Escape(string text)
        => text.Replace("[", "[[]").Replace("?", "[?]").Replace("*", "[*]");

    /// <summary>
    /// Turns a shell glob into a regular expression anchored at both ends.
    /// </summary>
    /// <param name="pattern">The glob.</param>
    /// <returns>The expression.</returns>
    /// <remarks>Only what glob offers: <c>*</c>, <c>?</c> and a
    /// <c>[...]</c> set (a leading <c>!</c> negating it).</remarks>
    internal static Regex GlobRegex(string pattern)
    {
        StringBuilder expression = new StringBuilder("^");
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            switch (c)
            {
                case '*':
                    expression.Append("[^/\\\\]*");
                    break;
                case '?':
                    expression.Append("[^/\\\\]");
                    break;
                case '[':
                    int close = pattern.IndexOf(']', i + 1);
                    if (close < 0)
                    {
                        expression.Append("\\[");
                        break;
                    }

                    string set = pattern.Substring(i + 1, close - i - 1);
                    expression.Append('[');
                    expression.Append(set.StartsWith("!", StringComparison.Ordinal)
                        ? "^" + Regex.Escape(set.Substring(1)).Replace("\\-", "-")
                        : Regex.Escape(set).Replace("\\-", "-"));
                    expression.Append(']');
                    i = close;
                    break;
                default:
                    expression.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        expression.Append('$');
        return new Regex(expression.ToString(),
            OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase : RegexOptions.None);
    }
}
