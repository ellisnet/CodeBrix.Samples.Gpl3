// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Shell.Kernel.Commands;
using SilverAssertions;
using Xunit;

namespace Lily.Shell.Kernel.Tests;

public class CommandLineTokenizerTests
{
    [Fact]
    public void whitespace_separates_tokens()
    {
        //Act
        var tokens = CommandLineTokenizer.Tokenize("engrave  file.ly   -o out.svg");

        //Assert
        tokens.Should().Equal("engrave", "file.ly", "-o", "out.svg");
    }

    [Fact]
    public void empty_and_blank_lines_yield_no_tokens()
    {
        //Assert
        CommandLineTokenizer.Tokenize("").Should().BeEmpty();
        CommandLineTokenizer.Tokenize("   ").Should().BeEmpty();
        CommandLineTokenizer.Tokenize(null).Should().BeEmpty();
    }

    [Fact]
    public void double_quotes_group_whitespace_into_one_token()
    {
        //Act
        var tokens = CommandLineTokenizer.Tokenize("engrave \"my score.ly\"");

        //Assert
        tokens.Should().Equal("engrave", "my score.ly");
    }

    [Fact]
    public void quotes_may_join_mid_token()
    {
        //Act
        var tokens = CommandLineTokenizer.Tokenize("a\"b c\"d");

        //Assert
        tokens.Should().Equal("ab cd");
    }

    [Fact]
    public void backslash_escapes_quote_and_backslash_inside_quotes()
    {
        //Act
        var tokens = CommandLineTokenizer.Tokenize("say \"a \\\"quoted\\\" word\\\\\"");

        //Assert
        tokens.Should().Equal("say", "a \"quoted\" word\\");
    }

    [Fact]
    public void backslash_outside_quotes_is_literal()
    {
        //Act
        var tokens = CommandLineTokenizer.Tokenize(@"open C:\scores\test.ly");

        //Assert
        tokens.Should().Equal("open", @"C:\scores\test.ly");
    }

    [Fact]
    public void an_unterminated_quote_runs_to_end_of_line()
    {
        //Act
        var tokens = CommandLineTokenizer.Tokenize("engrave \"unfinished name");

        //Assert
        tokens.Should().Equal("engrave", "unfinished name");
    }

    [Fact]
    public void an_empty_quoted_token_is_preserved()
    {
        //Act
        var tokens = CommandLineTokenizer.Tokenize("echo \"\"");

        //Assert
        tokens.Should().Equal("echo", "");
    }
}
