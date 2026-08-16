// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.Text;
using CodeBrix.LilyPort.Parsing.Actions;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Lexing;

/// <summary>
/// The character classes <c>lexer.ll</c> defines in its definitions section, translated
/// to .NET regular expressions.
/// <para>
/// Kept as named constants rather than inlined for the same reason upstream names them:
/// they are used across dozens of rules, and a difference in one of them is a
/// difference in what LilyPond accepts.
/// </para>
/// </summary>
public static class LexerPatterns
{
    /// <summary>A letter, including the Latin-1 upper range. <c>A</c> upstream.</summary>
    public const string Letter = @"[a-zA-Z\u0080-\u00ff]";

    /// <summary>A digit. <c>N</c> upstream.</summary>
    public const string Digit = "[0-9]";

    /// <summary>
    /// An identifier: letters, with single hyphens or underscores between them.
    /// <c>SYMBOL</c> upstream — and note it cannot END with a hyphen, which is what
    /// keeps <c>a-</c> from scanning as one symbol.
    /// </summary>
    public const string Symbol = Letter + "(?:[-_]" + Letter + "|" + Letter + ")*";

    /// <summary>A backslash followed by an identifier. <c>COMMAND</c> upstream.</summary>
    public const string Command = @"\\(?:" + Symbol + ")";

    /// <summary>Whitespace, newline included. <c>WHITE</c> upstream.</summary>
    public const string White = @"[ \n\t\f\r]";

    /// <summary>An unsigned integer. <c>UNSIGNED</c> upstream.</summary>
    public const string Unsigned = Digit + "+";

    /// <summary>A signed integer. <c>INT</c> upstream.</summary>
    public const string Integer = "-?" + Unsigned;

    /// <summary>An escaped Scheme integer such as <c>\3</c>. <c>E_UNSIGNED</c> upstream.</summary>
    public const string EscapedUnsigned = @"\\" + Digit + "+";

    /// <summary>A fraction such as <c>3/4</c>. <c>FRACTION</c> upstream.</summary>
    public const string Fraction = Digit + @"+/" + Digit + "+";

    /// <summary>A real number. <c>REAL</c> upstream.</summary>
    public const string Real = @"(?:" + Integer + @"\." + Digit + @"*|-?\." + Digit + "+)";

    /// <summary>A real with digits both sides of the point. <c>STRICTREAL</c> upstream.</summary>
    public const string StrictReal = Unsigned + @"\." + Unsigned;

    /// <summary>A rest or a spacer. <c>RESTNAME</c> upstream.</summary>
    public const string RestName = "[rs]";

    /// <summary>The characters that are their own token. <c>SPECIAL</c> upstream.</summary>
    public const string Special = @"[-+*/=<>{}!?_^'',.:]";

    /// <summary>A shorthand: any character, or a backslash and any character.</summary>
    public const string Shorthand = @"(?:\\.|.)";

    /// <summary>The escapes a quoted string understands. <c>ESCAPED</c> upstream.</summary>
    public const string Escaped = "[nt\\\\''\"\"]";

    /// <summary>A figured-bass alteration symbol. <c>FIG_ALT_SYMB</c> upstream.</summary>
    public const string FigureAlterationSymbol = @"[+\-!]";

    /// <summary>A figured-bass alteration expression. <c>FIG_ALT_EXPR</c> upstream.</summary>
    public const string FigureAlterationExpression
        = White + "*" + FigureAlterationSymbol + "(?:" + FigureAlterationSymbol + "|" + White + ")*";

    /// <summary>The UTF-8 byte-order mark, which upstream skips with a warning.</summary>
    public const string ByteOrderMark = "\ufeff";
}

/// <summary>
/// The ported rules of <c>lexer.ll</c>, for all fourteen start conditions.
/// <para>
/// Written in <c>lexer.ll</c>'s ORDER, because that order breaks ties between equally
/// long matches and several rules depend on it. Upstream's "pseudo backup rules" —
/// the <c>{SYMBOL}/[-_]</c> trailing-context patterns that keep flex from needing
/// backup states — are not reproduced: they exist to shape flex's generated tables and
/// have no effect on which token comes out. That divergence is recorded in
/// PORT-COVERAGE.
/// </para>
/// </summary>
public static class LilyPondLexerRules
{
    private static readonly LexerState[] TextModes =
    {
        LexerState.Initial, LexerState.Chords, LexerState.Figures, LexerState.Include,
        LexerState.Lyrics, LexerState.Markup, LexerState.Notes,
    };

