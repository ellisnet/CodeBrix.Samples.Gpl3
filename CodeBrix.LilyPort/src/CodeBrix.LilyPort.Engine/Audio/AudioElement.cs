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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Engine.Translation;

namespace CodeBrix.LilyPort.Engine.Audio; //was previously: lily/audio-element.cc, lily/include/audio-element.hh, lily/audio-element-info.cc, lily/include/audio-element-info.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - gc_mark() is NOT carried and has no analogue. Upstream's Performance keeps every
//     Audio_element alive and marks each one's causing Stream_event so Guile's collector
//     does not reclaim an event the performance still points at. In this port the
//     reference from Cause IS the reachability, so a mark method would be a no-op that
//     looked like it did something. Recorded in PORT-COVERAGE.
//   - VIRTUAL_CLASS_NAME/class_name() become the ClassName property the rest of the
//     engine already uses for the same purpose.

/// <summary>
/// Anything the MIDI path produces while music is being interpreted: a note, a key, a
/// tempo, a whole staff.
/// <para>
/// This is the performer-side twin of a <c>Grob</c>. A performer announces one of these
/// instead of typesetting something, and the <see cref="Layout.Performance"/> collects
/// them all; turning them into MIDI bytes happens afterwards, in
/// <see cref="MidiWalker"/>.
/// </para>
/// </summary>
public class AudioElement : IDiagnostics
{
    /// <summary>
    /// Gets the event this element was made for, or <see langword="null"/> when it has
    /// none.
    /// <para>
    /// Upstream keeps the setter private to <c>Performance</c>, which is the only thing
    /// allowed to establish the link; the port keeps that by making the setter internal
    /// to the assembly and having <see cref="Layout.Performance.AddElement"/> be the one
    /// caller.
    /// </para>
    /// </summary>
    public StreamEvent Cause { get; internal set; }

    /// <summary>Gets the C++ class name this element corresponds to.</summary>
    public virtual string ClassName => "Audio_element";

    /// <summary>Gets the element's name, as diagnostics refer to it.</summary>
    public virtual string Name => ClassName;

    /// <summary>Returns where this element came from, by way of its causing event.</summary>
    /// <returns>The origin, or <see langword="null"/> when there is no cause.</returns>
    public Input Origin() => Cause?.Origin as Input;

    /// <summary>Returns the external representation.</summary>
    /// <returns>The element's class name.</returns>
    public override string ToString() => "#<" + ClassName + ">";
}

/// <summary>
/// What a performer hands round when it announces an <see cref="AudioElement"/>: the
/// element, the event that caused it, and the performer that made it.
/// <para>Upstream calls this a "data container for broadcasts".</para>
/// </summary>
public sealed class AudioElementInfo
{
    /// <summary>Initializes an empty record.</summary>
    public AudioElementInfo()
    {
    }

    /// <summary>Initializes a record for an element and its cause.</summary>
    /// <param name="element">The announced element.</param>
    /// <param name="streamEvent">The event that caused it, or <see langword="null"/>.</param>
    public AudioElementInfo(AudioElement element, StreamEvent streamEvent)
    {
        Element = element;
        Event = streamEvent;
    }

    /// <summary>Gets or sets the announced element.</summary>
    public AudioElement Element { get; set; }

    /// <summary>Gets or sets the event that caused the element.</summary>
    public StreamEvent Event { get; set; }

    /// <summary>Gets or sets the performer that announced the element.</summary>
    public Translator OriginTranslator { get; set; }

    /// <summary>
    /// Returns the chain of contexts from the announcing performer's own context up to
    /// (but not including) the given translator's context.
    /// <para>
    /// <see cref="StaffPerformer"/> reads element zero of this to decide which Voice a
    /// note belongs to, which is how one MIDI track per voice is arranged.
    /// </para>
    /// </summary>
    /// <param name="end">The translator whose context ends the walk.</param>
    /// <returns>The contexts, innermost first.</returns>
    public List<Context> OriginContexts(Translator end)
    {
        List<Context> result = new List<Context>();

        Context context = OriginTranslator?.Context;
        Context stop = end?.Context;

        // Upstream's do/while runs the body once before testing, so the announcing
        // context is always element zero even when it IS the ending context.
        do
        {
            if (context == null)
            {
                break;
            }

            result.Add(context);
            context = context.Parent;
        }
        while (context != null && !ReferenceEquals(context, stop));

        return result;
    }
}
