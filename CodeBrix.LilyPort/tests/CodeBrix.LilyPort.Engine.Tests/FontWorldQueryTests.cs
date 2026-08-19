// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.IO;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// Ruling R18 — <c>ly:font-config-get-font-file</c> and
/// <c>ly:font-config-display-fonts</c> answer for the PORT'S OWN font world.
/// <para>
/// ⚠ THE ORACLE CANNOT BE READ FOR THESE, and that is not an oversight: the whole point
/// of the ruling is that the port answers something upstream does not. Upstream reports
/// the HOST's fontconfig view; the port has no host font world and D23 forbids acquiring
/// one. So the authorities here are the port's OWN asset manifest — which faces exist,
/// and under which resource names — and R16's document-font registry, and every case is
/// paired with a control that must come out DIFFERENTLY (rule 33). The <c>#f</c> arm is
/// the most important control of the three: without it, an implementation that answered
/// the same thing for every name would pass everything else.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class FontWorldQueryTests
{
    [Fact]
    public void a_vendored_family_answers_the_assembly_and_resource_its_bytes_come_from()
    {
        //Arrange
        // READ OFF THE ASSET MANIFEST, not off the implementation: the face is an
        // EmbeddedResource whose logical name the csproj fixes as
        // `CodeBrix.LilyPort.Engine.Fonts.text.<file>', so the answer must name that
        // resource and the assembly carrying it.
        const string Expected
            = "CodeBrix.LilyPort.Engine.dll/CodeBrix.LilyPort.Engine.Fonts.text.C059-Roman.otf";

        //Act
        string located = TextFontChain.VendoredFaceLocation("C059");

        //Assert
        located.Should().Be(Expected);

        // THE RESOURCE REALLY IS THERE — the answer is not a string the port assembled
        // out of a naming rule that no longer matches what it ships.
        FontAssets.TextFont("C059-Roman.otf").Should().NotBeNull();

        // THE CONTROL, and it carries the ruling's third arm: a family no vendored face
        // declares has no location at all. "serif" is deliberate — it is a request the
        // CHAIN answers (R14 walks it to C059), so an implementation that confused a
        // family name with a chain key would answer here and must not.
        TextFontChain.VendoredFaceLocation("Helvetica").Should().BeNull();
        TextFontChain.VendoredFaceLocation("serif").Should().BeNull();
    }

    [Fact]
    public void a_family_answers_its_regular_face_not_whichever_style_comes_first()
    {
        //Arrange
        // Each vendored family ships four styles. Upstream's fontconfig best-match on a
        // bare family name answers the regular one, so this does too — and the three
        // collections spell "regular" three different ways (C059 "Roman", URW "Regular",
        // TeX Gyre lower-case), which is exactly why the answer comes from the family
        // table's own style ORDER rather than from reading the file name.

        //Act
        string schola = TextFontChain.VendoredFaceLocation("TeX Gyre Schola");
        string sans = TextFontChain.VendoredFaceLocation("Nimbus Sans");
        string mono = TextFontChain.VendoredFaceLocation("Nimbus Mono PS");

        //Assert
        schola.Should().EndWith("texgyreschola-regular.otf");
        sans.Should().EndWith("NimbusSans-Regular.otf");
        mono.Should().EndWith("NimbusMonoPS-Regular.otf");

        // THE CONTROL: the bold face exists and is a DIFFERENT resource, so "it answered
        // regular" is a real claim and not the only string available.
        FontAssets.TextFontLocation("texgyreschola-bold.otf")
            .Should().NotBe(schola);
    }

    [Fact]
    public void the_listing_covers_every_vendored_face_exactly_once()
    {
        //Arrange
        // D23's count: 24 text faces, six families of four. Asserted against the ASSET
        // MANIFEST rather than against a literal, so a face added or dropped moves both
        // sides together and this stays a statement about coverage.
        List<string> shipped = new List<string>(FontAssets.TextFontNames());

        //Act
        IReadOnlyList<TextFace> listed = TextFontChain.VendoredFaces();

        //Assert
        listed.Count.Should().Be(shipped.Count);

        HashSet<string> listedFiles = new HashSet<string>();
        foreach (TextFace face in listed)
        {
            listedFiles.Add(face.FileName).Should().BeTrue();
        }

        foreach (string file in shipped)
        {
            listedFiles.Should().Contain(file);
        }

        // THE CONTROL: TeX Gyre Schola is in the family table TWICE — once as serif's
        // second level and once as R14's unknown-family answer — so a listing that simply
        // walked the table would report 28 and repeat four faces.
        listed.Count.Should().Be(24);
    }

    [Fact]
    public void a_document_supplied_family_answers_its_real_path_on_disk()
    {
        //Arrange
        // R16's registry is the other arm, and it is the one that CAN name a file,
        // because the document handed the port a path.
        string path = WriteScratchFace("C059-Roman.otf", "r18-doc-font");
        try
        {
            // THE CONTROL, MEASURED FIRST: before the registration "C059" answers the
            // VENDORED arm, so the case below is a change and not a coincidence.
            TextFontChain.VendoredFaceLocation("C059")
                .Should().Be(
                    "CodeBrix.LilyPort.Engine.dll/"
                    + "CodeBrix.LilyPort.Engine.Fonts.text.C059-Roman.otf");

            //Act
            TextFontChain.AddDocumentFont(path).Should().BeTrue();
            TextFace supplied = TextFontChain.DocumentFont("C059");

            //Assert
            supplied.Should().NotBeNull();
            supplied.SourcePath.Should().Be(Path.GetFullPath(path));

            IReadOnlyList<KeyValuePair<string, TextFace>> registrations
                = TextFontChain.DocumentFontRegistrations();
            registrations.Count.Should().Be(1);
            registrations[0].Key.Should().Be("C059");
            registrations[0].Value.SourcePath.Should().Be(Path.GetFullPath(path));

            // THE CONTROL: a VENDORED face has no path, because it has no file. An
            // implementation that invented one — the assembly's own location, say —
            // would answer something here and would be lying about where the bytes are.
            TextFace vendored = TextFace.Load("C059-Roman.otf");
            vendored.Should().NotBeNull();
            vendored.SourcePath.Should().BeNull();
        }
        finally
        {
            TextFontChain.ResetDocumentFonts();
            File.Delete(path);
        }
    }

    [Fact]
    public void the_two_entry_points_answer_from_scheme()
    {
        //Arrange
        // ⚠ Both of these THREW until R18 was built ("not applicable: ... N/A per D25"),
        // so this is also the fence that they came off the N/A list and stayed off.
        string path = WriteScratchFace("NimbusSans-Regular.otf", "r18-scheme");
        Interpreter ambientBefore = LilyPondScheme.Current;
        try
        {
            Interpreter.RunWithLargeStack(() =>
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();

                //Act
                object vendored = interpreter.EvalString(
                    "(ly:font-config-get-font-file \"C059\")", "<r18>");
                object unknown = interpreter.EvalString(
                    "(ly:font-config-get-font-file \"Helvetica\")", "<r18>");

                //Assert
                SchemeUtilities.StringText(vendored).Should().Be(
                    "CodeBrix.LilyPort.Engine.dll/"
                    + "CodeBrix.LilyPort.Engine.Fonts.text.C059-Roman.otf");

                // THE #f ARM, from Scheme, where it matters: a caller tests the answer
                // for truth, and a port that answered the empty string would read as TRUE
                // in Scheme and send that caller looking for a file called "".
                unknown.Should().Be(false);

                // The DOCUMENT arm through the whole Scheme surface: register with the
                // primitive R16 built, then ask with the one R18 built. The path is
                // spliced into Scheme SOURCE, so it goes through Printer.WriteString --
                // a raw splice dies on \U (C:\Users) and silently misreads \t and \a.
                //was previously: "(ly:font-config-add-font \"" + path.Replace("\\", "/") + "\")"
                interpreter.EvalString(
                    "(ly:font-config-add-font " + Printer.WriteString(path) + ")", "<r18>");
                object supplied = interpreter.EvalString(
                    "(ly:font-config-get-font-file \"Nimbus Sans\")", "<r18>");
                SchemeUtilities.StringText(supplied).Should().Be(Path.GetFullPath(path));

                // display-fonts writes to the port it is given, and names both halves of
                // the world. The CONTROL is the count line: a listing that forgot the
                // document's own font would say (0).
                string listing = WriteListing(interpreter);
                listing.Should().Contain("vendored faces (24):");
                listing.Should().Contain("document-supplied fonts (1):");
                listing.Should().Contain(Path.GetFullPath(path));
                listing.Should().Contain(
                    "CodeBrix.LilyPort.Engine.Fonts.text.texgyreschola-regular.otf");
            });
        }
        finally
        {
            LilyPondScheme.RestoreAmbient(ambientBefore);
            TextFontChain.ResetDocumentFonts();
            File.Delete(path);
        }
    }

    private static string WriteListing(Interpreter interpreter)
    {
        StringWriter sink = new StringWriter();
        System.IO.TextWriter saved = interpreter.ErrorWriter;
        interpreter.ErrorWriter = sink;
        try
        {
            interpreter.EvalString("(ly:font-config-display-fonts)", "<r18>");
        }
        finally
        {
            interpreter.ErrorWriter = saved;
        }

        return sink.ToString();
    }

    private static string WriteScratchFace(string vendored, string prefix)
    {
        byte[] bytes = FontAssets.TextFont(vendored);
        bytes.Should().NotBeNull();

        string path = Path.Combine(
            Path.GetTempPath(), prefix + "-" + Path.GetRandomFileName() + ".otf");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
