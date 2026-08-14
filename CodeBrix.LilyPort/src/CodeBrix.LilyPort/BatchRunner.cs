// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.LilyPort.Backends;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Parsing.Session;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort;

/// <summary>
/// The INTERNAL in-process batch runner: a <c>.ly</c> file in, an SVG document per
/// file out, through the REAL toplevel handlers.
/// <para>
/// This is the machinery decision D14 kept when Lily.Shell took over the public
/// CLI: the port's Scheme startup makes a process per file unusable, so the
/// regression harness drives thousands of files through one process, serialised
/// (standing rule 8) because the engine's Scheme layer is process-global state.
/// </para>
/// <para>
/// The lifecycle is <c>ly/init.ly</c>'s own: the prologue and epilogue this class
/// parses around each file are VERBATIM extracts of the vendored file (see
/// <see cref="ProloguelLy"/> and <see cref="EpilogueLy"/> for exactly what and why),
/// so score collection, book construction, the version check and the
/// expect-error handshake are all upstream's real code running through the real
/// parser. D20's divergence is DISCHARGED since the page-layout group landed: the runner used to
/// define <c>default-toplevel-book-handler</c> — the escape hatch <c>init.ly</c> itself
/// checks for — and take each book's scores STRAIGHT to the SVG backend, score by score.
/// It now runs the real <c>Book::process</c> → <c>Paper_book</c> → page-breaker path and
/// writes ONE FILE PER PAGE under the oracle's own naming.
/// </para>
/// <para>
/// What it still does differently from upstream is WHERE book processing happens:
/// upstream reaches it from inside the parser, this runner collects books during the
/// parse and processes them after it. That is why the loop publishes <c>%parser</c>
/// itself — see the note on <see cref="LilyParserSession.AsCurrentParser"/>'s call site.
/// </para>
/// </summary>
public static class BatchRunner
{
    private static readonly object Gate = new object();

    /// <summary>
    /// The identifier <c>print-book-with</c> reads the layout out of at book-processing
    /// time. Looked up by NAME because a toplevel <c>\layout</c> block rebinds it.
    /// </summary>
    private const string DefaultLayoutName = "$defaultlayout";
    private const string DefaultPaperName = "$defaultpaper";

    /// <summary>
    /// <c>ly/init.ly</c> lines 27–35, verbatim: the session variables the toplevel
    /// handlers collect into. Parsing this before each file is also what RESETS the
    /// collection state between files — upstream resets by replaying the whole
    /// session (<c>session-replay</c>), which the port does not carry; identifier
    /// leakage between files of one batch is therefore possible and recorded as a
    /// divergence. <c>input-file-name</c> is added here because upstream's main
    /// loop defines it before init.ly runs, and the epilogue's version check reads
    /// it when present.
    /// </summary>
    private const string ProloguelLy = @"
#(define toplevel-scores (list))
#(define toplevel-bookparts (list))
#(define $defaultheader #f)
#(define $current-book #f)
#(define $current-bookpart #f)
#(define version-seen #f)
#(define expect-error #f)
#(define output-empty-score-list #f)
#(define output-suffix #f)
";

    /// <summary>
    /// <c>ly/init.ly</c> lines 54–91, verbatim except as noted: the version check,
    /// the book construction from what the toplevel handlers collected, the handler
    /// dispatch (which finds the runner's <c>default-toplevel-book-handler</c> by
    /// <c>init.ly</c>'s own <c>defined?</c> test), and the expect-error handshake.
    /// The <c>verbose</c> gc-stats block is omitted: it prints Guile's collector
    /// statistics, which do not exist here.
    /// </summary>
    private const string EpilogueLy = @"
#(cond
  ((not (defined? 'input-file-name)))

  ((not version-seen)
   (version-not-seen-message input-file-name))

  ((ly:parser-has-error?)
   (suggest-convert-ly-message version-seen)))

#(ly:set-option 'protected-scheme-parsing #f)

#(let ((book-handler (if (defined? 'default-toplevel-book-handler)
                         default-toplevel-book-handler
                         toplevel-book-handler)))
   (cond ((pair? toplevel-bookparts)
          (let ((book (ly:make-book $defaultpaper $defaultheader)))
            (for-each (lambda (part)
                        (ly:book-add-bookpart! book part))
                      (reverse! toplevel-bookparts))
            (set! toplevel-bookparts (list))
            ;; if scores have been defined after the last explicit \bookpart:
            (if (pair? toplevel-scores)
                (for-each (lambda (score)
                            (ly:book-add-score! book score))
                          (reverse! toplevel-scores)))
            (set! toplevel-scores (list))
            (book-handler book)))
         ((or (pair? toplevel-scores) output-empty-score-list)
          (let ((book (apply ly:make-book $defaultpaper
                             $defaultheader toplevel-scores)))
            (set! toplevel-scores (list))
            (book-handler book)))))

