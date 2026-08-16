// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// <c>\include</c> accepts a string-valued VARIABLE, not only a string literal.
/// </summary>
/// <remarks>
/// <para>
/// The Include-state lexer rule tested <c>found.Value is string</c> where upstream asks
/// <c>scm_is_string</c>. A name assigned in a <c>.ly</c> holds a
/// <c>MutableString</c>, so the guard answered false, the error branch ran WITHOUT
/// popping the lexer state, and the lexer stayed in Include mode and swallowed the rest
/// of the file — which is why the symptom was "syntax error at end of input" and not
/// "wrong or undefined identifier". Trap 12a's sixth site.
/// </para>
/// <para>
/// This was the last ERROR in the regression sweep, and the last MISSING row: for the
/// life of the project the file produced no output at all.
/// </para>
/// </remarks>
[Collection("engine-global-state")]
public class IncludeIdentifierEndToEndTests
{
    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-include-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void an_include_named_by_a_variable_reaches_the_file_it_names()
    {
        //Arrange
        // The regression file's own material (rule 35b): the included file is a bare
        // duration, so the three quarter notes only appear if the include ran AND the
        // lexer came back out of Include state to read the rest of the line.
        string directory = ScratchDirectory();
        File.WriteAllText(
            Path.Combine(directory, "include-identifier.ily"), "\\version \"2.25.17\" 4\n");

        string source =
            "\\version \"2.25.17\"\n"
            + "whatToInclude = \"include-identifier.ily\"\n"
            + "{ c'4 \\include \\whatToInclude c'4 }\n";

        // THE CONTROL: the same include written as a string LITERAL. That rule always
        // popped correctly, so it passed even while the variable form died — which is
        // exactly why the defect survived so long, and a fence without it would not
        // distinguish "includes work" from "includes work when named by a variable".
        string literal =
            "\\version \"2.25.17\"\n"
            + "{ c'4 \\include \"include-identifier.ily\" c'4 }\n";

        //Act
        BatchRunResult byVariable = BatchRunner.RunText(
            source, "include-by-variable", directory, ScratchDirectory());
        BatchRunResult byLiteral = BatchRunner.RunText(
            literal, "include-by-literal", directory, ScratchDirectory());

        //Assert
        byVariable.ErrorCount.Should().Be(0);
        byVariable.SvgPath.Should().NotBeNull();
        byLiteral.SvgPath.Should().NotBeNull();

        // Three quarter notes either way: the included "4" is the duration of the middle
        // note, so the two spellings must engrave the SAME PAGE. Counting drawn glyph
        // paths rather than asserting a number keeps this independent of which font
        // build drew them, and the equality is the relationship that matters — a build
        // that engraved nothing would fail the second assertion.
        int byVariablePaths = CountGlyphPaths(File.ReadAllText(byVariable.SvgPath));
        int byLiteralPaths = CountGlyphPaths(File.ReadAllText(byLiteral.SvgPath));
        byVariablePaths.Should().Be(byLiteralPaths);
        byVariablePaths.Should().BeGreaterThan(0);
    }

    [Fact]
    public void an_include_naming_an_undefined_variable_stops_the_run_rather_than_engraving()
    {
        //Arrange
        // The OTHER branch of the same rule, and it is upstream's shape rather than a
        // convenience: lexer.ll reports through LexerError and deliberately does NOT pop
        // the state, so the run ends there. What must never happen is the run quietly
        // producing a page — which is what a lexer that popped anyway would do.
        string source =
            "\\version \"2.25.17\"\n"
            + "{ c'4 \\include \\noSuchVariable c'4 }\n";

        //Act
        System.Func<BatchRunResult> run = () => BatchRunner.RunText(
            source, "include-undefined", ScratchDirectory(), ScratchDirectory());

        //Assert
        run.Should().Throw<System.Exception>();
    }

    // Counts drawn glyph outlines. Which glyphs they are is not the point here; that
    // the two spellings of the same include draw the same number of them is.
    private static int CountGlyphPaths(string svg)
    {
        int count = 0;
        int index = 0;
        while ((index = svg.IndexOf("<path", index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += "<path".Length;
        }

        return count;
    }
}
