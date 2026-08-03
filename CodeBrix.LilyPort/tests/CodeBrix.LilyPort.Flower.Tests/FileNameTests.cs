/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Jan Nieuwenhuizen <janneke@gnu.org>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

// was previously: flower/test-file-name.cc, flower/test-file-path.cc,
//                 flower/test-string-convert.cc
// Modified by Jeremy Ellis on 2026-08-02 as part of the CodeBrix port:
//   - translated from C++/yaffut to C#/xUnit v3 with SilverAssertions

using System.Collections.Generic;
using System.IO;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Flower.Tests;

public class FileNameTests
{
    [Theory]
    [InlineData("foo.ly", "", "foo", "ly")]
    [InlineData("bar/foo.ly", "bar", "foo", "ly")]
    [InlineData("/bar/foo.ly", "/bar", "foo", "ly")]
    [InlineData("foo", "", "foo", "")]
    [InlineData("a/b/c/foo.tar.gz", "a/b/c", "foo.tar", "gz")]
    public void splits_a_path_into_its_parts(string input, string directory, string baseName, string extension)
    {
        //Arrange / Act
        FileName name = new FileName(input);

        //Assert -- note the extension EXCLUDES its dot
        name.Directory.Should().Be(directory);
        name.Base.Should().Be(baseName);
        name.Extension.Should().Be(extension);
    }

    [Fact]
    public void reassembling_the_parts_reproduces_the_original()
    {
        //Arrange / Act / Assert
        new FileName("bar/foo.ly").ToString().Should().Be("bar/foo.ly");
        new FileName("/bar/foo.ly").ToString().Should().Be("/bar/foo.ly");
        new FileName("foo").ToString().Should().Be("foo");
    }

    [Fact]
    public void dot_and_dot_dot_are_directories_not_base_names()
    {
        //Arrange / Act -- splitting ".." on the dot would give a nonsense extension
        FileName dot = new FileName(".");
        FileName dotDot = new FileName("..");

        //Assert
        dot.Directory.Should().Be(".");
        dot.Base.Should().Be("");
        dot.Extension.Should().Be("");
        dotDot.Directory.Should().Be("..");
        dotDot.Base.Should().Be("");
    }

    [Fact]
    public void detects_absolute_paths()
    {
        //Arrange / Act / Assert
        new FileName("/bar/foo.ly").IsAbsolute.Should().BeTrue();
        new FileName("bar/foo.ly").IsAbsolute.Should().BeFalse();
    }

    [Fact]
    public void backslashes_are_normalized_to_forward_slashes()
        => new FileName("bar\\foo.ly").Directory.Should().Be("bar");

    [Fact]
    public void canonicalized_removes_interior_dot_components()
        => new FileName("a/./b/foo.ly").Canonicalized().Directory.Should().Be("a/b");

    [Fact]
    public void canonicalized_collapses_doubled_separators()
        => new FileName("a//b/foo.ly").Canonicalized().Directory.Should().Be("a/b");

    [Fact]
    public void canonicalized_resolves_dot_dot_against_the_previous_component()
        => new FileName("a/b/../c/foo.ly").Canonicalized().Directory.Should().Be("a/c");

    [Fact]
    public void canonicalized_keeps_the_path_anchored_when_it_would_empty()
    {
        //Arrange / Act -- popping the only component leaves ".", not nothing
        FileName name = new FileName("a/../foo.ly").Canonicalized();

        //Assert
        name.Directory.Should().Be(".");
    }

    [Fact]
    public void directory_and_file_parts_split_the_whole()
    {
        //Arrange
        FileName name = new FileName("/a/b/foo.ly");

        //Act / Assert
        name.DirectoryPart().Should().Be("/a/b");
        name.FilePart().Should().Be("foo.ly");
    }

    [Fact]
    public void changing_the_extension_round_trips()
    {
        //Arrange
        FileName name = new FileName("score.ly");

        //Act
        name.Extension = "pdf";

        //Assert
        name.ToString().Should().Be("score.pdf");
    }
}

public class FilePathTests
{
    [Fact]
    public void find_locates_a_file_in_the_search_path()
    {
        //Arrange
        string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "probe.ly");
        File.WriteAllText(file, "% test");

