// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This file is a port of Frescobaldi's hyphenator.py, which is conveyed under
// the GNU Lesser General Public License (Wilbert Berendsen, March 2008,
// http://python-hyphenator.googlecode.com/). The LGPL permits its use here
// under the GPL; the notice travels with it, in THIRD-PARTY-NOTICES.txt.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Editor; //was previously: frescobaldi/hyphenator.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One place a word may be broken, and — for a dictionary that spells the
/// break differently from a plain hyphen — how to spell it.
/// </summary>
/// <remarks>
/// Upstream carries this as a <c>DataInt</c>: an integer with a
/// <c>data</c> attribute holding <c>(change, index, cut)</c> or nothing. The
/// integer is <see cref="Index"/> and the attribute is the other three
/// members, which is the same pair of facts under names that say what they
/// are.
/// </remarks>
public sealed class HyphenPosition
{
    /// <summary>Creates a plain break point.</summary>
    /// <param name="index">Where in the word it falls.</param>
    public HyphenPosition(int index) => Index = index;

    /// <summary>Creates a non-standard break point.</summary>
    /// <param name="index">Where in the word it falls.</param>
    /// <param name="change">How the break is spelled, as
    /// <c>before=after</c>.</param>
    /// <param name="changeIndex">Where to write the change, counting from the
    /// break point.</param>
    /// <param name="cut">How many characters the change replaces.</param>
    public HyphenPosition(int index, string change, int changeIndex, int cut)
    {
        Index = index;
        Change = change;
        ChangeIndex = changeIndex;
        Cut = cut;
    }

    /// <summary>Gets where in the word the break falls.</summary>
    public int Index { get; }

    /// <summary>Gets how the break is spelled, or null for a plain hyphen.</summary>
    /// <remarks>German's <c>ff=f</c> is the classic example: <c>Schiffahrt</c>
    /// breaks as <c>Schiff-fahrt</c>, so three letters replace two.</remarks>
    public string Change { get; }

    /// <summary>Gets where the change goes, counting from the break.</summary>
    public int ChangeIndex { get; }

    /// <summary>Gets how many characters the change replaces.</summary>
    public int Cut { get; }

    /// <summary>Gets whether the break is spelled other than as a hyphen.</summary>
    public bool IsNonstandard => Change != null;

    /// <inheritdoc/>
    public override string ToString() => IsNonstandard
        ? $"{Index} ({Change}, {ChangeIndex}, {Cut})"
        : Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// The hyphenation patterns read out of one <c>hyph_*.dic</c> file.
/// </summary>
/// <remarks>
/// <para>
/// The file is a list of patterns in Liang's TeX form: letters with odd or
/// even numbers between them, where an odd number means a break is allowed
/// there. Matching every pattern against the word and keeping the highest
/// number at each position gives the break points — which is exactly what
/// <see cref="Positions"/> does.
/// </para>
/// <para>
/// A file is read once per process: <see cref="Read"/> keeps what it has read,
/// as upstream's module-level <c>_hdcache</c> does.
/// </para>
/// </remarks>
public sealed class HyphenationPatterns
{
    //Upstream's parse = re.compile(r'(\d?)(\D?)').findall — a pattern is read
    //as a run of (number, letter) pairs, either of which may be missing.
    private static readonly Regex ParsePattern = new Regex(
        @"(\d?)(\D?)", RegexOptions.Compiled);

    //Upstream's _hex_re: ^^hh stands for the character with that hex code.
    private static readonly Regex HexEscape = new Regex(
        @"\^{2}([0-9a-f]{2})", RegexOptions.Compiled);

    private static readonly Dictionary<string, HyphenationPatterns> Cache
        = new Dictionary<string, HyphenationPatterns>(StringComparer.Ordinal);

