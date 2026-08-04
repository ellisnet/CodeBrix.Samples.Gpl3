// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Parsing.Grammar;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// The fence over the vendored grammar: what the mirror IS, and what the reader makes
/// of it.
/// <para>
/// Counts are asserted with EQUALITY, on purpose and for the same reason the Scheme
/// load fences are. This is the file that has to break when the mirror is re-synced to
/// a newer LilyPond — a re-sync that slipped through silently would leave the
/// hand-ported rule actions keyed to productions that no longer exist.
/// </para>
/// </summary>
public class GrammarShapeTests
{
    private static readonly BisonGrammar Grammar = BisonGrammarReader.ReadMirroredGrammar();

    [Fact]
    public void the_mirror_is_the_pinned_v2_27_2_sources()
    {
        //Arrange / Act / Assert
        // The mirror is byte-identical to lilypond/lily/parser.yy and lexer.ll at
        // commit 2d621459bd. If this fails, either the mirror was edited — which
        // parser-mirror/README.txt forbids — or it was deliberately re-synced, in which
        // case this constant and every count below are updated in the same change.
        GrammarMirror.Sha256Of(GrammarMirror.ParserSource)
            .Should().Be(GrammarMirror.PinnedParserSha256);
        GrammarMirror.Sha256Of(GrammarMirror.LexerSource)
            .Should().Be(GrammarMirror.PinnedLexerSha256);
    }

    [Fact]
    public void the_grammar_declares_the_bison_options_the_driver_has_to_reproduce()
    {
        //Arrange / Act / Assert
        // These four are why the port writes its own driver rather than using a
        // third-party generator (decision O7, master plan section 13). api.pure full
        // means no global parser state; %locations means a location stack the actions
        // read; and the two parse-params are what every action reaches the Lily_parser
        // through.
        Grammar.Defines.Should().ContainKey("api.pure");
        Grammar.Defines["api.pure"].Should().Be("full");
        Grammar.Defines.Should().ContainKey("parse.error");
        Grammar.HasLocations.Should().BeTrue();
        Grammar.ParseParameters.Should().HaveCount(2);
        Grammar.LexParameters.Should().HaveCount(1);
    }

    [Fact]
    public void the_start_symbol_is_the_left_hand_side_of_the_first_rule()
    {
        //Arrange / Act / Assert
        // parser.yy declares no %start, so Bison takes the first rule's left-hand side.
        Grammar.StartSymbol.Should().Be("start_symbol");
    }

    [Fact]
    public void the_precedence_declarations_are_all_read_in_order()
    {
        //Arrange / Act / Assert
        // CORRECTION to a figure the O7 decision record carries. Master plan section 13
        // says "the 36 precedence declarations"; what parser.yy actually has is TEN
        // %left/%right/%nonassoc declarations, and Bison assigns one precedence LEVEL
        // per declaration. The distinction matters to the generator: conflict
        // resolution compares levels, so ten is the number that decides shift/reduce
        // outcomes. (The larger figure appears to have counted something else; the
        // symbols carrying precedence measure 24, also not 36.)
        Grammar.PrecedenceLevelCount.Should().Be(10);

        int symbolsWithPrecedence = 0;
        foreach (GrammarSymbol symbol in Grammar.Symbols)
        {
            if (symbol.Precedence.HasValue)
            {
                symbolsWithPrecedence++;
            }
        }

        symbolsWithPrecedence.Should().Be(24);

        GrammarSymbol bottom = Grammar.Find("PREC_BOT");
        GrammarSymbol top = Grammar.Find("PREC_TOP");
        bottom.Should().NotBeNull();
        top.Should().NotBeNull();
        bottom.Precedence.Should().BeLessThan(top.Precedence.Value);

        // A precedence declaration also DECLARES its symbols. PREC_BOT is named
        // nowhere else in the grammar except %prec, so if declarations did not declare,
        // it would not exist at all.
        bottom.IsTerminal.Should().BeTrue();
        bottom.Associativity.Should().Be(Associativity.Left);
        Grammar.Find("REPEAT").Associativity.Should().Be(Associativity.None);
        Grammar.Find("UNARY_MINUS").Associativity.Should().Be(Associativity.Left);
    }

    [Fact]
    public void tokens_carry_their_display_alias()
    {
        //Arrange / Act / Assert
        // The alias is what makes LilyPond's syntax errors say \accepts rather than
        // ACCEPTS. It has no grammatical meaning, and the driver has to carry it for
        // the error messages to match upstream's.
        Grammar.Find("ACCEPTS").Alias.Should().Be("\\\\accepts");

        // END_OF_FILE is the one token with an explicit number, and it must be zero.
        GrammarSymbol endOfFile = Grammar.Find("END_OF_FILE");
        endOfFile.DeclaredNumber.Should().Be(0);
        endOfFile.Alias.Should().Be("end of input");
    }

