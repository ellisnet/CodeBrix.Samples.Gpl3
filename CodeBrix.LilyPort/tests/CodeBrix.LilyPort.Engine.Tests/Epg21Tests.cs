// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// EPG21's rules, asserted against upstream's own definitions rather than against what the
/// port happens to produce.
/// </summary>
/// <remarks>
/// <para>
/// Two of the three things this group can get wrong are INVISIBLE at the level of "did a
/// page come out": a bit mask renumbered (the masks are grob PROPERTY values, so the
/// engraver writes one number and the stencil reads another), and a binding left
/// unregistered (the head keeps its ordinary note-head stencil and the ligature is simply
/// drawn as loose notes). Both are fenced here.
/// </para>
/// <para>
/// The third — the shapes themselves — is fenced end to end in
/// <c>AncientNotationEndToEndTests</c>, because it is only observable in output.
/// </para>
/// </remarks>
[Collection(EngineGlobalStateCollection.Name)]
public class Epg21Tests
{
    // ----- the bit masks: grob property VALUES, so they are load-bearing -----

    [Fact]
    public void the_mensural_primitive_composites_are_the_unions_upstream_defines()
    {
        //Arrange
        // mensural-ligature.hh defines the composites in terms of the singles. Asserting
        // the RELATIONSHIP rather than the literals is what catches a renumbering: a
        // single mask moved without its composite moving would still satisfy hard-coded
        // numbers on both sides.

        //Act & Assert
        MensuralLigature.Stem.Should().Be(MensuralLigature.Up | MensuralLigature.Down);
        MensuralLigature.RightStem.Should()
            .Be(MensuralLigature.JoinUp | MensuralLigature.JoinDown);

        MensuralLigature.SingleHead.Should()
            .Be(MensuralLigature.Brevis | MensuralLigature.Maxima);

        MensuralLigature.Flexa.Should()
            .Be(MensuralLigature.FlexaBegin | MensuralLigature.FlexaEnd);

        MensuralLigature.Any.Should().Be(
            MensuralLigature.Flexa | MensuralLigature.SingleHead | MensuralLigature.Invalid);
    }

    [Fact]
    public void a_left_stem_is_not_a_note_shape_and_a_note_shape_is_not_a_stem()
    {
        //Arrange
        // The control for the test above, and the property the engraver's flexa test
        // depends on: `!(prim & (MLP_STEM | MLP_MAXIMA | MLP_INVALID))' is only a
        // meaningful question if the stem bits and the shape bits are disjoint. Overlap
        // them and every stemmed brevis silently stops being able to form a flexa.

        //Act & Assert
        (MensuralLigature.Stem & MensuralLigature.Any).Should().Be(0);
        (MensuralLigature.RightStem & MensuralLigature.Any).Should().Be(0);
        (MensuralLigature.Pes & MensuralLigature.Any).Should().Be(0);
    }

    [Fact]
    public void the_join_up_bit_is_the_up_bit_shifted_by_the_factor_the_engraver_multiplies_by()
    {
        //Arrange
        // Mensural_ligature_engraver::propagate_properties turns a LEFT stem on this head
        // into a RIGHT stem on the previous one by MULTIPLYING:
        //     prev_output | stem * (MLP_JOIN_UP / MLP_UP)
        // That arithmetic is only correct while JoinDown/Down has the same ratio as
        // JoinUp/Up. It is integer division, so a renumbering that broke the ratio would
        // silently truncate instead of failing.

        //Act
        int factor = MensuralLigature.JoinUp / MensuralLigature.Up;

        //Assert
        factor.Should().Be(4);
        (MensuralLigature.Down * factor).Should().Be(MensuralLigature.JoinDown);
        (MensuralLigature.Up * factor).Should().Be(MensuralLigature.JoinUp);
    }

    [Fact]
    public void the_vaticana_stacked_head_bit_does_not_collide_with_the_gregorian_context_bits()
    {
        //Arrange
        // vaticana-ligature.hh says STACKED_HEAD "extends those in gregorian-ligature.hh",
        // and both families are written into the SAME `context-info' property. A collision
        // would make is_stacked_head's answer indistinguishable from a pes or a flexa.
        int gregorianContextBits = GregorianLigature.PesLower | GregorianLigature.PesUpper
            | GregorianLigature.FlexaLeft | GregorianLigature.FlexaRight
            | GregorianLigature.AfterDeminutum;

        //Act & Assert
        (VaticanaLigature.StackedHead & gregorianContextBits).Should().Be(0);
    }

    // ----- prefixes_to_str -----

    [Fact]
    public void the_prefix_names_are_listed_in_upstreams_order_and_comma_separated()
    {
        //Arrange
        // gregorian-ligature.cc calls check_prefix in a fixed order and joins with ", ".
        // The order is user-visible: it is the text of the "ignored prefix(es)" warning.
        Item head = MakeItem(
            ("prefix-set",
                (object)(long)(GregorianLigature.Inclinatum | GregorianLigature.Virga
                    | GregorianLigature.Auctum)));

        //Act
        string result = GregorianLigature.PrefixesToStr(head);

        //Assert
        result.Should().Be("virga, inclinatum, auctum");
    }

