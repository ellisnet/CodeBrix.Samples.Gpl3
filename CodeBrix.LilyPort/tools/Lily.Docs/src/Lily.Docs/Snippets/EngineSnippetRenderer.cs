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
    /// </summary>
    private const string EngravingPrologue =
        "#(define lily-docs-page-handler default-toplevel-book-handler)\n";

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

    private readonly LilypondSourceComposer _composer;
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
    public EngineSnippetRenderer(TexinfoPageGeometry geometry, string scratchRoot)
    {
        _composer = new LilypondSourceComposer(geometry);
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

                BatchRunResult result = BatchRunner.RunText(
                    EngravingTextFor(composed.Source), name, IncludeDirectoryFor(snippet),
                    directory);

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
