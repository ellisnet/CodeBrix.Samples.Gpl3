// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Text.RegularExpressions;
using CodeBrix.LilyPort.Engine.Bootstrap;
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
/// //was previously: a note here said the multi-segment count was deliberately NOT
/// fenced, because a synthetic three-segment staff rendered 15 staff lines on the
/// pinned oracle and only 5 in the port — that residue was defect D11, and PARITY 7
/// (2026-08-14, the same day) closed it: D11 and D2 were ONE defect,
/// <c>Spanner::set_bound</c>'s missing double dispatch. The note went stale unretired
/// (trap 18). RE-MEASURED 2026-08-18 on both engines: oracle 15, port 15 — so the
/// fence the note said would have to be written red is now written GREEN below.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class StaffSpanEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

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

    [Fact]
    public void three_stop_and_restart_segments_draw_three_staff_symbols_worth()
    {
        //Arrange
        // The fence the header's retired note owed. Expected value read off the pinned
        // ORACLE (rule 35), re-measured 2026-08-18: three stop/restart segments draw
        // 15 staff lines. Written red at PARITY 6 it would have been — the port then
        // drew 5 — and D11's close (PARITY 7, Spanner::set_bound's double dispatch) is
        // what makes it green.
        const string Segment =
            "\\stopStaff s4 \\startStaff \\bar \"|\" f4 \\bar \"|\" e'4 ";
        const string One = Version
            + "\\score { \\new Staff { " + Segment + "} }\n";
        const string Three = Version
            + "\\score { \\new Staff { " + Segment + Segment + Segment + "} }\n";

        //Act
        int oneSegment = StaffLines(One, "staffspanseg1");
        int threeSegments = StaffLines(Three, "staffspanseg3");

        //Assert
        // The RELATIONSHIP is the claim (rule 33): each restarted segment gets its own
        // StaffSymbol's worth of lines, so three segments draw exactly three times the
        // one-segment answer — and the absolute count is the oracle's own 15.
        threeSegments.Should().Be(3 * oneSegment);
        threeSegments.Should().Be(15);
    }
}
