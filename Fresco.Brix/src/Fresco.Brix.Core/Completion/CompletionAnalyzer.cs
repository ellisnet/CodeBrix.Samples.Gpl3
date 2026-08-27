// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Ly.Lex;
using Fresco.Brix.Ly.Slexing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Lily = Fresco.Brix.Ly.Lex.LilyPondMode;
using Scheme = Fresco.Brix.Ly.Lex.SchemeMode;
using Parser = Fresco.Brix.Ly.Slexing.Parser;
using State = Fresco.Brix.Ly.Lex.State;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Completion; //was previously: frescobaldi/autocomplete/analyzer.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>What the analyzer decided: where the completed text starts, and
/// what to offer.</summary>
public readonly struct CompletionResult
{
    /// <summary>Nothing to offer.</summary>
    public static readonly CompletionResult None = default;

    /// <summary>Creates a result.</summary>
    /// <param name="column">The 0-based column in the line where the text
    /// being completed starts.</param>
    /// <param name="model">The completions.</param>
    public CompletionResult(int column, CompletionModel model)
    {
        Column = column;
        Model = model;
    }

    /// <summary>Gets where the completed text starts, within its line.</summary>
    public int Column { get; }

    /// <summary>Gets the completions, or null when there are none.</summary>
    public CompletionModel Model { get; }

    /// <summary>Gets whether there is anything to show.</summary>
    public bool HasCompletions => Model is { Count: > 0 };
}

/// <summary>
/// Works out what completions make sense where the caret is.
/// <para>
/// The whole answer comes from the tokens of the current line up to the caret
/// and from the parser that is active there: a parser maps to an ordered list
/// of tests, and the first test that produces a model wins. That is exactly
/// upstream's design, and the tests are ported one for one — which is what
/// makes <c>\override Staff.</c> offer grobs, <c>\override NoteHead #'</c>
/// offer that grob's properties, and <c>\key c \</c> offer the modes.
/// </para>
/// </summary>
public sealed class CompletionAnalyzer
{
    private readonly List<Token> _tokens = new List<Token>();
    private EditorDocument _document;
    private State _state;
    private Token _last;
    private int _lastPosition;

    /// <summary>Gets the tokens up to the caret, for the tests.</summary>
    public IReadOnlyList<Token> Tokens => _tokens;

    /// <summary>Gets where the completed text starts, within its line.</summary>
    public int Column { get; private set; }

    /// <summary>Gets the lexer state at the caret.</summary>
    public State LexState => _state;

    /// <summary>
    /// Analyzes a document at an offset and answers what to complete.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="offset">The caret offset.</param>
    /// <returns>The result.</returns>
    public CompletionResult Completions(EditorDocument document, int offset)
    {
        if (document == null) { return CompletionResult.None; }

        DocumentEditorState editorState = DocumentEditorState.For(document);
        if (editorState == null) { return CompletionResult.None; }

        _document = document;
        TextDocument store = document.Document;
        DocumentLine line = store.GetLineByOffset(offset);
        Column = offset - line.Offset;
        Position = offset;
        string text = store.GetText(line.Offset, Column);

        //A list of tokens ending EXACTLY at the caret: the cached ones up to
        //it, and then the partial one it falls inside re-parsed from the text
        //that is actually there.
        _state = TokenIter.StateAt(editorState.Highlighter, line.LineNumber);
        _tokens.Clear();
        foreach (var token in TokenIter.Tokens(editorState.Highlighter, line.LineNumber))
        {
            if (token.End > Column)
            {
                _tokens.AddRange(_state.Tokens(text, token.Pos));
                break;
            }

            _tokens.Add(token);
            _state.Follow(token);
            if (token.End == Column) { break; }
        }

        _last = _tokens.Count > 0 ? _tokens[_tokens.Count - 1] : null;
        _lastPosition = _last?.Pos ?? Column;

        Parser parser = _state.CurrentParser();
        if (parser == null || !Tests.TryGetValue(parser.GetType(), out var tests))
        {
            return CompletionResult.None;
        }

        foreach (var test in tests)
        {
            CompletionModel model = test(this);
            if (model is { Count: > 0 })
            {
                return new CompletionResult(Column, model);
            }
        }

        return CompletionResult.None;
    }

    /// <summary>Gets the caret offset the analysis was run at.</summary>
    public int Position { get; private set; }

