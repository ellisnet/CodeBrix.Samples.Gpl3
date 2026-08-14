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

using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/time-signature-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/**
   generate time_signatures.
*/

/// <summary>
/// Creates a <c>TimeSignature</c> whenever <c>timeSignature</c> changes.
/// <para>
/// The comparison against the last seen specification is by IDENTITY (<c>eq?</c>),
/// exactly as upstream: a <c>\time</c> writes a fresh pair into the property, so
/// identity distinguishes "the property was set again" from "nothing happened" even
/// when the value is equal.
/// </para>
/// </summary>
public class TimeSignatureEngraver : Engraver
{
    private static readonly Symbol TimeSignatureContextSymbol = Symbol.Intern("timeSignature");
    private static readonly Symbol TimeSignaturePropertySymbol = Symbol.Intern("time-signature");
    private static readonly Symbol BreakVisibilitySymbol = Symbol.Intern("break-visibility");
    private static readonly Symbol InitialTimeSignatureVisibilitySymbol
        = Symbol.Intern("initialTimeSignatureVisibility");

    private static readonly Symbol MeasurePositionSymbol = Symbol.Intern("measurePosition");
    private static readonly Symbol PartialBusySymbol = Symbol.Intern("partialBusy");

    private Item _timeSignature;
    private object _lastSpec = Nil.Instance;
    private StreamEvent _event;
    private StreamEvent _localEvent;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public TimeSignatureEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Time_signature_engraver";

    /// <summary>Gets the time signature made this timestep, for tests.</summary>
    public Item TimeSignatureItem => _timeSignature;

    /// <summary>Starts listening for time-signature events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo("polymetric-time-signature-event", ListenPolymetricTimeSignature);
        ListenTo("reference-time-signature-event", ListenReferenceTimeSignature);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    private void ListenPolymetricTimeSignature(StreamEvent ev)
    {
        // When this engraver is operating in Staff (as usual), an event from the
        // following code is not pertinent:
        //
        //     \context Voice \polymetric \time ...
        //
        // We don't promote this use, so we don't expect to see it routinely, but
        // users could discover it and use it to set voice-specific beaming without
        // changing the printed time signature.
        //
        // Ignoring events that do not correspond to the value of timeSignature in
        // this context should mitigate the problem.  Is there any better solution?
        object eventValue = ev.GetProperty(TimeSignaturePropertySymbol);
        object contextValue = GetProperty(TimeSignatureContextSymbol);
        if (CorePrimitives.SchemeEqual(contextValue, eventValue))
        {
            _localEvent = ev;
        }
    }

    private void ListenReferenceTimeSignature(StreamEvent ev) => _event = ev;

    /// <summary>Creates the <c>TimeSignature</c> when the specification changed.</summary>
    public override void ProcessMusic()
    {
        if (_timeSignature != null)
        {
            return;
        }

        object spec = GetProperty(TimeSignatureContextSymbol);
        if (!ReferenceEquals(_lastSpec, spec)
            && (spec is Pair || (spec is bool flag && !flag)))
        {
            StreamEvent ev = _localEvent ?? _event;
            _timeSignature = MakeItem(
                "TimeSignature", ev != null ? (object)ev : Nil.Instance);

            // check value before setting to respect overrides
            if (_timeSignature.GetProperty(TimeSignaturePropertySymbol) is Nil)
            {
                _timeSignature.SetProperty(TimeSignaturePropertySymbol, spec);
            }

            if (_lastSpec is Nil)
            {
                _timeSignature.SetProperty(
                    BreakVisibilitySymbol,
                    GetProperty(InitialTimeSignatureVisibilitySymbol));
            }

            _lastSpec = spec;
        }
    }

    /// <summary>Warns about a mid-measure time signature, then forgets the timestep.</summary>
    public override void StopTranslationTimestep()
    {
        if (_timeSignature != null && (_event != null || _localEvent != null))
        {
            // Avoid measure_position (context ()) here because its result is
            // normalized to be >= 0 always.
            if (GetProperty(MeasurePositionSymbol) is Moment mp
                && mp.MainPart > Rational.Zero
                && !TranslatorSchemeHelpers.ToBool(GetProperty(PartialBusySymbol)))
            {
                GrobWarning(_timeSignature, "mid-measure time signature without \\partial");
            }
        }

        _timeSignature = null;
        _event = null;
        _localEvent = null;
    }

    // Grob::warning reports at the grob's ultimate cause when it has one.
    private static void GrobWarning(Grob grob, string message)
    {
        StreamEvent cause = grob?.UltimateEventCause();
        if (cause != null)
        {
            TranslatorSchemeHelpers.EventWarning(cause, message);
        }
        else
        {
            Warn.Warning(message);
        }
    }
}
