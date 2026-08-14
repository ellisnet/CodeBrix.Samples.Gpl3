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
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/script-engraver.cc, lily/script-column-engraver.cc, lily/script-row-engraver.cc, lily/non-musical-script-column-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - the four script engravers share a file: three of them are the same twenty-line
//     collect-and-column shape differing only in which grobs they collect.
//   - make_script_from_event comes HOME here from the early shared seam file, which was created
//     for it while script-engraver.cc was unported. That seam file is now DELETED — this
//     was its last method — and Multi_measure_rest_engraver calls it here instead.
//   - upstream registers one acknowledger per interface; the port's single AcknowledgeGrob
//     writes the interface tests out in upstream's REGISTRATION ORDER, because a grob
//     carrying two of them must reach the same handler it reaches upstream.

/// <summary>
/// Handles note scripted articulations — turns each articulation event into a
/// <c>Script</c> grob and hangs it off the things it must clear.
/// </summary>
public class ScriptEngraver : Engraver
{
    private static readonly Symbol ArticulationEventSymbol = Symbol.Intern("articulation-event");
    private static readonly Symbol ArticulationTypeSymbol = Symbol.Intern("articulation-type");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol SideRelativeDirectionSymbol
        = Symbol.Intern("side-relative-direction");
    private static readonly Symbol DirectionSourceSymbol = Symbol.Intern("direction-source");

    private static readonly Symbol RhythmicHeadInterface = Symbol.Intern("rhythmic-head-interface");
    private static readonly Symbol StemInterface = Symbol.Intern("stem-interface");
    private static readonly Symbol TieInterface = Symbol.Intern("tie-interface");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol StemTremoloInterface = Symbol.Intern("stem-tremolo-interface");
    private static readonly Symbol InlineAccidentalInterface
        = Symbol.Intern("inline-accidental-interface");

    // upstream's Script_tuple: the event, and the grob process_music makes from it.
    private readonly List<(StreamEvent Event, Grob Script)> _scripts
        = new List<(StreamEvent, Grob)>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public ScriptEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Script_engraver";

    /// <summary>Starts listening for articulation events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(ArticulationEventSymbol, ListenArticulation);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Makes one script grob per articulation collected this timestep.</summary>
    public override void ProcessMusic()
    {
        for (int i = 0; i < _scripts.Count; i++)
        {
            StreamEvent ev = _scripts[i].Event;

            Grob p = MakeItem("Script", ev);

            MakeScriptFromEvent(p, Context, ev.GetProperty(ArticulationTypeSymbol), i);

            _scripts[i] = (ev, p);

            object forceDir = ev.GetProperty(DirectionSymbol);
            if (DirectionalElementInterface.IsDirection(forceDir)
                && DirectionalElementInterface.FromScheme(forceDir, Direction.Center)
                    != Direction.Center)
            {
                p.SetProperty(DirectionSymbol, forceDir);
            }
        }
    }

    /// <summary>Hangs this timestep's scripts off the grobs they must clear.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        Grob grob = info.Grob;

        // Upstream's registration order, written out.
        if (grob.HasInterface(RhythmicHeadInterface))
        {
            AcknowledgeRhythmicHead(info);
        }

        if (grob.HasInterface(StemInterface))
        {
            AcknowledgeStem(info);
        }

        if (grob.HasInterface(TieInterface))
        {
            AddSupportToAll(info.Grob);
        }

        if (grob.HasInterface(NoteColumnInterface) && grob is Item item)
        {
            AcknowledgeNoteColumn(item);
        }

        if (grob.HasInterface(StemTremoloInterface))
        {
            AddSupportToAll(info.Grob);
        }