    private readonly Dictionary<string, (int Offset, PatternValue[] Values)> _patterns
        = new Dictionary<string, (int, PatternValue[])>(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<HyphenPosition>> _positions
        = new Dictionary<string, IReadOnlyList<HyphenPosition>>(StringComparer.Ordinal);
    private readonly int _maxLength;

    private HyphenationPatterns(string text)
    {
        foreach (var raw in ReadLines(text))
        {
            string pattern = raw.Trim();
            if (pattern.Length == 0 || pattern[0] == '%') { continue; }

            pattern = ReplaceHex(pattern);

            //A pattern may carry a non-standard spelling after a slash.
            Alternative alternative = null;
            int slash = pattern.IndexOf('/');
            if (slash >= 0)
            {
                alternative = new Alternative(
                    pattern.Substring(0, slash), pattern.Substring(slash + 1));
                pattern = pattern.Substring(0, slash);
            }

            var tag = new StringBuilder();
            var values = new List<PatternValue>();
            foreach (Match match in ParsePattern.Matches(pattern))
            {
                string digits = match.Groups[1].Value;
                tag.Append(match.Groups[2].Value);
                int number = digits.Length == 0
                    ? 0
                    : digits[0] - '0';
                values.Add(alternative == null
                    ? new PatternValue(number)
                    : alternative.Next(number));
            }

            //A pattern of nothing but zeros says nothing.
            int start = 0;
            int end = values.Count;
            while (start < end && values[start].Value == 0) { start++; }

            if (start == end) { continue; }

            while (values[end - 1].Value == 0) { end--; }

            _patterns[tag.ToString()] = (start, values.GetRange(start, end - start).ToArray());
        }

        _maxLength = _patterns.Count == 0 ? 0 : _patterns.Keys.Max(k => k.Length);
    }

    /// <summary>Reads a dictionary file, or answers one already read.</summary>
    /// <param name="fileName">The <c>hyph_*.dic</c> file.</param>
    /// <param name="useCache">Whether an already-read copy may be answered.</param>
    /// <returns>The patterns.</returns>
    /// <remarks>Upstream's <c>Hyphenator(filename, cache=…)</c>: a false
    /// <c>cache</c> re-reads the file AND replaces what was kept, which is
    /// what happens here too.</remarks>
    public static HyphenationPatterns Read(string fileName, bool useCache = true)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentNullException(nameof(fileName));
        }

        lock (Cache)
        {
            if (useCache && Cache.TryGetValue(fileName, out var kept)) { return kept; }

            HyphenationPatterns read = new HyphenationPatterns(ReadText(fileName));
            Cache[fileName] = read;
            return read;
        }
    }

    /// <summary>Reads patterns straight out of text, for tests.</summary>
    /// <param name="text">The file's text, first line and all.</param>
    /// <returns>The patterns.</returns>
    public static HyphenationPatterns Parse(string text)
        => new HyphenationPatterns(text ?? string.Empty);

    /// <summary>Gets how many patterns were read.</summary>
    public int Count => _patterns.Count;

    /// <summary>
    /// Answers every position in a word where it may be broken.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <returns>The positions, in order.</returns>
    /// <remarks>The Dutch <c>lettergrepen</c> answers 3, 6 and 9. Positions
    /// too near either end are not filtered here — that is the hyphenator's
    /// job, because the margins are its settings.</remarks>
    public IReadOnlyList<HyphenPosition> Positions(string word)
    {
        if (string.IsNullOrEmpty(word)) { return Array.Empty<HyphenPosition>(); }

        word = word.ToLowerInvariant();
        lock (_positions)
        {
            if (_positions.TryGetValue(word, out var kept)) { return kept; }
        }

        string prepared = "." + word + ".";
        PatternValue[] result = new PatternValue[prepared.Length + 1];

        for (int i = 0; i < prepared.Length - 1; i++)
        {
            int limit = Math.Min(i + _maxLength, prepared.Length);
            for (int j = i + 1; j <= limit; j++)
            {
                if (!_patterns.TryGetValue(prepared.Substring(i, j - i), out var found))
                {
                    continue;
                }

                for (int k = 0; k < found.Values.Length; k++)
                {
                    int at = i + found.Offset + k;

                    //A well-formed pattern always lands inside the word: it
                    //has one more number than it has letters, and it matched
                    //at most to the last letter. A malformed one is skipped
                    //rather than resizing the array, which is what Python's
                    //slice assignment would silently do.
                    if (at < 0 || at >= result.Length) { continue; }

                    //Python's max returns its FIRST argument on a tie, so a
                    //pattern's own value — and the alternative spelling it may
                    //carry — wins over an equal value already there.
                    if (found.Values[k].Value >= result[at].Value)
                    {
                        result[at] = found.Values[k];
                    }
                }
            }
        }

        List<HyphenPosition> positions = new List<HyphenPosition>();
        for (int i = 0; i < result.Length; i++)
        {
            if (result[i].Value % 2 == 0) { continue; }

            positions.Add(result[i].Change == null
                ? new HyphenPosition(i - 1)
                : new HyphenPosition(
                    i - 1, result[i].Change, result[i].Index, result[i].Cut));
        }

        IReadOnlyList<HyphenPosition> answer = positions;
        lock (_positions) { _positions[word] = answer; }

        return answer;
    }