    private static readonly LexerState[] MusicModes =
    {
        LexerState.Initial, LexerState.Chords, LexerState.Lyrics,
        LexerState.Notes, LexerState.Figures,
    };

    private static readonly LexerState[] EmbeddedSchemeModes =
    {
        LexerState.Initial, LexerState.Chords, LexerState.Figures,
        LexerState.Lyrics, LexerState.Markup, LexerState.Notes,
    };

    private static readonly LexerState[] NotesFigures = { LexerState.Notes, LexerState.Figures };

    private static readonly LexerState[] ChordsNotesFigures =
    {
        LexerState.Chords, LexerState.Notes, LexerState.Figures,
    };

    private static readonly LexerState[] Quotes = { LexerState.Quote, LexerState.CommandQuote };

    /// <summary>Builds the rule list.</summary>
    /// <param name="host">The tables and reader the rules consult.</param>
    /// <returns>The rules, in <c>lexer.ll</c>'s order.</returns>
    public static IReadOnlyList<LexerRule> Create(ILexerHost host = null)
    {
        ILexerHost tables = host ?? new UnresolvedLexerHost();
        List<LexerRule> rules = new List<LexerRule>();

        AddCommentsAndModes(rules);
        AddIncludeAndMainInput(rules, tables);
        AddMusicWords(rules, tables);
        AddEmbeddedScheme(rules, tables);
        AddBrackets(rules);
        AddFigures(rules);
        AddNotesAndFigureWords(rules, tables);
        AddQuotes(rules, tables);
        AddLyrics(rules, tables);
        AddChords(rules, tables);
        AddMarkup(rules, tables);
        AddInitialWords(rules, tables);
        AddNumbersAndSpecials(rules, tables);

        return rules;
    }

