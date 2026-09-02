// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.PdfDocuments.Pdf;
using CodeBrix.PdfDocuments.Pdf.IO;
using Fresco.Brix.Documentation;
using Fresco.Brix.MusicView;
using Fresco.Brix.Services;
using SilverAssertions;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The bundled manuals: that they are all there, that they are what the
/// catalogue says they are, and that the repo tool that made them agrees.
/// </summary>
public class ManualCatalogTests
{
    /// <summary>The order tools/manuals renders and installs them in.</summary>
    /// <remarks>Written out here rather than read from the tool, because two
    /// lists that agree is the whole point: the tool's order is what the
    /// panel's chooser shows, and a silent divergence would put the Notation
    /// Reference in the wrong place in the list.</remarks>
    private static readonly string[] ToolOrder =
    {
        "learning", "notation", "usage", "extending", "internals",
        "essay", "music-glossary", "changes", "contributor",
    };

    [Fact]
    public void the_catalogue_holds_decision_d48s_nine_manuals()
    {
        //Act
        var names = ManualCatalog.All.Select(m => m.Name).ToArray();

        //Assert
        names.Should().Equal(ToolOrder);
        ManualCatalog.TotalPageCount.Should().Be(3368);
    }

    [Fact]
    public void every_manual_is_installed_beside_the_application()
    {
        //Arrange
        ManualLibrary library = new ManualLibrary();

        //Act
        var missing = ManualCatalog.All.Where(m => !library.IsInstalled(m)).ToList();

        //Assert
        missing.Should().BeEmpty();
        library.Any.Should().BeTrue();
        library.Installed.Should().HaveCount(9);
    }

    [Fact]
    public void the_licence_travels_with_the_documents()
    {
        //Arrange — the GNU FDL requires a copy of itself to accompany the work,
        //and tools/manuals puts it here. A folder of manuals without it is not
        //a conveyance we may make.
        string directory = ManualLibrary.DefaultDirectory();

        //Assert
        File.Exists(Path.Combine(directory, "COPYING.FDL")).Should().BeTrue();
        File.Exists(Path.Combine(directory, "README.txt")).Should().BeTrue();
        File.Exists(Path.Combine(directory, "MANIFEST.txt")).Should().BeTrue();
    }

    [Fact]
    public void the_manifest_agrees_with_the_catalogue_about_every_page_count()
    {
        //Arrange — the manifest is what tools/manuals measured when it
        //installed the files; the catalogue is what the application believes.
        //A regeneration that moved a page count must move both.
        string manifest = File.ReadAllText(
            Path.Combine(ManualLibrary.DefaultDirectory(), "MANIFEST.txt"));

        foreach (ManualDefinition manual in ManualCatalog.All)
        {
            //Act
            string row = manifest
                .Split('\n')
                .FirstOrDefault(l => l.StartsWith(manual.Name + " ", StringComparison.Ordinal)
                    || l.StartsWith(manual.Name + "\t", StringComparison.Ordinal));

            //Assert
            row.Should().NotBeNull();
            string[] fields = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int.Parse(fields[1]).Should().Be(manual.PageCount);
            long.Parse(fields[2]).Should().Be(
                new FileInfo(Path.Combine(
                    ManualLibrary.DefaultDirectory(), manual.FileName)).Length);
        }
    }

    [Fact]
    public void every_shipped_pdf_has_the_pages_the_catalogue_declares()
    {
        //Arrange
        ManualLibrary library = new ManualLibrary();

        foreach (ManualDefinition manual in ManualCatalog.All)
        {
            //Act
            ManualStructure structure = ManualOutline.ReadStructure(library.PathOf(manual));

            //Assert — a truncated or stale asset is a failure here rather than
            //a manual that quietly stops early.
            structure.Should().NotBeNull();
            structure.PageCount.Should().Be(manual.PageCount);
        }
    }

