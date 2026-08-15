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
        Warn.Messages[0].Should().Be("programming error: grob has no parent");
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
