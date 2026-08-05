// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The suite mode that turns a politely-inert placeholder into a failure.
/// <para>
/// Standing rule 4 records that every engine session so far surfaced defects where
/// something upstream declares was half-reproduced and NOTHING FAILED. This is the
/// mechanism against that: an unported primitive normally answers
/// <see cref="UnportedValue"/> so loading can continue, and under suite mode it throws
/// instead, so anything that depended on the placeholder says so.
/// </para>
/// <para>
/// These tests drive the flag directly rather than through the environment variable —
/// the variable is read once per run and a test that mutated it would change the meaning
/// of whatever ran next.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class ThrowOnUnportedTests
{
    [Fact]
    public void the_scope_restores_the_previous_setting()
    {
        //Arrange
        bool before = EnginePrimitives.ThrowOnUnported;

        //Act
        using (EnginePrimitives.ThrowingScope())
        {
            EnginePrimitives.ThrowOnUnported.Should().BeTrue();
        }

        //Assert
        // Process-global engine state again: a scope that leaked would silently change
        // every later test in the run.
        EnginePrimitives.ThrowOnUnported.Should().Be(before);
    }

    [Fact]
    public void nested_scopes_restore_in_order()
    {
        //Arrange
        bool before = EnginePrimitives.ThrowOnUnported;

        //Act / Assert
        using (EnginePrimitives.ThrowingScope())
        {
            using (EnginePrimitives.ThrowingScope())
            {
                EnginePrimitives.ThrowOnUnported.Should().BeTrue();
            }

            EnginePrimitives.ThrowOnUnported.Should().BeTrue();
        }

        EnginePrimitives.ThrowOnUnported.Should().Be(before);
    }

    [Fact]
    public void an_unported_primitive_throws_under_suite_mode_and_names_its_upstream_file()
    {
        //Arrange
        // ly:optimal-breaking is a real, unported entry point -- page-breaking-scheme.cc
        // declares it and EPG16 owes it. Deliberately chosen from a group far from any
        // current work, so this test does not quietly stop testing anything the day the
        // primitive lands.
        NotPortedException thrown = null;

        //Act
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            EnginePrimitives.InstallStubs(interpreter);

            using (EnginePrimitives.ThrowingScope())
            {
                try
                {
                    interpreter.Eval(SchemeReader.ReadAll(
                        "(ly:optimal-breaking #f)", "<test>")[0]);
                }
                catch (Exception ex)
                {
                    Exception cause = ex;
                    while (cause != null && !(cause is NotPortedException))
                    {
                        cause = cause.InnerException;
                    }

                    thrown = cause as NotPortedException;
                }
            }
        });

        //Assert
        thrown.Should().NotBeNull();
        thrown.EntryPoint.Name.Should().Be("ly:optimal-breaking");
        thrown.Message.Should().Contain("page-breaking-scheme.cc");
    }

    [Fact]
    public void the_same_primitive_answers_a_placeholder_when_the_mode_is_off()
    {
        //Arrange
        object result = null;

        //Act
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            EnginePrimitives.InstallStubs(interpreter);
            EnginePrimitives.ThrowOnUnported = false;

            result = interpreter.Eval(SchemeReader.ReadAll(
                "(ly:optimal-breaking #f)", "<test>")[0]);
        });

        //Assert
        // The default has to stay permissive: LilyPond's Scheme calls unported
        // primitives while building its tables, and a throwing stub there aborts the
        // file and hides every later call in it.
        result.Should().BeOfType<UnportedValue>();
    }
}