    [Fact]
    public void every_manual_is_one_page_shape_from_end_to_end()
    {
        //Arrange — PdfManual reads the FIRST page's size and uses it for all of
        //them, because the Texinfo renderer lays out one page shape per
        //document. This is the measurement that entitles it to.
        foreach (ManualDefinition manual in ManualCatalog.All)
        {
            string path = Path.Combine(ManualLibrary.DefaultDirectory(), manual.FileName);
            PdfDocument document = PdfReader.Open(path, PdfDocumentOpenMode.InformationOnly);

            //Act
            var sizes = new[] { 0, document.PageCount / 2, document.PageCount - 1 }
                .Select(i => (document.Pages[i].Width.Point, document.Pages[i].Height.Point))
                .Distinct()
                .ToList();

            //Assert
            sizes.Should().HaveCount(1);
            sizes[0].Should().Be((595.0, 842.0));
        }
    }

    [Fact]
    public void a_folder_with_no_manuals_in_it_reports_none()
    {
        //Arrange — the folder is deliberately removable (assets/docs/README),
        //so the empty state has to be a real answer rather than a crash.
        ManualLibrary library = new ManualLibrary(
            Path.Combine(Path.GetTempPath(), "fresco-brix-no-manuals-" + Guid.NewGuid()));

        //Assert
        library.Any.Should().BeFalse();
        library.Installed.Should().BeEmpty();
        library.OutlineOf(ManualCatalog.All[0]).Should().BeEmpty();
    }
}

/// <summary>The table of contents each manual carries in its own bookmarks.</summary>
public class ManualOutlineTests
{
    [Fact]
    public void the_internals_reference_carries_one_bookmark_per_node()
    {
        //Arrange — the Internals Reference is generated from the engine's own
        //object model: 810 nodes, and the renderer writes every one of them
        //into the PDF outline with a destination.
        ManualLibrary library = new ManualLibrary();

        //Act
        var outline = library.OutlineOf(ManualCatalog.Find("internals"));

        //Assert
        outline.Should().HaveCount(810);
        outline.Where(e => e.Page < 1).Should().BeEmpty();
    }

    [Fact]
    public void the_notation_reference_carries_its_own()
    {
        //Arrange
        ManualLibrary library = new ManualLibrary();

        //Act
        var outline = library.OutlineOf(ManualCatalog.Find("notation"));

        //Assert
        outline.Should().HaveCount(591);
    }

    [Fact]
    public void a_headings_section_number_is_kept_in_the_title_and_dropped_from_the_name()
    {
        //Arrange
        ManualLibrary library = new ManualLibrary();

        //Act
        var entry = library.OutlineOf(ManualCatalog.Find("internals"))
            .First(e => e.Heading == "NoteHead");

        //Assert — the number is what a reader sees; the name is what a search
        //has to match.
        entry.Title.Should().Be("3.1.98 NoteHead");
        entry.Heading.Should().Be("NoteHead");
        entry.Page.Should().Be(878);
    }

    [Fact]
    public void an_appendix_letter_is_a_section_number_too()
    {
        //Arrange
        ManualOutlineEntry entry = OutlineEntry("A.1 Chord name chart", 1024, 2);

        //Assert
        entry.Heading.Should().Be("Chord name chart");
    }

    [Fact]
    public void a_heading_that_is_not_numbered_keeps_all_of_itself()
    {
        //Arrange — the manuals' own title pages are level 0 and unnumbered.
        ManualOutlineEntry entry = OutlineEntry("LilyPond – Internals Reference", 2, 0);

        //Assert
        entry.Heading.Should().Be("LilyPond – Internals Reference");
    }

    /// <summary>Builds an entry, which is internal to Core.</summary>
    private static ManualOutlineEntry OutlineEntry(string title, int page, int level)
        => (ManualOutlineEntry)Activator.CreateInstance(
            typeof(ManualOutlineEntry),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null,
            new object[] { title, page, level },
            null);
}

/// <summary>Contextual help: the word at the caret, and where it is documented.</summary>
public class ContextHelpTests
{
    [Fact]
    public void a_command_loses_its_backslash()
    {
        //Assert
        ContextHelp.TermOf("\\tuplet").Should().Be("tuplet");
        ContextHelp.TermOf("#'note-head").Should().Be("note-head");
        ContextHelp.TermOf("  \\clef  ").Should().Be("clef");
    }

