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

using System;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/template-engraver-for-beams.cc, lily/include/template-engraver-for-beams.hh, lily/beam-engraver.cc, lily/chord-tremolo-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port:
//   - derived_mark () is dropped, as everywhere else in this port
//   - Moment is a readonly struct here, so begin_beam's "main_part_ = grace_part_"
//     builds a new Moment rather than assigning through

/// <summary>
/// The state every beam-making engraver shares: the beam being built, the pattern its
/// stems are accumulating into, and the options in force when it began.
/// </summary>
public abstract class TemplateEngraverForBeams : Engraver
{
    private static readonly Symbol MeasurePositionSymbol = Symbol.Intern("measurePosition");
    private static readonly Symbol CurrentTupletDescriptionSymbol
        = Symbol.Intern("currentTupletDescription");

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    protected TemplateEngraverForBeams(Context context)
        : base(context)
    {
    }

    /// <summary>The beam that has ended and is waiting to be typeset.</summary>
    protected Spanner FinishedBeam { get; set; }

    /// <summary>The pattern the beam under construction is accumulating.</summary>
    protected BeamingPattern BeamPattern { get; set; }

    /// <summary>The pattern belonging to <see cref="FinishedBeam"/>.</summary>
    protected BeamingPattern FinishedBeamPattern { get; set; }

    /// <summary>The moment of the most recently added stem.</summary>
    protected Moment LastAddedMoment { get; set; }

    /// <summary>The beaming options in force for the beam under construction.</summary>
    protected BeamingOptions BeamingOptionsInForce { get; set; } = new BeamingOptions();

    /// <summary>The beaming options belonging to <see cref="FinishedBeam"/>.</summary>
    protected BeamingOptions FinishedBeamingOptions { get; set; } = new BeamingOptions();

    /// <summary>Applies the accumulated pattern to the finished beam's stems.</summary>
    protected virtual void TypesetBeam()
    {
        if (FinishedBeam != null)
        {
            FinishedBeamPattern.Beamify(FinishedBeamingOptions);

            Beam.SetBeaming(FinishedBeam, FinishedBeamPattern);
            FinishedBeam = null;

            FinishedBeamPattern = null;
        }
    }

    /// <summary>Starts a new beaming pattern at the current measure position.</summary>
    protected void BeginBeam()
    {
        Moment beamStartPosition
            = GetProperty(MeasurePositionSymbol) is Moment m ? m : Moment.Zero;

        BeamingOptionsInForce = new BeamingOptions(Context);
        if (beamStartPosition.GracePart.IsNonZero)
        {
            beamStartPosition
                = new Moment(beamStartPosition.GracePart, beamStartPosition.GracePart);
        }

        BeamPattern = new BeamingPattern(
            MeasureTiming.MeasurePosition(
                Context, beamStartPosition, BeamingOptionsInForce.Period).MainPart);
    }

    /// <summary>Adds a stem to the beaming pattern.</summary>
    /// <param name="stem">The stem.</param>
    /// <param name="dur">Its duration.</param>
    protected void AddStem(Item stem, Duration dur)
    {
        BeamPattern.AddStem(
            LastAddedMoment.GracePart.IsNonZero
                ? LastAddedMoment.GracePart
                : LastAddedMoment.MainPart,
            Stem.IsInvisible(stem),
            dur,
            Context?.GetProperty(CurrentTupletDescriptionSymbol) as TupletDescription);
    }
}

/// <summary>
/// Handles <c>Beam</c> events by engraving beams. If omitted, then notes are printed
/// with flags instead of beams.
/// </summary>
public class BeamEngraver : TemplateEngraverForBeams
{
    private static readonly Symbol BeamEventSymbol = Symbol.Intern("beam-event");
    private static readonly Symbol SpanDirectionSymbol = Symbol.Intern("span-direction");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol AutoBeamingSymbol = Symbol.Intern("autoBeaming");
    private static readonly Symbol BeamMelismaBusySymbol = Symbol.Intern("beamMelismaBusy");
    private static readonly Symbol StencilSymbol = Symbol.Intern("stencil");
    private static readonly Symbol DurationSymbol = Symbol.Intern("duration");
    private static readonly Symbol DurationLogSymbol = Symbol.Intern("duration-log");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol RhythmicEventSymbol = Symbol.Intern("rhythmic-event");
    private static readonly Symbol StemInterfaceSymbol = Symbol.Intern("stem-interface");
    private static readonly Symbol RestInterfaceSymbol = Symbol.Intern("rest-interface");
    private static readonly Symbol RestCollisionCallbackSymbol
        = Symbol.Intern("ly:beam::rest-collision-callback");
    private static readonly Symbol PureRestCollisionCallbackSymbol
        = Symbol.Intern("ly:beam::pure-rest-collision-callback");

