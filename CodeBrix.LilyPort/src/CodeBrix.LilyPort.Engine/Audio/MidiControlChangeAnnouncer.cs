/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2016--2026 Heikki Tauriainen <g034737@welho.com>

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
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Audio; //was previously: lily/midi-cc-announcer.cc, lily/include/midi-cc-announcer.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - announce_from_context_properties() is DECLARED in the upstream header and DEFINED
//     nowhere in pinned 2.27.2 -- no definition, no caller. It is not carried, and this
//     note is the record of why, so a later reader comparing the header against this file
//     does not read the absence as an omission.

/// <summary>
/// Turns the five MIDI-control context properties into control-change events.
/// <para>
/// Two things use this and they differ only in where the value comes from:
/// <see cref="Translation.StaffPerformer"/> reads the properties once when a staff is
/// created, to establish initial values, and
/// <see cref="Translation.MidiControlChangePerformer"/> reads them out of a
/// <c>SetProperty</c> event, to follow changes as they happen.
/// </para>
/// </summary>
public abstract class MidiControlChangeAnnouncer
{
    /// <summary>
    /// The five MIDI controls LilyPond exposes, and how each maps to a context property.
    /// </summary>
    /// <remarks>
    /// A negative LSB control number means the control has only 7-bit ("coarse")
    /// resolution. The final all-zero entry that terminates upstream's C array has no
    /// analogue in a C# array and is not carried.
    /// </remarks>
    private static readonly ControlSpec[] Controls =
    {
        new ControlSpec("midiBalance", -1.0, 1.0, 8, 40),
        new ControlSpec("midiPanPosition", -1.0, 1.0, 10, 42),
        new ControlSpec("midiExpression", 0.0, 1.0, 11, 43),
        new ControlSpec("midiReverbLevel", 0.0, 1.0, 91, -1),
        new ControlSpec("midiChorusLevel", 0.0, 1.0, 93, -1),
    };

    private readonly Input _origin;

    /// <summary>Initializes an announcer.</summary>
    /// <param name="origin">
    /// Where to report out-of-range warnings against, or <see langword="null"/> to report
    /// them without a location.
    /// </param>
    protected MidiControlChangeAnnouncer(Input origin = null) => _origin = origin;

    /// <summary>
    /// Announces a control change for every supported property that holds a usable value.
    /// </summary>
    public void AnnounceControlChanges()
    {
        foreach (ControlSpec spec in Controls)
        {
            object value = GetPropertyValue(spec.ContextPropertyName);
            if (!IsNumber(value))
            {
                continue;
            }

            double val = Convert.ToDouble(value);
            if (val >= spec.RangeMin && val <= spec.RangeMax)
            {
                // Normalize the value to the 0.0 to 1.0 range.
                val = (val - spec.RangeMin) / (spec.RangeMax - spec.RangeMin);

                // Transform the normalized context property value into a 14-bit or a
                // 7-bit (non-negative) integer depending on the MIDI control's
                // resolution. For directional value changes, CENTER will correspond to
                // 0.5 exactly, and round_halfway_up rounds upwards in case of doubt. That
                // means that center position will round to 0x40 or 0x2000 by a hair's
                // breadth.
                const double fullFineScale = 0x3FFF;
                const double fullCoarseScale = 0x7F;
                bool fineResolution = spec.LsbControlNumber >= 0;
                int v = (int)LibcExtension.RoundHalfwayUp(
                    val * (fineResolution ? fullFineScale : fullCoarseScale));

                // Announce a control change for the most significant 7 bits of the
                // control value (and, if the control supports fine resolution, for the
                // least significant 7 bits as well).
                DoAnnounce(new AudioControlChange(
                    spec.MsbControlNumber, fineResolution ? v >> 7 : v));

                if (fineResolution)
                {
                    DoAnnounce(new AudioControlChange(spec.LsbControlNumber, v & 0x7F));
                }
            }
            else
            {
                WarnHere(
                    "ignoring out-of-range value change for MIDI property `"
                    + spec.ContextPropertyName + "'");
            }
        }
    }

    /// <summary>Reads the value to use for a context property.</summary>
    /// <param name="propertyName">The property to read.</param>
    /// <returns>The value, or something non-numeric to skip the control.</returns>
    protected abstract object GetPropertyValue(string propertyName);

    /// <summary>Does whatever this announcer does with a control change it has built.</summary>
    /// <param name="item">The control change.</param>
    protected abstract void DoAnnounce(AudioControlChange item);

    // See the note in DynamicPerformer: upstream's test is scm_is_number, and
    // TryToRational accepts everything that would.
    private static bool IsNumber(object value)
        => Bootstrap.SchemeConvert.TryToRational(value, out _);

    private void WarnHere(string message)
    {
        if (_origin != null)
        {
            _origin.Warning(message);
        }
        else
        {
            Warn.Warning(message);
        }
    }

    /// <summary>One MIDI control's mapping to a LilyPond context property.</summary>
    private sealed class ControlSpec
    {
        internal ControlSpec(
            string contextPropertyName,
            double rangeMin,
            double rangeMax,
            int msbControlNumber,
            int lsbControlNumber)
        {
            ContextPropertyName = contextPropertyName;
            RangeMin = rangeMin;
            RangeMax = rangeMax;
            MsbControlNumber = msbControlNumber;
            LsbControlNumber = lsbControlNumber;
        }

        internal string ContextPropertyName { get; }

        internal double RangeMin { get; }

        internal double RangeMax { get; }

        internal int MsbControlNumber { get; }

        internal int LsbControlNumber { get; }
    }
}
