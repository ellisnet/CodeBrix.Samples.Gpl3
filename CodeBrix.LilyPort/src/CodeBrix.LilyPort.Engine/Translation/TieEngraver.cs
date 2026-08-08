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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/tie-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - upstream declares acknowledge_note_head and lets the macro layer dispatch by
//     interface; the port's single AcknowledgeGrob writes the interface test out.

/// <summary>
/// Manufactures ties: watches note heads go by, remembers the ones a tie event asked to
/// tie, and joins them to the matching heads at the next timestep.
/// </summary>
/// <remarks>
/// Upstream's own TODO: remove the dependency on musical info — ties should be decided
/// from the heads' position and duration log, not from the events.
/// </remarks>
public class TieEngraver : Engraver
{
    private static readonly Symbol SkipTypesettingSymbol = Symbol.Intern("skipTypesetting");
    private static readonly Symbol TieWaitForNoteSymbol = Symbol.Intern("tieWaitForNote");
    private static readonly Symbol TieMelismaBusySymbol = Symbol.Intern("tieMelismaBusy");
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol ArticulationsSymbol = Symbol.Intern("articulations");
    private static readonly Symbol AutosplitEndSymbol = Symbol.Intern("autosplit-end");
    private static readonly Symbol TieEventSymbol = Symbol.Intern("tie-event");
    private static readonly Symbol NoteEventSymbol = Symbol.Intern("note-event");
    private static readonly Symbol NoteHeadInterface = Symbol.Intern("note-head-interface");

    /*
      Whether tie event has been processed and can be deleted or should
      be kept for later portions of a split note.
    */
    private bool _eventProcessed;
    private StreamEvent _event;
    private readonly List<Grob> _nowHeads = new List<Grob>();
    private readonly List<HeadEventTuple> _headsToTie = new List<HeadEventTuple>();
    private readonly List<Spanner> _ties = new List<Spanner>();

    private Spanner _tieColumn;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public TieEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Tie_engraver";

    /// <summary>Starts listening for tie events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(TieEventSymbol, ListenTie);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Reports that a tie is pending, which keeps a melisma alive.</summary>
    public override void ProcessMusic()
    {
        bool busy = _event != null;
        for (int i = 0; !busy && i < _headsToTie.Count; i++)
        {
            busy |= _headsToTie[i].TieEvent != null || _headsToTie[i].TieStreamEvent != null;
        }

        if (busy)
        {
            Context.SetProperty(TieMelismaBusySymbol, true);
        }
    }

    /// <summary>Notes a head, ties it to a waiting one, and keeps the column up to date.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        Grob h = info.Grob;
        if (!h.HasInterface(NoteHeadInterface))
        {
            return;
        }

        _nowHeads.Add(h);

        if (!TieNotehead(h, false))
        {
            TieNotehead(h, true);
        }

        if (_ties.Count > 0 && _tieColumn == null)
        {
            _tieColumn = MakeSpanner("TieColumn", _ties[0]);
        }

