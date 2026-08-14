/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2008--2026 Carl Sorensen <c_sorensen@byu.edu>

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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/fretboard-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - upstream's derived_mark() is not carried: it exists only to keep last_placements_
//     alive across a Guile garbage collection, and a managed field needs no such help.
//     Nothing else in that method has an effect.

/// <summary>
/// Generates a fret diagram from one or more events of type <c>NoteEvent</c>: makes
/// (guitar-like) tablature notes.
/// </summary>
public sealed class FretboardEngraver : Engraver
{
    private static readonly Symbol NoteEventSymbol = Symbol.Intern("note-event");
    private static readonly Symbol StringNumberEventSymbol = Symbol.Intern("string-number-event");
    private static readonly Symbol FingeringEventSymbol = Symbol.Intern("fingering-event");
    private static readonly Symbol NoteToFretFunctionSymbol = Symbol.Intern("noteToFretFunction");
    private static readonly Symbol ChordChangesSymbol = Symbol.Intern("chordChanges");
    private static readonly Symbol DotPlacementListSymbol = Symbol.Intern("dot-placement-list");
    private static readonly Symbol BeginOfLineVisibleSymbol
        = Symbol.Intern("begin-of-line-visible");

    private readonly List<StreamEvent> _noteEvents = new List<StreamEvent>();
    private readonly List<StreamEvent> _tabstringEvents = new List<StreamEvent>();
    private readonly List<StreamEvent> _fingeringEvents = new List<StreamEvent>();

    private Item _fretBoard;
    private object _lastPlacements = false;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public FretboardEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Fretboard_engraver";

    /// <summary>Starts listening for notes, string numbers and fingerings.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(NoteEventSymbol, ListenNote);
        ListenTo(StringNumberEventSymbol, ListenStringNumber);
        ListenTo(FingeringEventSymbol, ListenFingering);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Makes the fret board and hands it to the Scheme fret calculator.</summary>
    public override void ProcessMusic()
    {
        if (_noteEvents.Count == 0)
        {
            return;
        }

        object tabStrings = Articulations.ArticulationList(
            _noteEvents, _tabstringEvents, StringNumberEventSymbol);
        object fingers = Articulations.ArticulationList(
            _noteEvents, _fingeringEvents, FingeringEventSymbol);
        _fretBoard = MakeItem("FretBoard", _noteEvents[0]);
        object fretNotes = Pair.ListFrom(_noteEvents);
        object proc = GetProperty(NoteToFretFunctionSymbol);
        if (SchemeUtilities.IsProcedure(proc))
        {
            Interpreter interpreter = LilyPondScheme.Current;
            if (interpreter != null)
            {
                interpreter.Evaluator.Apply(
                    proc,
                    new object[]
                    {
                        Context,
                        fretNotes,
                        Pair.ListFrom(new[] { tabStrings, fingers }),
                        _fretBoard,
                    });
            }
        }

        object changes = GetProperty(ChordChangesSymbol);
        object placements = _fretBoard.GetProperty(DotPlacementListSymbol);
        if (SchemeUtilities.ToBool(changes)
            && SchemeUtilities.IsEqual(_lastPlacements, placements))
        {
            _fretBoard.SetProperty(BeginOfLineVisibleSymbol, true);
        }

        _lastPlacements = placements;
    }

    /// <summary>Forgets this timestep's fret board and events.</summary>
    public override void StopTranslationTimestep()
    {
        _fretBoard = null;
        _noteEvents.Clear();
        _tabstringEvents.Clear();
        _fingeringEvents.Clear();
    }

    private void ListenNote(StreamEvent ev) => _noteEvents.Add(ev);

    private void ListenStringNumber(StreamEvent ev) => _tabstringEvents.Add(ev);

    private void ListenFingering(StreamEvent ev) => _fingeringEvents.Add(ev);
}
