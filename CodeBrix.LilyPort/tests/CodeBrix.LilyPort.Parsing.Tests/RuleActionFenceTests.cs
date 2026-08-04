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
using CodeBrix.LilyPort.Parsing.Grammar;
using CodeBrix.LilyPort.Parsing.Lalr;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// The fence over the rule-action porting effort.
/// <para>
/// Decision O7 promises it: "the generator emits a manifest; a fence test asserts every
/// rule is implemented or on a recorded not-yet list". This is that test. It is the
/// same pattern as <c>TypePredicateTests</c> in the Engine, and it exists for the same
/// reason — an action that silently does not run is invisible, and so is a rule that
/// quietly stopped existing.
/// </para>
/// </summary>
public class RuleActionFenceTests
{
    private static readonly BisonGrammar Grammar = BisonGrammarReader.ReadMirroredGrammar();

    private static readonly ParseTables Tables = LalrGenerator.GenerateFromMirror();

    [Fact]
    public void the_manifest_lists_exactly_the_grammars_rules_in_order()
    {
        //Arrange
        // The manifest is committed data, and this is what makes a re-sync visible: a
        // production that appeared, vanished or changed shape shows up here rather than
        // as an action that never fires.
        IReadOnlyList<ManifestEntry> manifest = RuleManifest.Entries;

        //Act / Assert
        manifest.Should().HaveCount(Grammar.Rules.Count);

        List<string> mismatches = new List<string>();
        for (int i = 0; i < Grammar.Rules.Count; i++)
        {
            GrammarRule rule = Grammar.Rules[i];

            if (!string.Equals(manifest[i].Identity, rule.Identity, StringComparison.Ordinal))
            {
                mismatches.Add(
                    "rule " + i + ": manifest '" + manifest[i].Identity
                    + "' vs grammar '" + rule.Identity + "'");
            }
            else if (manifest[i].HasAction != (rule.ActionText != null))
            {
                mismatches.Add("rule " + i + " (" + rule.Identity + "): action flag differs");
            }
        }

        mismatches.Should().BeEmpty();
    }

    [Fact]
    public void the_manifest_records_the_size_of_the_porting_job()
    {
        //Arrange / Act
        int withAction = 0;
        foreach (ManifestEntry entry in RuleManifest.Entries)
        {
            if (entry.HasAction)
            {
                withAction++;
            }
        }

        //Assert
        // 479 action bodies to hand-port, out of 616 productions. Most are thin: 71 of
        // the action sites dispatch through MAKE_SYNTAX into
        // scm/ly-syntax-constructors.scm, which is already vendored.
        RuleManifest.Entries.Should().HaveCount(616);
        withAction.Should().Be(479);
    }

    [Fact]
    public void every_rule_is_either_ported_or_on_the_outstanding_list()
    {
        //Arrange
        // The fence itself. NotYetPorted is COMPUTED from the manifest rather than
        // maintained by hand, so it cannot drift out of date -- porting an action
        // removes it from the list by construction, and a rule that stops existing
        // cannot linger on it.
        RuleActionTable table = LilyPondRuleActions.Create();

        //Act
        IReadOnlyList<string> outstanding = table.NotYetPorted();

        //Assert
        (table.Implemented.Count + outstanding.Count).Should().Be(479);
    }

