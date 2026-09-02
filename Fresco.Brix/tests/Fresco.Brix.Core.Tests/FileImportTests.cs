// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Engrave;
using Fresco.Brix.Import;
using Fresco.Brix.Services;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// File &gt; Import over REAL files: that each of the four inputs converts to
/// LilyPond source, that the options the dialog collects reach the converter
/// and change what comes out, and that the converter's remarks arrive on the
/// job's error channel where the log reads them.
/// </summary>
/// <remarks>
/// <para>
/// The converters are <c>CodeBrix.LilyPort.Importers</c> and are verified on
/// the LilyPort board against the abc, MIDI and MusicXML corpora; what is
/// proved here is that THIS application reaches them correctly — the right
/// entry point for the right suffix, the file read in the right encoding, the
/// options carried across, and the result and the messages coming back.
/// </para>
/// <para>
/// The four sample files under <c>fixtures/import/samples</c> are written for
/// these tests: a four-bar ABC tune, a hand-built Standard MIDI File of a C
/// major scale, a two-bar MusicXML part (with the DOCTYPE a real file carries,
/// which is exactly why it is there), and that part again inside a real
/// <c>.mxl</c> container with its <c>META-INF/container.xml</c>.
/// </para>
/// </remarks>
public class FileImportTests
{
    /// <summary>Every sample converts to LilyPond source.</summary>
    /// <param name="sample">The file.</param>
    /// <returns>The task.</returns>
    [Theory]
    [InlineData("tune.abc")]
    [InlineData("scale.midi")]
    [InlineData("score.musicxml")]
    [InlineData("score.mxl")]
    public async Task a_real_file_converts_to_lilypond_source(string sample)
    {
        //Arrange
        string path = Sample(sample);
        ImportFormat format = ImportFormats.FormatOf(path).Value;
        ImportSettings settings = ImportSettings.For(format);

        //Act
        ImportJob job = await RunAsync(format, path, settings);

        //Assert
        job.Success.Should().BeTrue();
        job.Text.Should().NotBeNullOrEmpty();
        job.Text.Should().Contain("\\version");
        job.Text.Should().Contain("\\score");
    }

    /// <summary>
    /// A compressed container is read through the converter's own
    /// <c>--compressed</c> route, and answers what the document inside it does.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    public async Task a_compressed_container_reads_the_document_inside_it()
    {
        //Arrange
        ImportSettings settings = ImportSettings.For(ImportFormat.MusicXml);

        //Act
        ImportJob plain = await RunAsync(
            ImportFormat.MusicXml, Sample("score.musicxml"), settings);
        ImportJob zipped = await RunAsync(
            ImportFormat.MusicXml, Sample("score.mxl"), settings);

        //Assert — the only difference is the name each records for its input.
        zipped.Text.Replace("score.mxl", "score.musicxml", StringComparison.Ordinal)
            .Should().Be(plain.Text);
    }

    /// <summary>
    /// ⚠ THE OPTION REACHES THE CONVERTER: abc2ly's <c>-b</c>.
    /// </summary>
    /// <returns>The task.</returns>
    /// <remarks>Board row W-IMPORT's exit criterion asks for one option per
    /// format proved by diffing two imports; this is ABC's.</remarks>
    [Fact]
    public async Task the_abc_beaming_box_changes_the_output()
    {
        //Arrange
        AbcImportSettings kept = new AbcImportSettings { ImportBeaming = true };
        AbcImportSettings dropped = new AbcImportSettings { ImportBeaming = false };

        //Act
        ImportJob withBeams = await RunAsync(ImportFormat.Abc, Sample("tune.abc"), kept);
        ImportJob without = await RunAsync(ImportFormat.Abc, Sample("tune.abc"), dropped);

        //Assert
        withBeams.Text.Should().NotBe(without.Text);

        //`-b' keeps ABC's OWN beams, which the converter writes by turning
        //LilyPond's automatic beaming off and bracketing what ABC bracketed.
        withBeams.Text.Should().Contain("\\autoBeamOff");
        without.Text.Should().NotContain("\\autoBeamOff");
    }

