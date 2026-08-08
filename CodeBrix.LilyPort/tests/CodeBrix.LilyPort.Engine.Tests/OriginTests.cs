// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// Source files and the locations that point into them — EPG1's foundation.
/// <para>
/// Worth testing closely rather than trusting: every diagnostic LilyPond emits is
/// positioned by this code, and an off-by-one here is invisible until someone reads an
/// error message and finds it points at the wrong character.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class OriginTests
{
    // In the collection since 2026-08-07: the Warn-recording tests here read the
    // process-global Warn.Messages, and running in parallel with the engine
    // collection's interpreter bootstraps made them flake — proven pre-existing by
    // a pristine-control run during Wave A. Serializing with the collection is the
    // real fix; the two recording tests below also filter for their own messages.

    private const string Sample = "one\ntwo\nthree\n";

    [Fact]
    public void line_numbers_count_from_one()
    {
        //Arrange
        SourceFile file = new SourceFile("sample.ly", Sample);

        //Act / Assert
        file.GetLine(0).Should().Be(1);
        file.GetLine(3).Should().Be(1);
        file.GetLine(4).Should().Be(2);
        file.GetLine(8).Should().Be(3);
    }

    [Fact]
    public void a_file_with_no_newline_is_all_one_line()
    {
        //Arrange
        SourceFile file = new SourceFile("flat.ly", "no newline here");

        //Act / Assert
        file.GetLine(0).Should().Be(1);
        file.GetLine(10).Should().Be(1);
    }

    [Fact]
    public void an_offset_outside_the_file_reports_line_zero()
    {
        //Arrange
        SourceFile file = new SourceFile("sample.ly", Sample);

        //Act / Assert
        // Upstream's contains() check answers 0 rather than guessing, and callers rely on
        // that to mean "no position", not "the first line".
        file.GetLine(-1).Should().Be(0);
        file.GetLine(Sample.Length + 5).Should().Be(0);
    }

    [Fact]
    public void the_line_slice_covers_the_line_without_its_newline()
    {
        //Arrange
        SourceFile file = new SourceFile("sample.ly", Sample);

        //Act
        string second = file.LineString(5);

        //Assert
        second.Should().Be("two");
    }

    [Fact]
    public void tabs_advance_the_column_to_the_next_multiple_of_eight()
    {
        //Arrange
        // Upstream: (column / 8 + 1) * 8. A tab in column 0 lands on 8; a tab after three
        // characters also lands on 8; a tab after eight lands on 16.
        SourceFile file = new SourceFile("tabs.ly", "\tX\nabc\tY\n12345678\tZ");

        //Act / Assert
        file.GetCounts(1, out int _, out int _, out int afterOneTab, out int _);
        afterOneTab.Should().Be(8);

        file.GetCounts(7, out int _, out int _, out int afterThreeThenTab, out int _);
        afterThreeThenTab.Should().Be(8);

        file.GetCounts(18, out int _, out int _, out int afterEightThenTab, out int _);
        afterEightThenTab.Should().Be(16);
    }

    [Fact]
    public void a_surrogate_pair_counts_as_one_character()
    {
        //Arrange
        // The divergence recorded on SourceFile: upstream skips UTF-8 continuation bytes
        // so its column counts characters. Counting .NET chars would count an astral
        // character twice, so the low surrogate is skipped to match.
        SourceFile file = new SourceFile("astral.ly", "\U0001D11EX");

        //Act
        file.GetCounts(2, out int _, out int lineChar, out int column, out int _);

        //Assert
        lineChar.Should().Be(1);
        column.Should().Be(1);
    }

    [Fact]
    public void the_location_string_is_file_line_and_one_based_column()
    {
        //Arrange
        SourceFile file = new SourceFile("sample.ly", Sample);
        Input origin = new Input(file, 5, 8);

        //Act / Assert
        // Column is reported one-based even though it is counted from zero -- editors
        // number columns from one, and this string is meant to be pasted into one.
        origin.LocationString().Should().Be("sample.ly:2:2");
    }

    [Fact]
    public void quoting_splits_the_line_at_the_position()
    {
        //Arrange
        SourceFile file = new SourceFile("sample.ly", Sample);

        //Act
        string quoted = file.QuoteInput(5);

        //Assert
        // "two", split after "t", with the remainder indented to line up under it.
        quoted.Should().Be("t\n wo");
    }

    [Fact]
    public void an_origin_with_no_file_reports_position_unknown()
    {
        //Arrange
        Input origin = new Input();

        //Act / Assert
        origin.LocationString().Should().Be(" (position unknown)");
        origin.LineNumberString().Should().Be("?");
        origin.FileString().Should().BeEmpty();
        origin.LineNumber().Should().Be(0);
    }

    [Fact]
    public void set_location_spans_from_one_origin_to_another()
    {
        //Arrange
        SourceFile file = new SourceFile("sample.ly", Sample);
        Input first = new Input(file, 0, 3);
        Input last = new Input(file, 8, 13);
        Input joined = new Input();

        //Act
        joined.SetLocation(first, last);

        //Assert
        joined.Start.Should().Be(0);
        joined.End.Should().Be(13);
        joined.LineNumber().Should().Be(1);
        joined.EndLineNumber().Should().Be(3);
    }

    [Fact]
    public void step_forward_advances_an_empty_span_to_cover_one_character()
    {
        //Arrange
        SourceFile file = new SourceFile("sample.ly", Sample);
        Input origin = new Input(file, 2, 2);

        //Act
        origin.StepForward();

        //Assert
        origin.Start.Should().Be(3);
        origin.End.Should().Be(3);
    }

    [Fact]
    public void a_warning_carries_the_location_and_the_quoted_line()
    {
        //Arrange
        SourceFile file = new SourceFile("sample.ly", Sample);
        Input origin = new Input(file, 5, 8);
        Warn.ClearMessages();
        Warn.RecordMessages = true;

        try
        {
            //Act
            origin.Warning("something is off");

            //Assert
            // Warn's recording is process-global and other test collections run in
            // parallel, so find THIS test's message rather than trusting index 0.
            string message = null;
            foreach (string candidate in Warn.Messages)
            {
                if (candidate.Contains("something is off"))
                {
                    message = candidate;
                }
            }

            message.Should().NotBeNull();
            message.Should().Contain("sample.ly:2:2");
        }
        finally
        {
            Warn.RecordMessages = false;
            Warn.ClearMessages();
        }
    }

    [Fact]
    public void a_deprecation_warning_is_reported_once_per_distinct_message()
    {
        //Arrange
        Warn.ClearMessages();
        Warn.RecordMessages = true;

        try
        {
            //Act
            bool first = Warn.DeprecationWarning("old thing");
            bool second = Warn.DeprecationWarning("old thing");
            bool other = Warn.DeprecationWarning("different thing");

            //Assert
            // The de-duplication is the point: a deprecated construct in a loop would
            // otherwise bury every other diagnostic.
            first.Should().BeTrue();
            second.Should().BeFalse();
            other.Should().BeTrue();

            // Warn's recording is process-global and other test collections run in
            // parallel, so count only THIS test's messages.
            int recorded = 0;
            foreach (string candidate in Warn.Messages)
            {
                if (candidate.Contains("old thing") || candidate.Contains("different thing"))
                {
                    recorded++;
                }
            }

            recorded.Should().Be(2);
        }
        finally
        {
            Warn.RecordMessages = false;
            Warn.ClearMessages();
        }
    }

    [Fact]
    public void offsets_round_trip_through_line_and_column()
    {
        //Arrange
        // The bridge the port needs and upstream does not: the lexer reports line and
        // column, an Input wants an offset.
        SourceFile file = new SourceFile("sample.ly", Sample);

        //Act / Assert
        file.OffsetOfLineColumn(1, 1).Should().Be(0);
        file.OffsetOfLineColumn(2, 1).Should().Be(4);
        file.OffsetOfLineColumn(2, 3).Should().Be(6);
        file.OffsetOfLineColumn(3, 1).Should().Be(8);
    }

    [Fact]
    public void set_line_renumbers_the_file_from_a_point()
    {
        //Arrange
        // What \sourcefilename needs: an included fragment reports the line numbers of
        // the file it came from, not of the buffer it now lives in.
        SourceFile file = new SourceFile("sample.ly", Sample);

        //Act
        file.SetLine(0, 100);

        //Assert
        file.GetLine(0).Should().Be(100);
        file.GetLine(4).Should().Be(101);
    }
}
