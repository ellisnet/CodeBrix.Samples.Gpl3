/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Jan Nieuwenhuizen <janneke@gnu.org>
  Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using System.Globalization;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/multi-measure-rest-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/**
   The name says it all: make multi measure rests
*/

/// <summary>
/// Makes the <c>MultiMeasureRest</c> spanner — and its number, texts and scripts — for
/// music written with <c>R</c>.
/// </summary>
public class MultiMeasureRestEngraver : Engraver
{
    private static readonly Symbol MultiMeasureRestEvent
        = Symbol.Intern("multi-measure-rest-event");
    private static readonly Symbol MultiMeasureTextEvent
        = Symbol.Intern("multi-measure-text-event");
    private static readonly Symbol MultiMeasureArticulationEvent
        = Symbol.Intern("multi-measure-articulation-event");
    private static readonly Symbol ArticulationTypeSymbol = Symbol.Intern("articulation-type");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol MeasureCountSymbol = Symbol.Intern("measure-count");
    private static readonly Symbol MeasureStartNowSymbol = Symbol.Intern("measureStartNow");
    private static readonly Symbol InternalBarNumberSymbol = Symbol.Intern("internalBarNumber");
    private static readonly Symbol RestNumberThresholdSymbol
        = Symbol.Intern("restNumberThreshold");
    private static readonly Symbol CurrentCommandColumnSymbol
        = Symbol.Intern("currentCommandColumn");

    private readonly List<StreamEvent> _textEvents = new List<StreamEvent>();
    private readonly List<StreamEvent> _articulationEvents = new List<StreamEvent>();

    // text_[0] is a MultiMeasureRestNumber grob
    // the rest are optional MultiMeasureRestText and MultiMeasureRestScript
    // grobs
    private readonly List<Spanner> _text = new List<Spanner>();
    private StreamEvent _restEv;
    private Spanner _mmrest;
    private Moment _stopMoment = Moment.Zero;
    private int _startMeasure;

    // Ugh, this is a kludge - need this for multi-measure-rest-grace.ly
    private Item _lastCommandItem;
    private bool _firstTime = true;
    private int _numberThreshold;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public MultiMeasureRestEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Multi_measure_rest_engraver";

    /// <summary>Starts listening for the three multi-measure event classes.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(MultiMeasureRestEvent, ListenMultiMeasureRest);
        ListenTo(MultiMeasureTextEvent, ListenMultiMeasureText);
        ListenTo(MultiMeasureArticulationEvent, ListenMultiMeasureArticulation);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    private void ListenMultiMeasureRest(StreamEvent ev)
    {
        /* FIXME: Should use assign_event_once. Can't do that yet because of
           the kill-mm-rests hack in part-combine-iterator. */
        _restEv = ev;
        Moment now = NowMoment;
        _stopMoment = now + GetEventLength(_restEv, now);

        ClearLapsedEvents(now);
    }

    private void ListenMultiMeasureText(StreamEvent ev)
    {
        _textEvents.Add(ev);
    }

    private void ListenMultiMeasureArticulation(StreamEvent ev)
    {
        _articulationEvents.Add(ev);
    }

    private void AddBoundItemToGrobs(Item item)
    {
        Spanner.AddBoundItem(_mmrest, item);
        for (int i = 0; i < _text.Count; ++i)
        {
            Spanner.AddBoundItem(_text[i], item);
        }
    }

    private void ClearLapsedEvents(Moment now)
    {
        if (now.MainPart >= _stopMoment.MainPart)
        {
            _restEv = null;
            _textEvents.Clear();
            _articulationEvents.Clear();
        }
    }

    private bool GrobsInitialized() => _mmrest != null;

