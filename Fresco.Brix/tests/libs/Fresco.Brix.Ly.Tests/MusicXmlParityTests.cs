using Fresco.Brix.Ly;
using Fresco.Brix.Ly.MusicXml;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Fresco.Brix.Ly.Tests;

/// <summary>
/// The MusicXML exporter, replayed against python-ly's OWN output.
/// </summary>
/// <remarks>
/// <para>
/// Every fixture under <c>fixtures/musicxml</c> was recorded by
/// <c>tools/musicxmlprobe</c> running python-ly v0.9.10 over the document
/// beside it, once with the writer's defaults and once with <c>midi_out</c>.
/// The comparison is the WHOLE FILE, character for character — which is the
/// only comparison worth making for a format where element order is meaning.
/// </para>
/// <para>
/// ⚠ TWO FIELDS ARE NORMALISED, both inside <c>&lt;encoding&gt;</c> and neither
/// a fact about the music: the encoding DATE, which python-ly stamps with today
/// (upstream's own test suite rewrites it the same way), and the SOFTWARE name,
/// which is the caller's string — Frescobaldi overwrites python-ly's with its
/// own before saving. Nothing else is touched.
/// </para>
/// <para>
/// ⚠ AND THE ORACLE ANSWERS "WHAT PYTHON-LY PRODUCES WITH ITS DEMONSTRABLE
/// DEFECTS FIXED, AND ITS NON-CONFORMANCES CORRECTED" (rulings FR14 and
/// FR15). Twelve declared patches were applied to the
/// reference in memory at generation time; each fixture records the list, and
/// <see cref="every_fixture_declares_the_twelve_fixes_the_oracle_was_made_with"/>
/// asserts it, so the day python-ly fixes one of them the fixtures stop
/// matching and say why.
/// </para>
/// </remarks>
public class MusicXmlParityTests
{
    private static string FixtureDir
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "musicxml");

    /// <summary>Every recorded document, by name.</summary>
    public static TheoryData<string> Fixtures
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string path in Directory.GetFiles(FixtureDir, "*.musicxml.json").OrderBy(p => p))
            {
                data.Add(Path.GetFileName(path).Replace(".musicxml.json", string.Empty));
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void the_exporter_writes_what_python_ly_wrote(string name)
    {
        //Arrange
        string text = File.ReadAllText(Path.Combine(FixtureDir, name + ".ly"));
        using JsonDocument recorded = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FixtureDir, name + ".musicxml.json")));

        //Act & Assert — one document, both of the writer's settings.
        foreach (JsonElement run in recorded.RootElement.GetProperty("runs").EnumerateArray())
        {
            if (!run.GetProperty("answered").GetBoolean())
            {
                //A document the REFERENCE could not answer is not one the port
                //has to answer either; the fixture records why.
                continue;
            }

            string expected = run.GetProperty("xml").GetString();
            string actual = Normalize(Export(text));
            actual.Should().Be(expected, "the {0} run of {1} must match python-ly's own output",
                run.GetProperty("name").GetString(), name);
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void every_fixture_declares_the_twelve_fixes_the_oracle_was_made_with(string name)
    {
        //Arrange
        using JsonDocument recorded = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FixtureDir, name + ".musicxml.json")));

        //Act
        List<string> modules = recorded.RootElement.GetProperty("known_fixes").EnumerateArray()
            .Select(f => f.GetProperty("module").GetString()).ToList();

        //Assert — twelve declared patches to the reference, in the order the
        //tool lists them: FIVE defects under ruling FR14 (what upstream MEANT
        //to do and did not), then SEVEN under ruling FR15 (where upstream
        //writes something the MusicXML schema forbids). The second group is
        //applied to the oracle too, deliberately: with it in place the port and
        //the reference still match byte for byte, which is the evidence that
        //the conformance work moved information and lost none of it.
        modules.Should().Equal(
            "ly.music.read",
            "ly.musicxml.lymus2musxml",
            "ly.musicxml.ly2xml_mediator",
            "ly.musicxml.ly2xml_mediator",
            "ly.musicxml.xml_objs",
            "ly.musicxml.create_musicxml",
            "ly.musicxml.xml_objs",
            "ly.musicxml.create_musicxml",
            "ly.musicxml.create_musicxml",
            "ly.musicxml.create_musicxml",
            "ly.musicxml.create_musicxml",
            "ly.musicxml.create_musicxml");
        recorded.RootElement.GetProperty("reference").GetString().Should().Be("python-ly v0.9.10");
    }

    [Fact]
    public void the_corpus_is_the_size_it_was_recorded_at()
    {
        //Arrange
        string[] fixtures = Directory.GetFiles(FixtureDir, "*.musicxml.json");

        //Act
        int answered = 0;
        int runs = 0;
        foreach (string path in fixtures)
        {
            using JsonDocument recorded = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonElement run in recorded.RootElement.GetProperty("runs").EnumerateArray())
            {
                runs++;
                if (run.GetProperty("answered").GetBoolean()) { answered++; }
            }
        }

        //Assert — 17 of python-ly's own test documents, 54 ly.music fixtures and
        //10 probes written for this wave, each run twice.
        fixtures.Length.Should().Be(81);
        runs.Should().Be(162);
        answered.Should().Be(162);
    }

    /// <summary>Runs the port over a document and returns the whole file.</summary>
    /// <param name="text">The source.</param>
    /// <returns>The MusicXML.</returns>
    private static string Export(string text)
    {
        var writer = new ParseSource();
        writer.ParseText(text);
        return writer.MusicXml().ToDocumentString("utf-8");
    }

    /// <summary>Replaces the two fields that are about the run, not the music.</summary>
    /// <param name="xml">The file.</param>
    /// <returns>The file, normalised.</returns>
    private static string Normalize(string xml)
    {
        xml = System.Text.RegularExpressions.Regex.Replace(
            xml, "(?<=<encoding-date>)[^<]*(?=</encoding-date>)", "ENCODING-DATE");
        return System.Text.RegularExpressions.Regex.Replace(
            xml, "(?<=<software>)[^<]*(?=</software>)", "SOFTWARE");
    }
}

