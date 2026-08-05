// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Parsing.Actions;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>A module stand-in: a bag of symbol-keyed bindings.</summary>
internal sealed class FakeModule
{
    /// <summary>Gets the bindings.</summary>
    public Dictionary<Symbol, object> Bindings { get; } = new Dictionary<Symbol, object>();
}

/// <summary>What <see cref="ScriptedParserHost.MakeSyntax"/> hands back, so a test can see the dispatch.</summary>
internal sealed class SyntaxMark
{
    /// <summary>Gets or sets the constructor's name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the arguments.</summary>
    public object[] Arguments { get; set; }

    /// <summary>
    /// Gets the properties set on it, in order.
    /// <para>
    /// A mark stands in for whatever the constructor returned, and several of those
    /// constructors return MUSIC that a later rule then reads and writes —
    /// <c>chord_body</c> makes an <c>event-chord</c> and <c>note_chord_element</c>
    /// immediately gives its elements a duration. So a mark carries properties for the
    /// same reason <see cref="MadeMusic"/> does.
    /// </para>
    /// </summary>
    public List<(string Name, object Value)> Properties { get; } = new List<(string, object)>();
}

/// <summary>What <see cref="ScriptedParserHost.MakeMusic"/> hands back.</summary>
internal sealed class MadeMusic
{
    /// <summary>Gets or sets the music type's name.</summary>
    public string Name { get; set; }

    /// <summary>Gets the properties set on it, in order.</summary>
    public List<(string Name, object Value)> Properties { get; } = new List<(string, object)>();
}

/// <summary>A token source over a fixed list, with the pushback queue the grammar needs.</summary>
internal sealed class TokenListInput : IParserInput
{
    private readonly Stack<ParserToken> _pushed = new Stack<ParserToken>();
    private readonly IReadOnlyList<ParserToken> _tokens;
    private int _index;

    internal TokenListInput(IReadOnlyList<ParserToken> tokens = null)
        => _tokens = tokens ?? new List<ParserToken>();

    public ParserToken Next()
    {
        if (_pushed.Count > 0)
        {
            return _pushed.Pop();
        }

        return _index < _tokens.Count
            ? _tokens[_index++]
            : new ParserToken(0, null, default);
    }

    public void PushExtraToken(ParserToken token) => _pushed.Push(token);
}

/// <summary>
/// A scripted <see cref="IParserHost"/> and <see cref="ILexerHost"/> in one object, so
/// a test can drive text through the scanner and the parser together.
/// <para>
/// It is honest in the small: scopes really stack, identifiers really resolve through
/// them, modules really copy — because that is the behaviour the header rules exist to
/// exercise. Everything that would need the Scheme layer is either scripted by the
/// test (keywords, evaluation results) or recorded for assertion (handler calls,
/// syntax dispatches). PARTIAL: a group's session implements its own IParserHost
/// additions in its own <c>ScriptedParserHost.RagN.cs</c> file.
/// </para>
/// </summary>
internal sealed partial class ScriptedParserHost : IParserHost, ILexerHost
{
    private readonly UnresolvedLexerHost _embeddedScheme = new UnresolvedLexerHost();

    // ------ scripted inputs ------

    /// <summary>Gets the keyword table: word (without backslash) to token name and value.</summary>
    public Dictionary<string, (string TokenName, object Value)> Keywords { get; }
        = new Dictionary<string, (string, object)>(StringComparer.Ordinal);

    /// <summary>Gets the results <see cref="EvalSchemeToken"/> answers, keyed by token text.</summary>
    public Dictionary<string, object> EvalResults { get; }
        = new Dictionary<string, object>(StringComparer.Ordinal);

    /// <summary>Gets the values <see cref="IsScore"/> answers true for.</summary>
    public HashSet<object> Scores { get; } = new HashSet<object>();

    /// <summary>Gets the values <see cref="IsMarkupFunction"/> answers true for.</summary>
    public HashSet<object> MarkupFunctions { get; } = new HashSet<object>();

    /// <summary>
    /// Gets the markup-command table: word (without backslash) to the token it names,
    /// the command procedure, and the command's ARGUMENT PREDICATES.
    /// <para>
    /// The predicates are the point. On reading <c>\bold</c> the lexer pushes one
    /// <c>EXPECT_*</c> token per predicate plus a terminating
    /// <c>EXPECT_NO_MORE_ARGS</c>, and RAG19's rules parse the arguments off those
    /// announcements. A command is <c>MARKUP_FUNCTION</c> or
    /// <c>MARKUP_LIST_FUNCTION</c> depending on which it returns, exactly as a real
    /// host answers from the markup-command tables in the vendored Scheme.
    /// </para>
    /// </summary>
    public Dictionary<string, (string TokenName, object Value, string[] Predicates)> MarkupCommands
    { get; } = new Dictionary<string, (string, object, string[])>(StringComparer.Ordinal);

