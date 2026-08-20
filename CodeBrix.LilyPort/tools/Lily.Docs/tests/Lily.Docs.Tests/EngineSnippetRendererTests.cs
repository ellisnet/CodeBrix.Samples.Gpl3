// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CodeBrix.LilyPort;
using CodeBrix.Texinfo2Html;
using Lily.Docs;
using Lily.Docs.Snippets;
using SilverAssertions;
using Xunit;

namespace Lily.Docs.Tests;

/// <summary>
/// The engraving seam, end to end: the probe document's snippets go through the Texinfo
/// package's coordinator, into the port's own engine, and come back as pictures the
/// document places.
/// <para>
/// ⚠ EVERY GATE HERE COUNTS INVOCATIONS AND FAILURES, NEVER COMPLETION. The coordinator
/// CATCHES a renderer that throws and falls back to showing the snippet's source with a
/// warning — so a render that "succeeded" is compatible with every single snippet having
/// failed to engrave. A completion count cannot tell the two apart; an invocation count
/// and a failure count can.
/// </para>
/// </summary>
public sealed class EngineSnippetRendererTests
{
    private static string ProbePath =>
        Path.Combine(ToolPaths.ComposedReferenceDirectory, "probe.itely");

    /// <summary>
    /// The probe document engraves through the engine with no failures and no declines.
    /// </summary>
    [Fact]
    public void the_probe_document_engraves_every_snippet_through_the_engine()
    {
        //Arrange
        using EngineSnippetRenderer renderer =
            new EngineSnippetRenderer(TexinfoPageGeometry.AfourPaper, null);
        TexinfoHtmlRenderer html = new TexinfoHtmlRenderer();
        html.Options.SnippetRenderer = renderer;

        //Act
        TexinfoHtmlResult result = html.GenerateFromFile(ProbePath);

        //Assert
        renderer.InvocationCount.Should().BeGreaterThan(0);
        renderer.FailureCount.Should().Be(0, Report(renderer));
        renderer.DeclineCount.Should().Be(0, string.Join("; ", renderer.Declines));
        renderer.EngravedCount.Should().Be(renderer.InvocationCount);
        renderer.PageCount.Should().BeGreaterThanOrEqualTo(renderer.InvocationCount);

        // Every picture reached the document, and every one of them is an engraving
        // rather than a file the manual named.
        result.Images.Count.Should().Be(renderer.PageCount);
        foreach (TexinfoImageReference image in result.Images)
        {
            image.IsGenerated.Should().BeTrue();
            File.Exists(image.SourcePath).Should().BeTrue();
            new FileInfo(image.SourcePath).Length.Should().BeGreaterThan(0);
        }
    }

    /// <summary>
    /// Every engraving is an SVG whose bytes are actually an SVG document.
    /// <para>
    /// The extension alone proves nothing: a zero-length or truncated file has the right
    /// name and places into the document as a broken picture.
    /// </para>
    /// </summary>
    [Fact]
    public void every_engraving_is_a_well_formed_svg()
    {
        //Arrange
        using EngineSnippetRenderer renderer =
            new EngineSnippetRenderer(TexinfoPageGeometry.AfourPaper, null);
        TexinfoHtmlRenderer html = new TexinfoHtmlRenderer();
        html.Options.SnippetRenderer = renderer;

        //Act
        TexinfoHtmlResult result = html.GenerateFromFile(ProbePath);

        //Assert
        List<string> wrong = new List<string>();
        foreach (TexinfoImageReference image in result.Images)
        {
            string extension = Path.GetExtension(image.SourcePath);
            string head = ReadHead(image.SourcePath, 512);
            if (!string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase)
                || !head.Contains("<svg", StringComparison.Ordinal))
            {
                wrong.Add(image.SourcePath + " (" + extension + ")");
            }
        }

        wrong.Should().BeEmpty();
        result.Images.Count.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// THE CONTROL for every gate above. With NO renderer registered the same document
    /// renders just as happily, shows every snippet as source text, and says so in one
    /// warning — which is exactly what a document full of silently failed engravings would
    /// look like. This is the state the gates above have to be able to distinguish.
    /// </summary>
    [Fact]
    public void without_a_renderer_the_same_document_shows_sources_and_places_no_pictures()
    {
        //Arrange
        TexinfoHtmlRenderer html = new TexinfoHtmlRenderer();

        //Act
        TexinfoHtmlResult result = html.GenerateFromFile(ProbePath);

        //Assert
        result.Images.Should().BeEmpty();
        string snippetWarnings = string.Join("\n", result.Warnings.Messages);
        snippetWarnings.Should().Contain("snippet");
    }

