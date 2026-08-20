// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CodeBrix.PdfDocCreate.Html2Pdf;
using CodeBrix.Texinfo2Html;
using Lily.Docs;
using Lily.Docs.Generation;
using Lily.Docs.Manuals;
using Lily.Docs.Rendering;
using Lily.Docs.Snippets;
using SilverAssertions;
using Xunit;

namespace Lily.Docs.Tests;

/// <summary>
/// The wave-LD5 gates: the seven remaining manuals of decision D48's scope render to HTML
/// and PDF from the mirrored corpus, with their music engraved by the port's own engine.
/// <para>
/// Every gate here reads <see cref="CorpusManualFixture"/>'s single set of renders. The
/// per-manual gates are theories over the manual NAMES so that adding a manual is adding a
/// name; the ones that could only ever be about one manual are facts, and each says in its
/// own comment what makes it that manual's question.
/// </para>
/// </summary>
public sealed class CorpusManualRenderTests : IClassFixture<CorpusManualFixture>
{
    /// <summary>The seven manuals, as xunit theory data.</summary>
    public static TheoryData<string> AllManuals()
    {
        TheoryData<string> data = new TheoryData<string>();
        foreach (string name in CorpusManualFixture.ManualNames)
        {
            data.Add(name);
        }

        return data;
    }

    /// <summary>
    /// The six of the seven that carry music. The Contributor's Guide is the exception, and
    /// its own gate proves the exception rather than assuming it.
    /// </summary>
    public static TheoryData<string> EngravingManuals()
    {
        TheoryData<string> data = new TheoryData<string>();
        foreach (string name in CorpusManualFixture.ManualNames)
        {
            if (ManualCatalog.Find(name).EngravesSnippets)
            {
                data.Add(name);
            }
        }

        return data;
    }

    private readonly CorpusManualFixture _fixture;

