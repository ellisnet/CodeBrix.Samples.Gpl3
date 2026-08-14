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
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/timing-translator.cc, lily/include/timing-translator.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The metric heartbeat: maintains <c>measurePosition</c>, <c>measureLength</c>,
/// <c>currentBarNumber</c>, <c>internalBarNumber</c> and <c>measureStartNow</c>, and
/// adds the alias <c>Timing</c> to its containing context.
/// <para>
/// Responsible for synchronizing timing information from staves. Normally in
/// <c>Score</c>. In order to create polyrhythmic music, this engraver should be
/// removed from <c>Score</c> and placed in <c>Staff</c>.
/// </para>
/// <para>
/// Every value it maintains is read by other engravers at every timestep, which is why
/// it also asks the global context to STOP at each measure boundary — a bar line has
/// to exist as a moment before anything can be engraved at it.
/// </para>
/// </summary>
public class TimingTranslator : Translator
{
    private static readonly Symbol TimingSymbol = Symbol.Intern("Timing");
    private static readonly Symbol VoltaDepthSymbol = Symbol.Intern("volta-depth");
    private static readonly Symbol WhichBarSymbol = Symbol.Intern("whichBar");
    private static readonly Symbol BarTypeSymbol = Symbol.Intern("bar-type");
    private static readonly Symbol MeasurePositionSymbol = Symbol.Intern("measurePosition");
    private static readonly Symbol MeasureStartNowSymbol = Symbol.Intern("measureStartNow");
    private static readonly Symbol MeasureLengthSymbol = Symbol.Intern("measureLength");
    private static readonly Symbol DeprecatedBarCheckSynchronizeSymbol
        = Symbol.Intern("deprecatedBarCheckSynchronize");

    private static readonly Symbol IgnoreBarChecksSymbol = Symbol.Intern("ignoreBarChecks");
    private static readonly Symbol DurationSymbol = Symbol.Intern("duration");
    private static readonly Symbol TimeSignatureSymbol = Symbol.Intern("timeSignature");
    private static readonly Symbol TimeSignaturePropertySymbol = Symbol.Intern("time-signature");
    private static readonly Symbol PartialBusySymbol = Symbol.Intern("partialBusy");
    private static readonly Symbol FineFoldedSymbol = Symbol.Intern("fine-folded");
    private static readonly Symbol CurrentBarNumberSymbol = Symbol.Intern("currentBarNumber");
    private static readonly Symbol InternalBarNumberSymbol = Symbol.Intern("internalBarNumber");
    private static readonly Symbol AlternativeDirSymbol = Symbol.Intern("alternative-dir");
    private static readonly Symbol AlternativeNumberSymbol = Symbol.Intern("alternativeNumber");
    private static readonly Symbol AlternativeNumberingStyleSymbol
        = Symbol.Intern("alternativeNumberingStyle");

    private static readonly Symbol NumbersSymbol = Symbol.Intern("numbers");
    private static readonly Symbol NumbersWithLettersSymbol = Symbol.Intern("numbers-with-letters");
    private static readonly Symbol VoltaNumbersSymbol = Symbol.Intern("volta-numbers");
    private static readonly Symbol TimingPropertySymbol = Symbol.Intern("timing");
    private static readonly Symbol SkipBarsSymbol = Symbol.Intern("skipBars");
    private static readonly Symbol TimeSignatureSettingsSymbol
        = Symbol.Intern("timeSignatureSettings");

    private static readonly Symbol BeamExceptionsSymbol = Symbol.Intern("beamExceptions");
    private static readonly Symbol BeatBaseSymbol = Symbol.Intern("beatBase");
    private static readonly Symbol BeatStructureSymbol = Symbol.Intern("beatStructure");
    private static readonly Symbol SubmeasureStructureSymbol = Symbol.Intern("submeasureStructure");
    private static readonly Symbol BeamHalfMeasureSymbol = Symbol.Intern("beamHalfMeasure");
    private static readonly Symbol AutoBeamingSymbol = Symbol.Intern("autoBeaming");

    private static readonly Symbol CalcMeasureLengthProcSymbol
        = Symbol.Intern("calc-measure-length");

