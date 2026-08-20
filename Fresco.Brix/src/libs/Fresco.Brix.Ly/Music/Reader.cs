// === python-ly ly.music.read module ===
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LilyPondMode = Fresco.Brix.Ly.Lex.LilyPondMode;
using LyToken = Fresco.Brix.Ly.Slexing.Token;
using PitchTable = Fresco.Brix.Ly.Pitching.Pitches;
using SchemeMode = Fresco.Brix.Ly.Lex.SchemeMode;

namespace Fresco.Brix.Ly.Music; //was previously: ly/music/read.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Reads tokens from a <see cref="Source"/> and builds the meaningful tree of
/// <see cref="Item"/>s the music api works over. Whitespace and comments are
/// left out.
/// </summary>
public class Reader
{
    private static readonly Dictionary<string, Action<Reader, LyToken, IEnumerable<LyToken>, Box>>
        Commands = new Dictionary<string, Action<Reader, LyToken, IEnumerable<LyToken>, Box>>(
            StringComparer.Ordinal);

    private static readonly Dictionary<string, Action<Reader, LyToken, IEnumerable<LyToken>, Box>>
        Keywords = new Dictionary<string, Action<Reader, LyToken, IEnumerable<LyToken>, Box>>(
            StringComparer.Ordinal);

    private static readonly Dictionary<Type, Func<Reader, LyToken, IEnumerable<LyToken>, Item>>
        TokenClasses = new Dictionary<Type, Func<Reader, LyToken, IEnumerable<LyToken>, Item>>();

    private static readonly Dictionary<Type, Func<Reader, LyToken, Item>> MarkupClasses
        = new Dictionary<Type, Func<Reader, LyToken, Item>>();

    private static readonly Dictionary<Type, Func<Reader, LyToken, Item>> SchemeClasses
        = new Dictionary<Type, Func<Reader, LyToken, Item>>();

    private static readonly ConcurrentDictionary<(string Table, Type Type), object> Resolved
        = new ConcurrentDictionary<(string, Type), object>();

    private static readonly Dictionary<Type, Func<Item>> DirectItems
        = new Dictionary<Type, Func<Item>>
        {
            { typeof(LilyPondMode.VoiceSeparator), () => new VoiceSeparator() },
            { typeof(LilyPondMode.PipeSymbol), () => new PipeSymbol() },
            { typeof(LilyPondMode.Dynamic), () => new Dynamic() },
            { typeof(LilyPondMode.Tie), () => new Tie() },
        };

    private static readonly Dictionary<string, Func<Item>> InputModeCommands
        = new Dictionary<string, Func<Item>>(StringComparer.Ordinal)
        {
            { "\\notemode", () => new NoteMode() },
            { "\\notes", () => new NoteMode() },
            { "\\chordmode", () => new ChordMode() },
            { "\\chords", () => new ChordMode() },
            { "\\figuremode", () => new FigureMode() },
            { "\\figures", () => new FigureMode() },
            { "\\drummode", () => new DrumMode() },
            { "\\drums", () => new DrumMode() },
        };

    private static readonly Dictionary<string, Func<Item>> LyricModeCommands
        = new Dictionary<string, Func<Item>>(StringComparer.Ordinal)
        {
            { "\\lyricmode", () => new LyricMode() },
            { "\\lyrics", () => new LyricMode() },
            { "\\oldaddlyrics", () => new LyricMode() },
            { "\\addlyrics", () => new LyricMode() },
            { "\\lyricsto", () => new LyricsTo() },
        };

    private static readonly Dictionary<string, Func<Item>> BracketedKeywords
        = new Dictionary<string, Func<Item>>(StringComparer.Ordinal)
        {
            { "\\header", () => new Header() },
            { "\\score", () => new Score() },
            { "\\bookpart", () => new BookPart() },
            { "\\book", () => new Book() },
            { "\\paper", () => new Paper() },
            { "\\layout", () => new Layout() },
            { "\\midi", () => new Midi() },
            { "\\with", () => new With() },
            { "\\context", () => new LayoutContext() },
        };

    private static readonly Lazy<HashSet<Type>> UnsetItems
        = new Lazy<HashSet<Type>>(() => ItemTypesOf(new LilyPondMode.ParseUnset()));

    private static readonly Lazy<HashSet<Type>> RevertItems
        = new Lazy<HashSet<Type>>(() => ItemTypesOf(new LilyPondMode.ParseRevert()));

    private static readonly Lazy<HashSet<Type>> TweakItems
        = new Lazy<HashSet<Type>>(() => ItemTypesOf(new LilyPondMode.ParseTweak()));

    private readonly Source _source;

    static Reader()
    {
        RegisterTokenClasses();
        RegisterCommands();
        RegisterKeywords();
        RegisterMarkup();
        RegisterScheme();
    }

    /// <summary>Reads from a source; the language starts at nederlands.</summary>
    /// <param name="source">The source to read.</param>
    public Reader(Source source) => _source = source;

    /// <summary>Gets the source read from.</summary>
    public Source Source => _source;

    /// <summary>Gets the current pitch-name language.</summary>
    public string Language { get; private set; } = "nederlands";

    /// <summary>Gets or sets whether a chord is being read.</summary>
    public bool InChord { get; set; }

    /// <summary>Gets or sets the duration the previous durable carried.</summary>
    public (Fraction Base, Fraction Scaling) PrevDuration { get; set; }
        = (new Fraction(1, 4), Fraction.One);

    /// <summary>
    /// Changes the pitch name language, when the name is a known one — called
    /// internally for <c>\language</c> and language <c>\include</c>s.
    /// </summary>
    /// <param name="language">The language name.</param>
    /// <returns>Whether the name was known.</returns>
    public bool SetLanguage(string language)
    {
        if (Array.IndexOf(PitchTable.Languages, language) < 0) { return false; }

        Language = language;
        return true;
    }

    /// <summary>Yields the items read from a source.</summary>
    /// <param name="source">The tokens to read, or <see langword="null"/> for
    /// the reader's own source.</param>
    /// <returns>The items.</returns>
    public IEnumerable<Item> Read(IEnumerable<LyToken> source = null)
    {
        IEnumerable<LyToken> tokens = source ?? _source;
        foreach (LyToken t in Skip(tokens))
        {
            Item item = ReadItem(t, tokens);
            if (item != null) { yield return item; }
        }
    }

    /// <summary>Answers the one item that starts with a token, if any.</summary>
    /// <param name="t">The token.</param>
    /// <param name="source">The tokens to read on with.</param>
    /// <returns>The item, or <see langword="null"/>.</returns>
    public Item ReadItem(LyToken t, IEnumerable<LyToken> source = null)
    {
        Func<Reader, LyToken, IEnumerable<LyToken>, Item> method
            = Lookup(TokenClasses, "tokencls", t);
        return method?.Invoke(this, t, source ?? _source);
    }