    private void InitializeGrobs()
    {
        _mmrest = MakeSpanner("MultiMeasureRest", _restEv);
        _text.Add(MakeSpanner("MultiMeasureRestNumber", _mmrest));

        if (_articulationEvents.Count > 0)
        {
            for (int i = 0; i < _articulationEvents.Count; i++)
            {
                StreamEvent e = _articulationEvents[i];
                Spanner sp = MakeSpanner("MultiMeasureRestScript", e);
                ScriptEngraver.MakeScriptFromEvent(
                    sp, Context, e.GetProperty(ArticulationTypeSymbol), i);
                object dir = e.GetProperty(DirectionSymbol);
                if (DirectionalElementInterface.IsDirection(dir))
                {
                    sp.SetProperty(DirectionSymbol, dir);
                }

                _text.Add(sp);
            }
        }

        if (_textEvents.Count > 0)
        {
            for (int i = 0; i < _textEvents.Count; i++)
            {
                StreamEvent e = _textEvents[i];
                Spanner sp = MakeSpanner("MultiMeasureRestText", e);
                object t = e.GetProperty(TextSymbol);
                object dir = e.GetProperty(DirectionSymbol);
                sp.SetProperty(TextSymbol, t);
                if (DirectionalElementInterface.IsDirection(dir))
                {
                    sp.SetProperty(DirectionSymbol, dir);
                }

                _text.Add(sp);
            }
        }

        /*
          Stack different scripts.
        */
        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            object dir = (long)d.Value;
            Grob last = null;
            for (int i = 0; i < _text.Count; i++)
            {
                if (SchemeUtilities.IsEqual(dir, _text[i].GetProperty(DirectionSymbol)))
                {
                    if (last != null)
                    {
                        SidePositionInterface.AddSupport(_text[i], last);
                    }

                    last = _text[i];
                }
            }
        }

        for (int i = 0; i < _text.Count; i++)
        {
            SidePositionInterface.AddSupport(_text[i], _mmrest);
            _text[i].YParent = _mmrest;
            _text[i].XParent = _mmrest;
        }
    }

    private void ResetGrobs()
    {
        _text.Clear();
        _mmrest = null;
    }

    private void SetMeasureCount(int n)
    {
        _mmrest.SetProperty(MeasureCountSymbol, (long)n);

        Grob g = _text[0]; // the MultiMeasureRestNumber
        if (g.GetProperty(TextSymbol) is Nil)
        {
            if (n <= _numberThreshold)
            {
                g.Suicide();
            }
            else
            {
                object text = new MutableString(n.ToString(CultureInfo.InvariantCulture));
                g.SetProperty(TextSymbol, text);
            }
        }
    }

    /// <summary>Finalizes the running rest at each measure start, and begins a new one.</summary>
    public override void ProcessMusic()
    {
        if (SchemeUtilities.ToBool(GetProperty(MeasureStartNowSymbol)) || _firstTime)
        {
            _lastCommandItem = GetProperty(CurrentCommandColumnSymbol) as Item;

            // Finalize the current grobs.
            if (GrobsInitialized())
            {
                int currMeasure = RobustInt(GetProperty(InternalBarNumberSymbol), 0);
                SetMeasureCount(currMeasure - _startMeasure);
                if (_lastCommandItem != null)
                {
                    AddBoundItemToGrobs(_lastCommandItem);
                }

                AnnounceEndGrob(_mmrest, Nil.Instance);
                ResetGrobs();
            }
        }

        // Create new grobs if a rest event is (still) active.
        if (!GrobsInitialized() && _restEv != null)
        {
            InitializeGrobs();
            _textEvents.Clear();
            _articulationEvents.Clear();

            if (_lastCommandItem != null)
            {
                AddBoundItemToGrobs(_lastCommandItem);
                _lastCommandItem = null;
            }

            _startMeasure = RobustInt(GetProperty(InternalBarNumberSymbol), 0);
            _numberThreshold = RobustInt(GetProperty(RestNumberThresholdSymbol), 1);
        }

        _firstTime = false;
    }

    /// <summary>Expires lapsed events at each timestep.</summary>
    public override void StartTranslationTimestep()
    {
        ClearLapsedEvents(NowMoment);
    }

    private static int RobustInt(object value, int fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToInt(value, "multi-measure-rest-engraver")
            : fallback;
}
