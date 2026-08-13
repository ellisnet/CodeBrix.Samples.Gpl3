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
/// The 2026-08-03 §7b unblock wirings: the Prob Scheme bindings, the interface
/// registry answering as a hash table, and the (empty) function-documentation table.
/// Later demand-loop binding pull-forwards fence themselves here too.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class EnginePrimitiveWiringTests
{
    /// <summary>
    /// Boots an engine interpreter — primitives and stubs, no scm layer — and
    /// evaluates every source in turn, returning the written form of the last result.
    /// </summary>
    private static string Eval(params string[] sources)
    {
        string result = null;

        // CreateInterpreter publishes the bare interpreter as the ambient one;
        // restore whatever was ambient before, or every later context-property
        // assignment in the process would type-check against its empty tables.
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

    [Fact]
    public void a_prob_round_trips_its_properties_through_the_scheme_bindings()
    {
        //Arrange & Act
        string result = Eval(
            "(define p (ly:make-prob 'paper-system '((a . 1)) 'b 2))",
            "(list (ly:prob? p)"
            + "     (ly:prob-property p 'a)"
            + "     (ly:prob-property p 'b)"
            + "     (ly:prob-property p 'missing 42)"
            + "     (ly:prob-property p 'missing))");

        //Assert
        result.Should().Be("(#t 1 2 42 ())");
    }

    [Fact]
    public void prob_set_property_writes_the_mutable_alist()
    {
        //Arrange & Act
        string result = Eval(
            "(define p (ly:make-prob 'paper-system '()))",
            "(ly:prob-set-property! p 'Y-offset 7)",
            "(list (ly:prob-property p 'Y-offset) (ly:prob-type? p 'paper-system))");

        //Assert
        result.Should().Be("(7 #t)");
    }

    [Fact]
    public void all_grob_interfaces_answers_as_a_real_hash_table()
    {
        //Arrange
        // document-backend hash-folds and hashq-refs the result, so an alist is not
        // an acceptable stand-in for the hash table upstream returns.
        //Act
        string result = Eval(
            "(ly:add-interface 'test-interface \"doc\" '(prop-a prop-b))",
            "(list (hash-table? (ly:all-grob-interfaces))"
            + "     (hashq-ref (ly:all-grob-interfaces) 'test-interface))");

        //Assert
        result.Should().Be("(#t (test-interface \"doc\" (prop-a prop-b)))");
    }

    [Fact]
    public void function_documentation_answers_a_table_keyed_by_entry_point_name()
    {
        //Arrange
        // RESTATED 2026-08-13 (EPG24). This used to assert the table was EMPTY, which
        // was the honest reading while the port's bindings carried no docstrings; the
        // vendored entry-point-docs.tsv now fills it, so the fact worth holding is the
        // SHAPE of an entry rather than the absence of all of them.
        //
        // The entry is upstream's (varlist . docstring) pair, hand-read off
        // lily/general-scheme.cc's LY_DEFINE for ly:dir?. The second half of the answer
        // is a control: a name that no macro documents must be absent, so the test
        // cannot pass against a table that answers everything.
        //Act
        string result = Eval(
            "(let ((table (ly:get-all-function-documentation)))"
            + "  (list (hash-table? table)"
            + "        (car (hashq-ref table 'ly:dir?))"
            + "        (hashq-ref table 'no-such-entry-point-exists)))");

        //Assert
        result.Should().Be("(#t \"(SCM s)\" #f)");
    }

    [Fact]
    public void a_duration_converts_to_its_exact_whole_note_count()
    {
        //Arrange
        // ly:duration->number is upstream's Rational (*a): the length in whole notes
        // with the compression factor applied, as an EXACT rational -- a dotted
        // quarter is 3/8, and a triplet quarter (factor 2/3) is 1/6, never a float.
        //Act
        string result = Eval(
            "(list (ly:duration->number (ly:make-duration 2 1))"
            + "     (ly:duration->number (ly:make-duration 2 0 2/3))"
            + "     (ly:duration->number (ly:make-duration 0)))");

        //Assert
        result.Should().Be("(3/8 1/6 1)");
    }
}
