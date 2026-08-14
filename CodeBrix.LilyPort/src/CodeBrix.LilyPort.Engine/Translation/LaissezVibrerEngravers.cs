/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
  Copyright (C) 2017--2026 David Kastrup <dak@gnu.org>

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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/laissez-vibrer-engraver.cc, lily/repeat-tie-engraver.cc, lily/include/laissez-vibrer-engraver.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - the two engravers share a file because upstream's repeat-tie-engraver.cc is nothing
//     but a three-method subclass of the laissez-vibrer one; splitting them would put a
//     30-line file beside its own base class.
//   - Repeat_tie_engraver reuses its base's LISTENER METHOD under a different EVENT CLASS
//     (ADD_LISTENER_FOR (listen_laissez_vibrer, repeat_tie)), so the event class is a
//     virtual too, where upstream gets it from the boot macro.

/// <summary>
/// Creates laissez-vibrer ties: the tie that hangs off the right of a note head and
/// connects to nothing.
/// </summary>
public class LaissezVibrerEngraver : Engraver
{
    private static readonly Symbol TiesSymbol = Symbol.Intern("ties");
    private static readonly Symbol NoteHeadSymbol = Symbol.Intern("note-head");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol ArticulationsSymbol = Symbol.Intern("articulations");
    private static readonly Symbol NoteEventSymbol = Symbol.Intern("note-event");
    private static readonly Symbol LaissezVibrerEventSymbol
        = Symbol.Intern("laissez-vibrer-event");
    private static readonly Symbol NoteHeadInterface = Symbol.Intern("note-head-interface");

    private StreamEvent _event;
    private Grob _lvColumn;
    private readonly List<Grob> _lvTies = new List<Grob>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public LaissezVibrerEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Laissez_vibrer_engraver";

    /// <summary>Starts listening for this engraver's event class.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(MyEventClass, ListenLaissezVibrer);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Forgets this timestep's event, column and ties.</summary>
    public override void StopTranslationTimestep()
    {
        _event = null;
        _lvColumn = null;
        _lvTies.Clear();
    }

    /// <summary>Hangs a tie off an acknowledged note head.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!info.Grob.HasInterface(NoteHeadInterface))
        {
            return;
        }

        /* use the heard event_ for all note heads, or an individual event for just
         * a single note head (attached as an articulation inside a chord) */
        StreamEvent tieEv = _event;
        StreamEvent noteEv = info.EventCause;
        if (tieEv == null && noteEv != null && noteEv.IsInEventClass(NoteEventSymbol))
        {
            object articulations = noteEv.GetProperty(ArticulationsSymbol);
            object s = articulations;
            while (tieEv == null && s is Pair pair)
            {
                if (pair.Car is StreamEvent ev && IsMyEventClass(ev))
                {
                    tieEv = ev;
                }

                s = pair.Cdr;
            }
        }

        if (tieEv == null)
        {
            return;
        }

        Grob lvTie = MakeMyTie(tieEv);

        if (_lvColumn == null)
        {
            _lvColumn = MakeMyColumn(lvTie);
        }

        lvTie.SetObject(NoteHeadSymbol, info.Grob);

        PointerGroupInterface.AddGrob(_lvColumn, TiesSymbol, lvTie);

        if (DirectionalElementInterface.IsDirection(tieEv.GetProperty(DirectionSymbol)))
        {
            Direction d = DirectionalElementInterface.FromScheme(
                tieEv.GetProperty(DirectionSymbol), Direction.Center);
            lvTie.SetProperty(DirectionSymbol, (long)(int)d);
        }

        lvTie.YParent = _lvColumn;

        _lvTies.Add(lvTie);
    }

    /// <summary>Gets the event class this engraver listens for.</summary>
    protected virtual Symbol MyEventClass => LaissezVibrerEventSymbol;

    /// <summary>Determines whether an event is one this engraver acts on.</summary>
    /// <param name="ev">The event.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    protected virtual bool IsMyEventClass(StreamEvent ev)
        => ev.IsInEventClass(LaissezVibrerEventSymbol);

    /// <summary>Makes this engraver's kind of tie.</summary>
    /// <param name="cause">The event that caused it.</param>
    /// <returns>The tie.</returns>
    protected virtual Grob MakeMyTie(object cause) => MakeItem("LaissezVibrerTie", cause);

    /// <summary>Makes this engraver's kind of tie column.</summary>
    /// <param name="cause">The grob that caused it.</param>
    /// <returns>The column.</returns>
    protected virtual Grob MakeMyColumn(object cause) => MakeItem("LaissezVibrerTieColumn", cause);

    private void ListenLaissezVibrer(StreamEvent ev) => StreamEvent.AssignEventOnce(ref _event, ev);
}

/// <summary>
/// Creates repeat ties: the tie that arrives at the left of a note head from nothing,
/// which is the mirror image of a laissez-vibrer tie and shares all its machinery.
/// </summary>
public sealed class RepeatTieEngraver : LaissezVibrerEngraver
{
    private static readonly Symbol RepeatTieEventSymbol = Symbol.Intern("repeat-tie-event");

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public RepeatTieEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Repeat_tie_engraver";

    /// <summary>Gets the event class this engraver listens for.</summary>
    protected override Symbol MyEventClass => RepeatTieEventSymbol;

    /// <summary>Determines whether an event is one this engraver acts on.</summary>
    /// <param name="ev">The event.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    protected override bool IsMyEventClass(StreamEvent ev) => ev.IsInEventClass(RepeatTieEventSymbol);

    /// <summary>Makes this engraver's kind of tie.</summary>
    /// <param name="cause">The event that caused it.</param>
    /// <returns>The tie.</returns>
    protected override Grob MakeMyTie(object cause) => MakeItem("RepeatTie", cause);

    /// <summary>Makes this engraver's kind of tie column.</summary>
    /// <param name="cause">The grob that caused it.</param>
    /// <returns>The column.</returns>
    protected override Grob MakeMyColumn(object cause) => MakeItem("RepeatTieColumn", cause);
}