    /// <summary>
    /// ⚠ THE OPTION REACHES THE CONVERTER: midi2ly's <c>-a</c>.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    public async Task the_midi_absolute_pitch_box_changes_the_output()
    {
        //Arrange
        MidiImportSettings relative = new MidiImportSettings { AbsoluteMode = false };
        MidiImportSettings absolute = new MidiImportSettings { AbsoluteMode = true };

        //Act
        ImportJob asRelative = await RunAsync(
            ImportFormat.Midi, Sample("scale.midi"), relative);
        ImportJob asAbsolute = await RunAsync(
            ImportFormat.Midi, Sample("scale.midi"), absolute);

        //Assert
        asRelative.Text.Should().NotBe(asAbsolute.Text);
        asRelative.Text.Should().Contain("\\relative");
        asAbsolute.Text.Should().NotContain("\\relative");
    }

    /// <summary>
    /// ⚠ THE OPTION REACHES THE CONVERTER: musicxml2ly's <c>--language=</c>.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    public async Task the_musicxml_language_box_changes_the_output()
    {
        //Arrange
        MusicXmlImportSettings byDefault = new MusicXmlImportSettings();
        MusicXmlImportSettings english = new MusicXmlImportSettings { Language = "english" };

        //Act
        ImportJob plain = await RunAsync(
            ImportFormat.MusicXml, Sample("score.musicxml"), byDefault);
        ImportJob named = await RunAsync(
            ImportFormat.MusicXml, Sample("score.musicxml"), english);

        //Assert — the sample's one sharpened note is `fis' in the converter's
        //own default language and `fs' in English.
        plain.Text.Should().NotBe(named.Text);
        plain.Text.Should().NotContain("\\language");
        named.Text.Should().Contain("\\language \"english\"");
    }

    /// <summary>
    /// The five inverted MusicXML boxes reach the converter with their senses
    /// the right way round.
    /// </summary>
    /// <returns>The task.</returns>
    /// <remarks>The sample carries a beam and a MIDI-block-worthy score, which
    /// is what makes the two easiest of the five visible in the output.</remarks>
    [Fact]
    public async Task the_inverted_musicxml_boxes_reach_the_converter()
    {
        //Arrange
        MusicXmlImportSettings none = new MusicXmlImportSettings();
        MusicXmlImportSettings all = new MusicXmlImportSettings
        {
            ImportArticulationDirections = true,
            ImportRestPositions = true,
            ImportPageLayout = true,
            ImportBeaming = true,
            CommentOutMidi = true,
        };

        //Act
        ImportJob withoutBoxes = await RunAsync(
            ImportFormat.MusicXml, Sample("score.musicxml"), none);
        ImportJob withBoxes = await RunAsync(
            ImportFormat.MusicXml, Sample("score.musicxml"), all);

        //Assert — "Import beaming" CLEAR is what passes `--no-beaming', which
        //leaves LilyPond's own beaming on and drops the document's brackets;
        //TICKED converts the beaming, which is written by switching automatic
        //beaming off.
        withBoxes.Text.Should().Contain("autoBeaming = ##f");
        withoutBoxes.Text.Should().NotContain("autoBeaming = ##f");

        //...and "Comment out midi block" CLEAR is what passes `-m'.
        withoutBoxes.Text.Should().Contain("\\midi");
        withBoxes.Text.Should().Contain("uncomment");
    }

