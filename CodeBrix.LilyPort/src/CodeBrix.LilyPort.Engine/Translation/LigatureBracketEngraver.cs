/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2002--2026 Juergen Reuter <reuter@ipd.uka.de>

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

using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/ligature-bracket-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-09 as part of the CodeBrix port:
//   - upstream's two acknowledgers are branches of the one AcknowledgeGrob here, selected
//     by the interfaces the ADD_ACKNOWLEDGER macros name.
//   - this engraver is deliberately NOT a LigatureEngraver, upstream or here. It marks a
//     ligature and otherwise leaves the music alone, so it shares none of that class's
//     collect-the-heads machinery.

/// <summary>
/// Handles ligature events by engraving a horizontal square bracket over the notes,
/// leaving their appearance and spacing otherwise untouched.
/// </summary>
/// <remarks>
/// <para>
/// This is the ligature style of contemporary editions transcribing ancient music: the
/// ligature is MARKED rather than drawn, so unlike every other engraver in this group it
/// produces no connected shape and needs none of the head-folding machinery. It is also
/// the only one of them in the default <c>Voice</c> context, which the ancient contexts
/// then <c>\remove</c> in favour of their own.
/// </para>
/// <para>
/// The bracket is a <c>TupletBracket</c> under another name — hence
/// <see cref="TupletBracket.AddColumn"/> here — which upstream's own definition of the
/// grob calls out as ugly and true.
/// </para>
/// </remarks>
public sealed class LigatureBracketEngraver : Engraver
{
    private static readonly Symbol LigatureEventSymbol = Symbol.Intern("ligature-event");
    private static readonly Symbol NoteColumnInterfaceSymbol
        = Symbol.Intern("note-column-interface");

    private static readonly Symbol RestInterfaceSymbol = Symbol.Intern("rest-interface");

    private readonly UniqueSpanEventListener _ligatureListener = new UniqueSpanEventListener();

    private Spanner _ligature;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public LigatureBracketEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Ligature_bracket_engraver";

    /// <summary>Starts listening for ligature events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(LigatureEventSymbol, _ligatureListener.Listen);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Closes the bracket a stop event ends, and opens the one a start begins.</summary>
    public override void ProcessMusic()
    {
        StreamEvent ender = _ligatureListener.Stop;
        if (ender != null)
        {
            if (_ligature == null)
            {
                Epg8Support.EventWarning(ender, "cannot find start of ligature");
                return;
            }

            _ligature = null;
        }

        StreamEvent starter = _ligatureListener.Start;
        if (starter != null)
        {
            if (_ligature != null)
            {
                Epg8Support.EventWarning(starter, "already have a ligature");
                _ligature.Warning("ligature was started here");
                return;
            }

            _ligature = MakeSpanner("LigatureBracket", starter);
        }
    }

    /// <summary>Puts every note column and rest under the open bracket.</summary>
    /// <param name="info">The announced grob.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        Grob grob = info.Grob;
        if (grob.HasInterface(RestInterfaceSymbol)
            || grob.HasInterface(NoteColumnInterfaceSymbol))
        {
            AcknowledgeNoteColumn(info);
        }
    }

    /// <summary>Forgets this timestep's events.</summary>
    public override void StopTranslationTimestep() => _ligatureListener.Reset();

    private void AcknowledgeNoteColumn(GrobInfo info)
    {
        if (_ligature != null)
        {
            // TODO (upstream): We might see a MultiMeasureRest here, which is a Spanner,
            // when called from acknowledge_rest ().  What then?  Is passing a null
            // pointer to these functions OK?
            Item item = info.Grob as Item;
            TupletBracket.AddColumn(_ligature, item);
            Spanner.AddBoundItem(_ligature, item);
        }
    }
}
