// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CodeBrix.LilyPort.Engine.Bootstrap;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// PARITY 10 (2026-08-15) end to end: shape-note heads, whose whole mechanism was
/// missing from <c>Note_heads_engraver::process_music</c>.
/// <para>
/// Upstream reads the <c>shapeNoteStyles</c> vector and the key's <c>tonic</c>, takes
/// the note's scale degree as <c>(notename - tonic + 7) % 7</c>, and writes the vector's
/// symbol at that index into the head's <c>style</c>. The port's engraver stopped after
/// <c>staff-position</c>, so <c>\sacredHarpHeads</c>, <c>\aikenHeads</c> and every other
/// <c>shapeNoteStyles</c> setting was silently inert and every head came out the ordinary
/// black one.
/// </para>
/// <para>
/// The fence is a RELATIONSHIP between two renders of the same octave (rule 33), so it
/// names no glyph and no coordinate: shaped music must draw MORE distinct outlines than
/// plain music. The plain render is the CONTROL, and it is what makes the count mean
/// something — before the fix both renders answered identically.
/// </para>
/// <para>
/// The expected DIFFERENCE of three is hand-computed from
/// <c>ly/property-init.ly</c>'s <c>sacredHarpHeads = ##(fa sol la fa sol la mi)</c>: over
/// c d e f g a b those are fa sol la fa sol la mi, which is FOUR distinct head shapes
/// against the plain render's one. Read off the pinned oracle before it was asserted
/// (rule 35): six distinct path outlines against three.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class ShapeNoteHeadsEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    private const string Octave = "c'1 d' e' f' g' a' b'";

    private const string Shaped = Version
        + "\\score { \\new Staff { \\key c \\major \\sacredHarpHeads " + Octave + " } }\n";

    private const string Plain = Version
        + "\\score { \\new Staff { \\key c \\major " + Octave + " } }\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-shapenote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static int DistinctOutlines(string source, string name)
    {
        BatchRunResult result = BatchRunner.RunText(
            source, name, null, ScratchDirectory());
        result.SvgPath.Should().NotBeNull();

        string svg = File.ReadAllText(result.SvgPath);
        HashSet<string> outlines = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(svg, "<path[^>]*\\sd=\"([^\"]*)\""))
        {
            outlines.Add(match.Groups[1].Value);
        }

        return outlines.Count;
    }

    [Fact]
    public void shape_note_styles_draw_more_distinct_heads_than_plain_notes_do()
    {
        //Arrange / Act
        int shaped = DistinctOutlines(Shaped, "shapenote-shaped");
        int plain = DistinctOutlines(Plain, "shapenote-plain");

        //Assert
        // Four shapes against one: three MORE distinct outlines, on the same octave,
        // the same clef and the same key signature.
        (shaped - plain).Should().Be(3);
    }
}