    /// <summary>Gets the book values <see cref="BookHasPaper"/> answers true for.</summary>
    public HashSet<object> BooksWithPaper { get; } = new HashSet<object>();

    // ------ live state ------

    /// <summary>Gets the global bindings, below every scope.</summary>
    public FakeModule Globals { get; } = new FakeModule();

    /// <summary>Gets the scope stack; the last entry is the top.</summary>
    public List<FakeModule> Scopes { get; } = new List<FakeModule>();

    /// <inheritdoc/>
    public int ErrorLevel { get; set; }

    // ------ recordings ------

    /// <summary>Gets the handler calls, as (procedure, arguments).</summary>
    public List<(object Procedure, object[] Arguments)> Calls { get; }
        = new List<(object, object[])>();

    /// <summary>Gets the tokens evaluated through <see cref="EvalSchemeToken"/>.</summary>
    public List<object> EvaluatedTokens { get; } = new List<object>();

    /// <summary>Gets the lexer-mode operations, in order.</summary>
    public List<string> LexerModeOperations { get; } = new List<string>();

    /// <summary>Gets the path-keyed assignments <see cref="SetIdentifier"/> received.</summary>
    public List<(object Key, object Value)> PathAssignments { get; }
        = new List<(object, object)>();

    /// <summary>Gets the markup commands defined, as (name, function).</summary>
    public List<(object Name, object Function)> MarkupCommandsDefined { get; }
        = new List<(object, object)>();

    /// <summary>Gets the music objects <see cref="MakeMusic"/> handed back, in order.</summary>
    public List<MadeMusic> MadeMusicObjects { get; } = new List<MadeMusic>();

    /// <summary>
    /// Gets or sets whether <see cref="MakeMusic"/> answers a REAL
    /// <see cref="MusicObject"/> rather than a recording stand-in.
    /// <para>
    /// Off by default, because most action tests want to read back the property writes
    /// in order. It has to be ON for any test that parses real music text: the list
    /// helpers the surrounding grammar reaches (<c>reverse_music_list</c>,
    /// <c>post_event_cons</c>, <c>add_post_events</c>) are ported over the Engine's
    /// <see cref="MusicObject"/> directly — deliberately, see PORT-COVERAGE — so a
    /// stand-in cannot travel through them. The type name is written to the
    /// <c>name</c> property, which is where <see cref="Prob.Name"/> reads it from and
    /// where the music-descriptions table would have put it.
    /// </para>
    /// </summary>
    public bool MakeRealMusic { get; set; }

    /// <summary>Gets the real music objects made while <see cref="MakeRealMusic"/> was on.</summary>
    public List<MusicObject> RealMusicObjects { get; } = new List<MusicObject>();

    /// <summary>
    /// Gets or sets the scanner the mode pushes are FORWARDED to, or
    /// <see langword="null"/> to only record them.
    /// <para>
    /// Null by default, which is what the earlier groups' tests assert against. Set it
    /// and this host behaves like a real one for mode purposes: <c>\notemode { c4. }</c>
    /// then lexes its body in the NOTES start condition — where there is no
    /// <c>{REAL}</c> rule and <c>r</c> is a <c>RESTNAME</c> — instead of in the
    /// surrounding mode. Any test whose text is mode-sensitive needs this; without it
    /// the scanner never leaves INITIAL and the token stream is quietly the wrong one.
    /// </para>
    /// </summary>
    public ModalScanner Scanner { get; set; }

    // ------ IParserHost ------

    public object LookupIdentifier(string name)
    {
        Symbol symbol = Symbol.Intern(name);
        for (int i = Scopes.Count - 1; i >= 0; i--)
        {
            if (Scopes[i].Bindings.TryGetValue(symbol, out object scoped))
            {
                return scoped;
            }
        }

        return Globals.Bindings.TryGetValue(symbol, out object value)
            ? value
            : DefaultArgument.Instance;
    }

    public void SetIdentifier(object key, object value)
    {
        if (key is Symbol symbol)
        {
            FakeModule target = Scopes.Count > 0 ? Scopes[Scopes.Count - 1] : Globals;
            target.Bindings[symbol] = value;
            return;
        }

        PathAssignments.Add((key, value));
    }

    public object EvalSchemeToken(object token, SourceSpan location)
    {
        EvaluatedTokens.Add(token);
        return token is string text && EvalResults.TryGetValue(text, out object result)
            ? result
            : Unspecified.Instance;
    }

    public void PushNoteState() => PushMode("push-note-state", LexerState.Notes);

    public void PopLexerState()
    {
        LexerModeOperations.Add("pop-state");
        Scanner?.PopState();
    }

