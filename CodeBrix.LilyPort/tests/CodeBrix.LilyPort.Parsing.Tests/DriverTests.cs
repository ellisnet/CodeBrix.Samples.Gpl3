// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Parsing.Actions;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lalr;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// The parsing engine, driven over the REAL LilyPond tables.
/// <para>
/// The lexer is not ported yet, so these feed token streams directly. That is the
/// right seam to test at anyway: it isolates the driver's behaviour — shifting,
/// reducing, the location stack, error recovery, and the lookahead access that
/// <c>MYBACKUP</c> needs — from whatever the scanner will eventually decide the tokens
/// are.
/// </para>
/// </summary>
public class DriverTests
{
    private static readonly ParseTables Tables = LalrGenerator.GenerateFromMirror();

    private static int Sym(string name)
    {
        for (int i = 0; i < Tables.Symbols.Count; i++)
        {
            if (string.Equals(Tables.Symbols[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException("no symbol named " + name);
    }

    /// <summary>A token source over a fixed list, with the pushback queue the grammar needs.</summary>
    private sealed class TokenList : IParserInput
    {
        private readonly Stack<ParserToken> _pushed = new Stack<ParserToken>();
        private readonly IReadOnlyList<ParserToken> _tokens;
        private int _index;

        internal TokenList(IReadOnlyList<ParserToken> tokens) => _tokens = tokens;

        public int Reads { get; private set; }

        public ParserToken Next()
        {
            Reads++;

            if (_pushed.Count > 0)
            {
                return _pushed.Pop();
            }

            return _index < _tokens.Count
                ? _tokens[_index++]
                : new ParserToken(0, null, default);
        }

        public void PushExtraToken(ParserToken token) => _pushed.Push(token);
    }

    private static TokenList Stream(params (string Symbol, object Value)[] tokens)
    {
        List<ParserToken> list = new List<ParserToken>();
        for (int i = 0; i < tokens.Length; i++)
        {
            list.Add(new ParserToken(
                Sym(tokens[i].Symbol),
                tokens[i].Value,
                new SourceSpan("<test>", 1, i + 1, 1, i + 2)));
        }

        return new TokenList(list);
    }

    [Fact]
    public void an_empty_input_parses_because_lilypond_may_be_empty()
    {
        //Arrange
        // `lilypond: /* empty */` is a real production, so an empty file is a valid
        // LilyPond file. If the driver could not accept immediately, nothing else here
        // would be reachable either.
        LalrParser parser = new LalrParser(Tables, new Dictionary<int, RuleAction>());

        //Act
        parser.Parse(Stream());

        //Assert
        parser.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void a_scheme_token_at_top_level_parses_and_the_action_sees_its_value()
    {
        //Arrange
        // toplevel_expression: SCM_TOKEN -- the shortest path through the grammar that
        // carries a semantic value, so it exercises shift, reduce, and the value stack
        // together.
        object seen = null;
        Dictionary<int, RuleAction> actions = new Dictionary<int, RuleAction>();

        foreach (TableRule rule in Tables.Rules)
        {
            if (rule.Source != null
                && rule.Source.LeftHandSide == "toplevel_expression"
                && rule.Source.RightHandSide.Count == 1
                && rule.Source.RightHandSide[0] == "SCM_TOKEN")
            {
                actions[rule.Index] = (context, values, locations, location) =>
                {
                    seen = values[0];
                    return values[0];
                };
            }
        }

        LalrParser parser = new LalrParser(Tables, actions);

        //Act
        parser.Parse(Stream(("SCM_TOKEN", "the-value")));

        //Assert
        parser.ErrorCount.Should().Be(0);
        seen.Should().Be("the-value");
    }

    [Fact]
    public void a_rule_action_receives_the_span_its_whole_right_hand_side_covers()
    {
        //Arrange
        // %locations is declared, and the actions read it -- a diagnostic that points
        // at the first token of a construct rather than the last is the whole reason
        // the location stack exists.
        SourceSpan seen = default;
        Dictionary<int, RuleAction> actions = new Dictionary<int, RuleAction>();

        foreach (TableRule rule in Tables.Rules)
        {
            if (rule.Source != null
                && rule.Source.LeftHandSide == "toplevel_expression"
                && rule.Source.RightHandSide.Count == 1
                && rule.Source.RightHandSide[0] == "SCM_TOKEN")
            {
                actions[rule.Index] = (context, values, locations, location) =>
                {
                    seen = location;
                    return null;
                };
            }
        }

        LalrParser parser = new LalrParser(Tables, actions);

        //Act
        parser.Parse(Stream(("SCM_TOKEN", "x")));

        //Assert
        seen.FileName.Should().Be("<test>");
        seen.StartColumn.Should().Be(1);
    }

    [Fact]
    public void an_action_can_push_the_lookahead_back_and_replace_it()
    {
        //Arrange
        // THE REASON THE DRIVER IS OURS. MYBACKUP and MYREPARSE push the pending
        // lookahead back into the lexer, put different tokens in front of it, and clear
        // the parser's lookahead so the next read comes from the lexer again. There are
        // 99 sites doing this in parser.yy, and a generated parser does not expose the
        // lookahead at all -- which is why option (b) was rejected in decision O7.
        //
        // The rule has to be one whose reduction HOLDS the lookahead — a state that
        // also has shift actions, so the token had to be read to rule them out.
        // number_term: number_factor is one: the parser must first see that no '*'
        // or '/' follows.
        Dictionary<int, RuleAction> actions = new Dictionary<int, RuleAction>();
        bool sawLookahead = false;
        bool firstFiring = true;

        foreach (TableRule rule in Tables.Rules)
        {
            if (rule.Source != null
                && rule.Source.LeftHandSide == "number_term"
                && rule.Source.RightHandSide.Count == 1
                && rule.Source.RightHandSide[0] == "number_factor")
            {
                actions[rule.Index] = (context, values, locations, location) =>
                {
                    if (firstFiring)
                    {
                        firstFiring = false;

                        // The reduce happened because the parser looked ahead, so
                        // there is one to see and to push back.
                        sawLookahead = context.HasLookahead;
                        context.PushBackLookahead();
                    }

                    return values[0];
                };
            }
        }

        LalrParser parser = new LalrParser(Tables, actions);

        // An assignment whose value is a number: `name = 3`.
        TokenList input = Stream(("STRING", "name"), ("'='", null), ("UNSIGNED", 3L));

        //Act
        parser.Parse(input);

        //Assert
        sawLookahead.Should().BeTrue();
        parser.ErrorCount.Should().Be(0);

        // The pushed-back token was read a second time, which is the observable effect.
        input.Reads.Should().BeGreaterThan(4);
    }

    [Fact]
    public void a_state_whose_only_action_is_its_default_reduction_reduces_without_reading_a_token()
    {
        //Arrange
        // Bison's yybackup first tries to decide WITHOUT the lookahead
        // (yypact_value_is_default), and parser.yy leans on that: rule actions switch
        // the LEXER'S MODE, so a token read before such a reduction would be lexed in
        // the old mode. assignment_id: STRING reduces in such a state -- a bare STRING
        // at top level can only open an assignment, so nothing needs the next token --
        // and at the moment its action runs, only the STRING itself may have been read.
        Dictionary<int, RuleAction> actions = new Dictionary<int, RuleAction>();
        int readsAtAction = -1;
        bool lookaheadAtAction = true;
        TokenList input = Stream(("STRING", "name"), ("'='", null), ("SCM_TOKEN", "v"));

        foreach (TableRule rule in Tables.Rules)
        {
            if (rule.Source != null
                && rule.Source.LeftHandSide == "assignment_id"
                && rule.Source.RightHandSide.Count == 1
                && rule.Source.RightHandSide[0] == "STRING")
            {
                actions[rule.Index] = (context, values, locations, location) =>
                {
                    readsAtAction = input.Reads;
                    lookaheadAtAction = context.HasLookahead;
                    return values[0];
                };
            }
        }

        LalrParser parser = new LalrParser(Tables, actions);

        //Act
        parser.Parse(input);

        //Assert
        // Exactly the one token the rule itself consumed -- the '=' has not been
        // touched -- and the action sees Bison's yychar == YYEMPTY, which is the case
        // MYBACKUP's own guard exists for.
        parser.ErrorCount.Should().Be(0);
        readsAtAction.Should().Be(1);
        lookaheadAtAction.Should().BeFalse();
    }

    [Fact]
    public void a_mode_switching_head_reduces_before_the_next_token_is_read()
    {
        //Arrange
        // THE REASON THE LAZY LOOKAHEAD IS BEHAVIOUR AND NOT OPTIMIZATION. The nine
        // mode-keyword heads (RAG12) push a lexer state from their action; upstream
        // reaches every one WITHOUT a lookahead read, so the token after the mode
        // keyword is lexed in the NEW mode. A driver that read ahead eagerly lexed
        // exactly one token in the old mode at every such site — the wave-2 finding
        // this test retires.
        Dictionary<int, RuleAction> actions = new Dictionary<int, RuleAction>(
            LilyPondRuleActions.Create().Bind(Tables));
        int readsAtModePush = -1;
        TokenList input = Stream(("LYRICMODE", null), ("'{'", null), ("'}'", null));

        foreach (TableRule rule in Tables.Rules)
        {
            if (rule.Source != null
                && string.Equals(rule.Source.Identity, "mode_changing_head: LYRICMODE", StringComparison.Ordinal))
            {
                RuleAction ported = actions[rule.Index];
                actions[rule.Index] = (context, values, locations, location) =>
                {
                    readsAtModePush = input.Reads;
                    return ported(context, values, locations, location);
                };
            }
        }

        LalrParser parser = new LalrParser(Tables, actions);

        //Act
        parser.Parse(input, new ScriptedParserHost());

        //Assert
        // Only the LYRICMODE keyword itself has been read when the push runs; the
        // '{' is still unlexed, so a real scanner would read it in lyric mode.
        parser.ErrorCount.Should().Be(0);
        readsAtModePush.Should().Be(1);
    }

    [Fact]
    public void a_syntax_error_is_reported_and_recovered_from()
    {
        //Arrange
        // `lilypond: lilypond error` is a real production: LilyPond recovers at top
        // level and keeps reading the file, which is why one bad bar does not cost you
        // the rest of the score. The driver has to reproduce that, not just stop.
        LalrParser parser = new LalrParser(Tables, new Dictionary<int, RuleAction>());

        //Act
        // A '}' at top level cannot start anything.
        parser.Parse(Stream(("'}'", null), ("SCM_TOKEN", "after")));

        //Assert
        parser.ErrorCount.Should().Be(1);
        parser.Diagnostics.Should().ContainSingle();
        parser.Diagnostics[0].Should().Contain("syntax error");
    }

    [Fact]
    public void one_mistake_reports_once_rather_than_cascading()
    {
        //Arrange
        // Bison stays quiet for three good shifts after an error, and the reason is
        // practical: without it a single misplaced brace reports on every token that
        // follows it.
        LalrParser parser = new LalrParser(Tables, new Dictionary<int, RuleAction>());

        //Act
        parser.Parse(Stream(("'}'", null), ("'}'", null), ("'}'", null), ("SCM_TOKEN", "x")));

        //Assert
        parser.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void the_tables_the_driver_runs_on_are_the_ones_bison_agreed_with()
    {
        //Arrange / Act / Assert
        // Guards against the driver being tested on tables that quietly stopped
        // matching the baseline.
        Tables.States.Should().HaveCount(913);
        Tables.Conflicts.Should().BeEmpty();
    }
}
