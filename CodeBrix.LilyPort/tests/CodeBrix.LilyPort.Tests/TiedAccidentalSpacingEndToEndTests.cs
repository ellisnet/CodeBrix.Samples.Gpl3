// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CodeBrix.LilyPort.Engine.Bootstrap;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// PARITY 16 (2026-08-16) end to end: an accidental suppressed by a tie is a break
/// REMINDER and must reserve no horizontal width.
/// <para>
/// Upstream routes an accidental placement and an arpeggio onto a paper column's
/// <c>conditional-elements</c> rather than its <c>elements</c>
/// (<c>lily/paper-column-engraver.cc</c>), and <c>Separation_item::boxes</c> then
/// filters them through <c>Accidental_placement::get_relevant_accidentals</c>. That
/// function calls <c>split_accidentals</c>, whose whole test is
/// </para>
/// <code>
///     unsmob&lt;Grob&gt; (get_object (a, "tie")) &amp;&amp; !from_scm&lt;bool&gt; (get_property (a, "forced"))
/// </code>
/// <para>
/// — a tied, UNFORCED accidental is a break reminder, and a break reminder only costs
/// width when it begins a line, where it is actually shown. The port put every item on
/// <c>elements</c>, so the filter was never reached and the width of an accidental that
/// is never drawn was reserved in every column. Its own comment said
/// <c>add_conditional_item</c> was unported and that <c>Paper_column::minimum_distance</c>
/// omitted the matching skyline half; both had landed in earlier sessions and nothing
/// re-checked the note (trap 18).
/// </para>
/// <para>
/// THE MATERIAL IS THE REGRESSION FILE'S OWN, not invented (rule 35b):
/// <c>spacing-accidental-tie.ly</c> is built from <c>cis</c> sixteenths in 1/4, tied
/// across the bar, and it is that file's own second phrase that supplies the forced
/// case (<c>cis!</c>). What is asserted are RELATIONSHIPS, read off the pinned oracle
/// before being written down (rule 35). For the record, pinned LilyPond 2.27.2 gives a
/// music width of 31.5204 tied, 31.8204 untied and 31.8204 tied-but-forced; the
/// numbers themselves are font metrics and are not asserted.
/// </para>
/// <para>
/// THE CONTROL IS THE FORCED CASE, and it is what makes the claim specific. "Tied is
/// narrower" alone could be satisfied by anything that shrinks a tied bar. Forcing the
/// accidental with <c>cis!</c> sets <c>forced</c>, which takes the accidental OUT of
/// the break-reminder class by upstream's own test, so the width must come back to
/// exactly the untied width. A second control holds the tie and removes the
/// accidental (<c>c</c> instead of <c>cis</c>): with nothing to suppress, the tie
/// itself must change nothing, which is what shows the effect belongs to the
/// accidental rather than to the tie.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class TiedAccidentalSpacingEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    /// <summary>
    /// Eight sixteenths in 1/4 — two bars — as <c>spacing-accidental-tie.ly</c> writes
    /// them. <paramref name="music"/> is the only variable.
    /// </summary>
    private static string Source(string music)
        => Version
            + "\\paper { ragged-right = ##t }\n"
            + "\\relative { \\time 1/4 " + music + " }\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-tiedacc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// The width the engraved music occupies: the span of every placed mark's x, with
    /// the page furniture at the bottom of the page excluded.
    /// <para>
    /// Marks are read from the <c>translate</c> of the group each one sits in, which is
    /// the same quantity the comparator grades. The tagline sits far down the page and
    /// does not move with the music, so including it would mask the whole effect.
    /// </para>
    /// </summary>
    private static double MusicWidth(string music, string name)
    {
        BatchRunResult result = BatchRunner.RunText(
            Source(music), name, null, ScratchDirectory());
        result.SvgPath.Should().NotBeNull();

        string svg = File.ReadAllText(result.SvgPath);
        List<double> xs = new List<double>();
        foreach (Match m in Regex.Matches(
            svg, "<g transform=\"translate\\(([-0-9.]+), ([-0-9.]+)\\)\""))
        {
            double x = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double y = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (y < 100.0)
            {
                xs.Add(x);
            }
        }

        // Rule 36: a width computed from no marks is not a width.
        xs.Count.Should().BeGreaterThan(20,
            "the fixture must actually have engraved something to measure");
        return xs.Max() - xs.Min();
    }

    [Fact]
    public void a_tie_suppressed_accidental_reserves_no_width_but_a_forced_one_does()
    {
        //Arrange
        const string Tied = "cis'16 cis cis cis~ cis cis cis cis";
        const string Untied = "cis'16 cis cis cis cis cis cis cis";
        const string TiedForced = "cis'16 cis cis cis~ cis! cis cis cis";

        //Act
        double tied = MusicWidth(Tied, "tied-accidental-spacing-tied");
        double untied = MusicWidth(Untied, "tied-accidental-spacing-untied");
        double forced = MusicWidth(TiedForced, "tied-accidental-spacing-forced");

        //Assert
        // The mechanism: the second bar's accidental is suppressed by the tie, so it is
        // a break reminder and costs nothing, and the line is NARROWER than the untied
        // one that prints it.
        tied.Should().BeLessThan(untied,
            "a tied, unforced accidental is a break reminder and reserves no width");

        // The control: forcing the accidental takes it out of the break-reminder class
        // by upstream's own test, so the width must return to the untied width.
        forced.Should().BeApproximately(untied, 0.001,
            "a forced accidental is printed and must be paid for");
        forced.Should().BeGreaterThan(tied,
            "the forced and unforced tied cases must not agree, or the guard is inert");
    }

    [Fact]
    public void a_tie_with_no_accidental_to_suppress_changes_no_width()
    {
        //Arrange — the same two bars with a pitch that needs no accidental.
        const string Tied = "c'16 c c c~ c c c c";
        const string Untied = "c'16 c c c c c c c";

        //Act
        double tied = MusicWidth(Tied, "tied-accidental-spacing-natural-tied");
        double untied = MusicWidth(Untied, "tied-accidental-spacing-natural-untied");

        //Assert
        // The second control: with no accidental in play the tie must cost nothing
        // either way, which is what shows the effect above belongs to the ACCIDENTAL
        // and not to the tie.
        tied.Should().BeApproximately(untied, 0.001,
            "a tie with no accidental to suppress must not change the spacing");
    }
}
