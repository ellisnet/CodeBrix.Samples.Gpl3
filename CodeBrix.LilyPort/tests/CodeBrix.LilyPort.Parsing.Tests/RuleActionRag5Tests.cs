// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Parsing.Actions;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lalr;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// RULE ACTION GROUP 5 — the context definition and modification actions, exercised
/// two ways: whole inputs through the REAL scanner and tables with a scripted host
/// (<c>\new ... \with { ... }</c> and <c>foo = \context { ... }</c> both work
/// end-to-end because the RAG1 spine is ported), and direct invocation for the rules
/// whose surrounding grammar is not ported yet (<c>composite_music</c> is RAG6,
/// <c>property_operation</c> RAG7).
/// </summary>
public class RuleActionRag5Tests
{
    private static readonly ParseTables Tables = LalrGenerator.GenerateFromMirror();

    private static readonly IReadOnlyDictionary<int, RuleAction> Bound
        = LilyPondRuleActions.Create().Bind(Tables);

    private static RuleAction Action(string identity)
    {
        foreach (TableRule rule in Tables.Rules)
        {
            if (rule.Source != null
                && string.Equals(rule.Source.Identity, identity, StringComparison.Ordinal))
            {
                return Bound[rule.Index];
            }
        }

        throw new InvalidOperationException("no rule named " + identity);
    }

    private static ParseContext NewContext(ScriptedParserHost host)
        => new ParseContext(
            new LalrParser(Tables, new Dictionary<int, RuleAction>()),
            new TokenListInput())
        {
            UserState = host,
        };

    private static (LalrParser Parser, ModalScanner Scanner, ScriptedParserHost Host) Setup(string input)
    {
        ScriptedParserHost host = new ScriptedParserHost();
        host.Keywords["accepts"] = ("ACCEPTS", null);
        host.Keywords["consists"] = ("CONSISTS", null);
        host.Keywords["context"] = ("CONTEXT", null);
        host.Keywords["defaultchild"] = ("DEFAULTCHILD", null);
        host.Keywords["name"] = ("NAME", null);
        host.Keywords["new"] = ("NEWCONTEXT", null);
        host.Keywords["with"] = ("WITH", null);

        ModalScanner scanner = new ModalScanner(LilyPondLexerRules.Create(host), input, "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);

        LalrParser parser = new LalrParser(Tables, Bound);
        return (parser, scanner, host);
    }

    private static ContextMod ModOf(params object[] mods)
    {
        ContextMod result = new ContextMod();
        foreach (object mod in mods)
        {
            result.AddContextMod(mod);
        }

        return result;
    }

    [Fact]
    public void a_new_context_with_a_with_block_parses_from_real_text()
    {
        //Arrange
        // \new Staff \with { \consists "Foo_engraver" } { } — the whole \with
        // machinery runs: $@9 pushes note state, context_mod_list accumulates into a
        // ContextMod, optional_context_mods flattens, and context_prefix conses the
        // context-create constructor WITHOUT calling it (START_MAKE_SYNTAX). Since
        // RAG6 landed, the surrounding contexted_basic_music FINISHES the prefix
        // (FINISH_MAKE_SYNTAX), so the APPLIED constructor arrives at the toplevel
        // music handler with the braced music as its final argument.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\new Staff \\with { \\consists \"Foo_engraver\" } { }");
        host.Globals.Bindings[Symbol.Intern("toplevel-music-handler")] = "music-proc";

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.LexerModeOperations.AsText().Should().Equal("push-note-state", "pop-state");

        (object procedure, object[] arguments) = host.Calls[host.Calls.Count - 1];
        procedure.AsText().Should().Be("music-proc");

        SyntaxMark applied = (SyntaxMark)arguments[0];
        applied.Name.Should().Be("constructor:context-create");
        applied.Arguments.Should().HaveCount(4);
        applied.Arguments[0].Should().BeSameAs(Symbol.Intern("Staff"));

        // Arguments[1] is optional_id, whose action is RAG12 and not pinned here.
        List<object> mods = Pair.ToList(applied.Arguments[2]);
        mods.Should().HaveCount(1);
        List<object> entry = Pair.ToList(mods[0]);
        entry[0].Should().BeSameAs(Symbol.Intern("consists"));
        entry[1].AsText().Should().Be("Foo_engraver");

        ((SyntaxMark)applied.Arguments[3]).Name.Should().Be("sequential-music");
    }

    [Fact]
    public void a_context_definition_assigned_from_real_text_lands_as_a_context_def()
    {
        //Arrange
        // foo = \context { ... } goes assignment -> identifier_init_nonumber ->
        // context_def_spec_block, so the finished ContextDef lands in the identifier
        // table with every mod applied.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup(
            "foo = \\context { \\name \"MyStaff\" \\consists \"Bar_engraver\""
            + " \\accepts \"Voice\" \\defaultchild \"Voice\" }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);

        ContextDef def = (ContextDef)host.Globals.Bindings[Symbol.Intern("foo")];
        def.ContextName.Should().BeSameAs(Symbol.Intern("MyStaff"));
        Pair.ToList(def.GetTranslatorNames(Nil.Instance))
            .AsText().Should().Equal(Symbol.Intern("Bar_engraver"));
        Pair.ToList(def.Acceptance.GetList()).AsText().Should().Equal(Symbol.Intern("Voice"));
        def.Acceptance.GetDefault().Should().BeSameAs(Symbol.Intern("Voice"));
        def.Origin.Should().BeOfType<SourceSpan>();
    }

    [Fact]
    public void an_empty_context_block_still_becomes_a_context_def_with_its_origin()
    {
        //Arrange
        // context_def_spec_body: /* empty */ yields SCM_UNSPECIFIED, so the block
        // action must create the definition itself and stamp @$ on it.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object body = Action("context_def_spec_body: /* empty */")(
            context, new object[0], new SourceSpan[0], default);

        //Act
        object result = Action("context_def_spec_block: CONTEXT '{' context_def_spec_body '}'")(
            context,
            new object[] { "CONTEXT", '{', body, '}' },
            new SourceSpan[4],
            new SourceSpan("<test>", 1, 1, 1, 20));

        //Assert
        body.Should().BeSameAs(Unspecified.Instance);
        ContextDef def = (ContextDef)result;
        def.Origin.Should().BeOfType<SourceSpan>();
    }