    /// <summary>Records a mode push and, when a scanner is attached, performs it.</summary>
    /// <param name="operation">The name recorded for assertions.</param>
    /// <param name="state">The start condition to push.</param>
    internal void PushMode(string operation, LexerState state)
    {
        LexerModeOperations.Add(operation);
        Scanner?.PushState(state);
    }

    public void AddScope(object module) => Scopes.Add((FakeModule)module);

    public object RemoveScope()
    {
        FakeModule top = Scopes[Scopes.Count - 1];
        Scopes.RemoveAt(Scopes.Count - 1);
        return top;
    }

    public object CurrentModule()
        => Scopes.Count > 0 ? Scopes[Scopes.Count - 1] : Globals;

    public bool IsModule(object value) => value is FakeModule;

    public object MakeModule() => new FakeModule();

    public void ModuleCopy(object destination, object source)
    {
        foreach (KeyValuePair<Symbol, object> binding in ((FakeModule)source).Bindings)
        {
            ((FakeModule)destination).Bindings[binding.Key] = binding.Value;
        }
    }

    public bool TryModuleVariable(object module, object name, out object value)
        => ((FakeModule)module).Bindings.TryGetValue((Symbol)name, out value);

    public object Call(object procedure, params object[] arguments)
    {
        Calls.Add((procedure, arguments));
        return CallBehavior != null
            ? CallBehavior(procedure, arguments)
            : Unspecified.Instance;
    }

    public object MakeSyntax(string constructor, SourceSpan location, params object[] arguments)
    {
        SyntaxMark mark = new SyntaxMark { Name = constructor, Arguments = arguments };

        // event-chord is the one constructor a LATER rule reads back:
        // `note_chord_element: chord_body ...` takes the elements list off the chord
        // body's result to give every element the chord's duration. The vendored
        // definition is (make-music 'EventChord 'elements mlist) — scm/
        // ly-syntax-constructors.scm 165 — so the mark carries that property, or the
        // rule would silently find nothing to give a duration to.
        if (string.Equals(constructor, "event-chord", StringComparison.Ordinal)
            && arguments.Length == 1)
        {
            mark.Properties.Add(("elements", arguments[0]));
        }

        SyntaxDispatches.Add(mark);

        // composed-markup-list is the SECOND constructor a later rule reads back, and
        // it is read back harder than event-chord: `markup: markup_head_1_list
        // simple_markup` takes its CAR. A mark would fail that cast, so the host
        // reproduces the constructor's list surgery for real — see ComposeMarkupList,
        // which is pure consing and needs no interpreter. The mark is still recorded
        // above, so a test can assert the dispatch AND the composed result.
        if (string.Equals(constructor, "composed-markup-list", StringComparison.Ordinal)
            && arguments.Length == 2)
        {
            return ComposeMarkupList(arguments[0], arguments[1]);
        }

        return mark;
    }

    public object LocOnCopy(object value, SourceSpan location) => value;

    public object MakeMusic(string name, SourceSpan location)
    {
        if (MakeRealMusic)
        {
            MusicObject real = new MusicObject(Nil.Instance);
            real.SetProperty("name", Symbol.Intern(name));
            real.SetSpot(location);
            RealMusicObjects.Add(real);
            return real;
        }

        MadeMusic music = new MadeMusic { Name = name };
        MadeMusicObjects.Add(music);
        return music;
    }

    public void SetMusicProperty(object music, string name, object value)
    {
        if (music is MusicObject real)
        {
            real.SetProperty(name, value);
            return;
        }

        PropertyBag(music).Add((name, value));
    }

    /// <summary>
    /// Returns the property list of whichever music stand-in this is.
    /// <para>
    /// The host produces three shapes and the actions cannot tell them apart: the
    /// recording <see cref="MadeMusic"/>, a <see cref="SyntaxMark"/> standing in for
    /// what a music-making constructor returned, and — under
    /// <see cref="MakeRealMusic"/> — the Engine's own <see cref="MusicObject"/>, which
    /// keeps its own properties and never reaches here.
    /// </para>
    /// </summary>
    /// <param name="music">The music stand-in.</param>
    /// <returns>Its property list.</returns>
    internal static List<(string Name, object Value)> PropertyBag(object music)
        => music switch
        {
            MadeMusic made => made.Properties,
            SyntaxMark mark => mark.Properties,
            _ => throw new InvalidOperationException(
                "not a music value this host made: " + (music ?? "null")),
        };

    public bool IsMarkup(object value) => value is string || value is MutableString;

    public bool IsMarkupList(object value)
    {
        if (!(value is Pair))
        {
            return false;
        }

        for (object p = value; p is Pair pair; p = pair.Cdr)
        {
            if (!IsMarkup(pair.Car))
            {
                return false;
            }
        }

        return true;
    }