    /// <summary>Creates the test class over the shared renders.</summary>
    /// <param name="fixture">The one generation-and-seven-renders run.</param>
    public CorpusManualRenderTests(CorpusManualFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Html render warnings match the frozen baseline exactly.</summary>
    /// <param name="name">The manual.</param>
    [Theory]
    [MemberData(nameof(AllManuals))]
    public void html_render_warnings_match_the_frozen_baseline_exactly(string name)
    {
        //Arrange
        CorpusManualRender render = _fixture[name];
        SortedDictionary<string, int> expected = WarningSummary.ReadBaseline(
            Path.Combine(ToolPaths.ExpectedWarningsDirectory, name + ".tsv"));

        //Act
        SortedDictionary<string, int> actual = WarningSummary.Count(render.Html.Warnings);

        //Assert
        // Both directions: a category that SHRANK is as much a change to look at as one that
        // grew. Each baseline was read and reviewed before it was frozen, and its own header
        // comment says what every warning in it is.
        actual.Should().BeEquivalentTo(expected, string.Join("\n", render.Html.Warnings));
    }

    /// <summary>Every internal link in the html points at an id that exists.</summary>
    /// <param name="name">The manual.</param>
    [Theory]
    [MemberData(nameof(AllManuals))]
    public void every_internal_link_in_the_html_points_at_an_id_that_exists(string name)
    {
        //Arrange
        string html = _fixture[name].HtmlText;
        HashSet<string> ids = new HashSet<string>(
            Regex.Matches(html, "id=\"([^\"]+)\"").Select(m => m.Groups[1].Value));

        //Act
        List<string> targets = Regex.Matches(html, "href=\"#([^\"]+)\"")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        List<string> dangling = targets.Where(t => !ids.Contains(t)).ToList();

        //Assert
        // The strongest structural claim available with no fidelity target: not "the render
        // finished" but "every link it wrote goes somewhere".
        dangling.Should().BeEmpty();

        // ⚠ AND A FLOOR, because zero dangling links out of zero links is not a result.
        //
        // ⚠ THE FLOORS ARE MEASURED PER MANUAL, AND THE FIRST VERSION OF THIS GATE WAS NOT.
        // It carried one shared floor of 50 and a comment asserting that "the smallest of
        // these seven — changes — still writes hundreds". That was never measured, and it is
        // wrong by a factor of five: changes is a ten-page release-notes manual with TEN
        // internal links. A constant nobody measured, defended by a comment claiming it was
        // measured, is the exact reading error this suite's trap list is about — so the
        // numbers below come from LinkFloors, where each one says what it is.
        targets.Count.Should().BeGreaterThanOrEqualTo(LinkFloors[name].Targets);
        ids.Count.Should().BeGreaterThanOrEqualTo(LinkFloors[name].Ids);
    }

    /// <summary>
    /// What each manual's rendered HTML actually contains, MEASURED 2026-08-19 at wave LD5.
    /// <para>
    /// A FLOOR rather than an exact expectation, deliberately: this gate's claim is "the
    /// render did not collapse", and the per-manual expected-warnings and PDF baselines
    /// already catch content that moved. Asserting these exactly would add two more numbers
    /// to re-freeze on every corpus re-sync for a signal those files already carry.
    /// </para>
    /// <para>
    /// ⚠ <c>changes</c> IS THE ONE WORTH READING, because it is the one that broke the
    /// guessed floor and because its numbers look wrong until they are checked. Its source
    /// declares ELEVEN <c>@node</c>s and the render produces TEN sections plus <c>Top</c>.
    /// The missing one is <c>Notes for source compilation and packagers</c>, which upstream
    /// has wrapped in <c>@ignore</c> — its own comment reads "See if we need this again...".
    /// So the render is faithful and the manual really is that small; a twelfth node
    /// appearing here would mean <c>@ignore</c> had stopped being honoured.
    /// </para>
    /// <para>
    /// ⚠ <c>music-glossary</c> is the other one that reads oddly: 362 ids against 176 link
    /// targets, where every other manual has ids and targets within one of each other. A
    /// glossary is a flat list of defined terms, most of which nothing links TO — the ids
    /// are the entries and the targets are the cross-references between them.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, (int Ids, int Targets)> LinkFloors =
        new Dictionary<string, (int Ids, int Targets)>(StringComparer.Ordinal)
        {
            { "learning", (882, 881) },
            { "music-glossary", (362, 176) },
            { "contributor", (227, 226) },
            { "usage", (185, 184) },
            { "extending", (155, 154) },
            { "essay", (56, 55) },
            { "changes", (11, 10) },
        };

    /// <summary>The engraver was asked for every snippet and failed none.</summary>
    /// <param name="name">The manual.</param>
    [Theory]
    [MemberData(nameof(EngravingManuals))]
    public void the_engraver_was_asked_for_every_snippet_and_failed_none(string name)
    {
        //Arrange
        CorpusManualRender render = _fixture[name];
        SortedDictionary<string, int> baseline = WarningSummary.ReadBaseline(
            Path.Combine(ToolPaths.ExpectedWarningsDirectory, name + "-snippets.tsv"));

        //Act
        SortedDictionary<string, int> actual = Program.SnippetCountsOf(
            render.Snippets, render.Html.Result.Images.Count);

        //Assert
        // ⚠ ASKED AND FAILED, NEVER COMPLETION. The package catches a renderer that throws
        // and shows the snippet's source instead, so "the manual rendered" is compatible with
        // every engraving having failed. Freezing what was ASKED FOR as well as what came
        // back also catches a render that quietly stopped asking.
        //
        // ⚠ ALL SIX ARE FAILED 0, which is worth saying out loud beside the Notation
        // Reference's twelve: every failure that manual carries is in an appendix built from
        // the port's own generated bytes, and none of it is corpus prose.
        actual.Should().BeEquivalentTo(baseline, ReportFailures(render.Snippets));
    }

    /// <summary>Every engraving reached the document as a picture.</summary>
    /// <param name="name">The manual.</param>
    [Theory]
    [MemberData(nameof(EngravingManuals))]
    public void every_engraving_reached_the_document_as_a_picture(string name)
    {
        //Arrange
        CorpusManualRender render = _fixture[name];
        IReadOnlyList<TexinfoImageReference> images = render.Html.Result.Images;

        //Act
        int engraved = images.Count(image => image.IsGenerated);

        //Assert
        // The engraver's own count of pictures produced has to equal the document's count of
        // pictures placed, or something was engraved and then dropped on the way in.
        //
        // ⚠ AND THIS IS THE FENCE AGAINST ENGRAVING EACH MANUAL TWICE. The fixture produces
        // both formats from ONE Texinfo pass; rendering them separately would engrave every
        // snippet once per format, and the engraving baseline could not see it — re-freezing
        // would simply record the doubled numbers. It shows up here, because the count on the
        // left comes from the DOCUMENT and the count on the right from the ENGRAVER, and only
        // the engraver's doubles.
        engraved.Should().Be(render.Snippets.PageCount);
    }

    /// <summary>Every engraved picture is an svg file that exists.</summary>
    /// <param name="name">The manual.</param>
    [Theory]
    [MemberData(nameof(EngravingManuals))]
    public void every_engraved_picture_is_an_svg_file_that_exists(string name)
    {
        //Arrange
        IReadOnlyList<TexinfoImageReference> images = _fixture[name].Html.Result.Images;

        //Act
        List<string> wrong = images
            .Where(image => image.IsGenerated)
            .Where(image => !File.Exists(image.SourcePath)
                || !string.Equals(Path.GetExtension(image.SourcePath), ".svg",
                    StringComparison.OrdinalIgnoreCase)
                || new FileInfo(image.SourcePath).Length == 0)
            .Select(image => image.SourcePath)
            .ToList();

        //Assert
        // A picture reference with the right name and no bytes behind it places into the
        // document as a broken image and warns about nothing.
        wrong.Should().BeEmpty();
    }

    /// <summary>The engraved music is cropped rather than left on full pages.</summary>
    /// <param name="name">The manual.</param>
    [Theory]
    [MemberData(nameof(EngravingManuals))]
    public void the_engraved_music_is_cropped_rather_than_left_on_full_pages(string name)
    {
        //Arrange
        List<TexinfoImageReference> engraved = _fixture[name].Html.Result.Images
            .Where(image => image.IsGenerated).ToList();

        //Act
        List<string> tall = new List<string>();
        foreach (TexinfoImageReference image in engraved)
        {
            Match height = Regex.Match(ReadHead(image.SourcePath, 512), @"height=""([0-9.]+)mm""");
            if (!height.Success
                || double.Parse(height.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture) > 290)
            {
                tall.Add(image.SourcePath);
            }
        }

        //Assert
        // A4 is 297 mm tall. An engraving left on a whole page places into the manual as a
        // band of whitespace with a line of music at the top, and nothing else in this suite
        // would notice — the picture exists, the file has bytes, the count is right. This is
        // the gate on the ly:one-page-breaking directive wave LD2 added.
        tall.Should().BeEmpty();
        engraved.Count.Should().BeGreaterThan(0);
    }

    /// <summary>Pdf facts match the frozen baseline exactly.</summary>
    /// <param name="name">The manual.</param>
    [Theory]
    [MemberData(nameof(AllManuals))]
    public void pdf_facts_match_the_frozen_baseline_exactly(string name)
    {
        //Arrange
        CorpusManualRender render = _fixture[name];
        SortedDictionary<string, string> expected = WarningSummary.ReadPdfBaselineValues(
            Path.Combine(ToolPaths.ExpectedWarningsDirectory, name + "-pdf.tsv"));

        //Act
        SortedDictionary<string, string> actual = render.Pdf.BaselineValues();

        //Assert
        // Page count, warning counts, the page size actually used, and the two PDF-stage
        // settings — asserted together and in both directions. Freezing the SETTINGS beside
        // the results is what keeps a page count that moved from having two candidate
        // explanations.
        actual.Should().BeEquivalentTo(expected, ReportPdf(render.Pdf));
    }

    /// <summary>Pdf drops match the frozen baseline per code point.</summary>
    /// <param name="name">The manual.</param>
    [Theory]
    [MemberData(nameof(AllManuals))]
    public void pdf_drops_match_the_frozen_baseline_per_code_point(string name)
    {
        //Arrange
        CorpusManualRender render = _fixture[name];
        List<string> expected = WarningSummary.ReadPdfBaselineDrops(
            Path.Combine(ToolPaths.ExpectedWarningsDirectory, name + "-pdf.tsv"));

        //Act
        List<string> actual = render.Pdf.DropRows();

        //Assert
        // ⚠ EVERY ONE OF THESE SEVEN BASELINES IS EMPTY, and that is the wave's font result
        // rather than an absence of one: nothing in any of them drops. It is asserted as rows
        // rather than as a zero so that a character which STARTED dropping arrives named,
        // with its code point and an exact occurrence count, instead of as "1 != 0".
        actual.Should().BeEquivalentTo(expected, ReportPdf(render.Pdf));
    }

    /// <summary>Every page of the pdf is a4.</summary>
    /// <param name="name">The manual.</param>
    [Theory]
    [MemberData(nameof(AllManuals))]
    public void every_page_of_the_pdf_is_a4(string name)
    {
        //Arrange
        CorpusManualRender render = _fixture[name];
        SortedSet<string> sizes = PdfPageBoxes.DistinctPageSizes(render.PdfBytes);

        //Act
        int pagesMeasured = PdfPageBoxes.ReadMediaBoxes(render.PdfBytes).Count;

        //Assert
        // ⚠ ASKED OF THE FILE, NOT OF THE OPTIONS. Each manual declares @afourpaper and its
        // music was engraved to the 160 mm line width that declaration implies, so a US
        // Letter page would carry A4-measure music and look entirely plausible. Reading the
        // boxes back out of the bytes is the only form of this check that could ever fail.
        pagesMeasured.Should().Be(render.Pdf.PageCount);
        sizes.Should().BeEquivalentTo(new SortedSet<string>(StringComparer.Ordinal) { "595x842" });
    }

    /// <summary>Each manual declares the page size its snippets were engraved for.</summary>
    /// <param name="name">The manual.</param>
    [Theory]
    [MemberData(nameof(AllManuals))]
    public void each_manual_declares_the_page_size_its_snippets_were_engraved_for(string name)
    {
        //Arrange
        CorpusManualRender render = _fixture[name];

        //Act
        string lineWidth = render.Geometry.LineWidth;

        //Assert
        // The paired half of the A4 gate above, and the half that is about the MUSIC. The
        // page size is applied by Lily.Docs; this line width is READ off the manual's own
        // @afourpaper declaration by the same code path lilypond-book's
        // get_texinfo_width_indent uses, and it is written verbatim into every composed
        // snippet. The two agreeing is what makes an A4 page carry A4-measure music.
        lineWidth.Should().Be("160\\mm");
    }

    /// <summary>No picture was skipped by the pdf stage.</summary>
    /// <param name="name">The manual.</param>
    [Theory]
    [MemberData(nameof(AllManuals))]
    public void no_picture_was_skipped_by_the_pdf_stage(string name)
    {
        //Arrange
        IReadOnlyList<RenderWarning> items = _fixture[name].Pdf.PdfItems;

        //Act
        List<string> imageWarnings = items
            .Where(item => item.Code != null
                && item.Code.StartsWith("image.", StringComparison.Ordinal))
            .Select(item => item.Code + " x" + item.Occurrences + ": " + item.Message)
            .ToList();

        //Assert
        // ⚠ THIS IS DECISION D51'S RULING AS A GATE. Until Html2Pdf gained SVG placement it
        // answered an SVG with "not in a supported format and was skipped" and carried on,
        // producing a complete music manual containing no music.
        //
        // It covers the bitmap pictures too, which is what makes it worth running on these
        // seven: essay places thirty-one photographic plates and learning five screenshots,
        // and a skipped one leaves a caption with nothing above it.
        imageWarnings.Should().BeEmpty();
    }

    /// <summary>The pdf embeds a raster for every picture the document places.</summary>
    /// <param name="name">The manual.</param>
    [Theory]
    [MemberData(nameof(AllManuals))]
    public void the_pdf_embeds_a_raster_for_every_picture_the_document_places(string name)
    {
        //Arrange
        CorpusManualRender render = _fixture[name];
        string text = Encoding.Latin1.GetString(render.PdfBytes);

        //Act
        int embedded = Regex.Matches(text, @"/Subtype\s*/Image").Count;

        //Assert
        // ⚠ COUNTED IN THE FILE, because "nothing warned" is not evidence that anything
        // arrived. A LOWER BOUND on purpose: how many XObjects one placed picture costs is
        // the writer's business — an engraved SVG also brings a soft mask — but a document
        // placing N pictures cannot be correct with fewer than N rasters in it.
        embedded.Should().BeGreaterThanOrEqualTo(render.Html.Result.Images.Count);
    }

    /// <summary>The manuals that carry a licence notice put it in the rendered html.</summary>
    /// <param name="name">The manual.</param>
    [Theory]
    [MemberData(nameof(AllManuals))]
    public void the_manuals_that_carry_a_licence_notice_put_it_in_the_rendered_html(string name)
    {
        //Arrange
        const string Notice = "GNU Free Documentation License";
        string source = File.ReadAllText(Path.Combine(
            ToolPaths.CorpusDirectory, "en", ManualCatalog.Find(name).FileName));

        //Act
        bool sourceCarriesIt = source.Contains("fdl.itexi", StringComparison.Ordinal);
        bool renderCarriesIt = _fixture[name].HtmlText.Contains(Notice, StringComparison.Ordinal);

        //Assert
        // ⚠ READ OFF THE SOURCE RATHER THAN LISTED HERE, because the interesting case is the
        // one that would be got wrong by a list: changes.tely includes no fdl.itexi and
        // carries no notice, and a gate that simply asserted "every manual shows the notice"
        // would have to be weakened for it, which is how a licence gate stops being one. Six
        // of the seven include it; this asserts the biconditional, so a manual that gained or
        // lost the include announces itself.
        renderCarriesIt.Should().Be(sourceCarriesIt);
    }

    /// <summary>The contributors guide asks the engraver for nothing.</summary>
    [Fact]
    public void the_contributors_guide_asks_the_engraver_for_nothing()
    {
        //Arrange
        EngineSnippetRenderer probe = _fixture.ContributorEngraverProbe;

        //Act
        int asked = probe.InvocationCount;

        //Assert
        // ⚠ THE ONE GATE THAT COULD ONLY BE WRITTEN AS A SECOND RENDER. The catalogue declares
        // this manual engravesSnippets: false, and ManualDefinition warns that a false is a
        // claim rather than a default — a manual rendered with no engraver is exactly what a
        // manual whose every engraving failed looks like. So the fixture renders it AGAIN with
        // an engraver registered and nothing else changed, and the claim becomes "it was there
        // and was never called".
        //
        // ⚠ MEASURED, and it is why the plan's "zero snippets" needed checking rather than
        // quoting: the manual's twenty files contain nineteen occurrences of the letters
        // @lilypond, every one of them an ESCAPED mention in the chapter that documents how to
        // write snippets. A survey that counted those would have called this manual the
        // heaviest of the seven.
        asked.Should().Be(0);
        probe.FailureCount.Should().Be(0);
        probe.DeclineCount.Should().Be(0);

        // And the second render agrees with the first about everything else, which is what
        // makes it a probe of the engraver rather than a different document.
        WarningSummary.Count(_fixture.ContributorProbeHtml.Warnings)
            .Should().BeEquivalentTo(WarningSummary.Count(_fixture["contributor"].Html.Warnings));
    }

    /// <summary>The contributors guide places its one source image.</summary>
    [Fact]
    public void the_contributors_guide_places_its_one_source_image()
    {
        //Arrange
        IReadOnlyList<TexinfoImageReference> images = _fixture["contributor"].Html.Result.Images;

        //Act
        List<string> placed = images.Select(image => Path.GetFileName(image.SourcePath))
            .OrderBy(fileName => fileName, StringComparer.Ordinal).ToList();

        //Assert
        // ⚠ THE PHASE PLAN RECORDED THIS MANUAL AS HAVING "ZERO real @image uses", AND IT HAS
        // ONE. programming-work.itexi:52 is @sourceimage{architecture-diagram,,}, and
        // @sourceimage EXPANDS to @image{pictures/...} — one level below where a survey of
        // literal @image commands stops. That is the same reading error that hid the Notation
        // Reference's two pictures at wave LD3, in its fifth firing; the picture is in the
        // mirror because this wave measured the expansion rather than the command.
        placed.Should().BeEquivalentTo(new[] { "architecture-diagram.png" });
    }

    /// <summary>The contributors guide resolves every include including the staged pair.</summary>
    [Fact]
    public void the_contributors_guide_resolves_every_include_including_the_staged_pair()
    {
        //Arrange
        CorpusManualRender render = _fixture["contributor"];
        string html = render.HtmlText;

        //Act
        List<string> includeWarnings = render.Html.Warnings
            .Where(warning => WarningSummary.CategoryOf(warning) == "Include").ToList();

        //Assert
        // ⚠ ZERO, AND IT IS THE ONLY MANUAL OF THE NINE THAT REACHES ZERO WITH NOTHING
        // EXPLAINED AWAY. Decision D57, ruled 2026-08-19: the two @verbatiminclude targets
        // are vendored in assets/staged/. Wave LD5 first rendered this manual with both
        // missing, and neither was found by the closure measurement — that survey followed
        // @include and not @verbatiminclude, so the render's own warnings are what named
        // them. Trap (n)'s sixth firing.
        //
        // ⚠ THIS ZERO IS ONLY MEANINGFUL BESIDE ITS CONTROL. IncludeWarningControlTests
        // renders snippets.tely in the same suite and asserts exactly thirty-nine Include
        // warnings, which is what separates "everything resolved" from "the warning channel
        // stopped reporting".
        includeWarnings.Should().BeEmpty();

        // And the CONTENT, because a resolved include proves nothing about what was in the
        // file. ⚠ Both of these are printed VERBATIM inside @smallformat, so they are the
        // one place in the manual where upstream's own bytes appear unreformatted: the first
        // line of the source-tree tour, and the first line of the reviewer's checklist.
        html.Should().Contain("Toplevel READMEs");
        html.Should().Contain("clean-up, fixing, and enhancements are in separate commits");
    }

    /// <summary>The music glossarys prose music symbols survive into both formats.</summary>
    [Fact]
    public void the_music_glossarys_prose_music_symbols_survive_into_both_formats()
    {
        //Arrange
        CorpusManualRender render = _fixture["music-glossary"];
        string source = File.ReadAllText(
            Path.Combine(ToolPaths.CorpusDirectory, "en", "music-glossary.tely"));

        //Act
        int flatsInSource = Occurrences(source, '♭');
        int sharpsInSource = Occurrences(source, '♯');
        int flatsInHtml = Occurrences(render.HtmlText, '♭');
        int sharpsInHtml = Occurrences(render.HtmlText, '♯');

        //Assert
        // ⚠ THIS IS DECISION D50'S WHOLE POINT, AND THE ONLY PLACE IN NINE MANUALS IT CAN BE
        // ASKED. Every other music symbol in scope is LilyPond markup inside a @lilypond
        // snippet — drawn by the engine into an SVG and never handed to a text renderer at
        // all. These five are PROSE, inline in a chord-name @multitable that the stylesheet
        // sets in Merriweather, which carries neither character.
        //
        // They survive because CodeBrix.Platform.Fonts.NotoMusic is on Html2Pdf's fallback
        // chain and coverage is decided per glyph against the resolved face's own cmap: the
        // flat splits into a one-character NotoMusic run without the prose changing font.
        // Before that shipped, the PDF removed them with a warning while the HTML kept them.
        flatsInSource.Should().Be(4);
        sharpsInSource.Should().Be(1);
        flatsInHtml.Should().Be(flatsInSource);
        sharpsInHtml.Should().Be(sharpsInSource);

        // ⚠ AND THE PDF HALF, WHICH IS THE HALF THAT USED TO FAIL. An empty drop list here is
        // the measurement decision D56 was ruled on: music-glossary was the manual the tofu
        // switch was supposed to decide something in, and it drops nothing at all.
        render.Pdf.DropRows().Should().BeEmpty();
    }

    /// <summary>Learnings only unresolved picture is the one upstream generates.</summary>
    [Fact]
    public void learnings_only_unresolved_picture_is_the_one_upstream_generates()
    {
        //Arrange
        IReadOnlyList<string> warnings = _fixture["learning"].Html.Warnings;

        //Act
        List<string> includeWarnings = warnings
            .Where(warning => WarningSummary.CategoryOf(warning) == "Include").ToList();

        //Assert
        // ⚠ NOT ZERO, AND THE ONE IS EXPLAINED RATHER THAN TOLERATED. This manual names six
        // pictures through @sourceimage; five are the Frescobaldi screenshots and are in the
        // mirror. The sixth, pictures/context-example, exists upstream ONLY as an .eps — the
        // .png is a build product there, made by the doc build. So it cannot resolve from
        // source bytes, and it is baselined for the same reason essay's bibliography files
        // are.
        //
        // ⚠ The same .eps DOES resolve for the Notation Reference, which is worth knowing
        // before someone "fixes" this: there it is reached by \epsfile from inside a music
        // snippet, so the ENGINE reads it rather than the image resolver, and the engine reads
        // EPS perfectly well.
        includeWarnings.Should().HaveCount(1);
        includeWarnings[0].Should().Contain("pictures/context-example");
    }

    /// <summary>Essays only unresolved includes are the nine tex branch pictures.</summary>
    [Fact]
    public void essays_only_unresolved_includes_are_the_nine_tex_branch_pictures()
    {
        //Arrange
        IReadOnlyList<string> warnings = _fixture["essay"].Html.Warnings;

        //Act
        List<string> includeWarnings = warnings
            .Where(warning => WarningSummary.CategoryOf(warning) == "Include").ToList();
        List<string> texBranchPictures = includeWarnings
            .Where(warning => warning.Contains("pictures/pdf/", StringComparison.Ordinal))
            .ToList();

        //Assert
        // ⚠ NINE, NOT TWELVE — decision D57, ruled 2026-08-19. The three that left were the
        // bibliography: colorado.itexi, computer-notation.itexi and engravingbib.itexi are
        // generated upstream from Documentation/bib/*.bib, and they are now VENDORED in
        // assets/bib/, translated once by the BibTeX oracle. See
        // the_essays_bibliographies_reached_the_rendered_document below, which is the gate
        // that says the content arrived rather than merely that a warning left.
        //
        // WHAT REMAINS IS NINE pictures/pdf/NAME — the @iftex twin of a plate the @ifnottex
        // branch also names. The renderer's Print conditional profile deliberately reads
        // BOTH branches, and the package's @image extension probe deliberately EXCLUDES .pdf
        // ("a manual that keeps pdf/NAME variants for its TeX branch would then hand
        // Html2Pdf a file it cannot decode"). ⚠ EACH OF THE NINE PLATES IS IN THE DOCUMENT,
        // once, from the other branch — essay_places_every_picture_the_branch_it_renders_
        // names is what asserts that — so copying the ten pdf/*.pdf files into the mirror
        // would change nothing at all.
        texBranchPictures.Should().HaveCount(9);
        includeWarnings.Should().HaveCount(9);
    }

    /// <summary>The essays bibliographies reached the rendered document.</summary>
    [Fact]
    public void the_essays_bibliographies_reached_the_rendered_document()
    {
        //Arrange
        // One marker per vendored bibliography: a title that appears in THAT file and in
        // neither of the other two, so finding it says which bibliography arrived rather
        // than only that something did.
        //
        // ⚠ MEASURED OUT OF THE FILES, NOT REMEMBERED — and the first draft of this gate
        // proves why. Two of its three markers were plausible titles for a music
        // bibliography and neither existed in any of the three files; the gate would have
        // gone red on its first run and pointed at the render. Uniqueness is recomputed
        // below every run for the same reason.
        Dictionary<string, string> markers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "engravingbib.itexi", "Akkord-Lexikon" },
            { "colorado.itexi", "The notation of polyphonic music, 900-1600" },
            { "computer-notation.itexi", "A Music Workstation Based on Multiple Hierarchical Views of Music" },
        };
        string html = _fixture["essay"].HtmlText;

