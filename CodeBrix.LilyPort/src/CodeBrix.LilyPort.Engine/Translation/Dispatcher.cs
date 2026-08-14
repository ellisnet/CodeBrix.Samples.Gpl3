/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2005--2026 Erik Sandberg  <mandolaerik@gmail.com>

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

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/dispatcher.cc, lily/include/dispatcher.hh, lily/listener.cc, lily/include/listener.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/*
  Listeners

  Listeners are used for stream event dispatching.

  A listener is essentially any procedure accepting a single argument
  (namely an event).
*/

/// <summary>
/// A callback bound to a target: what a dispatcher actually calls when an event
/// arrives.
/// <para>
/// Upstream's <c>Listener</c> is a curried pair of a two-argument callback and a
/// target, deliberately comparable by its ingredients so that a specific listener can
/// be removed again. The port keeps that: two listeners are equal when their target
/// and their handler are the same, which is what
/// <see cref="Dispatcher.RemoveListener"/> depends on.
/// </para>
/// </summary>
public sealed class Listener : IEquatable<Listener>, ISchemeEqual
{
    /// <summary>Initializes a listener.</summary>
    /// <param name="target">The object the handler belongs to.</param>
    /// <param name="handler">The handler to call.</param>
    public Listener(object target, Action<StreamEvent> handler)
    {
        Target = target;
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>Gets the object the handler belongs to.</summary>
    public object Target { get; }

    /// <summary>Gets the handler.</summary>
    public Action<StreamEvent> Handler { get; }

    /// <summary>Calls the handler with an event.</summary>
    /// <param name="streamEvent">The event.</param>
    public void Invoke(StreamEvent streamEvent) => Handler(streamEvent);

    /// <summary>Determines whether two listeners have the same target and handler.</summary>
    /// <param name="other">The listener to compare with.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public bool Equals(Listener other)
        => other != null
           && ReferenceEquals(Target, other.Target)
           && Equals(Handler, other.Handler);

    /// <summary>Determines whether this equals another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the object is an equal listener.</returns>
    public override bool Equals(object obj) => Equals(obj as Listener);

    /// <summary>
    /// Compares by VALUE for Scheme's <c>equal?</c>.
    /// <para>Upstream: <c>Listener::equal_p</c>, the smob equality handler
    /// <c>scm_equal_p</c> dispatches to. Without it two distinct objects holding the
    /// same value answer <c>#f</c>, which is identity, not equality.</para>
    /// </summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns><see langword="true"/> when the two are equal by value.</returns>
    public bool SchemeEquals(object other) => Equals(other);

    /// <summary>Returns a hash code.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
        => System.HashCode.Combine(
            Target == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Target),
            Handler);
}

/*
Event dispatching:
- Collect a list of listeners for each relevant class
- Send the event to each of these listeners, in increasing priority order.
- An event is never sent twice to listeners with equal priority.
  The only case where listeners with equal priority may exist is when
  two dispatchers are connected for more than one event type.  In that
  case, the respective listeners all have the same priority, making
  sure that any event is only dispatched at most once for that
  combination of dispatchers, even if it matches more than one event
  type.
*/

/// <summary>
/// Routes stream events to whoever asked to hear them.
/// <para>
/// Every context owns one of these, and dispatchers chain: a Voice's dispatcher
/// registers itself as a listener of its Staff's, so an event broadcast high up
/// reaches engravers far down. That chaining is exactly why the PRIORITY mechanism
/// exists — without it an event matching two classes on a linked pair of dispatchers
/// would be delivered twice.
/// </para>
/// <para>
/// Listeners are called in increasing priority order, and never twice at the same
/// priority. A dispatcher-to-dispatcher link reuses ONE priority for every class it
/// forwards, which is what collapses those duplicates.
/// </para>
/// </summary>
public sealed class Dispatcher
{
    private static readonly Symbol ClassSymbol = Symbol.Intern("class");

    /* Hash table. Each event-class maps to a list of listeners. */
    private readonly Dictionary<Symbol, List<PriorityEntry>> _listeners
        = new Dictionary<Symbol, List<PriorityEntry>>();

