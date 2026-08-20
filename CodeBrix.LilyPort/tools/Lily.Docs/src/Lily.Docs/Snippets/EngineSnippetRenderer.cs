// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CodeBrix.LilyPort;
using CodeBrix.Texinfo2Html;

namespace Lily.Docs.Snippets;

/// <summary>
/// Engraves a manual's music snippets with the port's own engine, as the Texinfo
/// packages' <see cref="ILilypondSnippetRenderer"/>.
/// <para>
/// This is a CONSUMER of the engine, and it lives in Lily.Docs rather than in the engine
/// assembly for that reason: the engine is finished, and composing documentation snippets
/// is a documentation job. It composes each snippet the way lilypond-book composes it
/// (<see cref="LilypondSourceComposer"/>) and then runs it as one engine file.
/// </para>
/// <para>
/// ⚠ SEQUENTIAL, ALWAYS. The engine is process-global — one interpreter, one session,
/// one working directory — so two snippets cannot engrave at once, and the lock here says
/// so rather than leaving it to the caller's calling pattern to be true by accident.
/// </para>
/// <para>
/// ⚠ EACH SNIPPET IS ONE FILE, AND PER-FILE LEAKS ARE NOW PER-SNIPPET LEAKS.
/// <c>BatchRunner.RunText</c> restores the session per run, which is the machinery that
/// closed eleven measured leaks; the twelfth class is still open upstream of us. A
/// snippet that renders correctly alone and wrongly inside a full manual is this class of
/// fault FIRST, and one snippet costs seconds to re-run on its own.
/// </para>
/// </summary>
public sealed class EngineSnippetRenderer : ILilypondSnippetRenderer, IDisposable
{
    /// <summary>
    /// Saved before the composed source is parsed: the batch runner installs its own
    /// <c>default-toplevel-book-handler</c>, and <c>lilypond-book-preamble.ly</c> — which
    /// the composed source includes, exactly as lilypond-book writes it — REPLACES that
    /// handler with <c>print-book-with-defaults-as-systems</c>.
    /// <para>
    /// ⚠ MEASURED 2026-08-19: with the preamble's handler in force the engine reports
    /// <c>books=0, errors=0</c> and writes NO file. The book really is processed — the run
    /// prints "Fitting music on 1 page" — but it goes to <c>ly:book-process-to-systems</c>,
    /// whose output half (<c>Paper_book::classic_output</c>, the <c>-dcrop</c>/
    /// <c>-daux-files</c> EPS machinery) is deliberately UNPORTED and is recorded as such
    /// in the Engine's PORT-COVERAGE. So the snippet vanishes silently: no error, no
    /// diagnostic, no picture. Saving the runner's handler here and restoring it after the
    /// music is what puts the engraving back on the page path the port does implement.
    /// </para>
    /// <para>
    /// ⚠ AND RESTORING IT AT THE END IS NOT ENOUGH — MEASURED 2026-08-19, THIRTY-FIVE
    /// SNIPPETS. The epilogue's restore comes after the whole file has been read, which is
    /// in time for the IMPLICIT toplevel book (collected at end of parse) and far too late
    /// for an EXPLICIT one: a snippet that writes <c>\book { … }</c> hands it over the
    /// moment the block closes, through <c>toplevel-book-handler</c> — which the preamble
    /// has by then pointed at <c>print-book-with-defaults</c>, a route that bypasses the
    /// runner's collector entirely. Thirty-five of the notation manual's snippets do exactly
    /// that, and every one of them vanished in the same silence.
    /// </para>
    /// <para>
    /// So the prologue does not merely SAVE the handler: it re-points the two functions the
    /// preamble's handlers CALL — <c>print-book-with-defaults-as-systems</c> and
    /// <c>print-book-with-defaults</c> — at the collector. Whatever the preamble installs
    /// afterwards therefore still arrives here. This is the same substitution as before,
    /// made at the place that survives being overwritten: the port implements the page
    /// output path and not the systems one, so every book takes the page path.
    /// <c>LilyPondInit.RestoreDefaults</c> reverts a run's toplevel definitions, so all
    /// three definitions are per-snippet and leak into nothing.
    /// </para>
    /// </summary>
    private const string EngravingPrologue =
        "#(define lily-docs-page-handler default-toplevel-book-handler)\n"
        + "#(define print-book-with-defaults-as-systems lily-docs-page-handler)\n"
        + "#(define print-book-with-defaults lily-docs-page-handler)\n";