    [Fact]
    public void a_property_path_asks_about_its_last_part()
    {
        //Assert — a grob-property override is the commonest thing a reader
        //stops on, and what they mean is the property.
        ContextHelp.TermOf("Staff.NoteHead.color").Should().Be("color");
        ContextHelp.TermOf("NoteHead").Should().Be("NoteHead");
    }

    [Fact]
    public void whitespace_and_punctuation_are_not_words()
    {
        //Assert
        ContextHelp.TermOf(null).Should().BeNull();
        ContextHelp.TermOf("   ").Should().BeNull();
        ContextHelp.TermOf("{").Should().BeNull();
    }

    [Fact]
    public void a_grob_name_is_looked_up_in_the_internals_reference_first()
    {
        //Act — the decision comes from the port's own regenerated data, not
        //from the shape of the word.
        var order = ContextHelp.SearchOrder("NoteHead");

        //Assert
        order[0].Should().Be("internals");
        order.Should().HaveCount(9);
    }

    [Fact]
    public void a_scheme_word_is_looked_up_in_extending_first()
    {
        //Act
        var order = ContextHelp.SearchOrder("define-markup-command");

        //Assert
        order[0].Should().Be("extending");
    }

    [Fact]
    public void anything_else_starts_at_the_notation_reference()
    {
        //Act
        var order = ContextHelp.SearchOrder("tuplet");

        //Assert
        order[0].Should().Be("notation");
        order.Distinct().Should().HaveCount(9);
    }

    [Fact]
    public void a_grob_resolves_to_its_own_page_of_the_internals_reference()
    {
        //Arrange
        ContextHelp help = new ContextHelp(new ManualLibrary());

        //Act
        ContextHelpTarget target = help.Resolve("NoteHead");

        //Assert
        target.IsExact.Should().BeTrue();
        target.Manual.Name.Should().Be("internals");
        target.Entry.Title.Should().Be("3.1.98 NoteHead");
        target.Page.Should().Be(878);
    }

    [Fact]
    public void a_command_resolves_through_the_plural_the_manuals_head_with()
    {
        //Arrange — the manuals head their sections with the noun, usually
        //plural: \tuplet is documented under "Tuplets".
        ContextHelp help = new ContextHelp(new ManualLibrary());

        //Act
        ContextHelpTarget target = help.Resolve("\\tuplet");

        //Assert
        target.IsExact.Should().BeTrue();
        target.Manual.Name.Should().Be("notation");
        target.Entry.Heading.Should().Be("Tuplets");
        target.Page.Should().BeGreaterThan(1);
    }

    [Fact]
    public void a_word_no_heading_names_still_says_where_to_start_looking()
    {
        //Arrange
        ContextHelp help = new ContextHelp(new ManualLibrary());

        //Act
        ContextHelpTarget target = help.Resolve("qqqzzznotaword");

        //Assert — upstream's own action does nothing at all; this at least
        //opens a manual (ruling FR14, and the divergence is documented at the
        //ContextHelp class).
        target.Should().NotBeNull();
        target.IsExact.Should().BeFalse();
        target.Page.Should().Be(1);
    }

    [Fact]
    public void with_no_word_at_the_caret_the_first_manual_is_offered()
    {
        //Arrange
        ContextHelp help = new ContextHelp(new ManualLibrary());

        //Act
        ContextHelpTarget target = help.Resolve("   ");

        //Assert
        target.Term.Should().BeNull();
        target.Manual.Should().NotBeNull();
        target.Page.Should().Be(1);
    }

    [Fact]
    public void with_no_manuals_installed_nothing_is_offered()
    {
        //Arrange
        ManualLibrary empty = new ManualLibrary(
            Path.Combine(Path.GetTempPath(), "fresco-brix-no-manuals-" + Guid.NewGuid()));

        //Act
        ContextHelpTarget target = new ContextHelp(empty).Resolve("NoteHead");

        //Assert
        target.Should().BeNull();
    }
}