    /// <summary>Yields the tokens of a source, skipping space and comments.</summary>
    /// <param name="source">The tokens.</param>
    /// <returns>The tokens kept.</returns>
    public static IEnumerable<LyToken> Skip(IEnumerable<LyToken> source)
    {
        foreach (LyToken t in source)
        {
            if (!(t is Lex.Space) && !(t is Lex.Comment)) { yield return t; }
        }
    }

    /// <summary>
    /// Yields the tokens until the current parser is left, handing the last
    /// token read to a callback when one is given.
    /// </summary>
    /// <param name="lastToken">The callback, or <see langword="null"/>.</param>
    /// <returns>The tokens.</returns>
    public IEnumerable<LyToken> Consume(Action<LyToken> lastToken = null)
        => new SharedEnumerable<LyToken>(ConsumeCore(lastToken));

    private IEnumerable<LyToken> ConsumeCore(Action<LyToken> lastToken)
    {
        LyToken last = null;
        foreach (LyToken t in _source.UntilParserEnd())
        {
            last = t;
            yield return t;
        }

        if (lastToken != null && last != null) { lastToken(last); }
    }

    /// <summary>Makes an item for a token.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="token">The token the item starts with, or
    /// <see langword="null"/> when a position is given instead.</param>
    /// <param name="consume">Whether to consume the source into the item's
    /// tokens.</param>
    /// <param name="position">The position, when there is no token.</param>
    /// <returns>The item.</returns>
    /// <exception cref="ArgumentException">When neither a token nor a position
    /// is given.</exception>
    public T Factory<T>(LyToken token = null, bool consume = false, int? position = null)
        where T : Item, new()
    {
        var item = new T { SourceDocument = _source.Document };
        if (token != null)
        {
            item.Token = token;
            item.Position = token.Pos;
        }
        else if (position == null)
        {
            throw new ArgumentException("position must be specified if no token", nameof(position));
        }
        else
        {
            item.Position = position.Value;
        }

        if (consume)
        {
            item.Tokens = Consume().ToList();
            if (token == null && item.Tokens.Count > 0)
            {
                item.Position = item.Tokens[0].Pos;
            }
        }

        return item;
    }

    /// <summary>
    /// Adds a duration to an item, appending a <see cref="DurationItem"/> when
    /// there are duration tokens.
    /// </summary>
    /// <param name="item">The item to add to.</param>
    /// <param name="token">The first duration token, if already read.</param>
    /// <param name="source">The tokens to read on with.</param>
    public void AddDuration(Item item, LyToken token = null, IEnumerable<LyToken> source = null)
    {
        IEnumerable<LyToken> tokens = source ?? _source;
        var found = new List<LyToken>();
        if (token == null || token is LilyPondMode.Duration)
        {
            if (token != null) { found.Add(token); }

            foreach (LyToken t in tokens)
            {
                if (t is LilyPondMode.Duration)
                {
                    if (found.Count > 0 && t is LilyPondMode.Length)
                    {
                        _source.Pushback();
                        break;
                    }

                    found.Add(t);
                }
                else if (!(t is Lex.Space))
                {
                    _source.Pushback();
                    break;
                }
            }
        }

        if (found.Count > 0)
        {
            DurationItem duration = Factory<DurationItem>(found[0]);
            duration.Tokens = found.Skip(1).ToList();
            item.Append(duration);
            PrevDuration = Durations.BaseScaling(found);
            SetDuration(item, PrevDuration);
        }
        else
        {
            SetDuration(item, PrevDuration);
        }
    }

    /// <summary>
    /// Appends the arguments between brackets to an item.
    /// </summary>
    /// <param name="item">The item to add to.</param>
    /// <param name="source">The tokens to read on with.</param>
    /// <returns>Whether brackets were found.</returns>
    public bool AddBracketed(Item item, IEnumerable<LyToken> source)
    {
        foreach (LyToken t in source)
        {
            if (t is LilyPondMode.OpenBracket)
            {
                var tokens = new List<LyToken> { t };
                item.Extend(Read(Consume(tokens.Add)));
                item.Tokens = tokens;
                return true;
            }

            if (!(t is Lex.Space))
            {
                _source.Pushback();
                break;
            }
        }

        return false;
    }

    private static void SetDuration(Item item, (Fraction Base, Fraction Scaling) duration)
    {
        switch (item)
        {
            case Durable durable:
                durable.Duration = duration;
                break;
            case Tremolo tremolo:
                tremolo.Duration = duration;
                break;
            case Tempo tempo:
                tempo.Duration = duration;
                break;
            case Partial partial:
                partial.Duration = duration;
                break;
        }
    }

    private static HashSet<Type> ItemTypesOf(Slexing.Parser parser)
        => new HashSet<Type>(parser.ItemRules.Select(r => r.TokenClass));

    private static TMethod Lookup<TMethod>(
        Dictionary<Type, TMethod> table, string name, LyToken token)
        where TMethod : class
    {
        Type type = token.GetType();
        object cached = Resolved.GetOrAdd(
            (name, type),
            _ =>
            {
                for (Type c = type; c != null; c = c.BaseType)
                {
                    if (table.TryGetValue(c, out TMethod found)) { return found; }

                    if (c == typeof(LyToken)) { break; }
                }

                return null;
            });

        return (TMethod)cached;
    }