    private static void AddCommentsAndModes(List<LexerRule> rules)
    {
        // <*>\r -- swallow and ignore carriage returns.
        rules.Add(new LexerRule("\r", null, (s, t) => null));

        /* Use the trailing context feature. Otherwise, the BOM will not be
           found if the file starts with an identifier definition. */
        rules.Add(new LexerRule(
            LexerPatterns.ByteOrderMark,
            MusicModes,
            (s, t) =>
            {
                s.Warn("stray UTF-8 BOM encountered");
                return null;
            }));

        rules.Add(new LexerRule("%\\{", TextModes, (s, t) =>
        {
            s.PushState(LexerState.LongComment);
            return null;
        }));

        rules.Add(new LexerRule("%[^{\n\r][^\n\r]*[\n\r]?", TextModes, (s, t) => null));
        rules.Add(new LexerRule("%[\n\r]?", TextModes, (s, t) => null));
        rules.Add(new LexerRule(LexerPatterns.White + "+", TextModes, (s, t) => null));

        // The three commands that read a bare string in their own start condition.
        rules.Add(new LexerRule(@"\\version" + LexerPatterns.White + "*", MusicModes, (s, t) =>
        {
            s.PushState(LexerState.Version);
            return null;
        }));

        rules.Add(new LexerRule(@"\\sourcefilename" + LexerPatterns.White + "*", MusicModes, (s, t) =>
        {
            s.PushState(LexerState.SourceFileName);
            return null;
        }));

        rules.Add(new LexerRule(@"\\sourcefileline" + LexerPatterns.White + "*", MusicModes, (s, t) =>
        {
            s.PushState(LexerState.SourceFileLine);
            return null;
        }));

        rules.Add(new LexerRule("\"[^\"]*\"", new[] { LexerState.Version }, (s, t) =>
        {
            s.PopState();
            s.LastVersionString = t.Substring(1, t.Length - 2);

            // lexer.ll:255 — only the MAIN input's own \version answers "was a version
            // seen?". Without this the run's version check had no answer at all and
            // ly/init.ly's epilogue reported "no \version statement found" for EVERY
            // file, including the ones whose first line is a \version. Nothing noticed,
            // because ly:warning-located was writing into a null sink.
            //
            // Upstream's test is `is_main_input_ && include_stack_.size () ==
            // main_input_level_' because ONE lexer reads init.ly and, through
            // \maininput, the user's file as well, so it has to know which it is in. The
            // port's runner parses the user's file in a ParseText call of its own and
            // never uses \maininput, so the equivalent test is the include depth alone —
            // the prologue and epilogue it parses either side declare no \version.
            if (s.IncludeDepth == 0)
            {
                s.MainInputVersionString = s.LastVersionString;
            }

            return null;
        }));

        rules.Add(new LexerRule("\"[^\"]*\"", new[] { LexerState.SourceFileName }, (s, t) =>
        {
            s.PopState();
            s.SourceFileName = t.Substring(1, t.Length - 2);
            return null;
        }));

        rules.Add(new LexerRule(LexerPatterns.Integer, new[] { LexerState.SourceFileLine }, (s, t) =>
        {
            s.PopState();
            return null;
        }));

        // <incl,version,sourcefilename>\"[^"]* -- the backup rule for a missing end quote.
        rules.Add(new LexerRule(
            "\"[^\"]*",
            new[] { LexerState.Include, LexerState.Version, LexerState.SourceFileName },
            (s, t) =>
            {
                s.Error("end quote missing");
                s.PopState();
                return null;
            }));

        // The three string-reading conditions reject anything else, and say what they
        // wanted -- which is the whole reason they are separate states.
        rules.Add(new LexerRule("(?s).", new[] { LexerState.Version }, (s, t) =>
        {
            s.Error("quoted string expected after \\version");
            s.PopState();
            return null;
        }));

        rules.Add(new LexerRule("(?s).", new[] { LexerState.SourceFileName }, (s, t) =>
        {
            s.Error("quoted string expected after \\sourcefilename");
            s.PopState();
            return null;
        }));

        rules.Add(new LexerRule("(?s).", new[] { LexerState.SourceFileLine }, (s, t) =>
        {
            s.Error("integer expected after \\sourcefileline");
            s.PopState();
            return null;
        }));

        // <longcomment> -- and note a %{ inside does NOT nest another comment.
        rules.Add(new LexerRule("[^%]+", new[] { LexerState.LongComment }, (s, t) => null));
        rules.Add(new LexerRule("%*[^}%]*", new[] { LexerState.LongComment }, (s, t) => null));
        rules.Add(new LexerRule("%+\\}", new[] { LexerState.LongComment }, (s, t) =>
        {
            s.PopState();
            return null;
        }));
    }

    private static void AddIncludeAndMainInput(List<LexerRule> rules, ILexerHost host)
    {
        rules.Add(new LexerRule(@"\\maininput", MusicModes, (s, t) =>
        {
            if (!s.IsMainInput)
            {
                s.IsMainInput = true;
                LexerState state = s.State;
                s.PushState(LexerState.MainInput);
                s.PushState(state);
            }
            else
            {
                s.Error("\\maininput not allowed outside init files");
            }

            return null;
        }));

        rules.Add(new LexerRule(@"\\include", MusicModes, (s, t) =>
        {
            s.PushState(LexerState.Include);
            return null;
        }));

        rules.Add(new LexerRule("\"[^\"]*\"", new[] { LexerState.Include }, (s, t) =>
        {
            s.RequestedInclude = t.Substring(1, t.Length - 2);
            s.PopState();
            return null;
        }));

        rules.Add(new LexerRule(LexerPatterns.Command, new[] { LexerState.Include }, (s, t) =>
        {
            LexerLookup found = host.LookupIdentifier(t.Substring(1));

            // scm_is_string, which is BOTH string shapes: a name assigned in a .ly
            // arrives as a MutableString, and `is string` silently answered false for
            // it -- so PopState never ran, the lexer stayed in Include state and ate
            // the rest of the file.
            string name = found.Found ? ParserActionHelpers.SchemeStringText(found.Value) : null;
            if (name != null)
            {
                s.RequestedInclude = name;
                s.PopState();
            }
            else
            {
                s.Error("wrong or undefined identifier: `" + t.Substring(1) + "'");
            }

            return null;
        }));

        // <maininput>{ANY_CHAR} -- everything after \maininput's own file is discarded.
        rules.Add(new LexerRule("(?s).", new[] { LexerState.MainInput }, (s, t) => null));
    }

