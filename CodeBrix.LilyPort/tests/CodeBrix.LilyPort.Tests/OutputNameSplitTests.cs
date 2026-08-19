// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// <c>lilypond -o</c>'s value split into a directory and a base name —
/// upstream's <c>main.cc:729-761</c>, read against upstream's source rather than
/// against the port's own output.
/// <para>
/// The rule this fences is easy to get wrong in the direction that looks tidier:
/// <c>-o</c> names a FILE, not a directory, EXCEPT when the value happens to name a
/// directory that already exists. Every case below is paired with one that must come
/// out differently, because a split that answered "directory" to everything would
/// pass half of them on its own.
/// </para>
/// </summary>
public class OutputNameSplitTests
{
    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-outname-" + Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void no_output_option_names_neither_a_directory_nor_a_base_name()
    {
        //Arrange / Act
        BatchRunner.SplitOutputName(null, out string directory, out string baseName);

        //Assert
        // Both null is the signal to keep the caller's own defaults; an empty string
        // would read as "the current directory" and is a different instruction.
        directory.Should().BeNull();
        baseName.Should().BeNull();

        BatchRunner.SplitOutputName(string.Empty, out directory, out baseName);
        directory.Should().BeNull();
        baseName.Should().BeNull();
    }

    [Fact]
    public void an_existing_directory_is_taken_as_a_directory_and_names_no_base()
    {
        //Arrange
        string scratch = ScratchDirectory();

        try
        {
            //Act
            BatchRunner.SplitOutputName(scratch, out string directory, out string baseName);

            //Assert
            directory.Should().Be(scratch);
            baseName.Should().BeNull();
        }
        finally
        {
            Directory.Delete(scratch, true);
        }
    }

    [Fact]
    public void a_path_whose_last_element_does_not_exist_is_split_into_directory_and_name()
    {
        //Arrange
        string scratch = ScratchDirectory();
        string named = Path.Combine(scratch, "chorale");

        try
        {
            //Act
            BatchRunner.SplitOutputName(named, out string directory, out string baseName);

            //Assert
            // THE CONTROL for the case above: the same directory, one element longer,
            // must come out as a directory PLUS a name.
            directory.Should().Be(scratch);
            baseName.Should().Be("chorale");
        }
        finally
        {
            Directory.Delete(scratch, true);
        }
    }

    [Fact]
    public void a_bare_name_names_a_base_and_leaves_the_directory_to_the_caller()
    {
        //Arrange / Act
        BatchRunner.SplitOutputName("chorale", out string directory, out string baseName);

        //Assert
        directory.Should().BeNull();
        baseName.Should().Be("chorale");
    }

    [Fact]
    public void a_leading_dot_directory_is_dropped_the_way_upstream_drops_it()
    {
        //Arrange / Act
        BatchRunner.SplitOutputName(
            "." + Path.DirectorySeparatorChar + "chorale",
            out string directory,
            out string baseName);

        //Assert
        // upstream: `if (!dir.empty () && (dir != "."))'. Without that guard
        // `-o ./chorale' would be a different instruction from `-o chorale', and it is
        // not.
        directory.Should().BeNull();
        baseName.Should().Be("chorale");
    }

    [Fact]
    public void the_file_part_keeps_its_extension()
    {
        //Arrange / Act
        BatchRunner.SplitOutputName("chorale.pdf", out string directory, out string baseName);

        //Assert
        // `File_name::file_part' rejoins base and ext, and the --output arm of
        // `output_file_name_for_input_file_name' is the one that does NOT clear ext_.
        // So upstream really does engrave `-o chorale.pdf' to `chorale.pdf.svg', and
        // stripping the extension here would be an improvement, which is a parity bug
        // (rule 2).
        directory.Should().BeNull();
        baseName.Should().Be("chorale.pdf");
    }
}
