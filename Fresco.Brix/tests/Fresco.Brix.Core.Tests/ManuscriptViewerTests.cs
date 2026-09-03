// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.PdfDocuments.Drawing;
using CodeBrix.PdfDocuments.Pdf;
using CodeBrix.PdfDocuments.Pdf.Annotations;
using Fresco.Brix.Commands;
using Fresco.Brix.Documentation;
using Fresco.Brix.Manuscripts;
using Fresco.Brix.MusicView;
using Fresco.Brix.Sessions;
using Fresco.Brix.Shell;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// Writes throw-away PDFs for the manuscript tests, with and without
/// point-and-click links.
/// </summary>
/// <remarks>
/// The fixture is MADE rather than recorded, through the application's own PDF
/// stack (CodeBrix.PdfDocuments, which the pinned CodeBrix.PdfRasterizer brings)
/// — board rule 3 forbids a test naming the read-only Frescobaldi checkout, and
/// no engraved score this application can produce carries <c>textedit://</c>
/// links to record.
/// </remarks>
internal static class ManuscriptFixture
{
    /// <summary>The page width every made fixture uses, in points.</summary>
    internal const double WidthPoints = 200.0;

    /// <summary>The page height every made fixture uses, in points.</summary>
    internal const double HeightPoints = 400.0;

    /// <summary>Writes a PDF with the given number of blank pages.</summary>
    /// <param name="pages">How many pages.</param>
    /// <param name="path">Where to write it; a new temp file when null.</param>
    /// <returns>The path.</returns>
    internal static string Blank(int pages, string path = null)
        => Write(pages, null, path);

    /// <summary>Writes a PDF whose every page carries one link.</summary>
    /// <param name="pages">How many pages.</param>
    /// <param name="url">The link's URL.</param>
    /// <param name="path">Where to write it; a new temp file when null.</param>
    /// <returns>The path.</returns>
    internal static string WithLink(int pages, string url, string path = null)
        => Write(pages, url, path);

    /// <summary>Answers a path in a folder the test owns.</summary>
    /// <param name="name">The file name.</param>
    /// <returns>The path.</returns>
    internal static string PathFor(string name)
    {
        string folder = Path.Combine(
            Path.GetTempPath(), "frescobrix-manuscripts", Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, name);
    }

    private static string Write(int pages, string url, string path)
    {
        path ??= PathFor("manuscript.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        PdfDocument document = new PdfDocument();

        //⚠ THE PADDING IS LOAD-BEARING. CodeBrix.PdfDocuments 1.0.243.38 (which
        //the pinned CodeBrix.PdfRasterizer brings) refuses ANY PDF smaller than
        //1,024 bytes — PdfReader.Open throws "The file is not a valid PDF
        //document." on a file that is structurally perfect, measured at 1,022
        //bytes fails and 1,026 passes. A one-page fixture with no metadata
        //comes to 1,002 bytes and so could not be opened at all. Recorded in
        //~/ClaudeHome/FIXLIST_codebrix_packages_2026-09-01.txt (board wave W15).
        document.Info.Subject
            = "Fresco.Brix test fixture. This subject line is padding: the PDF "
            + "reader refuses any file under 1,024 bytes, and a blank one-page "
            + "document is smaller than that.";

        for (int i = 0; i < pages; i++)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromPoint(WidthPoints);
            page.Height = XUnit.FromPoint(HeightPoints);
            if (url == null) { continue; }

            //The link sits at 10..60 points across and 20..40 points up from
            //the BOTTOM, which is where PDF counts from.
            page.Annotations.Add(PdfLinkAnnotation.CreateWebLink(
                new PdfRectangle(new XPoint(10, 20), new XPoint(60, 40)), url));
        }

        document.Save(path);
        return path;
    }
}

/// <summary>
/// The open-manuscript list: what opening, choosing and closing do to it.
/// </summary>
/// <remarks>Upstream's <c>ViewdocChooserAction</c>, whose rules are the ones the
/// user guide states as promises.</remarks>
public class ManuscriptListTests
{
    [Fact]
    public void opening_files_adds_them_and_brings_the_last_one_to_the_front()
    {
        //Arrange
        ManuscriptList list = new ManuscriptList();

        //Act
        list.Load(new[] { "/tmp/a.pdf", "/tmp/b.pdf", "/tmp/c.pdf" });

        //Assert — upstream's loadFiles passes files[-1] as the active one.
        list.Count.Should().Be(3);
        list.Current.Path.Should().Be("/tmp/c.pdf");
        list.Paths().Should().BeEquivalentTo(new[] { "/tmp/a.pdf", "/tmp/b.pdf", "/tmp/c.pdf" });
    }