    [Fact]
    public void a_context_mod_arg_wraps_its_music_in_note_state()
    {
        //Arrange
        // context_mod_arg: { push_note_state } composite_music { pop_state; $$ = $2 }
        // — invoked directly because composite_music's actions are RAG6.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object music = new object();

        //Act
        object pushed = Action("$@4: /* empty */")(
            context, new object[0], new SourceSpan[0], default);
        object result = Action("context_mod_arg: $@4 composite_music")(
            context,
            new object[] { pushed, music },
            new SourceSpan[2],
            default);

        //Assert
        result.Should().BeSameAs(music);
        host.LexerModeOperations.AsText().Should().Equal("push-note-state", "pop-state");
    }

    [Fact]
    public void a_context_def_body_ignores_unspecified_and_adopts_a_whole_definition()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        RuleAction action = Action("context_def_spec_body: context_def_spec_body context_mod_arg");
        ContextDef supplied = new ContextDef();

        //Act
        object untouched = action(
            context,
            new object[] { Unspecified.Instance, Unspecified.Instance },
            new SourceSpan[2],
            default);
        object adopted = action(
            context,
            new object[] { Unspecified.Instance, supplied },
            new SourceSpan[2],
            default);

        //Assert
        untouched.Should().BeSameAs(Unspecified.Instance);
        adopted.Should().BeSameAs(supplied);
        context.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void a_context_def_body_merges_a_context_mods_entries()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        ContextMod mod = ModOf(Pair.List(Symbol.Intern("consists"), "X_engraver"));

        //Act
        object result = Action("context_def_spec_body: context_def_spec_body context_mod_arg")(
            context,
            new object[] { Unspecified.Instance, mod },
            new SourceSpan[2],
            default);