    /// <summary>
    /// The package reports a snippet's line number on the SAME BASE lilypond-book does.
    /// <para>
    /// This is what makes the composed source's <c>\sourcefileline</c> agree with the
    /// oracle's, and it is asserted here because nothing else can see it: the parity gate
    /// compares relevant contents, which deliberately drops those lines.
    /// </para>
    /// </summary>
    [Fact]
    public void the_package_reports_snippet_line_numbers_on_the_directive_line()
    {
        //Arrange
        IReadOnlyList<ComposedReferenceCase> cases = ComposedReferenceCase.ReadAll();
        RecordingSnippetRenderer recorder = new RecordingSnippetRenderer();
        TexinfoHtmlRenderer html = new TexinfoHtmlRenderer();
        html.Options.SnippetRenderer = recorder;

        //Act
        html.GenerateFromFile(ProbePath);

        //Assert
        List<int> expected = new List<int>();
        foreach (ComposedReferenceCase reference in cases)
        {
            expected.Add(reference.DirectiveLine);
        }

        recorder.LineNumbers.Should().BeEquivalentTo(expected);
    }

    /// <summary>
    /// A renderer that THROWS is caught by the coordinator and the document still
    /// renders — which is the trap, so this pins the behaviour rather than trusting it.
    /// The failure is visible only in the counts and the warnings.
    /// </summary>
    [Fact]
    public void a_throwing_renderer_is_caught_and_the_document_still_renders()
    {
        //Arrange
        ThrowingSnippetRenderer thrower = new ThrowingSnippetRenderer();
        TexinfoHtmlRenderer html = new TexinfoHtmlRenderer();
        html.Options.SnippetRenderer = thrower;

        //Act
        TexinfoHtmlResult result = html.GenerateFromFile(ProbePath);

        //Assert
        result.Html.Should().NotBeEmpty();
        result.Images.Should().BeEmpty();
        thrower.Invocations.Should().BeGreaterThan(0);
    }

