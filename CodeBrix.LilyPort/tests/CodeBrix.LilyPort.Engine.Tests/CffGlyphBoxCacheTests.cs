// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The glyph-box memo in <see cref="CffFont"/>, which is shared state whether or not
/// anything meant it to be.
/// <para>
/// A face is parsed once and kept for the life of the process — <c>AllFontMetrics</c>
/// caches it behind a lock, and every <c>TextFace</c> holds one — so the memo behind
/// <c>GlyphBox</c> is reachable from as many threads as touch the font. The sweep
/// engraves one file at a time in one process and never does, which is why the memo
/// went unguarded for the life of the port; the test suite does, because xUnit runs
/// test classes in parallel, and a torn <c>Dictionary</c> insert is what surfaced it.
/// </para>
/// <para>
/// The claim here is a RELATIONSHIP rather than a table of boxes: a glyph's box is a
/// deterministic function of its index, so whatever the single-threaded reader gets is
/// what every concurrent reader must get, and no reader may fail. Recording the boxes
/// themselves would be reading an expectation off the port's own output (rule 33); the
/// charstring interpreter's correctness is <see cref="CffOutlineTests"/>'s claim, not
/// this file's.
/// </para>
/// <para>
/// The cache must start COLD in each round, because the race is between two inserts —
/// once every index is memoized the readers only read and the defect cannot show. That
/// is why each round parses its own font rather than sharing one across rounds.
/// </para>
/// </summary>
public class CffGlyphBoxCacheTests
{
    private const string FontName = "emmentaler-20";

    // Enough glyphs that the readers are still inserting when they collide, and enough
    // rounds that a race which loses most of the time is still seen. Emmentaler-20
    // carries 662 outlines, so this asks for essentially all of them.
    private const int GlyphCount = 600;

    private const int Rounds = 8;

    private static CffFont LoadFont()
    {
        SfntReader reader = new SfntReader(FontAssets.MusicFont(FontName));
        return new CffFont(reader.GetTable("CFF "));
    }

    /// <summary>
    /// Reads every index, starting at a reader-dependent point and wrapping, so two
    /// readers are working on different parts of the cache rather than marching in
    /// step.
    /// <para>
    /// The rotation is deliberately an OFFSET rather than a stride. A stride only
    /// visits every index when it is coprime with the count, and the first draft of
    /// this fence used one — so most readers silently covered a fraction of the font
    /// and left the rest of the array at <c>default</c>, which then failed against the
    /// single-threaded run and read exactly like the defect it was written to catch.
    /// </para>
    /// </summary>
    private static Box[] ReadAll(CffFont font, int count, int offset)
    {
        Box[] boxes = new Box[count];
        for (int step = 0; step < count; step++)
        {
            int index = (step + offset) % count;
            boxes[index] = font.GlyphBox(index);
        }

        return boxes;
    }

    [Fact]
    public void concurrent_readers_of_a_cold_glyph_box_cache_agree_with_a_single_reader()
    {
        //Arrange
        CffFont reference = LoadFont();
        int count = Math.Min(GlyphCount, reference.GlyphCount);
        Box[] expected = ReadAll(reference, count, 0);

        // The control: the assertion below is only worth making if the boxes actually
        // differ from one another. A cache that answered ONE box for every index would
        // satisfy "every reader agrees" perfectly, so count the distinct answers and
        // require the set to be wide. Measured on emmentaler-20, this is in the
        // hundreds; the bar is set well under that so a font revision cannot make the
        // fence fail for an uninteresting reason.
        HashSet<Box> distinct = new HashSet<Box>(expected);
        distinct.Count.Should().BeGreaterThan(100,
            "the agreement claim is vacuous unless the glyphs have different boxes");

        int readers = Math.Max(4, Environment.ProcessorCount);
        int reads = 0;

        //Act
        for (int round = 0; round < Rounds; round++)
        {
            // Cold cache per round — this is the state the race lives in.
            CffFont shared = LoadFont();
            Box[][] answers = new Box[readers][];

            Parallel.For(0, readers, reader =>
            {
                // Spread the starting points evenly around the font, so the readers
                // collide across the whole cache rather than at one end of it.
                answers[reader] = ReadAll(shared, count, reader * (count / readers));
            });

            //Assert
            for (int reader = 0; reader < readers; reader++)
            {
                for (int index = 0; index < count; index++)
                {
                    answers[reader][index].Should().Be(expected[index],
                        "reader " + reader.ToString() + " must see the single-threaded"
                        + " box for glyph " + index.ToString());
                    reads++;
                }
            }
        }

        // Rule 36: an assertion that never ran is not a passing assertion.
        reads.Should().Be(Rounds * readers * count);
    }
}
