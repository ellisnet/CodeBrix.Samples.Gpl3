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
/// parser. The one deliberate divergence is decision D20: the runner defines
/// <c>default-toplevel-book-handler</c> — the escape hatch <c>init.ly</c> itself
/// checks for — and takes each book's scores STRAIGHT to the SVG backend,
/// score by score, instead of through <c>ly:book-process</c>. Page assembly is
/// EPG16's subsystem; when it lands, the handler comes out and the real book
/// path goes in. Recorded in PORT-COVERAGE under DIVERGENCES.
/// </para>
/// </summary>
public static class BatchRunner
{
    private static readonly object Gate = new object();

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

        // THE session — the one the init layer was read into. A second session would
        // trip the interpreter's call-after-session guards, exactly as a second
        // init.ly run would upstream.
        LilyParserSession session = LilyPondInit.Session();
        int errorCount = RunLifecycle(session, text, baseName, includeDirectory, diagnostics);

        // D20: straight from each score to the SVG backend — one document per input
        // file, systems stacked in file order. Page assembly is EPG16's.
        List<Stencil> systems = new List<Stencil>();
        int skipped = 0;
        double unitLength = 0.0;
        foreach (Book book in books)
        {
            // Paper_book's CONSTRUCTOR scales the paper, and Book::process normalizes
            // the result — in that order, because normalize computes line-width from
            // dimensions that must already be in output units. Everything downstream is
            // then engraved in staff spaces rather than millimetres.
            OutputDef paper = book.Paper;
            if (paper != null)
            {
                double outputScale = paper.GetDimension("output-scale");
                if (outputScale > 0.0)
                {
                    paper = paper.ScaledClone(outputScale);
                    unitLength = outputScale;
                }

                paper.Normalize();
            }

            foreach (object entry in Pair.ToList(book.Scores))
            {
                if (!(entry is Score score))
                {
                    // Toplevel markup and page markers wait on the text interface
                    // (EPG13) and page layout (EPG16); a named absence beats a
                    // wrong drawing.
                    skipped++;
                    diagnostics.Add("skipped toplevel non-score entry: "
                        + (entry?.GetType().Name ?? "null"));
                    continue;
                }

                if (score.ErrorFound)
                {
                    diagnostics.Add("score skipped: parse marked it errored");
                    continue;
                }

                try
                {
                    OutputDef layout = ScoreLayout(score, paper, defaultLayout);
                    EngraveResult engraved = LilyPortEngraver.Engrave(
                        score.GetMusic() as MusicObject, layout);
                    systems.Add(engraved.Stencil);
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    diagnostics.Add("engraving failed: " + exception.Message);
                }
            }
        }

        string svgPath = null;
        if (systems.Count > 0)
        {
            Stencil page = StackSystems(systems);
            Directory.CreateDirectory(outputDirectory);
            svgPath = Path.Combine(outputDirectory, baseName + ".svg");

            // framework-svg.scm's (set-unit-length (lookup 'output-scale)) — the one
            // number the backend needs that is not in the stencil.
            SvgBackend backend = new SvgBackend();
            if (unitLength > 0.0)
            {
                backend.UnitLength = unitLength;
            }

            File.WriteAllText(svgPath, backend.RenderDocument(page));
        }

        return new BatchRunResult(
            svgPath,
            books.Count,
            systems.Count,
            skipped,
            errorCount,
            diagnostics);
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
    /// <c>Paper_book</c>'s scaling; until EPG16 lands that subsystem, the parenting
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
    /// Stacks system stencils top to bottom the way a single page would show them,
    /// separated by a staff-height's worth of padding.
    /// </summary>
    private static Stencil StackSystems(List<Stencil> systems)
    {
        const double padding = 4.0;

        if (systems.Count == 1)
        {
            return systems[0];
        }

        Stencil page = systems[0];
        for (int i = 1; i < systems.Count; i++)
        {
            page.AddAtEdge(Flower.Axis.Y, Flower.Direction.Negative, systems[i], padding);
        }

        return page;
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
    public BatchRunResult(
        string svgPath,
        int bookCount,
        int systemCount,
        int skippedEntries,
        int errorCount,
        IReadOnlyList<string> diagnostics)
    {
        SvgPath = svgPath;
        BookCount = bookCount;
        SystemCount = systemCount;
        SkippedEntries = skippedEntries;
        ErrorCount = errorCount;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the SVG file written, or <see langword="null"/> when nothing engraved.</summary>
    public string SvgPath { get; }

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
}
