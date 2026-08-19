// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.IO;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The <c>-dwarning-as-error</c> WIRING (2026-08-18) — the half the flower suite
/// cannot fence, because it is the engine that installs
/// <see cref="Warn.WarningAsErrorSource"/> over the option store.
/// <para>
/// Upstream assigns <c>flower/warn.cc:45</c>'s global from
/// <c>lily/program-option-scheme.cc:112-115</c>; the port reads the store LIVE
/// instead, because <c>ly:reset-options</c> and the per-file session restore both
/// write option values without going through <c>ly:set-option</c> — a mirrored bool
/// would go stale exactly there and stick the promotion on for every later file
/// (trap 16's shape). The restore case below is that fence.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class WarningAsErrorTests
{
    [Fact]
    public void the_option_promotes_a_warning_and_the_bypassing_restore_demotes_it()
    {
        //Arrange
        Interpreter ambientBefore = LilyPondScheme.Current;
        TextWriter savedOutput = Warn.Output;
        try
        {
            Warn.Output = TextWriter.Null;
            Interpreter.RunWithLargeStack(() =>
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                ProgramOptions options = LilyPondScheme.Options;

                // THE CONTROL, MEASURED FIRST: at lily.scm:484's declared default (#f)
                // a warning returns normally, so the promotion below is the option's
                // doing and not a coincidence of the boot.
                interpreter.EvalString("(ly:warning \"not promoted\")", "<wae>");

                IReadOnlyDictionary<string, object> snapshot = options.SnapshotValues();

                //Act
                interpreter.EvalString("(ly:set-option 'warning-as-error #t)", "<wae>");

                //Assert
                // Upstream: warning -> deferrable_error with no scope open ->
                // print "fatal error:" and exit(1) (flower/warn.cc:260-261, 210-216).
                // The port's exit is the exception.
                Assert.Throws<LilyPondErrorException>(
                    () => interpreter.EvalString("(ly:warning \"promoted\")", "<wae>"));

                // THE RESTORE HALF, which is the whole reason the wiring is a live
                // read: RestoreValues writes the store DIRECTLY — the per-file
                // session reset's path, which never goes through ly:set-option — so
                // a set-time mirror would still answer true here. The live read must
                // demote the moment the stored value does.
                options.RestoreValues(snapshot);
                interpreter.EvalString("(ly:warning \"demoted again\")", "<wae>");
            });
        }
        finally
        {
            Warn.Output = savedOutput;
            LilyPondScheme.RestoreAmbient(ambientBefore);
        }
    }
}
