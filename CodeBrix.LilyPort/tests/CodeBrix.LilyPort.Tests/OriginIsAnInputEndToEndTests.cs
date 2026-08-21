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
/// PARITY 5 (2026-08-14) end to end: the <c>origin</c> property of anything the PARSER
/// built must satisfy <c>ly:input-location?</c>.
/// <para>
/// Upstream has exactly ONE location type. <c>@$</c> is an <c>Input</c>,
/// <c>set_spot</c> takes an <c>Input</c>, <c>Music::origin</c> answers one, and
/// <c>ly:input-location?</c> is the predicate <c>origin</c> is DECLARED with in
/// <c>define-music-properties.scm</c>. Here the parser's own location is a
/// <c>SourceSpan</c>, a Parsing-layer struct the Engine has never heard of, so the two
/// are different types and every grammar action that stamps an origin owes a
/// conversion.
/// </para>
/// <para>
/// The defect this fences: eight grammar actions stamped the RAW span —
/// <c>construct-chord-elements</c>, the <c>\context</c> block, both <c>\book</c>
/// blocks, <c>\score</c>, and three <c>\paper</c>/<c>\layout</c> rules. The type check
/// then failed on every music object the parser built, which is 3,263 of the port's
/// 4,274 diagnostic lines — 76% of "the port says eleven times as much as the oracle" —
/// and left <c>Music::origin</c> answering nothing, so a diagnostic about a note could
/// not say WHERE the note was. It moved no comparator row, which is exactly why it
/// survived: trap 1a, the ungraded gap.
/// </para>
/// <para>
/// Read off the ORACLE before it was asserted (rule 35): pinned LilyPond 2.27.2,
/// run under the corpus's own font pinning, answers Y for the sequential music, the
/// note event inside it, the event chord and the chord's own note events, and N for a
/// bare <c>ly:make-music</c>. That last one is the CONTROL and it is load bearing —
/// a stub predicate that answered #t to everything (trap 9) would pass all four Y rows
/// and fail only this one.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class OriginIsAnInputEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    /// <summary>
    /// Asks <c>ly:input-location?</c> about five origins and renders the answers as one
    /// space-free token, so the whole verdict survives as a single SVG text run.
    /// </summary>
    private const string Source = Version
        + "#(define (loc? m)"
        + "   (if (ly:input-location? (ly:music-property m 'origin)) \"Y\" \"N\"))\n"
        + "seq = { c'1 }\n"
        + "chd = { <c' e'>1 }\n"
        + "#(define answer\n"
        + "   (let* ((note (car (ly:music-property seq 'elements)))\n"
        + "          (ch (car (ly:music-property chd 'elements))))\n"
        + "     (string-append \"SEQ=\" (loc? seq)\n"
        + "                    \"|NOTE=\" (loc? note)\n"
        + "                    \"|CHORD=\" (loc? ch)\n"
        + "                    \"|INNER=\" (loc? (car (ly:music-property ch 'elements)))\n"
        + "                    \"|BARE=\" (loc? (ly:make-music 'NoteEvent)))))\n"
        + "\\markup #answer\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-origin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void every_origin_the_parser_stamps_answers_the_input_location_predicate()
    {
        //Arrange & Act
        BatchRunResult result = BatchRunner.RunText(
            Source, "origin", null, ScratchDirectory());

        //Assert
        result.SvgPath.Should().NotBeNull();
        string svg = File.ReadAllText(result.SvgPath);

        Match verdict = Regex.Match(svg, "<tspan>(SEQ=[^<]*)</tspan>");
        verdict.Success.Should().BeTrue();

        // The oracle's own answers. The four Y's are the fix; the N is the control that
        // keeps the predicate honest.
        verdict.Groups[1].Value.Should().Be("SEQ=Y|NOTE=Y|CHORD=Y|INNER=Y|BARE=N");
    }

    [Fact]
    public void stamping_an_origin_no_longer_trips_the_property_type_check()
    {
        //Arrange & Act
        BatchRunResult result = BatchRunner.RunText(
            Source, "origin", null, ScratchDirectory());

        //Assert
        // The other half of the same defect, and the half that made the noise: a
        // property whose type check fails is DISCARDED, so before the fix every music
        // object both failed this check and ended up with no origin at all. Counting
        // the diagnostic against a rendered CONTROL rather than against a bare zero
        // (rule 34) is not available here — there is no render in which the message is
        // legitimate — so it is asserted absent by name.
        //was previously: NotContain(d => d.Contains("Type check for `origin'"))
        // RESTATED at PARITY 6 (rule 33): D8 replaced that wording with upstream's own
        // sentence, so the old literal could no longer match anything and the fence had
        // quietly stopped fencing.
        result.Diagnostics.Should().NotContain(
            d => d.Contains("the property 'origin' must be of type"));
    }
}
