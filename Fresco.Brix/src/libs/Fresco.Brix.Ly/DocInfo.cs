// === python-ly ly.docinfo module ===
//
// Copyright (c) 2013 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
// See http://www.gnu.org/licenses/ for more information.

using Fresco.Brix.Ly.Lex;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilyPondMode = Fresco.Brix.Ly.Lex.LilyPondMode;
using SchemeMode = Fresco.Brix.Ly.Lex.SchemeMode;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Ly; //was previously: ly/docinfo.py (class DocInfo);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Harvests information from a <see cref="DocumentBase"/>: the version, the
/// pitch language, the includes, the identifiers the document defines, whether
/// it produces output, and so on.
/// <para>
/// Every token of the document is flattened into <see cref="Tokens"/> with a
/// <see cref="Newline"/> inserted between blocks, and the matching runtime
/// types into <see cref="Classes"/>, which makes the searches below plain
/// array scans. Like upstream, a <see cref="DocInfo"/> does NOT update when
/// the document changes — make a new one.
/// </para>
/// </summary>
public class DocInfo
{
    private readonly Dictionary<string, object> _cache
        = new Dictionary<string, object>(StringComparer.Ordinal);

    private DocumentBase _document;

    /// <summary>Harvests a document.</summary>
    /// <param name="document">The document to read.</param>
    public DocInfo(DocumentBase document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));

        var tokens = new List<Token>();
        var first = true;
        foreach (DocumentBlock block in document.Blocks())
        {
            if (!first)
            {
                //Upstream joins the blocks with the newline that separates
                //them in the text, positioned just before the block's start.
                tokens.Add(new Newline("\n", document.Position(block) - 1));
            }

            tokens.AddRange(document.TokensWithPosition(block));
            first = false;
        }

        Tokens = tokens.ToArray();
        Classes = Tokens.Select(t => t.GetType()).ToArray();
    }

    private DocInfo()
    {
    }

    /// <summary>Gets the document this information was harvested from.</summary>
    public DocumentBase Document => _document;

    /// <summary>Gets every token of the document, blocks joined by newlines.</summary>
    public Token[] Tokens { get; private set; }

    /// <summary>Gets the runtime type of every entry of <see cref="Tokens"/>.</summary>
    public Type[] Classes { get; private set; }

    /// <summary>
    /// Answers a new instance covering only the tokens inside a range — a
    /// cheap way to search part of a large document.
    /// </summary>
    /// <param name="start">The first position of the range.</param>
    /// <param name="end">The last position, or <see langword="null"/> for the
    /// rest of the document.</param>
    /// <returns>The instance.</returns>
    public DocInfo Range(int start = 0, int? end = null)
    {
        if (start == 0 && end == null)
        {
            return this;
        }

        int lo = 0;
        int hi = Tokens.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (start > Tokens[mid].Pos) { lo = mid + 1; } else { hi = mid; }
        }

        int first = lo;

        int last = Tokens.Length;
        if (end != null)
        {
            lo = 0;
            hi = Tokens.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (end.Value < Tokens[mid].Pos) { hi = mid; } else { lo = mid + 1; }
            }

            //Upstream's `end = lo - 1` with an exclusive slice: the last token
            //that STARTS within the range is left out, because it may run past
            //the range's end.
            last = lo - 1;
        }

        if (last < first) { last = first; }

        var result = new DocInfo
        {
            _document = _document,
            Tokens = Tokens[first..last],
        };
        result.Classes = Classes[first..last];
        return result;
    }

    /// <summary>Answers the document's mode, e.g. <c>lilypond</c>.</summary>
    /// <returns>The mode.</returns>
    public string Mode()
        => Cached(nameof(Mode), () => _document.InitialState().Mode());

    /// <summary>
    /// Answers the index of the first token matching a text and/or an exact
    /// class at or after a position, or -1.
    /// </summary>
    /// <param name="text">The token text to match, or <see langword="null"/>
    /// to match on the class alone.</param>
    /// <param name="type">The exact token type to match, or
    /// <see langword="null"/> to match on the text alone.</param>
    /// <param name="pos">The first index to search.</param>
    /// <param name="endPos">The index to stop before; negative values count
    /// back from the end, as Python's list.index does — the default -1 never
    /// looks at the very last token.</param>
    /// <returns>The index, or -1.</returns>
    public int Find(string text = null, Type type = null, int pos = 0, int endPos = -1)
    {
        int stop = NormalizeEnd(endPos);
        for (int i = Math.Max(pos, 0); i < stop; i++)
        {
            if (text != null && !string.Equals(Tokens[i].Text, text, StringComparison.Ordinal))
            {
                continue;
            }

            if (type != null && Classes[i] != type)
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    /// <summary>Answers every index <see cref="Find"/> would answer in turn.</summary>
    /// <param name="text">The token text to match, or <see langword="null"/>.</param>
    /// <param name="type">The exact token type to match, or <see langword="null"/>.</param>
    /// <param name="pos">The first index to search.</param>
    /// <param name="endPos">The index to stop before (negative counts back).</param>
    /// <returns>The indices.</returns>
    public IEnumerable<int> FindAll(
        string text = null, Type type = null, int pos = 0, int endPos = -1)
    {
        while (true)
        {
            int i = Find(text, type, pos, endPos);
            if (i == -1) { yield break; }

            yield return i;
            pos = i + 1;
        }
    }

    /// <summary>
    /// Answers the <c>\version</c> string without its quotes, e.g. 2.19.8, or
    /// <see langword="null"/> when the document declares none.
    /// </summary>
    /// <returns>The version string.</returns>
    /// <remarks>Virtual because the application's own DocInfo also looks in the
    /// document variables and in non-LilyPond comments, exactly as upstream's
    /// <c>lydocinfo.DocInfo</c> overrides <c>version_string</c>.</remarks>
    public virtual string VersionString() => Cached(nameof(VersionString), () =>
    {
        int i = Find("\\version", typeof(LilyPondMode.Keyword));
        if (i == -1) { return null; }

        var rest = Slice(i + 1, i + 10);
        for (int n = 0; n < rest.Count; n++)
        {
            Token t = rest[n];
            if (t is Space || t is Comment) { continue; }

            //A quoted argument runs to the closing quote; a bare one runs to
            //the next space or comment.
            bool quoted = t.Text == "\"";
            var text = new List<string>();
            for (int m = quoted ? n + 1 : n; m < rest.Count; m++)
            {
                Token u = rest[m];
                if (quoted ? u.Text == "\"" : (u is Space || u is Comment)) { break; }

                text.Add(u.Text);
            }

            return string.Concat(text);
        }

        return null;
    });

    /// <summary>Answers <see cref="VersionString"/> as its integer parts.</summary>
    /// <returns>The parts, empty when there is no version.</returns>
    public int[] Version() => Cached(nameof(Version), () =>
    {
        string version = VersionString();
        if (string.IsNullOrEmpty(version)) { return Array.Empty<int>(); }

        return Regex.Matches(version, @"\d+")
            .Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture))
            .ToArray();
    });

    /// <summary>Answers the arguments of the <c>\include</c> commands.</summary>
    /// <returns>The arguments.</returns>
    public IReadOnlyList<string> IncludeArgs()
        => Cached(nameof(IncludeArgs), ()
            => (IReadOnlyList<string>)QuotedArguments(
                "\\include", typeof(LilyPondMode.Keyword)));

    /// <summary>Answers the arguments of the scheme <c>load</c> commands.</summary>
    /// <returns>The arguments.</returns>
    public IReadOnlyList<string> SchemeLoadArgs()
        => Cached(nameof(SchemeLoadArgs), ()
            => (IReadOnlyList<string>)QuotedArguments(
                "load", typeof(SchemeMode.Keyword)));

    /// <summary>
    /// Answers the arguments of the constructs that name output documents —
    /// <c>\bookOutputName</c>, <c>\bookOutputSuffix</c> and the scheme
    /// <c>output-suffix</c> definition — each as its kind (<c>name</c> or
    /// <c>suffix</c>) and its argument.
    /// </summary>
    /// <returns>The arguments.</returns>
    public IReadOnlyList<(string Kind, string Argument)> OutputArgs()
        => Cached(nameof(OutputArgs), () =>
        {
            var result = new List<(string Kind, string Argument)>();
            var wanted = new (string Kind, string Command, Type Type)[]
            {
                ("suffix", "output-suffix", typeof(SchemeMode.Word)),
                ("suffix", "\\bookOutputSuffix", typeof(LilyPondMode.Command)),
                ("name", "\\bookOutputName", typeof(LilyPondMode.Command)),
            };

            foreach ((string kind, string command, Type type) in wanted)
            {
                foreach (int i in FindAll(command, type))
                {
                    var rest = Slice(i + 1, i + 6);
                    for (int n = 0; n < rest.Count; n++)
                    {
                        Token t = rest[n];
                        if (t.Text == "\"")
                        {
                            result.Add((kind, TakeUntilQuote(rest, n + 1)));
                            break;
                        }

                        if (t is LilyPondMode.Name)
                        {
                            result.Add((kind, t.Text));
                            break;
                        }

                        if (t is LilyPondMode.SchemeStart || t is Space || t is Comment)
                        {
                            continue;
                        }

                        break;
                    }
                }
            }

            return (IReadOnlyList<(string Kind, string Argument)>)result;
        });

    /// <summary>Answers the LilyPond identifiers the document defines.</summary>
    /// <returns>The name tokens.</returns>
    public IReadOnlyList<Token> Definitions() => Cached(nameof(Definitions), () =>
    {
        var result = new List<Token>();
        foreach (int i in FindAll(null, typeof(LilyPondMode.Name)))
        {
            if (i == 0 || Tokens[i - 1].Text == "\n")
            {
                result.Add(Tokens[i]);
            }
        }

        return (IReadOnlyList<Token>)result;
    });

    /// <summary>Answers the markup command definitions the document makes.</summary>
    /// <returns>The name tokens, in document order.</returns>
    public IReadOnlyList<Token> MarkupDefinitions()
        => Cached(nameof(MarkupDefinitions), () =>
        {
            var result = new List<Token>();

            //bla = \markup { … }
            foreach (int i in FindAll(null, typeof(LilyPondMode.Name)))
            {
                if (i != 0 && Tokens[i - 1].Text != "\n") { continue; }

                foreach (Token t in Slice(i + 1, i + 6))
                {
                    if (t.Text == "\\markup")
                    {
                        result.Add(Tokens[i]);
                    }
                    else if (t.Text == "=" || IsSpace(t))
                    {
                        continue;
                    }

                    break;
                }
            }

            //#(define-markup-command … )
            foreach (int i in FindAll("define-markup-command", typeof(SchemeMode.Function)))
            {
                foreach (Token t in Slice(i + 1, i + 6))
                {
                    if (t is SchemeMode.Word)
                    {
                        result.Add(t);
                        break;
                    }
                }
            }

            result.Sort((a, b) => a.Pos.CompareTo(b.Pos));
            return (IReadOnlyList<Token>)result;
        });

    /// <summary>
    /// Answers the pitch language the document selects, or
    /// <see langword="null"/> when it selects none.
    /// </summary>
    /// <returns>The language name.</returns>
    public string Language() => Cached(nameof(Language), () =>
    {
        foreach (int i in FindAll("\\language", typeof(LilyPondMode.Keyword)))
        {
            foreach (Token t in Slice(i + 1, i + 10))
            {
                if (t is Space || t.Text == "\"") { continue; }

                if (Pitching.Pitches.Languages.Contains(t.Text)) { return t.Text; }
            }
        }

        foreach (string name in IncludeArgs())
        {
            int dot = name.LastIndexOf('.');
            string language = dot < 0 ? name : name.Substring(0, dot);
            if (Pitching.Pitches.Languages.Contains(language)) { return language; }
        }

        return null;
    });

    /// <summary>Answers the <c>set-global-staff-size</c> argument, if any.</summary>
    /// <returns>The size, or <see langword="null"/>.</returns>
    public int? GlobalStaffSize() => Cached(nameof(GlobalStaffSize), () =>
    {
        int i = Find("set-global-staff-size", typeof(SchemeMode.Function));
        if (i == -1 || i + 2 >= Tokens.Length) { return (int?)null; }

        return int.TryParse(
            Tokens[i + 2].Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size)
            ? size
            : (int?)null;
    });

    /// <summary>
    /// Answers a hash over every token that is neither whitespace nor comment,
    /// so it does not change when only formatting changes.
    /// </summary>
    /// <returns>The hash.</returns>
    /// <remarks>Upstream hashes the token strings with Python's per-process
    /// randomized string hash; this uses FNV-1a so the value is stable across
    /// runs, which callers that persist it (the file cache) rely on.</remarks>
    public int TokenHash() => Cached(nameof(TokenHash), () =>
    {
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            uint hash = offset;
            foreach (Token t in Tokens)
            {
                if (t is Space || t is Comment) { continue; }

                foreach (char c in t.Text)
                {
                    hash = (hash ^ c) * prime;
                }

                hash = (hash ^ ' ') * prime; //token boundary
            }

            return (int)hash;
        }
    });

    /// <summary>Answers whether the document looks complete and compilable.</summary>
    /// <returns>Whether it does.</returns>
    public bool Complete() => Cached(nameof(Complete), ()
        => _document.StateEnd(_document[_document.Count - 1]).Depth() == 1);

    /// <summary>
    /// Answers whether the document probably generates output — it has notes,
    /// rests, markup, lyrics or an include.
    /// </summary>
    /// <returns>Whether it does.</returns>
    public bool HasOutput() => Cached(nameof(HasOutput), () =>
    {
        var wanted = new (string Text, Type Type)[]
        {
            (null, typeof(LilyPondMode.MarkupStart)),
            (null, typeof(LilyPondMode.Note)),
            (null, typeof(LilyPondMode.Rest)),
            ("\\include", typeof(LilyPondMode.Keyword)),
            (null, typeof(LilyPondMode.LyricMode)),
        };

        foreach ((string text, Type type) in wanted)
        {
            if (Find(text, type) != -1) { return true; }
        }

        return false;
    });

    /// <summary>
    /// Answers how many tokens are of a type or a type derived from it (use
    /// <see cref="Classes"/> directly for exact-type counts).
    /// </summary>
    /// <param name="type">The type to count.</param>
    /// <returns>The count.</returns>
    public int CountTokens(Type type) => Classes.Count(type.IsAssignableFrom);

    /// <summary>Answers how many tokens there are of each exact type.</summary>
    /// <returns>The counts.</returns>
    public IReadOnlyDictionary<Type, int> CountedTokens()
        => Classes.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());

    private int NormalizeEnd(int endPos)
    {
        //Python's list.index(x, start, stop) treats a negative stop as
        //len + stop, so the default -1 stops before the last token.
        int stop = endPos < 0 ? Tokens.Length + endPos : endPos;
        return Math.Clamp(stop, 0, Tokens.Length);
    }

    private IReadOnlyList<Token> Slice(int start, int end)
    {
        start = Math.Clamp(start, 0, Tokens.Length);
        end = Math.Clamp(end, start, Tokens.Length);
        return new ArraySegment<Token>(Tokens, start, end - start);
    }

    private List<string> QuotedArguments(string command, Type type)
    {
        var result = new List<string>();
        foreach (int i in FindAll(command, type))
        {
            var rest = Slice(i + 1, i + 10);
            for (int n = 0; n < rest.Count; n++)
            {
                Token t = rest[n];
                if (t is Space || t is Comment) { continue; }

                if (t.Text == "\"")
                {
                    result.Add(TakeUntilQuote(rest, n + 1));
                }

                break;
            }
        }

        return result;
    }

    private static string TakeUntilQuote(IReadOnlyList<Token> tokens, int start)
    {
        var text = new List<string>();
        for (int i = start; i < tokens.Count; i++)
        {
            if (tokens[i].Text == "\"") { break; }

            text.Add(tokens[i].Text);
        }

        return string.Concat(text);
    }

    private static bool IsSpace(Token token)
        => token.Text.Length > 0 && token.Text.All(char.IsWhiteSpace);

    /// <summary>
    /// Answers a computed value, working it out once and remembering it.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="key">The value's name.</param>
    /// <param name="compute">How to work it out.</param>
    /// <returns>The value.</returns>
    /// <remarks>Protected so a derived DocInfo caches its own answers in the
    /// same place — upstream's <c>@_cache</c> decorator, which a subclass
    /// applies to its overrides.</remarks>
    protected T Cached<T>(string key, Func<T> compute)
    {
        if (_cache.TryGetValue(key, out object value)) { return (T)value; }

        T result = compute();
        _cache[key] = result;
        return result;
    }
}