    /// <summary>
    /// The rule action groups that have been DECLARED COMPLETE, each with every
    /// nonterminal it owns (including its rules' mid-rule <c>$@N</c> actions). A
    /// finished porting session appends its group's row; the theory below then keeps
    /// it complete forever — an action that later went missing fails here by name.
    /// </summary>
    public static TheoryData<string, string[]> CompletedGroups => new TheoryData<string, string[]>
    {
        {
            "RAG1 — top level, identifiers and headers",
            new[]
            {
                "start_symbol", "lilypond", "toplevel_expression", "lookup",
                "lilypond_header_body", "lilypond_header", "header_block",
                "header_modification", "assignment_id", "assignment",
                "identifier_init", "identifier_init_nonumber",
                "$@1", "$@2", "$@3",
            }
        },
        {
            "RAG2 — embedded Scheme and embedded LilyPond",
            new[]
            {
                "embedded_scm_bare", "embedded_scm_bare_arg", "embedded_scm",
                "embedded_scm_active", "embedded_scm_arg", "scm_function_call",
                "embedded_lilypond_number", "embedded_lilypond",
            }
        },
        {
            "RAG3 — book, bookpart and score blocks",
            new[]
            {
                "book_block", "book_body", "bookpart_block", "bookpart_body",
                "score_block", "score_body", "score_item", "score_items",
                "$@5", "$@6", "$@7",
            }
        },
        {
            "RAG4 — output definitions, paper and tempo",
            new[]
            {
                "music_or_context_def", "output_def", "output_def_body",
                "output_def_head", "output_def_head_with_mode_switch",
                "paper_block", "tempo_event", "tempo_range",
                "$@8",
            }
        },
        {
            "RAG5 — context definitions and modifications",
            new[]
            {
                "context_change", "context_def_mod", "context_def_spec_block",
                "context_def_spec_body", "context_mod", "context_mod_arg",
                "context_mod_list", "context_modification", "context_modification_arg",
                "context_modification_mods_list", "context_prefix",
                "optional_context_mods",
                "$@4", "$@9",
            }
        },
        {
            "RAG6 — core music assembly",
            new[]
            {
                "music_list", "braced_music_list", "music", "pitch_as_music",
                "music_embedded", "music_embedded_backup", "music_assign",
                "repeated_music", "alternative_music", "sequential_music",
                "simultaneous_music", "simple_music", "new_lyrics", "basic_music",
                "contextable_music", "contexted_basic_music", "composite_music",
                "music_bare", "grouped_music_list",
            }
        },
        {
            "RAG7 — symbol lists, property paths and overrides",
            new[]
            {
                "symbol_list_arg", "symbol_list_rev", "symbol_list_part",
                "symbol_list_element", "symbol_list_part_bare", "property_path",
                "property_operation", "revert_arg", "revert_arg_backup",
                "revert_arg_part", "grob_prop_spec", "grob_prop_path",
                "context_prop_spec", "simple_revert_context", "music_property_def",
            }
        },
        {
            "RAG8 — music-function arglists: non-backup",
            new[]
            {
                "function_arglist_nonbackup", "function_arglist_nonbackup_reparse",
                "reparsed_rhythm",
            }
        },
        {
            "RAG9 — music-function arglists: backup",
            new[]
            {
                "function_arglist_backup",
            }
        },
        {
            "RAG10 — music-function arglists: common, partial and the call",
            new[]
            {
                "function_arglist", "function_arglist_skip_nonbackup",
                "function_arglist_partial", "function_arglist_partial_optional",
                "function_arglist_common", "function_arglist_common_reparse",
                "function_arglist_optional", "function_arglist_skip_backup",
                "music_function_call",
            }
        },
        {
            "RAG11 — partial functions, \\etc",
            new[]
            {
                "partial_function", "partial_function_scriptable",
            }
        },
        {
            "RAG12 — mode changes and lyric mode",
            new[]
            {
                "optional_id", "lyric_mode_music", "mode_changed_music",
                "mode_changing_head", "mode_changing_head_with_context",
                "lyric_element", "lyric_element_music",
                "$@10",
            }
        },
        {
            "RAG13 — strings, scalars and numbers",
            new[]
            {
                "text", "simple_string", "symbol", "scalar", "number_expression",
                "number_term", "number_factor", "bare_number_common", "bare_number",
                "exact_unsigned_number", "unsigned_integer", "exclamations",
                "questions", "string",
            }
        },
        {
            "RAG14 — chords and event chords",
            new[]
            {
                "event_chord", "note_chord_element", "chord_body", "chord_body_elements",
                "chord_body_element", "music_function_chord_body", "event_function_event",
                "new_chord", "chord_items", "chord_separator", "chord_item",
                "step_numbers", "step_number",
            }
        },
        {
            "RAG15 — post events, scripts and text attachments",
            new[]
            {
                "post_events", "post_event", "post_event_nofinger",
                "string_number_event", "direction_less_event", "direction_reqd_event",
                "gen_text_def", "fingering", "script_abbreviation", "script_dir",
            }
        },
        {
            "RAG16 — pitches, octaves and durations",
            new[]
            {
                "octave_check", "quotes", "erroneous_quotes", "sup_quotes", "sub_quotes",
                "steno_pitch", "steno_tonic_pitch", "pitch", "pitch_or_tonic_pitch",
                "maybe_notemode_duration", "optional_notemode_duration",
                "steno_duration", "duration", "dots", "multiplier_scm", "multipliers",
                "tremolo_type", "optional_rest", "pitch_or_music", "simple_element",
            }
        },
        {
            "RAG17 — figured bass",
            new[]
            {
                "bass_number", "bass_figure", "figured_bass_modification",
                "br_bass_figure", "figure_list",
            }
        },
        {
            "RAG18 — markup: modes, lists and structure",
            new[]
            {
                "full_markup_list", "markup_mode", "markup_mode_word", "full_markup",
                "partial_markup", "markup_top", "markup_scm", "markup_list",
                "markup_uncomposed_list", "markup_composed_list", "markup_braced_list",
                "markup_braced_list_body", "markup_word", "simple_markup",
                "simple_markup_noword", "markup",
                "$@11", "$@12", "$@13", "$@15",
            }
        },
        {
            "RAG19 — markup: commands and their argument lists",
            new[]
            {
                "markup_command_list", "markup_command_basic_arguments",
                "markup_command_list_arguments", "markup_partial_function",
                "markup_arglist_partial", "markup_head_1_item", "markup_head_1_list",
                "$@14",
            }
        },
    };

