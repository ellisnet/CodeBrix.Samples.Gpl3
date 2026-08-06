// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The Type 2 charstring interpreter, checked against an oracle that ships in the same
/// repository.
/// <para>
/// Every music font is vendored twice: once as an OTF whose CFF table holds the glyph
/// PROGRAMS, and once as an SVG font whose <c>d</c> attributes hold the same glyphs as
/// finished OUTLINES. The two were produced from one source by the font build, so
/// running the charstrings and comparing the geometry to the SVG outlines tests the
/// interpreter against a ready-made answer key rather than against a second
/// implementation that could be wrong the same way.
/// </para>
/// <para>
/// Both sides are measured as the CONTROL HULL — every on-curve and off-curve point —
/// rather than the true ink extremum, so the comparison is like for like. Solving the
/// cubics would tighten both numbers by the same amount and prove nothing extra.
/// </para>
/// </summary>
public class CffOutlineTests
{
    private const string FontName = "emmentaler-20";

    private static CffFont LoadCff(string name)
    {
        byte[] bytes = FontAssets.MusicFont(name);
        bytes.Should().NotBeNull();

        SfntReader reader = new SfntReader(bytes);
        return new CffFont(reader.GetTable("CFF "));
    }

    [Fact]
    public void the_interpreter_reads_every_glyph_program_in_the_music_font()
    {
        //Arrange
        CffFont font = LoadCff(FontName);

        //Act
        int glyphCount = font.GlyphCount;

        //Assert
        glyphCount.Should().BeGreaterThan(600);
    }

    [Fact]
    public void charstring_bounds_agree_with_the_shipped_svg_outlines()
    {
        //Arrange
        byte[] bytes = FontAssets.MusicFont(FontName);
        SfntReader reader = new SfntReader(bytes);
        CffFont cff = new CffFont(reader.GetTable("CFF "));
        List<string> names = reader.ReadCffGlyphNames();
        SvgFontOutlines outlines = new SvgFontOutlines(FontAssets.OutlineFont(FontName));

        //Act
        int compared = 0;
        List<string> disagreements = new List<string>();

        for (int index = 0; index < names.Count; index++)
        {
            string outline = outlines.Outline(names[index]);
            if (string.IsNullOrEmpty(outline))
            {
                continue;
            }

            Box expected = SvgPathBounds(outline);
            Box actual = cff.GlyphBox(index);
            if (expected.X.IsEmpty || actual.X.IsEmpty)
            {
                continue;
            }

            compared++;

            // One design unit out of a thousand-unit em. The two files were rounded
            // independently by the font build, so exact equality is not on offer.
            const double Tolerance = 1.5;
            if (Math.Abs(expected.X.Left - actual.X.Left) > Tolerance
                || Math.Abs(expected.X.Right - actual.X.Right) > Tolerance
                || Math.Abs(expected.Y.Left - actual.Y.Left) > Tolerance
                || Math.Abs(expected.Y.Right - actual.Y.Right) > Tolerance)
            {
                disagreements.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: svg [{1:0.##},{2:0.##}]x[{3:0.##},{4:0.##}] cff [{5:0.##},{6:0.##}]x[{7:0.##},{8:0.##}]",
                    names[index],
                    expected.X.Left, expected.X.Right, expected.Y.Left, expected.Y.Right,
                    actual.X.Left, actual.X.Right, actual.Y.Left, actual.Y.Right));
            }
        }

        //Assert
        compared.Should().BeGreaterThan(500);
        disagreements.Should().BeEmpty();
    }

    [Fact]
    public void a_recorded_outline_closes_every_contour_it_opens()
    {
        //Arrange
        byte[] bytes = FontAssets.MusicFont(FontName);
        SfntReader reader = new SfntReader(bytes);
        CffFont cff = new CffFont(reader.GetTable("CFF "));
        int index = reader.ReadCffGlyphNames().IndexOf("noteheads.s2");
        index.Should().BeGreaterThan(0);

        //Act
        string path = cff.GlyphPath(index);

        //Assert
        path.Should().StartWith("M");
        path.Should().EndWith("z");
        CountOf(path, 'M').Should().Be(CountOf(path, 'z'));
    }