    private string LastText => _last?.Text ?? string.Empty;

    private DocumentCompletionData Data => DocumentCompletionData.For(_document);

    private IReadOnlyList<Type> TokenClasses()
        => _tokens.Select(t => t.GetType()).ToList();

    /// <summary>
    /// Moves <see cref="Column"/> back over the tokens at the end of the line
    /// until one of the given kinds is met.
    /// </summary>
    /// <param name="classes">The kinds to stop at.</param>
    private void BackUntil(params Type[] classes)
    {
        for (int i = _tokens.Count - 1; i >= 0; i--)
        {
            if (classes.Any(c => c.IsInstanceOfType(_tokens[i]))) { break; }

            Column = _tokens[i].Pos;
        }
    }

    /// <summary>Finds the index of a token with exactly this text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="start">Where to start, negative counting from the end.</param>
    /// <param name="end">Where to stop, negative counting from the end.</param>
    /// <returns>The index, or -1.</returns>
    /// <remarks>Upstream writes <c>tokens.index(t, -5, -3)</c>, whose negative
    /// bounds are Python's slice semantics: this reproduces them.</remarks>
    private int IndexOf(string text, int start = 0, int? end = null)
    {
        int count = _tokens.Count;
        int from = start < 0 ? Math.Max(0, count + start) : Math.Min(start, count);
        int to = end == null
            ? count
            : end < 0 ? Math.Max(0, count + end.Value) : Math.Min(end.Value, count);

        for (int i = from; i < to; i++)
        {
            if (string.Equals(_tokens[i].Text, text, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private bool Contains(string text, int start = 0, int? end = null)
        => IndexOf(text, start, end) >= 0;

    #region | The tests — each answers a model or null |

    private CompletionModel TopLevel()
    {
        BackUntil(typeof(Space));
        return CompletionData.TopLevelContents;
    }

    private CompletionModel Book()
    {
        BackUntil(typeof(Space));
        return Data.BookCommands(Position);
    }

    private CompletionModel BookPart()
    {
        BackUntil(typeof(Space));
        return Data.BookPartCommands(Position);
    }

    private CompletionModel Score()
    {
        BackUntil(typeof(Space));
        return Data.ScoreCommands(Position);
    }

    private CompletionModel Tweak()
    {
        int i = IndexOf("\\tweak");
        if (i < 0) { return null; }

        List<Token> after = _tokens.Skip(i + 1).ToList();
        List<Type> classes = after.Select(t => t.GetType()).ToList();

        if (Same(classes, typeof(Space), typeof(Lily.SchemeStart)))
        {
            Column -= 1;
            return CompletionData.AllGrobProperties;
        }

        if (Same(classes, typeof(Space), typeof(Lily.SchemeStart), typeof(Scheme.Quote)))
        {
            Column -= 2;
            return CompletionData.AllGrobProperties;
        }

        if (classes.Count > 0 && Same(
                classes.Take(classes.Count - 1).ToList(),
                typeof(Space), typeof(Lily.SchemeStart), typeof(Scheme.Quote)))
        {
            Column = _lastPosition - 2;
            return CompletionData.AllGrobProperties;
        }

        //The 2.18-style [GrobName.]propertyname tweak.
        if (classes.Any(c => typeof(Lily.GrobName).IsAssignableFrom(c)))
        {
            BackUntil(typeof(Space), typeof(Lily.DotPath));
            return CompletionData.GrobProperties(after[0].Text, false);
        }

        if (after.Count > 0)
        {
            BackUntil(typeof(Space));
            return CompletionData.AllGrobPropertiesAndGrobNames;
        }

        return null;
    }

    private CompletionModel Key()
    {
        IReadOnlyList<Type> classes = TokenClasses();
        if (!Contains("\\key", -5, -2)) { return null; }

        if (!classes.Skip(Math.Max(0, classes.Count - 3))
                .Any(c => typeof(Lily.Note).IsAssignableFrom(c)))
        {
            return null;
        }

        if (LastText.StartsWith('\\')) { Column = _lastPosition; }

        return CompletionData.PitchModes;
    }

    private CompletionModel Clef()
    {
        if (!Contains("\\clef", -4, -1)) { return null; }

        BackUntil(typeof(Space), typeof(Lily.StringQuotedStart));
        return CompletionData.Clefs;
    }

    private CompletionModel Repeat()
    {
        if (!Contains("\\repeat", -4, -1)) { return null; }

        BackUntil(typeof(Space), typeof(Lily.StringQuotedStart));
        return CompletionData.RepeatTypes;
    }

    private CompletionModel Language()
    {
        if (!Contains("\\language", -4, -1)) { return null; }

        BackUntil(typeof(Lily.StringQuotedStart));
        return CompletionData.LanguageNames;
    }

    private CompletionModel Include()
    {
        if (!Contains("\\include", -4, -2)) { return null; }

        BackUntil(typeof(Lily.StringQuotedStart));

        //Even on Windows, LilyPond uses the forward slash.
        string text = LastText;
        int slash = text.LastIndexOf('/');
        string directory = slash >= 0 ? text.Substring(0, slash) : null;
        return Data.IncludeNames(directory);
    }

    private CompletionModel GeneralMusic()
    {
        if (LastText.StartsWith('\\')) { Column = _lastPosition; }

        return Data.MusicCommands(Position);
    }

    private CompletionModel LyricMode()
    {
        if (LastText.StartsWith('\\')) { Column = _lastPosition; }

        return Data.LyricCommands(Position);
    }

    private CompletionModel MusicGlyph()
    {
        int i = IndexOf("\\musicglyph", -5, -3);
        if (i < 0) { return null; }

        Type[] expected =
        {
            typeof(Lily.MarkupCommand), typeof(Space), typeof(Lily.SchemeStart),
            typeof(Scheme.StringQuotedStart), typeof(Scheme.StringBase),
        };
        for (int j = 0; j < expected.Length && i + j < _tokens.Count; j++)
        {
            if (_tokens[i + j].GetType() != expected[j]) { return null; }
        }

        if (i + 4 < _tokens.Count) { Column = _tokens[i + 4].Pos; }

        return CompletionData.MusicGlyphs;
    }

    private CompletionModel MidiInstrument()
    {
        if (!Contains("midiInstrument", -7, -2)) { return null; }

        if (LastText != "\"") { Column = _lastPosition; }

        return CompletionData.MidiInstruments;
    }

    private CompletionModel FontName()
    {
        //Upstream offers the fonts installed on the machine here. This app
        //never falls back to a system font (standing rule 6), so offering
        //system font names would offer names nothing can draw. The property
        //still completes as a scheme word through the other tests.
        return null;
    }

    private CompletionModel SchemeWord()
    {
        if (_last is not Scheme.Word) { return null; }

        Column = _lastPosition;
        return Data.SchemeWords();
    }

    private CompletionModel Markup()
    {
        string text = LastText;
        if (text.StartsWith('\\'))
        {
            if (!Ly.Words.Markupcommands.Contains(text.Substring(1))
                && text != "\\markup")
            {
                Column = _lastPosition;
            }
            else
            {
                return CompletionData.MarkupCommands;
            }
        }
        else
        {
            Match match = TrailingWord.Match(text);
            if (match.Success) { Column = _lastPosition + match.Index; }
        }

        return Data.Markup(Position);
    }

    private CompletionModel MarkupTop()
    {
        if (!LastText.StartsWith('\\')) { return null; }

        if (_last is not (Lily.MarkupCommand or Lily.MarkupUserCommand))
        {
            return null;
        }

        Column = _lastPosition;
        return Data.Markup(Position);
    }

    private CompletionModel Header()
    {
        if (Contains("=", -3) || LastText.StartsWith('\\'))
        {
            if (LastText.StartsWith('\\')) { Column = _lastPosition; }

            return CompletionData.LilyPondMarkup;
        }

        if (LastText.Length > 0 && char.IsLetter(LastText[0]))
        {
            Column = _lastPosition;
        }

        return CompletionData.HeaderVariables;
    }

    private CompletionModel Paper()
    {
        if (Contains("=", -3) || LastText.StartsWith('\\'))
        {
            if (LastText.StartsWith('\\')) { Column = _lastPosition; }

            return CompletionData.LilyPondMarkup;
        }

        if (LastText.Length > 0 && char.IsLetter(LastText[0]))
        {
            Column = _lastPosition;
        }

        return CompletionData.PaperVariables;
    }

    private CompletionModel Layout()
    {
        BackUntil(typeof(Space));
        return CompletionData.LayoutVariables;
    }

    private CompletionModel Midi()
    {
        BackUntil(typeof(Space));
        return CompletionData.MidiVariables;
    }

    private CompletionModel Engraver()
    {
        bool CommandIn(int start, int? end)
            => Contains("\\remove", start, end) || Contains("\\consists", start, end);

        if (_state.CurrentParser() is Lily.ParseString)
        {
            if (!CommandIn(-5, -2)) { return null; }

            if (LastText != "\"")
            {
                if (!Contains("\"", -2, -1)) { return null; }

                Column = _lastPosition;
            }

            return CompletionData.Engravers;
        }

        if (!CommandIn(-3, -1)) { return null; }

        BackUntil(typeof(Space));
        return CompletionData.Engravers;
    }

    private CompletionModel ContextVariableSet()
    {
        if (!Contains("=", -4)) { return null; }

        if (_last is Scheme.Word)
        {
            Column = _lastPosition;
            return Data.SchemeWords();
        }

        if (LastText.StartsWith('\\')) { Column = _lastPosition; }

        return CompletionData.LilyPondMarkup;
    }

    private CompletionModel Context()
    {
        BackUntil(typeof(Space));
        return CompletionData.ContextContents;
    }

    private CompletionModel With()
    {
        BackUntil(typeof(Space));
        return CompletionData.WithContents;
    }

    private CompletionModel Translator()
    {
        for (int i = _tokens.Count - 2; i >= 0; i--)
        {
            if (_tokens[i] is Lily.ContextName) { return null; }

            if (_tokens[i] is Lily.Translator) { break; }
        }

        BackUntil(typeof(Space));
        return CompletionData.Contexts;
    }

    private CompletionModel Override()
    {
        IReadOnlyList<Type> classes = TokenClasses();
        int i = LastIndexIn(classes, typeof(Lily.GrobName), 5);
        if (i < 0)
        {
            //No grob yet: offer contexts and grobs, unless we have dropped
            //into scheme, where the answer belongs to another test.
            if (_state.CurrentParser() is Scheme.ParseScheme) { return null; }

            BackUntil(typeof(Lily.DotPath), typeof(Space));
            IReadOnlyList<Parser> parsers = _state.Parsers();
            bool inContextBlock = parsers.Count > 1
                && parsers[1] is Lily.ParseWith or Lily.ParseContext;
            return inContextBlock
                || classes.Any(c => typeof(Lily.DotPath).IsAssignableFrom(c))
                    ? CompletionData.Grobs
                    : CompletionData.ContextsAndGrobs;
        }

        int count = _tokens.Count - i - 1;
        if (count == 0)
        {
            Column = _lastPosition;
            return CompletionData.Grobs;
        }

        if (count >= 2 && i + 2 < _tokens.Count)
        {
            //The scheme-start "#" is where the completed text begins.
            Column = _tokens[i + 2].Pos;
        }

        Type[] test =
        {
            typeof(Space), typeof(Lily.SchemeStart),
            typeof(Scheme.Quote), typeof(Scheme.Word),
        };
        List<Type> after = classes.Skip(i + 1).ToList();
        if (Same(after, test.Take(Math.Min(count, test.Length)).ToArray()))
        {
            return CompletionData.GrobProperties(_tokens[i].Text);
        }

        BackUntil(typeof(Lily.DotPath), typeof(Space));
        return CompletionData.GrobProperties(_tokens[i].Text, false);
    }

    private CompletionModel Revert()
        //The revert parser drops out of invalid constructs, which happens all
        //the time while the user is still typing — so the test looks for the
        //command itself rather than trusting the parser.
        => Contains("\\revert") ? Override() : null;

    private CompletionModel SetUnset()
    {
        IReadOnlyList<Type> classes = TokenClasses();
        BackUntil(typeof(Space), typeof(Lily.DotPath));
        if (classes.Any(c => typeof(Lily.ContextProperty).IsAssignableFrom(c))
            && _last is Space)
        {
            return null;
        }

        return classes.Any(c => typeof(Lily.DotPath).IsAssignableFrom(c))
            ? CompletionData.ContextProperties
            : CompletionData.ContextsAndProperties;
    }

    private CompletionModel MarkupOverride()
    {
        int i = IndexOf("\\override", -6, -4);
        if (i < 0) { return null; }

        Type[] expected =
        {
            typeof(Lily.MarkupCommand), typeof(Space), typeof(Lily.SchemeStart),
            typeof(Scheme.Quote), typeof(Scheme.OpenParen),
        };
        for (int j = 0; j < expected.Length && i + j < _tokens.Count; j++)
        {
            if (_tokens[i + j].GetType() != expected[j]) { return null; }
        }

        if (_tokens.Count > i + 5) { Column = _lastPosition; }

        return CompletionData.MarkupProperties;
    }

    private CompletionModel SchemeOther()
    {
        if (_last is not (Lily.SchemeStart or Scheme.OpenParen or Scheme.Word))
        {
            return null;
        }

        if (_last is Scheme.Word) { Column = _lastPosition; }

        return Data.SchemeWords();
    }

    private CompletionModel AccidentalStyle()
    {
        int i = IndexOf("\\accidentalStyle");
        if (i < 0) { return null; }

        BackUntil(typeof(Space), typeof(Lily.DotPath));
        List<Type> classes = TokenClasses().Skip(i + 1).ToList();
        int specifier = classes.FindIndex(
            c => typeof(Lily.AccidentalStyleSpecifier).IsAssignableFrom(c));
        if (specifier >= 0
            && classes.Skip(specifier + 1).Any(c => typeof(Space).IsAssignableFrom(c)))
        {
            return null;
        }

        return classes.Any(c => typeof(Lily.ContextName).IsAssignableFrom(c))
            ? CompletionData.AccidentalStyles
            : CompletionData.AccidentalStylesAndContexts;
    }

    private CompletionModel HideOmit()
    {
        int i = Math.Max(IndexOf("\\omit", -6), IndexOf("\\hide", -6));
        if (i < 0) { return null; }

        BackUntil(typeof(Space), typeof(Lily.DotPath));
        List<Type> classes = TokenClasses().Skip(i + 1).ToList();
        List<Type> exceptLast = classes.Count > 0
            ? classes.Take(classes.Count - 1).ToList()
            : classes;
        if (exceptLast.Any(c => typeof(Lily.GrobName).IsAssignableFrom(c)))
        {
            return null;
        }

        return classes.Any(c => typeof(Lily.ContextName).IsAssignableFrom(c))
            ? CompletionData.Grobs
            : CompletionData.ContextsAndGrobs;
    }

    #endregion

    private static readonly Regex TrailingWord
        = new Regex(@"\w+$", RegexOptions.Compiled);

    private static bool Same(IReadOnlyList<Type> classes, params Type[] expected)
    {
        if (classes.Count != expected.Length) { return false; }

        for (int i = 0; i < expected.Length; i++)
        {
            if (!expected[i].IsAssignableFrom(classes[i])) { return false; }
        }

        return true;
    }

    private static int LastIndexIn(
        IReadOnlyList<Type> classes, Type wanted, int fromEnd)
    {
        int start = Math.Max(0, classes.Count - fromEnd);
        for (int i = start; i < classes.Count; i++)
        {
            if (wanted.IsAssignableFrom(classes[i])) { return i; }
        }

        return -1;
    }

    /// <summary>
    /// Which tests run for which parser, in the order upstream runs them.
    /// </summary>
    private static readonly IReadOnlyDictionary<
        Type, IReadOnlyList<Func<CompletionAnalyzer, CompletionModel>>> Tests
        = BuildTests();

    private static IReadOnlyDictionary<
        Type, IReadOnlyList<Func<CompletionAnalyzer, CompletionModel>>> BuildTests()
    {
        Func<CompletionAnalyzer, CompletionModel> markupTop = a => a.MarkupTop();
        Func<CompletionAnalyzer, CompletionModel> tweak = a => a.Tweak();
        Func<CompletionAnalyzer, CompletionModel> schemeWord = a => a.SchemeWord();
        Func<CompletionAnalyzer, CompletionModel> key = a => a.Key();
        Func<CompletionAnalyzer, CompletionModel> clef = a => a.Clef();
        Func<CompletionAnalyzer, CompletionModel> repeat = a => a.Repeat();
        Func<CompletionAnalyzer, CompletionModel> accidentalStyle
            = a => a.AccidentalStyle();
        Func<CompletionAnalyzer, CompletionModel> hideOmit = a => a.HideOmit();
        Func<CompletionAnalyzer, CompletionModel> revert = a => a.Revert();
        Func<CompletionAnalyzer, CompletionModel> generalMusic = a => a.GeneralMusic();
        Func<CompletionAnalyzer, CompletionModel> engraver = a => a.Engraver();
        Func<CompletionAnalyzer, CompletionModel> contextVariableSet
            = a => a.ContextVariableSet();
        Func<CompletionAnalyzer, CompletionModel> translator = a => a.Translator();
        Func<CompletionAnalyzer, CompletionModel> over = a => a.Override();
        Func<CompletionAnalyzer, CompletionModel> setUnset = a => a.SetUnset();

        var music = new List<Func<CompletionAnalyzer, CompletionModel>>
        {
            markupTop, tweak, schemeWord, key, clef, repeat,
            accidentalStyle, hideOmit, revert, generalMusic,
        };
        var drumMusic = new List<Func<CompletionAnalyzer, CompletionModel>>
        {
            markupTop, tweak, schemeWord, key, clef, repeat,
            hideOmit, revert, generalMusic,
        };

        return new Dictionary<
            Type, IReadOnlyList<Func<CompletionAnalyzer, CompletionModel>>>
        {
            [typeof(Lily.ParseGlobal)] = new[] { markupTop, repeat, a => a.TopLevel() },
            [typeof(Lily.ParseBook)] = new[] { markupTop, a => a.Book() },
            [typeof(Lily.ParseBookPart)] = new[] { markupTop, a => a.BookPart() },
            [typeof(Lily.ParseScore)] = new Func<CompletionAnalyzer, CompletionModel>[]
                { a => a.Score() },
            [typeof(Lily.ParseMusic)] = music,
            [typeof(Lily.ParseNoteMode)] = music,
            [typeof(Lily.ParseChordMode)] = music,
            [typeof(Lily.ParseDrumMode)] = drumMusic,
            [typeof(Lily.ParseFigureMode)] = music,
            [typeof(Lily.ParseMarkup)] = new Func<CompletionAnalyzer, CompletionModel>[]
                { a => a.Markup() },
            [typeof(Lily.ParseHeader)] = new[] { markupTop, a => a.Header() },
            [typeof(Lily.ParsePaper)] = new Func<CompletionAnalyzer, CompletionModel>[]
                { a => a.Paper() },
            [typeof(Lily.ParseLayout)] = new[]
                { accidentalStyle, hideOmit, a => a.Layout() },
            [typeof(Lily.ParseMidi)] = new Func<CompletionAnalyzer, CompletionModel>[]
                { a => a.Midi() },
            [typeof(Lily.ParseContext)] = new[]
                { engraver, contextVariableSet, a => a.Context() },
            [typeof(Lily.ParseWith)] = new[]
                { markupTop, engraver, contextVariableSet, a => a.With() },
            [typeof(Lily.ParseTranslator)] = new[] { translator },
            [typeof(Lily.ExpectTranslatorId)] = new[] { translator },
            [typeof(Lily.ParseOverride)] = new[] { over },
            [typeof(Lily.ParseRevert)] = new[] { over },
            [typeof(Lily.ParseSet)] = new[] { setUnset },
            [typeof(Lily.ParseUnset)] = new[] { setUnset },
            [typeof(Lily.ParseTweak)] = new[] { tweak },
            [typeof(Lily.ParseTweakGrobProperty)] = new[] { tweak },
            [typeof(Lily.ParseString)] = new[]
            {
                engraver, clef, repeat,
                a => a.MidiInstrument(), a => a.Include(), a => a.Language(),
            },
            [typeof(Lily.ParseClef)] = new[] { clef },
            [typeof(Lily.ParseRepeat)] = new[] { repeat },
            [typeof(Scheme.ParseScheme)] = new[]
                { over, tweak, a => a.MarkupOverride(), a => a.SchemeOther() },
            [typeof(Scheme.ParseString)] = new Func<CompletionAnalyzer, CompletionModel>[]
            {
                a => a.MusicGlyph(), a => a.MidiInstrument(), a => a.FontName(),
            },
            [typeof(Lily.ParseLyricMode)] = new[]
                { markupTop, repeat, a => a.LyricMode() },
            [typeof(Lily.ParseAccidentalStyle)] = new[] { accidentalStyle },
            [typeof(Lily.ParseScriptAbbreviationOrFingering)] = new[] { accidentalStyle },
            [typeof(Lily.ParseHideOmit)] = new[] { hideOmit },
            [typeof(Lily.ParseGrobPropertyPath)] = new[] { revert },
        };
    }
}
