// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.IO;
using CodeBrix.LilyPort.Engine.Fonts;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// Ruling R16 — a DOCUMENT may supply its own fonts, and the port must render such a
/// document the way LilyPond does.
/// <para>
/// This is not the system-font fallback D23 forbids, and the distinction is the whole
/// ruling: upstream implements <c>ly:font-config-add-font</c> and
/// <c>ly:font-config-add-directory</c> as fontconfig APPLICATION fonts
/// (<c>all-font-metrics.cc:306,319</c>), the same set LilyPond's own bundled faces go
/// into, and <c>font-config.cc</c> builds that source separately from the system
/// directories. A document that carries its font files beside it depends on the host for
/// nothing — which is the documented purpose of the feature (Notation Reference,
/// §Finding fonts).
/// </para>
/// <para>
/// EXPECTED VALUES ARE READ OFF THE FACES THEMSELVES (rule 35a), which is where a family
/// name lives and what fontconfig indexes a file by. The end-to-end fence is the corpus
/// row <c>font-name-add-files.ly</c>, which is where the two dummy logo faces are.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class DocumentFontTests
{
    [Fact]
    public void a_face_answers_the_family_name_its_own_name_table_declares()
    {
        //Arrange
        // READ OFF THE FONT FILES with an independent dump of their `name' tables:
        // C059-Roman carries name ID 1 "C059" on both platforms and no ID 16 at all,
        // while texgyreschola-regular carries ID 16 "TeX Gyre Schola" AND an ID 1 that
        // says "TeXGyreSchola" without spaces on the Windows platform.
        TextFace c059 = TextFace.Load("C059-Roman.otf");
        TextFace schola = TextFace.Load("texgyreschola-regular.otf");
        c059.Should().NotBeNull();
        schola.Should().NotBeNull();

        //Act
        string plain = c059.FamilyName;
        string typographic = schola.FamilyName;

        //Assert
        plain.Should().Be("C059");

        // THE CONTROL FOR THE ID-16 PREFERENCE, and the reason it is not decoration: a
        // reader that took ID 1 would answer "TeXGyreSchola" here, which is a different
        // string and would not match what a document types.
        typographic.Should().Be("TeX Gyre Schola");
    }

    [Fact]
    public void a_registered_family_resolves_to_the_document_s_own_face()
    {
        //Arrange
        // The face is a VENDORED one written out to a scratch path, so the test supplies
        // a font the way a document does — from a file — without carrying a second copy
        // of a typeface into the repository.
        string path = WriteScratchFace("C059-Roman.otf", "doc-font-case");
        try
        {
            // THE CONTROL, MEASURED FIRST: "C059" is not one of the three generic names,
            // so before any registration R14 sends it down the unknown arm to TeX Gyre
            // Schola. If this did not hold, the case below would prove nothing.
            IReadOnlyList<TextFace> before = TextFontChain.For("C059", false, false);
            before.Count.Should().BeGreaterThan(0);
            before[0].FileName.Should().Be("texgyreschola-regular.otf");

            //Act
            TextFontChain.AddDocumentFont(path).Should().BeTrue();
            IReadOnlyList<TextFace> after = TextFontChain.For("C059", false, false);

            //Assert
            after.Count.Should().Be(1, "a document font is the face the document named, "
                + "not the head of a fallback chain");
            after[0].FileName.Should().Be(Path.GetFileName(path));

            // A family NOBODY registered still lands where R14 puts it — the registry is
            // consulted, not substituted for the chain.
            TextFontChain.For("Nothing Like This", false, false)[0].FileName
                .Should().Be("texgyreschola-regular.otf");

            // And the generic names are untouched: D23's chain still runs.
            TextFontChain.For("serif", false, false)[0].FileName
                .Should().Be("C059-Roman.otf");
        }
        finally
        {
            TextFontChain.ResetDocumentFonts();
            File.Delete(path);
        }
    }

    [Fact]
    public void a_registration_does_not_survive_its_file()
    {
        //Arrange
        // THE LEAK FENCE (trap 16). Upstream builds one fontconfig configuration per
        // process and engraves one file per process; the port sweeps 2,146 files through
        // one process, so a registration that outlived its file would let a later file
        // resolve a family it never asked for. font-name-add-files.ly makes that
        // concrete — it DELETES its font files on the way out, so a leaked registration
        // would point at a path that is gone.
        string path = WriteScratchFace("C059-Roman.otf", "doc-font-leak");
        try
        {
            TextFontChain.AddDocumentFont(path).Should().BeTrue();
            TextFontChain.For("C059", false, false)[0].FileName
                .Should().Be(Path.GetFileName(path));

            //Act
            TextFontChain.ResetDocumentFonts();

            //Assert
            TextFontChain.For("C059", false, false)[0].FileName
                .Should().Be("texgyreschola-regular.otf");
        }
        finally
        {
            TextFontChain.ResetDocumentFonts();
            File.Delete(path);
        }
    }

    [Fact]
    public void a_directory_registers_every_face_in_it()
    {
        //Arrange
        // ly:font-config-add-directory's own case. Two faces, so "it registered one and
        // stopped" cannot pass.
        string directory = Path.Combine(
            Path.GetTempPath(), "codebrix-lilyport-doc-fonts-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(
                Path.Combine(directory, "one.otf"), FontAssets.TextFont("C059-Roman.otf"));
            File.WriteAllBytes(
                Path.Combine(directory, "two.otf"),
                FontAssets.TextFont("NimbusSans-Regular.otf"));

            //Act
            int added = TextFontChain.AddDocumentFontDirectory(directory);

            //Assert
            added.Should().Be(2);
            TextFontChain.For("C059", false, false)[0].FileName.Should().Be("one.otf");
            TextFontChain.For("Nimbus Sans", false, false)[0].FileName.Should().Be("two.otf");

            // THE CONTROL: a directory with no fonts in it registers nothing and is not
            // an error — FcConfigAppFontAddDir fails on an unreadable directory, not an
            // empty one.
            string empty = Path.Combine(directory, "empty");
            Directory.CreateDirectory(empty);
            TextFontChain.AddDocumentFontDirectory(empty).Should().Be(0);
        }
        finally
        {
            TextFontChain.ResetDocumentFonts();
            Directory.Delete(directory, true);
        }
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