    [Fact]
    public void a_file_that_is_already_open_is_not_opened_twice()
    {
        //Arrange
        ManuscriptList list = new ManuscriptList();
        list.Load(new[] { "/tmp/a.pdf", "/tmp/b.pdf" });

        //Act
        IReadOnlyList<ManuscriptEntry> added
            = list.Load(new[] { "/tmp/b.pdf", "/tmp/c.pdf" });

        //Assert
        added.Should().HaveCount(1);
        list.Count.Should().Be(3);
        list.Current.Path.Should().Be("/tmp/c.pdf");
    }

    [Fact]
    public void closing_one_leaves_the_next_one_showing()
    {
        //Arrange
        ManuscriptList list = new ManuscriptList();
        list.Load(new[] { "/tmp/a.pdf", "/tmp/b.pdf", "/tmp/c.pdf" });
        list.SetCurrentIndex(1);

        //Act
        list.Remove(list.Current);

        //Assert — upstream does not move the index, so what took the closed
        //one's place becomes current.
        list.Count.Should().Be(2);
        list.Current.Path.Should().Be("/tmp/c.pdf");
    }

    [Fact]
    public void closing_the_last_one_falls_back_to_the_first()
    {
        //Arrange
        ManuscriptList list = new ManuscriptList();
        list.Load(new[] { "/tmp/a.pdf", "/tmp/b.pdf" });

        //Act — "b" is current, being the last opened.
        list.Remove(list.Current);

        //Assert — updateViewdoc's clamp.
        list.Count.Should().Be(1);
        list.Current.Path.Should().Be("/tmp/a.pdf");
    }

    [Fact]
    public void closing_the_only_one_leaves_nothing_current()
    {
        //Arrange
        ManuscriptList list = new ManuscriptList();
        list.Load(new[] { "/tmp/a.pdf" });

        //Act
        list.Remove(list.Current);

        //Assert
        list.Count.Should().Be(0);
        list.Current.Should().BeNull();
        list.CurrentIndex.Should().Be(-1);
    }

    [Fact]
    public void closing_the_others_keeps_the_one_that_was_showing()
    {
        //Arrange
        ManuscriptList list = new ManuscriptList();
        list.Load(new[] { "/tmp/a.pdf", "/tmp/b.pdf", "/tmp/c.pdf" });
        list.SetCurrentIndex(0);

        //Act
        list.RemoveOthers(list.Current);

        //Assert
        list.Count.Should().Be(1);
        list.Current.Path.Should().Be("/tmp/a.pdf");
    }

    [Fact]
    public void closing_them_all_empties_the_list()
    {
        //Arrange
        ManuscriptList list = new ManuscriptList();
        list.Load(new[] { "/tmp/a.pdf", "/tmp/b.pdf" });

        //Act
        list.RemoveAll();

        //Assert
        list.Count.Should().Be(0);
        list.CurrentIndex.Should().Be(-1);
        list.Paths().Should().BeEmpty();
    }

    [Fact]
    public void the_list_can_be_sorted_by_file_name_keeping_what_is_showing()
    {
        //Arrange
        ManuscriptList list = new ManuscriptList();
        list.Load(new[] { "/tmp/c.pdf", "/tmp/a.pdf", "/tmp/b.pdf" });
        list.SetCurrentIndex(0);

        //Act — upstream sorts by os.path.basename.
        list.Sort();

        //Assert
        list.Entries.Select(entry => entry.Name)
            .Should().BeEquivalentTo(new[] { "a.pdf", "b.pdf", "c.pdf" });
        list.Current.Name.Should().Be("c.pdf");
    }

