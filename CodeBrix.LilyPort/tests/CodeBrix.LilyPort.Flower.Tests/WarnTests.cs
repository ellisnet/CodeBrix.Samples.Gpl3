/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

// New-in-family tests. Upstream has no test-warn.cc or test-pqueue.cc.

using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Flower.Tests;

public class WarnTests : IDisposable
{
    private readonly LogLevel _savedLevel = Warn.Level;
    private readonly bool _savedAsError = Warn.WarningAsError;
    private readonly TextWriter _savedOutput = Warn.Output;

    public WarnTests()
    {
        //Arrange -- capture diagnostics in memory rather than on the console
        Warn.Output = TextWriter.Null;
        Warn.RecordMessages = true;
        Warn.ClearMessages();
        Warn.Level = LogLevel.LevelDebug;
        Warn.WarningAsError = false;
    }

    public void Dispose()
    {
        Warn.Level = _savedLevel;
        Warn.WarningAsError = _savedAsError;
        Warn.Output = _savedOutput;
        Warn.RecordMessages = false;
        Warn.ClearMessages();
    }

    [Fact]
    public void warning_carries_the_upstream_prefix()
    {
        //Act
        Warn.Warning("beam too steep");

        //Assert -- the prefix is user-facing LilyPond behaviour
        Warn.Messages[0].Should().Be("warning: beam too steep");
    }

    [Fact]
    public void a_location_is_prepended()
    {
        //Act
        Warn.Warning("bad note", "score.ly:12:3");

        //Assert
        Warn.Messages[0].Should().Be("score.ly:12:3: warning: bad note");
    }

    [Fact]
    public void programming_error_reports_but_does_not_throw()
    {
        //Act -- upstream continues, on the grounds that a partly-wrong score beats none
        Warn.ProgrammingError("grob has no parent");

        //Assert
        // RESTATED at PARITY 7: upstream's programming_error prints TWO lines
        // (warn.cc:234-238) and the port printed one. The second is the message's own
        // punctuation, so it is asserted here as a PAIR — a fence on the first line alone
        // would not notice the second going missing again.
        Warn.Messages.Count.Should().Be(2);
        Warn.Messages[0].Should().Be("programming error: grob has no parent");
        Warn.Messages[1].Should().Be("continuing, cross fingers");
    }

    [Fact]
    public void an_expected_warning_is_suppressed_and_consumed_once()
    {
        //Arrange -- ly:expect-warning's contract, read off flower/warn.cc:105-148:
        // a registered message is suppressed, the registration is CONSUMED, and a second
        // occurrence of the same message is therefore printed normally.
        Warn.ExpectWarning("unterminated tie");

        //Act
        Warn.Warning("unterminated tie");
        Warn.Warning("unterminated tie");

        //Assert
        Warn.Messages[0].Should().Be("suppressed warning: unterminated tie");
        // THE CONTROL: the second one is not suppressed, which is what "must be called
        // multiple times" in the upstream docstring means.
        Warn.Messages[1].Should().Be("warning: unterminated tie");
    }

    [Fact]
    public void an_expectation_matches_on_the_leading_text_only()
    {
        //Arrange
        // Upstream compares only the expectation's own length, deliberately, "needed for
        // the Input class, where the message contains the input file contents after the
        // real message" (warn.cc:133-137).
        Warn.ExpectWarning("cannot find file");

        //Act
        Warn.Warning("cannot find file: `nope.ly'\n\n  \\include \"nope.ly\"");
        // THE CONTROL: a message that merely CONTAINS the text, rather than starting
        // with it, is a different message and is not suppressed.
        Warn.Warning("I really cannot find file");

        //Assert
        Warn.Messages[0].Should().StartWith("suppressed warning: cannot find file:");
        Warn.Messages[1].Should().Be("warning: I really cannot find file");
    }

    [Fact]
    public void an_expectation_that_never_arrives_is_reported_and_the_list_is_cleared()
    {
        //Arrange -- this is warn-expected-warning-missing.ly's whole subject. Read off
        // the ORACLE first (rule 35): pinned LilyPond 2.27.2 prints
        //   warning: 1 expected warning(s) not encountered:
        //           this is a warning that won't be triggered
        // with the listed message indented by eight spaces on its own line.
        Warn.ExpectWarning("this is a warning that won't be triggered");

        //Act
        Warn.CheckExpectedWarnings();

        //Assert
        Warn.Messages[0].Should().Be(
            "warning: 1 expected warning(s) not encountered: "
            + "\n        this is a warning that won't be triggered");

        //Act -- THE CONTROL: the list is cleared, so a second check says nothing at all.
        int after = Warn.Messages.Count;
        Warn.CheckExpectedWarnings();

        //Assert
        Warn.Messages.Count.Should().Be(after);
    }

