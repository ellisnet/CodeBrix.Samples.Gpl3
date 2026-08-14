// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The entry points from <c>lily/general-scheme.cc</c> whose behaviour is not obvious
/// from their name.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class GeneralPrimitivesTests
{
    /// <summary>
    /// Boots an engine interpreter — primitives and stubs, no scm layer — and
    /// evaluates every source in turn, returning the written form of the last result.
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
    /// Upstream's whole body is <c>return scm_number_p (d);</c>, so the fence is the
    /// RELATIONSHIP to <c>number?</c> rather than a list of remembered answers.
    /// <para>
    /// The spread covers every branch of the numeric tower — fixnum, bignum, exact
    /// rational, real and complex, in both signs where a sign exists — because the
    /// defect this replaces was a C# type pattern (<c>is double || is long || is int</c>)
    /// that accepted two of those branches and silently refused the rest. The
    /// non-numbers are the CONTROL and have to be here: a predicate answering #t to
    /// everything would agree with <c>number?</c> on numbers alone, so the second half
    /// of the list is what makes the first half mean anything (trap 11).
    /// </para>
    /// </summary>
    [Fact]
    public void ly_dimension_answers_exactly_what_number_answers()
    {
        //Arrange
        const string Values =
            "(list 3 -3 9/4 -15/4 2.25 -2.25 0.0 (expt 2 80) (- (expt 2 80)) "
            + "(make-rectangular 1 2) \"3\" 'three #f '() (list 1))";

        //Act
        string result = Eval(
            "(define vs " + Values + ")",
            "(list (equal? (map ly:dimension? vs) (map number? vs))"
            + "      (map ly:dimension? vs))");

        //Assert
        // Ten numbers then five non-numbers, hand-counted off Values above.
        result.Should().Be("(#t (#t #t #t #t #t #t #t #t #t #t #f #f #f #f #f))");
    }
}
