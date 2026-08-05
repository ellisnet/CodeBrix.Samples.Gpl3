// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Parsing.Session;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// The <c>ly/</c> init layer's RATCHET: it loads clean, and it stays clean.
/// <para>
/// This began life as a throwaway sweep that turned "the layer fails" into a per-file
/// list, which is the only reason the layer's 79 parse errors were tractable — a single
/// aggregate count says nothing about which of 62 files is wrong, and the errors clustered
/// by CAUSE rather than by file. Now that the count is zero it is a fence rather than a
/// probe: any regression has to name the file and the message it broke, which is the
/// diagnostic the throwaway version had to be rebuilt by hand to get.
/// </para>
/// <para>
/// The interpreter is process-global (plan risk 7), so this serialises with the other
/// load fences on the "LilyPondScheme" collection.
/// </para>
/// </summary>
[Collection("LilyPondScheme")]
public class InitLayerProbeTests
{
    private static readonly object Gate = new object();
    private static ParseOutcome _outcome;

    private static ParseOutcome Loaded()
    {
        lock (Gate)
        {
            if (_outcome == null)
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                LilyPondScheme.LoadViaLilyScm(interpreter);
                _outcome = new LilyParserSession(interpreter).LoadInitLayer();
            }

            return _outcome;
        }
    }

    [Fact]
    public void the_init_layer_loads_with_no_diagnostics_at_all()
    {
        //Arrange / Act
        ParseOutcome outcome = Loaded();

        //Assert
        // The message lists what broke, because an aggregate count cannot be acted on:
        // every one of the original 79 pointed at a defect somewhere else entirely —
        // the base lexer mode, a terminal's name, a missing core binding, an unregistered
        // LY_DEFINE, and module-add! wrapping a variable it was handed.
        outcome.AllDiagnostics().Should().BeEmpty(
            "the ly/ init layer must load clean; it reported: "
            + string.Join(" || ", outcome.AllDiagnostics()));
        outcome.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void every_diagnostic_the_layer_could_report_names_its_file_and_line()
    {
        //Arrange
        // The ratchet is only usable while a diagnostic is LOCATED. An unlocated message
        // ("error: ...") tells a future session that something is wrong and nothing about
        // where, which is the state the layer was in before Input origins became real.
        ParseOutcome outcome = Loaded();

        //Act
        List<string> unlocated = new List<string>();
        foreach (string message in outcome.AllDiagnostics())
        {
            if (!message.Contains(".ly:"))
            {
                unlocated.Add(message);
            }
        }

        //Assert
        unlocated.Should().BeEmpty();
    }

    /// <summary>
    /// Every file <c>declarations-init.ly</c> names is opened.
    /// <para>
    /// "Loads clean" is worth nothing on its own: a layer that stopped early would report
    /// no diagnostics for the files it never opened. This is the companion check, and it
    /// is the reason the count matters — a run that aborted at the first <c>\include</c>
    /// scores zero errors too.
    /// </para>
    /// <para>
    /// NOTE the two different numbers, because they have been conflated before. SIXTY-TWO
    /// files are vendored under <c>Scheme/ly/</c>; <c>declarations-init.ly</c> names
    /// EIGHTEEN of them directly and the transitive set it actually opens is TWENTY-TWO.
    /// The rest of the 62 are reached by <c>init.ly</c>'s session lifecycle, by
    /// <c>\include</c> from a user file, or not at all — so "the 62-file layer" is a count
    /// of what is shipped, never of what a load touches.
    /// </para>
    /// </summary>
    [Fact]
    public void the_layer_opens_every_file_declarations_init_names()
    {
        //Arrange
        Interpreter interpreter = LilyPondScheme.CreateInterpreter();
        LilyPondScheme.LoadViaLilyScm(interpreter);
        LilyParserSession session = new LilyParserSession(interpreter);

        //Act
        session.LoadInitLayer();
        List<string> opened = new List<string>();
        foreach (Engine.Origins.SourceFile file in session.SourceFiles)
        {
            opened.Add(file.Name);
        }

        //Assert
        opened.Should().Contain("declarations-init.ly");
        foreach (string included in DirectIncludes)
        {
            opened.Should().Contain(included);
        }

        // ...and one that only a TRANSITIVE include reaches: midi-init.ly pulls it in,
        // so its presence is what says the walk did not stop at depth one.
        opened.Should().Contain("performer-init.ly");
    }

    /// <summary>
    /// The files <c>declarations-init.ly</c> names in its own text, in order.
    /// </summary>
    private static readonly string[] DirectIncludes =
    {
        "music-functions-init.ly", "toc-init.ly", "drumpitch-init.ly",
        "chord-modifiers-init.ly", "script-init.ly", "chord-repetition-init.ly",
        "scale-definitions-init.ly", "dynamic-scripts-init.ly", "spanners-init.ly",
        "predefined-fretboards-init.ly", "string-tunings-init.ly", "property-init.ly",
        "grace-init.ly", "midi-init.ly", "paper-defaults-init.ly",
        "context-mods-init.ly", "ancient-init.ly", "engraver-init.ly",
    };
}
