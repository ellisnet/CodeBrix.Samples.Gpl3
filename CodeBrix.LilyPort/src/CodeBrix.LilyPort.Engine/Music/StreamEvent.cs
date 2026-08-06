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

using System.Text;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Music; //was previously: lily/stream-event.cc, lily/include/stream-event.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/* TODO: Rename Stream_event -> Event */

/// <summary>
/// One thing happening at one moment: the unit the translation layer actually sees.
/// <para>
/// The music tree is walked by iterators, which turn it into a stream of these and
/// broadcast them through contexts. Engravers listen for the classes they care about.
/// An event carries its <c>class</c> as an immutable property, which is a LIST — an
/// event belongs to a whole hierarchy of classes at once, and
/// <see cref="IsInEventClass(Symbol)"/> tests membership.
/// </para>
/// </summary>
public class StreamEvent : Prob
{
    private static readonly Symbol StreamEventSymbol = Symbol.Intern("Stream_event");
    private static readonly Symbol ClassSymbol = Symbol.Intern("class");
    private static readonly Symbol OriginSymbol = Symbol.Intern("origin");
    private static readonly Symbol ElementSymbol = Symbol.Intern("element");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol PitchAlistSymbol = Symbol.Intern("pitch-alist");
    private static readonly Symbol MakeEventClassSymbol = Symbol.Intern("ly:make-event-class");

    /// <summary>Initializes an event with no class.</summary>
    public StreamEvent()
        : base(StreamEventSymbol, Nil.Instance)
    {
    }

    /// <summary>Initializes an event of a given class.</summary>
    /// <param name="eventClass">The class list this event belongs to.</param>
    /// <param name="immutableProperties">Further immutable properties, or null.</param>
    public StreamEvent(object eventClass, object immutableProperties)
        : base(
            StreamEventSymbol,
            new Pair(new Pair(ClassSymbol, eventClass), immutableProperties ?? Nil.Instance))
    {
    }

    /// <summary>Initializes a copy of another event.</summary>
    /// <param name="source">The event to copy.</param>
    protected StreamEvent(StreamEvent source)
        : base(source)
    {
    }

    /// <summary>Gets the C++ class name this object corresponds to.</summary>
    public override string ClassName => "Stream_event";

    /// <summary>Gets the event's class list.</summary>
    public object EventClass => GetProperty(ClassSymbol);

    /// <summary>Returns an independent copy of this event.</summary>
    /// <returns>The clone.</returns>
    public virtual StreamEvent Clone() => new StreamEvent(this);

    /// <summary>
    /// Expands an event-class leaf name into the whole ancestry, which is what
    /// <c>ly:make-event-class</c> does.
    /// <para>
    /// The expansion is load bearing everywhere a listener is registered for a
    /// SUPERCLASS: an engraver listening for <c>rhythmic-event</c> hears a note only
    /// because the note's class list carries its ancestors too. The engine's own
    /// internal events — <c>CreateContext</c>, <c>RemoveContext</c>, <c>Prepare</c> —
    /// go through the same table, where every one of them descends from
    /// <c>StreamEvent</c>.
    /// </para>
    /// <para>
    /// <c>ly:make-event-class</c> is defined in <c>scm/define-event-classes.scm</c>
    /// rather than in C++, so there is nothing to port and nothing to cache across
    /// interpreters. With no interpreter standing — a fixture exercising the dispatcher
    /// alone — the leaf name is its own one-element class list, which is what the
    /// dispatcher needs and all it needs.
    /// </para>
    /// </summary>
    /// <param name="className">The leaf class name.</param>
    /// <returns>The class list.</returns>
    public static object MakeEventClass(Symbol className)
    {
        if (className == null)
        {
            return Nil.Instance;
        }

        object procedure = Bootstrap.LilyPondScheme.LookupProcedure(MakeEventClassSymbol);
        if (procedure == null)
        {
            return Pair.List(className);
        }

        object expanded = SchemeUtilities.CallCallback(procedure, className);
        return expanded is Pair ? expanded : Pair.List(className);
    }

    /// <summary>Determines whether this event belongs to a given class.</summary>
    /// <param name="className">The class to test for.</param>
    /// <returns><see langword="true"/> when the class is in the event's class list.</returns>
    public bool IsInEventClass(Symbol className)
    {
        object cursor = GetProperty(ClassSymbol);
        while (cursor is Pair pair)
        {
            if (ReferenceEquals(pair.Car, className))
            {
                return true;
            }

            cursor = pair.Cdr;
        }

        return false;
    }