    /* alist of dispatchers that we listen to, each with the priority we hold there. */
    private readonly List<(Dispatcher Source, int Priority)> _dispatchers
        = new List<(Dispatcher, int)>();

    private readonly List<Symbol> _listenClasses = new List<Symbol>();

    /* priority counter. Listeners with low priority receive events first. */
    private int _priorityCount;

    private Listener _forwardingListener;

    /// <summary>Gets the event classes this dispatcher currently has listeners for.</summary>
    public IReadOnlyList<Symbol> ListenedTypes
    {
        get
        {
            List<Symbol> result = new List<Symbol>();
            foreach (KeyValuePair<Symbol, List<PriorityEntry>> entry in _listeners)
            {
                if (entry.Value.Count > 0)
                {
                    result.Add(entry.Key);
                }
            }

            return result;
        }
    }

    /// <summary>Sends an event to every listener that asked for one of its classes.</summary>
    /// <param name="streamEvent">The event to send.</param>
    public void Broadcast(StreamEvent streamEvent) => Dispatch(streamEvent);

    /// <summary>Determines whether any of an event's classes is listened to.</summary>
    /// <param name="classList">The event's class list.</param>
    /// <returns><see langword="true"/> when at least one class has a listener.</returns>
    public bool IsListenedClass(object classList)
    {
        object cursor = classList;
        while (cursor is Pair pair)
        {
            if (pair.Car is Symbol name
                && _listeners.TryGetValue(name, out List<PriorityEntry> list)
                && list.Count > 0)
            {
                return true;
            }

            cursor = pair.Cdr;
        }

        return false;
    }

    /// <summary>Registers a listener for an event class, at a fresh priority.</summary>
    /// <param name="listener">The listener.</param>
    /// <param name="eventClass">The class to listen for.</param>
    public void AddListener(Listener listener, Symbol eventClass)
        => InternalAddListener(listener, eventClass, ++_priorityCount);

    /// <summary>Registers a handler for an event class.</summary>
    /// <param name="target">The object the handler belongs to.</param>
    /// <param name="handler">The handler.</param>
    /// <param name="eventClass">The class to listen for.</param>
    /// <returns>The listener that was registered, so it can be removed later.</returns>
    public Listener AddListener(object target, Action<StreamEvent> handler, Symbol eventClass)
    {
        Listener listener = new Listener(target, handler);
        AddListener(listener, eventClass);
        return listener;
    }