    /// <summary>
    /// A file the converter only partly understood still opens, with its
    /// warnings, the way it does in Frescobaldi.
    /// </summary>
    /// <returns>The task.</returns>
    /// <remarks>
    /// ⚠ <c>abc2ly</c>'s <c>error()</c> writes its message and RETURNS unless
    /// <c>--strict</c> is given, and Frescobaldi's dialog never gives it — so
    /// the script exits zero, upstream's job succeeds, and the document opens
    /// with a hole in it and the reason in the log. The importer answers
    /// <c>Succeeded == false</c> here because it counts errors rather than
    /// reporting an exit code, so the job asks whether TEXT came out.
    /// </remarks>
    [Fact]
    public async Task a_file_the_converter_only_partly_understood_still_opens()
    {
        //Arrange — `??' is not ABC, and nothing else in the tune is wrong.
        string path = Temporary("huh.abc", "X:1\nT:Huh\nK:C\nC D ?? E|\n");

        //Act
        ImportJob job = await RunAsync(ImportFormat.Abc, path, new AbcImportSettings());

        //Assert
        job.Result.Succeeded.Should().BeFalse();
        job.Result.Errors.Should().Be(1);
        job.Text.Should().NotBeNullOrEmpty();
        job.Success.Should().BeTrue();
        job.History(MessageType.StdErr).Should().NotBeEmpty();
    }

    /// <summary>The converter's remarks arrive on the job's error channel.</summary>
    /// <returns>The task.</returns>
    /// <remarks>Upstream shows them in a job dialog it then throws away; here
    /// they are the job's <c>StdErr</c>, which is what the log panel
    /// reads.</remarks>
    [Fact]
    public async Task the_converters_remarks_reach_the_job()
    {
        //Arrange — an ABC file with a token the converter does not understand.
        string path = Temporary("remarks.abc", "X:1\nT:Broken\nK:C\nC D ?? E|\n");
        AbcImportSettings settings = new AbcImportSettings();

        //Act
        ImportJob job = await RunAsync(ImportFormat.Abc, path, settings);

        //Assert
        IReadOnlyList<JobMessage> errors = job.History(MessageType.StdErr);
        errors.Count.Should().Be(job.Result.Messages.Count);
        for (int index = 0; index < errors.Count; index++)
        {
            errors[index].Text.Should()
                .Be(job.Result.Messages[index].TrimEnd('\n') + "\n");
        }
    }

    /// <summary>
    /// A MusicXML document is read in the encoding it declares, not in UTF-8.
    /// </summary>
    [Fact]
    public void a_declared_encoding_is_honoured()
    {
        //Arrange — the composer's name has an accented character, and the file
        //says it is Latin-1.
        string document =
            "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n<score>Fauré</score>\n";
        string path = System.IO.Path.Combine(
            Directory.CreateTempSubdirectory("frescoxml").FullName, "latin.xml");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(document));

        //Act
        string text = ImportJob.ReadXmlText(path);