    private static int CountOf(string text, char character)
    {
        int count = 0;
        foreach (char c in text)
        {
            if (c == character)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The control hull of an SVG path, supporting the subset FontForge writes into an
    /// SVG font: move, line, horizontal and vertical line, cubic, smooth cubic, and
    /// close, in both absolute and relative forms.
    /// </summary>
    /// <param name="data">The path data.</param>
    /// <returns>The bounding box.</returns>
    private static Box SvgPathBounds(string data)
    {
        List<double> numbers = new List<double>();
        double x = 0;
        double y = 0;
        double startX = 0;
        double startY = 0;
        double lastControlX = 0;
        double lastControlY = 0;
        bool have = false;
        double left = 0, right = 0, bottom = 0, top = 0;
        char command = ' ';
        char previous = ' ';

        void Include(double px, double py)
        {
            if (!have)
            {
                have = true;
                left = right = px;
                bottom = top = py;
                return;
            }

            left = Math.Min(left, px);
            right = Math.Max(right, px);
            bottom = Math.Min(bottom, py);
            top = Math.Max(top, py);
        }

        int position = 0;
        while (position <= data.Length)
        {
            char c = position < data.Length ? data[position] : 'E';

            if (char.IsLetter(c) || position == data.Length)
            {
                Apply();
                previous = command;
                command = c;
                numbers.Clear();
                position++;
                continue;
            }

            if (c == ',' || char.IsWhiteSpace(c))
            {
                position++;
                continue;
            }

            int start = position;
            if (data[position] == '-' || data[position] == '+')
            {
                position++;
            }

            while (position < data.Length
                   && (char.IsDigit(data[position]) || data[position] == '.'
                       || data[position] == 'e' || data[position] == 'E'
                       || ((data[position] == '-' || data[position] == '+')
                           && (data[position - 1] == 'e' || data[position - 1] == 'E'))))
            {
                position++;
            }

            numbers.Add(double.Parse(
                data.Substring(start, position - start),
                NumberStyles.Float,
                CultureInfo.InvariantCulture));
        }

        void Apply()
        {
            bool relative = char.IsLower(command);
            char upper = char.ToUpperInvariant(command);

            switch (upper)
            {
                case 'M':
                    for (int i = 0; i + 1 < numbers.Count; i += 2)
                    {
                        x = relative ? x + numbers[i] : numbers[i];
                        y = relative ? y + numbers[i + 1] : numbers[i + 1];
                        if (i == 0)
                        {
                            startX = x;
                            startY = y;
                        }

                        Include(x, y);
                    }

                    break;

                case 'L':
                    for (int i = 0; i + 1 < numbers.Count; i += 2)
                    {
                        x = relative ? x + numbers[i] : numbers[i];
                        y = relative ? y + numbers[i + 1] : numbers[i + 1];
                        Include(x, y);
                    }

                    break;

                case 'H':
                    foreach (double value in numbers)
                    {
                        x = relative ? x + value : value;
                        Include(x, y);
                    }

                    break;

                case 'V':
                    foreach (double value in numbers)
                    {
                        y = relative ? y + value : value;
                        Include(x, y);
                    }

                    break;

                case 'C':
                    for (int i = 0; i + 5 < numbers.Count; i += 6)
                    {
                        double x1 = relative ? x + numbers[i] : numbers[i];
                        double y1 = relative ? y + numbers[i + 1] : numbers[i + 1];
                        double x2 = relative ? x + numbers[i + 2] : numbers[i + 2];
                        double y2 = relative ? y + numbers[i + 3] : numbers[i + 3];
                        x = relative ? x + numbers[i + 4] : numbers[i + 4];
                        y = relative ? y + numbers[i + 5] : numbers[i + 5];
                        Include(x1, y1);
                        Include(x2, y2);
                        Include(x, y);
                        lastControlX = x2;
                        lastControlY = y2;
                    }

                    break;

                case 'S':
                    for (int i = 0; i + 3 < numbers.Count; i += 4)
                    {
                        // A smooth cubic's first control point is the reflection of the
                        // previous curve's last one. Treating it as the current point
                        // instead is the classic way to get a hull that is too small.
                        bool smooth = char.ToUpperInvariant(previous) == 'C'
                                      || char.ToUpperInvariant(previous) == 'S';
                        double x1 = smooth ? (2 * x) - lastControlX : x;
                        double y1 = smooth ? (2 * y) - lastControlY : y;
                        double x2 = relative ? x + numbers[i] : numbers[i];
                        double y2 = relative ? y + numbers[i + 1] : numbers[i + 1];
                        x = relative ? x + numbers[i + 2] : numbers[i + 2];
                        y = relative ? y + numbers[i + 3] : numbers[i + 3];
                        Include(x1, y1);
                        Include(x2, y2);
                        Include(x, y);
                        lastControlX = x2;
                        lastControlY = y2;
                        previous = 'C';
                    }

                    break;

                case 'Z':
                    x = startX;
                    y = startY;
                    break;

                default:
                    break;
            }

            if (upper == 'C' || upper == 'S')
            {
                previous = 'C';
            }
        }

        return have
            ? new Box(new Interval(left, right), new Interval(bottom, top))
            : default;
    }
}
