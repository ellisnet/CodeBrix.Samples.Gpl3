// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Parsing.Session;
using CodeBrix.LilyScheme;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// Gate G6's closing half: the two <c>scm/</c> files that
/// <c>LilyPondOnDemandLoadTests</c> records as blocked are blocked ON THE PARSER, and
/// this is where that can be shown.
/// <para>
/// The Engine test project cannot reach the parser, so the <c>scm/</c> fence over there
/// necessarily measures the layer WITHOUT the <c>ly/</c> init layer under it. That is
/// the honest measurement for the Engine on its own, and it is not the whole story:
/// <c>hyphenate-internal-words.scm</c> reads <c>$defaultlayout</c>, which only exists
/// once <c>declarations-init.ly</c> has been parsed. This fence runs the same load with
/// the init layer in place, so the two halves together account for all 91 files.
/// </para>
/// <para>
/// The interpreter is process-global (plan risk 7), so this serialises with the other
/// load fences on the "LilyPondScheme" collection.
/// </para>
/// </summary>
[Collection("LilyPondScheme")]
public class SchemeLayerClosureTests
{
    private static readonly object Gate = new object();
    private static LoadReport _report;

    /// <summary>
    /// The files the Engine's <c>scm/</c> fence records as blocked, and which this
    /// fence re-attempts with the init layer under them.
    /// </summary>
    private static readonly string[] ParserGatedFiles =
    {
        "hyphenate-internal-words",
        "documentation-generate",
    };

    /// <summary>
    /// The one file that still does not load, and why it is a different KIND of thing
    /// from the other ninety.
    /// <para>
    /// <c>documentation-generate.scm</c> is not a library: its whole body is output
    /// generation, and it ends by opening <c>markup-commands.tely</c>,
    /// <c>type-predicates.tely</c> and half a dozen more for writing. Upstream runs it
    /// as a SCRIPT — <c>lilypond scm/documentation-generate.scm</c> — when it builds the
    /// manual, and "loading" it means writing those files into the working directory.
    /// So it is excluded on purpose rather than blocked: a fence that loaded it would be
    /// asserting that a test run may litter the tree.
    /// </para>
    /// <para>
    /// OPEN FOR JEREMY: gate G6 is worded "91 of 91 scm/ files load". On this reading it
    /// closes at 90 of 91 plus one script. If the intended reading is that the script
    /// must run too, that is a harness question — where its output goes — and not an
    /// engine one.
    /// </para>
    /// </summary>
    private const string DocumentationScript = "documentation-generate";

    private static LoadReport Loaded()
    {
        lock (Gate)
        {
            if (_report == null)
            {
                LoadReport report = null;

                // psyntax recurses hard enough to overflow the default stack, and the
                // init layer expands a great deal of it.
                Interpreter.RunWithLargeStack(() =>
                {
                    Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                    LilyPondScheme.LoadViaLilyScm(interpreter);
                    new LilyParserSession(interpreter).LoadInitLayer();
                    report = LilyPondScheme.LoadToFixpoint(interpreter, ParserGatedFiles);
                });

                _report = report;
            }

            return _report;
        }
    }

    [Fact]
    public void the_parser_gated_scm_file_loads_once_the_init_layer_has_run()
    {
        //Arrange / Act
        LoadReport report = Loaded();

        //Assert
        // hyphenate-internal-words.scm reads $defaultlayout, which only exists once
        // declarations-init.ly has been parsed, and then hyphenates every context and
        // grob name through ly:regex-replace with a PROCEDURE replacement. Both halves
        // had to be real for this to pass: the second one is what made ly:regex-exec
        // return a GOOPS <regex-match> rather than a bare match object.
        report.Loaded.Should().Contain("hyphenate-internal-words");
        report.Failed.ContainsKey("hyphenate-internal-words").Should().BeFalse(
            "it reported: " + string.Join(" || ", Describe(report)));
    }

    [Fact]
    public void the_documentation_script_is_the_only_scm_file_left_and_it_is_a_script()
    {
        //Arrange
        // EQUALITY on the failure set, so an unblocked file cannot be absorbed silently
        // and a newly-blocked one cannot hide. See DocumentationScript for why this one
        // is excluded by KIND rather than blocked by a missing engine piece.
        //Act
        LoadReport report = Loaded();

        //Assert
        List<string> failed = new List<string>(report.Failed.Keys);
        failed.Should().Equal(new[] { DocumentationScript });
        report.Loaded.Count.Should().Be(ParserGatedFiles.Length - 1);
    }

    private static IEnumerable<string> Describe(LoadReport report)
    {
        foreach (KeyValuePair<string, string> entry in report.Failed)
        {
            yield return entry.Key + ": " + entry.Value;
        }
    }
}
