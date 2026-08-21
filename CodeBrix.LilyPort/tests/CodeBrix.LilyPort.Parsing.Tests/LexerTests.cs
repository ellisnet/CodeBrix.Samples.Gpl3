// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// The modal scanner: the start-condition machinery the rest of the lexer hangs off.
/// <para>
/// All fourteen start conditions have their rules. What the scanner still DELEGATES is
/// the data those rules consult — the note-name and keyword tables, markup signatures,
/// and a Scheme reader — through <see cref="ILexerHost"/>, exactly as upstream reaches
/// them through <c>Lily_parser</c> and Guile. <see cref="LexerCoverage.DelegatedToHost"/>
/// names them, so "every mode is ported" is not read as "the lexer is finished".
/// </para>
/// </summary>
public class LexerTests
{
    private static ModalScanner Scan(string input)
        => new ModalScanner(LilyPondLexerRules.Create(), input, "<test>");

    /// <summary>
    /// <c>Lily_lexer::scan_escaped_word</c> opens by giving a music identifier's value
    /// the CURRENT input position, and <c>scan_shorthand</c> does the same.
    /// <para>
    /// It is the only thing that gives one identifier a per-use origin: a bare
    /// <c>\glide</c> is <c>(make-music 'FingerGlideEvent)</c>, which sets none, so
    /// without this every use shares one (absent) origin — and
    /// <c>finger-key-glide</c>, which partitions glide events BY origin, then takes
    /// them all for the first one and calls <c>car</c> on the empty list.
    /// </para>
    /// <para>
    /// The fence is the RELATIONSHIP between two uses of ONE identifier: they must be
    /// stamped with DIFFERENT locations. A single-use assertion would pass on a lexer
    /// that stamped a constant.
    /// </para>
    /// </summary>
    [Fact]
    public void two_uses_of_one_music_identifier_are_stamped_with_different_positions()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        host.Identifiers["glide"] = new LexerLookup("EVENT_IDENTIFIER", new MusicObject(Nil.Instance));
        ModalScanner scanner = new ModalScanner(
            LilyPondLexerRules.Create(host), "\\glide \\glide", "<test>");

        //Act
        Drain(scanner);

