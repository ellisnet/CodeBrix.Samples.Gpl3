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
/// The milestone-3 fence for the files LilyPond loads ON DEMAND rather than at startup:
/// the documentation generators, the output backends and a handful of utilities.
/// <para>
/// <see cref="LilyPondSchemeLoadTests"/> fences the startup layer; this fences the
/// remainder, and together they cover all 91 vendored <c>scm/</c> files. The counts here
/// are asserted with EQUALITY, not as a floor, and the three files that do not load are
/// named individually with the unported piece each waits on. That is the whole point of
/// the test: when engine or parser work unblocks one of them, this test FAILS rather than
/// silently absorbing the improvement, which forces the milestone-6 exit checklist to be
/// consulted and this fence to be re-stated.
/// </para>
/// <para>
/// Cost: the startup layer takes roughly twenty seconds and the on-demand pass adds to
/// that, so the load happens once for the whole class, under a lock, exactly as the
/// startup fence does.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class LilyPondOnDemandLoadTests
{
    // 91 vendored files: 55 loaded at startup, 36 on demand. 34 of the 36 load.
    //
    // MOVED 2026-08-03, and the fence is what noticed: ly-syntax-constructors.scm used
    // to be an ON-DEMAND file, because nothing could reach the (lily
    // ly-syntax-constructors) module. Phase 3's LilyPondScheme.EnableLilyModuleAutoload
    // makes (lily <name>) autoload from the mirror the way upstream's lazy
    // Scm_module does, so the startup layer now pulls it in itself — one file across
    // the line, in the right direction, with the 91 total unchanged.
    private const int OnDemandFilesAttempted = 36;

    private const int OnDemandFilesLoaded = 34;

    private const int VendoredFilesTotal = 91;

    private const int StartupFilesLoaded = 55;

    /// <summary>
    /// The files that do not load, each with the unported piece it is waiting for.
    /// <para>
    /// These are RECORDED blockages, not tolerated failures: every one traces to work
    /// this project has not done yet, and none is a defect in the loader or in
    /// LilyScheme. Do not add to this list to make a test pass -- a new entry means a
    /// regression, and a removed entry means milestone 6's exit checklist has moved.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> BlockedFiles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // $defaultlayout comes from the .ly initialisation layer, which the parser
            // builds. This test project cannot reach the parser, so the file is blocked
            // HERE by project structure rather than by anything unported: EPG2 added
            // Parsing.Tests' SchemeLayerClosureTests, which loads the init layer first
            // and shows the file loading clean. The two fences together account for the
            // whole layer.
            ["hyphenate-internal-words"] = "parser environment: $defaultlayout "
                + "(loads in Parsing.Tests' SchemeLayerClosureTests, which has the parser)",

            // The terminal doc-pipeline file. It ly:loads the whole pipeline including
            // hyphenate-internal-words, so it carries that file's parser gate as well as
            // its own output code.
            ["documentation-generate"] = "doc pipeline; parser-gated via hyphenate-internal-words",
        };

    // Process-global engine state again -- see EngineGlobalStateCollection. One load,
    // under a lock, read by every fact in the class.
    private static readonly object LoadGate = new object();

    private static int _startupLoaded;
    private static IReadOnlyList<string> _attempted;
    private static LoadReport _onDemand;

    private static LoadReport RunLoad(out IReadOnlyList<string> attempted, out int startupLoaded)
    {
        lock (LoadGate)
        {
            if (_onDemand == null)
            {
                int startupCount = 0;
                List<string> candidates = null;
                LoadReport report = null;

                // psyntax recurses hard enough to overflow the default stack.
                Interpreter.RunWithLargeStack(() =>
                {
                    EnginePrimitives.ResetCallCounts();
                    Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                    LoadReport startup = LilyPondScheme.LoadViaLilyScm(interpreter);

                    // Snapshot both the count and the names NOW. LoadViaLilyScm leaves its
                    // primitive-load-path hook installed with the startup report captured
                    // in the closure, so any on-demand file that ly:loads another file
                    // appends to that report afterwards. Reading startup.Loaded.Count
                    // after the on-demand pass therefore reports a larger, meaningless
                    // number -- this cost a measurement before it was understood.
                    startupCount = startup.Loaded.Count;
                    HashSet<string> alreadyLoaded =
                        new HashSet<string>(startup.Loaded, StringComparer.Ordinal);

                    // The on-demand set is every vendored file the startup pass did not
                    // load. Defining it by subtraction rather than by "not in the load
                    // order" matters: lily.scm is not in its own load list -- it DRIVES
                    // the list -- yet it is loaded at startup, and attempting it again
                    // draws a correct ly:error about redefining default-global-scale.
                    // That would be a false failure, not a real one.
                    candidates = LilyPondScheme.AllFiles()
                        .Where(name => !alreadyLoaded.Contains(name))
                        .ToList();

                    // Nothing records the order these depend on each other in, and
                    // alphabetical order inverts the real one (documentation-lib and
                    // lily-sort come first upstream). LoadToFixpoint's retry rounds
                    // recover it.
                    report = LilyPondScheme.LoadToFixpoint(interpreter, candidates);
                });

                _startupLoaded = startupCount;
                _attempted = candidates;
                _onDemand = report;
            }

            attempted = _attempted;
            startupLoaded = _startupLoaded;
            return _onDemand;
        }
    }

    [Fact]
    public void the_on_demand_set_is_every_vendored_file_startup_does_not_load()
    {
        //Arrange & Act
        RunLoad(out IReadOnlyList<string> attempted, out int startupLoaded);

        //Assert
        startupLoaded.Should().Be(StartupFilesLoaded);
        attempted.Count.Should().Be(OnDemandFilesAttempted);
        (startupLoaded + attempted.Count).Should().Be(VendoredFilesTotal);
    }

    [Fact]
    public void the_on_demand_files_load_to_the_recorded_count()
    {
        //Arrange
        // EQUALITY, deliberately. A floor would let an unblocked file be absorbed
        // silently, and the whole reason this fence exists is to make that impossible.
        //Act
        LoadReport report = RunLoad(out _, out _);

        //Assert
        report.Total.Should().Be(OnDemandFilesAttempted);
        report.Loaded.Count.Should().Be(OnDemandFilesLoaded);
    }

    [Fact]
    public void the_only_files_that_do_not_load_are_the_recorded_blocked_ones()
    {
        //Arrange
        // If this fails with a file MISSING from Failed, that file now loads: update the
        // count above, drop it from BlockedFiles, and work the milestone-6 exit
        // checklist. If it fails with an EXTRA file, something regressed.
        //Act
        LoadReport report = RunLoad(out _, out _);

        //Assert
        report.Failed.Keys.OrderBy(name => name, StringComparer.Ordinal)
            .Should().Equal(BlockedFiles.Keys.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void the_whole_vendored_scm_layer_is_accounted_for()
    {
        //Arrange & Act
        LoadReport report = RunLoad(out _, out int startupLoaded);

        //Assert
        LilyPondScheme.VendoredNames().Count().Should().Be(VendoredFilesTotal);
        (startupLoaded + report.Loaded.Count).Should().Be(VendoredFilesTotal - BlockedFiles.Count);
    }

    [Fact]
    public void every_blocked_file_fails_for_the_reason_recorded_against_it()
    {
        //Arrange
        // The recorded reason has to stay true, not just the file name. A blocked file
        // that starts failing somewhere else has moved, and the note needs rewriting
        // before it misleads the next reader.
        Dictionary<string, string> signatures = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hyphenate-internal-words"] = "$defaultlayout",
            ["documentation-generate"] = "string-length",
        };

        //Act
        LoadReport report = RunLoad(out _, out _);

        //Assert
        foreach (KeyValuePair<string, string> expected in signatures)
        {
            report.Failed.ContainsKey(expected.Key).Should().BeTrue();
            report.Failed[expected.Key].Should().Contain(expected.Value);
        }
    }

    [Fact]
    public void the_files_that_carry_the_documentation_pipeline_do_load()
    {
        //Arrange
        // These are the ones the §7b recovery work was aimed at, and each of them cost
        // a real fix: list-copy's improper tails, the Prob bindings, the interface and
        // function-documentation hash tables, Guile arrays. document-backend is the
        // last of them, and it came from the O8 ADD_INTERFACE extraction rather than
        // from the §7b pass -- it is the one file whose blocker was engine breadth.
        string[] recovered =
        {
            "documentation-lib", "lily-sort", "document-paper-sizes", "document-colors",
            "document-paper-variables", "document-break-align-symbols",
            "document-outside-staff-priorities", "document-script-priorities",
            "document-functions", "page", "qr-code", "document-backend",
        };

        //Act
        LoadReport report = RunLoad(out _, out _);

        //Assert
        foreach (string name in recovered)
        {
            report.Loaded.Should().Contain(name);
        }
    }
}