    public bool IsMarkupFunction(object value) => MarkupFunctions.Contains(value);

    public void DefineMarkupCommand(object name, object function)
        => MarkupCommandsDefined.Add((name, function));

    public bool IsScore(object value) => Scores.Contains(value);

    public bool BookHasPaper(object book) => BooksWithPaper.Contains(book);

    public bool IsKey(object value)
        => value is Symbol
           || (SchemeNumber.IsNumber(value)
               && SchemeNumber.IsExact(value)
               && SchemeNumber.IsInteger(value)
               && SchemeNumber.Compare(value, 0L) >= 0);

    // ------ ILexerHost ------

    // The lexer-side and parser-side word scans are ONE table upstream
    // (Lily_lexer::scan_word), so they are one table here: a test that scripts `c` as
    // a NOTENAME_PITCH gets it both from `c4` in real text and from
    // make_music_from_simple's symbol path.
    LexerLookup ILexerHost.ScanWord(LexerState state, string word)
        => WordScans.TryGetValue(Symbol.Intern(word), out (string TokenName, object Value) entry)
            ? new LexerLookup(entry.TokenName, entry.Value)
            : LexerLookup.None;

    LexerLookup ILexerHost.LookupKeyword(string word)
        => Keywords.TryGetValue(word, out (string TokenName, object Value) entry)
            ? new LexerLookup(entry.TokenName, entry.Value)
            : LexerLookup.None;

    LexerLookup ILexerHost.LookupIdentifier(string word)
        => Identifiers.TryGetValue(word, out LexerLookup found) ? found : LexerLookup.None;

    // The markup-command table the MARKUP lexer mode consults before falling back to
    // the ordinary escaped-word lookup. Scripted rather than empty as of RAG18/RAG19,
    // because a markup command's SIGNATURE is what the lexer announces as EXPECT_*
    // tokens — with no table, `\bold "x"` never produces them and the whole
    // markup-command half of the grammar is unreachable from real text.
    LexerLookup ILexerHost.LookupMarkupCommand(
        string word, out IReadOnlyList<MarkupPredicate> predicates)
    {
        if (MarkupCommands.TryGetValue(
                word, out (string TokenName, object Value, string[] Predicates) entry))
        {
            // The scripted table names its predicates; a scripted predicate IS its name,
            // which is all the arglist rules need when CallBehavior is scripted too.
            List<MarkupPredicate> declared = new List<MarkupPredicate>();
            foreach (string predicate in entry.Predicates)
            {
                declared.Add(new MarkupPredicate(predicate, predicate));
            }

            predicates = declared;
            return new LexerLookup(entry.TokenName, entry.Value);
        }

        predicates = new List<MarkupPredicate>();
        return LexerLookup.None;
    }

    object ILexerHost.ParseEmbeddedScheme(
        string input, int position, SourceSpan start, out int consumed)
        => _embeddedScheme.ParseEmbeddedScheme(input, position, start, out consumed);

    /// <summary>
    /// Hands the datum back unevaluated: a scripted host has no interpreter, so the only
    /// honest answer is the text the bracket matcher produced.
    /// </summary>
    /// <param name="token">The datum.</param>
    /// <param name="location">Where it was written.</param>
    /// <param name="extraToken">Ignored — a scripted host produces no multiple values.</param>
    /// <returns>The datum.</returns>
    object ILexerHost.EvalScheme(object token, SourceSpan location, char extraToken) => token;

    /// <summary>
    /// Classifies an already-evaluated value the same way a <c>\word</c> lookup would,
    /// when the script named one; otherwise refuses.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The token it names, or <see cref="LexerLookup.None"/>.</returns>
    LexerLookup ILexerHost.ScanSchemeValue(object value)
        => ScmValueTokens.TryGetValue(value ?? string.Empty, out LexerLookup found)
            ? found
            : LexerLookup.None;

    /// <summary>
    /// The tokens a scripted <c>$value</c> lexes as, keyed by the value the bracket
    /// matcher produced. Empty unless a test fills it in.
    /// </summary>
    public Dictionary<object, LexerLookup> ScmValueTokens { get; } = new Dictionary<object, LexerLookup>();

    /// <summary>Answers no markup command table for a value.</summary>
    /// <param name="value">The value.</param>
    /// <param name="predicates">Receives an empty list.</param>
    /// <returns>Always <see cref="LexerLookup.None"/>.</returns>
    LexerLookup ILexerHost.MarkupFunctionToken(object value, out IReadOnlyList<MarkupPredicate> predicates)
    {
        predicates = new List<MarkupPredicate>();
        return LexerLookup.None;
    }
}