    private static void RegisterTokenClasses()
    {
        void Add(Type type, Func<Reader, LyToken, IEnumerable<LyToken>, Item> method)
            => TokenClasses[type] = method;

        Add(typeof(LilyPondMode.SchemeStart), (r, t, s) => r.ReadSchemeItem(t));
        Add(typeof(Lex.StringStart), (r, t, s) => r.Factory<StringItem>(t, consume: true));
        Add(typeof(LilyPondMode.DecimalValue), (r, t, s) => r.Factory<Number>(t));
        Add(typeof(LilyPondMode.IntegerValue), (r, t, s) => r.Factory<Number>(t));
        Add(typeof(LilyPondMode.Fraction), (r, t, s) => r.Factory<Number>(t));
        Add(typeof(LilyPondMode.MusicItem), (r, t, s) => r.ReadMusicItem(t, s));
        Add(typeof(LilyPondMode.Length), (r, t, s) => r.HandleLength(t, s));
        Add(typeof(LilyPondMode.ChordStart), (r, t, s) => r.HandleChordStart(t, s));
        Add(typeof(LilyPondMode.OpenBracket), (r, t, s) => r.HandleMusicList(t));
        Add(typeof(LilyPondMode.OpenSimultaneous), (r, t, s) => r.HandleMusicList(t));
        Add(typeof(LilyPondMode.SimultaneousOrSequentialCommand), (r, t, s) => r.HandleMusicList(t));
        Add(typeof(LilyPondMode.Command), (r, t, s) => r.ReadCommand(t, s));

        //Upstream's MarkupStart(Markup, Command) reaches read_command through
        //its SECOND base; the lex port carries only the first, so the reader
        //registers the class itself (see the note on LilyPondMode.MarkupStart).
        Add(typeof(LilyPondMode.MarkupStart), (r, t, s) => r.ReadCommand(t, s));
        Add(typeof(LilyPondMode.Keyword), (r, t, s) => r.ReadKeyword(t, s));
        Add(typeof(LilyPondMode.UserCommand), (r, t, s) => r.Factory<UserCommand>(t));
        Add(typeof(LilyPondMode.ChordSeparator), (r, t, s) => r.ReadChordSpecifier(t));
        Add(typeof(LilyPondMode.TremoloColon), (r, t, s) => r.ReadTremolo(t));
        Add(typeof(LilyPondMode.Name), (r, t, s) => r.ReadAssignment(t));
        Add(typeof(LilyPondMode.ContextProperty), (r, t, s) => r.ReadAssignment(t));
        Add(typeof(LilyPondMode.PaperVariable), (r, t, s) => r.HandleVariableAssignment(t));
        Add(typeof(LilyPondMode.LayoutVariable), (r, t, s) => r.HandleVariableAssignment(t));
        Add(typeof(LilyPondMode.HeaderVariable), (r, t, s) => r.HandleVariableAssignment(t));
        Add(typeof(LilyPondMode.UserVariable), (r, t, s) => r.HandleVariableAssignment(t));
        Add(typeof(LilyPondMode.Direction), (r, t, s) => r.HandleDirection(t, s));
        Add(typeof(LilyPondMode.Slur), (r, t, s) => r.HandleSlurs(t));
        Add(typeof(LilyPondMode.Beam), (r, t, s) => r.HandleBeam(t));
        Add(typeof(LilyPondMode.Articulation), (r, t, s) => r.Factory<Articulation>(t));

        foreach (KeyValuePair<Type, Func<Item>> pair in DirectItems)
        {
            Func<Item> make = pair.Value;
            Add(pair.Key, (r, t, s) => r.MakeDirect(make, t));
        }
    }

    private static void RegisterCommands()
    {
        void Add(Action<Reader, LyToken, IEnumerable<LyToken>, Box> method, params string[] names)
        {
            foreach (string name in names) { Commands[name] = method; }
        }

        Add((r, t, s, b) => b.Item = r.HandleRelative(t, s), "\\relative");
        Add((r, t, s, b) => b.Item = r.HandleAbsolute(t, s), "\\absolute");
        Add((r, t, s, b) => b.Item = r.HandleTranspose(t, s), "\\transpose");
        Add((r, t, s, b) => b.Item = r.HandleClef(t, s), "\\clef");
        Add((r, t, s, b) => b.Item = r.HandleKey(t, s), "\\key");
        Add(
            (r, t, s, b) => b.Item = r.HandleScaler(t, s),
            "\\times", "\\tuplet", "\\scaleDurations");
        Add(
            (r, t, s, b) => b.Item = r.HandleTag(t),
            "\\tag", "\\keepWithTag", "\\removeWithTag", "\\appendToTag", "\\pushToTag");
        Add(
            (r, t, s, b) => b.Item = r.HandleGrace(t, s),
            "\\grace", "\\acciaccatura", "\\appoggiatura", "\\slashedGrace");
        Add((r, t, s, b) => b.Item = r.HandleAfterGrace(t, s), "\\afterGrace");
        Add((r, t, s, b) => b.Item = r.HandleRepeat(t, s), "\\repeat");
        Add((r, t, s, b) => b.Item = r.HandleAlternative(t, s), "\\alternative");
        Add((r, t, s, b) => b.Item = r.HandleTempo(t), "\\tempo");
        Add((r, t, s, b) => b.Item = r.HandleTime(t, s), "\\time");
        Add((r, t, s, b) => b.Item = r.HandlePartial(t, s), "\\partial");
        Add((r, t, s, b) => b.Item = r.HandleTranslator(t, s), "\\new", "\\context", "\\change");
        Add((r, t, s, b) => b.Item = r.HandleStringTuning(t, s), "\\stringTuning");
        Add((r, t, s, b) => b.Item = r.HandlePartCombine(t), "\\partcombine");
        Add(
            (r, t, s, b) => b.Item = r.HandleMarkup(t),
            "\\markup", "\\markuplist", "\\markuplines");

        foreach (string name in InputModeCommands.Keys.ToList())
        {
            Add((r, t, s, b) => b.Item = r.HandleInputMode(t), name);
        }

        foreach (string name in LyricModeCommands.Keys.ToList())
        {
            Add((r, t, s, b) => b.Item = r.HandleLyricMode(t, s), name);
        }
    }

    private static void RegisterKeywords()
    {
        void Add(Action<Reader, LyToken, IEnumerable<LyToken>, Box> method, params string[] names)
        {
            foreach (string name in names) { Keywords[name] = method; }
        }

        Add((r, t, s, b) => b.Item = r.HandleLanguage(t, s), "\\language");
        Add((r, t, s, b) => b.Item = r.HandleInclude(t, s), "\\include");
        Add((r, t, s, b) => b.Item = r.HandleVersion(t, s), "\\version");
        Add((r, t, s, b) => b.Item = r.HandleSet(t, s), "\\set");
        Add((r, t, s, b) => b.Item = r.HandleUnset(t), "\\unset");
        Add((r, t, s, b) => b.Item = r.HandleOverride(t), "\\override");
        Add((r, t, s, b) => b.Item = r.HandleRevert(t), "\\revert");
        Add((r, t, s, b) => b.Item = r.HandleTweak(t), "\\tweak");

        foreach (string name in BracketedKeywords.Keys.ToList())
        {
            Add((r, t, s, b) => b.Item = r.HandleBracketed(t, s), name);
        }
    }