    private StreamEvent _startEv;
    private StreamEvent _prevStartEv;
    private StreamEvent _stopEv;

    /// <summary>The beam under construction.</summary>
    protected Spanner Beam_;

    private Direction _forcedDirection = Direction.Center;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public BeamEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Beam_engraver";

    /// <summary>Starts listening for beam events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(BeamEventSymbol, ListenBeam);
    }

    /*
      Hmm. this isn't necessary, since grace beams and normal beams are
      always nested.
    */

    /// <summary>Whether a beam may START at the current moment.</summary>
    /// <returns><see langword="true"/> when it may.</returns>
    protected virtual bool ValidStartPoint() => !NowMoment.GracePart.IsNonZero;

    /// <summary>Whether a beam may END at the current moment.</summary>
    /// <returns><see langword="true"/> when it may.</returns>
    protected virtual bool ValidEndPoint() => ValidStartPoint();

    private void ListenBeam(StreamEvent ev)
    {
        Direction d = Stem.FromScmDirection(ev.GetProperty(SpanDirectionSymbol));

        if (d == Direction.Negative && ValidStartPoint())
        {
            StreamEvent.AssignEventOnce(ref _startEv, ev);

            Direction updown = Stem.FromScmDirection(ev.GetProperty(DirectionSymbol));
            if (updown.IsNonZero)
            {
                _forcedDirection = updown;
            }
        }
        else if (d == Direction.Positive && ValidEndPoint())
        {
            StreamEvent.AssignEventOnce(ref _stopEv, ev);
        }
    }

    private void SetMelisma(bool ml)
    {
        object b = GetProperty(AutoBeamingSymbol);
        if (!SchemeUtilities.ToBool(b))
        {
            Context.SetProperty(BeamMelismaBusySymbol, ml);
        }
    }

    /// <summary>Creates the beam when a start event arrived, and closes a finished one.</summary>
    public override void ProcessMusic()
    {
        if (_startEv != null)
        {
            if (Beam_ != null)
            {
                Epg8Support.EventWarning(_startEv, "already have a beam");
                return;
            }

            SetMelisma(true);
            _prevStartEv = _startEv;
            Beam_ = MakeSpanner("Beam", _startEv);

            BeginBeam();
        }

        TypesetBeam();
        if (_stopEv != null && Beam_ != null)
        {
            AnnounceEndGrob(Beam_, _stopEv);
        }
    }

    /// <summary>Applies the forced direction, then typesets the finished beam.</summary>
    protected override void TypesetBeam()
    {
        if (FinishedBeam != null)
        {
            Grob stem = FinishedBeam.GetBound(Direction.Positive);
            if (stem == null)
            {
                stem = FinishedBeam.GetBound(Direction.Negative);
                if (stem != null)
                {
                    FinishedBeam.SetBound(Direction.Positive, stem);
                }
            }

            if (stem != null && _forcedDirection.IsNonZero)
            {
                Stem.SetGrobDirection(stem, _forcedDirection);
            }

            _forcedDirection = Direction.Center;

            base.TypesetBeam();
        }
    }

    /// <summary>Clears the start event and keeps the melisma alive.</summary>
    public override void StartTranslationTimestep()
    {
        _startEv = null;

        if (Beam_ != null)
        {
            SetMelisma(true);
        }
    }

    /// <summary>Closes the beam when a stop event arrived.</summary>
    public override void StopTranslationTimestep()
    {
        if (_stopEv != null)
        {
            FinishedBeam = Beam_;
            FinishedBeamPattern = BeamPattern;
            BeamPattern = null;
            FinishedBeamingOptions = BeamingOptionsInForce;

            _stopEv = null;
            Beam_ = null;
            TypesetBeam();
            SetMelisma(false);
        }
    }

    /// <summary>Typesets any finished beam and kills an unterminated one.</summary>
    public override void FinalizeTranslation()
    {
        base.FinalizeTranslation();
        TypesetBeam();
        if (Beam_ != null)
        {
            Epg8Support.EventWarning(_prevStartEv, "unterminated beam");

            /*
              we don't typeset it, (we used to, but it was commented
              out. Reason unknown) */
            Beam_.Suicide();
            BeamPattern = null;
        }
    }

    /// <summary>Claims stems for the beam and pushes rests clear of it.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob.HasInterface(StemInterfaceSymbol))
        {
            AcknowledgeStem(info);
        }

        if (info.Grob.HasInterface(RestInterfaceSymbol))
        {
            AcknowledgeRest(info);
        }
    }

    private void AcknowledgeRest(GrobInfo info)
    {
        if (Beam_ != null
            && !SchemeConvert.IsNumber(info.Grob.GetPropertyData(StaffPositionSymbol)))
        {
            object unpure = LilyPondScheme.LookupProcedure(RestCollisionCallbackSymbol);
            object pure = LilyPondScheme.LookupProcedure(PureRestCollisionCallbackSymbol);
            if (unpure == null || pure == null)
            {
                Warn.ProgrammingError(
                    "ly:beam::rest-collision-callback is not available");
                return;
            }

            GrobClosure.ChainOffsetCallback(
                info.Grob, new UnpurePureContainer(unpure, pure), Axis.Y);
        }
    }

    private void AcknowledgeStem(GrobInfo info)
    {
        if (Beam_ == null)
        {
            return;
        }

        if (!ValidStartPoint())
        {
            return;
        }

        // It's suboptimal that we don't support callbacks returning ##f,
        // but this makes beams have no effect on "stems" reliably in
        // TabStaff when \tabFullNotation is switched off: the real stencil
        // callback for beams is called quite late in the process, and we
        // don't want to trigger it early.
        if (Beam_.GetPropertyData(StencilSymbol) is bool stencilFlag && !stencilFlag)
        {
            return;
        }

        Item stem = info.Grob as Item;
        if (stem == null || Stem.GetBeam(stem) != null)
        {
            return;
        }

        StreamEvent ev = info.UltimateEventCause;
        if (ev == null || !ev.IsInEventClass(RhythmicEventSymbol))
        {
            info.Grob.Warning("stem must have Rhythmic structure");
            return;
        }

        if (!(ev.GetProperty(DurationSymbol) is Duration stemDuration))
        {
            return;
        }

        int durlog = stemDuration.DurationLog;
        if (durlog <= 2)
        {
            Epg8Support.EventWarning(ev, "stem does not fit in beam");
            Epg8Support.EventWarning(_prevStartEv, "beam was started here");

            /*
              don't return, since

              [r4 c8] can just as well be modern notation.
            */
        }

        if (_forcedDirection.IsNonZero)
        {
            Stem.SetGrobDirection(stem, _forcedDirection);
        }

        stem.SetProperty(DurationLogSymbol, (long)durlog);
        LastAddedMoment = NowMoment;
        AddStem(stem, stemDuration);
        Beam.AddStem(Beam_, stem);
    }
}

