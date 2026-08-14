/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1999--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/note-name-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Prints pitches as words — the <c>NoteNames</c> context's engraver.
/// <para>
/// The words themselves come from Scheme: <c>noteNameFunction</c> maps a pitch to a
/// markup (respecting <c>printNotesLanguage</c>, <c>printAccidentalNames</c> and
/// <c>printOctaveNames</c>, which is why this engraver READS them without ever looking
/// at them), and simultaneous notes are joined with <c>noteNameSeparator</c> into one
/// concat markup on a single <c>NoteName</c> grob.
/// </para>
/// </summary>
public class NoteNameEngraver : Engraver
{
    private static readonly Symbol NoteEventSymbol = Symbol.Intern("note-event");
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol NoteNameFunctionSymbol = Symbol.Intern("noteNameFunction");
    private static readonly Symbol NoteNameSeparatorSymbol = Symbol.Intern("noteNameSeparator");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol MakeConcatMarkupSymbol = Symbol.Intern("make-concat-markup");

    private readonly List<StreamEvent> _events = new List<StreamEvent>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public NoteNameEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Note_name_engraver";

    /// <summary>Starts listening for note events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(NoteEventSymbol, ListenNote);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Turns the timestep's notes into one <c>NoteName</c> grob.</summary>
    public override void ProcessMusic()
    {
        object markupList = Nil.Instance;

        for (int i = 0; i < _events.Count; i++)
        {
            object pitch = _events[i].GetProperty(PitchSymbol);
            object proc = GetProperty(NoteNameFunctionSymbol);
            object sep = GetProperty(NoteNameSeparatorSymbol);

            if (i != 0)
            {
                markupList = new Pair(
                    TextInterface.IsMarkup(sep) ? sep : new MutableString(" "),
                    markupList);
            }

            if (SchemeUtilities.IsProcedure(proc))
            {
                object pitchName = SchemeUtilities.CallCallback(proc, pitch, Context);
                markupList = new Pair(pitchName, markupList);
            }
            else
            {
                Warn.ProgrammingError(
                    "No translation function defined as noteNameFunction.");
            }
        }

        if (markupList is Pair)
        {
            Item n = MakeItem("NoteName", _events[0]);
            object concat = Bootstrap.LilyPondScheme.LookupProcedure(MakeConcatMarkupSymbol);
            object text = SchemeUtilities.CallCallback(concat, Reverse(markupList));
            n.SetProperty(TextSymbol, text);
        }
    }

    /// <summary>Forgets the events heard this timestep.</summary>
    public override void StopTranslationTimestep()
    {
        _events.Clear();
    }

    private void ListenNote(StreamEvent ev)
    {
        _events.Add(ev);
    }

    private static object Reverse(object list)
    {
        object result = Nil.Instance;
        object cursor = list;
        while (cursor is Pair pair)
        {
            result = new Pair(pair.Car, result);
            cursor = pair.Cdr;
        }

        return result;
    }
}
