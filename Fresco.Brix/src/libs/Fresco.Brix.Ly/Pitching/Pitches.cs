// This file is part of python-ly, https://pypi.python.org/pypi/python-ly
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation, either version 3
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

using LilyPondMode = Fresco.Brix.Ly.Lex.LilyPondMode;
using PitchTable = Fresco.Brix.Ly.Pitching.Pitches;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Ly.Pitching; //was previously: ly/pitch/__init__.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The module-level surface of <c>ly/pitch/__init__.py</c>: the
/// <c>pitchInfo</c> language table, the octave string helpers, and the cached
/// per-language <see cref="PitchReader"/>/<see cref="PitchWriter"/> factories.
/// </summary>
public static class Pitches
{
    /// <summary>One language's pitch data: the seven note names, the nine
    /// accidental suffixes (double flat up to double sharp, quarter tones in
    /// between, empty where the language has no name), and the optional
    /// (long, short) replacement pairs.</summary>
    internal sealed class Info
    {
        /// <summary>Initializes the language info.</summary>
        /// <param name="names">The seven note names.</param>
        /// <param name="accs">The nine accidental suffixes.</param>
        /// <param name="replacements">The (long, short) replacement pairs.</param>
        internal Info(string[] names, string[] accs, (string S, string R)[] replacements = null)
        {
            Names = names;
            Accs = accs;
            Replacements = replacements ?? System.Array.Empty<(string, string)>();
        }

        /// <summary>The seven note names.</summary>
        internal string[] Names { get; }

        /// <summary>The nine accidental suffixes.</summary>
        internal string[] Accs { get; }

        /// <summary>The (long, short) replacement pairs.</summary>
        internal (string S, string R)[] Replacements { get; }
    }

    /// <summary>The <c>pitchInfo</c> table — data verbatim from upstream;
    /// norsk and suomi share the deutsch entry, catalan shares italiano,
    /// exactly as the upstream aliases do.</summary>
    internal static readonly Dictionary<string, Info> PitchInfo = BuildPitchInfo();

    /// <summary>The language names, in the table's (upstream insertion)
    /// order — upstream's <c>pitchInfo.keys()</c>.</summary>
    public static readonly string[] Languages =
    {
        "nederlands", "english", "deutsch", "svenska", "italiano", "espanol",
        "portugues", "vlaams", "norsk", "suomi", "catalan",
    };

    private static readonly Dictionary<string, PitchReader> PitchReaders
        = new Dictionary<string, PitchReader>(StringComparer.Ordinal);

    private static readonly Dictionary<string, PitchWriter> PitchWriters
        = new Dictionary<string, PitchWriter>(StringComparer.Ordinal);

    private static Dictionary<string, Info> BuildPitchInfo()
    {
        Dictionary<string, Info> info = new Dictionary<string, Info>(StringComparer.Ordinal)
        {
            {
                "nederlands", new Info(
                    new[] { "c", "d", "e", "f", "g", "a", "b" },
                    new[] { "eses", "eseh", "es", "eh", "", "ih", "is", "isih", "isis" },
                    new[] { ("ees", "es"), ("aes", "as") })
            },
            {
                "english", new Info(
                    new[] { "c", "d", "e", "f", "g", "a", "b" },
                    new[] { "ff", "tqf", "f", "qf", "", "qs", "s", "tqs", "ss" })
            },
            {
                "deutsch", new Info(
                    new[] { "c", "d", "e", "f", "g", "a", "h" },
                    new[] { "eses", "eseh", "es", "eh", "", "ih", "is", "isih", "isis" },
                    new[]
                    {
                        ("ases", "asas"), ("ees", "es"), ("aes", "as"),
                        ("heses", "heses"), ("hes", "b"),
                    })
            },
            {
                "svenska", new Info(
                    new[] { "c", "d", "e", "f", "g", "a", "h" },
                    new[] { "essess", "", "ess", "", "", "", "iss", "", "ississ" },
                    new[]
                    {
                        ("ees", "es"), ("aes", "as"),
                        ("hessess", "hessess"), ("hess", "b"),
                    })
            },
            {
                "italiano", new Info(
                    new[] { "do", "re", "mi", "fa", "sol", "la", "si" },
                    new[] { "bb", "bsb", "b", "sb", "", "sd", "d", "dsd", "dd" })
            },
            {
                "espanol", new Info(
                    new[] { "do", "re", "mi", "fa", "sol", "la", "si" },
                    new[] { "bb", "", "b", "", "", "", "s", "", "ss" })
            },
            {
                "portugues", new Info(
                    new[] { "do", "re", "mi", "fa", "sol", "la", "si" },
                    new[] { "bb", "btqt", "b", "bqt", "", "sqt", "s", "stqt", "ss" })
            },
            {
                "vlaams", new Info(
                    new[] { "do", "re", "mi", "fa", "sol", "la", "si" },
                    new[] { "bb", "", "b", "", "", "", "k", "", "kk" })
            },
        };

        info["norsk"] = info["deutsch"];
        info["suomi"] = info["deutsch"];
        info["catalan"] = info["italiano"];
        return info;
    }

