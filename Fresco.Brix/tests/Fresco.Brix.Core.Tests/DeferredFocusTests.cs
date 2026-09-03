// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Shell;
using SilverAssertions;
using System;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The rule a brand-new editor view takes the keyboard by: a focus request
/// that the control could not honour yet is honoured when it can, and only
/// then.
/// </summary>
public class DeferredFocusTests
{
    [Fact]
    public void a_request_a_control_can_honour_leaves_nothing_waiting()
    {
        //Arrange
        DeferredFocus focus = new DeferredFocus();

        //Act
        bool taken = focus.Request(() => true);

        //Assert
        taken.Should().Be(true);
        focus.IsPending.Should().Be(false);
    }

    [Fact]
    public void a_request_a_control_refuses_is_remembered()
    {
        //Arrange
        DeferredFocus focus = new DeferredFocus();

        //Act
        bool taken = focus.Request(() => false);

        //Assert
        taken.Should().Be(false);
        focus.IsPending.Should().Be(true);
    }

    [Fact]
    public void a_waiting_request_is_made_again_when_the_control_loads()
    {
        //Arrange
        DeferredFocus focus = new DeferredFocus();
        focus.Request(() => false);
        int asked = 0;

        //Act
        bool honoured = focus.Honour(() => { asked++; return true; });

        //Assert
        asked.Should().Be(1);
        honoured.Should().Be(true);
        focus.IsPending.Should().Be(false);
    }

    [Fact]
    public void a_control_that_loads_with_nothing_waiting_does_not_take_focus()
    {
        //Arrange
        //The whole point of the guard: a view is loaded for reasons of its own,
        //and must not pull the keyboard away from wherever the user put it.
        DeferredFocus focus = new DeferredFocus();
        int asked = 0;

        //Act
        bool honoured = focus.Honour(() => { asked++; return true; });

        //Assert
        asked.Should().Be(0);
        honoured.Should().Be(false);
    }

    [Fact]
    public void a_request_that_has_been_honoured_is_not_honoured_twice()
    {
        //Arrange
        DeferredFocus focus = new DeferredFocus();
        focus.Request(() => false);
        focus.Honour(() => true);
        int asked = 0;

        //Act
        bool honoured = focus.Honour(() => { asked++; return true; });

        //Assert
        asked.Should().Be(0);
        honoured.Should().Be(false);
    }

    [Fact]
    public void a_request_that_succeeds_after_one_that_failed_clears_the_wait()
    {
        //Arrange
        DeferredFocus focus = new DeferredFocus();
        focus.Request(() => false);

        //Act
        bool taken = focus.Request(() => true);

        //Assert
        taken.Should().Be(true);
        focus.IsPending.Should().Be(false);
    }

    [Fact]
    public void a_control_that_is_still_not_ready_keeps_the_request_waiting()
    {
        //Arrange
        DeferredFocus focus = new DeferredFocus();
        focus.Request(() => false);

        //Act
        bool honoured = focus.Honour(() => false);

        //Assert
        honoured.Should().Be(false);
        focus.IsPending.Should().Be(true);
    }

    [Fact]
    public void asking_without_a_way_to_focus_is_refused()
    {
        //Arrange
        DeferredFocus focus = new DeferredFocus();

        //Act
        Action request = () => focus.Request(null);
        Action honour = () => focus.Honour(null);

        //Assert
        request.Should().Throw<ArgumentNullException>();
        honour.Should().Throw<ArgumentNullException>();
    }
}