        //Assert
        text.Should().Contain("Fauré");
    }

    /// <summary>A file with a byte-order mark is read through it.</summary>
    [Fact]
    public void a_byte_order_mark_is_honoured()
    {
        //Arrange
        string path = System.IO.Path.Combine(
            Directory.CreateTempSubdirectory("frescobom").FullName, "bom.abc");
        File.WriteAllText(path, "X:1\nT:Fauré\nK:C\nC|\n", new UTF8Encoding(true));

        //Act
        string text = ImportJob.ReadPlainText(path);

        //Assert
        text.Should().StartWith("X:1");
        text.Should().Contain("Fauré");
    }

    /// <summary>
    /// A plain-text file that is not valid UTF-8 is read as Latin-1 rather than
    /// refused.
    /// </summary>
    [Fact]
    public void a_file_that_is_not_utf8_is_still_read()
    {
        //Arrange
        string path = System.IO.Path.Combine(
            Directory.CreateTempSubdirectory("frescolatin").FullName, "latin.abc");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes("X:1\nT:Fauré\nK:C\nC|\n"));

        //Act
        string text = ImportJob.ReadPlainText(path);

        //Assert
        text.Should().Contain("Fauré");
    }

    /// <summary>An unreadable file fails the job rather than throwing at the caller.</summary>
    /// <returns>The task.</returns>
    [Fact]
    public async Task a_missing_file_fails_the_job()
    {
        //Arrange
        string path = System.IO.Path.Combine(
            Directory.CreateTempSubdirectory("frescogone").FullName, "gone.abc");

        //Act
        ImportJob job = new ImportJob(
            ImportFormat.Abc, path, new AbcImportSettings().ToOptions("gone.abc"));
        await job.StartAsync();

        //Assert
        job.Success.Should().BeFalse();
        job.Error.Should().BeOfType<FileNotFoundException>();
        job.History(MessageType.Failure).Should().NotBeEmpty();
    }

    /// <summary>The job's title names the converter and the file.</summary>
    [Fact]
    public void the_job_title_names_the_converter_and_the_file()
    {
        //Act
        ImportJob job = new ImportJob(
            ImportFormat.Midi, "/tmp/song.midi",
            new MidiImportSettings().ToOptions("song.midi"));

        //Assert
        job.Title.Should().Contain("midi2ly");
        job.Title.Should().Contain("song.midi");
    }

    /// <summary>
    /// The name an import is written under steps past a file already on disk.
    /// </summary>
    [Fact]
    public void the_target_name_steps_past_what_is_there()
    {
        //Arrange
        string directory = Directory.CreateTempSubdirectory("frescotarget").FullName;
        string source = System.IO.Path.Combine(directory, "song.xml");
        File.WriteAllText(source, "<score/>");
        File.WriteAllText(System.IO.Path.Combine(directory, "song.ly"), "%");
        File.WriteAllText(System.IO.Path.Combine(directory, "song-1.ly"), "%");

        //Act
        PathUtil.SplitExtension(source, out string root);
        string target = root + ".ly";
        while (File.Exists(target)) { target = PathUtil.NextFile(target); }

        //Assert
        System.IO.Path.GetFileName(target).Should().Be("song-2.ly");
    }

    /// <summary>Which importer reads which sample.</summary>
    /// <param name="sample">The file.</param>
    /// <param name="expected">The importer.</param>
    [Theory]
    [InlineData("tune.abc", ImportFormat.Abc)]
    [InlineData("scale.midi", ImportFormat.Midi)]
    [InlineData("score.musicxml", ImportFormat.MusicXml)]
    [InlineData("score.mxl", ImportFormat.MusicXml)]
    public void the_suffix_picks_the_importer(string sample, ImportFormat expected)
    {
        //Act
        ImportFormat? format = ImportFormats.FormatOf(Sample(sample));

        //Assert
        format.Should().Be(expected);
    }

    /// <summary>Only the <c>.mxl</c> suffix takes the compressed route.</summary>
    [Fact]
    public void only_mxl_is_a_compressed_container()
    {
        //Act & Assert
        ImportFormats.IsCompressedMusicXml("score.mxl").Should().BeTrue();
        ImportFormats.IsCompressedMusicXml("score.xml").Should().BeFalse();
        ImportFormats.IsCompressedMusicXml("score.musicxml").Should().BeFalse();
    }

    /// <summary>The four commands are the four upstream has, under its names.</summary>
    [Fact]
    public void the_commands_are_upstreams()
    {
        //Arrange
        Commands.FileImportActions actions = new Commands.FileImportActions();

        //Act
        IReadOnlyList<string> names = actions.Actions.Keys.ToList();

        //Assert
        names.Should().BeEquivalentTo(new[]
        {
            "import_any", "import_musicxml", "import_midi", "import_abc",
        });
        actions.Name.Should().Be("file_import");
        actions.ImportAny.Text.Should().Be("Import...");
        actions.ImportMusicXml.Text.Should().Be("Import MusicXML...");
        actions.ImportMidi.Text.Should().Be("Import Midi...");
        actions.ImportAbc.Text.Should().Be("Import abc...");
    }

    private static string Sample(string name)
        => System.IO.Path.Combine(
            AppContext.BaseDirectory, "fixtures", "import", "samples", name);

    private static string Temporary(string name, string content)
    {
        string path = System.IO.Path.Combine(
            Directory.CreateTempSubdirectory("frescoimport").FullName, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static async Task<ImportJob> RunAsync(
        ImportFormat format, string path, ImportSettings settings)
    {
        ImportJob job = new ImportJob(
            format, path, settings.ToOptions(System.IO.Path.GetFileName(path)));
        await job.StartAsync();
        return job;
    }
}