    /// <summary>Converts a numeric octave to a string with apostrophes or
    /// commas: 0 → "", 1 → "'", -1 → ",", etc.</summary>
    /// <param name="octave">The octave number.</param>
    /// <returns>The octave string.</returns>
    public static string OctaveToString(int octave)
        => octave < 0 ? new string(',', -octave) : new string('\'', octave);

    /// <summary>Converts a string octave to an integer: "" → 0, "," → -1,
    /// "'''" → 3, etc.</summary>
    /// <param name="octave">The octave string.</param>
    /// <returns>The octave number.</returns>
    public static int OctaveToNum(string octave)
        => octave.Count(c => c == '\'') - octave.Count(c => c == ',');

    /// <summary>Returns the (cached) <see cref="PitchReader"/> for the
    /// specified language.</summary>
    /// <param name="language">The language name.</param>
    /// <returns>The reader.</returns>
    public static PitchReader PitchReaderFor(string language)
    {
        if (!PitchReaders.TryGetValue(language, out PitchReader reader))
        {
            Info info = PitchInfo[language];
            reader = new PitchReader(info.Names, info.Accs, info.Replacements);
            PitchReaders[language] = reader;
        }

        return reader;
    }

    /// <summary>Returns the (cached) <see cref="PitchWriter"/> for the
    /// specified language.</summary>
    /// <param name="language">The language name.</param>
    /// <returns>The writer, with its language name set.</returns>
    public static PitchWriter PitchWriterFor(string language)
    {
        if (!PitchWriters.TryGetValue(language, out PitchWriter writer))
        {
            Info info = PitchInfo[language];
            writer = new PitchWriter(info.Names, info.Accs, info.Replacements)
            {
                Language = language,
            };
            PitchWriters[language] = writer;
        }

        return writer;
    }
}

/// <summary>
/// Exception raised when there is no name for a pitch.
/// <para>
/// Can occur when translating pitch names, if the target language e.g. does
/// not have quarter-tone names.
/// </para>
/// </summary>
public class PitchNameNotAvailableException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="language">The language that misses the name.</param>
    public PitchNameNotAvailableException(string language)
        : base("no name for the pitch in language: " + language)
    {
        Language = language;
    }

    /// <summary>Gets the language that misses the name.</summary>
    public string Language { get; }
}

/// <summary>
/// A pitch with note, alter and octave attributes.
/// <para>Attributes may be manipulated directly.</para>
/// </summary>
public class Pitch
{
    /// <summary>Initializes a pitch.</summary>
    /// <param name="note">The base note (c, d, e, f, g, a, b) as an integer
    /// (0 to 6).</param>
    /// <param name="alter">The alteration: # = 1/2, b = -1/2, natural = 0 —
    /// upstream's .5-based floats as exact fractions.</param>
    /// <param name="octave">The octave: '' = 2, ,, = -2.</param>
    /// <param name="accidental">"", "?" or "!".</param>
    /// <param name="octavecheck">A number is an octave check.</param>
    public Pitch(
        int note = 0,
        Fraction alter = default,
        int octave = 0,
        string accidental = "",
        int? octavecheck = null)
    {
        Note = note;
        Alter = alter;
        Octave = octave;
        Accidental = accidental;
        Octavecheck = octavecheck;
    }

    /// <summary>Gets or sets the base note (0 to 6).</summary>
    public int Note { get; set; }

    /// <summary>Gets or sets the alteration (# = 1/2, b = -1/2).</summary>
    public Fraction Alter { get; set; }

    /// <summary>Gets or sets the octave ('' = 2, ,, = -2).</summary>
    public int Octave { get; set; }