    private static void AddMusicWords(List<LexerRule> rules, ILexerHost host)
    {
        rules.Add(new LexerRule(LexerPatterns.RestName, ChordsNotesFigures, (s, t) =>
            s.Token(s.Terminal("RESTNAME"), ModalScanner.SchemeText(t))));

        rules.Add(new LexerRule("q", ChordsNotesFigures, (s, t) =>
            s.Token(s.Terminal("CHORD_REPETITION"))));

        rules.Add(new LexerRule("R", ChordsNotesFigures, (s, t) =>
            s.Token(s.Terminal("MULTI_MEASURE_REST"))));
    }

    private static void AddEmbeddedScheme(List<LexerRule> rules, ILexerHost host)
    {
        // #  -- embedded Scheme, READ but not evaluated: the grammar decides where the
        // datum is evaluated, because a SCM_TOKEN in an argument position and one in a
        // toplevel position mean different things. An unreadable expression raises the
        // error level right here (lexer.ll 412) and still produces its token, so the
        // parse continues and the file reports the rest of its errors.
        rules.Add(new LexerRule("#", EmbeddedSchemeModes, (s, t) =>
        {
            object value = s.ReadEmbeddedScheme(host);
            if (value is DefaultArgument)
            {
                host.ErrorLevel = 1;
            }

            return s.Token(s.Terminal("SCM_TOKEN"), value);
        }));

        // $ -- IMMEDIATE Scheme. Unlike `#', this one is evaluated by the lexer and the
        // token is chosen from what the value turned out to BE, which is why `$foo' can
        // stand in for a music expression, a pitch, a duration or a music function
        // anywhere the corresponding *_IDENTIFIER is allowed.
        rules.Add(new LexerRule("\\$", EmbeddedSchemeModes, (s, t) =>
            s.ReadImmediateScheme(host)));
    }

    private static void AddBrackets(List<LexerRule> rules)
    {
        LexerState[] angleModes =
        {
            LexerState.Initial, LexerState.Notes, LexerState.Lyrics, LexerState.Chords,
        };

        rules.Add(new LexerRule("<<", angleModes, (s, t) =>
            s.Token(s.Terminal("DOUBLE_ANGLE_OPEN"))));
        rules.Add(new LexerRule(">>", angleModes, (s, t) =>
            s.Token(s.Terminal("DOUBLE_ANGLE_CLOSE"))));

        LexerState[] singleAngleModes = { LexerState.Initial, LexerState.Notes, LexerState.Chords };
        rules.Add(new LexerRule("<", singleAngleModes, (s, t) => s.Token(s.Terminal("ANGLE_OPEN"))));
        rules.Add(new LexerRule(">", singleAngleModes, (s, t) => s.Token(s.Terminal("ANGLE_CLOSE"))));
    }

    private static void AddFigures(List<LexerRule> rules)
    {
        LexerState[] figures = { LexerState.Figures };

        rules.Add(new LexerRule("_", figures, (s, t) => s.Token(s.Terminal("FIGURE_SPACE"))));
        rules.Add(new LexerRule(">", figures, (s, t) => s.Token(s.Terminal("FIGURE_CLOSE"))));
        rules.Add(new LexerRule("<", figures, (s, t) => s.Token(s.Terminal("FIGURE_OPEN"))));
        rules.Add(new LexerRule(@"\\\+", figures, (s, t) => s.Token(s.Terminal("E_PLUS"))));
        rules.Add(new LexerRule(@"\\!", figures, (s, t) => s.Token(s.Terminal("E_EXCLAMATION"))));
        rules.Add(new LexerRule(@"\\\\", figures, (s, t) => s.Token(s.Terminal("E_BACKSLASH"))));

        rules.Add(new LexerRule(
            "(?:" + LexerPatterns.FigureAlterationExpression
            + @"|\[" + LexerPatterns.FigureAlterationExpression + @"\])",
            figures,
            (s, t) => s.Token(s.Terminal("FIGURE_ALTERATION_EXPR"), ModalScanner.SchemeText(t))));

        rules.Add(new LexerRule(@"[\[\]]", figures, (s, t) => s.CharacterToken(t[0])));
    }