        if (_tieColumn != null)
        {
            for (int i = 0; i < _ties.Count; i++)
            {
                TieColumn.AddTie(_tieColumn, _ties[i]);
            }
        }
    }

    /// <summary>Retires ties whose note never arrived, and reports melisma state.</summary>
    public override void StartTranslationTimestep()
    {
        if (_headsToTie.Count > 0
            && !SchemeUtilities.IsSchemeTrue(GetProperty(TieWaitForNoteSymbol)))
        {
            Moment now = NowMoment;
            for (int i = _headsToTie.Count; i-- > 0;)
            {
                if (now > _headsToTie[i].EndMoment)
                {
                    ReportUnterminatedTie(_headsToTie[i]);
                    _headsToTie.RemoveAt(i);
                }
            }
        }

        Context.SetProperty(TieMelismaBusySymbol, _headsToTie.Count > 0);
    }

    /// <summary>Typesets this timestep's ties and records the heads that want one next.</summary>
    public override void ProcessAcknowledged()
    {
        bool wait = SchemeUtilities.IsSchemeTrue(GetProperty(TieWaitForNoteSymbol));
        if (_ties.Count > 0)
        {
            if (!wait)
            {
                foreach (HeadEventTuple tuple in _headsToTie)
                {
                    ReportUnterminatedTie(tuple);
                }

                _headsToTie.Clear();
            }

            for (int i = 0; i < _ties.Count; i++)
            {
                TypesetTie(_ties[i]);
            }

            _ties.Clear();
            _tieColumn = null;
        }

        List<HeadEventTuple> newHeadsToTie = new List<HeadEventTuple>();

        for (int i = 0; i < _nowHeads.Count; i++)
        {
            Grob head = _nowHeads[i];
            StreamEvent leftEv = head.EventCause();

            if (leftEv == null)
            {
                // may happen for ambitus
                continue;
            }

            // We only want real notes to cause ties, not e.g. pitched trills
            if (!leftEv.IsInEventClass(NoteEventSymbol))
            {
                continue;
            }

            object leftArticulations = leftEv.GetProperty(ArticulationsSymbol);

            StreamEvent tieEvent = null;
            StreamEvent tieStreamEvent = _event;
            object s = leftArticulations;
            while (tieEvent == null && tieStreamEvent == null && s is Pair pair)
            {
                if (pair.Car is StreamEvent ev && ev.IsInEventClass(TieEventSymbol))
                {
                    tieEvent = ev;
                }

                s = pair.Cdr;
            }

            if ((tieEvent != null || tieStreamEvent != null) && !HasAutosplitEnd(leftEv))
            {
                _eventProcessed = true;

                HeadEventTuple eventTup = new HeadEventTuple();

                eventTup.Head = head;
                eventTup.TieEvent = tieEvent;
                eventTup.TieStreamEvent = tieStreamEvent;
                eventTup.Tie = MakeSpanner("Tie", tieEvent != null ? tieEvent : tieStreamEvent);

                Moment now = NowMoment;
                eventTup.EndMoment = now + GetEventLength(leftEv, now);

                newHeadsToTie.Add(eventTup);
            }
        }

        if (!wait && newHeadsToTie.Count > 0)
        {
            foreach (HeadEventTuple tuple in _headsToTie)
            {
                ReportUnterminatedTie(tuple);
            }

            _headsToTie.Clear();
        }

        // hmmm, how to do with copy () ?
        for (int i = 0; i < newHeadsToTie.Count; i++)
        {
            _headsToTie.Add(newHeadsToTie[i]);
        }

        _nowHeads.Clear();
    }

    /// <summary>Discards the tie event once it has actually tied something.</summary>
    public override void StopTranslationTimestep()
    {
        /*
          Discard event only if it has been processed with at least one
          appropriate note.
        */
        if (_eventProcessed)
        {
            _event = null;
        }

        _eventProcessed = false;
    }

    private void ListenTie(StreamEvent ev)
    {
        if (!SchemeUtilities.IsSchemeTrue(GetProperty(SkipTypesettingSymbol)))
        {
            StreamEvent.AssignEventOnce(ref _event, ev);
        }
    }

    private static void ReportUnterminatedTie(HeadEventTuple tieStart)
    {
        /*
          If tie_from_chord_created is set, we have another note at the same
          moment that created a tie, so this is not necessarily an unterminated
          tie. Happens e.g. for <c e g>~ g
        */
        if (!tieStart.TieFromChordCreated)
        {
            tieStart.Tie.Warning("unterminated tie");
            tieStart.Tie.Suicide();
        }
    }

    /*
      Determines whether the end of an event was created by
      a split in Completion_heads_engraver or by user input.
    */
    private static bool HasAutosplitEnd(StreamEvent streamEvent)
        => streamEvent != null
           && SchemeUtilities.IsSchemeTrue(streamEvent.GetProperty(AutosplitEndSymbol));

    private bool TieNotehead(Grob h, bool enharmonic)
    {
        bool found = false;

        for (int i = 0; i < _headsToTie.Count; i++)
        {
            Grob th = _headsToTie[i].Head;
            StreamEvent rightEv = h.EventCause();
            StreamEvent leftEv = th.EventCause();

            // maybe should check positions too.
            if (rightEv == null || leftEv == null)
            {
                continue;
            }

            /*
              Make a tie only if pitches are equal or if event end was not generated by
              Completion_heads_engraver.
            */
            object p1 = leftEv.GetProperty(PitchSymbol);
            object p2 = rightEv.GetProperty(PitchSymbol);
            bool pitchesMatch = enharmonic
                ? p1 is Pitch pitch1 && p2 is Pitch pitch2
                  && pitch1.TonePitch() == pitch2.TonePitch()
                : SchemeUtilities.IsEqual(p1, p2);

            if (pitchesMatch && !HasAutosplitEnd(leftEv))
            {
                Spanner p = _headsToTie[i].Tie;
                Moment end = _headsToTie[i].EndMoment;

                StreamEvent cause = _headsToTie[i].TieEvent ?? _headsToTie[i].TieStreamEvent;

                AnnounceEndGrob(p, cause);

                Tie.SetHead(p, Direction.Negative, th);
                Tie.SetHead(p, Direction.Positive, h);

                if (DirectionalElementInterface.IsDirection(cause.GetProperty(DirectionSymbol)))
                {
                    Direction d = DirectionalElementInterface.FromScheme(
                        cause.GetProperty(DirectionSymbol), Direction.Center);
                    p.SetProperty(DirectionSymbol, (long)(int)d);
                }

                _ties.Add(p);
                _headsToTie.RemoveAt(i);

                found = true;

                /*
                  Prevent all other tied notes ending at the same moment (assume
                  implicitly the notes have also started at the same moment!)
                  from triggering an "unterminated tie" warning. Needed e.g. for
                  <c e g>~ g
                */
                for (int j = _headsToTie.Count; j-- > 0;)
                {
                    if (_headsToTie[j].EndMoment == end)
                    {
                        _headsToTie[j].TieFromChordCreated = true;
                    }
                }

                break;
            }
        }

        return found;
    }

    private static void TypesetTie(Spanner her)
    {
        Grob leftHead = Tie.Head(her, Direction.Negative);
        Grob rightHead = Tie.Head(her, Direction.Positive);

        if (leftHead == null || rightHead == null)
        {
            Warn.Warning("lonely tie");
            if (leftHead == null)
            {
                leftHead = rightHead;
            }
            else
            {
                rightHead = leftHead;
            }
        }

        her.SetBound(Direction.Negative, leftHead);
        her.SetBound(Direction.Positive, rightHead);
    }

    /// <summary>
    /// One head waiting for its partner, and the tie already made for the pair.
    /// </summary>
    /// <remarks>
    /// A CLASS, not a struct: <c>tie_from_chord_created</c> is written through the list
    /// after the tuple is stored, and upstream writes it through the vector element.
    /// </remarks>
    private sealed class HeadEventTuple
    {
        internal Grob Head;
        internal Moment EndMoment;
        internal StreamEvent TieStreamEvent;
        internal StreamEvent TieEvent;
        internal Spanner Tie;

        /*
          Indicate whether a tie from the same moment has been processed successfully
          This is needed for tied chords, e.g. <c e g>~ g, because otherwise the c
          and e will trigger a warning for an unterminated tie!
        */
        internal bool TieFromChordCreated;
    }
}