    [Theory]
    [MemberData(nameof(CompletedGroups))]
    public void a_completed_rule_action_group_has_nothing_outstanding(string group, string[] nonterminals)
    {
        //Arrange
        RuleActionTable table = LilyPondRuleActions.Create();
        HashSet<string> owned = new HashSet<string>(nonterminals, StringComparer.Ordinal);

        //Act
        List<string> outstanding = new List<string>();
        foreach (string identity in table.NotYetPorted())
        {
            string leftHandSide = identity.Substring(0, identity.IndexOf(':'));
            if (owned.Contains(leftHandSide))
            {
                outstanding.Add(identity);
            }
        }

        //Assert
        outstanding.Should().BeEmpty("the group '{0}' is declared complete", group);
    }

    [Fact]
    public void nothing_is_outstanding__every_action_body_in_the_grammar_is_ported()
    {
        //Arrange
        // THE END OF THE PORTING EFFORT, and from here on a REGRESSION FENCE. The
        // nineteen rule action groups are all landed, so the worklist NotYetPorted
        // computes from the manifest is empty — and it must stay empty. An action
        // deleted by mistake, or a re-sync that introduces a production nobody has
        // ported, fails here BY NAME rather than showing up as a rule that silently
        // reduces to Bison's default $$ = $1.
        RuleActionTable table = LilyPondRuleActions.Create();

        //Act
        IReadOnlyList<string> outstanding = table.NotYetPorted();

        //Assert
        outstanding.Should().BeEmpty();
        table.Implemented.Should().HaveCount(479);
    }

    [Fact]
    public void every_ported_action_names_a_rule_the_grammar_actually_has()
    {
        //Arrange
        // THE CHECK THAT EARNS ITS KEEP ON A RE-SYNC. An action registered for a
        // production the grammar no longer has would never run, and nothing else would
        // say so. Bind refuses rather than dropping it.
        RuleActionTable table = LilyPondRuleActions.Create();

        //Act
        Action bind = () => table.Bind(Tables);

        //Assert
        bind.Should().NotThrow();
    }

    [Fact]
    public void an_action_registered_for_a_production_that_does_not_exist_is_refused()
    {
        //Arrange
        // Proving the fence bites, rather than trusting that it would.
        RuleActionTable table = new RuleActionTable();
        table.Add("no_such_rule: nothing at all", (context, values, locations, location) => null);

        //Act
        Action bind = () => table.Bind(Tables);

        //Assert
        bind.Should().Throw<InvalidOperationException>()
            .WithMessage("*re-synced*");
    }

    [Fact]
    public void the_same_rule_cannot_be_registered_twice()
    {
        //Arrange
        RuleActionTable table = new RuleActionTable();
        table.Add("lilypond: lilypond assignment", (context, values, locations, location) => null);

        //Act
        Action again = () => table.Add(
            "lilypond: lilypond assignment",
            (context, values, locations, location) => null);

        //Assert
        again.Should().Throw<InvalidOperationException>();
    }
}
