/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1999--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/auto-beam-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port:
//   - derived_mark () is dropped, as everywhere else in this port
//   - beam_settings_ is an object rather than an SCM; the beam is still built from
//     the SNAPSHOT taken at begin_beam, not from the context's current overrides,
//     which is the whole reason upstream cannot use make_spanner here

/// <summary>
/// Generates beams based on measure characteristics and observed stems. Uses
/// <c>beatBase</c>, <c>beatStructure</c>, <c>beamExceptions</c>, <c>measureLength</c>
/// and <c>measurePosition</c> to decide when to start and stop a beam.
/// </summary>
public class AutoBeamEngraver : TemplateEngraverForBeams
{
    private static readonly Symbol BeamForbidEventSymbol = Symbol.Intern("beam-forbid-event");
    private static readonly Symbol BeamBreakEventSymbol = Symbol.Intern("beam-break-event");
    private static readonly Symbol BeamBreakPermissionSymbol
        = Symbol.Intern("beam-break-permission");
    private static readonly Symbol ForceSymbol = Symbol.Intern("force");
    private static readonly Symbol ForbidSymbol = Symbol.Intern("forbid");
    private static readonly Symbol AutoBeamCheckSymbol = Symbol.Intern("autoBeamCheck");
    private static readonly Symbol AutoBeamingSymbol = Symbol.Intern("autoBeaming");
    private static readonly Symbol SkipTypesettingSymbol = Symbol.Intern("skipTypesetting");
    private static readonly Symbol CurrentBarLineSymbol = Symbol.Intern("currentBarLine");
    private static readonly Symbol MeterScalingFactorSymbol
        = Symbol.Intern("meterScalingFactor");
    private static readonly Symbol DurationSymbol = Symbol.Intern("duration");
    private static readonly Symbol BeamSymbol = Symbol.Intern("Beam");
    private static readonly Symbol RhythmicEventSymbol = Symbol.Intern("rhythmic-event");
    private static readonly Symbol StemInterfaceSymbol = Symbol.Intern("stem-interface");
    private static readonly Symbol RestInterfaceSymbol = Symbol.Intern("rest-interface");
    private static readonly Symbol BeamInterfaceSymbol = Symbol.Intern("beam-interface");
    private static readonly Symbol BreathingSignInterfaceSymbol
        = Symbol.Intern("breathing-sign-interface");

    private StreamEvent _forbid;
    private StreamEvent _break;
    private Moment _measurePositionAtStartOfTimestep;
    private bool _forceEnd;
    private bool _breathingSign;
    private bool _forbidAutoEnding;
    private bool _consideredBar;

    /*
      shortest_dur_ is the shortest note in the beam.
    */
    private Rational _shortestDur = new Rational(1, 4);

    // This engraver is designed to operate in Voice context, so we expect only
    // one stem per timestep.
    private Item _currentStem;
    private List<Item> _stems = new List<Item>();

    private int _processAcknowledgedCount;

    /*
      Projected ending of the  beam we're working on.
    */
    private Moment _extendMom = new Moment(-1);

    /*
      Handle on the starting staff keeps it alive until beam is complete
    */
    private readonly ContextHandle _beamStartContext = new ContextHandle();

    // global time when beam started
    private Moment _beamStartMoment = Moment.Infinity;

    // We act as if beam were created, and start a grouping anyway.
    private object _beamSettings = Nil.Instance;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public AutoBeamEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Auto_beam_engraver";