    [Fact]
    public void a_file_that_is_not_there_is_marked_absent_and_reported()
    {
        //Arrange
        ManuscriptList list = new ManuscriptList();
        List<string> reported = null;
        list.Missing += (_, e) => reported = e.Paths.ToList();
        string missing = Path.Combine(
            Path.GetTempPath(), "no-such-manuscript-" + Guid.NewGuid() + ".pdf");

        //Act
        list.Load(new[] { missing });
        list.CheckMissingFiles();

        //Assert — upstream records ispresent when it loads and raises
        //viewdocsMissing from checkMissingFiles, after a session is restored.
        list.Entries.Single().IsPresent.Should().BeFalse();
        reported.Should().BeEquivalentTo(new[] { missing });
    }

    [Fact]
    public void nothing_is_reported_when_every_file_is_there()
    {
        //Arrange
        string path = ManuscriptFixture.Blank(1);
        ManuscriptList list = new ManuscriptList();
        bool reported = false;
        list.Missing += (_, _) => reported = true;

        //Act
        list.Load(new[] { path });
        list.CheckMissingFiles();

        //Assert
        list.Entries.Single().IsPresent.Should().BeTrue();
        reported.Should().BeFalse();
    }

    [Fact]
    public void every_listener_sees_the_clamped_index_not_the_old_one()
    {
        //Arrange — the fence for a defect found on X11 at board wave W15:
        //`Changed' was announced BEFORE the index was clamped, so the chooser
        //wrote an index the list no longer had and the panel then believed
        //nothing was current while a page was still on screen.
        ManuscriptList list = new ManuscriptList();
        list.Load(new[] { "/tmp/a.pdf", "/tmp/b.pdf", "/tmp/c.pdf" });
        list.SetCurrentIndex(1);

        List<int> seenByChanged = new List<int>();
        List<int> seenByCurrentChanged = new List<int>();
        list.Changed += (_, _) => seenByChanged.Add(list.CurrentIndex);
        list.CurrentChanged += (_, _) => seenByCurrentChanged.Add(list.CurrentIndex);

        //Act — one left, so the old index 1 is out of range.
        list.RemoveOthers(list.Entries[1]);

        //Assert
        list.Count.Should().Be(1);
        list.CurrentIndex.Should().Be(0);
        list.Current.Path.Should().Be("/tmp/b.pdf");
        seenByChanged.Should().BeEquivalentTo(new[] { 0 });
        seenByCurrentChanged.Should().BeEquivalentTo(new[] { 0 });
    }

    [Fact]
    public void an_emptied_list_tells_both_listeners_that_nothing_is_current()
    {
        //Arrange
        ManuscriptList list = new ManuscriptList();
        list.Load(new[] { "/tmp/a.pdf", "/tmp/b.pdf" });

        List<int> seen = new List<int>();
        list.Changed += (_, _) => seen.Add(list.CurrentIndex);

        //Act
        list.RemoveAll();

        //Assert
        seen.Should().BeEquivalentTo(new[] { -1 });
        list.Current.Should().BeNull();
    }

    [Fact]
    public void choosing_a_file_by_name_brings_it_to_the_front()
    {
        //Arrange
        ManuscriptList list = new ManuscriptList();
        list.Load(new[] { "/tmp/a.pdf", "/tmp/b.pdf", "/tmp/c.pdf" });

        //Act
        bool found = list.SetActive("/tmp/a.pdf");

        //Assert
        found.Should().BeTrue();
        list.Current.Path.Should().Be("/tmp/a.pdf");
        list.SetActive("/tmp/nowhere.pdf").Should().BeFalse();
    }
}

/// <summary>Opening a PDF as a manuscript, and reading it again.</summary>
public class PdfManuscriptTests
{
    [Fact]
    public async Task an_open_manuscript_offers_one_page_per_page()
    {
        //Arrange
        string path = ManuscriptFixture.Blank(3);

        //Act
        using PdfManuscript manuscript = await PdfManuscript.OpenAsync(path);

        //Assert
        manuscript.Should().NotBeNull();
        manuscript.PageCount.Should().Be(3);
        manuscript.Pages.Should().HaveCount(3);
        manuscript.Document.Count.Should().Be(3);
        manuscript.HasLinks.Should().BeFalse();
    }

    [Fact]
    public async Task a_missing_file_opens_as_nothing_rather_than_throwing()
    {
        //Act
        PdfManuscript manuscript = await PdfManuscript.OpenAsync(
            Path.Combine(Path.GetTempPath(), "gone-" + Guid.NewGuid() + ".pdf"));

        //Assert
        manuscript.Should().BeNull();
    }

