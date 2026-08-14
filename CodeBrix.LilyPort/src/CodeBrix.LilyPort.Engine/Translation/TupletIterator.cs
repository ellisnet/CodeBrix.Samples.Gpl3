/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>,
                 Erik Sandberg <mandolaerik@gmail.com>

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

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/tuplet-iterator.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Iterates <c>\times</c>, by sending <c>TupletSpanEvent</c>s at the start and end of each
/// tuplet bracket. Extra stop/start pairs are sent at regular intervals when
/// <c>tupletSpannerDuration</c> is set.
/// </summary>
public sealed class TupletIterator : MusicWrapperIterator
{
    private static readonly Symbol DenominatorSymbol = Symbol.Intern("denominator");
    private static readonly Symbol DurationSymbol = Symbol.Intern("duration");
    private static readonly Symbol LengthSymbol = Symbol.Intern("length");
    private static readonly Symbol NumeratorSymbol = Symbol.Intern("numerator");
    private static readonly Symbol TupletSpanEventSymbol = Symbol.Intern("TupletSpanEvent");
    private static readonly Symbol TupletSpannerDurationSymbol
        = Symbol.Intern("tupletSpannerDuration");

    private static readonly Symbol TweaksSymbol = Symbol.Intern("tweaks");

    private readonly ContextHandle _tupletHandler = new ContextHandle();

    // tupletSpannerDuration; the negative main part marks "not read yet", exactly as
    // upstream's `Moment spanner_duration_ {-1}' does.
    private Moment _spannerDuration = new Moment(-1L);

    // Next time to add a stop/start pair.
    private Moment _nextSplitMoment;

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Tuplet_iterator";

    /// <summary>Gets when the next event comes due, never overshooting the next split.</summary>
    public override Moment PendingMoment
    {
        get
        {
            Moment next = base.PendingMoment;
            if (next < Moment.Infinity && _nextSplitMoment < next)
            {
                next = _nextSplitMoment;
            }

            return next;
        }
    }

    /// <summary>Splits the bracket where it must, then walks the wrapped music.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        if (_spannerDuration.MainPart < Rational.Zero) // first time
        {
            if (Music.GetProperty(DurationSymbol) is Duration duration)
            {
                _spannerDuration = new Moment(duration.ToWholeNotes());
            }
            else
            {
                object setting = Context.GetProperty(TupletSpannerDurationSymbol);
                _spannerDuration = new Moment(
                    SchemeConvert.IsNumber(setting)
                        ? SchemeConvert.ToRational(setting, "Tuplet_iterator::process")
                        : Rational.Infinity);
            }
        }

        if (_spannerDuration.IsNonZero && new Moment(until.MainPart) == _nextSplitMoment)
        {
            if (_tupletHandler.Context != null)
            {
                CreateEvent(Direction.Positive)?.SendToContext(_tupletHandler.Context);
            }

            if (until.MainPart < MusicLength.MainPart)
            {
                Moment remaining = MusicLength - _nextSplitMoment;
                if (remaining < _spannerDuration)
                {
                    _spannerDuration = remaining;
                }

                _tupletHandler.Set(Context);
                MusicObject start = CreateEvent(Direction.Negative);
                if (start != null)
                {
                    ReportEvent(start);
                }

                _nextSplitMoment += _spannerDuration;
            }
            else
            {
                _tupletHandler.Reset();
            }
        }

        base.Process(until);

        if (Child != null && Child.Ok)
        {
            DescendToChild(Child.Context);
        }
    }

    /// <summary>Follows the child down the context tree once it has one.</summary>
    protected override void CreateContexts()
    {
        base.CreateContexts();

        if (Child != null && Child.Ok)
        {
            DescendToChild(Child.Context);
        }
    }

    private MusicObject CreateEvent(Direction direction)
    {
        MusicObject ev = MusicFactory.MakeSpanEvent(TupletSpanEventSymbol, direction);
        if (ev == null)
        {
            return null;
        }

        ev.SetSpot(Music.Origin);
        if (direction == Direction.Negative)
        {
            ev.SetProperty(NumeratorSymbol, Music.GetProperty(NumeratorSymbol));
            ev.SetProperty(DenominatorSymbol, Music.GetProperty(DenominatorSymbol));
            ev.SetProperty(TweaksSymbol, Music.GetProperty(TweaksSymbol));
            ev.SetProperty(LengthSymbol, _spannerDuration);
        }

        return ev;
    }
}
