/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
                 Jan Nieuwenhuizen <janneke@gnu.org>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Parsing.Actions;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lalr;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Session; //was previously: lily/lily-parser.cc (parse_file, parse_string);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <content>
/// Running a parse: the tables, the scanner, the driver and the <c>ly/</c> init layer.
/// </content>
public sealed partial class LilyParserSession
{
    private static readonly object TablesLock = new object();
    private static ParseTables _tables;
    private static IReadOnlyDictionary<int, RuleAction> _boundActions;

    /// <summary>
    /// Gets the LALR tables, generated once per process.
    /// <para>Generation reads the vendored <c>parser.yy</c> and builds the automaton;
    /// it takes a moment, and the result never varies, so it is shared.</para>
    /// </summary>
    public static ParseTables Tables
    {
        get
        {
            EnsureTables();
            return _tables;
        }
    }

    private static void EnsureTables()
    {
        if (_tables != null)
        {
            return;
        }

        lock (TablesLock)
        {
            if (_tables == null)
            {
                ParseTables tables = LalrGenerator.GenerateFromMirror();
                _boundActions = LilyPondRuleActions.Create().Bind(tables);
                _tables = tables;
            }
        }
    }

    /// <summary>
    /// Parses LilyPond source, running its toplevel expressions as it goes.
    /// <para>Upstream: <c>Lily_parser::parse_string</c> — the parse IS the execution,
    /// because a toplevel <c>\score</c> reaches its handler from the rule action that
    /// reduces it rather than from a later pass over a tree.</para>
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="fileName">The file's name, for locations.</param>
    /// <returns>What the parse produced and reported.</returns>
    public ParseOutcome ParseText(string text, string fileName) => ParseText(text, fileName, null);

    /// <summary>
    /// Parses LilyPond source, optionally entering the grammar at a different start
    /// symbol.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="fileName">The file's name, for locations.</param>
    /// <param name="startToken">The terminal to deliver before the input — upstream's
    /// <c>push_extra_token (Input (), EMBEDDED_LILY)</c>, which is how
    /// <c>ly:parse-string-expression</c> asks for a music expression rather than a whole
    /// file — or <see langword="null"/> for the ordinary toplevel entry.</param>
    /// <returns>What the parse produced and reported.</returns>
    public ParseOutcome ParseText(string text, string fileName, string startToken)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        EnsureTables();

        // The file has to be OPENED before it is scanned: every location the parse
        // produces is an offset into this text, and turning one into a real Input needs
        // the SourceFile to read it back from.
        OpenSource(fileName ?? "<input>", text);

        ModalScanner scanner = new ModalScanner(
            LilyPondLexerRules.Create(this), text, fileName ?? "<input>");
        scanner.UseSymbols(_tables.Symbols, _tables.TerminalCount);
        scanner.IncludeResolver = ResolveInclude;

        if (startToken != null)
        {
            scanner.PushExtraToken(new ParserToken(scanner.Terminal(startToken), null, default));
        }

        ModalScanner previous = Scanner;
        Scanner = scanner;
        _hasParsed = true;
        LalrParser parser = new LalrParser(_tables, _boundActions);

        // THE BASE MODE IS `notes', NOT `INITIAL'. Lily_lexer's constructor
        // (lily-lexer.cc 129, and again at 154 for the clone constructor) ends in
        // `push_note_state ()', so a lexer is in note mode from its first character and
        // nothing in the grammar ever pops down to INITIAL. That is why
        //
        //     ignatzekExceptionMusic = { <c e gis>-\markup { "+" } }
        //
        // reads as pitches at the top level of chord-modifiers-init.ly with no
        // \notemode anywhere in sight: there is no mode change, the file simply never
        // left note mode. A port whose base was INITIAL lexed every one of those as a
        // bare SYMBOL, which is a syntax error inside < >, and the diagnostic named the
        // note name rather than the mode — 41 of the init layer's 79 errors, in two
        // files, all from this one line.
        //
        // push_note_state also pushes the pitch-name table, so this must run AFTER the
        // scanner is live and be undone on the way out.
        PushNoteState();

        // Everything Lily_parser::parser_error reports lands in the SESSION's list rather
        // than the driver's — an embedded #(...) that raises is not a syntax error and
        // the driver never sees it. The outcome has to carry both, or a caller that reads
        // AllDiagnostics() is told a file parsed cleanly when its Scheme did not. The
        // ly/ init-layer fence is exactly such a caller.
        int diagnosticsBefore = Diagnostics.Count;

