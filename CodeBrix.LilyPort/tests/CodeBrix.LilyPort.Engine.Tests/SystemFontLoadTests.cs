// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// <c>ly:system-font-load</c>: loading one of the port's own music fonts BY NAME.
/// <para>
/// ⚠ THE NAME IS MISLEADING AND IT COST THE PORT A MANUAL APPENDIX. "System font" here
/// means the fonts LilyPond's own font system SHIPS, not the host's font configuration:
/// upstream is <c>all_fonts_global-&gt;find_otf_font</c>, a FILE search over LilyPond's
/// data directory, and its documentation says only Emmentaler and Emmentaler-Brace
/// qualify because the caller needs the <c>LILC</c> and <c>LILY</c> SFNT tables. The
/// entry point had been filed as a D25 N/A on the reading that D23 forbade it; D23
/// prohibits falling back to fonts the MACHINE has, which this never did.
/// </para>
/// <para>
/// Found by wave LD3 of Phase 5: 26 snippets of the notation manual's "Modern glyph
/// charts" appendix could not engrave, because <c>en/included/font-table.ly</c> opens
/// with <c>(ly:system-font-load "emmentaler-20")</c>.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class SystemFontLoadTests
{
    /// <summary>A shipped music font loads by name and answers its glyph list.</summary>
    [Fact]
    public void a_shipped_music_font_loads_by_name_and_answers_its_glyph_list()
    {
        //Arrange
        string result = null;

        //Act
        Interpreter ambientBefore = LilyPondScheme.Current;
        try
        {
            Interpreter.RunWithLargeStack(() =>
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                result = Printer.Write(interpreter.EvalString(
                    "(length (ly:otf-glyph-list (ly:system-font-load \"emmentaler-20\")))",
                    "<test>"));
            });
        }
        finally
        {
            LilyPondScheme.RestoreAmbient(ambientBefore);
        }

        //Assert
        // 668 is the same number OpenTypeFontTests reads off the font directly — the
        // CFF charset size, .notdef included. Asserting the COUNT rather than merely
        // "it returned something" is what makes this a test of the font that was
        // loaded rather than of the primitive returning any font at all.
        result.Should().Be("668");
    }

    /// <summary>The font it loads is the one the engine engraves with.</summary>
    [Fact]
    public void the_font_it_loads_is_the_one_the_engine_engraves_with()
    {
        //Arrange
        string result = null;

        //Act
        Interpreter ambientBefore = LilyPondScheme.Current;
        try
        {
            Interpreter.RunWithLargeStack(() =>
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                result = Printer.Write(interpreter.EvalString(
                    "(let ((a (ly:system-font-load \"emmentaler-20\"))"
                    + "      (b (ly:system-font-load \"emmentaler-20\")))"
                    + "  (eq? a b))",
                    "<test>"));
            });
        }
        finally
        {
            LilyPondScheme.RestoreAmbient(ambientBefore);
        }

        //Assert
        // Upstream caches in otf_dict_ and hands back the same font object; the port
        // caches in AllFontMetrics, which is also where FontInterface gets the font it
        // engraves every music glyph from. Identity is what says the two paths meet:
        // a second, separately-loaded copy would answer the same glyph list and still
        // be the wrong thing.
        result.Should().Be("#t");
    }

    /// <summary>A font that is not shipped is an error rather than a false answer.</summary>
    [Fact]
    public void a_font_that_is_not_shipped_is_an_error_rather_than_a_false_answer()
    {
        //Arrange
        Interpreter ambientBefore = LilyPondScheme.Current;
        bool raised = false;

        //Act
        try
        {
            Interpreter.RunWithLargeStack(() =>
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                try
                {
                    interpreter.EvalString(
                        "(ly:system-font-load \"no-such-font-at-all\")", "<test>");
                }
                catch (LilyPondErrorException)
                {
                    raised = true;
                }
            });
        }
        finally
        {
            LilyPondScheme.RestoreAmbient(ambientBefore);
        }

        //Assert
        // THE CONTROL for the two gates above, and the reason this raises rather than
        // answering false: every caller's next move is ly:otf-glyph-list, so a false
        // would surface as a wrong-type argument naming a DIFFERENT procedure, three
        // steps from the name that was actually wrong. Upstream errors here too.
        raised.Should().BeTrue();
    }
}