/// <summary>Turning a manual's pages into pictures the paged view can draw.</summary>
public class PdfManualTests
{
    [Fact]
    public async Task an_open_manual_offers_one_page_source_per_page()
    {
        //Arrange
        ManualLibrary library = new ManualLibrary();
        ManualDefinition definition = ManualCatalog.Find("changes");

        //Act
        using PdfManual manual = await PdfManual.OpenAsync(
            definition, library.PathOf(definition));

        //Assert
        manual.Should().NotBeNull();
        manual.PageCount.Should().Be(10);
        manual.Pages.Should().HaveCount(10);
        manual.PageWidthPoints.Should().Be(595.0);
        manual.PageHeightPoints.Should().Be(842.0);
    }

    [Fact]
    public async Task a_missing_file_opens_as_nothing_rather_than_throwing()
    {
        //Act
        PdfManual manual = await PdfManual.OpenAsync(
            ManualCatalog.Find("changes"),
            Path.Combine(Path.GetTempPath(), "not-a-manual-" + Guid.NewGuid() + ".pdf"));

        //Assert
        manual.Should().BeNull();
    }

    [Fact]
    public async Task the_view_document_carries_a_raster_page_per_page()
    {
        //Arrange
        ManualLibrary library = new ManualLibrary();
        ManualDefinition definition = ManualCatalog.Find("changes");
        using PdfManual manual = await PdfManual.OpenAsync(
            definition, library.PathOf(definition));

        //Act
        MusicDocument document = manual.ToDocument();

        //Assert
        document.Count.Should().Be(10);
        document.Pages[0].Should().BeOfType<RasterPage>();
        ((RasterPage)document.Pages[3]).Number.Should().Be(4);

        //The page knows how big it is before anything has been drawn — the
        //layout asks first (board trap 33).
        document.Pages[0].PageWidth.Should().Be(595.0);
    }

    [Fact]
    public async Task a_page_arrives_after_it_has_been_asked_for()
    {
        //Arrange
        ManualLibrary library = new ManualLibrary();
        ManualDefinition definition = ManualCatalog.Find("changes");
        using PdfManual manual = await PdfManual.OpenAsync(
            definition, library.PathOf(definition));
        IPageImageSource page = manual.Pages[0];

        TaskCompletionSource ready = new TaskCompletionSource();
        page.ImageReady += (_, _) => ready.TrySetResult();

        //Act — the first ask never waits; it starts the rendering and answers
        //with nothing, which is what lets it be called while painting.
        SKImage first = page.Image(600, 849);
        await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        SKImage second = page.Image(600, 849);

        //Assert
        first.Should().BeNull();
        second.Should().NotBeNull();
        second.Width.Should().Be(768);
    }

    [Fact]
    public async Task a_page_already_rendered_answers_at_any_size()
    {
        //Arrange
        ManualLibrary library = new ManualLibrary();
        ManualDefinition definition = ManualCatalog.Find("changes");
        using PdfManual manual = await PdfManual.OpenAsync(
            definition, library.PathOf(definition));
        IPageImageSource page = manual.Pages[1];

        TaskCompletionSource ready = new TaskCompletionSource();
        page.ImageReady += (_, _) => ready.TrySetResult();
        page.Image(600, 849);
        await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        //Act — a different zoom asks for a different width; what comes back
        //straight away is the rendering already in hand, scaled by the page.
        SKImage during = page.Image(1400, 1981);

        //Assert
        during.Should().NotBeNull();
        during.Width.Should().Be(768);
    }

    [Theory]
    [InlineData(1, 256)]
    [InlineData(256, 256)]
    [InlineData(257, 512)]
    [InlineData(600, 768)]
    [InlineData(2048, 2048)]
    [InlineData(9000, 2048)]
    public void render_widths_are_bucketed_and_capped(int wanted, int expected)
    {
        //Assert — a zoom is a stream of slightly different widths, and
        //rendering every one would keep a 1,280-page manual permanently busy.
        PdfManualAccess.RenderWidthFor(wanted).Should().Be(expected);
    }
}