        try
        {
            // The %parser fluid is live for the whole parse, because every ly:parser-*
            // binding a rule action or an embedded #(...) reaches reads it to find out
            // which parser it is talking about.
            object result = AsCurrentParser(() => parser.Parse(scanner, this));

            List<string> reported = new List<string>(parser.Diagnostics);
            for (int i = diagnosticsBefore; i < Diagnostics.Count; i++)
            {
                reported.Add(Diagnostics[i]);
            }

            return new ParseOutcome(
                result,
                parser.ErrorCount + (Diagnostics.Count - diagnosticsBefore),
                reported,
                scanner.Diagnostics);
        }
        finally
        {
            // Carried off the scanner before it goes out of scope: the scanner is
            // per-ParseText and Scanner is restored to the caller's below, so a version
            // read here would otherwise be unreachable by the time the run's version
            // check needs it.
            if (scanner.MainInputVersionString != null)
            {
                MainInputVersionString = scanner.MainInputVersionString;
            }

            PopLexerState();
            Scanner = previous;
        }
    }

    /// <summary>
    /// Resolves an <c>\include</c> — first against the vendored <c>ly/</c> layer, then
    /// against whatever the caller added.
    /// <para>Upstream searches a path built from the installation's <c>ly/</c>
    /// directory and the input file's own directory. The port's <c>ly/</c> is an
    /// embedded resource, so it is searched by name rather than by path — which also
    /// means an init file cannot be shadowed by one sitting beside a regression
    /// input.</para>
    /// </summary>
    /// <param name="name">The file name as written.</param>
    /// <returns>The source text, or <see langword="null"/>.</returns>
    private string ResolveInclude(string name)
    {
        string text = LilyPondScheme.ReadInitFile(name);
        if (text == null)
        {
            foreach (string directory in IncludePath)
            {
                string path = System.IO.Path.Combine(directory, name);
                if (System.IO.File.Exists(path))
                {
                    text = System.IO.File.ReadAllText(path);
                    break;
                }
            }
        }

        if (text != null)
        {
            // Upstream: Includable_lexer::new_input goes through Sources::get_file, so
            // the included file joins the run's source set and stays there.
            OpenSource(name, text);
        }

        return text;
    }

    /// <summary>Gets the directories an <c>\include</c> searches after the vendored layer.</summary>
    public List<string> IncludePath { get; } = new List<string>();

    /// <summary>
    /// Runs the <c>ly/</c> initialisation layer, which is what turns a bare
    /// interpreter into a session that can read a real <c>.ly</c> file.
    /// <para>
    /// Upstream this is <c>ly/init.ly</c>, and it runs THROUGH THE PARSER: the note
    /// names, the durations, the context definitions, every music function and every
    /// <c>\override</c> shorthand are ordinary LilyPond assignments in ordinary
    /// <c>.ly</c> files. That is why nothing could read a regression input until the
    /// last rule action landed — the init layer needs the whole grammar before the
    /// first test file needs any of it.
    /// </para>
    /// <para>
    /// The port drives <c>declarations-init.ly</c> rather than <c>init.ly</c>, because
    /// <c>init.ly</c>'s job past the declarations is the SESSION lifecycle —
    /// <c>\maininput</c>, the toplevel book handler, the version check — which the
    /// caller owns here. Recorded in PORT-COVERAGE.
    /// </para>
    /// </summary>
    /// <returns>What the initialisation reported.</returns>
    public ParseOutcome LoadInitLayer()
    {
        string source = LilyPondScheme.ReadInitFile("declarations-init.ly");
        if (source == null)
        {
            throw new InvalidOperationException(
                "the ly/ init layer is not vendored — Scheme/ly/declarations-init.ly is missing");
        }

        return ParseText(source, "declarations-init.ly");
    }
}

/// <summary>What a parse produced, and what it reported on the way.</summary>
public sealed class ParseOutcome
{
    /// <summary>Initializes the outcome.</summary>
    /// <param name="result">The start symbol's value.</param>
    /// <param name="errorCount">How many syntax errors the driver reported.</param>
    /// <param name="diagnostics">The driver's diagnostics.</param>
    /// <param name="lexerErrors">The scanner's diagnostics.</param>
    public ParseOutcome(
        object result,
        int errorCount,
        IReadOnlyList<string> diagnostics,
        IReadOnlyList<string> lexerErrors)
    {
        Result = result;
        ErrorCount = errorCount;
        Diagnostics = diagnostics ?? new List<string>();
        LexerErrors = lexerErrors ?? new List<string>();
    }

    /// <summary>Gets the start symbol's semantic value.</summary>
    public object Result { get; }

    /// <summary>Gets how many syntax errors the driver reported.</summary>
    public int ErrorCount { get; }

    /// <summary>Gets the driver's diagnostics, in order.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>Gets the scanner's diagnostics, in order.</summary>
    public IReadOnlyList<string> LexerErrors { get; }

    /// <summary>Gets a value indicating whether the parse was clean.</summary>
    public bool Success => ErrorCount == 0 && LexerErrors.Count == 0;

    /// <summary>Returns every diagnostic, driver and scanner alike.</summary>
    /// <returns>The messages.</returns>
    public IReadOnlyList<string> AllDiagnostics()
    {
        List<string> all = new List<string>(Diagnostics);
        all.AddRange(LexerErrors);
        return all;
    }
}
