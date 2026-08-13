// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The order in which a context's two dispatchers are wired.
/// <para>
/// Upstream wires them in one order and says why:
/// <c>lily/context.cc:375-395</c> registers <c>create_context_from_event</c> and its four
/// siblings on the new context's <c>event_source_</c> FIRST and only then calls
/// <c>events_below_-&gt;register_as_listener (event_source_)</c>, under the comment
/// <c>"We want to be the first ones to hear our own events. Therefore, wait before
/// registering events_below_"</c>. <c>lily/global-context.cc:50-55</c> does the same.
/// Because a <see cref="Dispatcher"/> serves listeners in increasing priority and
/// priority is registration order, the two orders are observably different — and the
/// difference is load-bearing, because <c>CreateContext</c> is the event that CREATES a
/// context and <c>LyricCombineIterator.CheckNewContext</c> listens for it from OUTSIDE
/// in order to catch a Voice the moment it exists.
/// </para>
/// <para>
/// These assert the RELATIONSHIP — self before relay — rather than any recorded
/// ordinal, and each is paired with a control.
/// </para>
/// </summary>
public class ContextListenerOrderTests
{
    private static Symbol Sym(string name) => Symbol.Intern(name);

    [Fact]
    public void a_context_has_already_acted_on_its_own_event_when_an_outside_listener_hears_it()
    {
        //Arrange
        // The outside listener is the shape LyricCombineIterator.CheckNewContext has: it
        // sits on EventsBelow and, when it fires, LOOKS at the tree. What it must never
        // see is the state from BEFORE the context acted — that is the whole point of
        // upstream's "we want to be the first ones to hear our own events".
        // SetProperty stands in for CreateContext because it is one of the same five
        // listeners, registered in the same place, and its effect is observable without
        // an output definition to create a child context from.
        Context context = new Context(Sym("Score"));
        Symbol property = Sym("instrumentName");
        int relayed = 0;
        bool setWhenRelayed = false;
        context.EventsBelow.AddListener(
            new Listener(
                context,
                _ =>
                {
                    relayed++;

                    // Against the VALUE, not against null: an unset context property
                    // answers a sentinel rather than null, so "not null" would be true
                    // whether the context had acted or not.
                    setWhenRelayed = context.GetProperty(property) is MutableString written
                                     && written.ToString() == "Flute";
                }),
            Sym("SetProperty"));

        StreamEvent request = Context.MakeEvent(Sym("SetProperty"));
        request.SetProperty(Sym("symbol"), property);
        request.SetProperty(Sym("value"), new MutableString("Flute"));

        //Act
        context.EventSource.Broadcast(request);

        //Assert
        // The relay must actually have run. Without this the fact below would be
        // satisfied by a listener that never fired at all — 18a's shape, an assertion
        // that measures an absence.
        relayed.Should().Be(1);
        setWhenRelayed.Should().BeTrue();
    }

    [Fact]
    public void a_dispatcher_serves_listeners_in_registration_order_either_way_round()
    {
        //Arrange
        // The mechanism the test above depends on, and its control: a Dispatcher orders
        // by priority and priority IS registration order, so registering the relay first
        // rather than last really does invert who hears an event first. Without this,
        // "self before relay" could look like a property of dispatchers rather than the
        // consequence of an ordering the constructor has to get right.
        List<string> selfFirst = new List<string>();
        Dispatcher sourceA = new Dispatcher();
        Dispatcher belowA = new Dispatcher();
        sourceA.AddListener(new Listener(selfFirst, _ => selfFirst.Add("self")), Sym("SetProperty"));
        belowA.RegisterAsListener(sourceA);
        belowA.AddListener(new Listener(selfFirst, _ => selfFirst.Add("outside")), Sym("SetProperty"));

        List<string> relayFirst = new List<string>();
        Dispatcher sourceB = new Dispatcher();
        Dispatcher belowB = new Dispatcher();
        belowB.RegisterAsListener(sourceB);
        belowB.AddListener(new Listener(relayFirst, _ => relayFirst.Add("outside")), Sym("SetProperty"));
        sourceB.AddListener(new Listener(relayFirst, _ => relayFirst.Add("self")), Sym("SetProperty"));

        //Act
        sourceA.Broadcast(new StreamEvent(StreamEvent.MakeEventClass(Sym("SetProperty")), Nil.Instance));
        sourceB.Broadcast(new StreamEvent(StreamEvent.MakeEventClass(Sym("SetProperty")), Nil.Instance));

        //Assert
        selfFirst.Should().Equal("self", "outside");
        relayFirst.Should().Equal("outside", "self");
    }

    [Fact]
    public void the_relay_to_EventsBelow_still_happens()
    {
        //Arrange
        // The control for the test above: putting the context's own listeners first must
        // not cost the relay. An assertion that "self runs first" would also pass if the
        // relay were simply broken, which is the failure this pairs against.
        Context parent = new Context(Sym("Score"));
        List<string> heard = new List<string>();
        parent.EventsBelow.AddListener(
            new Listener(heard, _ => heard.Add("relayed")), Sym("SetProperty"));

        //Act
        parent.EventSource.Broadcast(new StreamEvent(StreamEvent.MakeEventClass(Sym("SetProperty")), Nil.Instance));

        //Assert
        heard.Should().Equal("relayed");
    }

    [Fact]
    public void an_event_from_a_child_reaches_an_ancestors_EventsBelow()
    {
        //Arrange
        // The other half of the wiring, and the reason the order matters at all: an
        // outside listener sits on an ANCESTOR's EventsBelow, so the child's own
        // EventSource must still travel all the way up.
        Context parent = new Context(Sym("Score"));
        Context child = new Context(Sym("Staff"));
        parent.AddContext(child);
        List<string> heard = new List<string>();
        parent.EventsBelow.AddListener(
            new Listener(heard, _ => heard.Add("from-below")), Sym("SetProperty"));

        //Act
        child.EventSource.Broadcast(new StreamEvent(StreamEvent.MakeEventClass(Sym("SetProperty")), Nil.Instance));

        //Assert
        heard.Should().Equal("from-below");
    }
}