    /// <summary>
    /// Restores the page handler and asks for ONE PAGE SIZED TO THE MUSIC.
    /// <para>
    /// ⚠ THIS IS NOT PART OF THE COMPOSED SOURCE, AND MUST NEVER BECOME PART OF IT.
    /// lilypond-book's own output shaping is not in the file either: it is passed to
    /// LilyPond on the command line (<c>book_texinfo.py</c>'s <c>adjust_snippet_command</c>
    /// adds <c>-dseparate-page-formats=png,pdf</c> and <c>-dtall-page-formats=eps,png</c>,
    /// and the crop behaviour rides on those). The port has no command line — decision D14
    /// replaced it — so the same instructions arrive as engine configuration. Keeping them
    /// OUT of <see cref="ComposedSnippet.Source"/> is what lets that source stay
    /// byte-identical to the oracle's, which is the claim
    /// <c>LilypondSourceComposerTests</c> actually fences.
    /// </para>
    /// <para>
    /// <c>ly:one-page-breaking</c> is the choice because it breaks lines normally and then
    /// sizes the PAGE to what those lines need, which is a snippet picture. MEASURED
    /// 2026-08-19 for the same two-note snippet: the default breaker gives
    /// 156.0mm × 273.1mm — a page-tall band of whitespace under the music — and
    /// one-page-breaking gives 156.0mm × 20.8mm. A 160-note snippet grows to
    /// 159.4mm × 126.2mm, i.e. it STACKS into several systems rather than running off in
    /// one line, which is why <c>ly:one-line-auto-height-breaking</c> was not the choice
    /// (measured: it left the height at 273.1mm here).
    /// </para>
    /// <para>
    /// The WIDTH needs nothing: it is already the music's own width, because the preamble
    /// the composed source includes sets <c>use-paper-size-for-page</c> to false and the
    /// composed <c>\paper</c> block computes <c>line-width</c> down by the left padding.
    /// That is upstream's own mechanism doing upstream's own job — measured 156.0mm for a
    /// <c>line-width = 160\mm</c> snippet with 3mm padding, and 89.0mm for
    /// <c>papersize=a6</c>.
    /// </para>
    /// </summary>
    private const string EngravingEpilogue =
        "\n#(set! default-toplevel-book-handler lily-docs-page-handler)\n"
        + "\\paper { page-breaking = #ly:one-page-breaking }\n";

    private static readonly object EngineGate = new object();

    /// <summary>
    /// The directories already appended to the engine's include path, PROCESS-WIDE.
    /// <para>
    /// ⚠ The engine's parser session is cached and shared — <c>LilyPondInit.Session()</c>
    /// returns the same object every run, and <c>RestoreDefaults</c> restores paper, layout
    /// and the toplevel scope but NOT the include path. So an append made for one snippet
    /// is still there for the next, which is what makes installing once correct and
    /// installing per snippet a leak: this manual would have appended six directories two
    /// and a half thousand times, and every <c>\include</c> in it searches the whole list.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> InstalledIncludeDirectories =
        new HashSet<string>(StringComparer.Ordinal);

    private readonly LilypondSourceComposer _composer;
    private readonly IReadOnlyList<string> _includeDirectories;
    private readonly string _scratchRoot;
    private readonly bool _ownsScratchRoot;
    private readonly List<SnippetFailure> _failures = new List<SnippetFailure>();
    private readonly List<string> _declines = new List<string>();
    private int _counter;
    private bool _disposed;