    [Fact]
    public void a_head_with_no_prefixes_names_none()
    {
        //Arrange
        // The control. A prefixes_to_str that answered a non-empty string here would make
        // Vaticana_ligature_engraver::check_for_prefix_loss warn about every plain
        // punctum in every chant.
        Item head = MakeItem(("prefix-set", (object)0L));

        //Act
        string result = GregorianLigature.PrefixesToStr(head);

        //Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void the_pes_or_flexa_operator_is_not_one_of_the_named_prefixes()
    {
        //Arrange
        // check_for_prefix_loss masks PES_OR_FLEXA out before deciding to warn, because
        // `\~' is an operator rather than a head prefix and a curved flexa does not
        // "ignore" it. prefixes_to_str must agree by never naming it.
        Item head = MakeItem(("prefix-set", (object)(long)GregorianLigature.PesOrFlexa));

        //Act
        string result = GregorianLigature.PrefixesToStr(head);

        //Assert
        result.Should().BeEmpty();
    }

    // ----- the registration fence: EPG16's rule, applied to this group -----

    [Fact]
    public void every_ligature_stencil_binding_answers_a_real_procedure()
    {
        //Arrange
        // ⚠ THIS IS THE TEST THIS GROUP EXISTS TO HAVE. Three of these five answer '()
        // by design, which is exactly why leaving them unregistered would be invisible:
        // an unported stub answers the inert placeholder, which is TRUTHY, so the backend
        // would take the placeholder for a stencil instead of skipping an empty one.
        string[] names =
        {
            "ly:kievan-ligature::print",
            "ly:mensural-ligature::print",
            "ly:mensural-ligature::brew-ligature-primitive",
            "ly:vaticana-ligature::print",
            "ly:vaticana-ligature::brew-ligature-primitive",
        };

        //Act
        string result = Eval(
            "(list "
            + string.Join(" ", System.Array.ConvertAll(names, n => "(procedure? " + n + ")"))
            + ")");

        //Assert
        result.Should().Be("(#t #t #t #t #t)");
    }

    [Fact]
    public void the_ligature_stencil_bindings_are_not_unported_stubs()
    {
        //Arrange
        // The control, and the half that has teeth: an unported stub IS a procedure, so
        // `procedure?' alone cannot tell the two apart. Calling one and getting '() back
        // can — a stub answers the placeholder object, and the placeholder is not '().
        Item ligature = MakeItem();

        //Act
        object mensural = MensuralLigature.Print(ligature);
        object vaticana = VaticanaLigature.Print(ligature);
        object kievan = KievanLigature.Print(ligature);

        //Assert
        mensural.Should().BeSameAs(Nil.Instance);
        vaticana.Should().BeSameAs(Nil.Instance);
        kievan.Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void every_epg21_engraver_resolves_through_the_translator_registry()
    {
        //Arrange
        // `\consists Mensural_ligature_engraver' resolving to nothing is a WARNING, not an
        // error, and the score still engraves -- as ordinary notes. That is how
        // Ligature_bracket_engraver went 4,224 misses per sweep without anything failing.
        string[] names =
        {
            "Ligature_bracket_engraver",
            "Mensural_ligature_engraver",
            "Vaticana_ligature_engraver",
            "Kievan_ligature_engraver",
            "Episema_engraver",
        };

        //Act
        List<bool> found = new List<bool>();
        Interpreter ambientBefore = LilyPondScheme.Current;
        try
        {
            Interpreter.RunWithLargeStack(() =>
            {
                LilyPondScheme.CreateInterpreter();
                foreach (string name in names)
                {
                    found.Add(LilyPondScheme.Registries.Translators
                        .ContainsKey(Symbol.Intern(name)));
                }
            });
        }
        finally
        {
            LilyPondScheme.RestoreAmbient(ambientBefore);
        }

        //Assert
        found.Should().AllSatisfy(f => f.Should().BeTrue());
    }

    [Fact]
    public void the_abstract_ligature_engravers_are_not_in_the_registry()
    {
        //Arrange
        // The control. Upstream declares no ADD_TRANSLATOR for the three abstract classes,
        // and `\consists' must not be able to reach them: an abstract engraver in the
        // registry would be a name that resolves and then cannot be constructed.
        string[] names =
        {
            "Ligature_engraver",
            "Coherent_ligature_engraver",
            "Gregorian_ligature_engraver",
        };

        //Act
        List<bool> found = new List<bool>();
        Interpreter ambientBefore = LilyPondScheme.Current;
        try
        {
            Interpreter.RunWithLargeStack(() =>
            {
                LilyPondScheme.CreateInterpreter();
                foreach (string name in names)
                {
                    found.Add(LilyPondScheme.Registries.Translators
                        .ContainsKey(Symbol.Intern(name)));
                }
            });
        }
        finally
        {
            LilyPondScheme.RestoreAmbient(ambientBefore);
        }

        //Assert
        found.Should().AllSatisfy(f => f.Should().BeFalse());
    }

    private static Symbol Sym(string name) => Symbol.Intern(name);

    private static object Alist(params (string Key, object Value)[] entries)
    {
        object result = Nil.Instance;
        for (int i = entries.Length - 1; i >= 0; i--)
        {
            result = new Pair(new Pair(Sym(entries[i].Key), entries[i].Value), result);
        }

        return result;
    }

    private static Item MakeItem(params (string Key, object Value)[] extra)
    {
        List<(string, object)> entries = new List<(string, object)>
        {
            ("meta", Alist(("name", Sym("TestGrob")), ("interfaces", Nil.Instance))),
        };
        entries.AddRange(extra);
        return new Item(Alist(entries.ToArray()));
    }

    private static string Eval(params string[] sources)
    {
        string result = null;

        Interpreter ambientBefore = LilyPondScheme.Current;
        try
        {
            Interpreter.RunWithLargeStack(() =>
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                foreach (string source in sources)
                {
                    result = Printer.Write(interpreter.EvalString(source, "<test>"));
                }
            });
        }
        finally
        {
            LilyPondScheme.RestoreAmbient(ambientBefore);
        }

        return result;
    }
}