    /// <summary>Removes a listener from an event class.</summary>
    /// <param name="listener">The listener to remove.</param>
    /// <param name="eventClass">The class it was registered for.</param>
    public void RemoveListener(Listener listener, Symbol eventClass)
    {
        if (!_listeners.TryGetValue(eventClass, out List<PriorityEntry> list))
        {
            Warn.ProgrammingError("remove_listener called with incorrect class.");
            return;
        }

        // We just remove the listener once.
        bool removed = false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Listener.Equals(listener))
            {
                list.RemoveAt(i);
                removed = true;
                break;
            }
        }

        if (!removed)
        {
            Warn.Warning("Attempting to remove nonexisting listener.");
            return;
        }

        if (list.Count == 0)
        {
            /* Unregister with all dispatchers. */
            foreach ((Dispatcher Source, int Priority) source in _dispatchers)
            {
                source.Source.RemoveListener(ForwardingListener(), eventClass);
            }

            _listenClasses.Remove(eventClass);
        }
    }

    /// <summary>
    /// Starts forwarding another dispatcher's events into this one.
    /// <para>
    /// One priority is taken from the source and reused for EVERY class forwarded, so
    /// an event matching several classes still arrives here only once.
    /// </para>
    /// </summary>
    /// <param name="source">The dispatcher to listen to.</param>
    public void RegisterAsListener(Dispatcher source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        // We are creating and remembering the priority _we_ have with the
        // foreign dispatcher.  All events are dispatched with the same
        // priority.
        int priority = ++source._priorityCount;

        // Don't register twice to the same dispatcher.
        foreach ((Dispatcher Source, int Priority) existing in _dispatchers)
        {
            if (ReferenceEquals(existing.Source, source))
            {
                Warn.Warning("Already listening to dispatcher, ignoring request");
                return;
            }
        }

        _dispatchers.Add((source, priority));

        Listener forwarder = ForwardingListener();
        foreach (Symbol eventClass in new List<Symbol>(_listenClasses))
        {
            source.InternalAddListener(forwarder, eventClass, priority);
        }
    }

    /// <summary>Stops forwarding another dispatcher's events into this one.</summary>
    /// <param name="source">The dispatcher to stop listening to.</param>
    public void UnregisterAsListener(Dispatcher source)
    {
        _dispatchers.RemoveAll(entry => ReferenceEquals(entry.Source, source));

        Listener forwarder = ForwardingListener();
        foreach (Symbol eventClass in new List<Symbol>(_listenClasses))
        {
            source.RemoveListener(forwarder, eventClass);
        }
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description naming the listened classes.</returns>
    public override string ToString()
        => "#<Dispatcher " + string.Join(" ", ListenedTypes) + ">";

    private Listener ForwardingListener()
        => _forwardingListener ??= new Listener(this, Dispatch);

    private void InternalAddListener(Listener listener, Symbol eventClass, int priority)
    {
        if (!_listeners.TryGetValue(eventClass, out List<PriorityEntry> list))
        {
            list = new List<PriorityEntry>();
            _listeners[eventClass] = list;
        }

        // if ev_class is not yet listened to, we go through our list of
        // source dispatchers and register ourselves there with the priority
        // we have reserved for this dispatcher.
        if (list.Count == 0)
        {
            /* Tell all dispatchers that we listen to, that we want to hear ev_class
               events */
            foreach ((Dispatcher Source, int Priority) source in _dispatchers)
            {
                source.Source.InternalAddListener(ForwardingListener(), eventClass, source.Priority);
            }

            _listenClasses.Add(eventClass);
        }

        // Kept sorted by priority, as upstream's scm_merge does.
        PriorityEntry entry = new PriorityEntry(priority, listener);
        int index = list.Count;
        while (index > 0 && list[index - 1].Priority > priority)
        {
            index--;
        }

        list.Insert(index, entry);
    }

    private void Dispatch(StreamEvent streamEvent)
    {
        if (streamEvent == null)
        {
            return;
        }

        object classList = streamEvent.GetProperty(ClassSymbol);
        if (!(classList is Pair))
        {
            Warn.Warning("Event class should be a list");
            return;
        }

        /*
          For each event class there is a list of listeners, ordered by
          priority. The next task is to call these listeners, in priority order.
        */
        List<Queue> queues = new List<Queue>();

        object cursor = classList;
        while (cursor is Pair pair)
        {
            if (pair.Car is Symbol name
                && _listeners.TryGetValue(name, out List<PriorityEntry> list)
                && list.Count > 0)
            {
                // Snapshot: a handler may add or remove listeners while running, and
                // upstream's cons lists are likewise unaffected by later mutation.
                queues.Add(new Queue(new List<PriorityEntry>(list)));
            }

            cursor = pair.Cdr;
        }

        // Never send an event to two listeners with equal priority.
        int lastPriority = -1;

        while (true)
        {
            Queue next = null;
            foreach (Queue queue in queues)
            {
                if (queue.HasMore && (next == null || queue.Priority < next.Priority))
                {
                    next = queue;
                }
            }

            if (next == null)
            {
                break;
            }

            PriorityEntry entry = next.Take();
            if (entry.Priority != lastPriority)
            {
                lastPriority = entry.Priority;
                entry.Listener.Invoke(streamEvent);
            }
        }
    }

    private readonly struct PriorityEntry
    {
        internal PriorityEntry(int priority, Listener listener)
        {
            Priority = priority;
            Listener = listener;
        }

        internal int Priority { get; }

        internal Listener Listener { get; }
    }

    private sealed class Queue
    {
        private readonly List<PriorityEntry> _entries;
        private int _index;

        internal Queue(List<PriorityEntry> entries) => _entries = entries;

        internal bool HasMore => _index < _entries.Count;

        internal int Priority => _entries[_index].Priority;

        internal PriorityEntry Take() => _entries[_index++];
    }

}
