// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// Bindings whose OPTIONAL argument changes the answer, and which had been accepting it
/// and throwing it away.
/// <para>
/// Both of these declared the right arity, so nothing in the demand loop, the entry-point
/// closure or the ledger could notice: the call succeeded and returned a well-formed
/// value, just the value for the one-argument case. The C# function each one owes had
/// been ported faithfully and had no caller — trap 17a — so the fences here assert the
/// TWO answers DIFFER, which is the only thing a dropped argument can be caught by.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class OptionalArgumentBindingTests
{
    /// <summary>
    /// Boots an engine interpreter — primitives and stubs, no scm layer — and evaluates
    /// every source in turn, returning the written form of the last result.
    /// </summary>
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

    /// <summary>
    /// THE CLAIM, stated as the one shape a dropped argument cannot produce: ONE stencil,
    /// asked three ways, must answer DIFFERENTLY. A spacer — X = (0 . 1), Y empty, which
    /// is what <c>\hspace #1</c> builds — is empty on Y, not empty on X, and not empty as
    /// a whole. Hand-computed from upstream: <c>Stencil::is_empty (Axis)</c> is
    /// <c>Box::is_empty (a)</c>, while <c>Stencil::is_empty ()</c> is
    /// <c>scm_is_null (expr_) || dim_.is_empty ()</c> and <c>Box::is_empty ()</c> is the
    /// AND of both axes — so a stencil empty on one axis only is NOT empty as a whole.
    /// With the argument dropped all three calls answered the third value.
    /// </summary>
    [Fact]
    public void stencil_empty_answers_per_axis_when_an_axis_is_given()
    {
        //Arrange & Act
        string result = Eval(
            "(ly:register-stencil-expression 'test-head)",
            "(define s (ly:make-stencil (list 'test-head) '(0 . 1) '(+inf.0 . -inf.0)))",
            "(list (ly:stencil-empty? s 1) (ly:stencil-empty? s 0) (ly:stencil-empty? s))");

        //Assert
        result.Should().Be("(#t #f #f)");
    }

    /// <summary>
    /// THE CONTROL, which must come out DIFFERENTLY: a stencil empty on BOTH axes answers
    /// the same thing however it is asked. Without it the case above could be read as "the
    /// axis argument is doing something arbitrary" rather than "it selects the axis".
    /// </summary>
    [Fact]
    public void stencil_empty_on_both_axes_answers_the_same_whichever_way_it_is_asked()
    {
        //Arrange & Act
        string result = Eval(
            "(ly:register-stencil-expression 'test-head)",
            "(define e (ly:make-stencil (list 'test-head)"
            + "            '(+inf.0 . -inf.0) '(+inf.0 . -inf.0)))",
            "(list (ly:stencil-empty? e 1) (ly:stencil-empty? e 0) (ly:stencil-empty? e))");

        //Assert
        result.Should().Be("(#t #t #t)");
    }

    /// <summary>
    /// THE CLAIM for <c>ly:event-length</c>. Upstream's two-argument form
    /// (<c>translator.cc</c>'s <c>get_event_length (e, now)</c>) moves the whole length
    /// into the GRACE part when <c>now</c> is an in-grace moment; grace notes take no
    /// main-part time, so a length measured at a grace moment is a grace-only length.
    /// Hand-computed from that function: a 3/4 length at a moment whose grace part is
    /// -3/4 answers <c>#&lt;Mom 0G3/4&gt;</c>.
    /// </summary>
    [Fact]
    public void event_length_at_a_grace_moment_answers_a_grace_only_length()
    {
        //Arrange & Act
        string result = Eval(
            "(define e (ly:make-stream-event 'note-event"
            + "           (list (cons 'length (ly:make-moment 3/4)))))",
            "(ly:event-length e (ly:make-moment 0 -3/4))");

        //Assert
        result.Should().Be("#<Mom 0G3/4>");
    }

    /// <summary>
    /// THE CONTROL, in two directions at once. At an ORDINARY moment the same event must
    /// answer its plain length, and with NO moment at all it must answer the same thing —
    /// so a fence that passed by ignoring the argument cannot also pass the case above.
    /// </summary>
    [Fact]
    public void event_length_outside_grace_time_and_with_no_moment_answer_the_plain_length()
    {
        //Arrange & Act
        string result = Eval(
            "(define e (ly:make-stream-event 'note-event"
            + "           (list (cons 'length (ly:make-moment 3/4)))))",
            "(list (ly:event-length e (ly:make-moment 1)) (ly:event-length e))");

        //Assert
        result.Should().Be("(#<Mom 3/4> #<Mom 3/4>)");
    }

    /// <summary>
    /// THE CLAIM for <c>SchemeUtilities.Memq</c>, which is the engine's <c>scm_memq</c>.
    /// Guile fixnums are IMMEDIATES, so <c>(memq 3 '(1 2 3))</c> is true there; the engine
    /// helper compared with <c>ReferenceEquals</c>, under which one boxed 3 is never equal
    /// to another. <c>Figured_bass_engraver</c> reads <c>implicitBassFigures</c> — a list
    /// of figure NUMBERS — through it, so no implicit figure had ever been suppressed.
    /// <para>
    /// ⚠ The sibling <c>Assq</c> already carried a note about exactly this defect, from
    /// the day <c>ottavationMarkups</c> exposed it. This one and <c>AssqRemove</c> were
    /// left standing — trap 17c: when one member of a family is repaired, sweep the family.
    /// </para>
    /// </summary>
    [Fact]
    public void memq_finds_a_number_by_value_the_way_a_guile_fixnum_compares()
    {
        //Arrange
        // The list is built with SEPARATELY boxed longs, which is what a Scheme literal
        // list read by the parser gives: no two of them are the same reference.
        object list = Pair.List(1L, 2L, 3L);

        //Act
        bool found = SchemeUtilities.Memq(3L, list);
        bool absent = SchemeUtilities.Memq(4L, list);

        //Assert
        found.Should().BeTrue();
        absent.Should().BeFalse();
    }

    /// <summary>
    /// THE CONTROL, which must come out differently in the one way that matters: identity
    /// comparison is still identity for things Guile compares by identity. Two DISTINCT
    /// mutable strings of the same text are not <c>eq?</c>, so <c>memq</c> must not find
    /// one in a list holding the other — a value-equality implementation would.
    /// </summary>
    [Fact]
    public void memq_does_not_find_a_distinct_object_of_equal_value()
    {
        //Arrange
        object list = Pair.List(new MutableString("a"), new MutableString("b"));

        //Act
        bool foundEqualValue = SchemeUtilities.Memq(new MutableString("a"), list);
        bool foundSameSymbol = SchemeUtilities.Memq(
            Symbol.Intern("x"), Pair.List(Symbol.Intern("x")));

        //Assert
        foundEqualValue.Should().BeFalse();
        foundSameSymbol.Should().BeTrue();
    }
}