    /// <summary>Starts listening for beam-forbid and beam-break events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(BeamForbidEventSymbol, ListenBeamForbid);
        ListenTo(BeamBreakEventSymbol, ListenBeamBreak);
    }

    /// <summary>The measure position, scaled to the beat structure's period.</summary>
    /// <returns>The position.</returns>
    protected Moment BeamingMeasurePosition()
        => MeasureTiming.ScaledMeasurePosition(Context, BeamingOptionsInForce.Period);

    /// <summary>Remembers the measure position this timestep began at.</summary>
    public override void StartTranslationTimestep()
        => _measurePositionAtStartOfTimestep = BeamingMeasurePosition();

    /// <summary>Ends a beam that would otherwise run over a skip.</summary>
    public override void ProcessMusic()
    {
        Moment now = NowMoment;

        /*
          don't beam over skips
        */
        if (Busy() && _extendMom < now)
        {
            EndBeam();
        }
    }

    private void ListenBeamForbid(StreamEvent ev)
    {
        StreamEvent.AssignEventOnce(ref _forbid, ev);
        _forceEnd = true;
    }

    private void ListenBeamBreak(StreamEvent ev)
    {
        StreamEvent.AssignEventOnce(ref _break, ev);

        object permission = ev.GetProperty(BeamBreakPermissionSymbol);

        if (permission is Symbol permissionSymbol)
        {
            if (ReferenceEquals(permissionSymbol, ForceSymbol))
            {
                _forceEnd = true;
                _forbidAutoEnding = false;
            }
            else if (ReferenceEquals(permissionSymbol, ForbidSymbol))
            {
                _forceEnd = false;
                _forbidAutoEnding = true;
            }
            else
            {
                Warn.Warning(
                    "unknown beam-break-permission type: " + permissionSymbol.Name);
            }
        }
    }

    /// <summary>Asks <c>autoBeamCheck</c> whether a beam may start or stop here.</summary>
    /// <param name="dir">Start or stop.</param>
    /// <param name="testMom">The moment to test.</param>
    /// <param name="dur">The duration in question.</param>
    /// <returns><see langword="true"/> when the check says yes.</returns>
    protected virtual bool TestMoment(Direction dir, Moment testMom, Rational dur)
    {
        // TODO: Scale test_mom to accumulate tuplet ratios
        object check = GetProperty(AutoBeamCheckSymbol);
        if (!SchemeUtilities.IsProcedure(check))
        {
            return false;
        }

        object result = SchemeUtilities.CallCallback(
            check, Context, (long)dir.Value, testMom, new Moment(dur));
        return SchemeUtilities.IsSchemeTrue(result);
    }

    private bool Busy() => _beamStartMoment < Moment.Infinity;

    private void ConsiderBegin(Rational dur)
    {
        if (!Busy() && _forbid == null
            && SchemeUtilities.ToBool(GetProperty(AutoBeamingSymbol))
            && TestMoment(Direction.Negative, BeamingMeasurePosition(), dur))
        {
            BeginBeamHere();
        }
    }

    private void ConsiderEnd(Rational dur)
    {
        // Allow an autobeam to end when necessary: don't check for autoBeaming.
        //
        // measurePosition might have changed, e.g., at a transition between volta
        // alternatives.  Base this decision on the previous value.
        if (!_forbidAutoEnding && Busy()
            && TestMoment(Direction.Positive, _measurePositionAtStartOfTimestep, dur))
        {
            EndBeam();
        }
    }

    private Spanner CreateBeam()
    {
        if (SchemeUtilities.ToBool(GetProperty(SkipTypesettingSymbol)))
        {
            return null;
        }

        foreach (Item stem in _stems)
        {
            if (Stem.GetBeam(stem) != null)
            {
                return null;
            }
        }

        /*
          Can't use make_spanner () because we have to use
          beam_settings_.
        */
        Spanner beam = new Spanner(_beamSettings);

        foreach (Item stem in _stems)
        {
            Beam.AddStem(beam, stem);
        }

        GrobInfo i = MakeGrobInfo(beam, _stems[0]);
        AnnounceGrobLocallyOnly(i);
        if (_beamStartContext.Context != null)
        {
            AnnounceGrob(i, _beamStartContext.Context);
        }

        return beam;
    }

    private void BeginBeamHere()
    {
        if (Busy() || BeamPattern != null)
        {
            Warn.ProgrammingError("already have autobeam");
            return;
        }

        _stems.Clear();

        _beamStartContext.Set(Context?.Parent);
        _beamStartMoment = NowMoment;
        BeginBeam();
        _beamSettings = new GrobPropertyInfo(Context, BeamSymbol).Updated();
    }

    private void JunkBeam()
    {
        if (!Busy())
        {
            return;
        }

        _beamStartContext.Reset();
        _beamStartMoment = Moment.Infinity;
        _stems.Clear();
        BeamPattern = null;
        _beamSettings = Nil.Instance;

        _shortestDur = new Rational(1, 4);
    }

    /// <summary>Whether two moments are on the same side of the grace boundary.</summary>
    /// <param name="start">The beam's start moment.</param>
    /// <param name="now">The current moment.</param>
    /// <returns><see langword="true"/> when both are grace, or both are not.</returns>
    protected virtual bool IsSameGraceState(Moment start, Moment now)
        => start.GracePart.IsNonZero == now.GracePart.IsNonZero;

    private void EndBeam()
    {
        if (_stems.Count < 2)
        {
            JunkBeam();
        }
        else
        {
            FinishedBeam = CreateBeam();

            if (FinishedBeam != null)
            {
                GrobInfo i = MakeGrobInfo(FinishedBeam, Nil.Instance);
                AnnounceEndGrobLocallyOnly(i);
                if (_beamStartContext.Context != null)
                {
                    AnnounceEndGrob(i, _beamStartContext.Context);
                }

                FinishedBeamPattern = BeamPattern;
                FinishedBeamingOptions = BeamingOptionsInForce;
            }

            _beamStartMoment = Moment.Infinity;
            _stems.Clear();
            BeamPattern = null;
            _beamSettings = Nil.Instance;
        }

        _beamStartContext.Reset();
        _shortestDur = new Rational(1, 4);
    }

    /// <summary>Closes the beam's right bound, then typesets it.</summary>
    protected override void TypesetBeam()
    {
        if (FinishedBeam != null)
        {
            if (FinishedBeam.GetBound(Direction.Positive) == null)
            {
                FinishedBeam.SetBound(
                    Direction.Positive, FinishedBeam.GetBound(Direction.Negative));
            }

            base.TypesetBeam();
        }
    }

    /// <summary>Typesets any finished beam and clears the timestep's flags.</summary>
    public override void StopTranslationTimestep()
    {
        TypesetBeam();
        _processAcknowledgedCount = 0;
        _forbid = null;
        _break = null;
        _consideredBar = false;
        _breathingSign = false;
        _forbidAutoEnding = false;

        /*
          Normally, force_end_ gets cleared in the process_acknowledged stage.
          But since that stage might be skipped if skipTypesetting = #t,
          we reset it here as well to be on the safe side.
        */
        _forceEnd = false;
    }

    /// <summary>Typesets any finished beam and junks an unfinished one.</summary>
    public override void FinalizeTranslation()
    {
        base.FinalizeTranslation();

        /* finished beams may be typeset */
        TypesetBeam();

        /* but unfinished may need another announce/acknowledge pass */
        if (Busy())
        {
            JunkBeam();
        }
    }

    /// <summary>Notes stems, and the grobs that force a beam to end.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob.HasInterface(StemInterfaceSymbol))
        {
            _currentStem = info.Grob as Item;
        }

        if (info.Grob.HasInterface(BeamInterfaceSymbol))
        {
            _forceEnd = true;
        }

        if (info.Grob.HasInterface(BreathingSignInterfaceSymbol))
        {
            _breathingSign = true;
        }

        if (info.Grob.HasInterface(RestInterfaceSymbol))
        {
            _forceEnd = true;
        }
    }

    private void HandleCurrentStem(Item stem)
    {
        StreamEvent ev = stem.UltimateEventCause();
        if (ev == null || !ev.IsInEventClass(RhythmicEventSymbol))
        {
            Warn.ProgrammingError("stem must have rhythmic structure");
            return;
        }

        /*
          Don't (start) auto-beam over empty stems (skips or rests) or stems
          that already have beam
        */
        if (Stem.HeadCount(stem) == 0 || Stem.GetBeam(stem) != null)
        {
            if (Busy())
            {
                EndBeam();
            }

            return;
        }

        Duration stemDuration = ev.GetProperty(DurationSymbol) is Duration d
            ? d
            : new Duration(0, 0);

        Rational meterScalingFactor = Epg8Support.ToRational(
            GetProperty(MeterScalingFactorSymbol), Rational.One);
        stemDuration = stemDuration.Compressed(Rational.One / meterScalingFactor);

        if (stemDuration.DurationLog <= 2)
        {
            if (Busy())
            {
                EndBeam();
            }

            return;
        }

        /*
          ignore interspersed grace notes.
        */
        Moment now = NowMoment;
        if (!IsSameGraceState(_beamStartMoment, now))
        {
            return;
        }

        Rational dur = stemDuration.ToWholeNotes();
        bool recheckNeeded = false;

        if (dur < _shortestDur)
        {
            /* new shortest moment, so store it and set recheck_needed */
            _shortestDur = dur;
            recheckNeeded = true;
        }

        /* end should be based on shortest_dur_, begin should be
           based on current duration  */
        ConsiderEnd(_shortestDur);
        ConsiderBegin(dur);

        if (!Busy())
        {
            return;
        }

        LastAddedMoment = now;
        AddStem(stem, stemDuration);

        _stems.Add(stem);
        _extendMom = (_extendMom > now ? _extendMom : now) + GetEventLength(ev, now);
        if (recheckNeeded)
        {
            RecheckBeam();
        }
    }

    private void RecheckBeam()
    {
        /*
          Recheck the beam after the shortest duration has changed
          If shorter duration has created a new break, typeset the
          first part of the beam and reset the current beam to just
          the last part of the beam
        */

        for (int i = 0; (i + 1) < _stems.Count; /*in body*/)
        {
            bool foundEnd = TestMoment(
                Direction.Positive,
                new Moment(BeamPattern.EndMoment(i) - BeamPattern.StartMoment(0)
                           + BeamPattern.MeasureOffset),
                _shortestDur);
            if (!foundEnd)
            {
                i++;
            }
            else
            {
                /*
                  Save the current beam settings and shortest_dur_
                  Necessary because end_beam destroys them
                */
                Rational savedShortestDur = _shortestDur;
                object savedBeamSettings = _beamSettings;

                /* Eliminate (and save) the items no longer part of the first beam */

                BeamingPattern newGrouping
                    = BeamPattern.SplitPattern(i, BeamingOptionsInForce.Period);
                List<Item> newStems = _stems.GetRange(i + 1, _stems.Count - (i + 1));
                _stems.RemoveRange(i + 1, _stems.Count - (i + 1));

                EndBeam();
                TypesetBeam();

                /* now recreate the unbeamed data structures */
                _stems = newStems;
                BeamPattern = newGrouping;
                _shortestDur = savedShortestDur;
                _beamSettings = savedBeamSettings;
                _beamStartContext.Set(Context?.Parent);
                _beamStartMoment = NowMoment;

                i = 0;
            }
        }
    }

    /// <summary>Decides, after every announcement pass, whether the beam continues.</summary>
    public override void ProcessAcknowledged()
    {
        if (_breathingSign && !_forbidAutoEnding)
        {
            _forceEnd = true;

            // avoid breathing_sign_ staying true, thus switching on
            // force_end_ again during repeated calls to
            // process_acknowledged ()
            _breathingSign = false;
        }

        // This engraver can't observe bar lines with acknowledge_bar_line ()
        // because the Bar_engraver operates in Staff context.
        // process_acknowledged () can be called more than once, but
        // currentBarLine won't change.
        if (!_consideredBar)
        {
            _consideredBar = true;

            if (!_forceEnd && !_forbidAutoEnding && Busy())
            {
                if (GetProperty(CurrentBarLineSymbol) is Grob)
                {
                    _forceEnd = true;
                }
            }
        }

        if (_forceEnd)
        {
            _forceEnd = false;

            if (Busy())
            {
                EndBeam();
            }
        }

        if (_currentStem != null)
        {
            HandleCurrentStem(_currentStem);
            _currentStem = null;
        }

        Moment now = NowMoment;
        if (_extendMom > now)
        {
            return;
        }

        if (Busy())
        {
            if (_processAcknowledgedCount == 0)
            {
                ConsiderEnd(_shortestDur);
            }
            else if (_processAcknowledgedCount > 1)
            {
                if ((_extendMom < now)
                    || ((_extendMom == now) && (LastAddedMoment != now)))
                {
                    EndBeam();
                }
                else if (_stems.Count == 0)
                {
                    JunkBeam();
                }
            }
        }

        _processAcknowledgedCount++;
    }
}