    [Fact]
    public async Task reloading_reads_a_file_that_changed_on_disk()
    {
        //Arrange — the reload rule: upstream replaces the open document with a
        //freshly loaded one over the SAME file name, because a loaded document
        //holds what it read.
        string path = ManuscriptFixture.Blank(2);
        using (PdfManuscript before = await PdfManuscript.OpenAsync(path))
        {
            before.PageCount.Should().Be(2);
        }

        ManuscriptFixture.Blank(5, path);

        //Act
        using PdfManuscript after = await PdfManuscript.OpenAsync(path);

        //Assert
        after.PageCount.Should().Be(5);
    }

    [Fact]
    public async Task a_scores_point_and_click_links_reach_its_pages()
    {
        //Arrange
        string path = ManuscriptFixture.WithLink(2, "textedit:///tmp/score.ly:3:5:6");

        //Act
        using PdfManuscript manuscript = await PdfManuscript.OpenAsync(path);

        //Assert
        manuscript.HasLinks.Should().BeTrue();
        foreach (ScorePage page in manuscript.Document.Pages)
        {
            Link link = page.Links().Single();
            link.Url.Should().Be("textedit:///tmp/score.ly:3:5:6");

            //10..60 of 200 across, and 20..40 up from the bottom of 400 — which
            //is 0.90..0.95 down from the top.
            link.Left.Should().BeApproximately(0.05f, 0.0001f);
            link.Right.Should().BeApproximately(0.30f, 0.0001f);
            link.Top.Should().BeApproximately(0.90f, 0.0001f);
            link.Bottom.Should().BeApproximately(0.95f, 0.0001f);
        }
    }

    [Fact]
    public async Task the_pages_of_a_bundled_manual_open_as_a_manuscript_too()
    {
        //Arrange — a manuscript is any PDF, and the nine bundled manuals are
        //the known-good PDFs this repository already carries.
        ManualLibrary library = new ManualLibrary();
        ManualDefinition definition = ManualCatalog.Find("changes");

        //Act
        using PdfManuscript manuscript = await PdfManuscript.OpenAsync(
            library.PathOf(definition));

        //Assert
        manuscript.PageCount.Should().Be(10);
        manuscript.Document.Pages[0].PageWidth.Should().Be(595.0);
        manuscript.Document.Pages[0].PageHeight.Should().Be(842.0);
    }
}

/// <summary>
/// Reading a PDF's link annotations, and turning them into the fractions the
/// paged view hit-tests.
/// </summary>
public class PdfLinksTests
{
    [Fact]
    public void a_pdf_with_no_links_answers_no_pages()
    {
        //Arrange
        string path = ManuscriptFixture.Blank(2);

        //Act
        IReadOnlyList<LinkList> links = PdfLinks.Read(path);

        //Assert
        links.Should().BeEmpty();
    }

    [Fact]
    public void a_missing_file_answers_no_pages_rather_than_throwing()
    {
        //Act
        IReadOnlyList<LinkList> links = PdfLinks.Read(
            Path.Combine(Path.GetTempPath(), "gone-" + Guid.NewGuid() + ".pdf"));

        //Assert
        links.Should().BeEmpty();
    }

    [Fact]
    public void an_external_hyperlink_comes_back_with_its_address()
    {
        //Arrange — the guide names this use of its own: "keeping lists of
        //useful links around in that window".
        string path = ManuscriptFixture.WithLink(1, "https://example.invalid/notes");

        //Act
        IReadOnlyList<LinkList> links = PdfLinks.Read(path);

        //Assert
        Link link = links.Single().Single();
        link.Url.Should().Be("https://example.invalid/notes");
        link.IsExternal.Should().BeTrue();
    }

    [Fact]
    public void the_bundled_manuals_own_links_come_back()
    {
        //Arrange — a shipped asset, so the reader is proved against a PDF this
        //repository did not write.
        ManualLibrary library = new ManualLibrary();

        //Act
        IReadOnlyList<LinkList> links = PdfLinks.Read(
            library.PathOf(ManualCatalog.Find("changes")));

        //Assert — the URI links only: a /Dest link names a page OBJECT and
        //there is nowhere in Link to put that.
        links.Should().NotBeEmpty();
        links.SelectMany(page => page).Should().NotBeEmpty();
        links.SelectMany(page => page)
            .Should().OnlyContain(link => link.Url.Contains("://"));
    }