        if (grob.HasInterface(InlineAccidentalInterface))
        {
            AddSupportToAll(info.Grob);
        }
    }

    /// <summary>Hangs this timestep's scripts off a tie as it ends.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeEndGrob(GrobInfo info)
    {
        if (info.Grob.HasInterface(TieInterface))
        {
            AddSupportToAll(info.Grob);
        }
    }

    /// <summary>Drops this timestep's scripts.</summary>
    public override void StopTranslationTimestep() => _scripts.Clear();

    /// <summary>
    /// Copies an articulation's definition — from the context's <c>scriptDefinitions</c>
    /// alist — onto a freshly made script grob.
    /// </summary>
    /// <param name="p">The script grob to configure.</param>
    /// <param name="tg">The context whose <c>scriptDefinitions</c> to read.</param>
    /// <param name="artType">The articulation type symbol.</param>
    /// <param name="index">The articulation's index in user input order.</param>
    /// <remarks>
    /// <c>Breathing_sign</c> has similar (but simpler) code. A change here might warrant a
    /// change there.
    /// </remarks>
    public static void MakeScriptFromEvent(Grob p, Context tg, object artType, long index)
    {
        if (!(artType is Symbol))
        {
            Warn.ProgrammingError(
                "articulation-type must be a symbol since 2.23.6: "
                + SchemeUtilities.DeepCopy(artType));
        }

        object alist = tg.GetProperty(ScriptDefinitionsSymbol);
        Pair art = SchemeUtilities.Assq(artType, alist);

        if (art == null)
        {
            Warn.Warning("do not know how to interpret articulation: " + artType);
            return;
        }

        object entries = art.Cdr;
        bool priorityFound = false;

        object cursor = entries;
        while (cursor is Pair pair)
        {
            cursor = pair.Cdr;
            if (!(pair.Car is Pair propPair) || !(propPair.Car is Symbol sym))
            {
                continue;
            }

            object type = LilyPondScheme.Current != null
                ? SchemeUtilities.ObjectProperty(
                    LilyPondScheme.Current, sym, BackendTypePredicateSymbol)
                : null;
            if (!SchemeUtilities.IsProcedure(type))
            {
                Warn.ProgrammingError(
                    "invalid grob property name in script definition: " + sym.Name);
                continue;
            }

            object val = propPair.Cdr;

            if (ReferenceEquals(sym, ScriptPriorityPropertySymbol))
            {
                priorityFound = true;
                /* Make sure they're in order of user input by adding index i.
                   Don't use the direction in this priority. Smaller means closer
                   to the head.  */
                if (SchemeConvert.IsNumber(val))
                {
                    val = SchemeConvert.ToLong(val, "script-priority") + index;
                }
            }

            object preset = p.GetPropertyData(sym);
            if (val is Nil
                || !SchemeUtilities.IsSchemeTrue(SchemeUtilities.CallCallback(type, preset)))
            {
                p.SetProperty(sym, val);
            }
        }

        if (!priorityFound)
        {
            p.SetProperty(ScriptPriorityPropertySymbol, index);
        }
    }

    private static readonly Symbol ScriptDefinitionsSymbol = Symbol.Intern("scriptDefinitions");
    private static readonly Symbol ScriptPriorityPropertySymbol
        = Symbol.Intern("script-priority");
    private static readonly Symbol BackendTypePredicateSymbol = Symbol.Intern("backend-type?");

    private void ListenArticulation(StreamEvent ev)
    {
        // Discard double articulations for part-combining.
        for (int i = 0; i < _scripts.Count; i++)
        {
            if (SchemeUtilities.IsEqual(
                    _scripts[i].Event.GetProperty(ArticulationTypeSymbol),
                    ev.GetProperty(ArticulationTypeSymbol)))
            {
                return;
            }
        }

        _scripts.Add((ev, null));
    }

    private void AcknowledgeStem(GrobInfo info)
    {
        for (int i = 0; i < _scripts.Count; i++)
        {
            Grob e = _scripts[i].Script;

            if (DirectionalElementInterface.FromScheme(
                    e.GetProperty(SideRelativeDirectionSymbol), Direction.Center)
                != Direction.Center)
            {
                e.SetObject(DirectionSourceSymbol, info.Grob);
            }

            SidePositionInterface.AddSupport(e, info.Grob);
        }
    }

    private void AddSupportToAll(Grob support)
    {
        for (int i = 0; i < _scripts.Count; i++)
        {
            SidePositionInterface.AddSupport(_scripts[i].Script, support);
        }
    }

    private void AcknowledgeRhythmicHead(GrobInfo info)
    {
        if (info.EventCause != null)
        {
            for (int i = 0; i < _scripts.Count; i++)
            {
                Grob e = _scripts[i].Script;

                if (SidePositionInterface.IsOnXAxis(e) && e.YParent == null)
                {
                    e.YParent = info.Grob;
                }

                SidePositionInterface.AddSupport(e, info.Grob);
            }
        }
    }

    private void AcknowledgeNoteColumn(Item column)
    {
        /* Make note column the parent of the script.  That is not
           correct, but due to seconds in a chord, noteheads may be
           swapped around horizontally.

           As the note head to put it on is not known now, postpone this
           decision to Script_interface::calc_direction ().  */
        for (int i = 0; i < _scripts.Count; i++)
        {
            Grob e = _scripts[i].Script;

            if (e.XParent == null && SidePositionInterface.IsOnYAxis(e))
            {
                e.XParent = column;
            }
        }
    }
}

/// <summary>
/// Finds potentially colliding scripts and puts them into a <c>ScriptColumn</c> object;
/// that will fix the collisions.
/// </summary>
public class ScriptColumnEngraver : Engraver
{
    private static readonly Symbol SidePositionInterfaceSymbol
        = Symbol.Intern("side-position-interface");