    /// <summary>Gets or sets the accidental: "", "?" or "!".</summary>
    public string Accidental { get; set; }

    /// <summary>Gets or sets the octave check, or <see langword="null"/>.</summary>
    public int? Octavecheck { get; set; }

    /// <summary>Gets or sets the note-name token this pitch was read from —
    /// the slot <see cref="PitchIterator"/> hangs on the pitch.</summary>
    public Slexing.Token NoteToken { get; set; }

    /// <summary>Gets or sets the octave token, or <see langword="null"/>.</summary>
    public Slexing.Token OctaveToken { get; set; }

    /// <summary>Gets or sets the accidental token, or <see langword="null"/>.</summary>
    public Slexing.Token AccidentalToken { get; set; }

    /// <summary>Gets or sets the octave-check token, or <see langword="null"/>.</summary>
    public Slexing.Token OctavecheckToken { get; set; }

    /// <summary>Gets or sets the transposed shadow copy — upstream's dynamic
    /// <c>transposed</c> attribute the transpose tool hangs on a pitch;
    /// <see langword="null"/> when never set (upstream's AttributeError
    /// path).</summary>
    public Pitch Transposed { get; set; }

    /// <summary>Returns our string representation in the language.</summary>
    /// <param name="language">The pitch-name language.</param>
    /// <returns>The pitch text.</returns>
    public string Output(string language = "nederlands")
    {
        StringBuilder res = new StringBuilder();
        res.Append(PitchTable.PitchWriterFor(language).Write(Note, Alter));
        res.Append(PitchTable.OctaveToString(Octave));
        res.Append(Accidental);
        if (Octavecheck != null)
        {
            res.Append('=');
            res.Append(PitchTable.OctaveToString(Octavecheck.Value));
        }

        return res.ToString();
    }

    /// <summary>Returns a pitch c'.</summary>
    /// <returns>The pitch.</returns>
    public static Pitch C1() => new Pitch(octave: 1);

    /// <summary>Returns a pitch c.</summary>
    /// <returns>The pitch.</returns>
    public static Pitch C0() => new Pitch();

    /// <summary>Returns a pitch f.</summary>
    /// <returns>The pitch.</returns>
    public static Pitch F0() => new Pitch(3);

    /// <summary>Returns a new instance with our attributes.</summary>
    /// <returns>The copy. Upstream's <c>copy()</c> passes only note, alter and
    /// octave to the constructor, so accidental, octave check and the token
    /// slots are NOT copied — reproduced as-is.</returns>
    public Pitch Copy() => new Pitch(Note, Alter, Octave);

    /// <summary>Makes ourselves absolute, i.e. sets our octave from
    /// <paramref name="lastPitch"/>.</summary>
    /// <param name="lastPitch">The preceding pitch.</param>
    public void MakeAbsolute(Pitch lastPitch)
    {
        //upstream note: python // floors toward negative infinity — NOT C#
        //integer division.
        Octave += lastPitch.Octave - PitchMath.FloorDiv(Note - lastPitch.Note + 3, 7);
    }

    /// <summary>Makes ourselves relative, i.e. changes our octave from
    /// <paramref name="lastPitch"/>.</summary>
    /// <param name="lastPitch">The preceding pitch.</param>
    public void MakeRelative(Pitch lastPitch)
    {
        Octave -= lastPitch.Octave - PitchMath.FloorDiv(Note - lastPitch.Note + 3, 7);
    }
}

/// <summary>Writes a pitch name in one language.</summary>
public class PitchWriter
{
    private readonly string[] _names;
    private readonly string[] _accs;
    private readonly (string S, string R)[] _replacements;

    /// <summary>Initializes the writer over one language's data.</summary>
    /// <param name="names">The seven note names.</param>
    /// <param name="accs">The nine accidental suffixes.</param>
    /// <param name="replacements">The (long, short) replacement pairs.</param>
    internal PitchWriter(
        string[] names, string[] accs, (string S, string R)[] replacements = null)
    {
        _names = names;
        _accs = accs;
        _replacements = replacements ?? System.Array.Empty<(string, string)>();
    }

    /// <summary>Gets the language name — upstream's class-level "unknown"
    /// until <see cref="Pitches.PitchWriterFor"/> sets it.</summary>
    public string Language { get; internal set; } = "unknown";

