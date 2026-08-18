/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/tuplet-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - upstream declares four separate acknowledgers (note_column, script, finger,
//     string_number) and the macro layer dispatches by interface. The port has a single
//     AcknowledgeGrob, so the interface tests that the macros generate are written out.

/// <summary>
/// Catches tuplet events and generates the appropriate bracket and number, keeping a
/// stack of the tuplets currently open so nested tuplets each get their own.
/// </summary>
public class TupletEngraver : Engraver
{
    private static readonly Symbol BracketSymbol = Symbol.Intern("bracket");
    private static readonly Symbol CurrentCommandColumnSymbol
        = Symbol.Intern("currentCommandColumn");

    private static readonly Symbol CurrentMusicalColumnSymbol
        = Symbol.Intern("currentMusicalColumn");

    private static readonly Symbol CurrentTupletDescriptionSymbol
        = Symbol.Intern("currentTupletDescription");

    private static readonly Symbol DynamicInterfaceSymbol = Symbol.Intern("dynamic-interface");
    private static readonly Symbol FingerInterfaceSymbol = Symbol.Intern("finger-interface");
    private static readonly Symbol NoteColumnInterfaceSymbol
        = Symbol.Intern("note-column-interface");

    private static readonly Symbol ScriptInterfaceSymbol = Symbol.Intern("script-interface");
    private static readonly Symbol SkipTypesettingSymbol = Symbol.Intern("skipTypesetting");
    private static readonly Symbol SpanDirectionSymbol = Symbol.Intern("span-direction");
    private static readonly Symbol StringNumberInterfaceSymbol
        = Symbol.Intern("string-number-interface");

    private static readonly Symbol TupletFullLengthSymbol = Symbol.Intern("tupletFullLength");
    private static readonly Symbol TupletFullLengthNoteSymbol
        = Symbol.Intern("tupletFullLengthNote");

    private static readonly Symbol TupletNumberSymbol = Symbol.Intern("tuplet-number");
    private static readonly Symbol TupletSpanEventSymbol = Symbol.Intern("tuplet-span-event");

    private readonly List<TupletDescription> _tuplets = new List<TupletDescription>();
    private readonly List<TupletDescription> _newTuplets = new List<TupletDescription>();
    private readonly List<TupletDescription> _stoppedTuplets = new List<TupletDescription>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public TupletEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Tuplet_engraver";

    /// <summary>Starts listening for tuplet span events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(TupletSpanEventSymbol, ListenTupletSpan);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Closes finished tuplets, opens new ones, and makes their grobs.</summary>
    public override void ProcessMusic()
    {
        // This may happen if the end of a tuplet is part of a quoted voice.
        Moment now = NowMoment;
        while (_tuplets.Count > 0 && _tuplets[_tuplets.Count - 1].StopMoment == now)
        {
            _stoppedTuplets.Add(_tuplets[_tuplets.Count - 1]);
            _tuplets.RemoveAt(_tuplets.Count - 1);
        }

        foreach (TupletDescription tuplet in _stoppedTuplets)
        {
            if (tuplet.Bracket == null)
            {
                continue;
            }

            Item left = tuplet.Bracket.GetBound(Direction.Negative);
            if (left != null)
            {
                if (tuplet.FullLength)
                {
                    Item col = (tuplet.FullLengthNote
                        ? GetProperty(CurrentMusicalColumnSymbol)
                        : GetProperty(CurrentCommandColumnSymbol)) as Item;

                    tuplet.Bracket.SetBound(Direction.Positive, col);
                    tuplet.Number?.SetBound(Direction.Positive, col);
                }
                else if (tuplet.Bracket.GetBound(Direction.Positive) == null)
                {
                    // This tuplet only spans one note, e.g. \tuplet 3/2 { s8 c'8 s8 }.
                    tuplet.Bracket.SetBound(Direction.Positive, left);
                    tuplet.Number?.SetBound(Direction.Positive, left);
                }
            }
            else
            {
                // This tuplet spans no notes at all, e.g. \tuplet 3/2 { s8 s8 s8 }.
                // Remove it.
                tuplet.Bracket.Suicide();
                tuplet.Number?.Suicide();
            }
        }

        foreach (TupletDescription tuplet in _newTuplets)
        {
            if (_tuplets.Count > 0)
            {
                tuplet.Parent = _tuplets[_tuplets.Count - 1];
            }

            _tuplets.Add(tuplet);
        }

        _newTuplets.Clear();

        Context?.SetProperty(
            CurrentTupletDescriptionSymbol,
            _tuplets.Count == 0 ? (object)Nil.Instance : _tuplets[_tuplets.Count - 1]);

        for (int i = _tuplets.Count; i-- > 0;)
        {
            if (_tuplets[i].Bracket != null)
            {
                continue;
            }

            _tuplets[i].FullLength
                = GetProperty(TupletFullLengthSymbol) is bool full && full;

            _tuplets[i].FullLengthNote
                = GetProperty(TupletFullLengthNoteSymbol) is bool fullNote && fullNote;

            _tuplets[i].Bracket = MakeSpanner("TupletBracket", _tuplets[i].Event);
            _tuplets[i].Number = MakeSpanner("TupletNumber", _tuplets[i].Event);

            Spanner bracket = _tuplets[i].Bracket;
            Spanner number = _tuplets[i].Number;
            number.SetObject(BracketSymbol, bracket);
            bracket.SetObject(TupletNumberSymbol, number);
            number.SetParent(bracket, Axis.X);
            number.SetParent(bracket, Axis.Y);

            if (i + 1 < _tuplets.Count && _tuplets[i + 1].Bracket != null)
            {
                TupletBracket.AddTupletBracket(bracket, _tuplets[i + 1].Bracket);
            }

            if (i > 0 && _tuplets[i - 1].Bracket != null)
            {
                TupletBracket.AddTupletBracket(_tuplets[i - 1].Bracket, bracket);
            }
        }
    }

    /// <summary>Feeds note columns, scripts, fingerings and string numbers to the brackets.</summary>
    /// <param name="info">The announced grob.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        Grob grob = info.Grob;
        if (grob == null)
        {
            return;
        }

        if (grob is Item noteColumn && grob.HasInterface(NoteColumnInterfaceSymbol))
        {
            foreach (TupletDescription tuplet in _tuplets)
            {
                if (tuplet.Bracket != null)
                {
                    TupletBracket.AddColumn(tuplet.Bracket, noteColumn);
                    Spanner.AddBoundItem(tuplet.Number, noteColumn);
                }
            }

            return;
        }

        if (grob.HasInterface(ScriptInterfaceSymbol))
        {
            // MultiMeasureRestScript is a Spanner. Putting one inside a tuplet is
            // contrived, and upstream ignores it rather than handle it.
            if (grob is Item script && !script.HasInterface(DynamicInterfaceSymbol))
            {
                AddScriptToAllTuplets(script);
            }

            return;
        }

        if ((grob.HasInterface(FingerInterfaceSymbol)
             || grob.HasInterface(StringNumberInterfaceSymbol))
            && grob is Item item)
        {
            AddScriptToAllTuplets(item);
        }
    }

    /// <summary>Clears the per-timestep buckets.</summary>
    public override void StartTranslationTimestep()
    {
        _stoppedTuplets.Clear();

        // May seem superfluous, but necessary for skipTypesetting.
        _newTuplets.Clear();
    }

    /// <summary>Pulls back bounds that would otherwise run past the end of the piece.</summary>
    public override void FinalizeTranslation()
    {
        // If tupletFullLengthNote is used, fix up bounds to avoid grobs extending to the
        // musical column of the last time step, which is after the end of the piece.
        Item col = GetProperty(CurrentCommandColumnSymbol) as Item;
        foreach (TupletDescription description in _stoppedTuplets)
        {
            if (description.FullLengthNote)
            {
                description.Bracket?.SetBound(Direction.Positive, col);
                description.Number?.SetBound(Direction.Positive, col);
            }
        }

        base.FinalizeTranslation();
    }

    private void ListenTupletSpan(StreamEvent ev)
    {
        Direction dir = DirectionalElementInterface.FromScheme(
            ev.GetProperty(SpanDirectionSymbol), Direction.Center);

        if (dir == Direction.Negative)
        {
            TupletDescription newTuplet = new TupletDescription(ev, NowMoment);

            foreach (TupletDescription existing in _newTuplets)
            {
                if (existing == newTuplet)
                {
                    // Do not add an already-existing tuplet.
                    return;
                }
            }

            _newTuplets.Add(newTuplet);
        }
        else if (dir == Direction.Positive)
        {
            if (_tuplets.Count > 0)
            {
                _stoppedTuplets.Add(_tuplets[_tuplets.Count - 1]);
                _tuplets.RemoveAt(_tuplets.Count - 1);
            }
            else if (!(GetProperty(SkipTypesettingSymbol) is bool skip && skip))
            {
                //was previously: EventWarning. Upstream writes ev->debug_output here, not
                // ev->warning — a line that only appears under -ddebug-… . The port
                // printed it as a warning on every run, which is the whole of
                // part-combine-tuplet-single's diagnostics row: \partCombine ends the
                // same tuplet twice and upstream says so only to a debugging reader.
                // Rule 15: severity is part of the translation.
                TranslatorSchemeHelpers.EventDebugOutput(ev, "No tuplet to end");
            }
        }
        else
        {
            TranslatorSchemeHelpers.EventProgrammingError(ev, "direction tuplet-span-event_ invalid.");
        }
    }

    private void AddScriptToAllTuplets(Item script)
    {
        foreach (TupletDescription tuplet in _tuplets)
        {
            if (tuplet.Bracket != null)
            {
                TupletBracket.AddScript(tuplet.Bracket, script);
            }
        }
    }
}