    /// <summary>Determines whether this event belongs to a given class.</summary>
    /// <param name="className">The class name.</param>
    /// <returns><see langword="true"/> when the class is in the event's class list.</returns>
    public bool IsInEventClass(string className) => IsInEventClass(Symbol.Intern(className));

    /// <summary>Records where in the source this event came from.</summary>
    /// <param name="origin">The source location.</param>
    public void SetSpot(object origin) => SetProperty(OriginSymbol, origin);

    /// <summary>Gets where in the source this event came from.</summary>
    public object Origin => GetProperty(OriginSymbol);

    /// <summary>
    /// Copies anything transposable out of the immutable alist into the mutable one,
    /// so that a later transposition has something it is allowed to modify.
    /// </summary>
    public void MakeTransposable()
    {
        /* This is in preparation for transposing stuff
           that may be defined in the immutable part */
        object cursor = ImmutablePropertyAlist;
        while (cursor is Pair listPair)
        {
            if (listPair.Car is Pair entry)
            {
                object property = entry.Car;
                object value = entry.Cdr;

                bool transposable =
                    value is Pitch
                    || (ReferenceEquals(property, ElementSymbol) && value is MusicObject)
                    || (ReferenceEquals(property, ElementsSymbol) && value is Pair)
                    || (ReferenceEquals(property, PitchAlistSymbol) && value is Pair);

                if (transposable && SchemeUtilities.Assq(property, MutablePropertyAlist) == null)
                {
                    MutablePropertyAlist = new Pair(
                        new Pair(property, MusicObject.MusicDeepCopy(value)),
                        MutablePropertyAlist);
                }
            }

            cursor = listPair.Cdr;
        }
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The event's class and properties.</returns>
    public override string ToString()
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("#<Stream_event ");
        builder.Append(EventClass);
        builder.Append(' ');
        builder.Append(MutablePropertyAlist);
        builder.Append('>');
        return builder.ToString();
    }

    /// <summary>
    /// Deep-copies an event value: events are cloned, pairs are rebuilt, and anything
    /// else is shared.
    /// </summary>
    /// <param name="value">The value to copy.</param>
    /// <returns>The copy.</returns>
    public static object EventDeepCopy(object value)
    {
        if (value is StreamEvent ev)
        {
            return ev.Clone();
        }

        if (value is Pair pair)
        {
            return new Pair(EventDeepCopy(pair.Car), EventDeepCopy(pair.Cdr));
        }

        return value;
    }

    /// <summary>
    /// Warns that an event slot already holding a different event was reassigned.
    /// Identical events are not reported, because nothing of value is lost.
    /// </summary>
    /// <param name="oldEvent">The event already in the slot.</param>
    /// <param name="newEvent">The event that tried to replace it.</param>
    public static void WarnReassignEvent(StreamEvent oldEvent, StreamEvent newEvent)
    {
        if (newEvent == null)
        {
            // not expected
            return;
        }

        if (AreEqual(oldEvent, newEvent))
        {
            // nothing of value was lost
            return;
        }

        Warn.Warning("conflict with event: `" + FirstClassName(oldEvent) + "'");
        Warn.Warning("discarding event: `" + FirstClassName(newEvent) + "'");
    }

    /// <summary>
    /// Assigns to an event slot at most once, warning about later attempts.
    /// </summary>
    /// <param name="slot">The slot to fill.</param>
    /// <param name="newEvent">The event to store.</param>
    /// <returns><see langword="true"/> when the slot was empty and has now been filled.</returns>
    public static bool AssignEventOnce(ref StreamEvent slot, StreamEvent newEvent)
    {
        if (slot == null)
        {
            slot = newEvent;
            return slot != null;
        }

        WarnReassignEvent(slot, newEvent);
        return false;
    }

    /// <summary>Copies the mutable property alist, cloning any events it holds.</summary>
    /// <returns>The copied alist.</returns>
    protected override object CopyMutableProperties() => EventDeepCopy(MutablePropertyAlist);

    private static string FirstClassName(StreamEvent ev)
    {
        if (ev?.EventClass is Pair pair && pair.Car is Symbol symbol)
        {
            return symbol.Name;
        }

        return "?";
    }
}