    private Item _scriptColumn;
    private readonly List<Item> _scripts = new List<Item>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public ScriptColumnEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Script_column_engraver";

    /// <summary>Collects the musical side-positioned items of this timestep.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob.HasInterface(SidePositionInterfaceSymbol)
            && info.Grob is Item it
            && !Item.IsNonMusical(it))
        {
            _scripts.Add(it);
        }
    }

    /// <summary>Makes the column once more than one script wants one.</summary>
    public override void ProcessAcknowledged()
    {
        if (_scriptColumn == null && _scripts.Count > 1)
        {
            _scriptColumn = MakeItem("ScriptColumn", _scripts[0]);
        }
    }

    /// <summary>Hands the vertically side-positioned scripts to the column.</summary>
    public override void StopTranslationTimestep()
    {
        if (_scriptColumn != null)
        {
            for (int i = 0; i < _scripts.Count; i++)
            {
                if (SidePositionInterface.IsOnYAxis(_scripts[i]))
                {
                    ScriptColumn.AddSidePositioned(_scriptColumn, _scripts[i]);
                }
            }

            _scriptColumn = null;
        }

        _scripts.Clear();
    }
}

/// <summary>
/// Determines order in horizontal side position elements.
/// </summary>
public class ScriptRowEngraver : Engraver
{
    private static readonly Symbol AccidentalPlacementInterface
        = Symbol.Intern("accidental-placement-interface");
    private static readonly Symbol SidePositionInterfaceSymbol
        = Symbol.Intern("side-position-interface");

    private Item _scriptRow;
    private readonly List<Item> _scripts = new List<Item>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public ScriptRowEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Script_row_engraver";

    /// <summary>Collects the accidental placements and side-positioned items.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        // Upstream's registration order: accidental_placement, then side_position.
        if (info.Grob.HasInterface(AccidentalPlacementInterface) && info.Grob is Item accidental)
        {
            _scripts.Add(accidental);
        }

        if (info.Grob.HasInterface(SidePositionInterfaceSymbol)
            && info.Grob is Item it
            && !Item.IsNonMusical(it))
        {
            _scripts.Add(it);
        }
    }

    /// <summary>Makes the row once more than one grob wants one.</summary>
    public override void ProcessAcknowledged()
    {
        if (_scriptRow == null && _scripts.Count > 1)
        {
            _scriptRow = MakeItem("ScriptRow", _scripts[0]);
        }
    }

    /// <summary>Hands the horizontally placed grobs to the row.</summary>
    public override void StopTranslationTimestep()
    {
        if (_scriptRow != null)
        {
            for (int i = 0; i < _scripts.Count; i++)
            {
                Item scr = _scripts[i];
                if (scr.HasInterface(AccidentalPlacementInterface)
                    || SidePositionInterface.IsOnXAxis(scr))
                {
                    ScriptColumn.AddSidePositioned(_scriptRow, scr);
                }
            }

            _scriptRow = null;
        }

        _scripts.Clear();
    }
}

/// <summary>
/// Finds potentially colliding non-musical scripts and puts them into a
/// <c>ScriptColumn</c> object; that will fix the collisions.
/// </summary>
public class NonMusicalScriptColumnEngraver : Engraver
{
    private static readonly Symbol ScriptInterfaceSymbol = Symbol.Intern("script-interface");
    private static readonly Symbol NonMusicalSymbol = Symbol.Intern("non-musical");

    private Item _scriptColumn;
    private readonly List<Item> _scripts = new List<Item>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public NonMusicalScriptColumnEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Non_musical_script_column_engraver";

    /// <summary>Collects the non-musical scripts of this timestep.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob.HasInterface(ScriptInterfaceSymbol)
            && info.Grob is Item it
            && Item.IsNonMusical(it))
        {
            _scripts.Add(it);
        }
    }

    /// <summary>Makes the column once more than one script wants one.</summary>
    public override void ProcessAcknowledged()
    {
        if (_scriptColumn == null && _scripts.Count > 1)
        {
            _scriptColumn = MakeItem("ScriptColumn", _scripts[0]);
            _scriptColumn.SetProperty(NonMusicalSymbol, true);
        }
    }

    /// <summary>Hands the vertically side-positioned scripts to the column.</summary>
    public override void StopTranslationTimestep()
    {
        if (_scriptColumn != null)
        {
            for (int i = 0; i < _scripts.Count; i++)
            {
                Item scr = _scripts[i];
                if (scr.IsLive && SidePositionInterface.IsOnYAxis(scr))
                {
                    ScriptColumn.AddSidePositioned(_scriptColumn, scr);
                }
            }

            _scriptColumn = null;
        }

        _scripts.Clear();
    }
}
