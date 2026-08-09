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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/drum-note-engraver.cc;

/// <summary>Generates drum note heads.</summary>
public sealed class DrumNotesEngraver : Engraver
{
    private static readonly Symbol NoteEventSymbol = Symbol.Intern("note-event");
    private static readonly Symbol DrumStyleTableSymbol = Symbol.Intern("drumStyleTable");
    private static readonly Symbol DrumTypeSymbol = Symbol.Intern("drum-type");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol ScriptDefinitionsSymbol = Symbol.Intern("scriptDefinitions");
    private static readonly Symbol SideRelativeDirectionSymbol
        = Symbol.Intern("side-relative-direction");
    private static readonly Symbol DirectionSourceSymbol = Symbol.Intern("direction-source");
    private static readonly Symbol StemInterface = Symbol.Intern("stem-interface");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");

    private readonly List<Item> _scripts = new List<Item>();
    private readonly List<StreamEvent> _events = new List<StreamEvent>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public DrumNotesEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Drum_notes_engraver";

    /// <summary>Starts listening for notes.</summary>
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

    /// <summary>Makes a note head, and any percussion sign, per heard drum note.</summary>
    public override void ProcessMusic()
    {
        if (_events.Count == 0)
        {
            return;
        }

        object tab = GetProperty(DrumStyleTableSymbol);
        for (int i = 0; i < _events.Count; i++)
        {
            StreamEvent ev = _events[i];
            Item note = MakeItem("NoteHead", ev);

            object drumType = ev.GetProperty(DrumTypeSymbol);

            object defn = Nil.Instance;

            if (tab is SchemeHashTable hashTable)
            {
                // scm_hashq_ref with '() as the default: the handle is the key/value
                // pair, and its absence means the drum type is not in the table.
                Pair handle = hashTable.GetHandle(drumType);
                defn = handle != null ? handle.Cdr : Nil.Instance;
            }

            if (defn is Pair definition)
            {
                object style = definition.Car;
                object script = Cadr(definition);
                object pos = Caddr(definition);

                if (pos is long || pos is System.Numerics.BigInteger)
                {
                    note.SetProperty(StaffPositionSymbol, pos);
                }

                if (style is Symbol)
                {
                    note.SetProperty(StyleSymbol, style);
                }

                object dir = Nil.Instance;
                if (script is Pair scriptPair)
                {
                    dir = scriptPair.Cdr;
                    script = scriptPair.Car;
                }

                if (SchemeUtilities.IsSchemeTrue(script))
                {
                    // Error out if script doesn't exist
                    if (SchemeUtilities.LyAssoc(
                            script, Context?.GetProperty(ScriptDefinitionsSymbol)) == null)
                    {
                        Input origin = ev.Origin as Input;
                        string message = "unrecognised percussion sign: \""
                                         + SchemeUtilities.RobustSymbolToString(script, "?")
                                         + "\"";
                        if (origin != null)
                        {
                            origin.Error(message);
                        }
                        else
                        {
                            Warn.Error(message);
                        }
                    }

                    Item p = MakeItem("Script", ev);
                    ScriptEngraver.MakeScriptFromEvent(p, Context, script, 0);
                    if (DirectionalElementInterface.IsDirection(dir))
                    {
                        p.SetProperty(DirectionSymbol, dir);
                    }

                    p.YParent = note;
                    SidePositionInterface.AddSupport(p, note);
                    _scripts.Add(p);
                }
            }
        }
    }

    /// <summary>Hangs this timestep's percussion signs off the stem and note column.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob.HasInterface(StemInterface))
        {
            AcknowledgeStem(info);
        }

        // Grob_info_t<Item>: upstream's note-column acknowledger takes an Item.
        if (info.Grob is Item && info.Grob.HasInterface(NoteColumnInterface))
        {
            AcknowledgeNoteColumn(info);
        }
    }

    /// <summary>Forgets this timestep's signs and events.</summary>
    public override void StopTranslationTimestep()
    {
        _scripts.Clear();
        _events.Clear();
    }

    private static object Cadr(Pair pair) => pair.Cdr is Pair second ? second.Car : Nil.Instance;

    private static object Caddr(Pair pair)
        => pair.Cdr is Pair second && second.Cdr is Pair third ? third.Car : Nil.Instance;

    private void AcknowledgeStem(GrobInfo inf)
    {
        for (int i = 0; i < _scripts.Count; i++)
        {
            Grob e = _scripts[i];

            if (DirectionalElementInterface.FromScheme(
                    e.GetProperty(SideRelativeDirectionSymbol), Direction.Center)
                != Direction.Center)
            {
                e.SetObject(DirectionSourceSymbol, inf.Grob);
            }

            SidePositionInterface.AddSupport(e, inf.Grob);
        }
    }

    private void AcknowledgeNoteColumn(GrobInfo inf)
    {
        for (int i = 0; i < _scripts.Count; i++)
        {
            Grob e = _scripts[i];

            if (e.XParent == null && SidePositionInterface.IsOnYAxis(e))
            {
                e.XParent = inf.Grob;
            }

            SidePositionInterface.AddSupport(e, inf.Grob);
        }
    }

    private void ListenNote(StreamEvent ev) => _events.Add(ev);
}
