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

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

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
    public ParseOutcome ParseText(string text, string fileName)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        EnsureTables();

        ModalScanner scanner = new ModalScanner(
            LilyPondLexerRules.Create(this), text, fileName ?? "<input>");
        scanner.UseSymbols(_tables.Symbols, _tables.TerminalCount);
        scanner.IncludeResolver = ResolveInclude;

        ModalScanner previous = Scanner;
        Scanner = scanner;
        LalrParser parser = new LalrParser(_tables, _boundActions);

        try
        {
            object result = parser.Parse(scanner, this);
            return new ParseOutcome(result, parser.ErrorCount, parser.Diagnostics, scanner.Diagnostics);
        }
        finally
        {
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
        string vendored = LilyPondScheme.ReadInitFile(name);
        if (vendored != null)
        {
            return vendored;
        }

        foreach (string directory in IncludePath)
        {
            string path = System.IO.Path.Combine(directory, name);
            if (System.IO.File.Exists(path))
            {
                return System.IO.File.ReadAllText(path);
            }
        }

        return null;
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
