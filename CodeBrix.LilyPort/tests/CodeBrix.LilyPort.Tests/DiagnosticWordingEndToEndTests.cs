// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Linq;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// PARITY 6 (2026-08-14): the two diagnostics ruling R1 names, fenced by their upstream
/// text and their upstream SEVERITY.
/// <para>
/// R1 is general — a diagnostic with an upstream counterpart reproduces upstream's wording
/// and severity verbatim, and a genuinely port-only one is free to say whatever is
/// clearest. Nothing in this project grades that (trap 1a), which is how the type-check
/// message came to differ from upstream's in BOTH respects for the life of the project.
/// </para>
/// <para>
/// Read off the ORACLE before it was asserted (rule 35). <c>lily-guile.cc:258-267</c>:
/// when <c>ly_call (type, val)</c> is false, upstream emits a <c>warning</c> — not a
/// <c>programming_error</c> — reading
/// <c>the property 'X' must be of type 'T', ignoring invalid value 'V'</c>, where T comes
/// from the vendored <c>type-name</c> (<c>c++.scm:309</c>) and V from
/// <c>print_scm_val</c>, which was simply unported.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class DiagnosticWordingEndToEndTests
{
    private const string Version = "\\version \"2.27.2\"\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-diagwording-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Runs an action with <see cref="Warn"/> recording, and returns what it said.</summary>
    /// <param name="action">The work to run.</param>
    /// <returns>The recorded messages.</returns>
    private static string[] Recorded(Action action)
    {
        bool wasRecording = Warn.RecordMessages;
        Warn.RecordMessages = true;
        Warn.ClearMessages();
        try
        {
            action();
            return Warn.Messages.ToArray();
        }
        finally
        {
            Warn.RecordMessages = wasRecording;
            Warn.ClearMessages();
        }
    }

    [Fact]
    public void a_failed_property_type_check_says_what_upstream_says()
    {
        //Arrange
        // thickness is declared number?; a symbol is not one, so value_type_check refuses
        // it. The CONTROL is the same override with a legal value, which must produce no
        // such message at all — without it this would pass against an engine that emitted
        // the sentence unconditionally.
        const string Bad = Version
            + "\\score { { c'1 } \\layout { \\context { \\Score\n"
            + "  \\override Stem.thickness = #'not-a-number } } }\n";
        const string Good = Version
            + "\\score { { c'1 } \\layout { \\context { \\Score\n"
            + "  \\override Stem.thickness = #2.5 } } }\n";

        //Act
        // The type check runs at ENGINE time, so its message goes through Warn rather
        // than into the parser session's own diagnostics that BatchRunResult carries.
        string[] badMessages = Recorded(() =>
            BatchRunner.RunText(Bad, "typecheckbad", null, ScratchDirectory()));
        string[] goodMessages = Recorded(() =>
            BatchRunner.RunText(Good, "typecheckgood", null, ScratchDirectory()));

        //Assert
        string message = badMessages.FirstOrDefault(d => d.Contains("must be of type"));
        message.Should().NotBeNull();

        // Upstream's sentence, verbatim, and upstream's severity: a warning.
        message.Should().Contain("the property 'thickness' must be of type 'number'");
        message.Should().Contain("ignoring invalid value 'not-a-number'");
        message.Should().Contain("warning: ");
        message.Should().NotContain("programming error");

        // The wording the port used to emit is gone.
        message.Should().NotContain("Type check for");

        // The control render says nothing of the kind.
        goodMessages.Should().NotContain(d => d.Contains("must be of type"));
    }

    [Fact]
    public void ly_find_file_raises_upstream_s_fatal_error_only_when_strict()
    {
        //Arrange
        // Upstream's ly:find-file returns #f for a missing file by default and raises a
        // fatal error when the optional STRICT flag is #t (general-scheme.cc:55-77). The
        // port ignored the flag entirely, which is why \markup \image and \verbatim-file
        // produced nothing where the oracle stops the run — D9's MISSING (ii).
        const string Lax = Version
            + "#(define answer (if (ly:find-file \"no-such-asset-8f2a.png\") \"FOUND\" \"HASH-F\"))\n"
            + "\\markup #answer\n";
        const string Strict = Version
            + "\\markup \\verbatim-file \"no-such-asset-8f2a.txt\"\n";

        //Act
        BatchRunResult lax = null;
        string[] laxMessages = Recorded(
            () => lax = BatchRunner.RunText(Lax, "findfilelax", null, ScratchDirectory()));
        string[] strictMessages = Recorded(
            () => BatchRunner.RunText(Strict, "findfilestrict", null, ScratchDirectory()));

        //Assert
        // The CONTROL: without strict the answer is #f and the run completes normally.
        lax.SvgPath.Should().NotBeNull();
        File.ReadAllText(lax.SvgPath).Should().Contain("HASH-F");
        laxMessages.Should().NotContain(d => d.Contains("cannot find file"));

        // With strict the run is stopped, and says so in upstream's words.
        string message = strictMessages.FirstOrDefault(d => d.Contains("cannot find file"));
        message.Should().NotBeNull();
        message.Should().Contain("cannot find file 'no-such-asset-8f2a.txt'");
        message.Should().Contain("load path: ");
        message.Should().Contain("cwd: ");
    }
}
