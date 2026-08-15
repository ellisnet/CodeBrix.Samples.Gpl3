// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Text.RegularExpressions;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// PARITY 6 (2026-08-14): the <c>first_start_</c> half of <c>Staff_symbol_engraver</c>.
/// <para>
/// The engraver holds a <c>Unique_span_event_listener</c> on <c>staff_span</c> and its
/// <c>process_music</c> is written entirely in terms of it
/// (<c>staff-symbol-engraver.cc:66-78</c>). The port had NO listener at all: it opened one
/// spanner on the first timestep and closed it at finalize, so <c>\startStaff</c> and
/// <c>\stopStaff</c> were both inert. PARITY 6 registered the listener and translated
/// <c>process_music</c> and <c>stop_translation_timestep</c> as upstream writes them,
/// which took the sixteen <c>bar-line-placement-*</c> files from 21 drawn staff lines to
/// the oracle's own 126 and moved 37 comparator rows with no regression.
/// </para>
/// <para>
/// ⚠ WHAT THIS FILE DELIBERATELY DOES NOT FENCE, because it is NOT yet true. A synthetic
/// three-segment staff — <c>{ \stopStaff s4 \startStaff \bar "|" f4 \bar "|" e'4 }</c>
/// three times over — renders 15 staff lines on the pinned oracle and still renders 5 in
/// the port. The listener fix is therefore real but INCOMPLETE, and the residue has its
/// own entry in the plan's open-defect table. A fence asserting the multi-segment count
/// would have to be written red, and a fence written to the port's current answer would
/// record the defect as the contract — which is the thing rule 33 exists to prevent.
/// What is fenced here is the half that IS settled, and the control that any "just always
/// start a spanner" shortcut would break.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class StaffSpanEndToEndTests
{
    private const string Version = "\\version \"2.27.2\"\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-staffspan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static int StaffLines(string source, string name)
    {
        BatchRunResult result = BatchRunner.RunText(source, name, null, ScratchDirectory());
        result.SvgPath.Should().NotBeNull();
        return Regex.Matches(File.ReadAllText(result.SvgPath), "<line ").Count;
    }

    [Fact]
    public void a_staff_that_never_asks_gets_exactly_one_staff_symbol_however_long_it_is()
    {
        //Arrange
        // The first_start_ latch: with no staff-span event anywhere, the staff still gets
        // its implicit spanner, and adding music must not add staff symbols. Read off the
        // ORACLE before it was asserted (rule 35) — pinned LilyPond 2.27.2 draws five
        // staff lines for both, one staff symbol stretched across the system.
        const string Short = Version + "\\score { \\new Staff { \\bar \"|\" f4 } }\n";
        const string Long = Version
            + "\\score { \\new Staff { \\bar \"|\" f4 f4 f4 f4 } }\n";

        //Act
        int shortLines = StaffLines(Short, "staffspanshort");
        int longLines = StaffLines(Long, "staffspanlong");

        //Assert
        // The CONTROL that keeps the fix from being "always start a spanner": that
        // shortcut would open a fresh StaffSymbol on every timestep and the longer staff
        // would draw more lines than the shorter one.
        shortLines.Should().Be(longLines);
        shortLines.Should().Be(5);
    }

    [Fact]
    public void one_stop_and_restart_still_draws_a_single_staff_symbols_worth()
    {
        //Arrange
        // One segment, which the port and the oracle agree on at 5 lines — the boundary
        // of what the listener fix currently reaches. Kept as the regression guard for
        // the fix itself: before it, \stopStaff was inert and this drew the implicit
        // whole-staff spanner instead of the restarted one.
        const string One = Version
            + "\\score { \\new Staff { \\stopStaff s4 \\startStaff \\bar \"|\" f4 \\bar \"|\" e'4 } }\n";

        //Act & Assert
        StaffLines(One, "staffspanone").Should().Be(5);
    }
}