    private static void AddNotesAndFigureWords(List<LexerRule> rules, ILexerHost host)
    {
        rules.Add(new LexerRule(LexerPatterns.Symbol, NotesFigures, (s, t) => s.ScanBareWord(host, t)));

        rules.Add(new LexerRule("\\\\\"", NotesFigures, (s, t) =>
        {
            s.StartCommandQuote();
            return null;
        }));

        rules.Add(new LexerRule(LexerPatterns.Command, NotesFigures, (s, t) =>
            s.ScanEscapedWord(host, t.Substring(1))));

        rules.Add(new LexerRule(LexerPatterns.Fraction, NotesFigures, (s, t) =>
            s.Token(s.Terminal("FRACTION"), ScanFraction(t))));

        rules.Add(new LexerRule(LexerPatterns.StrictReal, NotesFigures, (s, t) =>
            s.Token(s.Terminal("REAL"), double.Parse(t, System.Globalization.CultureInfo.InvariantCulture))));

        rules.Add(new LexerRule(LexerPatterns.Unsigned, NotesFigures, (s, t) =>
            s.Token(s.Terminal("UNSIGNED"), long.Parse(t, System.Globalization.CultureInfo.InvariantCulture))));

        rules.Add(new LexerRule(LexerPatterns.EscapedUnsigned, NotesFigures, (s, t) =>
            s.Token(
                s.Terminal("E_UNSIGNED"),
                long.Parse(t.Substring(1), System.Globalization.CultureInfo.InvariantCulture))));
    }

    private static void AddQuotes(List<LexerRule> rules, ILexerHost host)
    {
        rules.Add(new LexerRule(@"\\" + LexerPatterns.Escaped, Quotes, (s, t) =>
        {
            s.AppendToString(EscapedChar(t[1]).ToString());
            return null;
        }));

        rules.Add(new LexerRule("[^\\\\\"]+", Quotes, (s, t) =>
        {
            s.AppendToString(t);
            return null;
        }));

        rules.Add(new LexerRule("\"", Quotes, (s, t) =>
        {
            string text = s.FinishString();
            bool wasCommand = s.State == LexerState.CommandQuote;
            s.PopState();

            return wasCommand
                ? s.ScanEscapedWord(host, text)
                : s.Token(s.Terminal("STRING"), ModalScanner.SchemeText(text));
        }));

        rules.Add(new LexerRule("\\\\", Quotes, (s, t) =>
        {
            s.AppendToString(t);
            return null;
        }));

        // Opening a string, from every mode that allows one.
        rules.Add(new LexerRule(
            "\"",
            new[]
            {
                LexerState.Initial, LexerState.Notes, LexerState.Figures,
                LexerState.Chords, LexerState.Markup, LexerState.Lyrics,
            },
            (s, t) =>
            {
                s.StartQuote();
                return null;
            }));
    }

    private static void AddLyrics(List<LexerRule> rules, ILexerHost host)
    {
        LexerState[] lyrics = { LexerState.Lyrics };

        rules.Add(new LexerRule(LexerPatterns.Fraction, lyrics, (s, t) =>
            s.Token(s.Terminal("FRACTION"), ScanFraction(t))));

        rules.Add(new LexerRule(LexerPatterns.StrictReal, lyrics, (s, t) =>
            s.Token(s.Terminal("REAL"), double.Parse(t, System.Globalization.CultureInfo.InvariantCulture))));

        rules.Add(new LexerRule(LexerPatterns.Unsigned, lyrics, (s, t) =>
            s.Token(s.Terminal("UNSIGNED"), long.Parse(t, System.Globalization.CultureInfo.InvariantCulture))));

        rules.Add(new LexerRule("\\\\\"", lyrics, (s, t) =>
        {
            s.StartCommandQuote();
            return null;
        }));

        rules.Add(new LexerRule(LexerPatterns.Command, lyrics, (s, t) =>
            s.ScanEscapedWord(host, t.Substring(1))));

        rules.Add(new LexerRule(@"(?s)\\.|\|", lyrics, (s, t) => s.ScanShorthand(host, t)));

        /* Characters needed to express durations, assignments */
        rules.Add(new LexerRule(@"[*.=]", lyrics, (s, t) => s.CharacterToken(t[0])));

        /* ugr. This sux. */
        rules.Add(new LexerRule(
            "[^|*.=$#{}\"\\\\ \t\n\r\f0-9][^$#{}\"\\\\ \t\n\r\f0-9]*",
            lyrics,
            (s, t) =>
            {
                if (t == "__")
                {
                    return s.Token(s.Terminal("EXTENDER"));
                }

                if (t == "--")
                {
                    return s.Token(s.Terminal("HYPHEN"));
                }

                return s.Token(s.Terminal("SYMBOL"), ModalScanner.SchemeText(LyricFudge(t)));
            }));

        /* This should really just cover {} */
        rules.Add(new LexerRule(@"[{}]", lyrics, (s, t) => s.CharacterToken(t[0])));
    }