    private static void RegisterMarkup()
    {
        MarkupClasses[typeof(LilyPondMode.SchemeStart)] = (r, t) => r.ReadSchemeItem(t);
        MarkupClasses[typeof(Lex.StringStart)] = (r, t) => r.Factory<StringItem>(t, consume: true);
        MarkupClasses[typeof(LilyPondMode.MarkupScore)] = (r, t) => r.HandleMarkupScore(t);
        MarkupClasses[typeof(LilyPondMode.MarkupCommand)] = (r, t) => r.HandleMarkupCommand(t);
        MarkupClasses[typeof(LilyPondMode.MarkupUserCommand)]
            = (r, t) => r.Factory<MarkupUserCommand>(t);
        MarkupClasses[typeof(LilyPondMode.OpenBracketMarkup)]
            = (r, t) => r.HandleMarkupOpenBracket(t);
        MarkupClasses[typeof(LilyPondMode.MarkupWord)] = (r, t) => r.Factory<MarkupWord>(t);
    }

    private static void RegisterScheme()
    {
        SchemeClasses[typeof(Lex.StringStart)] = (r, t) => r.Factory<StringItem>(t, consume: true);
        SchemeClasses[typeof(SchemeMode.Quote)] = (r, t) => r.HandleSchemeQuote(t);
        SchemeClasses[typeof(SchemeMode.OpenParen)] = (r, t) => r.HandleSchemeOpenParenthesis(t);
        SchemeClasses[typeof(SchemeMode.LilyPondStart)] = (r, t) => r.HandleSchemeLilyPondStart(t);
        foreach (Type type in new[]
        {
            typeof(SchemeMode.Dot), typeof(SchemeMode.Bool), typeof(SchemeMode.Char),
            typeof(SchemeMode.Word), typeof(SchemeMode.Number), typeof(SchemeMode.Fraction),
            typeof(SchemeMode.Float),
        })
        {
            SchemeClasses[type] = (r, t) => r.Factory<SchemeItem>(t);
        }
    }

    private Item MakeDirect(Func<Item> make, LyToken token)
    {
        Item item = make();
        item.SourceDocument = _source.Document;
        item.Token = token;
        item.Position = token.Pos;
        return item;
    }

    private Item HandleLength(LyToken t, IEnumerable<LyToken> source)
    {
        Unpitched item = Factory<Unpitched>(position: t.Pos);
        AddDuration(item, t, source);
        return item;
    }

    private Item HandleChordStart(LyToken t, IEnumerable<LyToken> source)
    {
        if (InChord) { return null; }

        InChord = true;
        Chord chord = Factory<Chord>(t);
        var tokens = new List<LyToken>();
        chord.Extend(Read(Consume(tokens.Add)));
        chord.Tokens = new List<LyToken>(chord.Tokens.Concat(tokens));
        InChord = false;
        AddDuration(chord, null, source);
        return chord;
    }

    private Item HandleMusicList(LyToken t)
    {
        (Item item, IEnumerable<LyToken> tokens) = TestMusicList(t);
        if (item == null) { return null; }

        if (tokens != null) { item.Extend(Read(tokens)); }

        return item;
    }

    private Item ReadCommand(LyToken t, IEnumerable<LyToken> source)
    {
        if (Commands.TryGetValue(t.Text, out var method))
        {
            var box = new Box();
            method(this, t, source, box);
            return box.Item;
        }

        return Factory<Command>(t);
    }

    private Item ReadKeyword(LyToken t, IEnumerable<LyToken> source)
    {
        if (Keywords.TryGetValue(t.Text, out var method))
        {
            var box = new Box();
            method(this, t, source, box);
            return box.Item;
        }

        return Factory<Keyword>(t);
    }

    private Item ReadChordSpecifier(LyToken t)
    {
        ChordSpecifier item = Factory<ChordSpecifier>(position: t.Pos);
        item.Append(Factory<ChordItem>(t));
        foreach (LyToken token in Consume())
        {
            if (token is LilyPondMode.ChordItem)
            {
                item.Append(Factory<ChordItem>(token));
            }
            else if (token is LilyPondMode.Note)
            {
                if (PitchTable.PitchReaderFor(Language)
                    .TryRead(token.Text, out int note, out Fraction alter))
                {
                    Note noteItem = Factory<Note>(token);
                    noteItem.Pitch = new Pitching.Pitch(note, alter);
                    item.Append(noteItem);
                }
            }
        }

        return item;
    }

    private Item ReadTremolo(LyToken t)
    {
        Tremolo item = Factory<Tremolo>(t);
        foreach (LyToken token in _source)
        {
            if (token is LilyPondMode.TremoloDuration)
            {
                item.Append(Factory<DurationItem>(token));
                item.Duration = Durations.BaseScalingString(token.Text);
            }
            else
            {
                _source.Pushback();
            }

            break;
        }

        return item;
    }

    private Item HandleVariableAssignment(LyToken t)
    {
        Item item = ReadAssignment(t);
        if (item == null) { return null; }

        //Handle \pt, \in and the other units.
        foreach (LyToken token in Skip(_source))
        {
            if (token is LilyPondMode.Unit)
            {
                item.Append(Factory<Command>(token));
            }
            else
            {
                _source.Pushback();
            }

            break;
        }

        return item;
    }

    private Item HandleDirection(LyToken t, IEnumerable<LyToken> source)
    {
        Postfix item = Factory<Postfix>(t);
        item.Direction = "_-^".IndexOf(t.Text, StringComparison.Ordinal) - 1;
        foreach (LyToken token in Skip(source))
        {
            if (token is Lex.StringStart
                || token is LilyPondMode.MarkupStart
                || token is LilyPondMode.Articulation
                || token is LilyPondMode.Slur
                || token is LilyPondMode.Beam
                || token is LilyPondMode.Dynamic)
            {
                AppendRead(item, token);
            }

            //Upstream writes `t in ('\\tag')` — a STRING, not a tuple, so this
            //is a substring test, and it is kept as one.
            else if (token is LilyPondMode.Command && "\\tag".Contains(token.Text))
            {
                AppendRead(item, token);
            }
            else if (token is LilyPondMode.Keyword && "\\tweak".Contains(token.Text))
            {
                AppendRead(item, token);
            }
            else
            {
                _source.Pushback();
            }

            break;
        }

        return item;
    }

    private Item HandleSlurs(LyToken t)
    {
        bool phrasing = t.Text.StartsWith("\\", StringComparison.Ordinal);
        string kind = t.Text.EndsWith("(", StringComparison.Ordinal) ? "start" : "stop";
        if (phrasing)
        {
            PhrasingSlur item = Factory<PhrasingSlur>(t);
            item.Event = kind;
            return item;
        }

        Slur slur = Factory<Slur>(t);
        slur.Event = kind;
        return slur;
    }

    private Item HandleBeam(LyToken t)
    {
        Beam item = Factory<Beam>(t);
        item.Event = t.Text == "[" ? "start" : "stop";
        return item;
    }