/// <summary>The element model the exporter builds its document on.</summary>
public class ETreeTests
{
    [Fact]
    public void an_element_with_nothing_in_it_closes_itself_with_a_space()
    {
        //Arrange
        var element = new ETreeElement("rest");

        //Act
        var builder = new StringBuilder();
        element.Serialize(builder);

        //Assert — python's short_empty_elements, space and all.
        builder.ToString().Should().Be("<rest />");
    }

    [Fact]
    public void attributes_come_out_in_the_order_they_were_set()
    {
        //Arrange
        var element = new ETreeElement("clef");
        element.Set("number", "2");
        element.Set("size", "small");
        element.Set("number", "3");

        //Act
        var builder = new StringBuilder();
        element.Serialize(builder);

        //Assert — setting an existing attribute keeps its position.
        builder.ToString().Should().Be("<clef number=\"3\" size=\"small\" />");
    }

    [Fact]
    public void the_tail_is_written_outside_the_element()
    {
        //Arrange
        var parent = new ETreeElement("note");
        ETreeElement child = parent.SubElement("pitch");
        child.Text = "C";
        child.Tail = "\n  ";

        //Act
        var builder = new StringBuilder();
        parent.Serialize(builder);

        //Assert
        builder.ToString().Should().Be("<note><pitch>C</pitch>\n  </note>");
    }

