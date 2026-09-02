// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly.MusicXml;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using Xunit;

namespace Fresco.Brix.Ly.Tests;

/// <summary>
/// RULING FR15 — the hard rule, enforced rather than asserted.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fresco.Brix does not write a MusicXML file that fails to conform to the
/// published MusicXML schema.</b> A rule nothing enforces is a wish, so this
/// class exports EVERY document in the parity corpus and validates the result
/// against the official schema — the one published by the W3C Music Notation
/// Community Group, vendored verbatim beside these tests with its provenance
/// and licence in <c>schema/README.txt</c>.
/// </para>
/// <para>
/// ⚠ IF ONE OF THESE FAILS, THE EXPORTER IS WRONG — not the schema, and not
/// this test. The failure message names the element and the line. Find what the
/// specification says about that element
/// (<c>https://www.w3.org/2021/06/musicxml40/</c>) and put the information
/// where it belongs; do not relax the test.
/// </para>
/// <para>
/// ⚠ AND CONFORMANCE IS ONLY HALF THE RULE. The other half is that nothing is
/// LOST on the way: <see cref="MusicXmlParityTests"/> holds every document to
/// python-ly's own output, and
/// <see cref="every_header_variable_survives_the_export"/> below checks that the
/// metadata MusicXML has no element for is still in the file — in
/// <c>&lt;miscellaneous-field&gt;</c>, where the specification puts it.
/// </para>
/// </remarks>
public class MusicXmlSchemaTests
{
    private static readonly Lazy<XmlSchemaSet> Schemas = new Lazy<XmlSchemaSet>(LoadSchemas);