    [Fact]
    public void character_literals_are_terminals_even_though_nothing_declares_them()
    {
        //Arrange / Act / Assert
        // '{' and '}' never appear in a %token declaration; Bison infers them from
        // their use in rules. Treating them as nonterminals would leave them with no
        // rules and make the whole grammar unreachable.
        Grammar.Find("'{'").Kind.Should().Be(SymbolKind.CharacterLiteral);
        Grammar.Find("'}'").Kind.Should().Be(SymbolKind.CharacterLiteral);
        Grammar.Find("'{'").IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void every_symbol_used_on_a_right_hand_side_is_a_token_or_has_rules()
    {
        //Arrange / Act
        // The strongest structural check available without building the tables: a
        // nonterminal with no rules is a symbol the parser can never derive, and it is
        // exactly what a mis-read %token declaration produces.
        List<string> undefined = new List<string>();

        foreach (GrammarRule rule in Grammar.Rules)
        {
            foreach (string name in rule.RightHandSide)
            {
                GrammarSymbol symbol = Grammar.Find(name);
                if (symbol != null && !symbol.IsTerminal && Grammar.RulesFor(name).Count == 0)
                {
                    undefined.Add(name);
                }
            }
        }

        //Assert
        undefined.Should().BeEmpty();
    }

    [Fact]
    public void mid_rule_actions_become_anonymous_empty_rules()
    {
        //Arrange / Act
        // Bison rewrites `a: B { action } C` into `a: B $@1 C` plus `$@1: /*empty*/`.
        // The rewrite CHANGES THE GRAMMAR -- the synthesized rule is a real reduction
        // point and can create conflicts -- so the reader reproduces it rather than
        // dropping the action or attaching it to the wrong rule.
        int midRuleCount = 0;
        foreach (GrammarRule rule in Grammar.Rules)
        {
            if (rule.IsMidRuleAction)
            {
                midRuleCount++;
                rule.RightHandSide.Should().BeEmpty();
                rule.ActionText.Should().NotBeNull();
                rule.LeftHandSide.Should().StartWith("$@");
            }
        }

        //Assert
        // start_symbol's second alternative is the first one in the file:
        //     | EMBEDDED_LILY { push_note_state } embedded_lilypond { ... }
        midRuleCount.Should().BeGreaterThan(0);

        IReadOnlyList<GrammarRule> start = Grammar.RulesFor("start_symbol");
        start.Should().HaveCount(2);
        start[1].RightHandSide.Should().HaveCount(3);
        start[1].RightHandSide[0].Should().Be("EMBEDDED_LILY");
        start[1].RightHandSide[1].Should().StartWith("$@");
        start[1].RightHandSide[2].Should().Be("embedded_lilypond");
    }

    [Fact]
    public void an_empty_alternative_is_a_rule_with_no_right_hand_side()
    {
        //Arrange / Act / Assert
        // `lilypond: /* empty */ { $$ = SCM_UNSPECIFIED; }` -- the comment is trivia,
        // so the alternative reads as genuinely empty. An empty rule that came out
        // non-empty would silently make the grammar unable to start.
        IReadOnlyList<GrammarRule> lilypond = Grammar.RulesFor("lilypond");
        lilypond[0].RightHandSide.Should().BeEmpty();
        lilypond[0].ActionText.Should().Contain("SCM_UNSPECIFIED");
    }

    [Fact]
    public void a_prec_annotation_is_read_off_the_alternative_it_belongs_to()
    {
        //Arrange / Act
        // %prec overrides the precedence a rule would otherwise take from its last
        // terminal, and getting it wrong changes how the grammar resolves shift/reduce
        // conflicts -- silently, into a parser that accepts different music.
        List<GrammarRule> withPrecedence = new List<GrammarRule>();
        foreach (GrammarRule rule in Grammar.Rules)
        {
            if (rule.PrecedenceSymbol != null)
            {
                withPrecedence.Add(rule);
            }
        }

        //Assert
        withPrecedence.Should().NotBeEmpty();
        foreach (GrammarRule rule in withPrecedence)
        {
            Grammar.Find(rule.PrecedenceSymbol).Should().NotBeNull();
            Grammar.Find(rule.PrecedenceSymbol).Precedence.Should().NotBeNull();
        }
    }

    [Fact]
    public void every_rule_has_a_unique_identity()
    {
        //Arrange / Act
        // The hand-ported actions are keyed on identity rather than on rule index,
        // because an index shifts the moment anything is inserted above it. A duplicate
        // identity would let two different actions collide on one key.
        HashSet<string> identities = new HashSet<string>();
        List<string> duplicates = new List<string>();

        foreach (GrammarRule rule in Grammar.Rules)
        {
            rule.Identity.Should().NotBeNull();
            if (!identities.Add(rule.Identity))
            {
                duplicates.Add(rule.Identity);
            }
        }

        //Assert
        duplicates.Should().BeEmpty();
        identities.Should().HaveCount(Grammar.Rules.Count);
    }

    [Fact]
    public void the_action_bodies_are_kept_verbatim_and_unparsed()
    {
        //Arrange / Act
        // They are C++ and they are hand-ported. The reader's job is to know a rule HAS
        // one and to key it, not to understand it -- so a body containing braces inside
        // a string or a character literal has to survive intact.
        int withAction = 0;
        foreach (GrammarRule rule in Grammar.Rules)
        {
            if (rule.ActionText != null)
            {
                withAction++;
            }
        }

        //Assert
        withAction.Should().BeGreaterThan(100);

        IReadOnlyList<GrammarRule> start = Grammar.RulesFor("start_symbol");
        start[1].ActionText.Should().Contain("pop_state");
        start[1].ActionText.Should().Contain("*retval = $3");
    }

    [Fact]
    public void an_unsupported_declaration_is_a_hard_error()
    {
        //Arrange
        // The whole point of generating in-repo is that a re-sync onto a grammar using
        // a new Bison feature fails LOUDLY at sync time. Skipping an unknown
        // declaration would change the language the parser accepts with nothing to say
        // so, which is the failure this design exists to prevent.
        const string Source = "%token FOO\n%glr-parser\n%%\na: FOO ;\n";

        //Act
        System.Action read = () => BisonGrammarReader.Read(Source);

        //Assert
        UnsupportedBisonFeatureException error
            = read.Should().Throw<UnsupportedBisonFeatureException>().Which;
        error.Feature.Should().Be("%glr-parser");
        error.Message.Should().Contain("BisonGrammarReader");
    }
}
