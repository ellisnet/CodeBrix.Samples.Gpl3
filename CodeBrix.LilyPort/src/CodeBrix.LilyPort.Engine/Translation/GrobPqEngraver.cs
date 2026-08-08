/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2001--2026  Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/grob-pq-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port:
//   - PULLED FORWARD FROM EPG10 by EPG18's demand loop. Lyric extenders are attached to
//     the note heads sounding under them, which get_current_note_head finds by reading
//     `busyGrobs' -- and this engraver is the only thing that ever writes that property.
//     Without it the list is permanently empty, so every extender ends up with no heads
//     and no right bound. It was also the second-most-demanded unported translator in the
//     whole sweep (5,017 "unknown translator" warnings), so the pull-forward is
//     demand-driven twice over.
//   - scm_merge_x merges DESTRUCTIVELY upstream; the port builds a fresh list. The result
//     is the same sequence, and nothing here holds a reference to either input afterwards.
//   - stop_translation_timestep is NOT carried. Upstream's walks the queue past the
//     entries ending NOW into a local that is then discarded — it reads a property, binds
//     two locals and returns, with no side effect of any kind. Reproducing it would add a
//     method that does nothing; it is recorded here instead so a later reader comparing
//     the two files does not take its absence for an omission.

/// <summary>
/// Keeps <c>busyGrobs</c>: the list of grobs that have started sounding and not yet
/// stopped, each paired with the moment it ends, sorted by that moment.
/// <para>
/// It is a priority queue in list form, and the ordering is the whole point — everything
/// that reads it (lyric extenders finding their note heads, rest collisions, melismata)
/// walks from the front and stops at the first entry that is still in the future.
/// </para>
/// <para>
/// Upstream's own caution, kept because it explains the loose ends: this engraver is not
/// water tight, and things like <c>tupletSpannerDuration</c> confuse it.
/// </para>
/// </summary>
public class GrobPqEngraver : Engraver
{
    private static readonly Symbol BusyGrobsSymbol = Symbol.Intern("busyGrobs");
    private static readonly Symbol MultiMeasureInterfaceSymbol
        = Symbol.Intern("multi-measure-interface");

    private readonly List<GrobPqEntry> _startedNow = new List<GrobPqEntry>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public GrobPqEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Grob_pq_engraver";

    /// <summary>
    /// Compares two priority-queue entries by their end moment — the
    /// <c>ly:grob-pq&lt;?</c> entry point.
    /// </summary>
    /// <param name="a">The first <c>(moment . grob)</c> entry.</param>
    /// <param name="b">The second entry.</param>
    /// <returns><see langword="true"/> when the first ends earlier.</returns>
    public static bool PqLess(object a, object b)
    {
        Moment left = (a as Pair)?.Car is Moment am ? am : Moment.Zero;
        Moment right = (b as Pair)?.Car is Moment bm ? bm : Moment.Zero;
        return Moment.Compare(left, right) < 0;
    }

    /// <summary>Starts the queue empty.</summary>
    public override void Initialize()
    {
        base.Initialize();
        Context.SetProperty(BusyGrobsSymbol, Nil.Instance);
    }

    /// <summary>Notes every grob that has a length, so its end can be queued.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        StreamEvent ev = info.EventCause;

        if (ev != null && !info.Grob.HasInterface(MultiMeasureInterfaceSymbol))
        {
            Moment now = NowMoment;
            Moment len = GetEventLength(ev, now);
            if (len.IsNonZero)
            {
                _startedNow.Add(new GrobPqEntry(info.Grob, now + len));
            }
        }
    }

    /// <summary>Merges this time step's new entries into the queue.</summary>
    public override void ProcessAcknowledged()
    {
        // A stable sort: upstream's std::sort is not stable, but its comparator looks only
        // at the end moment, and every caller walks the run of equal moments in full.
        _startedNow.Sort((x, y) => Moment.Compare(x.End, y.End));

        object list = Nil.Instance;
        for (int i = _startedNow.Count - 1; i >= 0; i--)
        {
            list = new Pair(new Pair(_startedNow[i].End, _startedNow[i].Grob), list);
        }

        object busy = GetProperty(BusyGrobsSymbol);
        busy = Merge(list, busy);
        Context.SetProperty(BusyGrobsSymbol, busy);

        _startedNow.Clear();
    }

    /// <summary>Drops the entries that ended before this time step.</summary>
    public override void StartTranslationTimestep()
    {
        Moment now = NowMoment;

        object startBusy = GetProperty(BusyGrobsSymbol);
        object busy = startBusy;
        while (busy is Pair pair && Caar(pair) is Moment end && end < now)
        {
            /*
              The grob-pq-engraver is not water tight, and stuff like
              tupletSpannerDuration confuses it.
            */
            busy = pair.Cdr;
        }

        if (!ReferenceEquals(startBusy, busy))
        {
            Context.SetProperty(BusyGrobsSymbol, busy);
        }
    }

    private static object Caar(Pair pair) => (pair.Car as Pair)?.Car;

    // Guile's merge!: take from the second list only when it compares LESS, so equal
    // moments keep the new entries ahead of the old ones.
    private static object Merge(object a, object b)
    {
        List<object> merged = new List<object>();
        while (a is Pair pa && b is Pair pb)
        {
            if (PqLess(pb.Car, pa.Car))
            {
                merged.Add(pb.Car);
                b = pb.Cdr;
            }
            else
            {
                merged.Add(pa.Car);
                a = pa.Cdr;
            }
        }

        while (a is Pair rest)
        {
            merged.Add(rest.Car);
            a = rest.Cdr;
        }

        while (b is Pair rest)
        {
            merged.Add(rest.Car);
            b = rest.Cdr;
        }

        object result = Nil.Instance;
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            result = new Pair(merged[i], result);
        }

        return result;
    }

    private readonly struct GrobPqEntry
    {
        internal GrobPqEntry(Grob grob, Moment end)
        {
            Grob = grob;
            End = end;
        }

        internal Grob Grob { get; }

        internal Moment End { get; }
    }
}
