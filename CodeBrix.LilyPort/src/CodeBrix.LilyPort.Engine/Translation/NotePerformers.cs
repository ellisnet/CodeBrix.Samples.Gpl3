/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1996--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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
using CodeBrix.LilyPort.Engine.Audio;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/note-performer.cc, lily/drum-note-performer.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - The two performers share the articulation walk verbatim upstream (it is copied and
//     pasted between the files). The port factors it into PerformerArticulations rather
//     than duplicating it, which is the one place the MIDI layer does not reproduce a duplication:
//     the two copies are identical line for line, so a single implementation cannot drift
//     from either. Recorded in PORT-COVERAGE.

/// <summary>
/// The articulation walk both note performers run: it finds the tie event, lets any
/// <c>midi-length</c> callback shorten the note, and totals the extra velocity.
/// </summary>
internal static class PerformerArticulations
{
    private static readonly Symbol ArticulationsSymbol = Symbol.Intern("articulations");
    private static readonly Symbol MidiLengthSymbol = Symbol.Intern("midi-length");
    private static readonly Symbol MidiExtraVelocitySymbol
        = Symbol.Intern("midi-extra-velocity");

    /// <summary>Walks a note event's articulations.</summary>
    /// <param name="performer">The performer running the walk.</param>
    /// <param name="noteEvent">The note event.</param>
    /// <param name="scriptEvents">The script events seen this timestep.</param>
    /// <param name="length">The note's length, which a callback may shorten.</param>
    /// <param name="tieEvent">The tie event found, if any.</param>
    /// <param name="velocity">The total extra velocity.</param>
    internal static void Walk(
        Translator performer,
        StreamEvent noteEvent,
        List<StreamEvent> scriptEvents,
        ref Moment length,
        out StreamEvent tieEvent,
        out int velocity)
    {
        object articulations = noteEvent.GetProperty(ArticulationsSymbol);
        tieEvent = null;
        velocity = 0;

        // Upstream prepends the script events walking the vector BACKWARDS, which leaves
        // them in front of the note's own articulations in their original order.
        for (int j = scriptEvents.Count; j-- > 0;)
        {
            articulations = new Pair(scriptEvents[j], articulations);
        }

        foreach (object entry in Pair.ToList(articulations))
        {
            if (!(entry is StreamEvent ev))
            {
                continue;
            }

            if (ev.IsInEventClass("tie-event"))
            {
                tieEvent = ev;
            }

            object callback = ev.GetProperty(MidiLengthSymbol);
            if (SchemeUtilities.IsProcedure(callback))
            {
                object shortened = SchemeUtilities.CallCallback(
                    callback, length, performer.Context);
                if (shortened is Moment moment)
                {
                    length = moment;
                }
            }

            if (ev.GetProperty(MidiExtraVelocitySymbol) is long extra)
            {
                velocity += (int)extra;
            }
        }
    }
}

/// <summary>Turns note events into <see cref="AudioNote"/>s.</summary>
public sealed class NotePerformer : Performer
{
    private static readonly Symbol InstrumentTranspositionSymbol
        = Symbol.Intern("instrumentTransposition");
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol MidiLengthSymbol = Symbol.Intern("midi-length");
    private static readonly Symbol NoteEventSymbol = Symbol.Intern("note-event");
    private static readonly Symbol BreathingEventSymbol = Symbol.Intern("breathing-event");
    private static readonly Symbol TieEventSymbol = Symbol.Intern("tie-event");
    private static readonly Symbol ArticulationEventSymbol
        = Symbol.Intern("articulation-event");

    private readonly List<StreamEvent> _noteEvents = new List<StreamEvent>();
    private readonly List<StreamEvent> _scriptEvents = new List<StreamEvent>();
    private readonly List<AudioNote> _notes = new List<AudioNote>();

    private List<AudioNote> _lastNotes = new List<AudioNote>();
    private Moment _lastStart;

    /// <summary>Initializes the performer in a context.</summary>
    /// <param name="context">The context this performer belongs to.</param>
    public NotePerformer(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Note_performer";

    /// <summary>Starts listening for notes, ties, articulations and breaths.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(NoteEventSymbol, ListenNote);
        ListenTo(BreathingEventSymbol, ListenBreathing);
        ListenTo(TieEventSymbol, ListenTie);
        ListenTo(ArticulationEventSymbol, ListenArticulation);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Makes an audio note for every note event heard this timestep.</summary>
    public override void ProcessMusic()
    {
        if (_noteEvents.Count == 0)
        {
            return;
        }

        Pitch transposing = new Pitch();
        if (GetProperty(InstrumentTranspositionSymbol) is Pitch transposition)
        {
            transposing = transposition;
        }

        foreach (StreamEvent noteEvent in _noteEvents)
        {
            if (!(noteEvent.GetProperty(PitchSymbol) is Pitch pitch))
            {
                continue;
            }

            Moment length = GetEventLength(noteEvent, NowMoment);

            PerformerArticulations.Walk(
                this, noteEvent, _scriptEvents, ref length,
                out StreamEvent tieEvent, out int velocity);

            _notes.Add(Announce(
                noteEvent,
                new AudioNote(pitch, length, tieEvent != null, transposing, velocity)));

            /*
              Grace notes shorten the previous non-grace note. If it was
              part of a tie, shorten the first note in the tie.
             */
            if (NowMoment.GracePart.IsNonZero && !_lastStart.GracePart.IsNonZero)
            {
                foreach (AudioNote last in _lastNotes)
                {
                    AudioNote tieHead = last.TieHead();
                    Moment start = tieHead.AudioColumn.When();

                    // Shorten the note if it would overlap. It might not if there's a
                    // rest in between.
                    if (start + tieHead.LengthMoment > NowMoment)
                    {
                        tieHead.LengthMoment = NowMoment - start;
                    }
                }
            }
        }
    }

    /// <summary>Remembers this timestep's notes and forgets its events.</summary>
    public override void StopTranslationTimestep()
    {
        if (_noteEvents.Count != 0)
        {
            _lastNotes = new List<AudioNote>(_notes);
            _lastStart = NowMoment;
        }

        _notes.Clear();
        _noteEvents.Clear();
        _scriptEvents.Clear();
    }

    private void ListenNote(StreamEvent ev) => _noteEvents.Add(ev);

    private void ListenTie(StreamEvent ev) => _scriptEvents.Add(ev);

    private void ListenArticulation(StreamEvent ev) => _scriptEvents.Add(ev);

    /// <summary>Shortens the previous notes to make room for a breath.</summary>
    private void ListenBreathing(StreamEvent ev)
    {
        object callback = ev.GetProperty(MidiLengthSymbol);
        if (!SchemeUtilities.IsProcedure(callback))
        {
            return;
        }

        foreach (AudioNote last in _lastNotes)
        {
            // Pass midi-length the available time since the last note started, including
            // any intervening rests. It returns how much is left for the note.
            Moment start = last.AudioColumn.When();
            Moment available = NowMoment - start;

            object answer = SchemeUtilities.CallCallback(callback, available, Context);
            Moment length = answer is Moment moment ? moment : available;

            // Take time from the first note of the tie, since it has all the length.
            AudioNote tieHead = last.TieHead();
            length += start - tieHead.AudioColumn.When();
            if (length < tieHead.LengthMoment)
            {
                tieHead.LengthMoment = length;
            }
        }
    }
}

/// <summary>Turns drum note events into <see cref="AudioNote"/>s, via <c>drumPitchTable</c>.</summary>
public sealed class DrumNotePerformer : Performer
{
    private static readonly Symbol DrumPitchTableSymbol = Symbol.Intern("drumPitchTable");
    private static readonly Symbol DrumTypeSymbol = Symbol.Intern("drum-type");
    private static readonly Symbol NoteEventSymbol = Symbol.Intern("note-event");
    private static readonly Symbol TieEventSymbol = Symbol.Intern("tie-event");
    private static readonly Symbol ArticulationEventSymbol
        = Symbol.Intern("articulation-event");

    private readonly List<StreamEvent> _noteEvents = new List<StreamEvent>();
    private readonly List<StreamEvent> _scriptEvents = new List<StreamEvent>();

    /// <summary>Initializes the performer in a context.</summary>
    /// <param name="context">The context this performer belongs to.</param>
    public DrumNotePerformer(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Drum_note_performer";

    /// <summary>Starts listening for notes, ties and articulations.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(NoteEventSymbol, ListenNote);
        ListenTo(TieEventSymbol, ListenTie);
        ListenTo(ArticulationEventSymbol, ListenArticulation);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Makes an audio note for every drum note whose type names a pitch.</summary>
    public override void ProcessMusic()
    {
        object table = GetProperty(DrumPitchTableSymbol);

        // Upstream consumes the vector from the BACK, so drum notes are announced in
        // reverse order within a timestep. Kept: the order audio items are announced in
        // is the order Midi_walker's stable sort preserves, and therefore the order the
        // bytes come out in.
        while (_noteEvents.Count != 0)
        {
            StreamEvent noteEvent = _noteEvents[_noteEvents.Count - 1];
            _noteEvents.RemoveAt(_noteEvents.Count - 1);

            object symbol = noteEvent.GetProperty(DrumTypeSymbol);
            object definition = Nil.Instance;

            if (symbol is Symbol && table is SchemeHashTable hashTable)
            {
                // scm_hashq_ref with '() as the default: the handle is the key/value
                // pair, and its absence means the drum type names no pitch.
                Pair handle = hashTable.GetHandle(symbol);
                definition = handle != null ? handle.Cdr : Nil.Instance;
            }

            if (!(definition is Pitch pitch))
            {
                continue;
            }

            Moment length = GetEventLength(noteEvent, NowMoment);

            PerformerArticulations.Walk(
                this, noteEvent, _scriptEvents, ref length,
                out StreamEvent tieEvent, out int velocity);

            Announce(
                noteEvent,
                new AudioNote(pitch, length, tieEvent != null, new Pitch(0, 0, Rational.Zero), velocity));
        }

        _noteEvents.Clear();
    }

    /// <summary>Forgets this timestep's events.</summary>
    public override void StopTranslationTimestep()
    {
        _noteEvents.Clear();
        _scriptEvents.Clear();
    }

    private void ListenNote(StreamEvent ev) => _noteEvents.Add(ev);

    private void ListenTie(StreamEvent ev) => _scriptEvents.Add(ev);

    private void ListenArticulation(StreamEvent ev) => _scriptEvents.Add(ev);
}