#(if (eq? expect-error (ly:parser-has-error?))
  (ly:parser-clear-error)
  (if expect-error
   (ly:parser-error (G_ ""expected error, but none found""))))
";

    /// <summary>
    /// Installs the two parse entry points that were waiting on this class:
    /// <c>ly:parse-file</c> (the full session lifecycle over a named file, books
    /// flowing to whatever toplevel book handler is bound) and <c>ly:parse-init</c>
    /// (a bare parse of one init file). Called by <see cref="LilyPondInit"/> once
    /// the layers are up, so the bindings exist in every real session.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void InstallSessionBindings(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        interpreter.DefinePrimitive("ly:parse-file", 1, 1, a =>
        {
            string name = ArgumentText(a[0], "ly:parse-file");
            string path = ResolveSource(name);
            if (path == null)
            {
                throw FileFailed(name);
            }

            LilyParserSession session = LilyPondInit.Session();
            List<string> diagnostics = new List<string>();
            int errors = RunLifecycle(
                session,
                File.ReadAllText(path),
                Path.GetFileNameWithoutExtension(path),
                Path.GetDirectoryName(Path.GetFullPath(path)),
                diagnostics);
            if (errors > 0)
            {
                throw FileFailed(name);
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:parse-init", 1, 1, a =>
        {
            string name = ArgumentText(a[0], "ly:parse-init");
            string text = LilyPondScheme.ReadInitFile(name);
            if (text == null && File.Exists(name))
            {
                text = File.ReadAllText(name);
            }

            if (text == null)
            {
                throw FileFailed(name);
            }

            // Upstream builds a FRESH parser over fresh Sources for this. The port
            // has one session per process (the call-after-session guards make a
            // second init-layer load impossible), so the shared session parses the
            // file directly — recorded in PORT-COVERAGE.
            LilyParserSession session = LilyPondInit.Session();
            ParseOutcome outcome = session.ParseText(text, name);
            if (!outcome.Success)
            {
                throw FileFailed(name);
            }

            return Unspecified.Instance;
        });
    }

    private static string ArgumentText(object value, string procedureName)
        => value is MutableString || value is string
            ? CodeBrix.LilyScheme.Primitives.StringPrimitives.Text(value, procedureName)
            : throw new ArgumentException(procedureName + ": expected a string file name");

    private static string ResolveSource(string name)
    {
        if (File.Exists(name))
        {
            return name;
        }

        string withExtension = name + ".ly";
        return File.Exists(withExtension) ? withExtension : null;
    }

    private static Exception FileFailed(string name)
        => new CodeBrix.LilyScheme.Runtime.SchemeThrow(
            Symbol.Intern("ly-file-failed"),
            Pair.List(new MutableString(name)));

    /// <summary>Runs one <c>.ly</c> file, writing its SVG beside nothing — into
    /// <paramref name="outputDirectory"/> under the file's base name.</summary>
    /// <param name="filePath">The <c>.ly</c> file to run.</param>
    /// <param name="outputDirectory">Where the <c>.svg</c> lands.</param>
    /// <returns>What the run produced and reported.</returns>
    public static BatchRunResult RunFile(string filePath, string outputDirectory)
    {
        if (filePath == null)
        {
            throw new ArgumentNullException(nameof(filePath));
        }

        return RunText(
            File.ReadAllText(filePath),
            Path.GetFileNameWithoutExtension(filePath),
            Path.GetDirectoryName(Path.GetFullPath(filePath)),
            outputDirectory);
    }

    /// <summary>Runs one file's text through the full pipeline.</summary>
    /// <param name="text">The LilyPond source.</param>
    /// <param name="baseName">The output base name, without extension.</param>
    /// <param name="includeDirectory">
    /// The directory the file's own <c>\include</c>s resolve against, or
    /// <see langword="null"/>.
    /// </param>
    /// <param name="outputDirectory">Where the <c>.svg</c> lands.</param>
    /// <returns>What the run produced and reported.</returns>
    public static BatchRunResult RunText(
        string text,
        string baseName,
        string includeDirectory,
        string outputDirectory)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (baseName == null)
        {
            throw new ArgumentNullException(nameof(baseName));
        }

        if (outputDirectory == null)
        {
            throw new ArgumentNullException(nameof(outputDirectory));
        }

        lock (Gate)
        {
            BatchRunResult result = null;
            Interpreter.RunWithLargeStack(() => result = RunLocked(
                text, baseName, includeDirectory, outputDirectory));
            return result;
        }
    }

    private static BatchRunResult RunLocked(
        string text,
        string baseName,
        string includeDirectory,
        string outputDirectory)
    {
        // Both layers, cached; this is also what guarantees an ambient interpreter.
        OutputDef defaultLayout = LilyPondInit.DefaultLayout();
        Interpreter interpreter = LilyPondScheme.Current;

        // One process per input file is what upstream gets for free and a batch runner
        // has to arrange. Without this, `#(set-global-staff-size 30)` in one regression
        // file rescales every file engraved after it.
        LilyPondInit.RestoreDefaults();

        List<string> diagnostics = new List<string>();
        List<Book> books = new List<Book>();

        // init.ly's own escape hatch, and D20's interception point: when this name is
        // bound, init.ly's epilogue hands each finished book HERE instead of to
        // ly:book-process. Rebound per run so a stale capture list can never leak
        // between runs.
        interpreter.DefinePrimitive("default-toplevel-book-handler", 1, 1, a =>
        {
            if (a[0] is Book book)
            {
                books.Add(book);
            }
            else
            {
                diagnostics.Add("toplevel book handler received a non-book: "
                    + (a[0]?.GetType().Name ?? "null"));
            }

            return Unspecified.Instance;
        });

        // The escape hatch above only catches the book init.ly's EPILOGUE builds from
        // implicit toplevel scores. An EXPLICIT \book block never gets there: the parser
        // hands it to `toplevel-book-handler' AT PARSE TIME, and the vendored binding
        // (declarations-init.ly: print-book-with-defaults → ly:book-process) computes the
        // pages and DISCARDS them — upstream's ly:book-process writes the output files
        // itself, this port collects pages off the paper book in the caller. So the
        // runner rebinds the parse-time name to the same collector, exactly the move
        // upstream's own lilypond-book-preamble.ly makes when IT wants books collected
        // instead of printed. Rebound per run for the same no-stale-capture reason as
        // the escape hatch. Before this, every explicit \book file was
        // NOOUT: "0 book(s)" with the book fully built — the sequence-name* MIDI rows.
        //
        // ⚠ The interpreter define alone is NOT ENOUGH: the parser resolves
        // `toplevel-book-handler' through ITS scope stack (Lily_lexer semantics), where
        // declarations-init.ly's #(define ...) landed — so the same procedure must also
        // be SET as a parser identifier, which happens below once the session is in hand.
        Primitive bookCollector = interpreter.DefinePrimitive("toplevel-book-handler", 1, 1, a =>
        {
            if (a[0] is Book book)
            {
                books.Add(book);
            }
            else
            {
                diagnostics.Add("toplevel book handler received a non-book: "
                    + (a[0]?.GetType().Name ?? "null"));
            }

            return Unspecified.Instance;
        });

        // THE session — the one the init layer was read into. A second session would
        // trip the interpreter's call-after-session guards, exactly as a second
        // init.ly run would upstream.
        LilyParserSession session = LilyPondInit.Session();

        // The parser half of the toplevel-book-handler rebind (see the note on the
        // collector above). RestoreDefaults ran at the top of this method, so this
        // per-run identifier survives the whole file and the NEXT run's restore wipes
        // it before its own re-set — no stale capture list can leak.
        session.SetIdentifier(Symbol.Intern("toplevel-book-handler"), bookCollector);
        int errorCount = RunLifecycle(session, text, baseName, includeDirectory, diagnostics);

        // One stencil per PAGE, as the page breaker chose them.
        // Until this group it was one per SCORE, stacked at a fixed padding into a single
        // document per input file -- which is why every multi-page reference page in the
        // oracle read as MISSING no matter how well the port engraved it.
        //
        // GROUPED PER BOOK: upstream names output PER
        // TOPLEVEL BOOK — get-outfile-name's counter-alist gives the first printed book
        // the bare base name and every further one `<base>-<n>' — and only within one
        // book's output does the SVG framework number the pages. Concatenating every
        // book's pages under one name mispaired a file holding both toplevel content
        // and an explicit \book against the oracle (header-book-multiplescores).
        List<List<Stencil>> bookPageGroups = new List<List<Stencil>>();

        // How many LINES the scores broke into, which is not how many scores there are.
        // Until line breaking landed this figure was systems.Count -- one per
        // score -- and every sweep log in the project reported it under the name
        // "system(s)". It read as a line count and was not one: accidental-styles.ly has
        // twenty scores and reported twenty systems before line breaking existed at all.
        int lines = 0;
        List<Performance> performances = new List<Performance>();
        int skipped = 0;
        double unitLength = 0.0;
        // THE PARSER STAYS CURRENT ACROSS ENGRAVING.
        //
        // Upstream never leaves the parser's dynamic extent to engrave: book processing
        // is reached from `default-toplevel-book-handler', which the PARSER calls, so
        // ly:parser-lookup and ly:parser-clone answer normally all the way down. D20's
        // score-level short-circuit moved the port's engraving OUT of that extent, and
        // The harness sweep measured what it cost: 18 files died on "there is no current
        // parser", because \markup \note asks for $defaultpaper while BUILDING ITS
        // STENCIL.
        //
        // ⚠ THIS IS NOT A STAND-IN AND IT DOES NOT RETIRE. The page-layout work inherited
        // it on the premise that moving the runner onto the real ly:book-process
        // path would make it unnecessary. That premise is WRONG and was MEASURED
        // wrong: with the runner fully on the book path, removing this wrapper puts
        // apply-output, fermata-dot-position, markup-rhythm-ragged and
        // flags-straight-layout-staff-size straight back on "there is no current parser".
        // The reason is that the book path is not the parser's DYNAMIC EXTENT. Upstream
        // reaches book processing from default-toplevel-book-handler, which the parser
        // calls WHILE PARSING, so %parser is live by construction; this runner collects
        // books during the parse and processes them after it, so it must publish %parser
        // itself. AsCurrentParser is upstream's own fluid, not a workaround. Retiring it
        // would mean processing each book from inside the toplevel handler, which is a
        // different design decision and not a clean-up.
        // THE LAYOUT IS RESOLVED BY NAME HERE, NOT CAPTURED BEFORE THE PARSE.
        //
        // `print-book-with' (scm/lily-library.scm) does (ly:parser-lookup '$defaultlayout)
        // at BOOK-PROCESSING time and hands the answer to ly:book-process, so a toplevel
        // \layout block is in place by then: parser.yy's toplevel_expression REBINDS the
        // $defaultlayout IDENTIFIER to the new definition rather than mutating the old
        // one. Reading the cached init-layer object instead — which is what this once
        // did — silently discarded every toplevel \layout in the suite:
        // \consists still worked, because a translator list is read off the definition
        // the layout block built, but no property operation from it ever ran, so
        // `\layout { \context { \Score scriptDefinitions = ... } }' set nothing at all.
        // Same shape as the earlier $defaultpaper finding, one identifier over.
        OutputDef parsedLayout
            = session.LookupIdentifier(DefaultLayoutName) as OutputDef ?? defaultLayout;

        // $defaultpaper is resolved BY NAME for the same reason $defaultlayout is: a
        // toplevel \paper block REBINDS the identifier rather than mutating the object,
        // so the one captured before the parse is the wrong one. It is what a book with
        // no \paper of its own falls back to.
        OutputDef parsedPaper = session.LookupIdentifier(DefaultPaperName) as OutputDef;

        session.AsCurrentParser(() =>
        {
        foreach (Book book in books)
        {
            // THE REAL ly:book-process PATH. D20's score-level
            // short-circuit is RETIRED: this used to walk the book's scores and hand each
            // one to LilyPortEngraver, producing one stacked drawing per input file. It
            // now runs Book::process, which builds a Paper_book, scales and normalizes its
            // paper, renders every score through Score::book_rendering, and then lets the
            // paper block's own `page-breaking' procedure choose the pages.
            //
            // The scaling that used to happen here is gone because Paper_book's
            // CONSTRUCTOR does it -- doing it twice would scale the paper squared.
            try
            {
                PaperBook paperBook = book.Process(parsedPaper, parsedLayout);
                if (paperBook == null)
                {
                    diagnostics.Add("book produced no paper book");
                    continue;
                }

                unitLength = paperBook.Paper.GetDimension("output-scale");

                List<Stencil> bookPages = new List<Stencil>();
                foreach (object entry in Pair.ToList(paperBook.Pages()))
                {
                    if (entry is Prob page && page.GetProperty("stencil") is Stencil pageStencil)
                    {
                        bookPages.Add(pageStencil);
                    }
                }

                bookPageGroups.Add(bookPages);

                lines += CountLines(paperBook);

                foreach (object performance in Pair.ToList(paperBook.Performances()))
                {
                    if (performance is Performance performed)
                    {
                        performances.Add(performed);
                    }
                }
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                diagnostics.Add("book processing failed: " + exception.Message);
            }
        }

            return Unspecified.Instance;
        });

        // ONE FILE PER PAGE, named the way scm/framework-svg.scm names them: a
        // single-page book is `<base>.svg' and a multi-page one is `<base>-1.svg'
        // upwards, counting from ONE. That naming is the ORACLE's, so the comparator
        // pairs a candidate with a reference by name alone -- and before the book path
        // landed the port
        // wrote one stacked `<base>.svg' for every file, which meant every page of every
        // multi-page reference was reported MISSING however well the music was engraved.
        // The OUTPUT NAME is per book (see bookPageGroups above): the first book prints
        // under the bare base name, the k-th under `<base>-<k>' — get-outfile-name's
        // counter — and the page numbering applies within each book's name.
        string svgPath = null;
        List<string> svgPaths = new List<string>();
        if (bookPageGroups.Exists(group => group.Count > 0))
        {
            Directory.CreateDirectory(outputDirectory);

            // framework-svg.scm's (set-unit-length (lookup 'output-scale)) — the one
            // number the backend needs that is not in the stencil.
            SvgBackend backend = new SvgBackend();
            if (unitLength > 0.0)
            {
                backend.UnitLength = unitLength;
            }

            for (int b = 0; b < bookPageGroups.Count; b++)
            {
                List<Stencil> bookPages = bookPageGroups[b];
                string bookName = b > 0 ? baseName + "-" + b : baseName;
                for (int i = 0; i < bookPages.Count; i++)
                {
                    string pagePath = Path.Combine(
                        outputDirectory,
                        bookPages.Count > 1
                            ? bookName + "-" + (i + 1) + ".svg"
                            : bookName + ".svg");

                    try
                    {
                        File.WriteAllText(pagePath, backend.RenderDocument(bookPages[i]));
                        svgPaths.Add(pagePath);
                    }
                    catch (Exception exception) when (!(exception is OutOfMemoryException))
                    {
                        diagnostics.Add("SVG output failed: " + exception.Message);
                    }
                }
            }

            svgPath = svgPaths.Count > 0 ? svgPaths[0] : null;
        }

        // scm/midi.scm's write-performances-midis names the files: the first performance
        // in a file gets `<base>.midi', and any further ones get `<base>-<n>.midi'
        // counting from 1. That naming is the ORACLE's, so the comparator can pair a
        // candidate with a reference by name alone.
        List<string> midiPaths = new List<string>();
        if (performances.Count > 0)
        {
            Directory.CreateDirectory(outputDirectory);

            for (int i = 0; i < performances.Count; i++)
            {
                // write-performances-midis counts from 0 and suffixes only when the
                // count is POSITIVE — so the FIRST performance is always `<base>.midi',
                // even in a file that goes on to produce more. The old `-1/-2' naming
                // for multi-performance files paired every candidate with the WRONG
                // reference: the port's first output was compared against the oracle's
                // second, and the oracle's first was reported missing.
                string midiPath = Path.Combine(
                    outputDirectory,
                    i > 0
                        ? baseName + "-" + i + ".midi"
                        : baseName + ".midi");

                try
                {
                    performances[i].WriteOutput(midiPath, PerformanceName(performances[i]));
                    midiPaths.Add(midiPath);
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    diagnostics.Add("MIDI output failed: " + exception.Message);
                }
            }
        }

        return new BatchRunResult(
            svgPath,
            books.Count,
            lines,
            skipped,
            errorCount,
            diagnostics,
            midiPaths,
            svgPaths);
    }

    /// <summary>
    /// Names a performance the way <c>scm/midi.scm</c>'s
    /// <c>write-performances-midis</c> does: <c>markup-&gt;string</c> of the headers'
    /// <c>midititle</c>, else <c>title</c>, else the empty string.
    /// </summary>
    /// <remarks>
    /// <c>performance-name-from-headers</c> is module-private in <c>(lily)</c>, so its
    /// two-lookup chain is reproduced through the same primitives rather than resolved
    /// by name. The runner once passed <see cref="string.Empty"/> here,
    /// which was right for every headerless regression file and wrong for the one that
    /// sets a title — the control track's name placeholder was erased instead of
    /// filled.
    /// </remarks>
    /// <param name="performance">The performance being written.</param>
    /// <returns>The name, possibly empty.</returns>
    private static string PerformanceName(Engine.Layout.Performance performance)
    {
        object lookup = LilyPondScheme.LookupProcedure(Symbol.Intern("ly:modules-lookup"));
        object markupToString = LilyPondScheme.LookupProcedure(Symbol.Intern("markup->string"));
        if (lookup == null || markupToString == null)
        {
            return string.Empty;
        }

        object title = SchemeUtilities.CallCallback(
            lookup, performance.Headers, Symbol.Intern("midititle"));
        if (title is bool noMidiTitle && !noMidiTitle)
        {
            title = SchemeUtilities.CallCallback(
                lookup, performance.Headers, Symbol.Intern("title"));
        }

        if (title is bool noTitle && !noTitle)
        {
            return string.Empty;
        }

        return SchemeUtilities.CallCallback(markupToString, title)?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Returns a score's <c>\midi</c> output definition, or <see langword="null"/> when
    /// the score asks for no MIDI.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ScoreLayout"/> there is no falling back to a default: a score
    /// without a <c>\midi</c> block produces no MIDI at all, and inventing one would give
    /// every one of the 2,146 regression files a MIDI file the oracle does not have.
    /// </remarks>
    private static OutputDef ScoreMidi(Score score)
    {
        Symbol midiSymbol = Symbol.Intern("midi");

        foreach (OutputDef def in score.Defs)
        {
            if (ReferenceEquals(def.CVariable("output-def-kind"), midiSymbol))
            {
                return def;
            }
        }

        return null;
    }

    /// <summary>
    /// The session lifecycle around one file: prologue (init.ly's collection-state
    /// defines, which is also the per-file reset), the file itself, and the epilogue
    /// (init.ly's version check, book construction and handler dispatch). Books flow
    /// to whichever toplevel book handler is bound when the epilogue runs.
    /// </summary>
    /// <returns>The parse error count of the file and epilogue together.</returns>
    private static int RunLifecycle(
        LilyParserSession session,
        string text,
        string baseName,
        string includeDirectory,
        List<string> diagnostics)
    {
        if (includeDirectory != null)
        {
            session.IncludePath.Add(includeDirectory);
        }

        try
        {
            session.SetIdentifier("input-file-name", new MutableString(baseName + ".ly"));

            ParseOutcome prologue = session.ParseText(ProloguelLy, "<batch-prologue>");
            diagnostics.AddRange(prologue.AllDiagnostics());

            ParseOutcome parsed = session.ParseText(text, baseName + ".ly");
            diagnostics.AddRange(parsed.AllDiagnostics());

            ParseOutcome epilogue = session.ParseText(EpilogueLy, "<batch-epilogue>");
            diagnostics.AddRange(epilogue.AllDiagnostics());

            return parsed.ErrorCount + epilogue.ErrorCount;
        }
        finally
        {
            if (includeDirectory != null)
            {
                session.IncludePath.Remove(includeDirectory);
            }
        }
    }

    /// <summary>
    /// The layout a score engraves under: its own <c>\layout</c> block when it has
    /// one, the session's <c>$defaultlayout</c> otherwise, parented into the book's
    /// paper so paper variables resolve. The upstream path runs this through
    /// <c>Paper_book</c>'s scaling; here the parenting
    /// carries the variables and the scale stays 1 (PORT-COVERAGE, DIVERGENCES).
    /// </summary>
    private static OutputDef ScoreLayout(Score score, OutputDef paper, OutputDef defaultLayout)
    {
        Symbol layoutSymbol = Symbol.Intern("layout");
        OutputDef found = null;
        foreach (OutputDef def in score.Defs)
        {
            if (ReferenceEquals(def.CVariable("output-def-kind"), layoutSymbol))
            {
                found = def;
                break;
            }
        }

        OutputDef layout = found ?? defaultLayout;

        if (paper != null)
        {
            // Score::book_rendering scales the score's layout by the BOOK paper's
            // output-scale and re-parents it onto that paper, unconditionally. The
            // re-parenting used to happen only for an unparented layout, which meant the
            // $defaultlayout kept pointing at the ORIGINAL $defaultpaper while the book
            // carried a clone of it; every paper variable the book computed, line-width
            // among them, then resolved to nothing.
            //
            // output-scale is deliberately NOT a dimension variable, so the scaled paper
            // still reports the original factor and this reads the same number the paper
            // was scaled by.
            double outputScale = paper.GetDimension("output-scale");
            OutputDef scaled = outputScale > 0.0 ? layout.ScaledClone(outputScale) : layout;

            // ScaledClone answers the SAME object when the Scheme layer is unreachable.
            // Re-parenting that would reach into a definition other scores share.
            layout = ReferenceEquals(scaled, layout) ? layout.Clone() : scaled;
            layout.Parent = paper;
        }

        return layout;
    }

    /// <summary>
    /// How many LINES the book came to, summed over its pages — the figure the sweep log
    /// reports under "system(s)".
    /// <para>Counted off each page's <c>lines</c> property rather than off the page count,
    /// because a page carries as many systems as the breaker put on it.</para>
    /// </summary>
    private static int CountLines(PaperBook paperBook)
    {
        int total = 0;
        foreach (object entry in Pair.ToList(paperBook.Pages()))
        {
            if (entry is Prob page)
            {
                total += Pair.ToList(page.GetProperty("lines")).Count;
            }
        }

        return total;
    }

}

