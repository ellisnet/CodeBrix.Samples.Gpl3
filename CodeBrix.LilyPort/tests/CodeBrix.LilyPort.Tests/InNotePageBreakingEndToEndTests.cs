// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using CodeBrix.LilyPort.Engine.Bootstrap;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// PARITY 12's fence for the last of D37: a system's FOOTNOTES and IN-NOTES have to cost
/// the page breaker height.
/// <para>
/// <c>Line_details</c> carries <c>footnote_heights_</c> and <c>in_note_heights_</c>, and
/// <c>Page_spacing::account_for_footnotes</c> adds both to what a page demands.
/// <c>Constrained_breaking</c>'s per-system fill is what puts them there, from
/// <c>System::get_footnote_heights_in_range</c> and
/// <c>get_in_note_heights_in_range</c> — and the port had NEITHER method and made
/// NEITHER assignment. Both vectors were constructed empty, copied faithfully when a
/// line was compressed, and read faithfully by the spacer, which is trap 17a exactly: a
/// read side whose input nothing produces, and it leaves no stub and no ledger row.
/// (The one place the port did fill <c>FootnoteHeights</c> is the <c>Line_details</c>
/// constructor for a title PROB, which is a different overload and is why grepping for
/// the name looked reassuring — trap 17b's shape.)
/// </para>
/// <para>
/// The visible cost was a page count: notes that take no height let a page hold music it
/// cannot hold, so <c>in-note-configuration</c> came out on ONE page where the oracle
/// needs two. The corpus page total moved 2315 -> 2316, which is the oracle's own count.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class InNotePageBreakingEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-innote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static int PageCount(string source, string name)
    {
        BatchRunResult result = BatchRunner.RunText(
            source, name, null, ScratchDirectory());

        result.SvgPaths.Should().NotBeNull();
        return result.SvgPaths.Count;
    }

    /// <summary>
    /// A page of music on a small sheet, with the caller's material appended to each
    /// note. The paper is deliberately tight so a modest extra height changes the count.
    /// </summary>
    private static string Source(string perNote)
        => Version
        + "#(set-default-paper-size \"a8\")\n"
        + "\\book {\n"
        + "  \\relative c' {\n"
        + "    \\repeat unfold 4 {\n"
        + perNote
        + "      a b c d\n"
        + "    }\n"
        + "  }\n"
        + "}\n";

    [Fact]
    public void in_notes_add_height_and_so_add_a_page()
    {
        //Arrange
        // Each in-note is a \footnote with footnote = ##f, which is what makes it an
        // IN-note rather than a foot-of-page one -- the same distinction
        // internal_get_note_heights_in_range switches on.
        string withNotes = Source(
            "      \\once \\override Score.Footnote.footnote = ##f\n"
            + "      \\footnote \"\" #'(0 . 0)"
            + " \\markup { \"an in-note that takes room\" } NoteHead\n");
        string without = Source(string.Empty);

        //Act
        int withCount = PageCount(withNotes, "innote-with");
        int withoutCount = PageCount(without, "innote-without");

        //Assert
        // The RELATIONSHIP is the fact: the notes have to cost something. Asserting a
        // literal page count would fence the paper size instead. Before the fix both
        // answered the same number, because the heights never reached the spacer.
        withCount.Should().BeGreaterThan(withoutCount);
    }

    [Fact]
    public void the_same_music_without_in_notes_is_the_control()
    {
        //Arrange
        // THE CONTROL that makes the comparison mean something: the bare music must fit
        // on ONE page, so the extra page above is the in-notes' doing and not the music
        // spilling over on its own.
        string without = Source(string.Empty);

        //Act
        int count = PageCount(without, "innote-control");

        //Assert
        count.Should().Be(1);
    }
}
