/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2000--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>, Erik Sandberg
  <mandolaerik@gmail.com>                     (percent-repeat-engraver.cc,
                                               slash-repeat-engraver.cc)
  Copyright (C) 2011--2026 Neil Puttock <n.puttock@gmail.com>
                                              (double-percent-repeat-engraver.cc)
  Copyright (C) 2000--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
                                              (repeat-acknowledge-engraver.cc)

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

using System.Globalization;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/percent-repeat-engraver.cc, lily/double-percent-repeat-engraver.cc, lily/slash-repeat-engraver.cc, lily/repeat-acknowledge-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>Makes whole-measure repeats: the <c>PercentRepeat</c> spanner and its counter.</summary>
public class PercentRepeatEngraver : Engraver
{
    private static readonly Symbol CountPercentRepeatsSymbol
        = Symbol.Intern("countPercentRepeats");

    private static readonly Symbol CurrentCommandColumnSymbol
        = Symbol.Intern("currentCommandColumn");

    private static readonly Symbol PercentEventSymbol = Symbol.Intern("percent-event");
    private static readonly Symbol RepeatCountSymbol = Symbol.Intern("repeat-count");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");

    private StreamEvent _percentEvent;

    // Moment (global time) where the percent should end.
    private Moment _stopMoment;

    private Spanner _percent;
    private Spanner _percentCounter;

    // If the measure starts with grace notes, the percent event occurs on the first
    // non-grace note, but we want the spanner's left bound to be the non-musical column
    // that was current at the time of the first grace note.
    private Item _firstCommandColumn;
    private Moment _commandMoment = new Moment(-1L);

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public PercentRepeatEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Percent_repeat_engraver";

    /// <summary>Starts listening for percent events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(PercentEventSymbol, ListenPercent);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Closes a finished percent, and opens a new one when asked.</summary>
    public override void ProcessMusic()
    {
        Moment now = NowMoment;

        // Maintain the first non-musical column in the grace group.
        if (now.MainPart != _commandMoment.MainPart)
        {
            _firstCommandColumn = GetProperty(CurrentCommandColumnSymbol) as Item;
            _commandMoment = now;
        }

        // Stop running percent if it has reached completion.
        if (_percent != null && _stopMoment.MainPart == now.MainPart)
        {
            _percent.SetBound(Direction.Positive, _firstCommandColumn);
            _percent = null;
            if (_percentCounter != null)
            {
                _percentCounter.SetBound(Direction.Positive, _firstCommandColumn);
                _percentCounter = null;
            }
        }
        else if (_percent != null && _percentEvent != null)
        {
            TranslatorSchemeHelpers.EventWarning(
                _percentEvent, "percent repeat started while another already in progress");

            _percent.Suicide();
            _percent = null;
            if (_percentCounter != null)
            {
                _percentCounter.Suicide();
                _percentCounter = null;
            }
        }

        // Start a new percent if requested.
        if (_percentEvent != null)
        {
            _stopMoment = now + GetEventLength(_percentEvent);
            Context?.GlobalContext?.AddMomentToProcess(_stopMoment);
            _percent = MakeSpanner("PercentRepeat", _percentEvent);
            _percent.SetBound(Direction.Negative, _firstCommandColumn);

            object count = _percentEvent.GetProperty(RepeatCountSymbol);
            if (!(count is Nil)
                && GetProperty(CountPercentRepeatsSymbol) is bool counting && counting
                && Context.CheckRepeatCountVisibility(Context, count))
            {
                _percentCounter = MakeSpanner("PercentRepeatCounter", _percentEvent);

                _percentCounter.SetProperty(TextSymbol, CountText(count));
                _percentCounter.SetBound(Direction.Negative, _firstCommandColumn);
                SidePositionInterface.AddSupport(_percentCounter, _percent);
                _percentCounter.SetParent(_percent, Axis.Y);
                _percentCounter.SetParent(_percent, Axis.X);
            }
            else
            {
                _percentCounter = null;
            }
        }
    }

    /// <summary>Forgets the event so it is not acted on twice.</summary>
    public override void StopTranslationTimestep() => _percentEvent = null;