    /// <summary>
    /// Creates a renderer.
    /// </summary>
    /// <param name="geometry">The page geometry of the manual being rendered, which
    /// supplies lilypond-book's formatter defaults.</param>
    /// <param name="scratchRoot">A directory to engrave into, or null to take a temporary
    /// one and delete it on disposal.</param>
    /// <param name="includeDirectories">Where the engine looks for the files a snippet's own
    /// <c>\include</c> and <c>\epsfile</c> name — upstream's
    /// <c>LILYPOND_BOOK_INCLUDE_DIRS</c>, which arrives here as
    /// <c>RenderPaths.SnippetIncludePaths</c>. Null or empty for a snippet set that names no
    /// files, which is what the probe document is.</param>
    public EngineSnippetRenderer(TexinfoPageGeometry geometry, string scratchRoot,
        IReadOnlyList<string> includeDirectories = null)
    {
        _composer = new LilypondSourceComposer(geometry);
        _includeDirectories = includeDirectories ?? Array.Empty<string>();
        if (string.IsNullOrEmpty(scratchRoot))
        {
            _scratchRoot = Path.Combine(Path.GetTempPath(),
                "lilydocs-snippets-" + Guid.NewGuid().ToString("N").Substring(0, 12));
            _ownsScratchRoot = true;
        }
        else
        {
            _scratchRoot = Path.GetFullPath(scratchRoot);
        }

        Directory.CreateDirectory(_scratchRoot);
    }

    /// <summary>
    /// How many times the coordinator ASKED for an engraving.
    /// <para>
    /// ⚠ This is not the number of snippets in the manual. The coordinator engraves an
    /// identical snippet once and reuses the picture, so invocations are the DISTINCT
    /// snippets. Gate on this and on <see cref="FailureCount"/> together: a caught
    /// exception becomes a verbatim block and a plausible-looking manual, so a completed
    /// render proves nothing on its own.
    /// </para>
    /// </summary>
    public int InvocationCount { get; private set; }

    /// <summary>How many invocations produced at least one picture.</summary>
    public int EngravedCount { get; private set; }

    /// <summary>How many pictures were produced in total, over every invocation.</summary>
    public int PageCount { get; private set; }

    /// <summary>How many invocations reported a failure. Must be zero.</summary>
    public int FailureCount => _failures.Count;

    /// <summary>
    /// How many invocations DECLINED — returned <c>NotRendered</c>, so the document shows
    /// the snippet's source instead. A decline is legitimate only for a kind that is out
    /// of scope, so the count is asserted against the measured set rather than tolerated.
    /// </summary>
    public int DeclineCount => _declines.Count;

    /// <summary>Every failure, with the snippet it came from.</summary>
    public IReadOnlyList<SnippetFailure> Failures => _failures;

    /// <summary>Every decline, described.</summary>
    public IReadOnlyList<string> Declines => _declines;

    /// <summary>Where the engravings were written.</summary>
    public string ScratchRoot => _scratchRoot;

    /// <inheritdoc/>
    public LilypondSnippetResult Render(LilypondSnippet snippet)
    {
        if (snippet == null)
        {
            throw new ArgumentNullException(nameof(snippet));
        }

        InvocationCount++;

        // MusicXML conversion is a separate upstream tool (musicxml2ly) and is out of
        // Phase 5's scope, so this declines rather than guessing. The document then shows
        // the reference verbatim and warns, which is the honest outcome.
        if (snippet.Kind == LilypondSnippetKind.MusicXmlFile)
        {
            _declines.Add("@musicxmlfile " + snippet.FileName + " at "
                + Describe(snippet) + ": MusicXML is out of scope");
            return LilypondSnippetResult.NotRendered;
        }

        // "FilePath is empty when the named file was not found, and a renderer handed an
        // empty path should decline rather than guess" — the package's own words.
        if (snippet.Kind == LilypondSnippetKind.LilypondFile
            && string.IsNullOrEmpty(snippet.FilePath))
        {
            _declines.Add("@lilypondfile " + snippet.FileName + " at " + Describe(snippet)
                + ": the file was not found on the search path");
            return LilypondSnippetResult.NotRendered;
        }

        ComposedSnippet composed;
        try
        {
            composed = snippet.Kind == LilypondSnippetKind.LilypondFile
                ? _composer.ComposeFile(snippet.FileName,
                    File.ReadAllText(snippet.FilePath), snippet.Options.All)
                : _composer.Compose(snippet.Source, snippet.Options.All, snippet.LineNumber);
        }
        catch (Exception error)
        {
            return Fail(snippet, "composing the source failed: " + error.Message);
        }

        return Engrave(snippet, composed);
    }