    /// <summary>
    /// Returns a string representing the pitch in our language — upstream's
    /// <c>__call__</c>.
    /// </summary>
    /// <param name="note">The base note (0 to 6).</param>
    /// <param name="alter">The alteration.</param>
    /// <returns>The pitch name.</returns>
    /// <exception cref="PitchNameNotAvailableException">If the requested pitch
    /// has an alteration that is not available in the current language.</exception>
    public string Write(int note, Fraction alter = default)
    {
        string pitch = _names[note];
        if (alter != Fraction.Zero)
        {
            string acc = _accs[PitchMath.TruncateToInt(alter * 4 + 4)];
            if (string.IsNullOrEmpty(acc))
            {
                throw new PitchNameNotAvailableException(Language);
            }

            pitch += acc;
        }

        foreach ((string s, string r) in _replacements)
        {
            if (pitch.StartsWith(s, StringComparison.Ordinal))
            {
                pitch = r + pitch.Substring(s.Length);
                break;
            }
        }

        return pitch;
    }
}

/// <summary>Reads a pitch name in one language.</summary>
public class PitchReader
{
    private readonly List<string> _names;
    private readonly List<string> _accs;
    private readonly (string S, string R)[] _replacements;
    private readonly Regex _rx;

    /// <summary>Initializes the reader over one language's data.</summary>
    /// <param name="names">The seven note names.</param>
    /// <param name="accs">The nine accidental suffixes.</param>
    /// <param name="replacements">The (long, short) replacement pairs.</param>
    internal PitchReader(
        string[] names, string[] accs, (string S, string R)[] replacements = null)
    {
        _names = new List<string>(names);
        _accs = new List<string>(accs);
        _replacements = replacements ?? System.Array.Empty<(string, string)>();
        _rx = new Regex(string.Format(
            "({0})({1})?$",
            string.Join("|", names),
            string.Join("|", accs.Where(acc => acc.Length != 0))));
    }

    /// <summary>
    /// Reads (note, alter) from a pitch name — upstream's <c>__call__</c>,
    /// which returns <c>False</c> when the text is no pitch name.
    /// </summary>
    /// <param name="text">The pitch name text.</param>
    /// <param name="note">The base note (0 to 6).</param>
    /// <param name="alter">The alteration, <c>Fraction(accsIndex - 4, 4)</c>.</param>
    /// <returns>Whether the text was a pitch name.</returns>
    public bool TryRead(string text, out int note, out Fraction alter)
    {
        note = 0;
        alter = Fraction.Zero;
        foreach ((string s, string r) in _replacements)
        {
            if (text.StartsWith(r, StringComparison.Ordinal))
            {
                text = s + text.Substring(r.Length);
            }
        }

        for (int dummy = 0; dummy < 2; dummy++)
        {
            //upstream's re.match: the match must start at position 0.
            Match m = _rx.Match(text);
            if (m.Success && m.Index == 0)
            {
                note = _names.IndexOf(m.Groups[1].Value);
                if (m.Groups[2].Success && m.Groups[2].Value.Length != 0)
                {
                    alter = new Fraction(_accs.IndexOf(m.Groups[2].Value) - 4, 4);
                }
                else
                {
                    alter = Fraction.Zero;
                }

                return true;
            }

            // HACK: were we using (rarely used) long english syntax?
            text = text.Replace("flat", "f").Replace("sharp", "s");
        }

        return false;
    }
}

/// <summary>Iterate over notes or pitches in a source.</summary>
public class PitchIterator
{
    /// <summary>Initializes with a <see cref="Ly.Source"/>.
    /// The language is by default set to "nederlands".</summary>
    /// <param name="source">The source to iterate.</param>
    /// <param name="language">The initial pitch-name language.</param>
    public PitchIterator(Source source, string language = "nederlands")
    {
        Source = source;
        SetLanguage(language);
    }

    /// <summary>Gets the source.</summary>
    public Source Source { get; }

    /// <summary>Gets the current pitch-name language.</summary>
    public string Language { get; private set; }

    /// <summary>
    /// Changes the pitch name language to use.
    /// <para>
    /// Called internally when <c>\language</c> or <c>\include</c> tokens are
    /// encountered with a valid language name/file. Sets <see cref="Language"/>
    /// when the name is a known language.
    /// </para>
    /// </summary>
    /// <param name="lang">The language name.</param>
    /// <returns>Whether the language was known (upstream returns True/None).</returns>
    public bool SetLanguage(string lang)
    {
        if (System.Array.IndexOf(PitchTable.Languages, lang) >= 0)
        {
            Language = lang;
            return true;
        }

        return false;
    }

