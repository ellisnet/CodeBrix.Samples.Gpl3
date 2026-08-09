// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// EPG16 end to end: LilyPond text in, a BOOK of pages out, through the real
/// <c>ly:book-process</c> path.
/// <para>
/// This group needs a reachability probe more than most, because every one of its failure
/// modes still produces a page. A breaker stuck on one page, a <c>\layout</c> block that
/// destroyed its own context definitions, an unset that unset nothing — none of them
/// raises, and the first two are indistinguishable from a layout opinion unless the test
/// says what the notation REQUIRES rather than what the port chose.
/// </para>
/// <para>
/// So every fact here is paired with a control that must come out DIFFERENTLY, and the
/// measurements are derivable from the input: how many FILES a book writes (one per page,
/// under the oracle's own naming), and how many staff lines are on them (five per system).
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class PageBreakingEndToEndTests
{
    private const string Version = "\\version \"2.27.2\"\n";

    /// <summary>A page small enough that a handful of systems cannot share it.</summary>
    private const string SmallPaper =
        "\\paper { paper-height = 4.0\\cm  paper-width = 8.0\\cm }\n";

    private const string NarrowLayout = "\\layout { indent = 0.0 line-width = 6.0\\cm }\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-pages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static BatchRunResult Run(string source, string name)
        => BatchRunner.RunText(source, name, null, ScratchDirectory());

    private static int StaffLines(BatchRunResult result)
        => result.SvgPaths.Sum(p => Regex.Matches(File.ReadAllText(p), "<line ").Count);

    [Fact]
    public void a_book_too_tall_for_one_page_is_broken_onto_several()
    {
        //Arrange
        // THIS IS THE SESSION'S OWN TRAP AND IT IS WHY THE ASSERTION IS "MORE THAN ONE"
        // RATHER THAN A COUNT. Page_spacer::calc_subproblem guards its cell update with
        // `page > 0 || page_start == 0'. Upstream passes VPOS — the LARGEST unsigned
        // value — when the page count is unconstrained, so `page > 0' is TRUE. Ported
        // literally against a -1 sentinel that test INVERTS: the only cell ever written
        // is page_start == 0, every line's best solution becomes "put lines 0..line on
        // ONE page", and the solver answers a single page for a book of any height —
        // silently, with a plausible-looking page.
        //
        // An exact page count would be a characterization of this port's spacing and
        // would have to be re-recorded whenever spacing improved. "Twelve forced line
        // breaks cannot share a four-centimetre page" is derivable from the notation, and
        // it is exactly what the inverted test destroys.
        string source = Version + SmallPaper + NarrowLayout
            + "{ " + string.Concat(Enumerable.Repeat("c'1 \\break ", 12)) + "}\n";

        //Act
        BatchRunResult result = Run(source, "pages-many");

        //Assert
        result.SvgPaths.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public void a_book_that_fits_gets_exactly_one_page()
    {
        //Arrange
        // The control, and it is the half that makes the fact above a fact. A breaker
        // that split at every opportunity would pass the test above and fail this one; a
        // breaker stuck on one page passes this one and fails that one. Neither alone
        // says anything about page breaking.
        string source = Version + SmallPaper + NarrowLayout + "{ c'1 }\n";

        //Act
        BatchRunResult result = Run(source, "pages-one");

        //Assert
        result.SvgPaths.Count.Should().Be(1);
    }

    [Fact]
    public void a_single_page_book_is_named_without_a_number()
    {
        //Arrange
        // scm/framework-svg.scm's naming, and it is the ORACLE's: a one-page book is
        // `<base>.svg' and a multi-page one is `<base>-1.svg' upwards, counting from ONE.
        // The comparator pairs a candidate with a reference BY NAME ALONE, so getting
        // this wrong reports every page of every multi-page reference as MISSING however
        // well the music engraved — which is what the port did until EPG16.
        string source = Version + SmallPaper + NarrowLayout + "{ c'1 }\n";

        //Act
        BatchRunResult result = Run(source, "naming-one");

        //Assert
        Path.GetFileName(result.SvgPaths[0]).Should().Be("naming-one.svg");
    }

    [Fact]
    public void a_multi_page_book_numbers_its_pages_from_one()
    {
        //Arrange
        string source = Version + SmallPaper + NarrowLayout
            + "{ " + string.Concat(Enumerable.Repeat("c'1 \\break ", 12)) + "}\n";

        //Act
        BatchRunResult result = Run(source, "naming-many");

        //Assert
        // Counting from ONE, not zero, and with no unnumbered file among them.
        Path.GetFileName(result.SvgPaths[0]).Should().Be("naming-many-1.svg");
        Path.GetFileName(result.SvgPaths[1]).Should().Be("naming-many-2.svg");
        result.SvgPaths.Should().NotContain(p => Path.GetFileName(p) == "naming-many.svg");
    }

    [Fact]
    public void every_system_reaches_a_page_when_the_book_is_split()
    {
        //Arrange
        // The failure this exists for is NOT a wrong page count — it is music that falls
        // off the end. EPG15 found the port drawing one line in four because Engrave()
        // took paperSystems[0] and threw the rest away, and that was invisible while
        // every score was one line. The same shape is available one level up: a page
        // breaker that assigns systems to pages and then writes only the first page's
        // would look entirely reasonable.
        //
        // Twelve forced breaks make twelve systems; five staff lines each is sixty, and
        // they must ALL be on disk somewhere in the book.
        string source = Version + SmallPaper + NarrowLayout
            + "{ " + string.Concat(Enumerable.Repeat("c'1 \\break ", 12)) + "}\n";

        //Act
        BatchRunResult result = Run(source, "pages-complete");

        //Assert
        StaffLines(result).Should().Be(12 * 5);
    }

    [Fact]
    public void a_toplevel_layout_block_with_a_context_modification_still_engraves()
    {
        //Arrange
        // A REGRESSION FENCE for the defect EPG16 found: ly:context-def-modify was never
        // registered, so it answered the inert placeholder — and `context-defs-from-music'
        // writes that straight back with ly:output-def-set-variable!. Every context
        // definition the block touched was OVERWRITTEN with the placeholder. This exact
        // two-line file took the layout from 43 context definitions to 20 and produced NO
        // PAGES AT ALL, reported only as "cannot create default child context".
        //
        // The assertion is that music DRAWS, not that it draws in any particular place:
        // what the override does to the staff is not this test's business.
        string source = Version
            + "\\layout { \\override StaffSymbol.staff-space = 1.23 }\n"
            + "{ c'4 }\n";

        //Act
        BatchRunResult result = Run(source, "toplevel-layout-mod");

        //Assert
        result.SvgPaths.Count.Should().Be(1);
        StaffLines(result).Should().Be(5);
    }

    [Fact]
    public void a_toplevel_layout_block_with_only_a_variable_still_engraves()
    {
        //Arrange
        // The control that was already passing before the fix, and it is worth keeping
        // for exactly that reason: it is what made the defect look like it was not there.
        // A plain variable assignment never reaches context-defs-from-music at all.
        string source = Version + "\\layout { ragged-right = ##t }\n{ c'4 }\n";

        //Act
        BatchRunResult result = Run(source, "toplevel-layout-var");

        //Assert
        result.SvgPaths.Count.Should().Be(1);
        StaffLines(result).Should().Be(5);
    }

    [Fact]
    public void annotate_spacing_draws_its_arrows_instead_of_killing_the_book()
    {
        //Arrange
        // Two words in a \paper block. system.cc's three staff accessors were unported,
        // so they answered the placeholder and scm/paper-system.scm's very next line —
        // (length spaceable-staves) — took the whole book down with "Not a proper list",
        // naming neither the paper variable nor the callback. The arrows themselves need
        // scm/stencil.scm's complex-number arithmetic, which the reader could not even
        // parse until this session.
        string source = Version + "\\paper { annotate-spacing = ##t }\n{ c'4 }\n";

        //Act
        BatchRunResult result = Run(source, "annotate-spacing");

        //Assert
        result.SvgPaths.Count.Should().Be(1);

        // The annotations are real ink, not just an absence of failure: the arrows and
        // their labels put MORE on the page than the same music without them.
        BatchRunResult plain = Run(Version + "{ c'4 }\n", "annotate-spacing-control");
        File.ReadAllText(result.SvgPaths[0]).Length
            .Should().BeGreaterThan(File.ReadAllText(plain.SvgPaths[0]).Length);
    }

    [Fact]
    public void unsetting_a_deprecated_property_takes_effect_in_a_real_score()
    {
        //Arrange
        // The end-to-end shape of the deprecation defect, and the reason it cost a whole
        // file rather than a warning: skipTypesetting goes ON, then OFF through a
        // deprecated alias. If the unset is discarded, typesetting never resumes and the
        // book produces NOTHING — no error, no diagnostic, no page.
        string source = Version
            + "#(define-deprecated-property\n"
            + "  'translation-type? 'deprecatedEpgSixteenUnset boolean?\n"
            + "  #:new-symbol 'skipTypesetting)\n"
            + "\\fixed c' {\n"
            + "  \\set Timing.skipTypesetting = ##t\n"
            + "  R1\n"
            + "  \\unset Timing.deprecatedEpgSixteenUnset\n"
            + "  b1\n"
            + "}\n";

        //Act
        BatchRunResult result = Run(source, "deprecated-unset");

        //Assert
        result.SvgPaths.Count.Should().Be(1);
        StaffLines(result).Should().Be(5);
    }

    [Fact]
    public void skip_typesetting_left_on_really_does_suppress_the_music()
    {
        //Arrange
        // The control, and it must produce NOTHING. Without it the test above passes
        // whether or not the unset worked — because a build that ignored skipTypesetting
        // entirely would also draw the note.
        string source = Version
            + "\\fixed c' {\n"
            + "  \\set Timing.skipTypesetting = ##t\n"
            + "  R1\n"
            + "  b1\n"
            + "}\n";

        //Act
        BatchRunResult result = Run(source, "skip-typesetting-on");

        //Assert
        StaffLines(result).Should().Be(0);
    }
}