/// <summary>
/// Generates one autobeam group across an entire grace phrase. As usual, any manual
/// beaming or <c>\noBeam</c> blocks autobeaming, just like setting
/// <c>autoBeaming</c> to <c>##f</c>.
/// </summary>
public sealed class GraceAutoBeamEngraver : AutoBeamEngraver
{
    // Full starting time of last grace group.  grace_part_ is zero ->
    // test_moment is false, last_grace_position_ not considered.
    private Moment _lastGraceStart = new Moment(-Rational.Infinity);

    // Measure position of same
    private Moment _lastGracePosition;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public GraceAutoBeamEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Grace_auto_beam_engraver";

    /// <summary>
    /// This is for ignoring interspersed grace notes in main note beaming. We never
    /// want to ignore something inside of grace note beaming, so this is always true.
    /// </summary>
    /// <param name="start">The beam's start moment.</param>
    /// <param name="now">The current moment.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    protected override bool IsSameGraceState(Moment start, Moment now) => true;

    /// <summary>Tracks the grace group's start, then does the ordinary work.</summary>
    public override void ProcessMusic()
    {
        Moment now = NowMoment;

        // Update last_grace_start_ and last_grace_position_ only when the
        // main time advances.
        if (now.MainPart > _lastGraceStart.MainPart)
        {
            _lastGraceStart = now;
            _lastGracePosition = BeamingMeasurePosition();
        }

        base.ProcessMusic();
    }

    /// <summary>Beams only within one grace group.</summary>
    /// <param name="dir">Start or stop.</param>
    /// <param name="testMom">The moment to test.</param>
    /// <param name="dur">The duration in question, unused here.</param>
    /// <returns><see langword="true"/> when the check says yes.</returns>
    protected override bool TestMoment(Direction dir, Moment testMom, Rational dur)
    {
        // If no grace group started this main moment, we have no business
        // beaming.  Same if we have left the original main time step.
        if (!_lastGraceStart.GracePart.IsNonZero
            || _lastGracePosition.MainPart != testMom.MainPart)
        {
            return false;
        }

        // Autobeam start only when at the start of the grace group.
        if (dir == Direction.Negative)
        {
            return _lastGracePosition == testMom;
        }

        // Autobeam end only when the grace part is finished.
        return !testMom.GracePart.IsNonZero;
    }
}