    /// <summary>Complains about a percent that never finished, and removes it.</summary>
    public override void FinalizeTranslation()
    {
        if (_percent != null)
        {
            _percent.ProgrammingError("percent end moment should have been processed");
            _percent.Suicide();
            _percentCounter?.Suicide();
        }

        base.FinalizeTranslation();
    }

    internal static string CountText(object count)
        => SchemeConvert.IsNumber(count)
            ? SchemeConvert.ToLong(count, "percent-repeat").ToString(CultureInfo.InvariantCulture)
            : string.Empty;

    private void ListenPercent(StreamEvent ev)
        => StreamEvent.AssignEventOnce(ref _percentEvent, ev);
}

/// <summary>Makes double-measure repeats: the <c>DoublePercentRepeat</c> item and its counter.</summary>
public class DoublePercentRepeatEngraver : Engraver
{
    private static readonly Symbol CountPercentRepeatsSymbol
        = Symbol.Intern("countPercentRepeats");

    private static readonly Symbol DoublePercentEventSymbol
        = Symbol.Intern("double-percent-event");

    private static readonly Symbol ForbidBreakSymbol = Symbol.Intern("forbidBreak");
    private static readonly Symbol RepeatCountSymbol = Symbol.Intern("repeat-count");
    private static readonly Symbol ScoreSymbol = Symbol.Intern("Score");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");

    private StreamEvent _percentEvent;

    // Moment (global time) where the percent started.
    private Moment _startMoment;
    private bool _shouldPrintDoublePercent;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public DoublePercentRepeatEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Double_percent_repeat_engraver";

    /// <summary>Starts listening for double-percent events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(DoublePercentEventSymbol, ListenDoublePercent);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Decides whether the sign prints here, and forbids a break over it.</summary>
    public override void PreProcessMusic()
    {
        _shouldPrintDoublePercent
            = _percentEvent != null && NowMoment.MainPart == _startMoment.MainPart;

        // Prevent breaks over the percent sign.
        if (_shouldPrintDoublePercent)
        {
            Context?.FindContextAbove(ScoreSymbol)?.SetProperty(ForbidBreakSymbol, true);
        }
    }

    /// <summary>Makes the sign and its counter.</summary>
    public override void ProcessMusic()
    {
        if (!_shouldPrintDoublePercent)
        {
            return;
        }

        Item doublePercent = MakeItem("DoublePercentRepeat", _percentEvent);

        object count = _percentEvent.GetProperty(RepeatCountSymbol);
        if (!(count is Nil)
            && GetProperty(CountPercentRepeatsSymbol) is bool counting && counting
            && Context.CheckRepeatCountVisibility(Context, count))
        {
            Item counter = MakeItem("DoublePercentRepeatCounter", _percentEvent);

            counter.SetProperty(TextSymbol, PercentRepeatEngraver.CountText(count));

            SidePositionInterface.AddSupport(counter, doublePercent);
            counter.SetParent(doublePercent, Axis.Y);
            counter.SetParent(doublePercent, Axis.X);
        }

        _percentEvent = null;
    }

    private void ListenDoublePercent(StreamEvent ev)
    {
        if (StreamEvent.AssignEventOnce(ref _percentEvent, ev))
        {
            _startMoment = NowMoment + new Moment(MeasureTiming.MeasureLength(Context));
            Context?.GlobalContext?.AddMomentToProcess(_startMoment);
        }
    }
}

/// <summary>
/// Makes beat repeats. This acknowledges repeated music with "percent" style, and
/// typesets a slash sign or a double percent sign.
/// </summary>
public class SlashRepeatEngraver : Engraver
{
    private static readonly Symbol RepeatSlashEventSymbol = Symbol.Intern("repeat-slash-event");
    private static readonly Symbol SlashCountSymbol = Symbol.Intern("slash-count");

    private StreamEvent _slash;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public SlashRepeatEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Slash_repeat_engraver";