/// <summary>What one batch run produced.</summary>
public sealed class BatchRunResult
{
    /// <summary>Initializes a result.</summary>
    /// <param name="svgPath">The SVG written, or <see langword="null"/>.</param>
    /// <param name="bookCount">How many books the toplevel handlers produced.</param>
    /// <param name="systemCount">How many scores engraved to a system.</param>
    /// <param name="skippedEntries">Toplevel entries skipped as not-yet-portable.</param>
    /// <param name="errorCount">Parse and epilogue errors.</param>
    /// <param name="diagnostics">Everything reported along the way.</param>
    /// <param name="midiPaths">The MIDI files written, in performance order.</param>
    /// <param name="svgPaths">The SVG pages written, in page order.</param>
    public BatchRunResult(
        string svgPath,
        int bookCount,
        int systemCount,
        int skippedEntries,
        int errorCount,
        IReadOnlyList<string> diagnostics,
        IReadOnlyList<string> midiPaths = null,
        IReadOnlyList<string> svgPaths = null)
    {
        SvgPath = svgPath;
        SvgPaths = svgPaths ?? (svgPath != null
            ? new[] { svgPath }
            : System.Array.Empty<string>());
        BookCount = bookCount;
        SystemCount = systemCount;
        SkippedEntries = skippedEntries;
        ErrorCount = errorCount;
        Diagnostics = diagnostics;
        MidiPaths = midiPaths ?? System.Array.Empty<string>();
    }

    /// <summary>Gets the FIRST SVG file written, or <see langword="null"/> when nothing engraved.</summary>
    public string SvgPath { get; }

    /// <summary>
    /// Gets every SVG file written, one per page, in page order.
    /// <para>On the book path a file may produce several. The driver's self-check counts what
    /// is on disk against what was reported written, so reporting only the first page
    /// would make every later page of every multi-page book look like a stale leftover.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> SvgPaths { get; }

    /// <summary>Gets how many books the toplevel handlers delivered.</summary>
    public int BookCount { get; }

    /// <summary>Gets how many scores engraved to a system.</summary>
    public int SystemCount { get; }

    /// <summary>Gets how many toplevel entries were skipped as not yet portable.</summary>
    public int SkippedEntries { get; }

    /// <summary>Gets the parse error count.</summary>
    public int ErrorCount { get; }

    /// <summary>Gets everything the run reported.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>Gets the MIDI files this run wrote, in performance order.</summary>
    public IReadOnlyList<string> MidiPaths { get; }
}
