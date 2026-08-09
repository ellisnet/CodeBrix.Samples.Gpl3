/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2010--2026 Neil Puttock <n.puttock@gmail.com>

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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/episema-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-09 as part of the CodeBrix port:
//   - upstream's two acknowledgers are branches of the one AcknowledgeGrob here, selected
//     by the interfaces the ADD_ACKNOWLEDGER macros name, and in the ORDER the macros
//     declare them -- note_column first, then note_head -- because a grob carrying both
//     would reach both hooks.

/// <summary>
/// Creates an <em>Editio Vaticana</em>-style episema line over a group of notes.
/// </summary>
/// <remarks>
/// ⚠ This engraver uses a <see cref="LastSpanEventListener"/>, NOT the unique one every
/// other engraver in this group uses, and the reason is notational: an episema can be
/// typeset over a SINGLE neume, so its start and stop arrive in the same timestep and a
/// unique listener would call the second one a duplicate.
/// </remarks>
public sealed class EpisemaEngraver : Engraver
{
    private static readonly Symbol CurrentMusicalColumnSymbol
        = Symbol.Intern("currentMusicalColumn");

    private static readonly Symbol EpisemaEventSymbol = Symbol.Intern("episema-event");
    private static readonly Symbol NoteColumnInterfaceSymbol
        = Symbol.Intern("note-column-interface");

    private static readonly Symbol NoteHeadInterfaceSymbol
        = Symbol.Intern("note-head-interface");

    // Must not use UniqueSpanEventListener since episema can be typeset over a
    // single neume.
    private readonly LastSpanEventListener _episemaListener = new LastSpanEventListener();
    private readonly List<Item> _noteColumns = new List<Item>();

    private Spanner _span;
    private Spanner _finished;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public EpisemaEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Episema_engraver";

    /// <summary>Starts listening for episema events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(EpisemaEventSymbol, _episemaListener.Listen);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Opens the episema a start event begins, and ends the one a stop event closes.</summary>
    public override void ProcessMusic()
    {
        StreamEvent starter = _episemaListener.Start;
        if (starter != null)
        {
            if (_span != null)
            {
                Epg8Support.EventWarning(starter, "already have an episema");
                _span.Warning("episema was started here");
            }
            else
            {
                _span = MakeSpanner("Episema", starter);
            }
        }

        StreamEvent ender = _episemaListener.Stop;
        if (ender != null)
        {
            if (_span == null)
            {
                Epg8Support.EventWarning(ender, "cannot find start of episema");
            }
            else
            {
                _finished = _span;
                AnnounceEndGrob(_finished, Nil.Instance);
                _span = null;
                _noteColumns.Clear();
            }
        }
    }

    /// <summary>Bounds the open episema on the left, then finishes the ended one.</summary>
    public override void StopTranslationTimestep()
    {
        if (_span != null && _span.GetBound(Direction.Negative) == null)
        {
            Item col = _noteColumns.Count != 0
                ? _noteColumns[0]
                : GetProperty(CurrentMusicalColumnSymbol) as Item;
            _span.SetBound(Direction.Negative, col);
        }

        TypesetAll();
        _episemaListener.Reset();
    }

    /// <summary>Finishes the ended episema, and kills an unterminated one.</summary>
    public override void FinalizeTranslation()
    {
        TypesetAll();
        if (_span != null)
        {
            _span.Warning("unterminated episema");
            _span.Suicide();
            _span = null;
        }
    }

    /// <summary>Remembers the note columns, and supports the episema on the note heads.</summary>
    /// <param name="info">The announced grob.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        Grob grob = info.Grob;
        if (grob is Item item && grob.HasInterface(NoteColumnInterfaceSymbol))
        {
            _noteColumns.Add(item);
        }

        if (grob.HasInterface(NoteHeadInterfaceSymbol))
        {
            AcknowledgeNoteHead(info);
        }
    }

    private void TypesetAll()
    {
        if (_finished != null)
        {
            if (_finished.GetBound(Direction.Positive) == null)
            {
                Item col = _noteColumns.Count != 0
                    ? _noteColumns[_noteColumns.Count - 1]
                    : GetProperty(CurrentMusicalColumnSymbol) as Item;
                _finished.SetBound(Direction.Positive, col);
            }

            _finished = null;
        }
    }

    private void AcknowledgeNoteHead(GrobInfo info)
    {
        if (_span != null)
        {
            SidePositionInterface.AddSupport(_span, info.Grob);
            Spanner.AddBoundItem(_span, info.Grob);
        }
        else if (_finished != null)
        {
            SidePositionInterface.AddSupport(_finished, info.Grob);
            Spanner.AddBoundItem(_finished, info.Grob);
        }
    }
}
