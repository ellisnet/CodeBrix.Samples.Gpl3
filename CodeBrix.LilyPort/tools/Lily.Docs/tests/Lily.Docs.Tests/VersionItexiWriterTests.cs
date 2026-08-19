// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using CodeBrix.LilyPort;
using Lily.Docs.Generation;
using SilverAssertions;
using Xunit;

namespace Lily.Docs.Tests;

/// <summary>
/// The <c>version.itexi</c> stand-in: the file upstream generates at build time and
/// nothing that includes <c>macros.itexi</c> renders without.
/// </summary>
public sealed class VersionItexiWriterTests
{
    /// <summary>The stand in defines the three macros the script emits.</summary>
    [Fact]
    public void the_stand_in_defines_the_three_macros_the_script_emits()
    {
        //Arrange
        string content = InvokeBuildContent();

        //Act
        bool hasVersion = content.Contains("@macro version\n");
        bool hasStable = content.Contains("@macro versionStable\n");
        bool hasDevel = content.Contains("@macro versionDevel\n");

        //Assert
        // The three names create-version-itexi.py makes. macros.itexi calls @version{};
        // a missing macro renders as an unknown command rather than as an error.
        hasVersion.Should().BeTrue();
        hasStable.Should().BeTrue();
        hasDevel.Should().BeTrue();
    }

    /// <summary>The version macro carries the ports own version.</summary>
    [Fact]
    public void the_version_macro_carries_the_ports_own_version()
    {
        //Arrange
        string content = InvokeBuildContent();

        //Act
        bool statesPortVersion = content.Contains("\n" + LilyPortInfo.UpstreamVersion + "\n");

        //Assert
        // Read off LilyPortInfo rather than written as a literal: the manuals must state
        // the version of the engine that generated them, and a literal here would drift
        // the moment the port's version moves.
        statesPortVersion.Should().BeTrue();
        LilyPortInfo.UpstreamVersion.Should().Be("2.27.2");
    }

    /// <summary>Every macro is closed.</summary>
    [Fact]
    public void every_macro_is_closed()
    {
        //Arrange
        string content = InvokeBuildContent();

        //Act
        int opened = CountOccurrences(content, "@macro ");
        int closed = CountOccurrences(content, "@end macro");

        //Assert
        // An unclosed @macro swallows the rest of the file it is included into, which
        // presents as content loss far from its cause.
        opened.Should().Be(3);
        closed.Should().Be(opened);
    }

    /// <summary>Writing produces a file on disk.</summary>
    [Fact]
    public void writing_produces_a_file_on_disk()
    {
        //Arrange
        string directory = Path.Combine(Path.GetTempPath(),
            "lily-docs-version-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        //Act
        string path = VersionItexiWriter.Write(directory);

        //Assert
        try
        {
            File.Exists(path).Should().BeTrue();
            Path.GetFileName(path).Should().Be("version.itexi");
            File.ReadAllText(path).Should().Be(InvokeBuildContent());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string InvokeBuildContent()
    {
        return typeof(VersionItexiWriter)
            .GetMethod("BuildContent",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Invoke(null, null) as string;
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