    /// <summary>Replaces every <c>^^hh</c> with the character it names.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text with the escapes resolved.</returns>
    internal static string ReplaceHex(string text)
        => HexEscape.Replace(
            text,
            match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());

    /// <summary>
    /// Reads a dictionary file with the character set its first line names.
    /// </summary>
    /// <param name="fileName">The file.</param>
    /// <returns>The text, first line and all.</returns>
    /// <remarks>
    /// The first line is a character set — sometimes the bare name, sometimes
    /// <c>charset NAME</c>. Every name on the line is tried in turn and the
    /// first one this application knows wins; when none does, Latin-1 stands
    /// in, which is upstream's fallback and cannot fail.
    /// </remarks>
    private static string ReadText(string fileName)
    {
        byte[] bytes = File.ReadAllBytes(fileName);
        int firstLineEnd = Array.IndexOf(bytes, (byte)'\n');
        string firstLine = Encoding.ASCII.GetString(
            bytes, 0, firstLineEnd < 0 ? bytes.Length : firstLineEnd);

        foreach (var name in firstLine.Split(
            (char[])null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(name, "charset", StringComparison.Ordinal)) { continue; }

            if (Charsets.TryDecode(name, bytes, out string decoded)) { return decoded; }
        }

        return Charsets.DecodeLatin1(bytes);
    }

    /// <summary>Splits the text into lines, dropping the first.</summary>
    /// <param name="text">The whole file.</param>
    /// <returns>The pattern lines.</returns>
    /// <remarks>Upstream consumes the first line to read the character set and
    /// never offers it as a pattern, however it turned out.</remarks>
    private static IEnumerable<string> ReadLines(string text)
    {
        using StringReader reader = new StringReader(text);
        _ = reader.ReadLine();
        while (reader.ReadLine() is { } line) { yield return line; }
    }

    /// <summary>
    /// One number in a pattern, with the non-standard spelling it may carry.
    /// </summary>
    private readonly struct PatternValue
    {
        public PatternValue(int value)
        {
            Value = value;
            Change = null;
            Index = 0;
            Cut = 0;
        }

        public PatternValue(int value, string change, int index, int cut)
        {
            Value = value;
            Change = change;
            Index = index;
            Cut = cut;
        }

        public int Value { get; }

        public string Change { get; }

        public int Index { get; }

        public int Cut { get; }
    }

    /// <summary>
    /// The non-standard spelling written after a pattern's slash.
    /// </summary>
    /// <remarks>Upstream's <c>ParsedAlternative</c>, and stateful for the same
    /// reason: it is handed each number of the pattern in turn and counts down
    /// as it goes, so where the change belongs is known by the time an odd
    /// number arrives.</remarks>
    private sealed class Alternative
    {
        private readonly string _change;
        private readonly int _cut;
        private int _index;

        public Alternative(string pattern, string text)
        {
            string[] parts = text.Split(',');
            _change = parts[0];
            if (parts.Length > 2)
            {
                _index = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                _cut = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)
                    + 1;
            }
            else
            {
                _index = 1;
                _cut = pattern.Count(c => !char.IsDigit(c) && c != '.') + 1;
            }

            if (pattern.StartsWith(".", StringComparison.Ordinal)) { _index += 1; }
        }

        public PatternValue Next(int value)
        {
            _index -= 1;
            return (value & 1) == 1
                ? new PatternValue(value, _change, _index, _cut)
                : new PatternValue(value);
        }
    }
}

/// <summary>
/// Hyphenates words with one dictionary's patterns, keeping the first and
/// last syllables at least as long as its settings ask.
/// </summary>
/// <remarks>
/// This is upstream's <c>Hyphenator</c> class, whose whole job is to put
/// margins around <see cref="HyphenationPatterns.Positions"/> and then offer
/// the three ways of spending the answer: every break at once
/// (<see cref="Inserted"/>), one break at a time longest-first
/// (<see cref="Iterate"/>), and the longest break that fits a width
/// (<see cref="Wrap"/>). Lyrics use the first of the three.
/// </remarks>
public sealed class Hyphenator
{
    private readonly HyphenationPatterns _patterns;

    /// <summary>Creates a hyphenator over a dictionary file.</summary>
    /// <param name="fileName">The <c>hyph_*.dic</c> file.</param>
    /// <param name="left">The shortest first syllable.</param>
    /// <param name="right">The shortest last syllable.</param>
    /// <param name="cache">Whether an already-read copy of the file may be
    /// used.</param>
    public Hyphenator(string fileName, int left = 2, int right = 2, bool cache = true)
        : this(HyphenationPatterns.Read(fileName, cache), left, right)
    {
    }

