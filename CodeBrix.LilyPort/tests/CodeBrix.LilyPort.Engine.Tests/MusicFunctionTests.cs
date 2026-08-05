// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The music-function call protocol — how actual arguments are matched to a signature.
/// <para>
/// This is the part of EPG1 most worth pinning down. LilyPond's optional-argument rule is
/// NOT Scheme's: a rejected optional argument is not consumed, its default is substituted,
/// and every following optional is defaulted too. Nearly every user-facing command in
/// <c>ly/music-functions-init.ly</c> depends on that, and a "tidier" rewrite silently
/// rebinds arguments rather than failing.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class MusicFunctionTests
{
    private static readonly object Gate = new object();

    /// <summary>
    /// Runs a body against a fully bootstrapped interpreter, published as the ambient one.
    /// <para>
    /// It has to be the AMBIENT interpreter, not merely a live one: a music function calls
    /// its signature predicates through <c>SchemeUtilities.CallCallback</c>, which reads
    /// <c>LilyPondScheme.Current</c> and answers the empty list when there is none. The
    /// empty list is TRUE in Scheme, so without this every predicate would appear to pass
    /// and these tests would assert nothing.
    /// </para>
    /// </summary>
    private static void WithInterpreter(Action<Interpreter> body)
    {
        // Process-global engine state -- serialise, as every other engine test does.
        lock (Gate)
        {
            Interpreter ambientBefore = LilyPondScheme.Current;
            try
            {
                Interpreter.RunWithLargeStack(() =>
                {
                    Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                    body(interpreter);
                });
            }
            finally
            {
                LilyPondScheme.RestoreAmbient(ambientBefore);
            }
        }
    }

    private static object Eval(Interpreter interpreter, string text)
        => interpreter.TreeIlEvaluator.ExpandAndEval(
            SchemeReader.ReadAll(text, "<test>")[0], interpreter.CurrentModule);

    [Fact]
    public void a_signature_of_plain_predicates_passes_arguments_straight_through()
    {
        WithInterpreter(interpreter =>
        {
            //Arrange
            object numberPredicate = Eval(interpreter, "number?");
            object function = Eval(interpreter, "(lambda (a b) (list a b))");
            MusicFunction musicFunction = new MusicFunction(
                Pair.List(Eval(interpreter, "list?"), numberPredicate, numberPredicate),
                function);

            //Act
            object result = musicFunction.Call(Pair.List(1L, 2L));

            //Assert
            List<object> items = Pair.ToList(result);
            items.Should().HaveCount(2);
            items[0].Should().Be(1L);
            items[1].Should().Be(2L);
        });
    }

    [Fact]
    public void a_trailing_optional_argument_may_simply_be_omitted()
    {
        WithInterpreter(interpreter =>
        {
            //Arrange
            // (number? . 99) is an optional argument defaulting to 99.
            object optional = new Pair(Eval(interpreter, "number?"), 99L);
            MusicFunction musicFunction = new MusicFunction(
                Pair.List(Eval(interpreter, "list?"), Eval(interpreter, "number?"), optional),
                Eval(interpreter, "(lambda (a b) (list a b))"));

            //Act
            object result = musicFunction.Call(Pair.List(1L));

            //Assert
            // The end of the argument list is recognisable on its own, so no \default
            // stand-in is needed for a TRAILING optional.
            List<object> items = Pair.ToList(result);
            items.Should().HaveCount(2);
            items[1].Should().Be(99L);
        });
    }

    [Fact]
    public void a_rejected_optional_argument_is_defaulted_and_not_consumed()
    {
        WithInterpreter(interpreter =>
        {
            //Arrange
            // Signature: (optional number? defaulting to 99) then (string?).
            // Call it with just a string: the optional's predicate rejects the string, so
            // the default is substituted AND the string stays available for the next slot.
            object optional = new Pair(Eval(interpreter, "number?"), 99L);
            MusicFunction musicFunction = new MusicFunction(
                Pair.List(Eval(interpreter, "list?"), optional, Eval(interpreter, "string?")),
                Eval(interpreter, "(lambda (a b) (list a b))"));

            //Act
            object result = musicFunction.Call(Pair.List(new MutableString("text")));

            //Assert
            // THIS is the rule that separates LilyPond's protocol from Scheme's. If the
            // rejected argument were consumed, the string would be lost and the call would
            // fail on arity instead.
            List<object> items = Pair.ToList(result);
            items.Should().HaveCount(2);
            items[0].Should().Be(99L);
            items[1].ToString().Should().Be("text");
        });
    }

    [Fact]
    public void one_rejected_optional_defaults_every_following_optional_too()
    {
        WithInterpreter(interpreter =>
        {
            //Arrange
            // Two optionals in a row, then a required string. Rejecting the first must
            // default BOTH -- upstream's do/while over consecutive optionals.
            object first = new Pair(Eval(interpreter, "number?"), 11L);
            object second = new Pair(Eval(interpreter, "number?"), 22L);
            MusicFunction musicFunction = new MusicFunction(
                Pair.List(Eval(interpreter, "list?"), first, second, Eval(interpreter, "string?")),
                Eval(interpreter, "(lambda (a b c) (list a b c))"));

            //Act
            object result = musicFunction.Call(Pair.List(new MutableString("text")));

            //Assert
            List<object> items = Pair.ToList(result);
            items.Should().HaveCount(3);
            items[0].Should().Be(11L);
            items[1].Should().Be(22L);
            items[2].ToString().Should().Be("text");
        });
    }

    [Fact]
    public void an_explicit_default_skips_an_optional_that_would_have_matched()
    {
        WithInterpreter(interpreter =>
        {
            //Arrange
            // *unspecified* is what \default becomes. The optional's predicate rejects it,
            // so the default is used -- and because the argument IS *unspecified*, it is
            // consumed rather than offered to the next slot.
            object optional = new Pair(Eval(interpreter, "number?"), 99L);
            MusicFunction musicFunction = new MusicFunction(
                Pair.List(Eval(interpreter, "list?"), optional, Eval(interpreter, "string?")),
                Eval(interpreter, "(lambda (a b) (list a b))"));

            //Act
            object result = musicFunction.Call(
                Pair.List(Unspecified.Instance, new MutableString("text")));

            //Assert
            List<object> items = Pair.ToList(result);
            items.Should().HaveCount(2);
            items[0].Should().Be(99L);
            items[1].ToString().Should().Be("text");
        });
    }

    [Fact]
    public void an_optional_argument_that_matches_is_used_as_given()
    {
        WithInterpreter(interpreter =>
        {
            //Arrange
            object optional = new Pair(Eval(interpreter, "number?"), 99L);
            MusicFunction musicFunction = new MusicFunction(
                Pair.List(Eval(interpreter, "list?"), optional, Eval(interpreter, "string?")),
                Eval(interpreter, "(lambda (a b) (list a b))"));

            //Act
            object result = musicFunction.Call(
                Pair.List(7L, new MutableString("text")));

            //Assert
            List<object> items = Pair.ToList(result);
            items[0].Should().Be(7L);
            items[1].ToString().Should().Be("text");
        });
    }

    [Fact]
    public void too_many_arguments_is_an_error()
    {
        WithInterpreter(interpreter =>
        {
            //Arrange
            MusicFunction musicFunction = new MusicFunction(
                Pair.List(Eval(interpreter, "list?"), Eval(interpreter, "number?")),
                Eval(interpreter, "(lambda (a) (list a))"));

            //Act / Assert
            Assert.Throws<ArgumentException>(() => musicFunction.Call(Pair.List(1L, 2L)));
        });
    }

    [Fact]
    public void the_signature_car_is_the_return_type_and_is_not_an_argument()
    {
        WithInterpreter(interpreter =>
        {
            //Arrange
            // One entry after the return predicate means ONE argument, not two.
            MusicFunction musicFunction = new MusicFunction(
                Pair.List(Eval(interpreter, "number?"), Eval(interpreter, "number?")),
                Eval(interpreter, "(lambda (a) (* a 2))"));

            //Act
            object result = musicFunction.Call(Pair.List(21L));

            //Assert
            result.Should().Be(42L);
        });
    }
}
