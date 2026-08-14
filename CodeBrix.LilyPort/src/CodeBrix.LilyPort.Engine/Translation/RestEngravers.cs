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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/rest-engraver.cc, lily/rest-collision-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/*
  Should merge with Note_head_engraver
*/

/// <summary>
/// Makes one <c>Rest</c> per rest event, positioned by its pitch when it has one.
/// </summary>
public class RestEngraver : Engraver
{
    private static readonly Symbol RestEventSymbol = Symbol.Intern("rest-event");
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol MiddleCPositionSymbol = Symbol.Intern("middleCPosition");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");

    // Upstream also declares an `Item *dot_` member that nothing but the timestep
    // reset ever touches — dead upstream; carrying it here would be a CS0414 warning.
    // Recorded in PORT-COVERAGE.
    private StreamEvent _restEvent;
    private Grob _rest;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public RestEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Rest_engraver";

    /// <summary>Starts listening for rest events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(RestEventSymbol, ListenRest);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Forgets the previous timestep's event and grobs.</summary>
    public override void StartTranslationTimestep()
    {
        _restEvent = null;
        _rest = null;
    }

    /// <summary>Makes the rest for the event heard this timestep.</summary>
    public override void ProcessMusic()
    {
        if (_restEvent != null && _rest == null)
        {
            _rest = MakeItem("Rest", _restEvent);
            Pitch p = _restEvent.GetProperty(PitchSymbol) as Pitch;

            if (p != null)
            {
                long pos = p.Steps();
                object c0 = GetProperty(MiddleCPositionSymbol);
                if (SchemeConvert.IsNumber(c0))
                {
                    pos += SchemeConvert.ToLong(c0, "middleCPosition");
                }

                _rest.SetProperty(StaffPositionSymbol, pos);
            }
        }
    }

    private void ListenRest(StreamEvent ev) => StreamEvent.AssignEventOnce(ref _restEvent, ev);
}

/// <summary>
/// Watches the sounding grobs and, whenever more than one column is busy and one of
/// them holds a rest, puts them under a <c>RestCollision</c>.
/// <para>
/// The <c>busyGrobs</c> queue this engraver reads is maintained by
/// <c>Grob_pq_engraver</c> (lily/grob-pq-engraver.cc). For an empty queue the
/// property answers the empty list and no collision object is made — which is
/// upstream's own behaviour, not a stub.
/// </para>
/// </summary>
public class RestCollisionEngraver : Engraver
{
    private static readonly Symbol BusyGrobsSymbol = Symbol.Intern("busyGrobs");
    private static readonly Symbol RhythmicHeadInterface = Symbol.Intern("rhythmic-head-interface");
    private static readonly Symbol RestInterface = Symbol.Intern("rest-interface");

    private Grob _restCollision;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public RestCollisionEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Rest_collision_engraver";

    /// <summary>
    /// Collects the busy columns and makes the collision object when rests are among
    /// them.
    /// </summary>
    public override void ProcessAcknowledged()
    {
        int restCount = 0;

        // Upstream collects the columns in a std::unordered_set, whose iteration order
        // is unspecified; the port keeps first-occurrence order, which is
        // deterministic. Recorded in PORT-COVERAGE.
        List<Grob> columns = new List<Grob>();
        Moment now = NowMoment;

        for (object s = GetProperty(BusyGrobsSymbol); s is Pair pair; s = pair.Cdr)
        {
            Pair entry = pair.Car as Pair;
            Grob g = entry?.Cdr as Grob;
            if (entry == null || g == null || !(entry.Car is Moment m))
            {
                continue;
            }

            if (g.HasInterface(RhythmicHeadInterface) && m > now)
            {
                Item column = g.XParent as Item;
                if (column == null)
                {
                    continue;
                }

                // Only include rests that start now. Include notes that started any time.
                PaperColumn paperColumn = column.GetColumn();
                if (!g.HasInterface(RestInterface) || paperColumn == null
                    || PaperColumn.WhenMoment(paperColumn) == now)
                {
                    if (!columns.Contains(column))
                    {
                        columns.Add(column);
                    }

                    restCount += NoteColumn.HasRests(column) ? 1 : 0;
                }
            }
        }

        if (_restCollision == null && restCount != 0 && columns.Count > 1)
        {
            _restCollision = MakeItem("RestCollision", Nil.Instance);
            foreach (Grob g in columns)
            {
                RestCollision.AddColumn(_restCollision, g);
            }
        }
    }

    /// <summary>Forgets the timestep's collision object.</summary>
    public override void StopTranslationTimestep()
    {
        _restCollision = null;
    }
}