        //Assert
        host.MusicIdentifierSpots.Should().HaveCount(2);
        SourceSpan first = host.MusicIdentifierSpots[0].Value;
        SourceSpan second = host.MusicIdentifierSpots[1].Value;
        (first.StartColumn == second.StartColumn).Should().BeFalse();
    }

    /// <summary>
    /// The CONTROL for the above: a word that resolves to NOTHING is never offered for
    /// stamping, so "the scanner stamps every word it reads" cannot pass.
    /// </summary>
    [Fact]
    public void an_unknown_escaped_word_is_never_offered_for_stamping()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ModalScanner scanner = new ModalScanner(
            LilyPondLexerRules.Create(host), "\\nosuchidentifier", "<test>");

        //Act
        Drain(scanner);

        //Assert
        host.MusicIdentifierSpots.Should().BeEmpty();
    }

    private static void Drain(ModalScanner scanner)
    {
        while (scanner.Next().Symbol != 0)
        {
            // The ported rules produce no tokens yet; this runs them for their effects.
        }
    }

    /// <summary>
    /// A printable Latin-1 symbol is TEXT, not an invalid character.
    /// <para>
    /// The last rule in the file is upstream's <c>&lt;*&gt;.[\200-\277]*</c>, whose class
    /// is BYTES — the UTF-8 continuation bytes — so that a stray lead byte and its
    /// continuations are reported as one character. Written over .NET chars as
    /// <c>[\u0080-\u00bf]</c> it became the printable Latin-1 symbols instead, and
    /// because the class FOLLOWED the dot the rule swallowed the PRECEDING character
    /// too: length two where the whitespace rule matched one, so it won the
    /// longest-match contest and turned <c>Copyright © 2026</c> into an error AT THE
    /// SPACE.
    /// </para>
    /// <para>
    /// ⚠ THE SYMPTOM NAMED THE WRONG PLACE TWICE OVER. What reached the user was
    /// "syntax error, unexpected token -1" — the grammar has no terminal for the
    /// <c>'%'</c> the rule returns, so <c>Terminal</c> answered -1 — reported one
    /// character before the one at fault, in a document whose only unusual feature
    /// looked like a multi-line <c>\font-select</c> argument.
    /// </para>
    /// </summary>
    [Fact]
    public void a_printable_latin1_symbol_lexes_as_text_rather_than_as_an_invalid_character()
    {
        //Arrange
        ModalScanner scanner = Scan("\\markup { Copyright \u00A9 2026 }");

        //Act
        Drain(scanner);

        //Assert
        // Filtered to THIS rule's diagnostic rather than asserted empty: the scanner is
        // built with no host tables here, so `\markup' itself is an unknown command and
        // says so. That is the harness, not the subject.
        scanner.Diagnostics.Where(d => d.Contains("invalid character"))
            .Should().BeEmpty();
    }

    /// <summary>
    /// Every character the old class covered lexes cleanly, and its neighbours outside
    /// the class still did.
    /// </summary>
    [Fact]
    public void every_character_the_old_class_covered_lexes_cleanly()
    {
        //Arrange
        List<int> broken = new List<int>();

        //Act
        // U+00A0 to U+00CF spans the whole of the mis-translated class and reaches past
        // its far edge, so the gate covers the characters that failed AND the ones that
        // never did. Asserted as a RANGE rather than as the one character the notation
        // manual happened to use: © was the messenger, not the subject.
        for (int codePoint = 0x00A0; codePoint <= 0x00CF; codePoint++)
        {
            ModalScanner scanner = Scan("\\markup { x " + (char)codePoint + " y }");
            Drain(scanner);
            if (scanner.Diagnostics.Any(d => d.Contains("invalid character")))
            {
                broken.Add(codePoint);
            }
        }

        //Assert
        broken.Should().BeEmpty();
    }

    /// <summary>
    /// An unmatched character is still reported, and reported WHOLE.
    /// <para>
    /// THE CONTROL for the two gates above: a rule that had simply stopped reporting
    /// would pass both of them. It also fences the half the fix had to keep — upstream's
    /// "Better not return half a utf8 character" — in the only form that means anything
    /// over UTF-16, which is a surrogate PAIR.
    /// </para>
    /// </summary>
    [Fact]
    public void an_unmatched_astral_character_is_reported_once_and_whole()
    {
        //Arrange
        // U+1D11E, the G clef: two UTF-16 code units, and no rule matches it outside a
        // markup word.
        string clef = char.ConvertFromUtf32(0x1D11E);
        ModalScanner scanner = Scan(clef);

        //Act
        Drain(scanner);

        //Assert
        // ONCE: a rule consuming one code unit at a time would report twice.
        scanner.Diagnostics.Should().ContainSingle();
        scanner.Diagnostics[0].Should().Contain("invalid character");

        // WHOLE: the message carries both code units, so it names the character rather
        // than half of one.
        scanner.Diagnostics[0].Should().Contain(clef);
    }

    [Fact]
    public void there_are_thirteen_exclusive_start_conditions()
    {
        //Arrange / Act / Assert
        // lexer.ll declares thirteen %x conditions, and the O7 decision record counts
        // the same. INITIAL is flex's own, making fourteen values in the enum.
        Enum.GetValues<LexerState>().Should().HaveCount(14);
        Enum.IsDefined(LexerState.Initial).Should().BeTrue();
    }

    [Fact]
    public void whitespace_and_line_comments_are_skipped()
    {
        //Arrange
        ModalScanner scanner = Scan("  % a comment\n   \n");

        //Act
        ParserToken token = scanner.Next();

        //Assert
        token.Symbol.Should().Be(0);
        scanner.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void a_block_comment_pushes_and_pops_the_start_condition()
    {
        //Arrange
        // %{ ... %} is a STATE, not a pattern, because its contents must not be scanned
        // as anything else. Getting in and back out again is the whole behaviour.
        ModalScanner scanner = Scan("%{ this %text is not scanned %}  ");

        //Act
        Drain(scanner);

        //Assert
        scanner.State.Should().Be(LexerState.Initial);
        scanner.StateDepth.Should().Be(0);
        scanner.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void a_version_statement_reads_its_string_in_its_own_state()
    {
        //Arrange
        // \version pushes a start condition whose only job is to read one quoted
        // string. Doing it with a state rather than a pattern is what lets the string
        // contain characters that mean something else everywhere in the file.
        ModalScanner scanner = Scan("\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n");

        //Act
        Drain(scanner);

        //Assert
        scanner.LastVersionString.AsText().Should().Be(LilyVersion.CompatibleWithVersion);
        scanner.State.Should().Be(LexerState.Initial);
        scanner.StateDepth.Should().Be(0);
    }

    [Fact]
    public void start_conditions_nest_rather_than_replacing_each_other()
    {
        //Arrange
        // %option stack, and it matters: a \markup inside \lyricmode has to come back
        // to lyrics, not to the top. A scanner that used BEGIN everywhere would lose
        // the outer mode silently.
        ModalScanner scanner = Scan(string.Empty);

        //Act
        scanner.PushState(LexerState.Lyrics);
        scanner.PushState(LexerState.Markup);

        //Assert
        scanner.State.Should().Be(LexerState.Markup);
        scanner.TopState().Should().Be(LexerState.Lyrics);
        scanner.StateDepth.Should().Be(2);

        scanner.PopState();
        scanner.State.Should().Be(LexerState.Lyrics);

        scanner.PopState();
        scanner.State.Should().Be(LexerState.Initial);
    }

    [Fact]
    public void the_longest_match_wins_and_ties_go_to_the_earlier_rule()
    {
        //Arrange
        // flex's core semantics, and LilyPond leans on them: \version beats a shorter
        // command match only because it matches more characters.
        List<LexerRule> rules = new List<LexerRule>
        {
            new LexerRule("ab", null, (scanner, text) => new ParserToken(1, text, default)),
            new LexerRule("abc", null, (scanner, text) => new ParserToken(2, text, default)),
            new LexerRule("abc", null, (scanner, text) => new ParserToken(3, text, default)),
        };

        ModalScanner scanner = new ModalScanner(rules, "abc");

        //Act
        ParserToken token = scanner.Next();

        //Assert
        // The longer match wins over the earlier rule...
        token.Symbol.Should().Be(2);

        // ...and between the two equally long ones, the earlier wins.
        token.Value.AsText().Should().Be("abc");
    }

    [Fact]
    public void an_unrecognised_character_is_reported_rather_than_echoed()
    {
        //Arrange
        // %option nodefault: flex's default action echoes unmatched input to stdout,
        // which would turn a typo into silently-accepted text.
        //
        // With every mode's rules in place a stray character is not UNMATCHED -- the
        // {SHORTHAND} rule matches any single character in the music modes, exactly as
        // upstream does, and the failure is then an identifier lookup that finds
        // nothing. Same outcome, reported at the point that actually knows.
        ModalScanner scanner = Scan("");

        //Act
        Drain(scanner);

        //Assert
        scanner.Diagnostics.Should().ContainSingle();
        scanner.Diagnostics[0].Should().Contain("undefined character or shorthand");
    }

    [Fact]
    public void a_token_pushed_back_by_a_rule_action_is_read_before_any_input()
    {
        //Arrange
        // What MYBACKUP and MYREPARSE need. The parser pushes the pending lookahead
        // back and puts different tokens in front of it; the scanner has to hand those
        // out before reading another character.
        ModalScanner scanner = Scan("   ");

        //Act
        scanner.PushExtraToken(new ParserToken(42, "second", default));
        scanner.PushExtraToken(new ParserToken(41, "first", default));

        //Assert
        // Most recently pushed comes out first, which is what makes the macros' order
        // of pushes produce the order upstream intends.
        scanner.Next().Value.AsText().Should().Be("first");
        scanner.Next().Value.AsText().Should().Be("second");
        scanner.Next().Symbol.Should().Be(0);
    }

    [Fact]
    public void locations_track_lines_and_columns_across_the_input()
    {
        //Arrange
        // %locations is declared in the grammar and the actions read it, so a token's
        // span has to be right or every diagnostic points at the wrong place.
        List<LexerRule> rules = new List<LexerRule>
        {
            new LexerRule("[ \n]+", null, (scanner, text) => null),
            new LexerRule("[a-z]+", null, (scanner, text) => scanner.Token(1, text)),
        };

        ModalScanner scanner = new ModalScanner(rules, "abc\n  def");

        //Act
        ParserToken first = scanner.Next();
        ParserToken second = scanner.Next();

        //Assert
        first.Location.StartLine.Should().Be(1);
        first.Location.StartColumn.Should().Be(1);
        second.Location.StartLine.Should().Be(2);
        second.Location.StartColumn.Should().Be(3);
    }

    [Fact]
    public void the_start_conditions_not_yet_covered_are_named()
    {
        //Arrange / Act / Assert
        // The fence. The mode machinery is ported; the token-producing rules for each
        // mode are not. Naming what is missing is what keeps "the lexer exists" from
        // being read as "the lexer is done" -- the same reason
        // EngraveResult.MissingTranslators names its absentees.
        LexerCoverage.Ported.Should().Contain(LexerState.Initial);
        LexerCoverage.Ported.Should().Contain(LexerState.LongComment);
        LexerCoverage.Ported.Should().Contain(LexerState.Version);

        // All fourteen start conditions now have their rules.
        LexerCoverage.Ported.Should().HaveCount(Enum.GetValues<LexerState>().Length);
        LexerCoverage.NotYetPorted.Should().BeEmpty();

        // What is still delegated is the DATA, not the rules -- and it is named.
        LexerCoverage.DelegatedToHost.Should().NotBeEmpty();
    }

    private static List<ParserToken> TokensOf(string input, LexerState start = LexerState.Initial)
    {
        ModalScanner scanner = new ModalScanner(LilyPondLexerRules.Create(), input, "<test>");
        scanner.UseSymbols(Names, Names.Count);
        scanner.Begin(start);

        List<ParserToken> tokens = new List<ParserToken>();
        while (true)
        {
            ParserToken token = scanner.Next();
            if (token.Symbol == 0)
            {
                return tokens;
            }

            tokens.Add(token);
        }
    }

    /// <summary>
    /// A stand-in symbol table: the terminal NAMES, numbered by position. The real one
    /// comes from the parse tables; these tests only need names to come back out.
    /// </summary>
    private static readonly List<string> Names = new List<string>
    {
        "END_OF_FILE", "SYMBOL", "UNSIGNED", "REAL", "FRACTION", "STRING",
        "RESTNAME", "CHORD_REPETITION", "MULTI_MEASURE_REST", "SCM_TOKEN",
        "DOUBLE_ANGLE_OPEN", "DOUBLE_ANGLE_CLOSE", "ANGLE_OPEN", "ANGLE_CLOSE",
        "FIGURE_OPEN", "FIGURE_CLOSE", "FIGURE_SPACE", "CHORD_MINUS", "CHORD_COLON",
        "CHORD_SLASH", "CHORD_CARET", "CHORD_BASS", "EXTENDER", "HYPHEN",
        "SCORE", "'{'", "'}'", "'*'", "'.'", "'='", "'%'", "E_UNSIGNED",
    };

    private static string NameOf(ParserToken token)
        => token.Symbol >= 0 && token.Symbol < Names.Count ? Names[token.Symbol] : "?" + token.Symbol;

    [Fact]
    public void notes_mode_reads_words_numbers_and_durations()
    {
        //Arrange / Act
        // The mode a score spends most of its time in. `c` comes out as a SYMBOL here
        // rather than as a pitch because the note-name TABLE lives in the host -- see
        // LexerCoverage.DelegatedToHost -- but the tokenisation is upstream's.
        List<ParserToken> tokens = TokensOf("c4 r2 R1 q", LexerState.Notes);

        //Assert
        tokens.Should().HaveCount(7);
        NameOf(tokens[0]).Should().Be("SYMBOL");
        NameOf(tokens[1]).Should().Be("UNSIGNED");
        NameOf(tokens[2]).Should().Be("RESTNAME");
        NameOf(tokens[3]).Should().Be("UNSIGNED");
        NameOf(tokens[4]).Should().Be("MULTI_MEASURE_REST");
        NameOf(tokens[5]).Should().Be("UNSIGNED");
        NameOf(tokens[6]).Should().Be("CHORD_REPETITION");
    }

    [Fact]
    public void a_quoted_string_is_read_in_its_own_state_with_escapes()
    {
        //Arrange / Act
        // The quote states exist so that a string can contain characters that mean
        // something else everywhere else -- and the escapes have to survive.
        List<ParserToken> tokens = TokensOf("\"a\\nb\"", LexerState.Initial);

        //Assert
        tokens.Should().ContainSingle();
        NameOf(tokens[0]).Should().Be("STRING");
        tokens[0].Value.AsText().Should().Be("a\nb");
    }

    [Fact]
    public void lyrics_mode_recognises_the_extender_and_the_hyphen()
    {
        //Arrange / Act
        // -- and __ are single tokens in lyrics and nowhere else, which is exactly the
        // kind of thing an exclusive start condition is for.
        List<ParserToken> tokens = TokensOf("Ah -- ha __", LexerState.Lyrics);

        //Assert
        NameOf(tokens[0]).Should().Be("SYMBOL");
        NameOf(tokens[1]).Should().Be("HYPHEN");
        NameOf(tokens[2]).Should().Be("SYMBOL");
        NameOf(tokens[3]).Should().Be("EXTENDER");
    }

    [Fact]
    public void an_underscore_in_a_lyric_syllable_becomes_a_space()
    {
        //Arrange / Act
        // lyric_fudge. It is how a syllable carries a space without ending at one.
        List<ParserToken> tokens = TokensOf("a_b", LexerState.Lyrics);

        //Assert
        tokens.Should().ContainSingle();
        tokens[0].Value.AsText().Should().Be("a b");
    }

    [Fact]
    public void chords_mode_gives_its_punctuation_its_own_tokens()
    {
        //Arrange / Act
        // In chords, ':' '/' '^' and '-' are chord syntax rather than the articulation
        // and duration marks they are in notes.
        List<ParserToken> tokens = TokensOf("c:7/+e^5-", LexerState.Chords);

        //Assert
        NameOf(tokens[0]).Should().Be("SYMBOL");
        NameOf(tokens[1]).Should().Be("CHORD_COLON");
        NameOf(tokens[2]).Should().Be("UNSIGNED");
        NameOf(tokens[3]).Should().Be("CHORD_BASS");
        NameOf(tokens[4]).Should().Be("SYMBOL");
        NameOf(tokens[5]).Should().Be("CHORD_CARET");
        NameOf(tokens[6]).Should().Be("UNSIGNED");
        NameOf(tokens[7]).Should().Be("CHORD_MINUS");
    }

    [Fact]
    public void figures_mode_gives_angle_brackets_their_figure_meanings()
    {
        //Arrange / Act
        // < and > open and close a figure group here rather than a chord.
        List<ParserToken> tokens = TokensOf("<6 4>", LexerState.Figures);

        //Assert
        NameOf(tokens[0]).Should().Be("FIGURE_OPEN");
        NameOf(tokens[1]).Should().Be("UNSIGNED");
        NameOf(tokens[2]).Should().Be("UNSIGNED");
        NameOf(tokens[3]).Should().Be("FIGURE_CLOSE");
    }

    [Fact]
    public void markup_mode_reads_bare_words_as_symbols_and_keeps_its_braces()
    {
        //Arrange / Act
        List<ParserToken> tokens = TokensOf("{ hello world }", LexerState.Markup);

        //Assert
        NameOf(tokens[0]).Should().Be("'{'");
        NameOf(tokens[1]).Should().Be("SYMBOL");
        NameOf(tokens[2]).Should().Be("SYMBOL");
        NameOf(tokens[3]).Should().Be("'}'");
    }

    [Fact]
    public void a_fraction_reads_as_a_scheme_pair_of_numerator_and_denominator()
    {
        //Arrange / Act
        List<ParserToken> tokens = TokensOf("3/4", LexerState.Notes);

        //Assert
        // upstream's scan_fraction is scm_cons (num, den) — a SCHEME PAIR, which is
        // what makes FRACTION usable as a semantic value in its own right
        // (embedded_scm_bare_arg, identifier_init) and what
        // `multipliers: multipliers '*' FRACTION` reads with scm_car/scm_cdr.
        tokens.Should().ContainSingle();
        NameOf(tokens[0]).Should().Be("FRACTION");
        tokens[0].Value.Should().BeOfType<Pair>();

        Pair fraction = (Pair)tokens[0].Value;
        fraction.Car.Should().Be(3L);
        fraction.Cdr.Should().Be(4L);
    }

    [Fact]
    public void embedded_scheme_is_read_as_one_token_however_deeply_nested()
    {
        //Arrange / Act
        // # hands the parser one SCM_TOKEN whatever the expression's shape, and the
        // scanner has to step past exactly the expression -- no more, no less.
        List<ParserToken> tokens = TokensOf("#(a (b c) \"d)\") e", LexerState.Notes);

        //Assert
        NameOf(tokens[0]).Should().Be("SCM_TOKEN");
        NameOf(tokens[1]).Should().Be("SYMBOL");
        tokens[1].Value.AsText().Should().Be("e");
    }
}
