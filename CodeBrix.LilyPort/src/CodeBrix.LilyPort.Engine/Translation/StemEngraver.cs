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
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/stem-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Creates stems, flags and single-stem tremolos. It also works together with the beam
/// engraver for overriding beaming.
/// <para>
/// One stem is made per timestep, on the first rhythmic head heard, and every further
/// head of the same timestep joins it — which is how a chord shares one stem. A flag
/// is made eagerly for every flagged duration and killed again at the timestep's end
/// if a beam claimed the stem, because whether a beam will claim it is not knowable at
/// acknowledgement time.
/// </para>
/// </summary>
public class StemEngraver : Engraver
{
    private static readonly Symbol TremoloEventSymbol = Symbol.Intern("tremolo-event");
    private static readonly Symbol RhythmicHeadInterfaceSymbol
        = Symbol.Intern("rhythmic-head-interface");

    private static readonly Symbol TremoloTypeSymbol = Symbol.Intern("tremolo-type");
    private static readonly Symbol DurationSymbol = Symbol.Intern("duration");
    private static readonly Symbol FlagCountSymbol = Symbol.Intern("flag-count");
    private static readonly Symbol TremoloFlagSymbol = Symbol.Intern("tremolo-flag");
    private static readonly Symbol StemSymbol = Symbol.Intern("stem");
    private static readonly Symbol FlagSymbol = Symbol.Intern("flag");
    private static readonly Symbol BeamSymbol = Symbol.Intern("beam");
    private static readonly Symbol CurrentBarLineSymbol = Symbol.Intern("currentBarLine");
    private static readonly Symbol StemLeftBeamCountSymbol = Symbol.Intern("stemLeftBeamCount");
    private static readonly Symbol StemRightBeamCountSymbol = Symbol.Intern("stemRightBeamCount");

    private Grob _stem;
    private Grob _tremolo;
    private readonly List<Item> _maybeFlags = new List<Item>();
    private StreamEvent _tremoloEvent;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public StemEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Stem_engraver";

    /// <summary>Starts listening for tremolo events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(TremoloEventSymbol, ListenTremolo);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    private void MakeStem(GrobInfo gi)
    {
        /* Announce the cause of the head as cause of the stem.  The
           stem needs a rhythmic structure to fit it into a beam.  */
        _stem = MakeItem("Stem", gi.Grob);
        _ = MakeItem("StemStub", gi.Grob);
        if (_tremoloEvent != null)
        {
            /* Stem tremolo is never applied to a note by default,
               it must be requested.  But there is a default for the
               tremolo value:

               c4:8 c c:

               the first and last (quarter) note both get one tremolo flag.  */
            object typeValue = _tremoloEvent.GetProperty(TremoloTypeSymbol);
            int requestedType = SchemeConvert.IsNumber(typeValue)
                ? SchemeConvert.ToInt(typeValue, "tremolo-type")
                : 8;

            /*
              we take the duration log from the Event, since the duration-log
              for a note head is always <= 2.
            */
            StreamEvent ev = gi.EventCause;
            int durationLog = ev?.GetProperty(DurationSymbol) is Duration dur
                ? dur.DurationLog
                : 0;

            int tremoloFlags
                = IntLog2(requestedType) - 2
                  - (durationLog > 2 ? durationLog - 2 : 0);
            if (tremoloFlags <= 0)
            {
                WarnEvent(_tremoloEvent, "tremolo duration is too long");
                tremoloFlags = 0;
            }

            if (tremoloFlags != 0)
            {
                _tremolo = MakeItem("StemTremolo", _tremoloEvent);

                /* The number of tremolo flags is the number of flags of the
                   tremolo-type minus the number of flags of the note itself.  */
                _tremolo.SetProperty(FlagCountSymbol, (long)tremoloFlags);
                _tremolo.XParent = _stem;
                _stem.SetObject(TremoloFlagSymbol, _tremolo);
                _tremolo.SetObject(StemSymbol, _stem);
            }
        }
    }

    /// <summary>
    /// Gives every rhythmic head of the timestep the one stem, making it on the first.
    /// </summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!info.Grob.HasInterface(RhythmicHeadInterfaceSymbol))
        {
            return;
        }