    /// <summary>A MusicXML reference is declined rather than guessed at.</summary>
    [Fact]
    public void a_musicxml_snippet_is_declined()
    {
        //Arrange
        using EngineSnippetRenderer renderer =
            new EngineSnippetRenderer(TexinfoPageGeometry.AfourPaper, null);
        string document = "\\input texinfo\n@settitle x\n@afourpaper\n\n@node Top\n@top x\n\n"
            + "@musicxmlfile{absent.xml}\n\n@bye\n";
        string path = Path.Combine(Path.GetTempPath(),
            "lilydocs-musicxml-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".texi");
        File.WriteAllText(path, document);
        TexinfoHtmlRenderer html = new TexinfoHtmlRenderer();
        html.Options.SnippetRenderer = renderer;

        try
        {
            //Act
            html.GenerateFromFile(path);

            //Assert
            renderer.DeclineCount.Should().Be(1);
            renderer.FailureCount.Should().Be(0);
            renderer.EngravedCount.Should().Be(0);
            string.Join("", renderer.Declines).Should().Contain("MusicXML is out of scope");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The engine configuration wraps the composed source and does not touch it. This is
    /// the claim that keeps the parity gate meaningful: if the directives were folded into
    /// the composed source, that source would stop matching the oracle's and the frozen
    /// reference would have to be re-frozen to hide it.
    /// </summary>
    [Fact]
    public void the_engraving_directives_leave_the_composed_source_verbatim()
    {
        //Arrange
        LilypondSourceComposer composer =
            new LilypondSourceComposer(TexinfoPageGeometry.AfourPaper);
        ComposedSnippet composed = composer.Compose("{ c'4 }", Array.Empty<string>(), 1);

        //Act
        string engraved = EngineSnippetRenderer.EngravingTextFor(composed.Source);

        //Assert
        engraved.Should().Contain(composed.Source);
        engraved.Replace(composed.Source, string.Empty)
            .Should().Be("#(define lily-docs-page-handler default-toplevel-book-handler)\n"
                + "#(define print-book-with-defaults-as-systems lily-docs-page-handler)\n"
                + "#(define print-book-with-defaults lily-docs-page-handler)\n"
                + "\n#(set! default-toplevel-book-handler lily-docs-page-handler)\n"
                + "\\paper { page-breaking = #ly:one-page-breaking }\n");
    }

    /// <summary>
    /// THE CONTROL for the directives: WITHOUT them the same composed source engraves
    /// nothing at all, because lilypond-book's preamble sends the book to the systems
    /// output the port does not implement.
    /// <para>
    /// This is the failure the directives exist to prevent, and it is silent — no error, no
    /// diagnostic, no picture — so it is pinned here rather than described in a comment.
    /// </para>
    /// <para>
    /// ⚠ WAVE LD3 FOUND A SECOND DOOR INTO THE SAME SILENCE, which is why the prologue now
    /// carries three definitions rather than one. Restoring the handler in the EPILOGUE is
    /// in time for the implicit toplevel book and far too late for an EXPLICIT
    /// <c>\book { … }</c>, which is handed over the moment its block closes — thirty-five
    /// of the notation manual's snippets, all lost the same way. The prologue therefore
    /// also re-points the two functions the preamble's handlers CALL, so that whatever the
    /// preamble installs afterwards still reaches the collector.
    /// </para>
    /// </summary>
    [Fact]
    public void without_the_engraving_directives_the_same_source_writes_no_picture()
    {
        //Arrange
        LilypondSourceComposer composer =
            new LilypondSourceComposer(TexinfoPageGeometry.AfourPaper);
        ComposedSnippet composed = composer.Compose("{ c'4 d'4 }", Array.Empty<string>(), 1);
        string directory = Path.Combine(Path.GetTempPath(),
            "lilydocs-control-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(directory);
        string previous = Directory.GetCurrentDirectory();

        try
        {
            //Act
            Directory.SetCurrentDirectory(directory);
            BatchRunResult bare = BatchRunner.RunText(
                composed.Source, "bare", null, directory);
            BatchRunResult directed = BatchRunner.RunText(
                EngineSnippetRenderer.EngravingTextFor(composed.Source), "directed", null,
                directory);
            Directory.SetCurrentDirectory(previous);

            //Assert
            bare.SvgPaths.Should().BeEmpty();
            bare.ErrorCount.Should().Be(0);
            directed.SvgPaths.Should().ContainSingle();
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// The engraving is cropped to the music rather than left on a full page — the whole
    /// point of asking for one-page breaking.
    /// </summary>
    [Fact]
    public void an_engraving_is_cropped_to_the_music()
    {
        //Arrange
        using EngineSnippetRenderer renderer =
            new EngineSnippetRenderer(TexinfoPageGeometry.AfourPaper, null);
        LilypondSourceComposer composer =
            new LilypondSourceComposer(TexinfoPageGeometry.AfourPaper);
        ComposedSnippet composed = composer.Compose("{ c'4 d'4 }", Array.Empty<string>(), 1);
        string directory = Path.Combine(renderer.ScratchRoot, "crop");
        Directory.CreateDirectory(directory);
        string previous = Directory.GetCurrentDirectory();

        try
        {
            //Act
            Directory.SetCurrentDirectory(directory);
            BatchRunResult result = BatchRunner.RunText(
                EngineSnippetRenderer.EngravingTextFor(composed.Source), "crop", null,
                directory);
            Directory.SetCurrentDirectory(previous);

            //Assert
            result.SvgPaths.Should().ContainSingle();
            string svg = ReadHead(result.SvgPaths[0], 512);

            // A4 is 297mm tall; a two-note snippet must be a small fraction of that. The
            // bound is deliberately loose — this asserts "cropped", not a pixel count.
            Match height = Regex.Match(svg, @"height=""([0-9.]+)mm""");
            height.Success.Should().BeTrue();
            double millimetres = double.Parse(height.Groups[1].Value,
                CultureInfo.InvariantCulture);
            millimetres.Should().BeLessThan(60);
            millimetres.Should().BeGreaterThan(0);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    private static string Report(EngineSnippetRenderer renderer)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append(renderer.FailureCount).Append(" snippet(s) failed to engrave:");
        foreach (SnippetFailure failure in renderer.Failures)
        {
            builder.Append('\n').Append(failure);
            if (!string.IsNullOrEmpty(failure.ComposedSource))
            {
                builder.Append("\n--- composed source ---\n").Append(failure.ComposedSource);
            }
        }

        return builder.ToString();
    }

    private static string ReadHead(string path, int count)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] buffer = new byte[count];
        int read = stream.Read(buffer, 0, count);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    /// <summary>Records what the coordinator hands a renderer, engraving nothing.</summary>
    private sealed class RecordingSnippetRenderer : ILilypondSnippetRenderer
    {
        private readonly List<int> _lineNumbers = new List<int>();

        public IReadOnlyList<int> LineNumbers => _lineNumbers;

        public LilypondSnippetResult Render(LilypondSnippet snippet)
        {
            _lineNumbers.Add(snippet.LineNumber);
            return LilypondSnippetResult.NotRendered;
        }
    }

    /// <summary>Throws on every snippet, to prove the coordinator swallows it.</summary>
    private sealed class ThrowingSnippetRenderer : ILilypondSnippetRenderer
    {
        public int Invocations { get; private set; }

        public LilypondSnippetResult Render(LilypondSnippet snippet)
        {
            Invocations++;
            throw new InvalidOperationException("deliberate");
        }
    }
}