    private static void AddChords(List<LexerRule> rules, ILexerHost host)
    {
        LexerState[] chords = { LexerState.Chords };

        rules.Add(new LexerRule(LexerPatterns.Symbol, chords, (s, t) => s.ScanBareWord(host, t)));

        rules.Add(new LexerRule("\\\\\"", chords, (s, t) =>
        {
            s.StartCommandQuote();
            return null;
        }));

        rules.Add(new LexerRule(LexerPatterns.Command, chords, (s, t) =>
            s.ScanEscapedWord(host, t.Substring(1))));

        rules.Add(new LexerRule(LexerPatterns.Fraction, chords, (s, t) =>
            s.Token(s.Terminal("FRACTION"), ScanFraction(t))));

        rules.Add(new LexerRule(LexerPatterns.Unsigned, chords, (s, t) =>
            s.Token(s.Terminal("UNSIGNED"), long.Parse(t, System.Globalization.CultureInfo.InvariantCulture))));

        rules.Add(new LexerRule("-", chords, (s, t) => s.Token(s.Terminal("CHORD_MINUS"))));
        rules.Add(new LexerRule(":", chords, (s, t) => s.Token(s.Terminal("CHORD_COLON"))));
        rules.Add(new LexerRule(@"/\+", chords, (s, t) => s.Token(s.Terminal("CHORD_BASS"))));
        rules.Add(new LexerRule("/", chords, (s, t) => s.Token(s.Terminal("CHORD_SLASH"))));
        rules.Add(new LexerRule(@"\^", chords, (s, t) => s.Token(s.Terminal("CHORD_CARET"))));
    }

    private static void AddMarkup(List<LexerRule> rules, ILexerHost host)
    {
        LexerState[] markup = { LexerState.Markup };

        rules.Add(new LexerRule(@"\\score\b", markup, (s, t) => s.Token(s.Terminal("SCORE"))));
        rules.Add(new LexerRule(@"\\score-lines\b", markup, (s, t) => s.Token(s.Terminal("SCORELINES"))));

        rules.Add(new LexerRule("\\\\\"", markup, (s, t) =>
        {
            s.StartCommandQuote();
            return null;
        }));

        rules.Add(new LexerRule(LexerPatterns.Command, markup, (s, t) =>
        {
            string word = t.Substring(1);

            LexerLookup command = host.LookupMarkupCommand(
                word, out IReadOnlyList<MarkupPredicate> predicates);
            if (!command.Found)
            {
                // Neither a markup command nor a markup-list command: fall back to the
                // ordinary escaped-word lookup, exactly as upstream does.
                return s.ScanEscapedWord(host, word);
            }

            // If the list of predicates is, say, (number? number? markup?), then tokens
            // EXPECT_MARKUP EXPECT_SCM EXPECT_SCM EXPECT_NO_MORE_ARGS will be generated.
            // Note that we have to push them in REVERSE order, so the first token pushed
            // in the loop will be EXPECT_NO_MORE_ARGS.
            s.PushMarkupPredicates(predicates);

            return s.Token(s.Terminal(command.TokenName), command.Value);
        }));

        rules.Add(new LexerRule("[^$#{}\"\\\\ \t\n\r\f]+", markup, (s, t) =>
            s.Token(s.Terminal("SYMBOL"), ModalScanner.SchemeText(t))));

        rules.Add(new LexerRule(@"[{}]", markup, (s, t) => s.CharacterToken(t[0])));
    }

