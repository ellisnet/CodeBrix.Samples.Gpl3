// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The OUTPUT half of the Scheme surface: scores, books, output definitions, page
/// markers, paper systems and the formatting entry point.
/// <para>
/// This is the layer <c>scm/lily.scm</c>'s toplevel handlers are written against. A
/// parsed <c>\score</c> reaches the engine only through <c>ly:score-embedded-format</c>
/// or <c>ly:book-process</c>, and both of those bottom out in <c>ly:run-translator</c>
/// followed by <c>ly:format-output</c> — so a stub anywhere along here stops a
/// <c>.ly</c> file producing output at all, with the parse having succeeded.
/// </para>
/// </summary>
public static class OutputPrimitives
{
    private static readonly Symbol OutputDefKindSymbol = Symbol.Intern("output-def-kind");
    private static readonly Symbol LayoutSymbol = Symbol.Intern("layout");
    private static readonly Symbol ClonedSymbol = Symbol.Intern("cloned");
    private static readonly Symbol OutputSymbol = Symbol.Intern("output");
    private static readonly Symbol OutputScaleSymbol = Symbol.Intern("output-scale");

    /// <summary>Installs the primitives, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallScores(interpreter);
        InstallBooks(interpreter);
        InstallOutputDefs(interpreter);
        InstallPageMarkers(interpreter);
        InstallPaperSystems(interpreter);
        InstallOutputters(interpreter);
    }

    /// <summary><c>paper-outputter-scheme.cc</c> — the six <c>ly:outputter-*</c> bindings.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallOutputters(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:make-paper-outputter", 2, 3, a =>
        {
            if (!(a[0] is SchemeOutputPort))
            {
                throw SchemeErrors.WrongType("ly:make-paper-outputter", "port", a[0]);
            }

            if (!(a[1] is Pair) && !(a[1] is Nil))
            {
                throw SchemeErrors.WrongType("ly:make-paper-outputter", "list", a[1]);
            }

            object defaultCallback =
                a.Length > 2 && !(a[2] is DefaultArgument) ? a[2] : (object)false;
            if (!(defaultCallback is bool) && !SchemeUtilities.IsProcedure(defaultCallback))
            {
                throw SchemeErrors.WrongType(
                    "ly:make-paper-outputter", "procedure", defaultCallback);
            }

            return new PaperOutputter(a[0], a[1], defaultCallback);
        });

        interpreter.DefinePrimitive("ly:outputter-dump-stencil", 2, 2, a =>
        {
            if (!(a[1] is Stencil stencil))
            {
                throw SchemeErrors.WrongType("ly:outputter-dump-stencil", "stencil", a[1]);
            }

            AsOutputter(a[0], "ly:outputter-dump-stencil").OutputStencil(stencil);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:outputter-dump-string", 2, 2, a =>
        {
            if (!(a[1] is MutableString) && !(a[1] is string))
            {
                throw SchemeErrors.WrongType("ly:outputter-dump-string", "string", a[1]);
            }

            return AsOutputter(a[0], "ly:outputter-dump-string").DumpString(a[1]);
        });

        interpreter.DefinePrimitive("ly:outputter-port", 1, 1, a =>
            AsOutputter(a[0], "ly:outputter-port").File);

        interpreter.DefinePrimitive("ly:outputter-close", 1, 1, a =>
        {
            AsOutputter(a[0], "ly:outputter-close").Close();
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:outputter-output-scheme", 2, 2, a =>
        {
            AsOutputter(a[0], "ly:outputter-output-scheme").OutputScheme(a[1]);
            return Unspecified.Instance;
        });
    }

    private static PaperOutputter AsOutputter(object value, string procedureName)
        => value as PaperOutputter
            ?? throw SchemeErrors.WrongType(procedureName, "paper outputter", value);

    /// <summary><c>score-scheme.cc</c> and <c>paper-score-scheme.cc</c>.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallScores(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:make-score", 1, 1, a =>
        {
            Score score = new Score();
            score.SetMusic(a[0] as MusicObject
                ?? throw SchemeErrors.WrongType("ly:make-score", "music", a[0]));
            return score;
        });

        interpreter.DefinePrimitive("ly:score-output-defs", 1, 1, a =>
        {
            List<object> defs = new List<object>();
            foreach (OutputDef def in AsScore(a[0], "ly:score-output-defs").Defs)
            {
                defs.Add(def);
            }

            return Pair.ListFrom(defs);
        });

        interpreter.DefinePrimitive("ly:score-add-output-def!", 2, 2, a =>
        {
            AsScore(a[0], "ly:score-add-output-def!").AddOutputDef(
                a[1] as OutputDef
                ?? throw SchemeErrors.WrongType(
                    "ly:score-add-output-def!", "output definition", a[1]));
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:score-header", 1, 1, a =>
            AsScore(a[0], "ly:score-header").GetHeader());

        interpreter.DefinePrimitive("ly:score-set-header!", 2, 2, a =>
        {
            if (!(a[1] is SchemeModule))
            {
                throw SchemeErrors.WrongType("ly:score-set-header!", "module", a[1]);
            }

            AsScore(a[0], "ly:score-set-header!").SetHeader(a[1]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:score-music", 1, 1, a =>
            AsScore(a[0], "ly:score-music").GetMusic());

        interpreter.DefinePrimitive("ly:score-error?", 1, 1, a =>
            AsScore(a[0], "ly:score-error?").ErrorFound);

        // Run score through layout (an output definition) scaled to correct
        // output-scale already, returning a list of layout lines.
        interpreter.DefinePrimitive("ly:score-embedded-format", 2, 2, a =>
        {
            Score score = AsScore(a[0], "ly:score-embedded-format");
            OutputDef enclosing = a[1] as OutputDef
                ?? throw SchemeErrors.WrongType(
                    "ly:score-embedded-format", "output definition", a[1]);

            if (score.ErrorFound)
            {
                return Nil.Instance;
            }

            /* UGR, FIXME, these are default \layout blocks once again.  They suck. */
            OutputDef scoreDef = null;
            foreach (OutputDef def in score.Defs)
            {
                if (ReferenceEquals(def.CVariable("output-def-kind"), LayoutSymbol))
                {
                    scoreDef = def;
                    break;
                }
            }

            if (scoreDef == null)
            {
                return false;
            }

            /* Don't rescale if the layout has already been scaled */
            scoreDef = SchemeUtilities.ToBool(scoreDef.CVariable("cloned"))
                ? scoreDef.Clone()
                : ScaleOutputDef(scoreDef, OutputScale(enclosing));

            scoreDef.Parent = enclosing;

            ContextDef globalDef = ContextDef.FindContextDef(
                scoreDef, Symbol.Intern("Global"));
            if (globalDef == null)
            {
                Warn.ProgrammingError("definition for Global context not found");
                return false;
            }

            GlobalContext global = new GlobalContext(scoreDef, globalDef);
            global.MakeGlobalTranslator();
            global.Iterate(score.GetMusic() as MusicObject);

            return FormatOutput(global);
        });

        interpreter.DefinePrimitive("ly:paper-score-paper-systems", 1, 1, a =>
        {
            PaperScore paperScore = a[0] as PaperScore
                ?? throw SchemeErrors.WrongType(
                    "ly:paper-score-paper-systems", "paper score", a[0]);

            List<object> systems = new List<object>();
            foreach (Prob system in paperScore.GetPaperSystems())
            {
                systems.Add(system);
            }

            // Upstream answers a VECTOR here, not a list, and scm/framework-*.scm
            // indexes into it — a list would fail at the first vector-ref.
            return systems.ToArray();
        });

        // Given a global context in its final state, process it and return the
        // Music_output object in its final state.
        interpreter.DefinePrimitive("ly:format-output", 1, 1, a =>
        {
            GlobalContext global = a[0] as GlobalContext
                ?? throw SchemeErrors.WrongType("ly:format-output", "global context", a[0]);
            return FormatOutput(global);
        });

        interpreter.DefinePrimitive("ly:music-output?", 1, 1, a => a[0] is MusicOutput);
        interpreter.DefinePrimitive("ly:score?", 1, 1, a => a[0] is Score);
        interpreter.DefinePrimitive("ly:paper-score?", 1, 1, a => a[0] is PaperScore);

        InstallPerformances(interpreter);
    }

    /// <summary>
    /// <c>performance-scheme.cc</c>, whole — both of its bindings.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallPerformances(Interpreter interpreter)
    {
        // Return the list of headers with the innermost first.
        interpreter.DefinePrimitive("ly:performance-headers", 1, 1, a =>
            AsPerformance(a[0], "ly:performance-headers").Headers);

        // Write PERFORMANCE to FILENAME storing NAME as the name of the performance in
        // the file metadata.
        interpreter.DefinePrimitive("ly:performance-write", 3, 3, a =>
        {
            Performance performance = AsPerformance(a[0], "ly:performance-write");

            string fileName = AsText(a[1])
                ?? throw SchemeErrors.WrongType("ly:performance-write", "string", a[1]);
            string name = AsText(a[2])
                ?? throw SchemeErrors.WrongType("ly:performance-write", "string", a[2]);

            performance.WriteOutput(fileName, name);
            return Unspecified.Instance;
        });
    }

    private static Performance AsPerformance(object value, string who)
        => value as Performance
            ?? throw SchemeErrors.WrongType(who, "performance", value);

    private static string AsText(object value)
        => value is MutableString mutable ? mutable.ToString() : value as string;

    /// <summary>
    /// <c>book-scheme.cc</c>, minus the two processing entry points.
    /// <para>
    /// <c>ly:book-process</c> and <c>ly:book-process-to-systems</c> both go through
    /// <c>Book::process</c> into a <c>Paper_book</c>, whose whole job is PAGE layout.
    /// The real implementations are registered by <c>PageBreakingCallbacks</c>; before
    /// that subsystem landed they stayed stubbed rather than half-built, because a book
    /// that silently produced one page per score would be indistinguishable from a
    /// correct one on every single-score regression file and wrong on every longer one.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallBooks(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:make-book", 2, -1, a =>
        {
            Book book = new Book
            {
                Paper = a[0] as OutputDef
                    ?? throw SchemeErrors.WrongType("ly:make-book", "output definition", a[0]),
            };

            if (a[1] is SchemeModule)
            {
                book.Header = a[1];
            }

            // Upstream (book-scheme.cc) never calls add_score here — it APPENDS the
            // whole list at once: book->scores_ = ly_append (scores, book->scores_).
            // init.ly passes toplevel-scores newest-first and Book.Process reverses on
            // the way out ("Render in order of parsing"), so the list must arrive AS
            // PASSED. Consing per score would reverse it a second time and render every
            // multi-score file backwards. Consing from the tail is that append.
            //
            // The rest arrives SPREAD — this interpreter's `apply' spreads its final
            // list — and every slot is one score-list ENTRY taken AS-IS: an entry may
            // itself be a list (a toplevel markup is collected as a markup LIST), and
            // flattening one level (which this once did) stripped that
            // wrapping so the entry failed is-markup-list and the whole book of a
            // toplevel \markup \score rendered NOTHING.
            for (int i = a.Length - 1; i >= 2; i--)
            {
                if (!(a[i] is DefaultArgument))
                {
                    book.AddScore(a[i]);
                }
            }

            return book;
        });

        // Upstream is 1-0-0: ONE required argument that IS the score list
        // (lily-library.scm hands it toplevel-scores whole). Same append contract as
        // ly:make-book — consing from the tail keeps the list as passed.
        interpreter.DefinePrimitive("ly:make-book-part", 1, 1, a =>
        {
            Book book = new Book();
            List<object> parts = Pair.ToList(a[0]);
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                book.AddScore(parts[i]);
            }

            return book;
        });

        interpreter.DefinePrimitive("ly:book-add-score!", 2, 2, a =>
        {
            AsBook(a[0], "ly:book-add-score!").AddScore(a[1]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:book-add-bookpart!", 2, 2, a =>
        {
            // Through AddBookpart, never a bare cons: upstream's binding calls
            // Book::add_bookpart, which wraps scores-so-far into an implicit part
            // FIRST — the ordering the sequence-name* books depend on.
            AsBook(a[0], "ly:book-add-bookpart!").AddBookpart(a[1]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:book-book-parts", 1, 1, a =>
            AsBook(a[0], "ly:book-book-parts").Bookparts);

        interpreter.DefinePrimitive("ly:book-paper", 1, 1, a =>
            (object)AsBook(a[0], "ly:book-paper").Paper ?? false);

        interpreter.DefinePrimitive("ly:book-header", 1, 1, a =>
        {
            object header = AsBook(a[0], "ly:book-header").Header;
            return header is SchemeModule ? header : false;
        });

        interpreter.DefinePrimitive("ly:book-set-header!", 2, 2, a =>
        {
            if (!(a[1] is SchemeModule))
            {
                throw SchemeErrors.WrongType("ly:book-set-header!", "module", a[1]);
            }

            AsBook(a[0], "ly:book-set-header!").Header = a[1];
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:book-scores", 1, 1, a =>
            AsBook(a[0], "ly:book-scores").Scores);

        interpreter.DefinePrimitive("ly:book?", 1, 1, a => a[0] is Book);
    }

    /// <summary>The rest of <c>output-def-scheme.cc</c>.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallOutputDefs(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:make-output-def", 0, 0, a => new OutputDef());

        interpreter.DefinePrimitive("ly:output-def-parent", 1, 2, a =>
        {
            OutputDef parent = AsOutputDef(a[0], "ly:output-def-parent").Parent;
            if (parent != null)
            {
                return parent;
            }

            return a.Length > 1 && !(a[1] is DefaultArgument) ? a[1] : Nil.Instance;
        });

        interpreter.DefinePrimitive("ly:output-def-set-variable!", 3, 3, a =>
        {
            AsOutputDef(a[0], "ly:output-def-set-variable!").SetVariable(
                a[1] as Symbol
                ?? throw SchemeErrors.WrongType("ly:output-def-set-variable!", "symbol", a[1]),
                a[2]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:paper-outputscale", 1, 1, a =>
            OutputScale(AsOutputDef(a[0], "ly:paper-outputscale")));

        // Font selection over an alist chain of grob properties. The music branches
        // (fetaMusic, fetaBraces) answer real metrics; the text branches are the
        // TextLayout bridge's and answered #f with a warning until the text interface landed — a named
        // absence, not a wrong font.
        interpreter.DefinePrimitive("ly:paper-get-font", 2, 2, a =>
        {
            Fonts.FontMetric font = Fonts.FontInterface.SelectFont(
                AsOutputDef(a[0], "ly:paper-get-font"), a[1]);
            return (object)font ?? false;
        });

        interpreter.DefinePrimitive("ly:paper-fonts", 1, 1, a =>
        {
            List<object> fonts = new List<object>();
            foreach (Fonts.FontMetric font in Fonts.FontInterface.PaperFonts(
                AsOutputDef(a[0], "ly:paper-fonts")))
            {
                fonts.Add(font);
            }

            return Pair.ListFrom(fonts);
        });

        // Both of these walk the definition's own scope and keep the entries whose KEY
        // is the definition's own context name. The test matters: \Staff also binds
        // aliases and clones under other names, and counting those would report a
        // context type twice.
        interpreter.DefinePrimitive("ly:output-description", 1, 1, a =>
        {
            List<object> entries = new List<object>();
            foreach (KeyValuePair<Symbol, object> entry
                     in AsOutputDef(a[0], "ly:output-description").Variables())
            {
                if (entry.Value is ContextDef definition
                    && ReferenceEquals(entry.Key, definition.ContextName))
                {
                    entries.Add(new Pair(entry.Key, definition.ToAlist()));
                }
            }

            return Pair.ListFrom(entries);
        });

        interpreter.DefinePrimitive("ly:output-find-context-def", 1, 2, a =>
        {
            object wanted = a.Length > 1 && !(a[1] is DefaultArgument) ? a[1] : null;
            if (wanted != null && !(wanted is Symbol))
            {
                throw SchemeErrors.WrongType("ly:output-find-context-def", "symbol", wanted);
            }

            List<object> entries = new List<object>();
            foreach (KeyValuePair<Symbol, object> entry
                     in AsOutputDef(a[0], "ly:output-find-context-def").Variables())
            {
                if (entry.Value is ContextDef definition
                    && ReferenceEquals(entry.Key, definition.ContextName)
                    && (wanted == null || definition.IsAlias(wanted)))
                {
                    entries.Add(new Pair(entry.Key, definition));
                }
            }

            return Pair.ListFrom(entries);
        });
    }

    /// <summary><c>page-marker-scheme.cc</c>.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallPageMarkers(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:make-page-permission-marker", 2, 2, a =>
        {
            PageMarker marker = new PageMarker();
            marker.SetPermission(
                a[0] as Symbol
                ?? throw SchemeErrors.WrongType(
                    "ly:make-page-permission-marker", "symbol", a[0]),
                a[1]);
            return marker;
        });

        interpreter.DefinePrimitive("ly:make-page-label-marker", 1, 1, a =>
        {
            PageMarker marker = new PageMarker();
            marker.SetLabel(
                a[0] as Symbol
                ?? throw SchemeErrors.WrongType("ly:make-page-label-marker", "symbol", a[0]));
            return marker;
        });

        interpreter.DefinePrimitive("ly:page-marker?", 1, 1, a => a[0] is PageMarker);
    }

    /// <summary><c>paper-system-scheme.cc</c>.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallPaperSystems(Interpreter interpreter)
        => interpreter.DefinePrimitive("ly:paper-system?", 1, 1, a => PaperSystem.Is(a[0]));

    /// <summary>
    /// Asks a finished context for what it produced and lays it out — upstream's
    /// <c>ly:format-output</c> body.
    /// </summary>
    internal static object FormatOutput(GlobalContext global)
    {
        // THE PROPERTY IS ON THE SCORE CONTEXT, NOT ON GLOBAL.
        // Upstream's Global_context::get_output does `get_property (get_score_context (),
        // "output")', and Score_engraver::finalize sets it on ITS OWN context — which is
        // the Score, a CHILD of Global. Reading it off Global walks UPWARD and therefore
        // never finds it, so this answered nothing at all for every caller: both
        // ly:format-output and ly:score-embedded-format. It was invisible because the one
        // path the sweep exercised, LilyPortEngraver, reaches the paper score by walking
        // the tree for the ScoreEngraver instead of by asking for this property.
        Context scoreContext = global.ScoreContext;
        object output = scoreContext != null
            ? scoreContext.GetProperty(OutputSymbol)
            : Nil.Instance;
        if (output is MusicOutput musicOutput)
        {
            musicOutput.Process();
        }

        return output;
    }

    /// <summary>
    /// Returns an output definition's <c>output-scale</c>, which is what every
    /// dimension in it is measured in.
    /// </summary>
    private static double OutputScale(OutputDef definition)
        => definition.GetDimension(OutputScaleSymbol);

    /// <summary>
    /// Returns a copy of an output definition with every dimension rescaled.
    /// <para>
    /// DIVERGENCE, recorded in PORT-COVERAGE: upstream's <c>scale_output_def</c> calls
    /// <c>scm/paper.scm</c>'s <c>scale-layout</c>, which rewrites the numeric variables
    /// in a fresh module. The port calls the same Scheme procedure when it is bound and
    /// falls back to a plain clone when it is not — a clone is the identity at scale 1,
    /// which is every case the port reaches today, and it is honest about doing nothing
    /// rather than applying a factor it did not compute.
    /// </para>
    /// </summary>
    internal static OutputDef ScaleOutputDef(OutputDef definition, double scale)
    {
        object procedure = LilyPondScheme.LookupProcedure(Symbol.Intern("scale-layout"));
        if (procedure != null)
        {
            object scaled = SchemeUtilities.CallCallback(procedure, definition, scale);
            if (scaled is OutputDef result)
            {
                return result;
            }
        }

        return definition.Clone();
    }


    private static Score AsScore(object value, string procedureName)
        => value as Score ?? throw SchemeErrors.WrongType(procedureName, "score", value);

    private static Book AsBook(object value, string procedureName)
        => value as Book ?? throw SchemeErrors.WrongType(procedureName, "book", value);

    private static OutputDef AsOutputDef(object value, string procedureName)
        => value as OutputDef
            ?? throw SchemeErrors.WrongType(procedureName, "output definition", value);
}