    /// <summary>Starts listening for slash events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(RepeatSlashEventSymbol, ListenRepeatSlash);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Makes the slash, or the double-percent sign when the count is zero.</summary>
    public override void ProcessMusic()
    {
        if (_slash != null)
        {
            long count = TranslatorSchemeHelpers.ToLong(_slash.GetProperty(SlashCountSymbol), 0);
            MakeItem(count == 0 ? "DoubleRepeatSlash" : "RepeatSlash", _slash);
            _slash = null;
        }
    }

    private void ListenRepeatSlash(StreamEvent ev)
        => StreamEvent.AssignEventOnce(ref _slash, ev);
}

/// <summary>
/// Augments <c>repeatCommands</c> with <c>start-repeat</c> and <c>end-repeat</c> entries
/// based on received events.
/// <para>
/// This is internal behaviour that lets other engravers support both <c>\repeat volta</c>
/// and a hand-set <c>repeatCommands</c> without knowing which they are looking at.
/// </para>
/// </summary>
public class RepeatAcknowledgeEngraver : Engraver
{
    private static readonly Symbol EndRepeatSymbol = Symbol.Intern("end-repeat");
    private static readonly Symbol RepeatCommandsSymbol = Symbol.Intern("repeatCommands");
    private static readonly Symbol RepeatCountSymbol = Symbol.Intern("repeat-count");
    private static readonly Symbol ReturnCountSymbol = Symbol.Intern("return-count");
    private static readonly Symbol StartRepeatSymbol = Symbol.Intern("start-repeat");
    private static readonly Symbol VoltaRepeatEndEventSymbol
        = Symbol.Intern("volta-repeat-end-event");

    private static readonly Symbol VoltaRepeatStartEventSymbol
        = Symbol.Intern("volta-repeat-start-event");

    private bool _heardVoltaRepeatEnd;
    private bool _heardVoltaRepeatStart;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public RepeatAcknowledgeEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Repeat_acknowledge_engraver";

    /// <summary>Clears <c>repeatCommands</c> at the start.</summary>
    public override void Initialize()
    {
        base.Initialize();
        Context?.SetProperty(RepeatCommandsSymbol, Nil.Instance);
    }

    /// <summary>Starts listening for the two volta repeat events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(VoltaRepeatEndEventSymbol, ListenVoltaRepeatEnd);
        ListenTo(VoltaRepeatStartEventSymbol, ListenVoltaRepeatStart);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Empties <c>repeatCommands</c> where it is defined.</summary>
    public override void StartTranslationTimestep()
    {
        Context where = Context?.WhereDefined(RepeatCommandsSymbol, out object _);
        (where ?? Context)?.SetProperty(RepeatCommandsSymbol, Nil.Instance);
    }

    /// <summary>Forgets what was heard this timestep.</summary>
    public override void StopTranslationTimestep()
    {
        _heardVoltaRepeatEnd = false;
        _heardVoltaRepeatStart = false;
    }

    private void ListenVoltaRepeatEnd(StreamEvent ev)
    {
        if (_heardVoltaRepeatEnd)
        {
            return;
        }

        long count = TranslatorSchemeHelpers.ToLong(ev.GetProperty(ReturnCountSymbol), 0);
        if (count >= 0)
        {
            _heardVoltaRepeatEnd = true;
            AddRepeatCommand(Pair.List(EndRepeatSymbol, count));
        }
    }

    private void ListenVoltaRepeatStart(StreamEvent ev)
    {
        if (_heardVoltaRepeatStart)
        {
            return;
        }

        long count = TranslatorSchemeHelpers.ToLong(ev.GetProperty(RepeatCountSymbol), 0);
        if (count >= 1)
        {
            _heardVoltaRepeatStart = true;
            AddRepeatCommand(Pair.List(StartRepeatSymbol, count));
        }
    }

    private void AddRepeatCommand(object newCommand)
    {
        if (Context == null)
        {
            return;
        }

        Context where = Context.WhereDefined(RepeatCommandsSymbol, out object commands);
        if (where != null && (commands is Pair || commands is Nil))
        {
            where.SetProperty(RepeatCommandsSymbol, new Pair(newCommand, commands));
        }
    }
}