    /// <summary>Creates a hyphenator over patterns already read.</summary>
    /// <param name="patterns">The patterns.</param>
    /// <param name="left">The shortest first syllable.</param>
    /// <param name="right">The shortest last syllable.</param>
    public Hyphenator(HyphenationPatterns patterns, int left = 2, int right = 2)
    {
        _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
        Left = left;
        Right = right;
    }

    /// <summary>Gets or sets the shortest first syllable.</summary>
    public int Left { get; set; }

    /// <summary>Gets or sets the shortest last syllable.</summary>
    public int Right { get; set; }

    /// <summary>Answers the break points that are not too near either end.</summary>
    /// <param name="word">The word.</param>
    /// <returns>The positions, in order.</returns>
    public IReadOnlyList<HyphenPosition> Positions(string word)
    {
        if (string.IsNullOrEmpty(word)) { return Array.Empty<HyphenPosition>(); }

        int right = word.Length - Right;
        return _patterns.Positions(word)
            .Where(p => Left <= p.Index && p.Index <= right)
            .ToArray();
    }

    /// <summary>
    /// Walks the ways the word can be broken in two, longest first.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <returns>The pairs, longest first part first.</returns>
    public IEnumerable<(string First, string Second)> Iterate(string word)
    {
        IReadOnlyList<HyphenPosition> positions = Positions(word);
        for (int index = positions.Count - 1; index >= 0; index--)
        {
            HyphenPosition position = positions[index];
            if (!position.IsNonstandard)
            {
                yield return (
                    word.Substring(0, position.Index), word.Substring(position.Index));
                continue;
            }

            string change = IsUpper(word) ? position.Change.ToUpperInvariant() : position.Change;
            int equals = change.IndexOf('=');
            string before = equals < 0 ? change : change.Substring(0, equals);
            string after = equals < 0 ? string.Empty : change.Substring(equals + 1);
            int at = position.Index + position.ChangeIndex;
            yield return (
                word.Substring(0, at) + before,
                after + word.Substring(Math.Min(at + position.Cut, word.Length)));
        }
    }

    /// <summary>
    /// Answers the longest first part that fits a width, with the hyphen
    /// already on it, and the rest of the word.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <param name="width">How wide the first part may be.</param>
    /// <param name="hyphen">What to break with.</param>
    /// <returns>The two parts, or null when nothing fits.</returns>
    public (string First, string Second)? Wrap(
        string word, int width, string hyphen = "-")
    {
        hyphen ??= string.Empty;
        int room = width - hyphen.Length;
        foreach (var (first, second) in Iterate(word))
        {
            if (first.Length <= room) { return (first + hyphen, second); }
        }

        return null;
    }

    /// <summary>Answers the word with every break written into it.</summary>
    /// <param name="word">The word.</param>
    /// <param name="hyphen">What to break with.</param>
    /// <returns>The hyphenated word.</returns>
    /// <remarks>The Dutch <c>lettergrepen</c> answers
    /// <c>let-ter-gre-pen</c>. Lyrics ask for <c> -- </c> as the hyphen,
    /// which is how a syllable is spelled in a LilyPond lyrics block.</remarks>
    public string Inserted(string word, string hyphen = "-")
    {
        if (string.IsNullOrEmpty(word)) { return word; }

        hyphen ??= string.Empty;
        List<char> characters = new List<char>(word);
        IReadOnlyList<HyphenPosition> positions = Positions(word);
        bool upper = IsUpper(word);

        for (int index = positions.Count - 1; index >= 0; index--)
        {
            HyphenPosition position = positions[index];
            if (!position.IsNonstandard)
            {
                characters.InsertRange(position.Index, hyphen);
                continue;
            }

            string change = upper ? position.Change.ToUpperInvariant() : position.Change;
            string written = change.Replace("=", hyphen);
            int at = position.Index + position.ChangeIndex;
            int cut = Math.Min(position.Cut, Math.Max(0, characters.Count - at));
            characters.RemoveRange(at, cut);
            characters.InsertRange(at, written);
        }

        return new string(characters.ToArray());
    }

    /// <summary>Answers Python's <c>str.isupper()</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Whether every cased character is upper case, and there is at
    /// least one.</returns>
    private static bool IsUpper(string text)
    {
        bool anyCased = false;
        foreach (char character in text)
        {
            if (char.IsLower(character)) { return false; }

            if (char.IsUpper(character)) { anyCased = true; }
        }

        return anyCased;
    }
}