/// <summary>
/// Handles <c>Beam</c> events by engraving beams, only at grace points in time.
/// </summary>
public sealed class GraceBeamEngraver : BeamEngraver
{
    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public GraceBeamEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Grace_beam_engraver";

    /// <summary>Whether a beam may START at the current moment.</summary>
    /// <returns><see langword="true"/> only at grace moments.</returns>
    protected override bool ValidStartPoint() => NowMoment.GracePart.IsNonZero;

    /// <summary>Whether a beam may END at the current moment.</summary>
    /// <returns><see langword="true"/> when a grace beam is open.</returns>
    protected override bool ValidEndPoint() => Beam_ != null && ValidStartPoint();
}

/**

This acknowledges repeated music with "tremolo" style.  It typesets
a beam.

TODO:

- perhaps use engraver this to steer other engravers? That would
create dependencies between engravers, which is bad.

- create dots if appropriate.

- create TremoloBeam iso Beam?
*/

/// <summary>Generates beams for tremolo repeats.</summary>
public sealed class ChordTremoloEngraver : Engraver
{
    private static readonly Symbol TremoloSpanEventSymbol = Symbol.Intern("tremolo-span-event");
    private static readonly Symbol SpanDirectionSymbol = Symbol.Intern("span-direction");
    private static readonly Symbol TremoloTypeSymbol = Symbol.Intern("tremolo-type");
    private static readonly Symbol RepeatCountSymbol = Symbol.Intern("repeat-count");
    private static readonly Symbol GapCountSymbol = Symbol.Intern("gap-count");
    private static readonly Symbol RhythmicEventSymbol = Symbol.Intern("rhythmic-event");
    private static readonly Symbol StemInterfaceSymbol = Symbol.Intern("stem-interface");