    private static readonly Symbol DefaultTimeSignatureSettingsSymbol
        = Symbol.Intern("default-time-signature-settings");

    private static readonly Symbol BeamExceptionsProcSymbol = Symbol.Intern("beam-exceptions");
    private static readonly Symbol BeatBaseProcSymbol = Symbol.Intern("beat-base");
    private static readonly Symbol BeatStructureProcSymbol = Symbol.Intern("beat-structure");
    private static readonly Symbol CalcSubmeasureStructureProcSymbol
        = Symbol.Intern("calc-submeasure-structure");

    private Moment _measureStartMoment = Moment.Infinity;
    private bool _warnedForBarCheck;

    // alt... members pertain to bar numbering for repeat alternatives
    private StreamEvent _altEvent;
    private long _altStartingBarNumber;
    private long _altNumber;
    private bool _altResetEnabled;

    private StreamEvent _barCheckEvent;
    private StreamEvent _fineEvent;
    private StreamEvent _measureLengthChangeEvent;
    private StreamEvent _partialEvent;
    private readonly List<StreamEvent> _polymetricTimeSignatureEvents = new List<StreamEvent>();

    /// <summary>Initializes the translator in a context.</summary>
    /// <param name="context">The context this translator belongs to.</param>
    public TimingTranslator(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Timing_translator";

    /// <summary>
    /// Adds the <c>Timing</c> alias to the containing context and starts listening.
    /// <para>
    /// The listeners register here rather than in the constructor, the port-wide rule
    /// SpacingEngravers.cs records.
    /// </para>
    /// </summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();

        if (Context != null && !Context.IsAlias(TimingSymbol))
        {
            Context.AddAlias(TimingSymbol);
        }

        ListenTo("alternative-event", ListenAlternative);
        ListenTo("bar-event", ListenBar);
        ListenTo("bar-check-event", ListenBarCheck);
        ListenTo("fine-event", ListenFine);
        ListenTo("measure-length-change-event", ListenMeasureLengthChange);
        ListenTo("partial-event", ListenPartial);
        ListenTo("polymetric-time-signature-event", ListenPolymetricTimeSignature);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    private void ListenAlternative(StreamEvent ev)
    {
        // Use alternative bar numbers for the outermost volta brackets.
        long depth = TranslatorSchemeHelpers.ToLong(ev.GetProperty(VoltaDepthSymbol), 0);
        if (depth != 1)
        {
            return;
        }

        // It is common to have the same repeat structure in multiple voices, so we
        // ignore simultaneous events; but it might not be a bad thing to add some
        // consistency checks here if they could catch some kinds of user error.
        if (_altEvent != null)
        {
            return;
        }

        _altEvent = ev;
    }

    private void ListenBar(StreamEvent ev)
    {
        // To mimic the previous implementation, we always set whichBar.
        Context.SetProperty(WhichBarSymbol, ev.GetProperty(BarTypeSymbol));
    }

    private void ListenBarCheck(StreamEvent ev)
    {
        // Simultaneous bar checks are normal.  Since we're going to issue at most one
        // warning, we only need to handle one of the events.
        if (_barCheckEvent == null)
        {
            _barCheckEvent = ev;
        }

        // barCheckSynchronize is implemented here so that changes to timing
        // properties occur before any translator's pre_process_music () is called.
        Moment now = NowMoment;
        if (now.MainPart != _measureStartMoment.MainPart
            && TranslatorSchemeHelpers.ToBool(GetProperty(DeprecatedBarCheckSynchronizeSymbol)))
        {
            Moment mp = TranslatorSchemeHelpers.ToMoment(GetProperty(MeasurePositionSymbol), Moment.Zero);
            if (mp.MainPart.IsNonZero && !TranslatorSchemeHelpers.ToBool(GetProperty(IgnoreBarChecksSymbol)))
            {
                Context?.SetProperty(MeasurePositionSymbol, Moment.Zero);
                Context?.SetProperty(MeasureStartNowSymbol, true);
                // The old Bar_check_iterator used to warn regardless (once).
            }
        }
    }

    private void ListenFine(StreamEvent ev) => StreamEvent.AssignEventOnce(ref _fineEvent, ev);

    private void ListenMeasureLengthChange(StreamEvent ev)
    {
        if (ev.GetProperty(DurationSymbol) is Duration dur)
        {
            // We want to warn about inconsistent simultaneous commands, but
            // assign_event_once () would be too strict because we don't require
            // full log-dots-factor equality.  For example, it is fine if one event
            // has `2.` and another `2*3/2`.
            if (_measureLengthChangeEvent != null
                && _measureLengthChangeEvent.GetProperty(DurationSymbol) is Duration prevDur)
            {
                if (dur.ToWholeNotes() != prevDur.ToWholeNotes())
                {
                    StreamEvent.WarnReassignEvent(_measureLengthChangeEvent, ev);
                    return;
                }
            }

            _measureLengthChangeEvent = ev;

            // compute and set measureLength
            Moment mp = TranslatorSchemeHelpers.ToMoment(GetProperty(MeasurePositionSymbol), Moment.Zero);
            Rational mlen = mp.MainPart + dur.ToWholeNotes();
            Context?.SetProperty(MeasureLengthSymbol, SchemeConvert.FromRational(mlen));
        }
        else // set measureLength according to timeSignature
        {
            object tsig = GetProperty(TimeSignatureSymbol);
            object mlenScm = CallScheme(CalcMeasureLengthProcSymbol, tsig);

            // measureLength <= measurePosition is a problem because the measure
            // should have ended before this point.
            Moment mp = TranslatorSchemeHelpers.ToMoment(GetProperty(MeasurePositionSymbol), Moment.Zero);
            Rational mlen = TranslatorSchemeHelpers.ToRational(mlenScm, Rational.Zero);
            if (mlen <= mp.MainPart)
            {
                TranslatorSchemeHelpers.EventWarning(
                    ev,
                    "setting measureLength (" + mlen + ") ≤ measurePosition ("
                    + mp.MainPart + ")");
            }

            Context?.SetProperty(MeasureLengthSymbol, mlenScm);
        }
    }

    private void ListenPartial(StreamEvent ev)
    {
        if (!(ev.GetProperty(DurationSymbol) is Duration dur))
        {
            TranslatorSchemeHelpers.EventProgrammingError(ev, "invalid duration in \\partial");
            return;
        }

        // We want to warn about inconsistent simultaneous commands, but
        // assign_event_once () would be too strict because we don't require full
        // log-dots-factor equality.  For example, it is fine if one event has `2.`
        // and another `2*3/2`.
        if (_partialEvent == null)
        {
            _partialEvent = ev;
        }
        else if (_partialEvent.GetProperty(DurationSymbol) is Duration prevDur
                 && dur.ToWholeNotes() != prevDur.ToWholeNotes())
        {
            StreamEvent.WarnReassignEvent(_partialEvent, ev);
            return;
        }

        if (Context != null && Context.InitMoment < NowMoment) // in mid piece
        {
            Context.SetProperty(PartialBusySymbol, true);
        }
        else
        {
            // It would be consistent with the mid-piece behavior to refuse to adjust
            // measurePosition when measureLength is infinite, and it would help
            // defend consumers that might try to normalize a negative measurePosition
            // using measureLength as a modulus; however, even if we caught it here,
            // measureLength could be changed immediately afterward, so there is no
            // point in trying.  We will detect it and warn in pre_process_music().
            Moment old = TranslatorSchemeHelpers.ToMoment(GetProperty(MeasurePositionSymbol), Moment.Zero);
            Moment mp = new Moment(-dur.ToWholeNotes(), old.GracePart);
            Context?.SetProperty(MeasurePositionSymbol, mp);
            if (mp.IsNonZero)
            {
                Context.SetProperty(MeasureStartNowSymbol, Nil.Instance);
            }
        }
    }

    private void ListenPolymetricTimeSignature(StreamEvent ev)
        => _polymetricTimeSignatureEvents.Add(ev);

    /// <summary>
    /// Applies a mid-piece <c>\partial</c>, checks polymetric time signatures against
    /// the reference, and evaluates the pending bar check.
    /// </summary>
    public override void PreProcessMusic()
    {
        Moment now = NowMoment;

        if (_partialEvent != null)
        {
            Rational mlen = MeasureTiming.MeasureLength(Context);

            if (!mlen.IsFinite)
            {
                TranslatorSchemeHelpers.EventWarning(
                    _partialEvent,
                    "cannot calculate a finite measurePosition from an infinite"
                    + " measureLength");
                // We could try to handle this more gracefully by setting a
                // calculated measureLength here, but there might be side effects
                // that are hard to foresee, so we don't bother.
            }

            // Handle \partial in mid piece.  \partial at the start is handled in
            // listen_partial () so that measureStartNow can be updated accordingly
            // before any translator's pre_process_music () is called for the first
            // time.
            if (Context != null && Context.InitMoment < NowMoment)
            {
                // paranoia: listen_partial() should have rejected this event
                if (_partialEvent.GetProperty(DurationSymbol) is Duration dur)
                {
                    if (mlen.IsFinite)
                    {
                        Rational mp = mlen - dur.ToWholeNotes();

                        // TODO: If the new position is negative, warn and suggest
                        // setting measureLength?
                        Context.SetProperty(
                            MeasurePositionSymbol, new Moment(mp, now.GracePart));
                    }
                }
            }
        }

        // Check that the measure length of every \polymeter \time command matches the
        // reference measure length defined here in the Timing context.
        if (_polymetricTimeSignatureEvents.Count > 0)
        {
            // The measureLength property can be changed to create ad-hoc irregular
            // measures, so don't rely on it for this check.  Recompute the regular
            // measure length from the time signature.
            object refTsig = GetProperty(TimeSignatureSymbol);
            object refMlen = CallScheme(CalcMeasureLengthProcSymbol, refTsig);

            foreach (StreamEvent ev in _polymetricTimeSignatureEvents)
            {
                object polyTsig = ev.GetProperty(TimeSignaturePropertySymbol);
                if (!(polyTsig is Nil)) // ignore \polymetric \default
                {
                    object polyMlen = CallScheme(CalcMeasureLengthProcSymbol, polyTsig);

                    // If the polymetric time signature was scaled by \scaleDurations,
                    // scale the nominal measure length by the same factor.
                    Duration polyDur = ev.GetProperty(DurationSymbol) is Duration d
                        ? d
                        : new Duration(0, 0);
                    Rational factor = polyDur.Factor;
                    object scaled = SchemeConvert.FromRational(
                        TranslatorSchemeHelpers.ToRational(polyMlen, Rational.Zero) * factor);
                    if (!CodeBrix.LilyScheme.Primitives.CorePrimitives.SchemeEqual(
                            scaled, refMlen))
                    {
                        TranslatorSchemeHelpers.EventWarning(
                            ev, "conflicting measure length: " + Printer.Write(scaled));
                        Warn.Warning(
                            "measure length in Timing context: " + Printer.Write(refMlen));
                    }
                }
            }
        }

        // We can't assume that measurePosition and measureStartNow have the same
        // values as at the start of the timestep.  A partial-event or
        // Alternative_sequence_iterator might have changed them.
        //
        // TODO: Using alternativeRestores for timing properties is an imperfect
        // solution.  See comments in Alternative_sequence_iterator.  Consider making
        // Timing_translator responsible for timing properties.
        //
        // We don't pay attention to which alternative a bar check is in.  If the
        // previous alternative ends at a measure boundary or the next alternative
        // begins at a measure boundary, we accept it.  False negatives may result.
        // We could probably eliminate most false negatives if bar-check events told
        // their place in the repeat structure.  The question is whether the value to
        // the user is worth complicating the internals.
        //
        // We ignore differences in grace part.  Simultaneous sequences may include
        // different amounts of grace time, which makes iteration interesting (see
        // issue #34).  The measure starts with the earliest grace note, but we don't
        // want to fail later bar checks when the only difference is grace notes.
        bool barCheckOk = now.MainPart == _measureStartMoment.MainPart;
        if (TranslatorSchemeHelpers.ToBool(Context?.GetProperty(MeasureStartNowSymbol)))
        {
            _measureStartMoment = now;
            barCheckOk = true;
        }

        // One mistake offsets all subsequent bar checks by the same amount.  It is
        // noisy to warn in every measure until the next mistake or change in timing,
        // so we suppress further warnings.
        if (!barCheckOk && _barCheckEvent != null && !_warnedForBarCheck
            && !TranslatorSchemeHelpers.ToBool(GetProperty(IgnoreBarChecksSymbol)))
        {
            Moment mp = TranslatorSchemeHelpers.ToMoment(GetProperty(MeasurePositionSymbol), Moment.Zero);
            TranslatorSchemeHelpers.EventWarning(_barCheckEvent, "bar check failed at: " + mp);
            _warnedForBarCheck = true;
        }
    }

    /// <summary>Maintains the alternative numbering for repeat alternatives.</summary>
    public override void ProcessMusic()
    {
        if (_altEvent == null)
        {
            return;
        }

        // Which alternative is this?
        // LEFT: starting the first alternative
        // CENTER: starting a latter alternative
        // RIGHT: ending the last alternative
        long altDir = TranslatorSchemeHelpers.ToLong(_altEvent.GetProperty(AlternativeDirSymbol), 0);

        if (altDir == -1) // LEFT
        {
            // Use a consistent numbering algorithm for the full set of
            // alternatives by changing it only on the first alternative.
            object style = GetProperty(AlternativeNumberingStyleSymbol);
            _altResetEnabled = ReferenceEquals(style, NumbersSymbol)
                || ReferenceEquals(style, NumbersWithLettersSymbol);
            if (_altResetEnabled)
            {
                _altStartingBarNumber
                    = TranslatorSchemeHelpers.ToLong(GetProperty(CurrentBarNumberSymbol), 0);
            }
        }
        else if (altDir == 0) // CENTER
        {
            if (_altResetEnabled)
            {
                Context?.SetProperty(CurrentBarNumberSymbol, _altStartingBarNumber);
            }
        }

        // Upstream asserts on any other value and falls through to RIGHT.
        _altNumber = 0;
        if (altDir < 1) // alt_dir < RIGHT
        {
            object nums = _altEvent.GetProperty(VoltaNumbersSymbol);
            if (nums is Pair) // paranoia: there should always be a number
            {
                // Upstream applies guile's `min` over the list; the fold below is the
                // same arithmetic without a procedure lookup.
                long min = long.MaxValue;
                bool found = false;
                foreach (object num in Pair.ToList(nums))
                {
                    if (SchemeConvert.IsNumber(num))
                    {
                        long candidate = SchemeConvert.ToLong(num, "volta-numbers");
                        if (!found || candidate < min)
                        {
                            min = candidate;
                            found = true;
                        }
                    }
                }

                if (found)
                {
                    _altNumber = min;
                }
            }

            Context?.SetProperty(AlternativeNumberSymbol, _altNumber);
        }
        else
        {
            Context.SetProperty(AlternativeNumberSymbol, Nil.Instance);
        }
    }

    /// <summary>
    /// Asks the global context to stop at the coming measure boundary, and forgets the
    /// timestep's events.
    /// </summary>
    public override void StopTranslationTimestep()
    {
        if (TranslatorSchemeHelpers.ToBool(GetProperty(TimingPropertySymbol))
            && !TranslatorSchemeHelpers.ToBool(GetProperty(SkipBarsSymbol)))
        {
            Rational mp = TranslatorSchemeHelpers
                .ToMoment(GetProperty(MeasurePositionSymbol), Moment.Zero).MainPart;

            Rational barleft = mp < Rational.Zero
                ? -mp
                : MeasureTiming.MeasureLength(Context) - mp;

            if (barleft > Rational.Zero && barleft.IsFinite)
            {
                Moment nextmom = new Moment(NowMoment.MainPart + barleft);
                Context?.GlobalContext?.AddMomentToProcess(nextmom);
            }
        }

        _altEvent = null;
        _barCheckEvent = null;
        _polymetricTimeSignatureEvents.Clear();
    }

    /// <summary>Seeds the timing properties before the first timestep.</summary>
    public override void Initialize()
    {
        // Sanity check: When we are not in the top user-accessible context (which is
        // recommended in cases of polymeter with unaligned measures), we expect that
        // a context above (typically Score) has been initialized as Timing so that we
        // can copy its current property values for our initial values here.
        Context parent = Context?.Parent;
        if (parent != null
            && parent.IsAccessibleToUser
            && parent.FindContextAbove(TimingSymbol) == null)
        {
            Warn.ProgrammingError("Can't find Timing context template");
        }

        object barNumber = GetProperty(CurrentBarNumberSymbol);
        if (!IsSchemeInteger(barNumber))
        {
            barNumber = 1L;
        }

        Context?.SetProperty(CurrentBarNumberSymbol, barNumber);
        Context?.SetProperty(InternalBarNumberSymbol, barNumber);

        object timeSignature = GetProperty(TimeSignatureSymbol);
        if (!(timeSignature is Pair) && !IsFalse(timeSignature))
        {
            Warn.ProgrammingError("missing timeSignature");
            timeSignature = false;
        }

        Context.SetProperty(TimeSignatureSymbol, timeSignature);

        object measureLength = GetProperty(MeasureLengthSymbol);
        if (measureLength is Nil)
        {
            measureLength = CallScheme(CalcMeasureLengthProcSymbol, timeSignature);
        }

        Context?.SetProperty(MeasureLengthSymbol, measureLength);

        Context?.SetProperty(
            MeasurePositionSymbol, new Moment(Rational.Zero, NowMoment.GracePart));
        Context?.SetProperty(MeasureStartNowSymbol, true);

        object timeSignatureSettings = GetProperty(TimeSignatureSettingsSymbol);
        if (!(timeSignatureSettings is Pair))
        {
            Warn.ProgrammingError("missing timeSignatureSettings");
            timeSignatureSettings
                = LilyPondScheme.LookupProcedure(DefaultTimeSignatureSettingsSymbol)
                  ?? Nil.Instance;
        }

        Context?.SetProperty(TimeSignatureSettingsSymbol, timeSignatureSettings);

        object beamExceptions = GetProperty(BeamExceptionsSymbol);
        if (!(beamExceptions is Pair))
        {
            beamExceptions
                = CallScheme(BeamExceptionsProcSymbol, timeSignature, timeSignatureSettings);
        }

        Context?.SetProperty(BeamExceptionsSymbol, beamExceptions);

        object beatBase = GetProperty(BeatBaseSymbol);
        if (!TranslatorSchemeHelpers.IsExactRational(beatBase))
        {
            beatBase = CallScheme(BeatBaseProcSymbol, timeSignature, timeSignatureSettings);
        }

        Context?.SetProperty(BeatBaseSymbol, beatBase);

        object beatStructure = GetProperty(BeatStructureSymbol);
        if (!(beatStructure is Pair))
        {
            beatStructure = CallScheme(
                BeatStructureProcSymbol, beatBase, timeSignature, timeSignatureSettings);
        }

        Context?.SetProperty(BeatStructureSymbol, beatStructure);

        object submeasureStructure = GetProperty(SubmeasureStructureSymbol);
        if (!(submeasureStructure is Pair))
        {
            submeasureStructure = CallScheme(
                CalcSubmeasureStructureProcSymbol,
                beatBase,
                timeSignature,
                timeSignatureSettings);
        }

        Context?.SetProperty(SubmeasureStructureSymbol, submeasureStructure);

        Context.SetProperty(BeamHalfMeasureSymbol, GetProperty(BeamHalfMeasureSymbol));

        Context.SetProperty(AutoBeamingSymbol, GetProperty(AutoBeamingSymbol));
    }

    /// <summary>Advances the measure clock by the time the previous timestep took.</summary>
    public override void StartTranslationTimestep()
    {
        GlobalContext global = Context?.GlobalContext;
        if (global == null)
        {
            Warn.ProgrammingError("Timing_translator without a global context");
            return;
        }

        Moment now = global.NowMoment;
        Moment dt = now - global.PreviousMoment;
        if (dt < Moment.Zero)
        {
            Warn.ProgrammingError("moving backwards in time");
            dt = Moment.Zero;
        }
        else if (dt.MainPart.IsInfinite)
        {
            Warn.ProgrammingError("moving infinitely to future");
            dt = Moment.Zero;
        }

        if (!dt.IsNonZero)
        {
            return;
        }

        if (_fineEvent != null)
        {
            if (!TranslatorSchemeHelpers.ToBool(_fineEvent.GetProperty(FineFoldedSymbol)))
            {
                TranslatorSchemeHelpers.EventWarning(_fineEvent, "found music after \\fine");
            }

            _fineEvent = null;
        }

        _measureLengthChangeEvent = null;

        if (_partialEvent != null)
        {
            Context?.UnsetProperty(PartialBusySymbol);
            _partialEvent = null;
        }

        Rational mp = TranslatorSchemeHelpers
            .ToMoment(GetProperty(MeasurePositionSymbol), Moment.Zero).MainPart;

        object measureStartNow = Nil.Instance;

        if (TranslatorSchemeHelpers.ToBool(GetProperty(TimingPropertySymbol)))
        {
            Rational len = MeasureTiming.MeasureLength(Context);

            mp += dt.MainPart;

            if (mp >= len)
            {
                long cbn = TranslatorSchemeHelpers.ToLong(GetProperty(CurrentBarNumberSymbol), 0);
                long ibn = TranslatorSchemeHelpers.ToLong(GetProperty(InternalBarNumberSymbol), 0);

                // Advance by just one measure.
                mp -= len;
                ++cbn;
                ++ibn;

                // Advance through any remaining measures.
                long numMeasures = (mp / len).TruncatedInteger();
                mp %= len;
                cbn += numMeasures;
                ibn += numMeasures;

                Context?.SetProperty(CurrentBarNumberSymbol, cbn);
                Context?.SetProperty(InternalBarNumberSymbol, ibn);
                _measureStartMoment = Moment.Infinity;
            }

            if (!mp.IsNonZero && dt.MainPart.IsNonZero && _measureStartMoment == Moment.Infinity)
            {
                // We have arrived at zero (as opposed to revisiting it).
                measureStartNow = true;
                _measureStartMoment = now;
            }
        }

        // Because "timing" can be switched on and off asynchronously with
        // graces, measurePosition might get into strange settings of
        // grace_part_.  It does not actually make sense to have it diverge
        // from the main timing.  Updating the grace part outside of the
        // actual check for "timing" looks strange and will lead to changes
        // of grace_part_ even when timing is off.  However, when timing is
        // switched back on again, this will generally happen in an override
        // that does _not_ in itself advance current_moment.  So the whole
        // timing advance logic will only get triggered while "timing" is
        // still of.  Maybe we should keep measurePosition.grace_part_
        // constantly at zero anyway?

        Context?.SetProperty(MeasurePositionSymbol, new Moment(mp, now.GracePart));
        Context.SetProperty(MeasureStartNowSymbol, measureStartNow);

        // We set whichBar at each timestep because the user manuals used to suggest
        // using \set Timing.whichBar = ... rather than \once \set Timing.whichBar =
        // ..., so we might need to erase the user's value from the previous
        // timestep.
        //
        // It might be nice to set up a convert-ly rule to change user code to use
        // \bar and redocument whichBar as internal.
        Context.SetProperty(WhichBarSymbol, Nil.Instance);
    }

    private static bool IsSchemeInteger(object value)
        => value is long || value is int || value is System.Numerics.BigInteger;

    private static bool IsFalse(object value) => value is bool flag && !flag;

    private static object CallScheme(Symbol procedureName, params object[] arguments)
    {
        object procedure = LilyPondScheme.LookupProcedure(procedureName);
        if (procedure == null)
        {
            Warn.ProgrammingError(
                "Timing_translator: Scheme procedure not found: " + procedureName.Name);
            return Nil.Instance;
        }

        return SchemeUtilities.CallCallback(procedure, arguments);
    }
}