        try
        {
            FilePath path = new FilePath();
            path.Append(directory);

            //Act
            string found = path.Find("probe.ly");

            //Assert
            found.Should().Be(directory + "/probe.ly");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void find_returns_empty_when_the_file_is_absent()
    {
        //Arrange
        FilePath path = new FilePath();
        path.Append(Path.GetTempPath());

        //Act / Assert
        path.Find("this-file-does-not-exist-12345.ly").Should().Be("");
    }

    [Fact]
    public void find_tries_each_extension_in_turn()
    {
        //Arrange
        string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "probe.ily"), "% test");

        try
        {
            FilePath path = new FilePath();
            path.Append(directory);

            //Act
            string found = path.Find("probe", new List<string> { "ly", "ily" });

            //Assert
            found.Should().Be(directory + "/probe.ily");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void try_append_only_adds_directories_that_exist()
    {
        //Arrange
        FilePath path = new FilePath();

        //Act / Assert
        path.TryAppend(Path.GetTempPath()).Should().BeTrue();
        path.TryAppend("/no/such/directory/anywhere").Should().BeFalse();
        path.Directories.Count.Should().Be(1);
    }

    [Fact]
    public void prepend_puts_a_directory_first()
    {
        //Arrange
        FilePath path = new FilePath();
        path.Append("second");

        //Act
        path.Prepend("first");

        //Assert
        path.Directories[0].Should().Be("first");
    }
}

public class StringConvertTests
{
    [Fact]
    public void hex_to_nibble_accepts_both_cases_and_rejects_others()
    {
        //Arrange / Act / Assert
        StringConvert.HexToNibble('0').Should().Be(0);
        StringConvert.HexToNibble('9').Should().Be(9);
        StringConvert.HexToNibble('a').Should().Be(10);
        StringConvert.HexToNibble('F').Should().Be(15);
        StringConvert.HexToNibble('z').Should().Be(-1);
    }

    [Fact]
    public void bin_to_hex_produces_two_lowercase_digits()
    {
        //Arrange / Act / Assert
        StringConvert.BinToHex((byte)0x00).Should().Be("00");
        StringConvert.BinToHex((byte)0xff).Should().Be("ff");
        StringConvert.BinToHex((byte)0x1a).Should().Be("1a");
    }

    [Fact]
    public void hex_to_bin_reverses_bin_to_hex()
    {
        //Arrange
        string original = "LilyPond";

        //Act
        string round = StringConvert.HexToBin(StringConvert.BinToHex(original));

        //Assert
        round.Should().Be(original);
    }

    [Fact]
    public void big_endian_helpers_emit_most_significant_byte_first()
    {
        //Arrange / Act -- MIDI is a big-endian format, which is why these exist
        string u32 = StringConvert.BigEndianU32(0x01020304);
        string u16 = StringConvert.BigEndianU16(0x0102);

        //Assert
        ((int)u32[0]).Should().Be(1);
        ((int)u32[3]).Should().Be(4);
        ((int)u16[0]).Should().Be(1);
        ((int)u16[1]).Should().Be(2);
    }

    [Fact]
    public void pad_to_pads_right_and_never_truncates()
    {
        //Arrange / Act / Assert
        StringConvert.PadTo("ab", 5).Should().Be("ab   ");
        StringConvert.PadTo("abcdef", 3).Should().Be("abcdef");
    }

    [Fact]
    public void percent_encode_leaves_the_unreserved_set_alone()
    {
        //Arrange / Act / Assert -- upstream's unreserved set is WIDER than RFC 3986's:
        //slash and colon stay literal, because these become file URIs
        StringConvert.PercentEncode("abcXYZ019-._").Should().Be("abcXYZ019-._");
        StringConvert.PercentEncode("/a/b:c").Should().Be("/a/b:c");
    }

    [Fact]
    public void percent_encode_escapes_everything_else()
    {
        //Arrange / Act / Assert
        StringConvert.PercentEncode("a b").Should().Be("a%20b");
        StringConvert.PercentEncode("#").Should().Be("%23");
    }
}
