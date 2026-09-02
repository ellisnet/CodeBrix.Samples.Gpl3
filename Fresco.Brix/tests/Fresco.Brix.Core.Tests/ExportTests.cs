// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Export;
using Fresco.Brix.Ly.MusicXml;
using Fresco.Brix.MusicView;
using SilverAssertions;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>Writing the current document out as MusicXML.</summary>
public class MusicXmlExportTests
{
    private const string Score = "\\version \"2.24.0\"\n"
        + "\\header { title = \"Twinkle\" composer = \"Traditional\" }\n"
        + "\\relative c'' { c4 c g' g | a a g g }\n";

    [Fact]
    public void the_document_is_musicxml_with_the_standard_doctype()
    {
        //Act
        string xml = MusicXmlExport.Convert(Score).ToDocumentString();

        //Assert — ruling FR15: the DOCTYPE and the root element name the SAME
        //version, and it is the one the corpus is validated against.
        xml.Should().StartWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.Should().Contain("-//Recordare//DTD MusicXML 4.0 Partwise//EN");
        xml.Should().Contain("<score-partwise version=\"4.0\">");
    }

    [Fact]
    public void the_header_reaches_the_score_information()
    {
        //Act
        string xml = MusicXmlExport.Convert(Score).ToDocumentString();

        //Assert
        xml.Should().Contain("<movement-title>Twinkle</movement-title>");
        xml.Should().Contain("<creator type=\"composer\">Traditional</creator>");
    }

    [Fact]
    public void the_music_comes_out_as_notes_in_measures()
    {
        //Act
        string xml = MusicXmlExport.Convert(Score).ToDocumentString();

        //Assert — eight notes over two bars.
        Occurrences(xml, "<note>").Should().Be(8);
        Occurrences(xml, "<measure number=").Should().Be(2);
        xml.Should().Contain("<step>C</step>");
    }

    [Fact]
    public void the_software_element_names_this_application_and_not_the_converter()
    {
        //Act
        string xml = MusicXmlExport.Convert(Score).ToDocumentString();

        //Assert — upstream overwrites python-ly's own string here for exactly
        //this reason.
        xml.Should().Contain("<software>Fresco.Brix ");
        xml.Should().NotContain("python-ly");
    }

    [Fact]
    public void a_document_the_converter_cannot_read_reports_rather_than_throws()
    {
        //Arrange — a LilyPond command the converter has no handler for, which
        //upstream reports and skips rather than failing over.
        var warnings = new List<string>();

        //Act
        string xml = MusicXmlExport.Convert(
            "\\relative c'' { \\showStaffSwitch c4 d e f }", null, warnings).ToDocumentString();

        //Assert
        xml.Should().Contain("<score-partwise");
        warnings.Should().NotBeEmpty();
    }