    [Fact]
    public void error_throws_rather_than_exiting_the_process()
    {
        //Act / Assert -- the one deliberate control-flow change in flower/:
        //upstream calls exit(), which a library must not do
        LilyPondErrorException failure = Assert.Throws<LilyPondErrorException>(
            () => Warn.Error("cannot continue", "score.ly:1:1"));
        failure.Message.Should().Contain("cannot continue");
        failure.Location.Should().Be("score.ly:1:1");
    }

    [Fact]
    public void the_three_error_severities_print_under_the_three_upstream_names()
    {
        //Arrange
        // ADDED at PARITY 6 (ruling R1, severity). flower/warn.cc has three distinct
        // prefixes and the port had two of them swapped: print_error, which the FATAL
        // error/1 goes through, prints "fatal error: " (warn.cc:197), while "error: "
        // belongs to non_fatal_error (warn.cc:249) — the one that does NOT stop the run.
        // Asserting all three together is the point: any future swap moves two of these
        // lines at once, where a single assertion on one of them would not notice.

        //Act
        Assert.Throws<LilyPondErrorException>(() => Warn.Error("stopped here"));
        Warn.NonFatalError("carried on");
        Warn.ProgrammingError("should not happen");

        //Assert
        Warn.Messages[0].Should().Be("fatal error: stopped here");
        Warn.Messages[1].Should().Be("error: carried on");
        Warn.Messages[2].Should().Be("programming error: should not happen");
        // RESTATED at PARITY 7 — the programming error's second line, so that this fence
        // still covers the whole of what the third severity prints.
        Warn.Messages[3].Should().Be("continuing, cross fingers");
    }

    [Fact]
    public void warning_as_error_promotes_a_warning()
    {
        //Arrange
        Warn.WarningAsError = true;

        //Act / Assert
        Assert.Throws<LilyPondErrorException>(() => Warn.Warning("promoted"));
    }

    [Fact]
    public void the_log_level_mask_gates_output()
    {
        //Arrange
        Warn.Level = LogLevel.LevelError;

        //Act / Assert
        Warn.IsEnabled(LogLevel.Error).Should().BeTrue();
        Warn.IsEnabled(LogLevel.Warn).Should().BeFalse();
        Warn.IsEnabled(LogLevel.Debug).Should().BeFalse();

        //Arrange
        Warn.Level = LogLevel.LevelDebug;

        //Act / Assert -- each level includes everything below it
        Warn.IsEnabled(LogLevel.Warn).Should().BeTrue();
        Warn.IsEnabled(LogLevel.Debug).Should().BeTrue();
    }
}

public class PriorityQueueTests
{
    [Fact]
    public void yields_elements_smallest_first()
    {
        //Arrange
        PriorityQueue<int> queue = new PriorityQueue<int>();
        foreach (int value in new[] { 5, 1, 4, 1, 9, 2 })
        {
            queue.Insert(value);
        }

        //Act
        List<int> order = new List<int>();
        while (queue.Count > 0)
        {
            order.Add(queue.DeleteMinimum());
        }

        //Assert
        order.Should().Equal(new List<int> { 1, 1, 2, 4, 5, 9 });
    }

    [Fact]
    public void front_peeks_without_removing()
    {
        //Arrange
        PriorityQueue<int> queue = new PriorityQueue<int>();
        queue.Insert(7);
        queue.Insert(3);

        //Act / Assert
        queue.Front().Should().Be(3);
        queue.Count.Should().Be(2);
    }

    [Fact]
    public void honours_a_custom_comparer()
    {
        //Arrange -- reverse ordering, so the largest comes out first
        PriorityQueue<int> queue = new PriorityQueue<int>(
            Comparer<int>.Create((a, b) => b.CompareTo(a)));
        queue.Insert(1);
        queue.Insert(9);
        queue.Insert(5);

        //Act / Assert
        queue.DeleteMinimum().Should().Be(9);
    }
}