    /// <summary>Yields all the tokens from the source, following the
    /// language.</summary>
    /// <returns>The tokens, with a <see cref="LanguageName"/> token yielded
    /// after a recognized language argument.</returns>
    public IEnumerable<Slexing.Token> Tokens()
    {
        Slexing.Token t;
        while ((t = Source.NextToken()) != null)
        {
            yield return t;
            if (t is LilyPondMode.Keyword)
            {
                if (t.Text == "\\include" || t.Text == "\\language")
                {
                    Slexing.Token inner;
                    while ((inner = Source.NextToken()) != null)
                    {
                        if (!(inner is Lex.Space) && inner.Text != "\"")
                        {
                            string lang = inner.Text.EndsWith(".ly", StringComparison.Ordinal)
                                ? inner.Text.Substring(0, inner.Text.Length - 3)
                                : inner.Text;
                            if (SetLanguage(lang))
                            {
                                yield return new LanguageName(lang, inner.Pos);
                            }

                            break;
                        }

                        yield return inner;
                    }
                }
            }
        }
    }

    /// <summary>Reads the token and returns (note, alter) or false —
    /// upstream's <c>read()</c> returning None.</summary>
    /// <param name="token">The token to read.</param>
    /// <param name="note">The base note (0 to 6).</param>
    /// <param name="alter">The alteration.</param>
    /// <returns>Whether the token was a pitch name.</returns>
    public bool Read(Slexing.Token token, out int note, out Fraction alter)
        => PitchTable.PitchReaderFor(Language).TryRead(token.Text, out note, out alter);

    /// <summary>
    /// Yields all tokens, but collects Note and Octave tokens.
    /// <para>
    /// When a Note is encountered, also reads octave and octave check and then
    /// a <see cref="Pitch"/> is yielded instead of the tokens.
    /// </para>
    /// </summary>
    /// <returns>The mixed stream of <see cref="Slexing.Token"/> and
    /// <see cref="Pitch"/> objects.</returns>
    public IEnumerable<object> Pitches()
    {
        IEnumerator<Slexing.Token> tokens = Tokens().GetEnumerator();
        while (tokens.MoveNext())
        {
            Slexing.Token t = tokens.Current;
            bool broke = false;
            while (t is LilyPondMode.Note)
            {
                if (!Read(t, out int note, out Fraction alter))
                {
                    broke = true;
                    break;
                }

                Pitch p = new Pitch(note, alter)
                {
                    NoteToken = t,
                    OctaveToken = null,
                    AccidentalToken = null,
                    OctavecheckToken = null,
                };

                t = null; // prevent hang in this loop
                while (tokens.MoveNext())
                {
                    t = tokens.Current;
                    if (t is LilyPondMode.Octave)
                    {
                        p.Octave = PitchTable.OctaveToNum(t.Text);
                        p.OctaveToken = t;
                    }
                    else if (t is LilyPondMode.Accidental)
                    {
                        p.AccidentalToken = t;
                        p.Accidental = t.Text;
                    }
                    else if (t is LilyPondMode.OctaveCheck)
                    {
                        p.Octavecheck = PitchTable.OctaveToNum(t.Text);
                        p.OctavecheckToken = t;
                        break;
                    }
                    else if (!(t is Lex.Space))
                    {
                        break;
                    }
                }

                yield return p;
                if (t == null)
                {
                    broke = true;
                    break;
                }
            }

            //upstream's while..else: only reached when the while condition
            //went false without a break.
            if (!broke)
            {
                yield return t;
            }
        }
    }

    /// <summary>Returns the cursor position for the given token.</summary>
    /// <param name="token">The token.</param>
    /// <returns>The position.</returns>
    public int Position(Slexing.Token token) => Source.Position(token);

    /// <summary>Returns the cursor position for the given pitch (its note
    /// token).</summary>
    /// <param name="pitch">The pitch.</param>
    /// <returns>The position.</returns>
    public int Position(Pitch pitch) => Source.Position(pitch.NoteToken);

