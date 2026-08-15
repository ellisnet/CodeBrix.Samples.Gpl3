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
/// The entry points of <c>lily/moment-scheme.cc</c> whose ARGUMENT TYPES are not
/// obvious from their names.
/// <para>
/// Upstream ships no unit tests for <c>lily/</c>; every expectation here is read off
/// the <c>LY_DEFINE</c> body it fences.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class MusicPrimitivesTests
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
    /// <c>ly_moment_mul</c> takes a moment's main part when handed a moment and
    /// OTHERWISE a plain rational — <c>LY_ASSERT_TYPE (is_scm&lt;Rational&gt;, b, 2)</c>,
    /// with upstream's own comment saying the type error names Rational rather than
    /// Moment "since logically that makes better sense".
    /// <para>
    /// The two forms are asserted to agree, which is the RELATIONSHIP rather than a
    /// remembered pair of numbers; the moment-only reading this replaces made
    /// <c>operators.scm</c>'s <c>(* &lt;number&gt; &lt;Moment&gt;)</c> method a type error
    /// on every call.
    /// </para>
    /// </summary>
    [Fact]
    public void moment_mul_takes_a_rational_as_well_as_a_moment()
    {
        //Arrange
        const string Setup = "(define m (ly:make-moment 3/4))";

        //Act
        string result = Eval(
            Setup,
            "(list (equal? (ly:moment-mul m 1/2) (ly:moment-mul m (ly:make-moment 1/2)))"
            + "      (ly:moment-main (ly:moment-mul m 1/2))"
            + "      (ly:moment-main (ly:moment-mul m 2)))");

        //Assert
        // 3/4 * 1/2 = 3/8 and 3/4 * 2 = 3/2, hand-computed.
        result.Should().Be("(#t 3/8 3/2)");
    }

    /// <summary>The same argument rule on the division half.</summary>
    [Fact]
    public void moment_div_takes_a_rational_as_well_as_a_moment()
    {
        //Arrange
        const string Setup = "(define m (ly:make-moment 3/4))";

        //Act
        string result = Eval(
            Setup,
            "(list (equal? (ly:moment-div m 1/2) (ly:moment-div m (ly:make-moment 1/2)))"
            + "      (ly:moment-main (ly:moment-div m 1/2)))");

        //Assert
        // 3/4 / 1/2 = 3/2, hand-computed.
        result.Should().Be("(#t 3/2)");
    }

    /// <summary>
    /// The CONTROL for the two above: <c>is_scm&lt;Rational&gt;</c> is
    /// <c>scm_is_real AND (exact OR infinite)</c>, so an INEXACT finite real is a type
    /// error rather than a third accepted shape. Without this, "accept any number"
    /// would pass both fences above and still diverge from upstream.
    /// </summary>
    [Fact]
    public void moment_mul_refuses_an_inexact_real_and_a_non_number()
    {
        //Arrange / Act
        string result = Eval(
            "(define m (ly:make-moment 3/4))",
            "(define (throws? thunk)"
            + " (catch #t (lambda () (thunk) #f) (lambda args #t)))",
            "(list (throws? (lambda () (ly:moment-mul m 0.5)))"
            + "      (throws? (lambda () (ly:moment-mul m 'half)))"
            + "      (throws? (lambda () (ly:moment-mul m 1/2))))");

        //Assert
        // The third is the counter-control: the accepted shape must NOT throw.
        result.Should().Be("(#t #t #f)");
    }
}
