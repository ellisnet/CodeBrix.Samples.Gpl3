// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The context definition types RULE ACTION GROUP 5 brought in:
/// <see cref="ContextDef"/>, <see cref="ContextMod"/> and
/// <see cref="AcceptanceSet"/> — the containers <c>\context { ... }</c> and
/// <c>\with { ... }</c> build. These pin the DATA semantics (mod dispatch, the
/// acceptance ordering rules, translator-list resolution) ahead of the session that
/// wires definitions into context creation.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class ContextDefTests
{
    private static Symbol Sym(string name) => Symbol.Intern(name);

    private static object Mod(string tag, object argument)
        => Pair.List(Sym(tag), argument);

    [Fact]
    public void an_empty_context_mod_has_no_mods()
    {
        //Arrange
        ContextMod mod = new ContextMod();

        //Act
        object mods = mod.GetMods();

        //Assert
        mods.Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void mods_come_back_in_written_order_as_a_fresh_list_each_time()
    {
        //Arrange
        // parser.yy's optional_context_mods comment leans on this: get_mods creates
        // fresh copies, so the grammar may append! the results together.
        ContextMod mod = new ContextMod();
        object first = Mod("consists", "A_engraver");
        object second = Mod("remove", "B_engraver");

        //Act
        mod.AddContextMod(first);
        mod.AddContextMod(second);
        object once = mod.GetMods();
        object again = mod.GetMods();

        //Assert
        List<object> items = Pair.ToList(once);
        items.Should().HaveCount(2);
        items[0].Should().BeSameAs(first);
        items[1].Should().BeSameAs(second);
        again.Should().NotBeSameAs(once);
    }

    [Fact]
    public void the_list_constructor_preserves_written_order()
    {
        //Arrange
        object first = Mod("consists", "A_engraver");
        object second = Mod("consists", "B_engraver");

        //Act
        ContextMod mod = new ContextMod(Pair.List(first, second));

        //Assert
        List<object> items = Pair.ToList(mod.GetMods());
        items[0].Should().BeSameAs(first);
        items[1].Should().BeSameAs(second);
    }

    [Fact]
    public void a_copied_context_mod_diverges_from_its_source_on_later_adds()
    {
        //Arrange
        ContextMod original = new ContextMod();
        original.AddContextMod(Mod("consists", "A_engraver"));

        //Act
        ContextMod copy = new ContextMod(original);
        copy.AddContextMod(Mod("consists", "B_engraver"));

        //Assert
        Pair.ToList(original.GetMods()).Should().HaveCount(1);
        Pair.ToList(copy.GetMods()).Should().HaveCount(2);
    }

    [Fact]
    public void add_context_mods_merges_a_list_in_order()
    {
        //Arrange
        ContextMod mod = new ContextMod();
        object first = Mod("consists", "A_engraver");
        object second = Mod("remove", "B_engraver");

        //Act
        mod.AddContextMods(Pair.List(first, second));

        //Assert
        List<object> items = Pair.ToList(mod.GetMods());
        items[0].Should().BeSameAs(first);
        items[1].Should().BeSameAs(second);
    }

    [Fact]
    public void accepted_items_go_to_the_front_but_never_in_front_of_the_default()
    {
        //Arrange
        AcceptanceSet acceptance = new AcceptanceSet();

        //Act
        acceptance.AcceptDefault(Sym("Voice"));
        acceptance.Accept(Sym("Staff"));
        acceptance.Accept(Sym("Lyrics"));

        //Assert
        // The default stays first; each later \accepts lands right behind it.
        Pair.ToList(acceptance.GetList())
            .Should().Equal(Sym("Voice"), Sym("Lyrics"), Sym("Staff"));
        acceptance.GetDefault().Should().BeSameAs(Sym("Voice"));
    }

    [Fact]
    public void accepting_the_default_again_has_no_effect()
    {
        //Arrange
        AcceptanceSet acceptance = new AcceptanceSet();
        acceptance.AcceptDefault(Sym("Voice"));

        //Act
        acceptance.Accept(Sym("Voice"));

        //Assert
        Pair.ToList(acceptance.GetList()).Should().Equal(Sym("Voice"));
    }

    [Fact]
    public void denying_removes_the_item_and_clears_a_denied_default()
    {
        //Arrange
        AcceptanceSet acceptance = new AcceptanceSet();
        acceptance.AcceptDefault(Sym("Voice"));
        acceptance.Accept(Sym("Staff"));

        //Act
        acceptance.Deny(Sym("Voice"));

        //Assert
        Pair.ToList(acceptance.GetList()).Should().Equal(Sym("Staff"));
        acceptance.HasDefault.Should().BeFalse();
    }

    [Fact]
    public void a_shallow_copy_diverges_from_its_source()
    {
        //Arrange
        AcceptanceSet original = new AcceptanceSet();
        original.Accept(Sym("Staff"));

        //Act
        AcceptanceSet copy = AcceptanceSet.ShallowCopy(original);
        copy.Accept(Sym("Lyrics"));

        //Assert
        Pair.ToList(original.GetList()).Should().Equal(Sym("Staff"));
        Pair.ToList(copy.GetList()).Should().Equal(Sym("Lyrics"), Sym("Staff"));
    }

    [Fact]
    public void a_new_definition_is_named_by_the_empty_symbol()
    {
        //Arrange & Act
        ContextDef def = new ContextDef();

        //Assert
        def.ContextName.Should().BeSameAs(Sym(""));
        def.ContextAliases.Should().BeSameAs(Nil.Instance);
        def.PropertyOps.Should().BeSameAs(Nil.Instance);
        def.Description.Should().BeSameAs(Nil.Instance);
        def.GrobDescriptions.Should().BeSameAs(Nil.Instance);
        def.TranslatorGroupType.Should().BeSameAs(Nil.Instance);
        def.Acceptance.GetList().Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void each_mod_tag_lands_in_its_own_facet()
    {
        //Arrange
        ContextDef def = new ContextDef();
        object descriptions = Pair.List(Sym("Clef"));

        //Act
        def.AddContextMod(Mod("context-name", Sym("MyStaff")));
        def.AddContextMod(Mod("alias", Sym("Staff")));
        def.AddContextMod(Mod("translator-type", Sym("Engraver_group")));
        def.AddContextMod(Mod("description", "a staff of my own"));
        def.AddContextMod(Mod("grob-descriptions", descriptions));

        //Assert
        def.ContextName.Should().BeSameAs(Sym("MyStaff"));
        Pair.ToList(def.ContextAliases).Should().Equal(Sym("Staff"));
        def.TranslatorGroupType.Should().BeSameAs(Sym("Engraver_group"));
        def.Description.Should().Be("a staff of my own");
        def.GrobDescriptions.Should().BeSameAs(descriptions);
    }

    [Fact]
    public void string_arguments_become_symbols_for_the_symbol_taking_tags()
    {
        //Arrange
        // Upstream converts with scm_string_to_symbol for every tag but description
        // and grob-descriptions — a \name "Foo" line arrives as a string.
        ContextDef def = new ContextDef();

        //Act
        def.AddContextMod(Mod("context-name", "MyVoice"));
        def.AddContextMod(Mod("alias", "Voice"));
        def.AddContextMod(Mod("description", "kept a string"));

        //Assert
        def.ContextName.Should().BeSameAs(Sym("MyVoice"));
        Pair.ToList(def.ContextAliases).Should().Equal(Sym("Voice"));
        def.Description.Should().Be("kept a string");
    }

    [Fact]
    public void get_translator_names_honors_remove_and_returns_reverse_order()
    {
        //Arrange
        ContextDef def = new ContextDef();
        def.AddContextMod(Mod("consists", "Clef_engraver"));
        def.AddContextMod(Mod("consists", "Note_heads_engraver"));
        def.AddContextMod(Mod("remove", "Clef_engraver"));

        //Act
        object names = def.GetTranslatorNames(Nil.Instance);

        //Assert
        Pair.ToList(names).Should().Equal(Sym("Note_heads_engraver"));
    }

    [Fact]
    public void a_repeated_consists_keeps_only_the_last_occurrence()
    {
        //Arrange
        // Duplicates are deduplicated WITHOUT a warning, keeping the position of the
        // last \consists — upstream documents why in get_translator_names.
        ContextDef def = new ContextDef();
        def.AddContextMod(Mod("consists", "A_engraver"));
        def.AddContextMod(Mod("consists", "B_engraver"));
        def.AddContextMod(Mod("consists", "A_engraver"));

        //Act
        object names = def.GetTranslatorNames(Nil.Instance);

        //Assert
        // Reverse order: written order after deduplication is B then A.
        Pair.ToList(names).Should().Equal(Sym("A_engraver"), Sym("B_engraver"));
    }

    [Fact]
    public void user_mods_apply_after_the_definitions_own()
    {
        //Arrange
        // The userMod parameter is the \with block at the instantiation site; its
        // \remove must beat the definition's \consists.
        ContextDef def = new ContextDef();
        def.AddContextMod(Mod("consists", "Clef_engraver"));
        object userMods = Pair.List(Mod("remove", "Clef_engraver"));

        //Act
        object names = def.GetTranslatorNames(userMods);

        //Assert
        names.Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void default_child_accepts_and_denies_drive_the_acceptance_set()
    {
        //Arrange
        ContextDef def = new ContextDef();

        //Act
        def.AddContextMod(Mod("default-child", Sym("Voice")));
        def.AddContextMod(Mod("accepts", Sym("Staff")));
        def.AddContextMod(Mod("accepts", Sym("Lyrics")));
        def.AddContextMod(Mod("denies", Sym("Staff")));

        //Assert
        Pair.ToList(def.Acceptance.GetList()).Should().Equal(Sym("Voice"), Sym("Lyrics"));
        def.Acceptance.GetDefault().Should().BeSameAs(Sym("Voice"));
    }

    [Fact]
    public void property_operations_accumulate_newest_first()
    {
        //Arrange
        ContextDef def = new ContextDef();
        object assign = Pair.List(Sym("assign"), Sym("fontSize"), 2L);
        object unset = Pair.List(Sym("unset"), Sym("fontSize"));

        //Act
        def.AddContextMod(assign);
        def.AddContextMod(unset);

        //Assert
        List<object> ops = Pair.ToList(def.PropertyOps);
        ops[0].Should().BeSameAs(unset);
        ops[1].Should().BeSameAs(assign);
    }

    [Fact]
    public void to_alist_and_lookup_answer_every_facet()
    {
        //Arrange
        ContextDef def = new ContextDef();
        def.AddContextMod(Mod("context-name", Sym("MyStaff")));
        def.AddContextMod(Mod("consists", "Clef_engraver"));
        def.AddContextMod(Mod("accepts", Sym("Voice")));
        def.AddContextMod(Mod("default-child", Sym("Voice")));
        def.AddContextMod(Mod("translator-type", Sym("Engraver_group")));

        //Act
        object alist = def.ToAlist();
        Dictionary<object, object> byKey = new Dictionary<object, object>();
        foreach (object entry in Pair.ToList(alist))
        {
            byKey[((Pair)entry).Car] = ((Pair)entry).Cdr;
        }

        //Assert
        byKey[Sym("context-name")].Should().BeSameAs(Sym("MyStaff"));
        Pair.ToList(byKey[Sym("consists")]).Should().Equal(Sym("Clef_engraver"));
        Pair.ToList(byKey[Sym("accepts")]).Should().Equal(Sym("Voice"));
        byKey[Sym("default-child")].Should().BeSameAs(Sym("Voice"));
        byKey[Sym("group-type")].Should().BeSameAs(Sym("Engraver_group"));

        def.Lookup(Sym("context-name")).Should().BeSameAs(Sym("MyStaff"));
        Pair.ToList(def.Lookup(Sym("consists"))).Should().Equal(Sym("Clef_engraver"));
        def.Lookup(Sym("no-such-key")).Should().BeSameAs(DefaultArgument.Instance);
    }

    [Fact]
    public void to_alist_omits_default_child_and_group_type_when_unset()
    {
        //Arrange
        ContextDef def = new ContextDef();

        //Act
        List<object> keys = Pair.ToList(def.ToAlist())
            .Select(entry => ((Pair)entry).Car)
            .ToList();

        //Assert
        keys.Should().NotContain(Sym("default-child"));
        keys.Should().NotContain(Sym("group-type"));
        keys.Should().Contain(Sym("context-name"));
    }

    [Fact]
    public void is_alias_answers_bottom_the_own_name_and_declared_aliases()
    {
        //Arrange
        ContextDef def = new ContextDef();
        def.AddContextMod(Mod("context-name", Sym("MyStaff")));
        def.AddContextMod(Mod("alias", Sym("Staff")));

        //Act & Assert
        // Bottom means "accepts no default child", so it flips when one is set.
        def.IsAlias(Sym("Bottom")).Should().BeTrue();
        def.IsAlias(Sym("MyStaff")).Should().BeTrue();
        def.IsAlias(Sym("Staff")).Should().BeTrue();
        def.IsAlias(Sym("Voice")).Should().BeFalse();

        def.AddContextMod(Mod("default-child", Sym("Voice")));
        def.IsAlias(Sym("Bottom")).Should().BeFalse();
    }

    [Fact]
    public void a_clone_diverges_from_the_original()
    {
        //Arrange
        // ly:context-def-modify clones and mutates the clone; the original must not
        // move. The acceptance spine is the part a shared copy would corrupt.
        ContextDef original = new ContextDef();
        original.AddContextMod(Mod("context-name", Sym("MyStaff")));
        original.AddContextMod(Mod("accepts", Sym("Voice")));
        original.AddContextMod(Mod("consists", "Clef_engraver"));

        //Act
        ContextDef clone = original.Clone();
        clone.AddContextMod(Mod("accepts", Sym("Lyrics")));
        clone.AddContextMod(Mod("remove", "Clef_engraver"));
        clone.AddContextMod(Mod("context-name", Sym("Other")));

        //Assert
        Pair.ToList(original.Acceptance.GetList()).Should().Equal(Sym("Voice"));
        Pair.ToList(clone.Acceptance.GetList()).Should().Equal(Sym("Lyrics"), Sym("Voice"));
        Pair.ToList(original.GetTranslatorNames(Nil.Instance)).Should().Equal(Sym("Clef_engraver"));
        clone.GetTranslatorNames(Nil.Instance).Should().BeSameAs(Nil.Instance);
        original.ContextName.Should().BeSameAs(Sym("MyStaff"));
        clone.ContextName.Should().BeSameAs(Sym("Other"));
    }

    [Fact]
    public void set_spot_records_the_origin()
    {
        //Arrange
        ContextDef def = new ContextDef();
        object where = "somewhere.ly:3";

        //Act
        def.SetSpot(where);

        //Assert
        def.Origin.Should().BeSameAs(where);
    }

    [Fact]
    public void an_unknown_mod_tag_is_a_programming_error_not_a_silent_drop()
    {
        //Arrange
        ContextDef def = new ContextDef();
        TextWriter savedOutput = Warn.Output;
        Warn.Output = TextWriter.Null;
        Warn.RecordMessages = true;
        Warn.ClearMessages();

        try
        {
            //Act
            def.AddContextMod(Mod("no-such-tag", Sym("x")));

            //Assert
            Warn.Messages
                .Any(m => m.Contains("unknown context mod tag"))
                .Should().BeTrue();
        }
        finally
        {
            Warn.RecordMessages = false;
            Warn.ClearMessages();
            Warn.Output = savedOutput;
        }
    }

    [Fact]
    public void the_type_predicates_answer_true_over_real_instances()
    {
        //Arrange
        // The standing obligation from TypePredicates: a stubbed predicate is right
        // until the type exists and wrong from then on. These two types now exist.
        object result = null;

        //Act
        // CreateInterpreter publishes the bare interpreter as the ambient one;
        // restore whatever was ambient before, or every later context-property
        // assignment in the process would type-check against its empty tables.
        Interpreter ambientBefore = LilyPondScheme.Current;
        try
        {
            Interpreter.RunWithLargeStack(() =>
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                interpreter.DefineValue("the-def", new ContextDef());
                interpreter.DefineValue("the-mod", new ContextMod());
                result = interpreter.EvalString(
                    "(list (ly:context-def? the-def)"
                    + "     (ly:context-def? the-mod)"
                    + "     (ly:context-mod? the-mod)"
                    + "     (ly:context-mod? 42))",
                    "<test>");
            });
        }
        finally
        {
            LilyPondScheme.RestoreAmbient(ambientBefore);
        }

        //Assert
        List<object> answers = Pair.ToList(result);
        answers.Should().Equal(true, false, true, false);
    }
}