    [Theory]
    //An upright page: 10..60 of 200 across, 20..40 up from the bottom of 400.
    [InlineData(0, 0.05f, 0.90f, 0.30f, 0.95f)]
    //A quarter turn clockwise sends (u, v) to (1 - v, u).
    [InlineData(90, 0.05f, 0.05f, 0.10f, 0.30f)]
    [InlineData(180, 0.70f, 0.05f, 0.95f, 0.10f)]
    [InlineData(270, 0.90f, 0.70f, 0.95f, 0.95f)]
    public void a_rotated_page_turns_its_links_with_it(
        int rotate, float left, float top, float right, float bottom)
    {
        //Arrange, Act
        var area = PdfLinks.AreaOf((10, 20, 60, 40), (0, 0, 200, 400), rotate);

        //Assert — board trap 65's other half: PdfPage.Width/Height are already
        //turned, an annotation's /Rect is not.
        area.Left.Should().BeApproximately(left, 0.0001f);
        area.Top.Should().BeApproximately(top, 0.0001f);
        area.Right.Should().BeApproximately(right, 0.0001f);
        area.Bottom.Should().BeApproximately(bottom, 0.0001f);
    }

    [Fact]
    public void a_page_box_that_does_not_start_at_the_origin_is_allowed_for()
    {
        //Arrange, Act — the same rectangle, in a box moved 20 right and 10 up.
        var area = PdfLinks.AreaOf((30, 30, 80, 50), (20, 10, 220, 410), 0);

        //Assert
        area.Left.Should().BeApproximately(0.05f, 0.0001f);
        area.Top.Should().BeApproximately(0.90f, 0.0001f);
        area.Right.Should().BeApproximately(0.30f, 0.0001f);
        area.Bottom.Should().BeApproximately(0.95f, 0.0001f);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 90)]
    [InlineData(360, 0)]
    [InlineData(630, 270)]
    [InlineData(-90, 270)]
    [InlineData(-180, 180)]
    public void any_multiple_of_ninety_reduces_to_one_of_four_turns(
        int rotate, int expected)
    {
        //Arrange, Act, Assert — the specification allows any multiple of 90,
        //positive or negative.
        PdfLinks.Turn(rotate).Should().Be(expected);
    }

    [Fact]
    public void a_box_with_no_area_answers_an_empty_rectangle()
    {
        //Arrange, Act
        var area = PdfLinks.AreaOf((10, 20, 60, 40), (0, 0, 0, 0), 0);

        //Assert
        area.Should().Be((0f, 0f, 0f, 0f));
    }
}

/// <summary>A raster page's links and its excerpt, which the PNG export uses.</summary>
public class RasterPageLinkTests
{
    [Fact]
    public void a_raster_page_carries_no_links_until_it_is_given_some()
    {
        //Arrange
        using SkiaSharp.SKImage picture = Picture();
        RasterPage page = new RasterPage(new MemoryImageSource(picture));

        //Act, Assert
        page.Links().Should().BeEmpty();
    }

    [Fact]
    public void a_raster_page_hands_back_the_links_it_was_given()
    {
        //Arrange
        using SkiaSharp.SKImage picture = Picture();
        RasterPage page = new RasterPage(new MemoryImageSource(picture));
        Link link = new Link(0.05f, 0.90f, 0.30f, 0.95f, "textedit:///tmp/x.ly:1:0:0");

        //Act
        page.SetLinks(new LinkList(new[] { link }));

        //Assert
        page.Links().Single().Url.Should().Be("textedit:///tmp/x.ly:1:0:0");
    }

    [Fact]
    public async Task an_excerpt_of_a_raster_page_comes_out_the_size_it_was_asked_for()
    {
        //Arrange — the PNG excerpt of a rubber-band selection: the dialog
        //renders through ScorePage.Image, the one drawing path every page kind
        //has, so a RASTER page needed nothing added for it to work.
        string path = ManuscriptFixture.Blank(1);
        using PdfManuscript manuscript = await PdfManuscript.OpenAsync(path);
        manuscript.Should().NotBeNull();
        manuscript.Document.Should().NotBeNull();
        manuscript.Document.Count.Should().Be(1);
        ScorePage page = manuscript.Document.Pages[0];
        page.Should().NotBeNull();
        page.UpdateSize(72.0, 72.0, 1.0);
        page.Width.Should().BeGreaterThan(0);

        //Act — half the page, at twice its natural resolution.
        using SkiaSharp.SKImage image = page.Image(
            new SkiaSharp.SKRect(0f, 0f, page.Width / 2f, page.Height / 2f), 144.0, 144.0);

        //Assert
        image.Should().NotBeNull();
        image.Width.Should().Be((int)Math.Round(ManuscriptFixture.WidthPoints));
        image.Height.Should().Be((int)Math.Round(ManuscriptFixture.HeightPoints));
    }