    [Theory]
    [InlineData("a & b", "a &amp; b")]
    [InlineData("a < b", "a &lt; b")]
    [InlineData("a > b", "a &gt; b")]
    [InlineData("&amp;", "&amp;amp;")]
    public void text_is_escaped_the_way_python_escapes_it(string text, string expected)
    {
        //Assert
        ETreeElement.EscapeText(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("a \" b", "a &quot; b")]
    [InlineData("a\tb", "a&#09;b")]
    [InlineData("a\nb", "a&#10;b")]
    [InlineData("a\rb", "a&#13;b")]
    public void attribute_values_escape_the_three_whitespace_characters_as_numbers(
        string text, string expected)
    {
        //Assert — and the tab as TWO digits, which is the literal python writes.
        ETreeElement.EscapeAttribute(text).Should().Be(expected);
    }

    [Fact]
    public void indenting_leaves_text_that_is_already_there_alone()
    {
        //Arrange
        var root = new ETreeElement("part");
        ETreeElement measure = root.SubElement("measure");
        ETreeElement words = measure.SubElement("words");
        words.Text = "Allegro";

        //Act
        ETreeUtil.Indent(root);

        //Assert
        words.Text.Should().Be("Allegro");
        root.Text.Should().Be("\n  ");
        measure.Text.Should().Be("\n    ");
    }
}

/// <summary>The two tables the exporter looks words up in.</summary>
public class MusicXmlTranslationTests
{
    [Theory]
    [InlineData("4", "quarter")]
    [InlineData("1", "whole")]
    [InlineData("16", "16th")]
    [InlineData("\\breve", "breve")]
    [InlineData("2048", "2048th")]
    [InlineData("nonsense", "quarter")]
    public void a_duration_value_becomes_the_type_musicxml_calls_it(string value, string expected)
    {
        //Assert — a value the list does not know falls to quarter, which is
        //upstream's own except-ValueError arm.
        Ly2XmlTranslations.DurationValueToType(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("c", "major", 0)]
    [InlineData("g", "major", 1)]
    [InlineData("bes", "major", -2)]
    [InlineData("a", "minor", 0)]
    [InlineData("d", "dorian", 0)]
    public void a_key_becomes_its_number_of_fifths(string key, string mode, int expected)
    {
        //Assert
        Ly2XmlTranslations.GetFifths(key, mode).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(8, "H")]
    [InlineData(9, "J")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    public void a_rehearsal_mark_number_becomes_letters_with_no_i(int n, string expected)
    {
        //Assert — engraving convention leaves I out, because it reads as a bar
        //line; upstream's bijective() does the same.
        Ly2XmlTranslations.Bijective(n).Should().Be(expected);
    }

    [Theory]
    [InlineData("treble", "G", 2, 0)]
    [InlineData("bass_8", "F", 4, -1)]
    [InlineData("percussion", "percussion", 0, 0)]
    [InlineData("tab", "TAB", 5, 0)]
    public void a_clef_name_becomes_its_musicxml_definition(
        string name, string sign, int line, int octaveChange)
    {
        //Act
        ClefSignature clef = Ly2XmlTranslations.ClefNameToClef(name);

        //Assert
        clef.Sign.Should().Be(sign);
        clef.Line.Should().Be(line);
        clef.OctaveChange.Should().Be(octaveChange);
    }

    [Fact]
    public void a_clef_name_nothing_knows_answers_nothing()
    {
        //Assert
        Ly2XmlTranslations.ClefNameToClef("no-such-clef").Should().BeNull();
    }

    [Theory]
    [InlineData(".", "staccato")]
    [InlineData(">", "accent")]
    [InlineData("\\trill", "ornament")]
    [InlineData("\\fermata", "other")]
    public void an_articulation_token_is_sorted_into_its_group(string token, string expected)
    {
        //Assert
        Ly2XmlTranslations.ArticulationTokenToXmlName(token).Should().Be(expected);
    }

    [Fact]
    public void the_midi_sound_map_has_all_hundred_and_twenty_eight_general_midi_names()
    {
        //Assert — including the ones deliberately left unanswered.
        MidiSoundMap.Sounds.Count.Should().Be(128);
        MidiSoundMap.Sounds["acoustic grand"].Should().Be("keyboard.piano.grand");
        MidiSoundMap.Sounds["distorted guitar"].Should().BeNull();
    }
}