    private Item ReadAssignment(LyToken t)
    {
        Assignment item = Factory<Assignment>(t);
        foreach (LyToken token in Skip(_source))
        {
            if (token is LilyPondMode.Variable
                || token is LilyPondMode.UserVariable
                || token is LilyPondMode.DotPath)
            {
                item.Append(Factory<PathItem>(token));
            }
            else if (token is LilyPondMode.EqualSign)
            {
                item.Tokens = new List<LyToken> { token };
                foreach (Item i in Read())
                {
                    item.Append(i);
                    break;
                }

                return item;
            }
            else if (token is LilyPondMode.SchemeStart)
            {
                //Accept only one scheme item; if another is found, answer the
                //first and discard the assignment (should not normally happen).
                foreach (Scheme s in item.Find<Scheme>())
                {
                    _source.Pushback();
                    return s;
                }

                item.Append(ReadSchemeItem(token));
            }
            else
            {
                _source.Pushback();
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Tests whether a music list starts here, handling <c>\simultaneous</c>
    /// and <c>\sequential</c> too.
    /// </summary>
    private (Item Item, IEnumerable<LyToken> Tokens) TestMusicList(LyToken t)
    {
        (Item, IEnumerable<LyToken>) MakeMusicList(
            LyToken start, bool simultaneous, IReadOnlyList<LyToken> tokens = null)
        {
            MusicList list = Factory<MusicList>(start);
            list.Simultaneous = simultaneous;
            list.Tokens = tokens ?? new List<LyToken>();
            return (list, Consume(token => list.Tokens = new List<LyToken>(list.Tokens) { token }));
        }

        if (t is LilyPondMode.OpenBracket || t is LilyPondMode.OpenSimultaneous)
        {
            return MakeMusicList(t, t.Text == "<<");
        }

        if (t is LilyPondMode.SimultaneousOrSequentialCommand)
        {
            foreach (LyToken t1 in Skip(_source))
            {
                if (t1 is LilyPondMode.OpenBracket || t1 is LilyPondMode.OpenSimultaneous)
                {
                    return MakeMusicList(
                        t,
                        t.Text == "\\simultaneous" || t1.Text == "<<",
                        new List<LyToken> { t1 });
                }

                _source.Pushback();
                return (Factory<Keyword>(t), null);
            }
        }

        return (null, null);
    }

    private Item ReadMusicItem(LyToken t, IEnumerable<LyToken> source)
    {
        Item item = null;
        bool inPitchCommand = _source.State.CurrentParser() is LilyPondMode.ParsePitchCommand;
        if (t.GetType() == typeof(LilyPondMode.Note))
        {
            if (PitchTable.PitchReaderFor(Language)
                .TryRead(t.Text, out int note, out Fraction alter))
            {
                Note noteItem = Factory<Note>(t);
                var pitch = new Pitching.Pitch(note, alter);
                noteItem.Pitch = pitch;
                item = noteItem;
                foreach (LyToken token in source)
                {
                    if (token is LilyPondMode.Octave)
                    {
                        pitch.Octave = PitchTable.OctaveToNum(token.Text);
                        noteItem.OctaveToken = token;
                    }
                    else if (token is LilyPondMode.Accidental)
                    {
                        noteItem.AccidentalToken = token;
                        pitch.Accidental = token.Text;
                    }
                    else if (token is LilyPondMode.OctaveCheck)
                    {
                        pitch.Octavecheck = PitchTable.OctaveToNum(token.Text);
                        noteItem.OctavecheckToken = token;
                        break;
                    }
                    else if (!(token is Lex.Space))
                    {
                        _source.Pushback();
                        break;
                    }
                }
            }
        }
        else
        {
            //Upstream indexes a dict here and would raise for an unlisted
            //MusicItem subclass; answering nothing is the same for every
            //document that tokenizes.
            item = t switch
            {
                LilyPondMode.Rest _ => Factory<Rest>(t),
                LilyPondMode.Spacer _ => (Item)Factory<Skip>(t),
                LilyPondMode.Skip _ => Factory<Skip>(t),
                LilyPondMode.Q _ => Factory<Q>(t),
                LilyPondMode.DrumNote _ => Factory<DrumNote>(t),
                _ => null,
            };
        }

        if (item != null && !InChord && !inPitchCommand)
        {
            AddDuration(item, null, source);
        }

        return item;
    }

    private Item HandleRelative(LyToken t, IEnumerable<LyToken> source)
    {
        Relative item = Factory<Relative>(t);

        //Get one pitch and exit on a non-comment.
        bool pitchFound = false;
        foreach (Item i in Read(source))
        {
            item.Append(i);
            if (!pitchFound && i is Note)
            {
                pitchFound = true;
                continue;
            }

            break;
        }

        return item;
    }

    private Item HandleAbsolute(LyToken t, IEnumerable<LyToken> source)
        => AppendOne(Factory<Absolute>(t), source);

    private Item HandleTranspose(LyToken t, IEnumerable<LyToken> source)
    {
        Transpose item = Factory<Transpose>(t);

        //Get two pitches.
        int pitchesFound = 0;
        foreach (Item i in Read(source))
        {
            item.Append(i);
            if (pitchesFound < 2 && i is Note)
            {
                pitchesFound += 1;
                continue;
            }

            break;
        }

        return item;
    }

    private Item HandleClef(LyToken t, IEnumerable<LyToken> source)
    {
        Clef item = Factory<Clef>(t);
        foreach (LyToken token in Skip(source))
        {
            if (token is LilyPondMode.ClefSpecifier)
            {
                item.SpecifierValue = token;
            }
            else if (token is Lex.StringStart)
            {
                item.SpecifierValue = Factory<StringItem>(token, consume: true);
            }

            break;
        }

        return item;
    }

    private Item HandleKey(LyToken t, IEnumerable<LyToken> source)
    {
        KeySignature item = Factory<KeySignature>(t);
        item.Extend(Read(source).Take(2));
        return item;
    }

    private Item HandleScaler(LyToken t, IEnumerable<LyToken> source)
    {
        Scaler item = Factory<Scaler>(t);
        item.Scaling = Fraction.One;
        if (t.Text == "\\scaleDurations")
        {
            foreach (Item i in Read(source))
            {
                item.Append(i);
                if (i is Number number)
                {
                    object value = number.Value();
                    if (value is int integer) { item.Scaling = new Fraction(integer); }
                    else if (value is Fraction fraction) { item.Scaling = fraction; }
                }
                else if (i is Scheme scheme)
                {
                    (int First, int Second)? pair = scheme.GetPairInts();
                    if (pair != null && pair.Value.Second != 0)
                    {
                        item.Scaling = new Fraction(pair.Value.First, pair.Value.Second);
                    }
                    else if (pair == null)
                    {
                        Fraction? value = scheme.GetFraction();
                        if (value != null) { item.Scaling = value.Value; }
                    }
                }

                break;
            }
        }
        else if (t.Text == "\\tuplet")
        {
            foreach (LyToken token in source)
            {
                if (token is LilyPondMode.Fraction)
                {
                    item.Append(Factory<Number>(token));
                    (item.Numerator, item.Denominator) = SplitFraction(token.Text);
                    item.Scaling = Fraction.One / Fraction.Parse(token.Text);
                }
                else if (token is LilyPondMode.Duration)
                {
                    AddDuration(item, token, source);
                    break;
                }
                else if (!(token is Lex.Space))
                {
                    _source.Pushback();
                    break;
                }
            }
        }
        else
        {
            //t == "\\times"
            foreach (LyToken token in source)
            {
                if (token is LilyPondMode.Fraction)
                {
                    item.Append(Factory<Number>(token));
                    (item.Numerator, item.Denominator) = SplitFraction(token.Text);
                    item.Scaling = Fraction.Parse(token.Text);
                    break;
                }

                if (!(token is Lex.Space))
                {
                    _source.Pushback();
                    break;
                }
            }
        }

        return AppendOne(item, source);
    }

    private Item HandleTag(LyToken t)
    {
        Tag item = Factory<Tag>(t);
        int argCount = t.Text == "\\appendToTag" || t.Text == "\\pushToTag" ? 3 : 2;
        item.Extend(Read().Take(argCount));
        return item;
    }

    private Item HandleGrace(LyToken t, IEnumerable<LyToken> source)
        => AppendOne(Factory<Grace>(t), source);

    private Item HandleAfterGrace(LyToken t, IEnumerable<LyToken> source)
    {
        AfterGrace item = Factory<AfterGrace>(t);
        item.Extend(Read(source).Take(2));

        //Put the grace music in a Grace item.
        if (item.Count > 1)
        {
            var last = (Item)item[item.Count - 1];
            Grace grace = Factory<Grace>(position: last.Position);
            grace.Append(last);
            item.Append(grace);
        }

        return item;
    }

    private Item HandleRepeat(LyToken t, IEnumerable<LyToken> source)
    {
        Repeat item = Factory<Repeat>(t);
        item.SpecifierValue = null;
        item.RepeatCountValue = null;
        foreach (LyToken token in Skip(source))
        {
            if (token is LilyPondMode.RepeatSpecifier)
            {
                item.SpecifierValue = token;
            }

            //Upstream's `not item.specifier` tests a BOUND METHOD, always
            //truthy, so its string-specifier branch never runs; kept dead.
            else if (token is LilyPondMode.RepeatCount)
            {
                item.RepeatCountValue = token;
            }
            else if (token is LilyPondMode.SchemeStart)
            {
                //The specifier or the count may be given in scheme.
                Item s = ReadSchemeItem(token);
                if (item.SpecifierValue != null)
                {
                    if (item.RepeatCountValue != null)
                    {
                        item.Append(s);
                        break;
                    }

                    item.RepeatCountValue = s;
                }
                else
                {
                    item.SpecifierValue = s;
                }
            }
            else
            {
                _source.Pushback();
                foreach (Item i in Read(source))
                {
                    item.Append(i);
                    break;
                }

                foreach (LyToken next in Skip(source))
                {
                    if (next.Text == "\\alternative" && next is LilyPondMode.Command)
                    {
                        item.Append(HandleAlternative(next, source));
                    }
                    else
                    {
                        _source.Pushback();
                    }

                    break;
                }

                break;
            }
        }

        return item;
    }

    private Item HandleAlternative(LyToken t, IEnumerable<LyToken> source)
        => AppendOne(Factory<Alternative>(t), source);

    private Item HandleTempo(LyToken t)
    {
        Tempo item = Factory<Tempo>(t);
        IEnumerable<LyToken> source = Consume();
        bool equalSignSeen = false;
        bool textSeen = false;
        LyToken last = null;
        foreach (LyToken token in source)
        {
            last = token;
            if (!equalSignSeen)
            {
                if (!textSeen
                    && (token is LilyPondMode.SchemeStart
                        || token is Lex.StringStart
                        || token is LilyPondMode.Markup))
                {
                    item.Append(ReadItem(token));
                    last = null;
                    textSeen = true;
                }
                else if (token is LilyPondMode.Length)
                {
                    AddDuration(item, token, source);
                    last = null;
                }
                else if (token is LilyPondMode.EqualSign)
                {
                    item.Tokens = new List<LyToken> { token };
                    equalSignSeen = true;
                    last = null;
                }
            }
            else if (token is LilyPondMode.IntegerValue || token is LilyPondMode.SchemeStart)
            {
                item.Append(ReadItem(token));
            }
            else if (token.Text == "-")
            {
                item.Tokens = new List<LyToken>(item.Tokens) { token };
            }
        }

        //When the last token no longer belongs to the \tempo expression, push
        //it back.
        if (last != null && !(last is Lex.Space) && !(last is Lex.Comment))
        {
            _source.Pushback();
        }

        return item;
    }

    private Item HandleTime(LyToken t, IEnumerable<LyToken> source)
    {
        TimeSignature item = Factory<TimeSignature>(t);
        foreach (LyToken token in Skip(source))
        {
            if (token is LilyPondMode.SchemeStart)
            {
                item.BeatStructureValue = ReadSchemeItem(token);
                continue;
            }

            if (token is LilyPondMode.Fraction)
            {
                (int num, int den) = SplitFraction(token.Text);
                item.NumeratorValue = num;
                item.FractionValue = new Fraction(1, den);
            }
            else
            {
                _source.Pushback();
            }

            break;
        }

        return item;
    }

    private Item HandlePartial(LyToken t, IEnumerable<LyToken> source)
    {
        Partial item = Factory<Partial>(t);
        AddDuration(item, null, source);
        return item;
    }

    private Item HandleTranslator(LyToken t, IEnumerable<LyToken> source)
    {
        bool change = t.Text == "\\change";
        Item item = change ? Factory<Change>(t) : (Item)Factory<ContextItem>(t);
        IEnumerable<LyToken> inner = Consume();
        foreach (LyToken token in Skip(inner))
        {
            if (token is LilyPondMode.ContextName || token is LilyPondMode.Name)
            {
                SetContext(item, token);
                foreach (LyToken next in inner)
                {
                    if (next is LilyPondMode.EqualSign)
                    {
                        foreach (LyToken idToken in inner)
                        {
                            if (idToken is Lex.StringStart)
                            {
                                SetContextId(item, Factory<StringItem>(idToken, consume: true));
                                break;
                            }

                            if (idToken is LilyPondMode.Name)
                            {
                                SetContextId(item, idToken);
                                break;
                            }

                            if (!(idToken is Lex.Space))
                            {
                                _source.Pushback();
                                break;
                            }
                        }
                    }
                    else if (!(next is Lex.Space))
                    {
                        _source.Pushback();
                        break;
                    }
                }
            }
            else
            {
                _source.Pushback();
            }

            break;
        }

        if (!change)
        {
            foreach (Item i in Read(source))
            {
                item.Append(i);
                if (!(i is With)) { break; }
            }
        }

        return item;
    }

    private Item HandleInputMode(LyToken t)
    {
        Item item = MakeDirect(InputModeCommands[t.Text], t);
        foreach (Item i in Read())
        {
            item.Append(i);
            break;
        }

        return item;
    }

    private Item HandleLyricMode(LyToken t, IEnumerable<LyToken> source)
    {
        Item item = MakeDirect(LyricModeCommands[t.Text], t);
        if (item is LyricsTo lyricsTo)
        {
            foreach (LyToken token in Skip(source))
            {
                if (token is LilyPondMode.Name)
                {
                    lyricsTo.ContextIdValue = token;
                }
                else if (token is Lex.StringBase || token is LilyPondMode.SchemeStart)
                {
                    lyricsTo.ContextIdValue = ReadItem(token);
                }
                else
                {
                    _source.Pushback();
                }

                break;
            }
        }

        foreach (LyToken token in Skip(Consume()))
        {
            Item i = ReadLyricItem(token) ?? ReadItem(token);
            if (i != null) { item.Append(i); }

            break;
        }

        return item;
    }

    /// <summary>Reads one lyric item; answers nothing for other tokens.</summary>
    private Item ReadLyricItem(LyToken t)
    {
        if (t is Lex.StringStart || t is LilyPondMode.MarkupStart)
        {
            LyricText item = Factory<LyricText>(position: t.Pos);
            item.Append(ReadItem(t));
            AddDuration(item);
            return item;
        }

        if (t is LilyPondMode.LyricText)
        {
            LyricText item = Factory<LyricText>(t);
            AddDuration(item);
            return item;
        }

        if (t is LilyPondMode.Lyric) { return Factory<LyricItem>(t); }

        (Item list, IEnumerable<LyToken> source) = TestMusicList(t);
        if (list == null) { return null; }

        if (source != null)
        {
            foreach (LyToken token in Skip(source))
            {
                Item i = ReadLyricItem(token) ?? ReadItem(token);
                if (i != null) { list.Append(i); }
            }
        }

        return list;
    }

    private Item HandleStringTuning(LyToken t, IEnumerable<LyToken> source)
        => AppendOne(Factory<StringTuning>(t), source);

    private Item HandlePartCombine(LyToken t)
    {
        PartCombine item = Factory<PartCombine>(t);
        item.Extend(Read().Take(2));
        return item;
    }

    private Item HandleLanguage(LyToken t, IEnumerable<LyToken> source)
    {
        Language item = Factory<Language>(t);
        foreach (Item name in Read(source))
        {
            item.Append(name);
            if (name is StringItem text)
            {
                string value = text.Value();
                item.LanguageName = value;
                if (Array.IndexOf(PitchTable.Languages, value) >= 0) { Language = value; }
            }

            break;
        }

        return item;
    }

    private Item HandleInclude(LyToken t, IEnumerable<LyToken> source)
    {
        Item item = null;
        Item name = null;
        foreach (Item read in Read(source))
        {
            name = read;
            if (read is StringItem text)
            {
                string value = text.Value();
                if (value.EndsWith(".ly", StringComparison.Ordinal))
                {
                    string language = value.Substring(0, value.Length - 3);
                    if (Array.IndexOf(PitchTable.Languages, language) >= 0)
                    {
                        Language languageItem = Factory<Language>(t);
                        languageItem.LanguageName = language;
                        Language = language;
                        languageItem.Append(read);
                        item = languageItem;
                    }
                }
            }

            break;
        }

        if (item == null)
        {
            Include include = Factory<Include>(t);
            if (name != null) { include.Append(name); }

            item = include;
        }

        return item;
    }

    private Item HandleVersion(LyToken t, IEnumerable<LyToken> source)
        => AppendOne(Factory<Version>(t), source);

    private Item HandleBracketed(LyToken t, IEnumerable<LyToken> source)
    {
        Item item = MakeDirect(BracketedKeywords[t.Text], t);
        if (!AddBracketed(item, source) && t.Text == "\\with")
        {
            //\with also supports one other argument instead of { … }
            AppendOne(item, source);
        }

        return item;
    }

    private Item HandleSet(LyToken t, IEnumerable<LyToken> source)
    {
        Set item = Factory<Set>(t);
        var tokens = new List<LyToken>();
        foreach (LyToken token in Skip(source))
        {
            tokens.Add(token);
            if (token is LilyPondMode.EqualSign)
            {
                item.Tokens = tokens;
                foreach (Item i in Read(source))
                {
                    item.Append(i);
                    break;
                }

                break;
            }
        }

        return item;
    }

    private Item HandleUnset(LyToken t)
    {
        Unset item = Factory<Unset>(t);
        var tokens = new List<LyToken>();
        foreach (LyToken token in Skip(Consume()))
        {
            if (!UnsetItems.Value.Contains(token.GetType()))
            {
                _source.Pushback();
                break;
            }

            tokens.Add(token);
        }

        item.Tokens = tokens;
        return item;
    }

    private Item HandleOverride(LyToken t)
    {
        Override item = Factory<Override>(t);
        foreach (LyToken token in Skip(Consume()))
        {
            if (token is Lex.StringStart || token is LilyPondMode.SchemeStart)
            {
                item.Append(ReadItem(token));
            }
            else if (token is LilyPondMode.EqualSign)
            {
                item.Tokens = new List<LyToken> { token };
                foreach (Item i in Read())
                {
                    item.Append(i);
                    break;
                }

                break;
            }
            else
            {
                item.Append(Factory<PathItem>(token));
            }
        }

        return item;
    }

    private Item HandleRevert(LyToken t)
    {
        Revert item = Factory<Revert>(t);
        LyToken last = null;
        foreach (LyToken token in Skip(Consume()))
        {
            last = token;
            if (RevertItems.Value.Contains(token.GetType()))
            {
                item.Append(Factory<PathItem>(token));
            }
            else
            {
                break;
            }
        }

        bool hasGrobProperty = item.Any(i => ((Item)i).Token is LilyPondMode.GrobProperty);
        if (last is LilyPondMode.SchemeStart && !hasGrobProperty)
        {
            item.Append(ReadSchemeItem(last));
        }
        else
        {
            _source.Pushback();
        }

        return item;
    }

    private Item HandleTweak(LyToken t)
    {
        Tweak item = Factory<Tweak>(t);
        LyToken last = null;
        foreach (LyToken token in Skip(Consume()))
        {
            last = token;
            if (TweakItems.Value.Contains(token.GetType()))
            {
                item.Append(Factory<PathItem>(token));
            }
            else
            {
                _source.Pushback();
                break;
            }
        }

        if (item.Count == 0 && last is LilyPondMode.SchemeStart)
        {
            item.Append(ReadSchemeItem(last));
        }

        foreach (Item i in Read())
        {
            item.Append(i);
            break;
        }

        return item;
    }

    private Item HandleMarkup(LyToken t)
    {
        Markup item = Factory<Markup>(t);
        AddMarkupArguments(item);
        return item;
    }

    /// <summary>Reads LilyPond markup, recursively.</summary>
    private Item ReadMarkup(LyToken t)
    {
        Func<Reader, LyToken, Item> method = Lookup(MarkupClasses, "markup", t);
        return method?.Invoke(this, t);
    }

    private Item HandleMarkupScore(LyToken t)
    {
        MarkupScore item = Factory<MarkupScore>(t);
        foreach (LyToken token in Consume())
        {
            if (token is LilyPondMode.OpenBracket)
            {
                item.Tokens = new List<LyToken> { token };
                item.Extend(Read(
                    Consume(last => item.Tokens = new List<LyToken>(item.Tokens) { last })));
                return item;
            }

            if (!(token is Lex.Space))
            {
                _source.Pushback();
                break;
            }
        }

        return item;
    }

    private Item HandleMarkupCommand(LyToken t)
    {
        MarkupCommand item = Factory<MarkupCommand>(t);
        AddMarkupArguments(item);
        return item;
    }

    private Item HandleMarkupOpenBracket(LyToken t)
    {
        MarkupList item = Factory<MarkupList>(t);
        AddMarkupArguments(item);
        return item;
    }

    private void AddMarkupArguments(Item item)
    {
        foreach (LyToken t in Consume())
        {
            Item i = ReadMarkup(t);
            if (i != null)
            {
                item.Append(i);
            }
            else if (item is MarkupList && t is LilyPondMode.CloseBracketMarkup)
            {
                item.Tokens = new List<LyToken> { t };
            }
        }
    }

    /// <summary>Reads a scheme expression, just after the # in LilyPond mode.</summary>
    private Item ReadSchemeItem(LyToken t)
    {
        Scheme item = Factory<Scheme>(t);
        foreach (LyToken token in Consume())
        {
            if (token is Lex.Space) { continue; }

            Item i = ReadScheme(token);
            if (i != null)
            {
                item.Append(i);
                break;
            }
        }

        return item;
    }

    private Item ReadScheme(LyToken t)
    {
        Func<Reader, LyToken, Item> method = Lookup(SchemeClasses, "scheme", t);
        return method?.Invoke(this, t);
    }

    private Item HandleSchemeQuote(LyToken t)
    {
        SchemeQuote item = Factory<SchemeQuote>(t);
        foreach (LyToken token in Consume())
        {
            if (token is Lex.Space) { continue; }

            Item i = ReadScheme(token);
            if (i != null)
            {
                item.Append(i);
                break;
            }
        }

        return item;
    }

    private Item HandleSchemeOpenParenthesis(LyToken t)
    {
        SchemeList item = Factory<SchemeList>(t);
        foreach (LyToken token in Consume(last => item.Tokens = new List<LyToken> { last }))
        {
            if (token is Lex.Space) { continue; }

            Item i = ReadScheme(token);
            if (i != null) { item.Append(i); }
        }

        return item;
    }

    private Item HandleSchemeLilyPondStart(LyToken t)
    {
        SchemeLily item = Factory<SchemeLily>(t);
        item.Extend(Read(Consume(last => item.Tokens = new List<LyToken> { last })));
        return item;
    }

    /// <summary>Appends what a token reads as, when anything claims it —
    /// upstream appends unconditionally and would fail on a token no handler
    /// answers for.</summary>
    private void AppendRead(Item item, LyToken token)
    {
        Item read = ReadItem(token);
        if (read != null) { item.Append(read); }
    }

    private Item AppendOne(Item item, IEnumerable<LyToken> source)
    {
        foreach (Item i in Read(source))
        {
            item.Append(i);
            break;
        }

        return item;
    }

    private static void SetContext(Item item, LyToken token)
    {
        switch (item)
        {
            case Translator translator:
                translator.ContextValue = token;
                break;
            case ContextItem context:
                context.ContextValue = token;
                break;
        }
    }

    private static void SetContextId(Item item, object value)
    {
        switch (item)
        {
            case Translator translator:
                translator.ContextIdValue = value;
                break;
            case ContextItem context:
                context.ContextIdValue = value;
                break;
        }
    }

    private static (int Numerator, int Denominator) SplitFraction(string text)
    {
        string[] parts = text.Split('/');
        return (
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    /// <summary>A cell the dispatch tables write their result into, standing in
    /// for python's returning handler methods.</summary>
    private sealed class Box
    {
        internal Item Item { get; set; }
    }

    /// <summary>
    /// One python generator OBJECT: every enumeration continues the same
    /// underlying walk instead of restarting it. The reader hands the same
    /// consume() result to nested loops the way upstream does, and restarting
    /// would re-run its body — re-entering the parser-end walk and firing its
    /// last-token callback again.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    private sealed class SharedEnumerable<T> : IEnumerable<T>
    {
        private readonly IEnumerator<T> _enumerator;

        internal SharedEnumerable(IEnumerable<T> source)
            => _enumerator = source.GetEnumerator();

        public IEnumerator<T> GetEnumerator() => new Proxy(_enumerator);

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();

        /// <summary>Hands out the shared enumerator without disposing it when a
        /// foreach over it ends.</summary>
        private sealed class Proxy : IEnumerator<T>
        {
            private readonly IEnumerator<T> _inner;

            internal Proxy(IEnumerator<T> inner) => _inner = inner;

            public T Current => _inner.Current;

            object System.Collections.IEnumerator.Current => Current;

            public bool MoveNext() => _inner.MoveNext();

            public void Reset() => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }
    }
}
