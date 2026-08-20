// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Origins;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// <see cref="PointAndClick.PercentEncode"/> against upstream's
/// <c>String_convert::percent_encode</c> (<c>flower/string-convert.cc:180-220</c>).
/// <para>
/// Every expected value is read off upstream's <c>is_not_escape_character</c>: letters,
/// digits, <c>-</c>, <c>.</c>, <c>/</c>, <c>:</c> and <c>_</c> pass; EVERYTHING else is
/// <c>%XX</c> of each UTF-8 byte. The two characters worth their own cases are the two
/// this method got wrong for the life of the port while its doc comment claimed
/// upstream's set: <c>~</c> was kept (upstream escapes it — a home-directory path in a
/// <c>textedit://</c> anchor read differently on the two engines) and <c>:</c> was
/// escaped (upstream keeps it — every anchor's own separators would have doubled as
/// encoded ones had the Scheme side ever fed a pre-joined URL through).
/// </para>
/// </summary>
public class PointAndClickTests
{
    [Fact]
    public void the_unreserved_set_is_upstreams_letters_digits_and_five_marks()
    {
        //Act + Assert
        PointAndClick.PercentEncode("azAZ09-./:_").Should().Be("azAZ09-./:_");
    }

    [Fact]
    public void a_tilde_is_escaped_the_way_upstream_escapes_it()
    {
        //Act + Assert
        PointAndClick.PercentEncode("~jeremy/file.ly").Should().Be("%7Ejeremy/file.ly");
    }

    [Fact]
    public void a_colon_passes_through_unescaped()
    {
        //Act + Assert
        PointAndClick.PercentEncode("file.ly:12:3:4").Should().Be("file.ly:12:3:4");
    }

    [Fact]
    public void a_space_and_a_hash_become_percent_bytes()
    {
        //Act + Assert
        PointAndClick.PercentEncode("a b#c").Should().Be("a%20b%23c");
    }

    [Fact]
    public void a_non_ascii_character_is_encoded_per_utf8_byte()
    {
        //Arrange
        //U+00E9 is C3 A9 in UTF-8, and upstream walks the string's BYTES.
        const string name = "café.ly";

        //Act + Assert
        PointAndClick.PercentEncode(name).Should().Be("caf%C3%A9.ly");
    }
}
