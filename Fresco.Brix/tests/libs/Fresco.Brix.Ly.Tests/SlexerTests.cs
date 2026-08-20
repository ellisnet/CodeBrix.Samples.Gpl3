// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly.Slexing;
using SilverAssertions;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Ly.Tests;

/// <summary>
/// The stateful-lexer machinery against upstream's own example: ly/slexer.py's
/// module docstring DEFINES a mini-grammar (words, numbers, quoted strings)
/// and PRINTS the token stream it produces, and its <c>__main__</c> block adds
/// the freeze/thaw round trip through a Fridge. Every expectation here is that
/// printed output — nothing is recorded from the port.
/// </summary>
public class SlexerTests
{
    private sealed class Word : Token
    {
        internal const string Pattern = @"\w+";

        internal static readonly TokenRule Rule
            = TokenRule.Of<Word>(Pattern, (t, p) => new Word(t, p));

        private Word(string text, int pos)
            : base(text, pos)
        {
        }
    }

    private sealed class Number : Token
    {
        internal const string Pattern = @"\d+";

        internal static readonly TokenRule Rule
            = TokenRule.Of<Number>(Pattern, (t, p) => new Number(t, p));

        private Number(string text, int pos)
            : base(text, pos)
        {
        }
    }

    private class StringText : Token
    {
        internal static readonly TokenRule Rule
            = TokenRule.Factory<StringText>((t, p) => new StringText(t, p));

        protected StringText(string text, int pos)
            : base(text, pos)
        {
        }
    }

    private sealed class StringStart : StringText
    {
        internal const string Pattern = "\"";

        internal static new readonly TokenRule Rule
            = TokenRule.Of<StringStart>(Pattern, (t, p) => new StringStart(t, p));

        private StringStart(string text, int pos)
            : base(text, pos)
        {
        }

        public override void UpdateState(State state) => state.Enter(new PString());
    }

    private sealed class StringEnd : StringText
    {
        internal const string Pattern = "\"";

        internal static new readonly TokenRule Rule
            = TokenRule.Of<StringEnd>(Pattern, (t, p) => new StringEnd(t, p));

        private StringEnd(string text, int pos)
            : base(text, pos)
        {
        }

        public override void UpdateState(State state) => state.Leave();
    }

    private sealed class PTest : Parser
    {
        private static readonly TokenRule[] ParserItems =
        [
            Number.Rule,
            Word.Rule,
            StringStart.Rule,
        ];

        protected override TokenRule[] Items => ParserItems;
    }

    private sealed class PString : Parser
    {
        private static readonly TokenRule[] ParserItems =
        [
            StringEnd.Rule,
        ];

        public override TokenRule Default => StringText.Rule;

        protected override TokenRule[] Items => ParserItems;
    }

    private static List<string> Render(IEnumerable<Token> tokens)
        => tokens.Select(t => t.GetType().Name + " |" + t.Text + "|").ToList();

    [Fact]
    public void the_docstring_example_tokenizes_exactly_as_printed()
    {
        //Arrange
        State state = new State(new PTest());

        //Act
        List<string> result = Render(state.Tokens(
            "een tekst met 7 woorden, "
            + "een \"tekst met 2 aanhalingstekens\" "
            + "en 2 of 3 nummers"));

        //Assert
        result.Should().Equal(new List<string>
        {
            "Word |een|",
            "Word |tekst|",
            "Word |met|",
            "Number |7|",
            "Word |woorden|",
            "Word |een|",
            "StringStart |\"|",
            "StringText |tekst met 2 aanhalingstekens|",
            "StringEnd |\"|",
            "Word |en|",
            "Number |2|",
            "Word |of|",
            "Number |3|",
            "Word |nummers|",
        });
    }

    [Fact]
    public void a_frozen_state_resumes_inside_the_string_context()
    {
        //Arrange
        //Upstream's __main__: tokenize a text that OPENS a string without
        //closing it, freeze, thaw, and continue — the continuation must still
        //be in the string parser and close it.
        State state = new State(new PTest());
        Fridge fridge = new Fridge();
        List<string> first = Render(state.Tokens("text with \"part of a "));

        //Act
        int number = fridge.Freeze(state);
        State resumed = fridge.Thaw(number);
        List<string> second = Render(resumed.Tokens("quoted string\" in the middle"));

        //Assert
        first.Should().Equal(new List<string>
        {
            "Word |text|",
            "Word |with|",
            "StringStart |\"|",
            "StringText |part of a |",
        });
        //After StringEnd the state LEFT the string parser, and PTest has no
        //default — upstream's own printed output is three Words (verified by
        //running ly/slexer.py's __main__; the first version of this fence
        //guessed a default token here and the PORT was right).
        second.Should().Equal(new List<string>
        {
            "StringText |quoted string|",
            "StringEnd |\"|",
            "Word |in|",
            "Word |the|",
            "Word |middle|",
        });
    }

    [Fact]
    public void freezing_the_same_state_twice_answers_the_same_number()
    {
        //Arrange
        State one = new State(new PTest());
        Render(one.Tokens("a \"b"));
        State two = new State(new PTest());
        Render(two.Tokens("c \"d"));
        Fridge fridge = new Fridge();

        //Act + Assert
        //Both states sit inside PString over PTest, so they freeze EQUAL and
        //the fridge stores one entry — upstream's index-by-equality contract.
        fridge.Freeze(one).Should().Be(fridge.Freeze(two));
        fridge.Count().Should().Be(1);
    }

    [Fact]
    public void token_positions_point_into_the_parsed_string()
    {
        //Arrange
        State state = new State(new PTest());

        //Act
        List<Token> tokens = state.Tokens("ab 12").ToList();

        //Assert
        tokens[0].Pos.Should().Be(0);
        tokens[0].End.Should().Be(2);
        tokens[1].Pos.Should().Be(3);
        tokens[1].End.Should().Be(5);
    }
}
