/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2001--2026  Han-Wen Nienhuys <hanwen@xs4all.nl>
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
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/percent-repeat-iterator.cc;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// The iterator for <c>\repeat percent</c>: the body is iterated once for real, and each
/// later repetition is announced as a percent, double-percent or slash event instead.
/// </summary>
public sealed class PercentRepeatIterator : SequentialIterator
{
    private static readonly Symbol CalcRepeatSlashCountSymbol
        = Symbol.Intern("calc-repeat-slash-count");

    private static readonly Symbol DoublePercentEventSymbol = Symbol.Intern("DoublePercentEvent");
    private static readonly Symbol ElementSymbol = Symbol.Intern("element");
    private static readonly Symbol LengthSymbol = Symbol.Intern("length");
    private static readonly Symbol PercentEventSymbol = Symbol.Intern("PercentEvent");
    private static readonly Symbol RepeatCountSymbol = Symbol.Intern("repeat-count");
    private static readonly Symbol RepeatSlashEventSymbol = Symbol.Intern("RepeatSlashEvent");
    private static readonly Symbol SlashCountSymbol = Symbol.Intern("slash-count");

    private int _doneCount;
    private long _repeatCount;
    private Moment _bodyLength;
    private Symbol _eventType;
    private object _slashCount;

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Percent_repeat_iterator";

    /// <summary>Measures the body, then hands over to the sequence machinery.</summary>
    protected override void CreateContexts()
    {
        if (Music.GetProperty(ElementSymbol) is MusicObject body)
        {
            _bodyLength = body.GetLength();
        }

        _repeatCount = Music.GetProperty(RepeatCountSymbol) is long count ? count : 0;

        base.CreateContexts();

        DescendToBottomContext();
    }

    // Arrive here for the first time after the original percent expression is completed,
    // and then after each placeholder element. At this point of time, we can determine
    // what kind of percent expression we are dealing with and provide the respective
    // music expressions for the remaining repeats.
    /// <summary>Announces the percent event that stands in for each later repetition.</summary>
    protected override void NextElement()
    {
        base.NextElement();

        ++_doneCount;
        if (_doneCount >= _repeatCount)
        {
            return;
        }

        if (_doneCount == 1)
        {
            Rational measureLength = MeasureTiming.MeasureLength(Context);
            if (_bodyLength.MainPart == measureLength)
            {
                _eventType = PercentEventSymbol;
            }
            else if (_bodyLength.MainPart == measureLength * new Rational(2))
            {
                _eventType = DoublePercentEventSymbol;
            }
            else
            {
                if (Music.GetProperty(ElementSymbol) is MusicObject body)
                {
                    _slashCount = CalcSlashCount(body);
                }

                _eventType = RepeatSlashEventSymbol;
            }
        }

        MusicObject percent = MusicFactory.MakeMusic(_eventType);
        if (percent == null)
        {
            return;
        }

        percent.SetSpot(Music.Origin);
        percent.SetProperty(LengthSymbol, _bodyLength);
        percent.SetProperty(RepeatCountSymbol, (long)(_doneCount + 1));
        if (_slashCount != null)
        {
            percent.SetProperty(SlashCountSymbol, _slashCount);
        }

        ReportEvent(percent);
    }

    private static object CalcSlashCount(MusicObject body)
    {
        object procedure = LilyPondScheme.LookupProcedure(CalcRepeatSlashCountSymbol);
        Interpreter interpreter = LilyPondScheme.Current;
        if (procedure == null || interpreter == null)
        {
            Warn.ProgrammingError("calc-repeat-slash-count is not available");
            return null;
        }

        return interpreter.Evaluator.Apply(procedure, new object[] { body });
    }
}
