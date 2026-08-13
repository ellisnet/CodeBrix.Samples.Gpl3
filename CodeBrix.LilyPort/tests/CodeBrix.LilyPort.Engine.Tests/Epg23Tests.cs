// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// EPG23's fences: the long tail of entry points, and gate G3's closure.
/// <para>
/// Every fact here is a RELATIONSHIP with a control that must come out differently, per
/// standing rule 18 — none of these expectations was recorded from the port's own output.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class Epg23Tests
{
    [Fact]
    public void ly_event_p_agrees_with_the_post_event_mus_type()
    {
        //Arrange
        // Upstream answers m->is_mus_type ("post-event"), so the predicate must track the
        // music's OWN types list rather than any particular event name. Asserting it
        // against the types list is what keeps this from being a recorded literal: if
        // define-music-types.scm ever moves 'post-event, both sides move together.
        const string Source = @"
            (let ((post  (make-music 'SlurEvent))
                  (plain (make-music 'SequentialMusic)))
              (list (ly:event? post)
                    (if (memq 'post-event (ly:music-property post 'types)) #t #f)
                    (ly:event? plain)
                    (if (memq 'post-event (ly:music-property plain 'types)) #t #f)))";

        //Act
        object result = Epg8TestHarness.Eval(Source);
        object[] values = ToArray(result, 4);

        //Assert
        // The predicate agrees with the types list on BOTH, and the two cases disagree
        // with each other — which is the control: an implementation returning a constant
        // would satisfy the first pair and fail here.
        values[0].Should().Be(values[1]);
        values[2].Should().Be(values[3]);
        values[0].Should().Be(true);
        values[2].Should().Be(false);
    }

    [Fact]
    public void ly_duration_to_string_appends_dots_and_the_scale_factor()
    {
        //Arrange
        // Duration::to_string is "2^-log", then one '.' per dot, then "*factor" when the
        // factor is not 1. Hand-computed from that expression: a quarter note is log 2,
        // so the undotted form is "4".
        const string Source = @"
            (list (ly:duration->string (ly:make-duration 2 0))
                  (ly:duration->string (ly:make-duration 2 1))
                  (ly:duration->string (ly:make-duration 2 0 2 3))
                  (ly:duration->string (ly:make-duration 3 0)))";

        //Act
        object[] values = ToArray(Epg8TestHarness.Eval(Source), 4);
        string plain = Text(values[0]);
        string dotted = Text(values[1]);
        string scaled = Text(values[2]);
        string eighth = Text(values[3]);

        //Assert
        plain.Should().Be("4");
        dotted.Should().Be(plain + ".");
        scaled.Should().Be(plain + "*2/3");

        // The control: a DIFFERENT duration log must not produce the same string, or the
        // three relationships above would hold for an implementation that ignored its
        // argument.
        eighth.Should().Be("8");
        eighth.Should().NotBe(plain);
    }

    [Fact]
    public void ly_angle_agrees_between_its_pair_and_two_argument_forms()
    {
        //Arrange
        // atan2 (1, 1) is pi/4, which is 45 degrees — hand-computed, not recorded.
        const string Source = @"
            (list (ly:angle 1 1)
                  (ly:angle (cons 1 1))
                  (ly:angle 1 0)
                  (ly:length 3 4))";

        //Act
        object[] values = ToArray(Epg8TestHarness.Eval(Source), 4);

        //Assert
        Convert.ToDouble(values[0]).Should().BeApproximately(45.0, 1e-9);

        // The two spellings are the same question, so they must answer the same — the
        // relationship that a one-argument/two-argument arity slip would break.
        Convert.ToDouble(values[1]).Should().BeApproximately(45.0, 1e-9);

        // Controls: a different vector gives a different angle, and ly:angle's sibling
        // ly:length still answers a LENGTH (3-4-5) rather than an angle, which is what
        // fails if the two were wired to the same body.
        Convert.ToDouble(values[2]).Should().BeApproximately(0.0, 1e-9);
        Convert.ToDouble(values[3]).Should().BeApproximately(5.0, 1e-9);
    }

    [Fact]
    public void the_spring_setters_mutate_the_callers_spring_and_return_a_copy()
    {
        //Arrange
        // ⚠ Standing trap 9. Upstream's setter takes a POINTER, mutates the smob it was
        // handed, and returns smobbed_copy () — a NEW smob. Spring is a struct here, so
        // the value Scheme holds is a box; mutating an unboxed local would leave the
        // caller's spring untouched and nothing would fail loudly.
        Interpreter interpreter = Epg8TestHarness.Loaded();
        object boxed = new Spring(2.0, 1.0);
        interpreter.GuileModule.Define(Symbol.Intern("epg23-spring"), boxed);

        //Act
        object returned = Epg8TestHarness.Eval(
            "(ly:spring-set-inverse-stretch-strength! epg23-spring 5.0)");

        //Assert
        // The CALLER's spring changed — this is the half a copy-based port gets wrong.
        ((Spring)boxed).InverseStretchStrength.Should().Be(5.0);

        // ...and a DIFFERENT object came back, which is the control: returning the same
        // box would also satisfy the line above while diverging from smobbed_copy ().
        ReferenceEquals(returned, boxed).Should().BeFalse();
        ((Spring)returned).InverseStretchStrength.Should().Be(5.0);

        // The other half of the spring is untouched, so the two setters are not wired to
        // one another.
        ((Spring)boxed).InverseCompressStrength.Should().NotBe(5.0);
    }

    [Fact]
    public void cpp_warning_translation_rewrites_printf_placeholders_for_guile()
    {
        //Arrange
        // Upstream's rules, each with the reason it is not the obvious one:
        //   ~   -> ~~   (a literal tilde must survive Guile's format)
        //   %%  -> %    (an escaped percent collapses)
        //   %s  -> ~a   (NOT ~s, which would add quotes)
        //   %d  -> ~a   (NOT ~d, which only ice-9 supports)
        //   %x  -> ~    (any other conversion loses its letter)
        const string Source = @"
            (list (ly:translate-cpp-warning-scheme ""a ~ b"")
                  (ly:translate-cpp-warning-scheme ""100%% sure"")
                  (ly:translate-cpp-warning-scheme ""file %s line %d"")
                  (ly:translate-cpp-warning-scheme ""plain text""))";

        //Act
        object[] values = ToArray(Epg8TestHarness.Eval(Source), 4);

        //Assert
        Text(values[0]).Should().Be("a ~~ b");
        Text(values[1]).Should().Be("100% sure");
        Text(values[2]).Should().Be("file ~a line ~a");

        // The control: text with no placeholder must come back UNCHANGED. A rewrite that
        // touched every character would still pass the three above.
        Text(values[3]).Should().Be("plain text");
    }

    [Fact]
    public void an_accumulative_option_gathers_values_in_the_order_they_were_added()
    {
        //Arrange
        // include-settings is the one accumulative option lily.scm declares
        // (#:accumulative? #t). Values are STORED reversed and read back in order, so
        // this fences the pair of reversals against each other rather than either alone.
        object saved = Epg8TestHarness.Eval("(ly:get-option 'include-settings)");

        try
        {
            //Act
            object[] values = ToArray(
                Epg8TestHarness.Eval(@"
                    (begin
                      (ly:append-to-option 'include-settings ""first"")
                      (ly:append-to-option 'include-settings ""second"")
                      (list (ly:get-option 'include-settings)
                            (ly:get-option 'point-and-click)))"),
                2);

            //Assert
            object[] gathered = ToArray(values[0], 2);
            Text(gathered[0]).Should().Be("first");
            Text(gathered[1]).Should().Be("second");

            // The control: an ordinary option is NOT turned into a list by any of this.
            values[1].Should().NotBeOfType<Pair>();
        }
        finally
        {
            // Program options are process-global engine state (standing rule 8), so the
            // option is put back through ly:reset-options — which also exercises it.
            Interpreter interpreter = Epg8TestHarness.Loaded();
            interpreter.GuileModule.Define(Symbol.Intern("epg23-saved-setting"), saved);
            Epg8TestHarness.Eval(
                "(ly:reset-options (list (cons 'include-settings epg23-saved-setting)))");
        }
    }

    [Fact]
    public void a_not_applicable_binding_raises_with_its_reason_instead_of_answering()
    {
        //Arrange
        // D25's contract: an accepted N/A binding EXISTS and THROWS. The failure mode it
        // exists to prevent is a silent #f (or the inert UnportedValue, which is truthy)
        // flowing on into a caller that believes it.
        //
        // ly:smob-protects is category guile-internals: the port has no GC protection
        // list to report.

        //Act
        Exception raised = Record.Exception(() => Epg8TestHarness.Eval("(ly:smob-protects)"));

        //Assert
        raised.Should().NotBeNull();

        // The message has to carry BOTH halves — what happened and under which ruling —
        // because that is what a Phase 4 session reading a failure needs to decide
        // whether to flip the row back to owed.
        string message = Describe(raised);
        message.Should().Contain("not applicable");
        message.Should().Contain("guile-internals");
    }

    [Fact]
    public void the_font_predicates_answer_no_rather_than_a_truthy_placeholder()
    {
        //Arrange
        // ⚠ Both are declared LY_DEFINE rather than as smob type predicates, so their
        // stubs answered the inert UnportedValue — which is TRUTHY. Before EPG23 these
        // said YES to every value they were handed.
        //
        // ly:pango-font? is a constant #f by D13/D23 (no Pango layer exists), and
        // ly:otf-font? must still discriminate, or "answers #f" would be satisfied by
        // both for the wrong reason.
        Interpreter interpreter = Epg8TestHarness.Loaded();
        OpenTypeFont font = new OpenTypeFont(
            FontAssets.MusicFont("emmentaler-20"), "emmentaler-20", interpreter);
        interpreter.GuileModule.Define(
            Symbol.Intern("epg23-music-font"),
            new OpenTypeFontMetric(font, "emmentaler-20"));

        const string Source = @"
            (list (ly:pango-font? 42)
                  (ly:otf-font? 42)
                  (ly:otf-font? epg23-music-font)
                  (ly:pango-font? epg23-music-font))";

        //Act
        object[] values = ToArray(Epg8TestHarness.Eval(Source), 4);

        //Assert
        values[0].Should().Be(false);
        values[1].Should().Be(false);

        // The control: a REAL music font answers yes, so ly:otf-font? is discriminating
        // rather than constantly false...
        values[2].Should().Be(true);

        // ...while ly:pango-font? still says no to that same font, which is the second
        // control — the two predicates must not be wired to one another.
        values[3].Should().Be(false);
    }

    private static object[] ToArray(object list, int expected)
    {
        object[] values = new object[expected];
        object cursor = list;
        for (int i = 0; i < expected; i++)
        {
            Pair pair = cursor as Pair;
            pair.Should().NotBeNull();
            values[i] = pair.Car;
            cursor = pair.Cdr;
        }

        return values;
    }

    private static string Text(object value)
        => value is MutableString mutable ? mutable.ToString() : value as string;

    private static string Describe(Exception error)
    {
        string text = error.ToString();
        for (Exception cause = error; cause != null; cause = cause.InnerException)
        {
            text += " | " + cause.Message;
        }

        return text;
    }
}