    /// <summary>
    /// Output a changed Pitch: the pitch is written in the source's document.
    /// <para>
    /// To use this method reliably, you must instantiate the PitchIterator
    /// with a <see cref="Ly.Source"/> that has tokens-with-position set to
    /// true.
    /// </para>
    /// </summary>
    /// <param name="pitch">The pitch to write.</param>
    /// <param name="language">The language, or <see langword="null"/> for the
    /// iterator's current language.</param>
    public void Write(Pitch pitch, string language = null)
    {
        DocumentBase document = Source.Document;
        PitchWriter pwriter = PitchTable.PitchWriterFor(language ?? Language);
        string note = pwriter.Write(pitch.Note, pitch.Alter);
        int end = pitch.NoteToken.End;
        if (note != pitch.NoteToken.Text)
        {
            document.SetText(pitch.NoteToken.Pos, end, note);
        }

        string octave = PitchTable.OctaveToString(pitch.Octave);

        //upstream note: `octave != pitch.octave_token` is a python string
        //comparison, True whenever the token is None.
        if (pitch.OctaveToken == null || octave != pitch.OctaveToken.Text)
        {
            if (pitch.OctaveToken == null)
            {
                document.SetText(end, end, octave);
            }
            else
            {
                end = pitch.OctaveToken.End;
                document.SetText(pitch.OctaveToken.Pos, end, octave);
            }
        }

        if (!string.IsNullOrEmpty(pitch.Accidental))
        {
            if (pitch.AccidentalToken == null)
            {
                document.SetText(end, end, pitch.Accidental);
            }
            else if (pitch.Accidental != pitch.AccidentalToken.Text)
            {
                end = pitch.AccidentalToken.End;
                document.SetText(pitch.AccidentalToken.Pos, end, pitch.Accidental);
            }
        }
        else if (pitch.AccidentalToken != null)
        {
            document.Delete(pitch.AccidentalToken.Pos, pitch.AccidentalToken.End);
        }

        if (pitch.Octavecheck != null)
        {
            string octavecheck = "=" + PitchTable.OctaveToString(pitch.Octavecheck.Value);
            if (pitch.OctavecheckToken == null)
            {
                document.SetText(end, end, octavecheck);
            }
            else if (octavecheck != pitch.OctavecheckToken.Text)
            {
                document.SetText(
                    pitch.OctavecheckToken.Pos, pitch.OctavecheckToken.End, octavecheck);
            }
        }
        else if (pitch.OctavecheckToken != null)
        {
            document.Delete(pitch.OctavecheckToken.Pos, pitch.OctavecheckToken.End);
        }
    }
}

/// <summary>A Token that denotes a language name.</summary>
public class LanguageName : Lex.Token
{
    /// <summary>Initializes the token — constructed directly by
    /// <see cref="PitchIterator.Tokens"/>, never by a rule.</summary>
    /// <param name="text">The language name.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LanguageName(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Python integer semantics the pitch tools rely on — floor division
/// and modulo (toward negative infinity) and <c>int()</c> truncation. New
/// plumbing, not an upstream class.</summary>
internal static class PitchMath
{
    /// <summary>Python's <c>//</c>: floor division toward negative infinity.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor (positive at every call site).</param>
    /// <returns>The floored quotient.</returns>
    internal static int FloorDiv(int a, int b)
    {
        int q = a / b;
        if ((a % b != 0) && ((a < 0) != (b < 0)))
        {
            q -= 1;
        }

        return q;
    }

    /// <summary>Python's <c>divmod()</c>: floored quotient and non-negative
    /// remainder (for a positive divisor).</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The (quotient, remainder) pair.</returns>
    internal static (int Div, int Mod) DivMod(int a, int b)
    {
        int div = FloorDiv(a, b);
        return (div, a - div * b);
    }

    /// <summary>Python's <c>%</c>: remainder with the divisor's sign.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The remainder.</returns>
    internal static int Mod(int a, int b) => a - FloorDiv(a, b) * b;

    /// <summary>Python's <c>int()</c> over a fraction: truncation toward
    /// zero.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The truncated integer.</returns>
    internal static int TruncateToInt(Fraction value)
        => (int)(value.Numerator / value.Denominator);
}

/// <summary>The StopIteration signal the ported pitch tools use to reproduce
/// python generator-exhaustion control flow. New plumbing, not an upstream
/// class.</summary>
internal sealed class StopIterationSignal : Exception
{
    /// <summary>Initializes the signal.</summary>
    internal StopIterationSignal()
    {
    }
}