        AcknowledgeRhythmicHead(info);
    }

    private void AcknowledgeRhythmicHead(GrobInfo gi)
    {
        if (gi.Grob.GetObject(StemSymbol) is Grob)
        {
            return;
        }

        StreamEvent cause = gi.EventCause;
        if (cause == null)
        {
            return;
        }

        if (!(cause.GetProperty(DurationSymbol) is Duration d))
        {
            return;
        }

        if (_stem == null)
        {
            MakeStem(gi);
        }

        int ds = Stem.DurationLog(_stem);
        int dc = d.DurationLog;

        // half notes and quarter notes all have compatible stems.
        // Longas are done differently (oops?), so we can't unify
        // them with the other stemmed notes.
        if (ds == 1)
        {
            ds = 2;
        }

        if (dc == 1)
        {
            dc = 2;
        }

        // whole notes and brevis both have no stems
        if (ds == -1)
        {
            ds = 0;
        }

        if (dc == -1)
        {
            dc = 0;
        }

        if (ds != dc)
        {
            WarnEvent(
                cause,
                "adding note head to incompatible stem (type = "
                + (ds < 0 ? 1 << -ds : 1) + "/" + (ds > 0 ? 1 << ds : 1) + ")");
            WarnEvent(cause, "maybe input should specify polyphonic voices");
        }

        Stem.AddHead(_stem, gi.Grob);

        if (Stem.IsNormalStem(_stem) && Stem.DurationLog(_stem) > 2
            && !(_stem.GetObject(FlagSymbol) is Grob))
        {
            Item flag = MakeItem("Flag", _stem);
            flag.XParent = _stem;
            _stem.SetObject(FlagSymbol, flag);
            _maybeFlags.Add(flag);
        }
    }

    private void KillUnusedFlags()
    {
        foreach (Item maybeFlag in _maybeFlags)
        {
            // Q. Why don't we remove pointers to killed flags from the vector?
            if (maybeFlag.XParent?.GetObject(BeamSymbol) is Grob)
            {
                maybeFlag.Suicide();
            }
        }
    }

    /// <summary>Kills the flags that a beam made redundant.</summary>
    public override void FinalizeTranslation()
    {
        KillUnusedFlags();
    }

    /// <summary>
    /// Closes the timestep: applies pending beam counts to the stem and forgets it.
    /// </summary>
    public override void StopTranslationTimestep()
    {
        if (GetProperty(CurrentBarLineSymbol) is Grob)
        {
            KillUnusedFlags();
        }

        _tremolo = null;
        if (_stem != null)
        {
            /* FIXME: junk these properties.  */
            object prop = GetProperty(StemLeftBeamCountSymbol);
            if (SchemeConvert.IsNumber(prop))
            {
                Stem.SetBeaming(
                    _stem, SchemeConvert.ToInt(prop, "stemLeftBeamCount"), Direction.Negative);
                Context?.UnsetProperty(StemLeftBeamCountSymbol);
            }

            prop = GetProperty(StemRightBeamCountSymbol);
            if (SchemeConvert.IsNumber(prop))
            {
                Stem.SetBeaming(
                    _stem, SchemeConvert.ToInt(prop, "stemRightBeamCount"), Direction.Positive);
                Context?.UnsetProperty(StemRightBeamCountSymbol);
            }

            _stem = null;
        }

        _tremoloEvent = null;
    }

    private void ListenTremolo(StreamEvent ev)
        => StreamEvent.AssignEventOnce(ref _tremoloEvent, ev);

    /* Return the 2-log, rounded down — lily/include/misc.hh intlog2.  */
    private static int IntLog2(int d)
    {
        if (d <= 0)
        {
            Warn.Error("intlog2 with negative argument: " + d);
        }

        int i = 0;
        while (d != 1)
        {
            d /= 2;
            i++;
        }

        return i;
    }

    private static void WarnEvent(StreamEvent ev, string message)
    {
        if (ev?.Origin is Input origin)
        {
            origin.Warning(message);
        }
        else
        {
            Warn.Warning(message);
        }
    }
}