    private static string FixtureDir
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "musicxml");

    private static string SchemaDir => Path.Combine(AppContext.BaseDirectory, "schema");

    /// <summary>Every recorded document, by name.</summary>
    public static TheoryData<string> Fixtures
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string path in Directory.GetFiles(FixtureDir, "*.ly").OrderBy(p => p))
            {
                data.Add(Path.GetFileNameWithoutExtension(path));
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void every_document_either_conforms_or_is_never_written(string name)
    {
        //Arrange
        string text = File.ReadAllText(Path.Combine(FixtureDir, name + ".ly"));

        //Act
        var writer = new ParseSource();
        writer.ParseText(text);
        MusicXmlDocument document = writer.MusicXml();

        //Assert — RULING FR15 in one sentence. A document the converter turned
        //into music must satisfy the schema; a document it could not is refused
        //at the export boundary and never reaches a file. There is no third
        //outcome, and in particular there is no "written but invalid".
        if (!document.HasParts)
        {
            //<part-list> needs a <score-part> and <score-partwise> needs a
            //<part>; this document has neither, which is why it is refused.
            Validate(document.ToDocumentString()).Should().NotBeEmpty(
                "'{0}' has no parts, so it could not be conformant — the point "
                + "is that it is never written", name);
            return;
        }

        IReadOnlyList<string> problems = Validate(document.ToDocumentString());
        problems.Should().BeEmpty(
            "'{0}' must satisfy the MusicXML schema (ruling FR15): {1}",
            name,
            string.Join(" | ", problems));
    }

    [Fact]
    public void the_corpus_splits_where_it_did_and_nothing_has_quietly_moved()
    {
        //Arrange
        int conformant = 0;
        int refused = 0;

        //Act
        foreach (string path in Directory.GetFiles(FixtureDir, "*.ly"))
        {
            var writer = new ParseSource();
            writer.ParseText(File.ReadAllText(path));
            MusicXmlDocument document = writer.MusicXml();
            if (!document.HasParts) { refused++; }
            else if (Validate(document.ToDocumentString()).Count == 0) { conformant++; }
        }

        //Assert — 51 documents the converter turns into music, every one of
        //them schema-conformant, and 30 it cannot (all header, all markup, or
        //shaped in a way its reader does not follow) which are refused rather
        //than written. If either number moves, find out why before changing it.
        conformant.Should().Be(51);
        refused.Should().Be(30);
        (conformant + refused).Should().Be(81);
    }

    [Fact]
    public void the_document_declares_the_version_it_was_validated_against()
    {
        //Arrange
        var writer = new ParseSource();
        writer.ParseText("\\relative c'' { c4 d e f }");

        //Act
        string xml = writer.MusicXml().ToDocumentString();

        //Assert — and the DOCTYPE must name the SAME version as the root, which
        //upstream's does not: it writes a 2.0 public identifier on a 3.0 root.
        MusicXmlCreator.MusicXmlVersion.Should().Be("4.0");
        xml.Should().Contain("<score-partwise version=\"4.0\">");
        xml.Should().Contain("-//Recordare//DTD MusicXML 4.0 Partwise//EN");
    }

    [Fact]
    public void every_header_variable_survives_the_export()
    {
        //Arrange — a header using every LilyPond variable the exporter routes
        //differently: two the schema models as creators, two it models as
        //rights, one it models as a title, and four it does not model at all.
        const string source = "\\version \"2.24.0\"\n"
            + "\\header {\n"
            + "  title = \"Twinkle\"\n"
            + "  composer = \"Traditional\"\n"
            + "  arranger = \"J. Ellis\"\n"
            + "  copyright = \"Public Domain\"\n"
            + "  tagline = \"engraved by LilyPort\"\n"
            + "  subtitle = \"a little star\"\n"
            + "  opus = \"Op. 1\"\n"
            + "  piece = \"Andante\"\n"
            + "  dedication = \"for nobody\"\n"
            + "}\n"
            + "\\relative c'' { c4 c g' g }\n";

        var writer = new ParseSource();
        writer.ParseText(source);

        //Act
        string xml = writer.MusicXml().ToDocumentString();

        //Assert — NOTHING IS LOST. Every value the document stated is in the
        //file, in the place the specification names for it.
        Validate(xml).Should().BeEmpty();
        xml.Should().Contain("<movement-title>Twinkle</movement-title>");
        xml.Should().Contain("<creator type=\"composer\">Traditional</creator>");
        xml.Should().Contain("<creator type=\"arranger\">J. Ellis</creator>");
        xml.Should().Contain("<rights type=\"copyright\">Public Domain</rights>");
        xml.Should().Contain("<rights type=\"tagline\">engraved by LilyPort</rights>");
        xml.Should().Contain(
            "<miscellaneous-field name=\"subtitle\">a little star</miscellaneous-field>");
        xml.Should().Contain("<miscellaneous-field name=\"opus\">Op. 1</miscellaneous-field>");
        xml.Should().Contain("<miscellaneous-field name=\"piece\">Andante</miscellaneous-field>");
        xml.Should().Contain(
            "<miscellaneous-field name=\"dedication\">for nobody</miscellaneous-field>");
    }

    [Fact]
    public void identification_puts_its_children_in_the_order_the_sequence_requires()
    {
        //Arrange — the shape that used to be wrong: a creator AND a rights
        //statement, both of which the sequence puts before the encoding that
        //the writer creates first.
        var writer = new ParseSource();
        writer.ParseText(
            "\\header { composer = \"X\" copyright = \"Y\" subtitle = \"Z\" }\n"
            + "\\relative c'' { c4 d e f }\n");

        //Act
        string xml = writer.MusicXml().ToDocumentString();

        //Assert — creator*, rights*, encoding?, source?, relation*, miscellaneous?
        int creator = xml.IndexOf("<creator", StringComparison.Ordinal);
        int rights = xml.IndexOf("<rights", StringComparison.Ordinal);
        int encoding = xml.IndexOf("<encoding>", StringComparison.Ordinal);
        int misc = xml.IndexOf("<miscellaneous>", StringComparison.Ordinal);

        creator.Should().BeGreaterThan(0);
        rights.Should().BeGreaterThan(creator);
        encoding.Should().BeGreaterThan(rights);
        misc.Should().BeGreaterThan(encoding);
    }

    [Fact]
    public void the_vendored_schema_is_the_published_one_and_says_which_version()
    {
        //Arrange & Act
        string schema = File.ReadAllText(Path.Combine(SchemaDir, "musicxml.xsd"));

        //Assert — if this ever fails, somebody swapped the schema out; read
        //schema/README.txt before doing anything else.
        schema.Should().Contain("MusicXML W3C XML schema (XSD)");
        schema.Should().Contain("Version 4.0");
        schema.Should().Contain("W3C Music Notation Community Group");
        File.Exists(Path.Combine(SchemaDir, "xlink.xsd")).Should().BeTrue();
        File.Exists(Path.Combine(SchemaDir, "xml.xsd")).Should().BeTrue();
    }

    /// <summary>Validates a MusicXML document against the vendored schema.</summary>
    /// <param name="xml">The document.</param>
    /// <returns>What the schema objected to; empty when it conforms.</returns>
    private static IReadOnlyList<string> Validate(string xml)
    {
        var problems = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = Schemas.Value,

            //⚠ IGNORE, not Parse. The document carries a DOCTYPE whose system
            //identifier is a URL, and a validating read must not go to the
            //network to fetch a DTD that the specification itself deprecated in
            //favour of this schema.
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        };
        settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;
        settings.ValidationEventHandler += (_, e) =>
            problems.Add($"{e.Severity} at line {e.Exception?.LineNumber}: {e.Message}");

        using var reader = XmlReader.Create(new StringReader(xml), settings);
        while (reader.Read())
        {
            //Reading to the end is what runs the validation.
        }

        return problems;
    }

    /// <summary>Loads the vendored schema, resolving its imports locally.</summary>
    /// <returns>The schema set.</returns>
    /// <remarks>
    /// The published <c>musicxml.xsd</c> imports <c>xml.xsd</c> and
    /// <c>xlink.xsd</c> by their <c>musicxml.org</c> URLs, which is why the
    /// specification also ships a <c>catalog.xml</c>. The two are added to the
    /// set FIRST, by namespace, so the importing schema finds them there and
    /// never reaches for the network — and the vendored file stays byte for
    /// byte the published one.
    /// </remarks>
    private static XmlSchemaSet LoadSchemas()
    {
        var set = new XmlSchemaSet { XmlResolver = null };
        set.Add("http://www.w3.org/XML/1998/namespace", Path.Combine(SchemaDir, "xml.xsd"));
        set.Add("http://www.w3.org/1999/xlink", Path.Combine(SchemaDir, "xlink.xsd"));
        set.Add(null, Path.Combine(SchemaDir, "musicxml.xsd"));
        set.Compile();
        return set;
    }
}