        //Assert
        ContextDef def = (ContextDef)result;
        Pair.ToList(def.GetTranslatorNames(Nil.Instance))
            .AsText().Should().Equal(Symbol.Intern("X_engraver"));
    }

    [Fact]
    public void music_inside_a_context_block_goes_through_the_music_handler()
    {
        //Arrange
        // The handler is scripted to answer SCM_UNSPECIFIED (the recording Call), so
        // the faithful outcome HERE is "not a context mod" — what matters is that the
        // music reached context-mod-music-handler at all.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        host.Globals.Bindings[Symbol.Intern("context-mod-music-handler")] = "handler-proc";
        MusicObject music = new MusicObject(Nil.Instance);

        //Act
        object result = Action("context_def_spec_body: context_def_spec_body context_mod_arg")(
            context,
            new object[] { Unspecified.Instance, music },
            new SourceSpan[2],
            default);

        //Assert
        host.Calls.Should().HaveCount(1);
        host.Calls[0].Procedure.AsText().Should().Be("handler-proc");
        host.Calls[0].Arguments[0].Should().BeSameAs(music);
        context.ErrorCount.Should().Be(1);
        host.ErrorLevel.Should().Be(1);
        result.Should().BeOfType<ContextDef>();
    }

    [Fact]
    public void a_single_mod_lands_in_the_definition_unless_undefined()
    {
        //Arrange
        // context_def_spec_body: context_def_spec_body context_mod — SCM_UNDEFINED
        // (an errored mod) is skipped without creating a definition.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        RuleAction action = Action("context_def_spec_body: context_def_spec_body context_mod");

        //Act
        object skipped = action(
            context,
            new object[] { Unspecified.Instance, DefaultArgument.Instance },
            new SourceSpan[2],
            default);
        object built = action(
            context,
            new object[] { Unspecified.Instance, Pair.List(Symbol.Intern("context-name"), "Foo") },
            new SourceSpan[2],
            default);

        //Assert
        skipped.Should().BeSameAs(Unspecified.Instance);
        ((ContextDef)built).ContextName.Should().BeSameAs(Symbol.Intern("Foo"));
    }

    [Fact]
    public void a_with_block_inside_a_context_block_merges_every_mod()
    {
        //Arrange
        // context_def_spec_body: context_def_spec_body context_modification — the
        // \with { } value is always a ContextMod, and each of its mods is applied.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        ContextMod mod = ModOf(
            Pair.List(Symbol.Intern("consists"), "A_engraver"),
            Pair.List(Symbol.Intern("alias"), Symbol.Intern("Staff")));

        //Act
        object result = Action("context_def_spec_body: context_def_spec_body context_modification")(
            context,
            new object[] { Unspecified.Instance, mod },
            new SourceSpan[2],
            default);

        //Assert
        ContextDef def = (ContextDef)result;
        Pair.ToList(def.GetTranslatorNames(Nil.Instance))
            .AsText().Should().Equal(Symbol.Intern("A_engraver"));
        Pair.ToList(def.ContextAliases).AsText().Should().Equal(Symbol.Intern("Staff"));
    }

    [Fact]
    public void a_with_of_a_context_mod_or_unspecified_needs_no_conversion()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        RuleAction action = Action("context_modification: WITH context_modification_arg");
        ContextMod mod = new ContextMod();

        //Act
        object passed = action(
            context,
            new object[] { "WITH", mod },
            new SourceSpan[2],
            default);

        // let's permit \with #*unspecified* to go for an empty context mod
        object empty = action(
            context,
            new object[] { "WITH", Unspecified.Instance },
            new SourceSpan[2],
            default);

        //Assert
        passed.Should().BeSameAs(mod);
        empty.Should().BeOfType<ContextMod>();
        ((ContextMod)empty).GetMods().Should().BeSameAs(Nil.Instance);
        context.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void a_with_of_anything_else_is_not_a_context_mod()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("context_modification: WITH context_modification_arg")(
            context,
            new object[] { "WITH", 42L },
            new SourceSpan[2],
            default);

        //Assert
        context.ErrorCount.Should().Be(1);
        host.ErrorLevel.Should().Be(1);
        result.Should().BeOfType<ContextMod>();
    }

    [Fact]
    public void music_after_with_goes_through_the_music_handler()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        host.Globals.Bindings[Symbol.Intern("context-mod-music-handler")] = "handler-proc";
        MusicObject music = new MusicObject(Nil.Instance);

        //Act
        object result = Action("context_modification: WITH context_modification_arg")(
            context,
            new object[] { "WITH", music },
            new SourceSpan[2],
            default);

        //Assert
        host.Calls.Should().HaveCount(1);
        host.Calls[0].Arguments[0].Should().BeSameAs(music);

        // The scripted handler answers SCM_UNSPECIFIED, which \with permits as an
        // empty mod — no error.
        context.ErrorCount.Should().Be(0);
        result.Should().BeOfType<ContextMod>();
    }

    [Fact]
    public void a_with_brace_list_starts_empty_and_accumulates_mods()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object mod = Pair.List(Symbol.Intern("consists"), "X_engraver");

        //Act
        object list = Action("context_mod_list: /* empty */")(
            context, new object[0], new SourceSpan[0], default);
        object grown = Action("context_mod_list: context_mod_list context_mod")(
            context,
            new object[] { list, mod },
            new SourceSpan[2],
            default);
        object unchanged = Action("context_mod_list: context_mod_list context_mod")(
            context,
            new object[] { grown, DefaultArgument.Instance },
            new SourceSpan[2],
            default);

        //Assert
        unchanged.Should().BeSameAs(list);
        List<object> mods = Pair.ToList(((ContextMod)list).GetMods());
        mods.Should().HaveCount(1);
        mods[0].Should().BeSameAs(mod);
    }

    [Fact]
    public void a_context_mod_arg_in_a_with_list_merges_mods_or_errors()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        RuleAction action = Action("context_mod_list: context_mod_list context_mod_arg");
        ContextMod list = new ContextMod();
        ContextMod source = ModOf(Pair.List(Symbol.Intern("consists"), "X_engraver"));

        //Act
        object merged = action(
            context,
            new object[] { list, source },
            new SourceSpan[2],
            default);
        action(
            context,
            new object[] { list, 42L },
            new SourceSpan[2],
            default);

        //Assert
        merged.Should().BeSameAs(list);
        Pair.ToList(list.GetMods()).Should().HaveCount(1);
        context.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void optional_context_mods_flattens_the_reversed_list_of_mod_lists()
    {
        //Arrange
        // The worker conses NEWEST FIRST, so ((b1 b2) (a1)) must come out (a1 b1 b2)
        // — reversed, then appended destructively (safe: get_mods copies).
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object a1 = Pair.List(Symbol.Intern("consists"), "A");
        object b1 = Pair.List(Symbol.Intern("consists"), "B");
        object b2 = Pair.List(Symbol.Intern("remove"), "C");
        object reversedLists = Pair.List(Pair.List(b1, b2), Pair.List(a1));
        RuleAction action = Action("optional_context_mods: context_modification_mods_list");

        //Act
        object flattened = action(
            context,
            new object[] { reversedLists },
            new SourceSpan[1],
            default);
        object empty = action(
            context,
            new object[] { Nil.Instance },
            new SourceSpan[1],
            default);

        //Assert
        List<object> mods = Pair.ToList(flattened);
        mods.Should().HaveCount(3);
        mods[0].Should().BeSameAs(a1);
        mods[1].Should().BeSameAs(b1);
        mods[2].Should().BeSameAs(b2);
        empty.Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void the_mods_list_worker_records_each_with_blocks_mods()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object mod = Pair.List(Symbol.Intern("consists"), "X_engraver");
        ContextMod block = ModOf(mod);
        RuleAction action = Action(
            "context_modification_mods_list: context_modification_mods_list context_modification");

        //Act
        object grown = action(
            context,
            new object[] { Nil.Instance, block },
            new SourceSpan[2],
            default);
        object unchanged = action(
            context,
            new object[] { grown, "not a mod" },
            new SourceSpan[2],
            default);

        //Assert
        Pair outer = (Pair)grown;
        Pair.ToList(outer.Car).Should().Equal(mod);
        outer.Cdr.Should().BeSameAs(Nil.Instance);
        unchanged.Should().BeSameAs(grown);
    }

    [Fact]
    public void the_context_prefix_packs_the_constructor_without_calling_it()
    {
        //Arrange
        // START_MAKE_SYNTAX conses the procedure onto its first arguments; RAG6's
        // contexted_basic_music finishes the call once the music exists.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object mods = Pair.List(Pair.List(Symbol.Intern("consists"), "X"));

        //Act
        object result = Action("context_prefix: CONTEXT symbol optional_id optional_context_mods")(
            context,
            new object[] { "CONTEXT", Symbol.Intern("Staff"), "the-id", mods },
            new SourceSpan[4],
            default);

        //Assert
        List<object> packed = Pair.ToList(result);
        packed.Should().HaveCount(4);
        packed[0].AsText().Should().Be("constructor:context-find-or-create");
        packed[1].Should().BeSameAs(Symbol.Intern("Staff"));
        packed[2].AsText().Should().Be("the-id");
        packed[3].Should().BeSameAs(mods);
        host.Calls.Should().BeEmpty();
    }

    [Fact]
    public void a_context_change_dispatches_to_the_syntax_constructor()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("context_change: CHANGE symbol '=' simple_string")(
            context,
            new object[] { "CHANGE", Symbol.Intern("Staff"), '=', "up" },
            new SourceSpan[4],
            default);

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("context-change");
        mark.Arguments[0].Should().BeSameAs(Symbol.Intern("Staff"));
        mark.Arguments[1].AsText().Should().Be("up");
    }

    [Fact]
    public void every_context_def_mod_keyword_interns_its_tag_symbol()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        (string Identity, string Tag)[] keywords =
        {
            ("context_def_mod: CONSISTS", "consists"),
            ("context_def_mod: REMOVE", "remove"),
            ("context_def_mod: ACCEPTS", "accepts"),
            ("context_def_mod: DEFAULTCHILD", "default-child"),
            ("context_def_mod: DENIES", "denies"),
            ("context_def_mod: ALIAS", "alias"),
            ("context_def_mod: TYPE", "translator-type"),
            ("context_def_mod: DESCRIPTION", "description"),
            ("context_def_mod: NAME", "context-name"),
        };

        foreach ((string identity, string tag) in keywords)
        {
            //Act
            object result = Action(identity)(
                context,
                new object[] { "KEYWORD" },
                new SourceSpan[1],
                default);

            //Assert
            result.Should().BeSameAs(Symbol.Intern(tag));
        }
    }

    [Fact]
    public void a_context_mod_pairs_the_tag_with_its_argument()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object operation = Pair.List(Symbol.Intern("assign"), Symbol.Intern("x"), 1L);

        //Act
        object passed = Action("context_mod: property_operation")(
            context,
            new object[] { operation },
            new SourceSpan[1],
            default);
        object paired = Action("context_mod: context_def_mod STRING")(
            context,
            new object[] { Symbol.Intern("consists"), "X_engraver" },
            new SourceSpan[2],
            default);

        //Assert
        passed.Should().BeSameAs(operation);
        List<object> entry = Pair.ToList(paired);
        entry[0].Should().BeSameAs(Symbol.Intern("consists"));
        entry[1].AsText().Should().Be("X_engraver");
    }

    [Fact]
    public void only_consists_and_remove_take_a_non_string_scheme_argument()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        RuleAction action = Action("context_mod: context_def_mod embedded_scm");

        //Act
        object allowed = action(
            context,
            new object[] { Symbol.Intern("consists"), Symbol.Intern("Some_translator") },
            new SourceSpan[2],
            default);
        object stringy = action(
            context,
            new object[] { Symbol.Intern("accepts"), "Voice" },
            new SourceSpan[2],
            default);
        object refused = action(
            context,
            new object[] { Symbol.Intern("accepts"), 42L },
            new SourceSpan[2],
            default);

        //Assert
        Pair.ToList(allowed)
            .AsText().Should().Equal(Symbol.Intern("consists"), Symbol.Intern("Some_translator"));
        Pair.ToList(stringy).AsText().Should().Equal(Symbol.Intern("accepts"), "Voice");
        refused.Should().BeSameAs(Nil.Instance);
        context.ErrorCount.Should().Be(1);
        host.ErrorLevel.Should().Be(1);
    }
}