    /// <summary>A small picture to hang a page on.</summary>
    /// <returns>The picture.</returns>
    private static SkiaSharp.SKImage Picture()
    {
        SkiaSharp.SKImageInfo info = new SkiaSharp.SKImageInfo(20, 40);
        using SkiaSharp.SKSurface surface = SkiaSharp.SKSurface.Create(info);
        surface.Canvas.Clear(SkiaSharp.SKColors.White);
        return surface.Snapshot();
    }
}

/// <summary>The manuscripts a named session carries.</summary>
public class ManuscriptSessionTests
{
    [Fact]
    public void a_session_remembers_the_open_manuscripts_and_which_was_showing()
    {
        //Arrange — the user guide's promise: "the opened manuscripts are
        //maintained in sessions, alongside the input documents".
        Services.SettingsStore settings = TestSettings.Create();
        SessionStore store = new SessionStore(settings);

        //Act
        store.Write("work", new SessionData
        {
            Paths = new[] { "/tmp/score.ly" },
            ActiveIndex = 0,
            Manuscripts = new[] { "/tmp/a.pdf", "/tmp/b.pdf" },
            ActiveManuscript = 1,
        });
        SessionData read = store.Read("work");

        //Assert
        read.Manuscripts.Should().BeEquivalentTo(new[] { "/tmp/a.pdf", "/tmp/b.pdf" });
        read.ActiveManuscript.Should().Be(1);
        read.Paths.Should().BeEquivalentTo(new[] { "/tmp/score.ly" });
    }

    [Fact]
    public void a_session_that_never_had_a_manuscript_reads_back_empty()
    {
        //Arrange
        Services.SettingsStore settings = TestSettings.Create();
        SessionStore store = new SessionStore(settings);

        //Act
        store.Write("plain", new SessionData { Paths = new[] { "/tmp/score.ly" } });
        SessionData read = store.Read("plain");

        //Assert
        read.Manuscripts.Should().BeEmpty();
        read.ActiveManuscript.Should().Be(-1);
    }

    [Fact]
    public void the_panel_answers_what_a_session_should_remember()
    {
        //Arrange
        ManuscriptViewerPanel panel = Panel();
        panel.Manuscripts.Load(new[] { "/tmp/a.pdf", "/tmp/b.pdf" });
        panel.Manuscripts.SetCurrentIndex(0);

        //Act
        var data = panel.SessionData();

        //Assert
        data.Paths.Should().BeEquivalentTo(new[] { "/tmp/a.pdf", "/tmp/b.pdf" });
        data.Active.Should().Be(0);
    }

    [Fact]
    public void restoring_a_session_replaces_the_list_and_reports_what_is_gone()
    {
        //Arrange
        ManuscriptViewerPanel panel = Panel();
        panel.Manuscripts.Load(new[] { "/tmp/old.pdf" });

        List<string> reported = null;
        panel.ReportMissing = paths => reported = paths.ToList();

        string here = ManuscriptFixture.Blank(1);
        string gone = Path.Combine(
            Path.GetTempPath(), "gone-" + Guid.NewGuid() + ".pdf");

        //Act
        panel.RestoreSession(new[] { here, gone }, 1);

        //Assert
        panel.Manuscripts.Paths().Should().BeEquivalentTo(new[] { here, gone });
        panel.Manuscripts.Current.Path.Should().Be(gone);
        reported.Should().BeEquivalentTo(new[] { gone });
    }

    [Fact]
    public void restoring_an_empty_session_empties_the_list()
    {
        //Arrange
        ManuscriptViewerPanel panel = Panel();
        panel.Manuscripts.Load(new[] { "/tmp/old.pdf" });

        //Act
        panel.RestoreSession(Array.Empty<string>(), -1);

        //Assert
        panel.Manuscripts.Count.Should().Be(0);
    }

