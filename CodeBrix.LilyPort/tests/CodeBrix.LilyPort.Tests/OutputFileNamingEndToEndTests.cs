// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// PARITY 12's fence for D37: what the output FILES are called.
/// <para>
/// Two rules, both read off upstream rather than off the port.
/// <c>scm/framework-svg.scm</c>'s <c>output-stencils</c> seeds its counter at
/// <c>(1- first-page-number)</c> and bumps it before each page, so a page's file suffix
/// is its PAGE NUMBER and a book that starts on page 3 has no <c>-1</c> or <c>-2</c> at
/// all. <c>scm/lily-library.scm</c>'s <c>get-outfile-name</c> gives a book the base name,
/// then <c>-&lt;output-suffix&gt;</c> when one is set, then <c>-&lt;n&gt;</c> for the n-th
/// book already printed under the SAME key — where the key is the base name and the
/// suffix TOGETHER.
/// </para>
/// <para>
/// The port numbered pages from one regardless, and carried a comment asserting that was
/// the oracle's rule (trap 26). Every file in the <c>page-turn-page-breaking</c> family
/// sets <c>auto-first-page-number</c>, which starts those books on page 2 to avoid a bad
/// turn — so every page of every one of them was named one too low, each family member's
/// LAST page read MISSING, and the pages before it were graded against the oracle's NEXT
/// page. It also numbered books by a running index, which named both halves of
/// <c>book-change-global-staffsize-abs-fonts</c> wrongly.
/// </para>
/// <para>
/// Each rule is stated as a PAIR that must come out differently, because a namer that
/// ignored the paper variable entirely would satisfy either half alone.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class OutputFileNamingEndToEndTests
{
    private const string Version = "\\version \"2.27.2\"\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-naming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>The written files' base names, in the order the runner wrote them.</summary>
    private static List<string> WrittenNames(string source, string name)
    {
        BatchRunResult result = BatchRunner.RunText(
            source, name, null, ScratchDirectory());

        result.SvgPaths.Should().NotBeNull();
        return result.SvgPaths.Select(Path.GetFileName).ToList();
    }

    /// <summary>A two-page book: the page break is forced, so the count is not a guess.</summary>
    private static string TwoPageBook(string paper)
        => Version
        + "\\book {\n"
        + "  \\paper { " + paper + " }\n"
        + "  \\score { { c'1 \\pageBreak c'1 } }\n"
        + "}\n";

    [Fact]
    public void pages_are_named_from_the_books_first_page_number()
    {
        //Arrange
        // first-page-number 3 over two pages: output-stencils names them 3 and 4. There
        // is deliberately no -1 and no -2 -- that absence IS the rule.
        string source = TwoPageBook("first-page-number = #3");

        //Act
        List<string> names = WrittenNames(source, "naming-from-three");

        //Assert
        names.Should().Equal(
            new List<string> { "naming-from-three-3.svg", "naming-from-three-4.svg" });
    }

    [Fact]
    public void pages_are_named_from_one_when_the_book_does_not_say_otherwise()
    {
        //Arrange
        // The control for the fact above, and the reason the port's defect stayed
        // invisible for so long: with the default first-page-number the two rules agree,
        // so every ordinary file in the corpus was named correctly either way.
        string source = TwoPageBook(string.Empty);

        //Act
        List<string> names = WrittenNames(source, "naming-default");

        //Assert
        names.Should().Equal(
            new List<string> { "naming-default-1.svg", "naming-default-2.svg" });
    }

    [Fact]
    public void output_suffix_names_the_book_and_the_counter_does_not_fire()
    {
        //Arrange
        // Two books printed under DIFFERENT keys, which is book-change's own shape: the
        // first carries a suffix, the second none. get-outfile-name's counter is keyed by
        // base name AND suffix, so neither book collides with the other and NEITHER gets a
        // number. A running book index would have named the second one "-1".
        string source =
            Version
            + "#(define output-suffix \"alpha\")\n"
            + "\\book { \\score { { c'1 } } }\n"
            + "#(define output-suffix #f)\n"
            + "\\book { \\score { { d'1 } } }\n";

        //Act
        List<string> names = WrittenNames(source, "naming-suffix");

        //Assert
        names.Should().Equal(
            new List<string> { "naming-suffix-alpha.svg", "naming-suffix.svg" });
    }

    [Fact]
    public void the_counter_does_fire_when_two_books_share_one_key()
    {
        //Arrange
        // THE CONTROL that makes the pair mean something: with no suffix anywhere both
        // books land on the same key, so the second one DOES take the counter's "-1".
        // Without this half, a namer that had simply dropped the counter would pass.
        string source =
            Version
            + "\\book { \\score { { c'1 } } }\n"
            + "\\book { \\score { { d'1 } } }\n";

        //Act
        List<string> names = WrittenNames(source, "naming-twobooks");

        //Assert
        names.Should().Equal(
            new List<string> { "naming-twobooks.svg", "naming-twobooks-1.svg" });
    }
}
