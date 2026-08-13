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
using System.Text;
using System.Text.RegularExpressions;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// BOOK-PATH session (2026-08-12) end to end: what the book pipeline delivers besides
/// plain toplevel scores — markup-embedded scores and bookpart MIDI.
/// <para>
/// The first pair fences the toplevel <c>\markup \score</c> path, which produced ZERO
/// systems for every such book: the systems walk handed <c>interpret-markup-list</c> — a
/// vendored Scheme closure — to <c>SchemeUtilities.CallCallback</c>, whose callable test
/// accepted only C#-defined procedures and silently answered <c>'()</c> (the loose end
/// EPG15's close-out recorded), while <c>ly:make-book</c> flattened the collected
/// markup LIST one level too many, so the entry also failed <c>is-markup-list</c>.
/// </para>
/// <para>
/// The second pair fences bookpart MIDI: a <c>\bookpart</c>'s performances were left on
/// the child paper book the runner never reads (upstream's <c>Paper_book::output</c>
/// recurses; the port's caller collects), and a headerless bookpart missed the header
/// merge upstream's <c>Book::set_parent</c> performs, so its MIDI sequence carried no
/// name where the oracle titles it from the enclosing book — the
/// <c>sequence-name-scoping</c> rows.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class BookPathEndToEndTests
{
    private const string Version = "\\version \"2.27.2\"\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-bookpath-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static BatchRunResult Run(string source, string name)
        => BatchRunner.RunText(source, name, null, ScratchDirectory());

    /// <summary>
    /// Horizontal line spans on the page, the StaffLinesEndToEndTests observable:
    /// vertical bar lines and stems have x1 == x2 and must not be counted.
    /// </summary>
    private static int HorizontalLineCount(BatchRunResult result)
    {
        string text = File.ReadAllText(result.SvgPath);
        int count = 0;
        foreach (Match m in Regex.Matches(text, "<line [^>]*>"))
        {
            Dictionary<string, double> attrs = Regex
                .Matches(m.Value, "\\b(x1|x2|y1|y2)=\"([-0-9.]+)\"")
                .ToDictionary(
                    a => a.Groups[1].Value,
                    a => double.Parse(a.Groups[2].Value, CultureInfo.InvariantCulture));
            if (attrs.Count == 4
                && Math.Abs(attrs["y1"] - attrs["y2"]) < 1e-6
                && Math.Abs(attrs["x2"] - attrs["x1"]) > 1.0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The FIRST track-name meta event (FF 03 len text) in a standard MIDI file —
    /// where <c>ly:performance-write</c> stores the sequence name.
    /// </summary>
    private static string FirstTrackName(string midiPath)
    {
        byte[] bytes = File.ReadAllBytes(midiPath);
        for (int i = 0; i + 2 < bytes.Length; i++)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0x03)
            {
                int length = bytes[i + 2];
                if (i + 3 + length <= bytes.Length)
                {
                    return Encoding.UTF8.GetString(bytes, i + 3, length);
                }
            }
        }

        return null;
    }

    [Fact]
    public void a_toplevel_markup_score_draws_its_staff()
    {
        //Arrange
        // Five staff lines is the whole hand-computed expectation: the embedded score
        // is one default Staff. Before the fix this page did not exist at all.
        string source =
            Version
            + "\\markup \\score { \\new Staff { c'1 } \\layout { } }\n";

        //Act
        BatchRunResult result = Run(source, "bookpath-markup-score");

        //Assert
        result.SvgPath.Should().NotBeNull();
        HorizontalLineCount(result).Should().Be(5);
    }

    [Fact]
    public void omitting_the_staff_symbol_in_a_markup_score_draws_no_lines()
    {
        //Arrange
        // The control that must come out differently — without it, any page with five
        // long horizontal lines would satisfy the fact above.
        string source =
            Version
            + "\\markup \\score { \\new Staff \\with { \\omit StaffSymbol }"
            + " { c'1 } \\layout { } }\n";

        //Act
        BatchRunResult result = Run(source, "bookpath-markup-omitted");

        //Assert
        result.SvgPath.Should().NotBeNull();
        HorizontalLineCount(result).Should().Be(0);
    }

    [Fact]
    public void a_headerless_bookpart_titles_its_midi_from_the_enclosing_book()
    {
        //Arrange
        // Two performances: the headerless bookpart's score must inherit the BOOK's
        // title through the set_parent header merge, and the score with its own
        // midititle must keep it — the pair must come out differently, because a port
        // that dropped headers entirely would name both the same way.
        string source =
            Version
            + "\\book {\n"
            + "  \\header { title = \"Enclosing Book\" }\n"
            + "  \\bookpart {\n"
            + "    \\score { \\new Staff { c'1 } \\midi { } }\n"
            + "  }\n"
            + "  \\bookpart {\n"
            + "    \\score {\n"
            + "      \\new Staff { c'1 }\n"
            + "      \\header { midititle = \"Own Name\" }\n"
            + "      \\midi { }\n"
            + "    }\n"
            + "  }\n"
            + "}\n";

        //Act
        BatchRunResult result = Run(source, "bookpath-midi-title");

        //Assert
        result.MidiPaths.Count.Should().Be(2);
        List<string> names = result.MidiPaths
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(FirstTrackName)
            .ToList();
        names.Should().Contain("Enclosing Book");
        names.Should().Contain("Own Name");
    }
}