    private static void AddInitialWords(List<LexerRule> rules, ILexerHost host)
    {
        LexerState[] initial = { LexerState.Initial };

        rules.Add(new LexerRule(LexerPatterns.Symbol, initial, (s, t) => s.ScanBareWord(host, t)));

        rules.Add(new LexerRule("\\\\\"", initial, (s, t) =>
        {
            s.StartCommandQuote();
            return null;
        }));

        rules.Add(new LexerRule(LexerPatterns.Command, initial, (s, t) =>
            s.ScanEscapedWord(host, t.Substring(1))));
    }

    private static void AddNumbersAndSpecials(List<LexerRule> rules, ILexerHost host)
    {
        // These four are written with NO start condition in lexer.ll, which in flex
        // means every INCLUSIVE condition -- and since all thirteen are exclusive, that
        // is INITIAL alone.
        LexerState[] initial = { LexerState.Initial };

        rules.Add(new LexerRule(LexerPatterns.Fraction, initial, (s, t) =>
            s.Token(s.Terminal("FRACTION"), ScanFraction(t))));

        rules.Add(new LexerRule(LexerPatterns.Real, initial, (s, t) =>
            s.Token(s.Terminal("REAL"), double.Parse(t, System.Globalization.CultureInfo.InvariantCulture))));

        rules.Add(new LexerRule(LexerPatterns.Unsigned, initial, (s, t) =>
            s.Token(s.Terminal("UNSIGNED"), long.Parse(t, System.Globalization.CultureInfo.InvariantCulture))));

        rules.Add(new LexerRule(LexerPatterns.Special, MusicModes, (s, t) => s.CharacterToken(t[0])));

        rules.Add(new LexerRule(LexerPatterns.Shorthand, MusicModes, (s, t) => s.ScanShorthand(host, t)));

        // <*>.[\200-\277]* -- the last rule in the file, and the reason a stray byte
        // reports rather than scanning as text. It returns '%' deliberately: "Better
        // not return half a utf8 character."
        rules.Add(new LexerRule("(?s).[\u0080-\u00bf]*", null, (s, t) =>
        {
            s.Error("invalid character: `" + t + "'");
            return s.CharacterToken('%');
        }));
    }

    /// <summary>
    /// Converts <c>NUM/DEN</c> into a numerator/denominator pair.
    /// <para>
    /// Upstream: <c>scan_fraction</c> (<c>lexer.ll</c> 1300-1310), which returns
    /// <c>scm_cons (scm_c_read_string (left), scm_c_read_string (right))</c> — a
    /// SCHEME PAIR of exact integers, not a private tuple. The shape matters beyond
    /// this file: <c>FRACTION</c> is a semantic value in its own right
    /// (<c>embedded_scm_bare_arg</c>, <c>identifier_init</c>), so it reaches Scheme
    /// as a music-function argument and as the value of <c>\x = 3/4</c>, where
    /// <c>scale?</c> and <c>fraction?</c> test it with <c>pair?</c>; and
    /// <c>multipliers: multipliers '*' FRACTION</c> reads it with <c>scm_car</c> /
    /// <c>scm_cdr</c>.
    /// </para>
    /// </summary>
    /// <param name="text">The fraction as written.</param>
    /// <returns>The <c>(numerator . denominator)</c> pair.</returns>
    public static Pair ScanFraction(string text)
    {
        int slash = text.IndexOf('/');
        return new Pair(
            long.Parse(text.Substring(0, slash), System.Globalization.CultureInfo.InvariantCulture),
            long.Parse(text.Substring(slash + 1), System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Turns underscores into spaces in a lyric syllable, which is how a syllable
    /// carries a space without ending at it.
    /// </summary>
    /// <param name="text">The syllable.</param>
    /// <returns>The fudged syllable.</returns>
    public static string LyricFudge(string text) => text.Replace('_', ' ');

    /// <summary>Returns the character an escape sequence stands for.</summary>
    /// <param name="escape">The character after the backslash.</param>
    /// <returns>The character.</returns>
    public static char EscapedChar(char escape)
        => escape switch
        {
            'n' => '\n',
            't' => '\t',
            _ => escape,
        };
}
