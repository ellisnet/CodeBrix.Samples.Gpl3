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
/// <c>Context::check_removal</c> runs, so <c>RemoveContext</c> actually fires.
/// </summary>
/// <remarks>
/// <para>
/// The port's <c>CheckRemoval</c> was a faithful translation that NOTHING CALLED.
/// Upstream drives it from <c>Global_context::run_iterator_on_me</c> twice — at the end
/// of every timestep and again after the iterator quits — and both calls were missing,
/// so the event was defined, its listeners were wired, and it never fired.
/// </para>
/// <para>
/// Almost nothing listens for it, which is why this stayed silent through every session
/// up to EPG14. <c>recording-group-emulate</c> (scm/part-combiner.scm) is the port's
/// first listener, and its handler exists solely to append the END MOMENT to the
/// recorded event list. Without that entry, <c>make-autochange-music</c> ends its
/// recursion with <c>rest-mom</c> still <c>#f</c> and calls
/// <c>(skip-of-moment-span prev-change-mom #f)</c>, which dies inside
/// <c>ly:moment-sub</c>.
/// </para>
/// <para>
/// THE EXPECTED COUNT BELOW WAS MEASURED AGAINST THE ORACLE, not recorded from this
/// port: instrumenting <c>recording-group-emulate</c> under LilyPond 2.27.2 for this
/// exact music reports TEN entries, the last at moment 2; the port reported nine. A
/// characterization test taken from the port would have locked in the nine.
/// </para>
/// </remarks>
[Collection("engine-global-state")]
public class ContextRemovalEndToEndTests
{
    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-removal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void the_recorded_event_list_carries_the_end_moment()
    {
        //Arrange
        // Two whole notes of music, so the end moment is 2. The recorded list is built
        // newest-first, so its FIRST entry is the last moment in time — and that entry
        // is the one only RemoveContext can produce.
        string source =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "#(define seen #f)\n"
            + "#(define orig recording-group-emulate)\n"
            + "#(set! recording-group-emulate\n"
            + "   (lambda (music odef)\n"
            + "     (let ((r (orig music odef)))\n"
            + "       (set! seen (cons (length (cdar r)) (caaar (cdar r))))\n"
            + "       r)))\n"
            + "\\context PianoStaff <<\n"
            + "  \\context Staff = \"up\" { \\autoChange \\new Voice"
            + " << \\relative { g4 c e d c8 r r4 a g } >> }\n"
            + "  \\context Staff = \"down\" { \\clef bass s1*2 }\n"
            + ">>\n"
            + "#(if (not (and (= (car seen) 10) (equal? (cdr seen) (ly:make-moment 2))))\n"
            + "     (ly:error \"recorded ~a entries ending at ~a\" (car seen) (cdr seen)))\n";

        //Act
        BatchRunResult result = BatchRunner.RunText(
            source, "removal-recorded-list", null, ScratchDirectory());

        //Assert
        // ly:error above would surface as a diagnostic; an empty diagnostic string means
        // the count and the end moment both matched the oracle's.
        string.Join(" || ", result.Diagnostics).Should().Be(string.Empty);
        result.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void autochange_music_engraves_to_a_page()
    {
        //Arrange
        // The end-to-end consequence, with a CONTROL that must engrave either way: if
        // \autoChange were the only thing broken, the control still passes, and if the
        // whole path were broken, both fail and the test says so rather than passing
        // for the wrong reason.
        string control =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\context PianoStaff <<\n"
            + "  \\context Staff = \"up\" { \\new Voice"
            + " << \\relative { g4 c e d c8 r r4 a g } >> }\n"
            + "  \\context Staff = \"down\" { \\clef bass s1*2 }\n"
            + ">>\n";
        string autoChanged = control.Replace(
            "\\new Voice <<", "\\autoChange \\new Voice <<", StringComparison.Ordinal);

        //Act
        BatchRunResult plain = BatchRunner.RunText(
            control, "removal-control", null, ScratchDirectory());
        BatchRunResult changed = BatchRunner.RunText(
            autoChanged, "removal-autochange", null, ScratchDirectory());

        //Assert
        plain.SvgPath.Should().NotBeNull();
        changed.SvgPath.Should().NotBeNull();
        string.Join(" || ", changed.Diagnostics).Should().Be(string.Empty);
        File.ReadAllText(changed.SvgPath).Should().Contain("<svg");
    }
}
