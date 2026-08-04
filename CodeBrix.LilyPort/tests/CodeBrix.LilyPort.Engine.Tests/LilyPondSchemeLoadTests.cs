// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.LilyPort.Engine;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyScheme;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The milestone-3 measurement: how much of LilyPond's Scheme layer loads, and which C++
/// entry points it reaches on the way.
/// <para>
/// The reached-stub list is the porting worklist for the engine, so this is not only a
/// regression fence -- it is the measurement that decides what gets ported next.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class LilyPondSchemeLoadTests
{
    // Every file LilyPond loads at startup now loads. This is an equality check rather
    // than a floor because the startup layer is complete: any drop is a regression.
    //
    // 55 as of 2026-08-03, up from 54, and the extra one is real rather than double
    // counting: Phase 3's (lily <name>) autoloader lets the startup layer pull
    // ly-syntax-constructors.scm in ITSELF — upstream reaches that module the same
    // lazy way — and the load goes through the same primitive-load-path hook, so the
    // report sees it. lily.scm's own list is still 54 names; the report counts what
    // the hook actually loaded, which is the honest measure and the one that moved.
    private const int MinimumFilesLoaded = 55;

    private const int TotalFilesInLoadList = 55;

    // The engine's registries, the stub call counts and the reader's hash extensions are
    // process-global, exactly as they are in the C++ this is ported from. Two loads
    // running at once therefore corrupt each other, and xUnit runs test classes in
    // parallel -- so the load happens once, under a lock, and every test reads the result.
    // It is also the right call on cost: a full load takes roughly twenty seconds.
    private static readonly object LoadGate = new object();

    private static LoadReport _report;
    private static IReadOnlyList<EntryPoint> _reached;
    private static EngineRegistries _registries;

    private static LoadReport RunLoad(out IReadOnlyList<EntryPoint> reachedStubs)
    {
        lock (LoadGate)
        {
            if (_report == null)
            {
                LoadReport report = null;
                List<EntryPoint> reached = null;

                // psyntax recurses hard enough to overflow the default stack.
                Interpreter.RunWithLargeStack(() =>
                {
                    EnginePrimitives.ResetCallCounts();
                    Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                    report = LilyPondScheme.LoadViaLilyScm(interpreter);
                    reached = EnginePrimitives.Called().ToList();
                });

                _report = report;
                _reached = reached;
                _registries = LilyPondScheme.Registries;
            }

            reachedStubs = _reached;
            return _report;
        }
    }

    [Fact]
    public void lilypond_scheme_layer_loads_at_least_the_established_floor()
    {
        //Arrange & Act
        LoadReport report = RunLoad(out _);

        //Assert
        report.Total.Should().Be(TotalFilesInLoadList);
        report.Failed.Should().BeEmpty();
        report.Loaded.Count.Should().BeGreaterThanOrEqualTo(MinimumFilesLoaded);
    }

    [Fact]
    public void the_foundation_files_load_without_error()
    {
        //Arrange
        // These carry the definitions everything else is written against, so a failure
        // here is a different kind of problem from a leaf file failing.
        string[] foundation =
        {
            "lily", "lily-library", "output-lib", "c++", "chord-entry",
            "define-music-types", "define-event-classes", "define-grob-properties",
            "define-context-properties", "markup-macros", "stencil",
        };

        //Act
        LoadReport report = RunLoad(out _);

        //Assert
        foreach (string name in foundation)
        {
            report.Failed.ContainsKey(name).Should().BeFalse();
        }
    }

    [Fact]
    public void startup_reaches_no_unported_entry_point_at_all()
    {
        //Arrange
        // Every primitive the startup layer calls is now ported for real. A stub that
        // gets reached again means new demand appeared -- add it to the worklist and
        // port it, rather than relaxing this test.
        //Act
        RunLoad(out IReadOnlyList<EntryPoint> reached);

        //Assert
        reached.Should().BeEmpty();
    }

    [Fact]
    public void loading_populates_the_registries_the_scheme_layer_fills_in()
    {
        //Arrange & Act
        RunLoad(out _);

        //Assert
        _registries.GrobInterfaces.Count.Should().BeGreaterThan(50);
        _registries.Translators.Count.Should().BeGreaterThan(20);
        _registries.StencilHeads.Count.Should().BeGreaterThan(20);
    }

    [Fact]
    public void every_declared_entry_point_names_the_upstream_file_that_declares_it()
    {
        //Arrange & Act
        RunLoad(out _);

        //Assert
        foreach (EntryPoint entry in EnginePrimitives.All.Values)
        {
            entry.UpstreamFile.Should().NotBeNullOrEmpty();
            entry.Name.Should().StartWith("ly:");
        }
    }

    [Fact]
    public void type_predicates_answer_false_rather_than_a_placeholder()
    {
        //Arrange
        // A placeholder is truthy, so a type predicate returning one would make every
        // type check in LilyPond's Scheme silently succeed.
        object result = null;

        //Act
        lock (LoadGate)
        {
            Interpreter.RunWithLargeStack(() =>
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                result = interpreter.TreeIlEvaluator.ExpandAndEval(
                    CodeBrix.LilyScheme.Reader.SchemeReader.ReadAll("(ly:grob? 42)", "<test>")[0],
                    interpreter.CurrentModule);
            });

            // That interpreter replaced the shared state, so the cached load no longer
            // matches it; force the next reader to rebuild.
            _report = null;
        }

        //Assert
        result.Should().Be(false);
    }
}