    private StreamEvent _repeat;
    private Spanner _beam;

    // Store the pointer to the previous stem, so we can create a beam if
    // necessary and end the spanner
    private Grob _previousStem;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public ChordTremoloEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Chord_tremolo_engraver";

    /// <summary>Starts listening for tremolo span events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(TremoloSpanEventSymbol, ListenTremoloSpan);
    }

    private void ListenTremoloSpan(StreamEvent ev)
    {
        Direction spanDir = Stem.FromScmDirection(ev.GetProperty(SpanDirectionSymbol));
        if (spanDir == Direction.Negative)
        {
            StreamEvent.AssignEventOnce(ref _repeat, ev);
        }
        else if (spanDir == Direction.Positive)
        {
            if (_repeat == null)
            {
                Epg8Support.EventWarning(ev, "No tremolo to end");
            }

            _repeat = null;
            _beam = null;
            _previousStem = null;
        }
    }

    /// <summary>Creates the tremolo beam once a span has started.</summary>
    public override void ProcessMusic()
    {
        if (_repeat != null && _beam == null)
        {
            _beam = MakeSpanner("Beam", _repeat);
        }
    }

    /// <summary>Kills an unterminated chord tremolo.</summary>
    public override void FinalizeTranslation()
    {
        base.FinalizeTranslation();
        if (_beam != null)
        {
            Epg8Support.EventWarning(_repeat, "unterminated chord tremolo");
            AnnounceEndGrob(_beam, Nil.Instance);
            _beam.Suicide();
        }
    }

    /// <summary>Beams each pair of tremolo stems together.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!info.Grob.HasInterface(StemInterfaceSymbol))
        {
            return;
        }

        if (_beam != null)
        {
            int tremoloType
                = (int)ToLongOr(_repeat?.GetProperty(TremoloTypeSymbol), 1);
            int flags = Math.Max(0, Misc.IntLog2(tremoloType) - 2);
            int repeatCount
                = (int)ToLongOr(_repeat?.GetProperty(RepeatCountSymbol), 1);
            int gapCount = Math.Min(flags, Misc.IntLog2(repeatCount) + 1);

            Grob s = info.Grob;
            if (_previousStem != null)
            {
                // FIXME: We know that the beam has ended only in listen_tremolo_span
                //        but then it is too late for Spanner_break_forbid_engraver
                //        to allow a line break... So, as a nasty hack, announce the
                //        spanner's end after each note except the first. The only
                //        "drawback" is that for multi-note tremolos a break would
                //        theoretically be allowed after the second note (but since
                //        that note is typically not at a barline, I don't think
                //        anyone will ever notice!)
                AnnounceEndGrob(_beam, _previousStem);

                // Create the whole beam between previous and current note
                Stem.SetBeaming(_previousStem, flags, Direction.Positive);
                Stem.SetBeaming(s, flags, Direction.Negative);
            }

            if (Stem.DurationLog(s) != 1)
            {
                _beam.SetProperty(GapCountSymbol, (long)gapCount);
            }

            StreamEvent cause = info.UltimateEventCause;
            if (cause != null && cause.IsInEventClass(RhythmicEventSymbol))
            {
                Beam.AddStem(_beam, s);
            }
            else
            {
                s.Warning("stem must have Rhythmic structure");
            }

            // Store current grob, so we can possibly end the spanner here (and
            // reset the beam direction to RIGHT)
            _previousStem = s;
        }
    }

    private static long ToLongOr(object value, long fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToLong(value, "chord tremolo property")
            : fallback;
}