    [Fact]
    public void writing_to_a_file_writes_the_same_document()
    {
        //Arrange
        string path = Path.Combine(Path.GetTempPath(), "fresco-xml-" + Guid.NewGuid().ToString("N") + ".xml");

        try
        {
            //Act
            MusicXmlExport.Write(Score, path);

            //Assert
            File.ReadAllText(path).Should().Be(MusicXmlExport.Convert(Score).ToDocumentString());
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    [Theory]
    [InlineData("/scores/twinkle.ly", "/scores/twinkle.xml")]
    [InlineData(null, "document.xml")]
    public void the_suggested_name_swaps_the_suffix(string from, string expected)
    {
        //Assert
        MusicXmlExport.SuggestedName(from).Should().Be(expected);
    }

    [Fact]
    public void a_document_with_no_music_is_refused_and_no_file_is_written()
    {
        //Arrange — ruling FR15. A document the converter cannot turn into any
        //part would come out as a <part-list/> skeleton, which is not valid
        //MusicXML however willingly a reader opens it. Upstream writes it
        //anyway; Fresco.Brix does not write a non-conformant file, so it says
        //so and writes nothing.
        string path = Path.Combine(
            Path.GetTempPath(), "fresco-none-" + Guid.NewGuid().ToString("N") + ".xml");

        try
        {
            //Act
            MusicXmlExportResult result = MusicXmlExport.Write(
                "\\header { title = \"nothing to see\" }\n", path);

            //Assert
            result.Ok.Should().BeFalse();
            result.Reason.Should().NotBeNullOrEmpty();
            File.Exists(path).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    [Fact]
    public void a_document_with_music_is_written_and_says_where()
    {
        //Arrange
        string path = Path.Combine(
            Path.GetTempPath(), "fresco-some-" + Guid.NewGuid().ToString("N") + ".xml");

        try
        {
            //Act
            MusicXmlExportResult result = MusicXmlExport.Write(Score, path);

            //Assert
            result.Ok.Should().BeTrue();
            result.Path.Should().Be(path);
            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    private static int Occurrences(string text, string needle)
    {
        int count = 0;
        int at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at++;
        }

        return count;
    }
}

/// <summary>Writing the source out as syntax-highlighted HTML.</summary>
public class ColoredHtmlTests
{
    private const string Source = "\\relative c'' { c4 d e f % a comment\n}\n";

    [Fact]
    public void a_whole_document_is_produced_by_default()
    {
        //Act
        string html = ColoredHtml.FromText(Source);

        //Assert
        html.Should().StartWith("<!DOCTYPE HTML PUBLIC");
        html.Should().Contain("<title></title>");
        html.Should().Contain("</html>");
    }

    [Fact]
    public void the_tokens_come_out_in_spans_and_the_text_is_all_there()
    {
        //Act
        string html = ColoredHtml.FromText(Source);

        //Assert
        html.Should().Contain("<span ");
        html.Should().Contain("relative");
        html.Should().Contain("a comment");
    }

    [Fact]
    public void inline_styles_carry_the_colours_and_no_stylesheet_is_written()
    {
        //Act
        string html = ColoredHtml.FromText(
            Source, options: new ColoredHtmlOptions { Inline = true });

        //Assert — a clipboard has nowhere to put a stylesheet.
        html.Should().Contain("style=\"");
        html.Should().NotContain("<style type=\"text/css\">");
    }

    [Fact]
    public void a_stylesheet_is_written_when_the_styles_are_not_inline()
    {
        //Act
        string html = ColoredHtml.FromText(
            Source, options: new ColoredHtmlOptions { Inline = false });

        //Assert
        html.Should().Contain("<style type=\"text/css\">");
        html.Should().Contain("class=\"");
    }

    [Fact]
    public void a_body_only_export_has_no_document_around_it()
    {
        //Act
        string html = ColoredHtml.FromText(
            Source, options: new ColoredHtmlOptions { FullHtml = false });

        //Assert
        html.Should().NotContain("<!DOCTYPE");
        //The wrapper is still there; whether it carries a style attribute is
        //the Inline setting's business, and it defaults on.
        html.Should().StartWith("<pre id=\"document\"");
    }

    [Fact]
    public void the_markup_characters_in_the_source_are_escaped()
    {
        //Act
        string html = ColoredHtml.FromText("\\markup { \"a < b & c > d\" }");

        //Assert
        html.Should().Contain("&lt;");
        html.Should().Contain("&amp;");
        html.Should().Contain("&gt;");
    }

    [Fact]
    public void line_numbers_come_out_in_a_table_beside_the_source()
    {
        //Act
        string html = ColoredHtml.FromText(
            "c4\nd4\ne4\n", options: new ColoredHtmlOptions { NumberLines = true });

        //Assert
        html.Should().Contain("<table border=\"0\"");
        html.Should().Contain("id=\"linenumbers\"");
    }

    [Theory]
    [InlineData("/scores/twinkle.ly", "/scores/twinkle.html")]
    [InlineData("/scores/page.html", "/scores/page_html.html")]
    [InlineData(null, "document.html")]
    public void the_suggested_name_never_overwrites_the_source(string from, string expected)
    {
        //Assert — upstream's own rule, oddity included.
        ColoredHtml.SuggestedName(from).Should().Be(expected);
    }
}

/// <summary>Writing the engraved score out.</summary>
public class ScoreExportTests
{
    private static string Fixture(string name)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static MusicDocument Score(params string[] names)
    {
        var pages = new List<ScorePage>();
        foreach (string name in names)
        {
            var page = new SvgPage(Fixture(name), new Fresco.Brix.MusicView.LilyPortTypefaceResolver());
            page.UpdateSize(96, 96, 1.0);
            pages.Add(page);
        }

        var document = new MusicDocument(pages) { FileName = Fixture(names[0]) };
        return document;
    }

    [Fact]
    public void a_score_writes_a_vector_pdf_with_the_engines_own_faces_in_it()
    {
        //Arrange
        MusicDocument score = Score("twinkle.svg");
        string path = Path.Combine(
            Path.GetTempPath(), "fresco-score-" + Guid.NewGuid().ToString("N") + ".pdf");

        try
        {
            //Act
            int pages = ScoreExport.WritePdf(score, path);
            string bytes = Encoding.Latin1.GetString(File.ReadAllBytes(path));

            //Assert — board trap 60: the face the ENGINE measured the title
            //with, not the one a non-idempotent resolver used to hand back —
            //and, since the FR7 (b) re-base, embedded as a real OpenType program
            //subset sparsely (/FontFile3 on a /CIDFontType0, PDF 1.6) rather than
            //as Type 3 glyph procedures.
            pages.Should().Be(1);
            bytes.Should().NotContain("/Subtype /Image");
            bytes.Should().NotContain("/Subtype/Image");
            bytes.Should().Contain("C059");
            bytes.Should().NotContain("TeXGyreSchola");
            bytes.Should().NotContain("TeX Gyre Schola");
            bytes.Should().NotContain("Merriweather");
            bytes.Should().Contain("/FontFile3");
            bytes.Should().Contain("/CIDFontType0");
            bytes.Substring(0, 8).Should().Be("%PDF-1.6");
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    [Fact]
    public void the_engines_faces_are_written_out_once_for_the_pdf_writer_to_register()
    {
        //Arrange — the faces are embedded resources of the engine; the writer
        //registers files, so they are extracted beside the settings.
        string directory = Path.Combine(Path.GetTempPath(), "fresco-fonts-" + Guid.NewGuid().ToString("N"));

        try
        {
            //Act
            var files = LilyPortScorePdfFonts.Extract(directory);
            var again = LilyPortScorePdfFonts.Extract(directory);

            //Assert — sixteen faces, four chains of four; the second call finds
            //them at their size and writes nothing.
            files.Should().HaveCount(16);
            again.Should().Equal(files);
            LilyPortScorePdfFonts.MapFamily("serif").Should().Be("C059");
            LilyPortScorePdfFonts.MapFamily("LilyPond Sans Serif").Should().Be("Nimbus Sans");
            LilyPortScorePdfFonts.MapFamily("monospace").Should().Be("Nimbus Mono PS");
            LilyPortScorePdfFonts.MapFamily("Foo Bar Baz").Should().Be("TeX Gyre Schola");
        }
        finally
        {
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
        }
    }

    [Fact]
    public void the_pdf_says_which_application_made_it()
    {
        //Arrange
        MusicDocument score = Score("twinkle.svg");
        string path = Path.Combine(
            Path.GetTempPath(), "fresco-score-" + Guid.NewGuid().ToString("N") + ".pdf");

        try
        {
            //Act
            ScoreExport.WritePdf(score, path);
            using var document = CodeBrix.PdfDocuments.Pdf.IO.PdfReader.Open(
                path, CodeBrix.PdfDocuments.Pdf.IO.PdfDocumentOpenMode.Import);

            //Assert — read back, because the writer stores the information
            //dictionary's strings as UTF-16 hex, which a byte search cannot see.
            document.Info.Creator.Should().StartWith("Fresco.Brix");
            document.Info.Title.Should().Be("twinkle");
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    [Fact]
    public void exporting_a_page_as_svg_copies_the_engines_own_file()
    {
        //Arrange
        MusicDocument score = Score("twinkle.svg");
        string path = Path.Combine(
            Path.GetTempPath(), "fresco-page-" + Guid.NewGuid().ToString("N") + ".svg");

        try
        {
            //Act
            ScoreExport.WriteSvg(score.Pages[0], path);

            //Assert — the file the engine wrote, anchors and all; a re-recording
            //would draw the same shapes and lose them.
            string written = File.ReadAllText(path);
            written.Should().Be(File.ReadAllText(Fixture("twinkle.svg")));
            written.Should().Contain("textedit://");
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    [Fact]
    public void exporting_a_page_as_png_writes_a_png_at_the_asked_for_size()
    {
        //Arrange
        MusicDocument score = Score("twinkle.svg");
        string path = Path.Combine(
            Path.GetTempPath(), "fresco-page-" + Guid.NewGuid().ToString("N") + ".png");

        try
        {
            //Act
            ScoreExport.WritePng(score.Pages[0], path, 96.0);

            //Assert
            using SKBitmap bitmap = SKBitmap.Decode(path);
            bitmap.Width.Should().Be(794);
            bitmap.Height.Should().Be(1123);
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    [Fact]
    public void the_suggested_name_keeps_the_scores_own_name()
    {
        //Arrange
        MusicDocument score = Score("twinkle.svg");

        //Act
        string name = ScoreExport.SuggestedName(score, ".pdf");

        //Assert
        Path.GetFileName(name).Should().Be("twinkle.pdf");
    }
}

/// <summary>Rendering an engraved MIDI file to a sound file.</summary>
public class AudioExportTests
{
    [Theory]
    [InlineData("/scores/twinkle.ly", "/scores/twinkle.wav")]
    [InlineData(null, "document.wav")]
    public void the_suggested_name_swaps_the_suffix(string from, string expected)
    {
        //Assert
        AudioExport.SuggestedName(from).Should().Be(expected);
    }

    [Fact]
    public void a_midi_file_that_is_not_there_is_reported_rather_than_thrown()
    {
        //Arrange
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".midi");
        string output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".wav");

        //Act
        AudioExportResult result = AudioExport.Render(missing, output);

        //Assert — and nothing half-written is left behind.
        result.Ok.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        File.Exists(output).Should().BeFalse();
    }
}
