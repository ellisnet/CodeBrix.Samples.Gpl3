/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2001--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/tab-note-heads-engraver.cc, lily/tab-staff-symbol-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - the two engravers share a file because tab-staff-symbol-engraver.cc is a single
//     six-line acknowledger and belongs beside the engraver whose staff it sizes.
//   - upstream walks the returned string/fret/finger list with scm_list_ref inside the
//     loop, which is quadratic; this walks the list ONCE with a cursor. The visiting
//     ORDER and the values are identical — only the cost differs — and the index `i` is
//     still tracked, because `index` is computed from it.

/// <summary>
/// Generates one or more tablature note heads from an event of type <c>NoteEvent</c>:
/// makes (guitar-like) tablature notes.
/// </summary>
public sealed class TabNoteHeadsEngraver : Engraver
{
    private static readonly Symbol NoteEventSymbol = Symbol.Intern("note-event");
    private static readonly Symbol StringNumberEventSymbol = Symbol.Intern("string-number-event");
    private static readonly Symbol FingeringEventSymbol = Symbol.Intern("fingering-event");
    private static readonly Symbol NoteToFretFunctionSymbol = Symbol.Intern("noteToFretFunction");
    private static readonly Symbol TablatureFormatSymbol = Symbol.Intern("tablatureFormat");
    private static readonly Symbol TabStaffLineLayoutFunctionSymbol
        = Symbol.Intern("tabStaffLineLayoutFunction");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");

    private readonly List<StreamEvent> _noteEvents = new List<StreamEvent>();
    private readonly List<StreamEvent> _tabstringEvents = new List<StreamEvent>();
    private readonly List<StreamEvent> _fingeringEvents = new List<StreamEvent>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public TabNoteHeadsEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Tab_note_heads_engraver";

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

    /// <summary>Makes one tablature note head per string the fret calculator answered.</summary>
    public override void ProcessMusic()
    {
        object tabStrings = Articulations.ArticulationList(
            _noteEvents, _tabstringEvents, StringNumberEventSymbol);
        object definedFingers = Articulations.ArticulationList(
            _noteEvents, _fingeringEvents, FingeringEventSymbol);
        object tabNotes = Pair.ListFrom(_noteEvents);
        object proc = GetProperty(NoteToFretFunctionSymbol);
        object stringFretFinger = Nil.Instance;
        Interpreter interpreter = LilyPondScheme.Current;
        if (SchemeUtilities.IsProcedure(proc) && interpreter != null)
        {
            stringFretFinger = interpreter.Evaluator.Apply(
                proc,
                new object[]
                {
                    Context,
                    tabNotes,
                    Pair.ListFrom(new[] { tabStrings, definedFingers }),
                });
        }

        object fretProcedure = GetProperty(TablatureFormatSymbol);
        object staffLineProcedure = GetProperty(TabStaffLineLayoutFunctionSymbol);
        int fretCount = ListLength(stringFretFinger);
        bool lengthChanged = _noteEvents.Count != fretCount;

        if (!(stringFretFinger is Nil) && interpreter != null)
        {
            object cursor = stringFretFinger;
            for (int i = 0; i < fretCount; i++)
            {
                if (!(cursor is Pair cell))
                {
                    break;
                }

                object noteEntry = cell.Car;
                cursor = cell.Cdr;

                if (!(noteEntry is Pair entry))
                {
                    continue;
                }

                object stringNumber = entry.Car;
                if (SchemeUtilities.IsSchemeTrue(stringNumber))
                {
                    object fret = ((Pair)entry.Cdr).Car;
                    object fretLabel = interpreter.Evaluator.Apply(
                        fretProcedure, new object[] { Context, stringNumber, fret });
                    int index = lengthChanged ? 0 : i;
                    Item note = MakeItem("TabNoteHead", _noteEvents[index]);
                    note.SetProperty(TextSymbol, fretLabel);
                    object staffPosition = interpreter.Evaluator.Apply(
                        staffLineProcedure, new object[] { Context, stringNumber });
                    note.SetProperty(StaffPositionSymbol, staffPosition);
                }
            }
        }
    }

    /// <summary>Forgets this timestep's events.</summary>
    public override void StopTranslationTimestep()
    {
        _noteEvents.Clear();
        _tabstringEvents.Clear();
        _fingeringEvents.Clear();
    }

    // scm_ilength: the length of a proper list, or -1 when the value is not one. The
    // caller casts it to an unsigned size, so a -1 would become an enormous loop bound;
    // the guard below on `stringFretFinger is Nil` plus the cursor walk is what keeps
    // that impossible here.
    private static int ListLength(object list)
    {
        int length = 0;
        object cursor = list;
        while (cursor is Pair pair)
        {
            length++;
            cursor = pair.Cdr;
        }

        return cursor is Nil ? length : -1;
    }

    private void ListenNote(StreamEvent ev) => _noteEvents.Add(ev);

    private void ListenStringNumber(StreamEvent ev) => _tabstringEvents.Add(ev);

    private void ListenFingering(StreamEvent ev) => _fingeringEvents.Add(ev);
}

/// <summary>
/// Creates a tablature staff symbol, but looks at <c>stringTunings</c> for the number
/// of lines.
/// </summary>
public sealed class TabStaffSymbolEngraver : Engraver
{
    private static readonly Symbol StringTuningsSymbol = Symbol.Intern("stringTunings");
    private static readonly Symbol LineCountSymbol = Symbol.Intern("line-count");
    private static readonly Symbol StaffSymbolInterface = Symbol.Intern("staff-symbol-interface");

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public TabStaffSymbolEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Tab_staff_symbol_engraver";

    /// <summary>Sizes an acknowledged staff symbol from the string tunings.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        // Grob_info_t<Spanner>: upstream's acknowledger takes a Spanner.
        if (!(info.Grob is Spanner) || !info.Grob.HasInterface(StaffSymbolInterface))
        {
            return;
        }

        long k = ListLength(GetProperty(StringTuningsSymbol));
        if (k >= 0)
        {
            info.Grob.SetProperty(LineCountSymbol, k);
        }
    }

    // scm_ilength: -1 for anything that is not a proper list, which is exactly the case
    // the caller's `k >= 0` test exists to reject.
    private static long ListLength(object list)
    {
        long length = 0;
        object cursor = list;
        while (cursor is Pair pair)
        {
            length++;
            cursor = pair.Cdr;
        }

        return cursor is Nil ? length : -1;
    }
}