    /// <summary>
    /// Wraps a composed source in the engine configuration one engraving needs, WITHOUT
    /// altering the composed source itself.
    /// </summary>
    /// <param name="composedSource">The source as lilypond-book would have composed it.</param>
    /// <returns>What the engine is actually handed.</returns>
    /// <remarks>
    /// Exposed so a test can assert the separation rather than take it on trust: the
    /// composed source must appear in the engraved text verbatim and unmodified, and the
    /// directives must be the only difference.
    /// </remarks>
    public static string EngravingTextFor(string composedSource)
        => EngravingPrologue + (composedSource ?? string.Empty) + EngravingEpilogue;

    /// <summary>Deletes the scratch directory when this renderer owns it.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_ownsScratchRoot)
        {
            return;
        }

        try
        {
            Directory.Delete(_scratchRoot, true);
        }
        catch (IOException)
        {
            // A scratch directory that will not delete is not worth failing a render over;
            // it is under the temporary root and the next boot clears it.
        }
    }

    /// <summary>
    /// Appends this renderer's include directories to the engine's own include path, once
    /// per process, the way upstream's <c>-I</c> flags do it for a LilyPond process.
    /// <para>
    /// The lever is <c>ly:parser-append-to-include-path</c> — an UPSTREAM primitive
    /// (<c>lily/lily-parser-scheme.cc</c>) the port implements, not something invented here.
    /// It is reached through a Scheme-only run because that is the whole of what a run needs
    /// to be: <c>BatchRunner.RunText</c> takes ONE include directory and scopes it to the
    /// run, and a snippet needs several.
    /// </para>
    /// <para>
    /// ⚠ THE EFFECT IS PROCESS-WIDE AND PERMANENT, deliberately. That is what a real
    /// LilyPond process gets from its command line, and the engine here IS the process. It
    /// is also why <see cref="InstalledIncludeDirectories"/> is static: appending the same
    /// directory once per renderer would grow the path every time a manual is rendered.
    /// </para>
    /// </summary>
    private void InstallIncludeDirectories()
    {
        List<string> pending = new List<string>();
        foreach (string directory in _includeDirectories)
        {
            if (!string.IsNullOrEmpty(directory)
                && !InstalledIncludeDirectories.Contains(directory))
            {
                pending.Add(directory);
            }
        }

        if (pending.Count == 0)
        {
            return;
        }

        StringBuilder text = new StringBuilder();
        foreach (string directory in pending)
        {
            text.Append("#(ly:parser-append-to-include-path \"")
                .Append(directory.Replace("\\", "\\\\").Replace("\"", "\\\""))
                .Append("\")\n");
        }

        BatchRunner.RunText(text.ToString(), "lily-docs-include-path", null, _scratchRoot);
        foreach (string directory in pending)
        {
            InstalledIncludeDirectories.Add(directory);
        }
    }

    private LilypondSnippetResult Engrave(LilypondSnippet snippet, ComposedSnippet composed)
    {
        _counter++;
        string name = "snippet-" + _counter.ToString("D5", CultureInfo.InvariantCulture);
        string directory = Path.Combine(_scratchRoot, name);

        lock (EngineGate)
        {
            string previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.CreateDirectory(directory);

                // The engine writes by relative name and reports the change the way
                // `main.cc' does, so each snippet engraves in its own directory exactly as
                // the sweep gives each file one.
                Directory.SetCurrentDirectory(directory);
                BatchRunner.ReportWorkingDirectoryChange(directory);
                InstallIncludeDirectories();

                // A manual's pictures carry no point-and-click anchors: the engine's
                // default is upstream's #t, but an anchor here would point into this
                // renderer's scratch directory — a path no reader has — and the frozen
                // picture inventory must not grow an element for it. Off, deliberately,
                // the way every documentation build disables it.
                BatchRunResult result = BatchRunner.RunText(
                    EngravingTextFor(composed.Source), name, IncludeDirectoryFor(snippet),
                    directory,
                    new BatchRunOptions { PointAndClick = false });

                if (result.ErrorCount > 0)
                {
                    return Fail(snippet, "the engine reported " + result.ErrorCount
                        + " error(s): " + FirstDiagnostic(result), composed);
                }

                if (result.SvgPaths.Count == 0)
                {
                    return Fail(snippet, "the engine engraved nothing: " + FirstDiagnostic(result),
                        composed);
                }

                List<LilypondSnippetImage> images = new List<LilypondSnippetImage>();
                foreach (string page in result.SvgPaths)
                {
                    if (!File.Exists(page))
                    {
                        return Fail(snippet, "the engine reported writing " + page
                            + ", which is not there", composed);
                    }

                    images.Add(LilypondSnippetImage.FromFile(page));
                }

                EngravedCount++;
                PageCount += images.Count;
                return LilypondSnippetResult.FromImages(images);
            }
            catch (Exception error)
            {
                // The coordinator would catch this and turn it into a failure anyway. Doing
                // it here keeps the engine's own message, which is the half worth reading.
                return Fail(snippet, error.GetType().Name + ": " + error.Message, composed);
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }
        }
    }

    /// <summary>
    /// Where a snippet's own <c>\include</c>s resolve from: the directory of the file an
    /// <c>@lilypondfile</c> named, or the manual's own base directory for inline music.
    /// </summary>
    private static string IncludeDirectoryFor(LilypondSnippet snippet)
    {
        if (snippet.Kind == LilypondSnippetKind.LilypondFile
            && !string.IsNullOrEmpty(snippet.FilePath))
        {
            return Path.GetDirectoryName(Path.GetFullPath(snippet.FilePath));
        }

        return string.IsNullOrEmpty(snippet.BaseDirectory) ? null : snippet.BaseDirectory;
    }

    private LilypondSnippetResult Fail(LilypondSnippet snippet, string message)
        => Fail(snippet, message, null);

    private LilypondSnippetResult Fail(LilypondSnippet snippet, string message,
        ComposedSnippet composed)
    {
        _failures.Add(new SnippetFailure(Describe(snippet), message,
            composed == null ? string.Empty : composed.Source));
        return LilypondSnippetResult.Failed(message);
    }

    private static string FirstDiagnostic(BatchRunResult result)
    {
        foreach (string diagnostic in result.Diagnostics)
        {
            if (!string.IsNullOrWhiteSpace(diagnostic))
            {
                return diagnostic.Split('\n')[0];
            }
        }

        return "(the engine reported nothing)";
    }

    private static string Describe(LilypondSnippet snippet)
    {
        string file = string.IsNullOrEmpty(snippet.SourceFile) ? "<unknown>" : snippet.SourceFile;
        return file + ":" + snippet.LineNumber.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>One snippet that could not be engraved.</summary>
public sealed class SnippetFailure
{
    internal SnippetFailure(string location, string message, string composedSource)
    {
        Location = location;
        Message = message;
        ComposedSource = composedSource;
    }

    /// <summary>Where the snippet was written, as <c>file:line</c>.</summary>
    public string Location { get; }

    /// <summary>Why it could not be engraved.</summary>
    public string Message { get; }

    /// <summary>
    /// The source that was composed for it, when composition got that far. This is the
    /// half worth reading: a failure is far more often the composition than the engine.
    /// </summary>
    public string ComposedSource { get; }

    /// <summary>Formats the failure for a report.</summary>
    /// <returns>Location and message on one line.</returns>
    public override string ToString() => Location + ": " + Message;
}
