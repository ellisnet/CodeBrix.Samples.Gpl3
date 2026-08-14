/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2000--2026 Jan Nieuwenhuizen <janneke@gnu.org>
  Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/part-combine-iterator.cc, lily/part-combine-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The iterator for <c>\partCombine</c>: it runs the part iterators simultaneously and
/// notices when one of them LEAVES a context.
/// <para>
/// Everything else about part combining happens in Scheme
/// (<c>scm/part-combiner.scm</c> decides solo/a2/unisono and rewrites the music). What
/// C++ owns is this single side effect: a context a part iterator has abandoned would
/// otherwise keep counting empty measures, so the iterator broadcasts a duration-less
/// multi-measure-rest event there to end the rest.
/// </para>
/// </summary>
public sealed class PartCombineIterator : SimultaneousMusicIterator
{
    private static readonly Symbol MultiMeasureRestEventSymbol
        = Symbol.Intern("multi-measure-rest-event");

    private static readonly Symbol DurationSymbol = Symbol.Intern("duration");

    private StreamEvent _mmrestEvent;

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Part_combine_iterator";

    /// <summary>
    /// Runs the part iterators, then kills multi-measure rests in every context they
    /// have just stopped using.
    /// </summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        // Catalog the contexts that the part iterators were previously sending
        // events to.
        List<Context> previouslyActive = new List<Context>();
        foreach (MusicIterator child in Children)
        {
            Context c = child.Context;
            if (c != null)
            {
                previouslyActive.Add(c);
            }
        }

        // Run the part iterators.  They may change contexts.
        base.Process(until);

        // Kill multi-measure rests in contexts that were previously active and
        // are no longer active.
        foreach (Context c in previouslyActive)
        {
            if (!IsActiveContext(c))
            {
                KillMultiMeasureRest(c);
            }
        }
    }

    private bool IsActiveContext(Context c)
    {
        foreach (MusicIterator child in Children)
        {
            if (ReferenceEquals(child.Context, c))
            {
                return true;
            }
        }

        return false;
    }

    private void KillMultiMeasureRest(Context c)
    {
        if (_mmrestEvent == null)
        {
            _mmrestEvent = new StreamEvent(
                StreamEvent.MakeEventClass(MultiMeasureRestEventSymbol), Nil.Instance);
            _mmrestEvent.SetProperty(DurationSymbol, Nil.Instance);
        }

        c.EventSource.Broadcast(_mmrestEvent);
    }
}

/// <summary>
/// Prints the part-combine markings — <q>a2</q>, <q>Solo</q>, <q>Solo II</q>,
/// <q>unisono</q> — as a <c>CombineTextScript</c>.
/// <para>
/// The event may arrive a timestep before the text can be made: with
/// <c>partCombineTextsOnNote</c> set, the marking waits until a note is actually heard,
/// which is why the event is held in <c>_waitingEvent</c> rather than consumed where it
/// arrives.
/// </para>
/// </summary>
public class PartCombineEngraver : Engraver
{
    private static readonly Symbol PartCombineEventSymbol = Symbol.Intern("part-combine-event");
    private static readonly Symbol NoteEventSymbol = Symbol.Intern("note-event");
    private static readonly Symbol PartCombineStatusSymbol = Symbol.Intern("part-combine-status");
    private static readonly Symbol Solo1Symbol = Symbol.Intern("solo1");
    private static readonly Symbol Solo2Symbol = Symbol.Intern("solo2");
    private static readonly Symbol UnisonoSymbol = Symbol.Intern("unisono");
    private static readonly Symbol SoloTextSymbol = Symbol.Intern("soloText");
    private static readonly Symbol SoloIiTextSymbol = Symbol.Intern("soloIIText");
    private static readonly Symbol ADueTextSymbol = Symbol.Intern("aDueText");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol PrintPartCombineTextsSymbol
        = Symbol.Intern("printPartCombineTexts");

    private static readonly Symbol PartCombineTextsOnNoteSymbol
        = Symbol.Intern("partCombineTextsOnNote");

    private static readonly Symbol NoteHeadInterface = Symbol.Intern("note-head-interface");
    private static readonly Symbol StemInterface = Symbol.Intern("stem-interface");

    private Item _text;
    private StreamEvent _newEvent; // Event happened at this moment
    private readonly BooleanEventListener _noteListener = new BooleanEventListener();

    // Event possibly from an earlier moment waiting to create a text:
    private StreamEvent _waitingEvent;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public PartCombineEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Part_combine_engraver";

    /// <summary>Starts listening for part-combine events and for notes.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(PartCombineEventSymbol, ListenPartCombine);
        ListenTo(NoteEventSymbol, _noteListener.Listen);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Makes the marking once its condition is met.</summary>
    public override void ProcessMusic()
    {
        if (_waitingEvent != null
            && SchemeUtilities.ToBool(GetProperty(PrintPartCombineTextsSymbol)))
        {
            if (_noteListener.Heard
                || !SchemeUtilities.ToBool(GetProperty(PartCombineTextsOnNoteSymbol)))
            {
                CreateItem(_waitingEvent);
                _waitingEvent = null;
            }
        }
    }

    /// <summary>Attaches the marking to the note heads and stems it sits over.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (_text == null || info.Grob == null)
        {
            return;
        }

        if (info.Grob.HasInterface(NoteHeadInterface))
        {
            Grob t = _text;
            SidePositionInterface.AddSupport(t, info.Grob);
            if (SidePositionInterface.IsOnXAxis(t) && t.YParent == null)
            {
                t.YParent = info.Grob;
            }
        }

        if (info.Grob.HasInterface(StemInterface))
        {
            SidePositionInterface.AddSupport(_text, info.Grob);
        }
    }

    /// <summary>Forgets the timestep's text and event.</summary>
    public override void StopTranslationTimestep()
    {
        _text = null;
        _newEvent = null;
        _noteListener.Reset();
    }

    private void ListenPartCombine(StreamEvent ev)
    {
        StreamEvent.AssignEventOnce(ref _newEvent, ev);

        // If two events occur at the same moment, discard the second as the
        // warning indicates:
        _waitingEvent = _newEvent;
    }

    private void CreateItem(StreamEvent ev)
    {
        object what = ev.GetProperty(PartCombineStatusSymbol);
        object text = Nil.Instance;
        if (ReferenceEquals(what, Solo1Symbol))
        {
            text = GetProperty(SoloTextSymbol);
        }
        else if (ReferenceEquals(what, Solo2Symbol))
        {
            text = GetProperty(SoloIiTextSymbol);
        }
        else if (ReferenceEquals(what, UnisonoSymbol))
        {
            text = GetProperty(ADueTextSymbol);
        }

        if (TextInterface.IsMarkup(text))
        {
            _text = MakeItem("CombineTextScript", ev);
            _text.SetProperty(TextSymbol, text);
        }
    }
}