    private static ManuscriptViewerPanel Panel()
        => new ManuscriptViewerPanel(new ManuscriptViewerActions(TestSettings.Create()));
}

/// <summary>The Manuscript Viewer's own commands and its panel key.</summary>
public class ManuscriptViewerActionsTests
{
    [Fact]
    public void the_collection_is_upstreams_own()
    {
        //Arrange, Act
        ManuscriptViewerActions actions = new ManuscriptViewerActions(TestSettings.Create());

        //Assert — ManuscriptViewerActions.name = "manuscript".
        actions.Name.Should().Be("manuscript");
        actions.ViewerOpen.Name.Should().Be("viewer_open");
        actions.ViewerClose.Name.Should().Be("viewer_close");
        actions.ViewerCloseOther.Name.Should().Be("viewer_close_other");
        actions.ViewerCloseAll.Name.Should().Be("viewer_close_all");
        actions.ViewerDocumentSelect.Name.Should().Be("viewer_document_select");
        actions.ViewerShowToolbar.Name.Should().Be("viewer_show_toolbar");
    }

    [Fact]
    public void the_six_captions_the_manuscript_viewer_rewords_are_its_own()
    {
        //Arrange, Act
        ManuscriptViewerActions actions = new ManuscriptViewerActions(TestSettings.Create());

        //Assert — viewers/manuscript/__init__.py translateUI, verbatim.
        actions.ViewerDocumentSelect.Text.Should().Be("Select Manuscript Document");
        actions.ViewerOpen.Text.Should().Be("Open manuscript(s)");
        actions.ViewerOpen.IconText.Should().Be("Open");
        actions.ViewerClose.Text.Should().Be("Close manuscript");
        actions.ViewerClose.IconText.Should().Be("Close");
        actions.ViewerCloseOther.Text.Should().Be("Close other manuscripts");
        actions.ViewerCloseAll.Text.Should().Be("Close all manuscripts");
    }

    [Fact]
    public void there_is_no_print_command_anywhere_in_the_collection()
    {
        //Arrange, Act
        ManuscriptViewerActions actions = new ManuscriptViewerActions(TestSettings.Create());

        //Assert — ruling FR5.5, and Jeremy again on 2026-09-02. Upstream's
        //viewer_print sits between the chooser and the zoom controls.
        actions.Actions.Keys.Should().NotContain("viewer_print");
        actions.Actions.Values
            .Should().NotContain(action => action.Text.Contains("Print"));
    }

    [Fact]
    public void the_panel_carries_upstreams_own_toggle_key_and_name()
    {
        //Arrange, Act
        ManuscriptViewerPanel panel = new ManuscriptViewerPanel(
            new ManuscriptViewerActions(TestSettings.Create()));

        //Assert — viewers/manuscript/__init__.py:37-38: Meta+Alt+A, docked
        //right. KeySequence writes its modifiers in ITS own order (Ctrl, Shift,
        //Alt, Meta), so "Meta+Alt+A" reads back as "Alt+Meta+A" — the same
        //chord, and every panel toggle here is written that way (board trap 37).
        panel.ToggleAction.Shortcuts.Single().ToString().Should().Be("Alt+Meta+A");
        panel.Name.Should().Be("manuscriptview");
        panel.Area.Should().Be(DockArea.Right);
    }

    [Fact]
    public void the_panels_titles_are_upstreams_two()
    {
        //Arrange
        ManuscriptViewerPanel panel = new ManuscriptViewerPanel(
            new ManuscriptViewerActions(TestSettings.Create()));

        //Act
        panel.TranslateUI();

        //Assert — the dock says "Manuscript" and the Tools entry says
        //"Manuscript Viewer", as upstream's translateUI sets them.
        panel.Title.Should().Be("Manuscript");
        panel.ToggleAction.Text.Should().Be("Manuscript Viewer");
    }

    [Fact]
    public void the_help_entry_opens_the_guides_own_page()
    {
        //Arrange, Act, Assert — upstream's slotShowHelp shows viewerName().
        ManuscriptViewerPanel.HelpPage.Should().Be("manuscriptview");
        ManuscriptViewerPanel.SettingsPrefix.Should().Be("manuscriptview/");
    }
}