        //Act
        List<string> missing = new List<string>();
        List<string> ambiguous = new List<string>();
        foreach (KeyValuePair<string, string> marker in markers)
        {
            if (!html.Contains(marker.Value, StringComparison.Ordinal))
            {
                missing.Add(marker.Key);
            }

            List<string> holders = markers.Keys
                .Where(name => File.ReadAllText(Path.Combine(
                    ToolPaths.BibliographyAssetsDirectory, name))
                    .Contains(marker.Value, StringComparison.Ordinal))
                .ToList();
            if (holders.Count != 1 || holders[0] != marker.Key)
            {
                ambiguous.Add(marker.Value + " -> " + string.Join(", ", holders));
            }
        }

        //Assert
        // ⚠ THE HALF THAT MATTERS. A warning going away proves an include RESOLVED; it does
        // not prove the file had anything in it, and a zero-byte colorado.itexi would resolve
        // just as quietly. These three strings are entries a reader can find in the printed
        // manual — the first book of the engraving bibliography, the last of the Colorado
        // list, and an article from the computer-notation list.
        ambiguous.Should().BeEmpty();
        missing.Should().BeEmpty();

        // ⚠ AND THE MARKER THAT MUST *NOT* BE THERE. lily-bib.bst wraps its output in
        // `@c bib -> itexi intro' and `@c bib -> itexi end', which are Texinfo COMMENTS.
        // Finding either in the rendered HTML would mean the renderer had stopped treating
        // @c as a comment and started printing it — a failure that would otherwise look like
        // success, because the bibliography would still be there.
        html.Should().NotContain("bib -> itexi");
    }

    /// <summary>Essay places every picture the branch it renders names.</summary>
    [Fact]
    public void essay_places_every_picture_the_branch_it_renders_names()
    {
        //Arrange
        string[] infoOnly = { "baer-flat-bw.png", "henle-flat-bw.png", "lily-flat-bw.png" };
        IReadOnlyList<TexinfoImageReference> images = _fixture["essay"].Html.Result.Images;

        //Act
        List<string> named = images.Where(image => !image.IsGenerated)
            .Select(image => Path.GetFileName(image.SourcePath))
            .OrderBy(fileName => fileName, StringComparer.Ordinal)
            .ToList();

        //Assert
        // Thirty-one plates placed, and the count frozen so a picture that stopped resolving
        // shows up here rather than only as one more warning among twelve.
        named.Should().HaveCount(31);

        // ⚠ AND THE THREE THAT ARE MIRRORED BUT DELIBERATELY ABSENT, which is the point of
        // this gate. engraving.itely names henle-flat-bw, baer-flat-bw and lily-flat-bw inside
        // an @ifinfo branch — Info output, which decision D48's scope excludes and the
        // renderer's Print conditional profile turns off. They are in the mirror because the
        // mirror is the manual's SOURCE closure; their absence from the document is what says
        // the conditional profile is the one this phase renders. If Info were ever switched
        // on, or @ifinfo started being read, three pictures would appear here and this gate
        // would say so.
        named.Should().NotContain(infoOnly);
    }

    /// <summary>The seven manuals consume none of the ports generated files.</summary>
    [Fact]
    public void the_seven_manuals_consume_none_of_the_ports_generated_files()
    {
        //Arrange
        List<string> sources = new List<string>();
        foreach (string name in CorpusManualFixture.ManualNames)
        {
            sources.Add(Path.Combine(ToolPaths.CorpusDirectory, "en",
                ManualCatalog.Find(name).FileName));
        }

        //Act
        List<string> including = new List<string>();
        foreach (string closureFile in ClosureOf(sources))
        {
            string text = File.ReadAllText(closureFile);
            foreach (string generated in DocumentationGenerator.ExpectedOutputs)
            {
                if (text.Contains("@include " + RenderPaths.GeneratedDirectoryName + "/" + generated,
                    StringComparison.Ordinal))
                {
                    including.Add(Path.GetFileName(closureFile) + " -> " + generated);
                }
            }
        }

        //Assert
        // ⚠ STATED AS A GATE BECAUSE IT IS THE ONE THING THAT SEPARATES THESE SEVEN FROM THE
        // OTHER TWO, and because it would otherwise be a sentence in a document. The Internals
        // Reference IS one of the port's nineteen outputs and the Notation Reference includes
        // the other eighteen; these seven include none of them and are pure corpus prose.
        //
        // That is a fact about the mission's ORIGIN — Phase 5 began as "render what the port
        // generates" — and NOT a reason to treat them as lesser: decision D48 ruled all nine
        // owed in both formats, on the measured ground that consuming none of the nineteen
        // says nothing about a manual's cost or feasibility.
        including.Should().BeEmpty();
    }

    /// <summary>Every manual in scope is in the catalogue.</summary>
    [Fact]
    public void every_manual_in_scope_is_in_the_catalogue()
    {
        //Arrange
        string[] ruled =
        {
            "internals", "notation", "learning", "usage", "extending", "essay", "changes",
            "music-glossary", "contributor",
        };

        //Act
        List<string> catalogued = ManualCatalog.All.Select(manual => manual.Name).ToList();

        //Assert
        // Decision D48's scope, written out and checked rather than described. ⚠ snippets.tely
        // is deliberately NOT here: it is the include-warning CONTROL, not a deliverable, and
        // ManualCatalog.Find returning null for it is what stops it being rendered by accident.
        catalogued.Should().BeEquivalentTo(ruled);
        ManualCatalog.Find("snippets").Should().BeNull();
        ManualCatalog.Find("web").Should().BeNull();
    }

    private static IEnumerable<string> ClosureOf(IEnumerable<string> roots)
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        Queue<string> queue = new Queue<string>(roots);
        string corpusEn = Path.Combine(ToolPaths.CorpusDirectory, "en");
        while (queue.Count > 0)
        {
            string path = queue.Dequeue();
            if (!seen.Add(path) || !File.Exists(path))
            {
                continue;
            }

            yield return path;
            foreach (Match include in Regex.Matches(File.ReadAllText(path), @"^@include\s+(\S+)\s*$",
                RegexOptions.Multiline))
            {
                string name = include.Groups[1].Value;
                queue.Enqueue(Path.Combine(corpusEn, name));
                queue.Enqueue(Path.Combine(ToolPaths.CorpusDirectory, name));
            }
        }
    }

    private static int Occurrences(string text, char character)
    {
        int count = 0;
        foreach (char candidate in text)
        {
            if (candidate == character)
            {
                count++;
            }
        }

        return count;
    }

    private static string ReadHead(string path, int count)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] buffer = new byte[count];
        int read = stream.Read(buffer, 0, count);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static string ReportFailures(EngineSnippetRenderer snippets)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append(snippets.FailureCount).Append(" failure(s), ")
            .Append(snippets.DeclineCount).Append(" decline(s):");
        foreach (SnippetFailure failure in snippets.Failures.Take(20))
        {
            builder.Append('\n').Append(failure);
        }

        foreach (string decline in snippets.Declines.Take(20))
        {
            builder.Append('\n').Append(decline);
        }

        return builder.ToString();
    }

    private static string ReportPdf(ManualPdfRender pdf)
    {
        StringBuilder report = new StringBuilder();
        report.Append("pdf: ").Append(pdf.PageCount).Append(" pages, ")
            .Append(pdf.PdfWarnings.Count).Append(" warnings, ")
            .Append(pdf.PdfItems.Count).Append(" structured items");
        foreach (string row in pdf.DropRows())
        {
            report.Append('\n').Append("  ").Append(row.Replace('\t', ' '));
        }

        return report.ToString();
    }
}
