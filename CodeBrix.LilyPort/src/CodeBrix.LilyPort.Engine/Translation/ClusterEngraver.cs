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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/cluster-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - upstream's INT_MIN/INT_MAX seeds for the pitch scan become int.MinValue/MaxValue.
//     They are seeds for a max/min fold over a list the caller has already checked is
//     non-empty, so they can never be the answer.

/// <summary>Engraves a cluster using <c>Spanner</c> notation.</summary>
public sealed class ClusterSpannerEngraver : Engraver
{
    private static readonly Symbol ColumnsSymbol = Symbol.Intern("columns");
    private static readonly Symbol PositionsSymbol = Symbol.Intern("positions");
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol MiddleCPositionSymbol = Symbol.Intern("middleCPosition");
    private static readonly Symbol ClusterNoteEventSymbol = Symbol.Intern("cluster-note-event");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");

    private readonly List<StreamEvent> _clusterNotes = new List<StreamEvent>();
    private Item _beacon;
    private Spanner _spanner;
    private Spanner _finishedSpanner;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public ClusterSpannerEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Cluster_spanner_engraver";

    /// <summary>Starts listening for cluster notes.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(ClusterNoteEventSymbol, ListenClusterNote);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Closes off the last spanner at the end of the context's life.</summary>
    public override void FinalizeTranslation()
    {
        TypesetGrobs();
        _finishedSpanner = _spanner;
        _spanner = null;
        TypesetGrobs();
    }

    /// <summary>Makes the beacon for this timestep's notes and extends the spanner.</summary>
    public override void ProcessMusic()
    {
        if (_clusterNotes.Count > 0)
        {
            object c0scm = GetProperty(MiddleCPositionSymbol);

            int c0 = SchemeConvert.IsNumber(c0scm)
                ? SchemeConvert.ToInt(c0scm, "cluster-spanner-engraver")
                : 0;
            int pmax = int.MinValue;
            int pmin = int.MaxValue;

            for (int i = 0; i < _clusterNotes.Count; i++)
            {
                Pitch pit = _clusterNotes[i].GetProperty(PitchSymbol) as Pitch;

                int p = (pit != null ? pit.Steps() : 0) + c0;

                pmax = System.Math.Max(pmax, p);
                pmin = System.Math.Min(pmin, p);
            }

            _beacon = MakeItem("ClusterSpannerBeacon", _clusterNotes[0]);
            _beacon.SetProperty(PositionsSymbol, new Pair((long)pmin, (long)pmax));
        }

        if (_beacon != null && _spanner == null)
        {
            _spanner = MakeSpanner("ClusterSpanner", _clusterNotes[0]);
        }

        if (_beacon != null && _spanner != null)
        {
            Spanner.AddBoundItem(_spanner, _beacon);
            PointerGroupInterface.AddGrob(_spanner, ColumnsSymbol, _beacon);
        }
    }

    /// <summary>Ends the spanner when a note column arrives with no cluster note.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        // Grob_info_t<Item>: upstream's acknowledger takes an Item.
        if (!(info.Grob is Item) || !info.Grob.HasInterface(NoteColumnInterface))
        {
            return;
        }

        if (_beacon == null)
        {
            _finishedSpanner = _spanner;
            _spanner = null;
        }
    }

    /// <summary>Closes off a finished spanner and forgets this timestep's notes.</summary>
    public override void StopTranslationTimestep()
    {
        TypesetGrobs();
        _clusterNotes.Clear();
    }

    private void TypesetGrobs()
    {
        if (_finishedSpanner != null)
        {
            if (_finishedSpanner.GetBound(Direction.Positive) == null)
            {
                _finishedSpanner.SetBound(
                    Direction.Positive, _finishedSpanner.GetBound(Direction.Negative));
            }

            _finishedSpanner = null;
        }

        _beacon = null;
    }

    private void ListenClusterNote(StreamEvent ev) => _clusterNotes.Add(ev);
}
