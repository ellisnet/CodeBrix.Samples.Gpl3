// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// Fences for the boot expansion cache: a REPLAYED boot must be observably a replay,
/// answer exactly what the recording boot answered, and still expand NEW code that
/// uses the layer's Scheme-defined macros — the ly-syntax-constructors regression
/// (2026-08-12), where mode-e recordings rebuilt every value binding but no macro. A
/// corrupted or missing cache file must fall back to recording, never fail a boot.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class BootExpansionCacheTests
{
    [Fact]
    public void the_directory_override_and_the_disable_switch_are_honored()
    {
        //Arrange
        string overrideDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string dirBefore = Environment.GetEnvironmentVariable(BootExpansionCache.DirectoryVariable);
        string enabledBefore = Environment.GetEnvironmentVariable(BootExpansionCache.EnabledVariable);
        try
        {
            //Act + Assert
            Environment.SetEnvironmentVariable(BootExpansionCache.DirectoryVariable, overrideDir);
            BootExpansionCache.CacheDirectory.Should().Be(overrideDir);

            Environment.SetEnvironmentVariable(BootExpansionCache.EnabledVariable, "0");
            BootExpansionCache.Enabled.Should().BeFalse();
            (BootExpansionCache.Acquire() == null).Should().BeTrue();

            // CONTROL: any other value leaves the cache enabled.
            Environment.SetEnvironmentVariable(BootExpansionCache.EnabledVariable, "1");
            BootExpansionCache.Enabled.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(BootExpansionCache.DirectoryVariable, dirBefore);
            Environment.SetEnvironmentVariable(BootExpansionCache.EnabledVariable, enabledBefore);
            BootExpansionCache.ResetProcessMemo();
        }
    }

    /// <summary>
    /// One test carries the whole record → replay → corrupt cycle because the record
    /// phase is a full live boot (~half a minute); splitting the phases would pay it
    /// once per fact.
    /// </summary>
    [Fact]
    public void a_recorded_boot_replays_identically_and_a_corrupt_file_falls_back()
    {
        //Arrange
        string cacheDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string dirBefore = Environment.GetEnvironmentVariable(BootExpansionCache.DirectoryVariable);
        Interpreter ambientBefore = LilyPondScheme.Current;
        bool recordingWasReplay = true;
        bool recordingSavedFile = false;
        bool replayWasReplay = false;
        string recordedAnswer = null;
        string replayedAnswer = null;
        string liveMacroUseAfterReplay = null;
        bool corruptFellBackToRecorder = false;
        try
        {
            Environment.SetEnvironmentVariable(BootExpansionCache.DirectoryVariable, cacheDir);
            BootExpansionCache.ResetProcessMemo();

            Interpreter.RunWithLargeStack(() =>
            {
                // Record: first boot in a fresh world expands live and saves.
                Interpreter recording = LilyPondScheme.CreateInterpreter();
                recordingWasReplay = recording.ExpansionCache.IsReplay;
                LilyPondScheme.LoadViaLilyScm(recording);
                recordingSavedFile = File.Exists(BootExpansionCache.CacheFilePath);
                SchemeBootstrap.LoadExpanded(
                    recording,
                    "(define-module (fence recorded) #:use-module (lily))"
                    + "(define-public fence-ok (markup? (markup #:simple \"fence\")))",
                    "fence-recorded.scm");
                recordedAnswer = Printer.Write(recording.EvalString(
                    "(module-ref (resolve-module '(fence recorded)) 'fence-ok)", "<fence>"));

                // Replay: forced back to disk, the next boot must be a replay and
                // answer identically.
                BootExpansionCache.ResetProcessMemo();
                Interpreter replaying = LilyPondScheme.CreateInterpreter();
                replayWasReplay = replaying.ExpansionCache.IsReplay;
                LilyPondScheme.LoadViaLilyScm(replaying);
                SchemeBootstrap.LoadExpanded(
                    replaying,
                    "(define-module (fence recorded) #:use-module (lily))"
                    + "(define-public fence-ok (markup? (markup #:simple \"fence\")))",
                    "fence-recorded.scm");
                replayedAnswer = Printer.Write(replaying.EvalString(
                    "(module-ref (resolve-module '(fence recorded)) 'fence-ok)", "<fence>"));

                // The regression fence: a NEW module, guaranteed absent from every
                // recording, LIVE-expanding through a LAYER-defined macro after a
                // replayed boot. Mode-e recordings died exactly here — an
                // unbound-variable throw, because no macro survived replay.
                SchemeBootstrap.LoadExpanded(
                    replaying,
                    "(define-module (fence live) #:use-module (lily))"
                    + "(define-public fence-live-ok (markup? (markup #:simple \"fence-live\")))",
                    "fence-live.scm");
                liveMacroUseAfterReplay = Printer.Write(replaying.EvalString(
                    "(module-ref (resolve-module '(fence live)) 'fence-live-ok)", "<fence>"));

                // Corrupt: overwrite the file with garbage; acquiring must fall back
                // to an empty recording instance, never fail.
                File.WriteAllBytes(BootExpansionCache.CacheFilePath, new byte[] { 1, 2, 3, 4 });
                BootExpansionCache.ResetProcessMemo();
                var fallback = BootExpansionCache.Acquire();
                corruptFellBackToRecorder = fallback != null && !fallback.IsReplay && fallback.FileCount == 0;
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(BootExpansionCache.DirectoryVariable, dirBefore);
            BootExpansionCache.ResetProcessMemo();
            LilyPondScheme.RestoreAmbient(ambientBefore);
            try
            {
                Directory.Delete(cacheDir, true);
            }
            catch (IOException)
            {
                // Leftover temp cache directories are harmless.
            }
        }

        //Assert
        recordingWasReplay.Should().BeFalse();
        recordingSavedFile.Should().BeTrue();
        replayWasReplay.Should().BeTrue();
        recordedAnswer.Should().Be("#t");
        replayedAnswer.Should().Be(recordedAnswer);
        liveMacroUseAfterReplay.Should().Be("#t");
        corruptFellBackToRecorder.Should().BeTrue();
    }
}