/// <summary>Reaches PdfManual's internal width rule.</summary>
internal static class PdfManualAccess
{
    internal static int RenderWidthFor(int wanted)
        => (int)typeof(PdfManual)
            .GetMethod("RenderWidthFor",
                System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.NonPublic)
            .Invoke(null, new object[] { wanted });
}

/// <summary>Handing a file to the desktop's own application for it.</summary>
public class HelperApplicationTests
{
    [Fact]
    public void a_command_line_splits_like_the_shell_does()
    {
        //Assert — upstream's own expression: quoted runs stay together and the
        //quotes come off.
        HelperApplications.ShellSplit("evince").Should().Equal("evince");
        HelperApplications.ShellSplit("okular --page $f")
            .Should().Equal("okular", "--page", "$f");
        HelperApplications.ShellSplit("\"/opt/my viewer/run\" -a")
            .Should().Equal("/opt/my viewer/run", "-a");
        HelperApplications.ShellSplit(null).Should().BeEmpty();
    }

    [Fact]
    public void a_files_kind_decides_which_helper_opens_it()
    {
        //Assert — upstream's own mapping, and the extension comparison is
        //invariant-culture so a Turkish locale does not lose ".MIDI".
        HelperApplications.TypeFor(new Uri("/tmp/score.pdf", UriKind.Absolute))
            .Should().Be("pdf");
        HelperApplications.TypeFor(new Uri("/tmp/score.MIDI", UriKind.Absolute))
            .Should().Be("midi");
        HelperApplications.TypeFor(new Uri("/tmp/page.JPEG", UriKind.Absolute))
            .Should().Be("image");
        HelperApplications.TypeFor(new Uri("/tmp/score.svg", UriKind.Absolute))
            .Should().Be("browser");
        HelperApplications.TypeFor(new Uri("mailto:someone@example.com"))
            .Should().Be("email");
    }

    [Fact]
    public void a_type_the_caller_named_is_not_second_guessed()
    {
        //Assert — only the default "browser" is refined; a caller asking for a
        //terminal in a directory means it.
        HelperApplications.TypeFor(new Uri("/tmp", UriKind.Absolute), "shell")
            .Should().Be("shell");
    }

    [Fact]
    public void a_directory_is_a_directory()
    {
        //Assert
        HelperApplications.TypeFor(new Uri(Path.GetTempPath(), UriKind.Absolute))
            .Should().Be("directory");
    }

    [Fact]
    public void with_no_settings_no_helper_is_configured()
    {
        //Arrange
        HelperApplications helpers = new HelperApplications();

        //Assert — every type then goes to the desktop's own handler.
        helpers.Command("pdf").Should().BeNull();
    }

    [Fact]
    public void a_configured_helper_is_read_and_split()
    {
        //Arrange
        using SettingsStore settings = new SettingsStore(
            Path.Combine(Path.GetTempPath(), "fresco-helpers-" + Guid.NewGuid()));
        settings.SetString(HelperApplications.SettingsPrefix + "pdf", "okular --page $f");

        //Act
        var command = new HelperApplications(settings).Command("pdf");

        //Assert
        command.Should().Equal("okular", "--page", "$f");
    }

    [Fact]
    public void there_is_always_a_terminal_to_offer()
    {
        //Assert — upstream yields at least one candidate on every platform,
        //and the last Linux one is unconditional.
        var terminals = HelperApplications.TerminalCommands().ToList();
        terminals.Should().NotBeEmpty();
        terminals[^1].Should().NotBeEmpty();
    }

    [Fact]
    public void a_url_that_names_no_local_file_has_none()
    {
        //Assert
        HelperApplications.LocalFile(new Uri("https://example.com/a.pdf"))
            .Should().BeNull();
        HelperApplications.LocalFile(new Uri("/tmp/a.pdf", UriKind.Absolute))
            .Should().Be("/tmp/a.pdf");
    }
}
