/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2000--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/dynamic-engraver.cc, lily/dynamic-align-engraver.cc, lily/concurrent-hairpin-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - the three dynamic engravers share a file; the aligner and the collector exist only
//     to serve what the first one makes.
//   - upstream's std::unordered_set<Spanner *> `running_` becomes a HashSet with a
//     reference comparer, which is what a pointer set is.

/// <summary>
/// Creates hairpins, dynamic texts and dynamic text spanners.
/// </summary>
public class DynamicEngraver : Engraver
{
    private static readonly Symbol AbsoluteDynamicEventSymbol
        = Symbol.Intern("absolute-dynamic-event");
    private static readonly Symbol BreakDynamicSpanEventSymbol
        = Symbol.Intern("break-dynamic-span-event");
    private static readonly Symbol SpanDynamicEventSymbol = Symbol.Intern("span-dynamic-event");
    private static readonly Symbol SpannerBrokenSymbol = Symbol.Intern("spanner-broken");
    private static readonly Symbol SpanTypeSymbol = Symbol.Intern("span-type");
    private static readonly Symbol SpanTextSymbol = Symbol.Intern("span-text");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol NoneSymbol = Symbol.Intern("none");
    private static readonly Symbol HairpinTypeSymbol = Symbol.Intern("hairpin");
    private static readonly Symbol AdjacentSpannersSymbol = Symbol.Intern("adjacent-spanners");
    private static readonly Symbol ClassSymbol = Symbol.Intern("class");
    private static readonly Symbol DecrescendoEventSymbol = Symbol.Intern("decrescendo-event");
    private static readonly Symbol CrescendoEventSymbol = Symbol.Intern("crescendo-event");
    private static readonly Symbol NoteHeadsSymbol = Symbol.Intern("note-heads");
    private static readonly Symbol RestSymbol = Symbol.Intern("rest");
    private static readonly Symbol CurrentMusicalColumnSymbol
        = Symbol.Intern("currentMusicalColumn");
    private static readonly Symbol HairpinInterface = Symbol.Intern("hairpin-interface");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");

    private readonly UniqueSpanEventListener _spanDynamicListener
        = new UniqueSpanEventListener();
    private Spanner _currentSpanner;
    private Spanner _finishedSpanner;
    private Item _script;
    private StreamEvent _scriptEvent;
    private bool _endNewSpanner;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public DynamicEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Dynamic_engraver";

    /// <summary>Starts listening for dynamic events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(AbsoluteDynamicEventSymbol, ListenAbsoluteDynamic);
        ListenTo(BreakDynamicSpanEventSymbol, ListenBreakDynamicSpan);
        ListenTo(SpanDynamicEventSymbol, _spanDynamicListener.Listen);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Ends the running spanner and starts whatever this timestep asks for.</summary>
    public override void ProcessMusic()
    {
        if (_currentSpanner != null)
        {
            StreamEvent ender = _spanDynamicListener.Stop
                                ?? _scriptEvent
                                ?? _spanDynamicListener.Start;
            if (ender != null)
            {
                _finishedSpanner = _currentSpanner;
                AnnounceEndGrob(_finishedSpanner, ender);
                _currentSpanner = null;
            }
        }

        if (_spanDynamicListener.Start is StreamEvent starter)
        {
            string startType = GetSpannerType(starter);
            object crescType = GetPropertySetting(
                starter, SpanTypeSymbol, Symbol.Intern(startType + "Spanner"));
            if (ReferenceEquals(crescType, TextSymbol))
            {
                _currentSpanner = MakeSpanner("DynamicTextSpanner", starter);
                object text = GetPropertySetting(
                    starter, SpanTextSymbol, Symbol.Intern(startType + "Text"));
                if (TextInterface.IsMarkup(text))
                {
                    _currentSpanner.SetProperty(TextSymbol, text);
                }

                /*
                  If the line of a text spanner is hidden, end the alignment spanner
                  early: this allows dynamics to be spaced individually instead of
                  being linked together.
                */
                if (ReferenceEquals(_currentSpanner.GetProperty(StyleSymbol), NoneSymbol))
                {
                    _currentSpanner.SetProperty(SpannerBrokenSymbol, true);
                }
            }
            else
            {
                if (!ReferenceEquals(crescType, HairpinTypeSymbol))
                {
                    TranslatorSchemeHelpers.EventWarning(
                        starter,
                        "unknown crescendo style: " + SchemeUtilities.DeepCopy(crescType)
                        + "\ndefaulting to hairpin.");
                }

                _currentSpanner = MakeSpanner("Hairpin", starter);
            }

            // if we have a break-dynamic-span event right after the start dynamic, break
            // the new spanner immediately
            if (_endNewSpanner)
            {
                _currentSpanner.SetProperty(SpannerBrokenSymbol, true);
                _endNewSpanner = false;
            }

            if (_finishedSpanner != null)
            {
                if (_finishedSpanner.HasInterface(HairpinInterface))
                {
                    PointerGroupInterface.AddGrob(
                        _finishedSpanner, AdjacentSpannersSymbol, _currentSpanner);
                }

                if (_currentSpanner.HasInterface(HairpinInterface))
                {
                    PointerGroupInterface.AddGrob(
                        _currentSpanner, AdjacentSpannersSymbol, _finishedSpanner);
                }
            }
        }

        if (_scriptEvent != null)
        {
            _script = MakeItem("DynamicText", _scriptEvent);
            _script.SetProperty(TextSymbol, _scriptEvent.GetProperty(TextSymbol));
            if (_finishedSpanner != null)
            {
                _finishedSpanner.SetBound(Direction.Positive, _script);
            }

            if (_currentSpanner != null)
            {
                _currentSpanner.SetBound(Direction.Negative, _script);
            }
        }
    }

    /// <summary>Parents the dynamic text and binds the spanners to the note column.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!info.Grob.HasInterface(NoteColumnInterface) || !(info.Grob is Item column))
        {
            return;
        }

        if (_script != null && _script.XParent == null)
        {
            IReadOnlyList<Grob> heads
                = PointerGroupInterface.ExtractGrobSet(column, NoteHeadsSymbol);

            /*
              Spacing constraints may require dynamics to be attached to rests,
              so check for a rest if this note column has no note heads.
            */
            Grob xParent = heads.Count > 0 ? column : column.GetObject(RestSymbol) as Grob;
            if (xParent != null)
            {
                _script.XParent = xParent;
            }
        }

        if (_currentSpanner != null && _currentSpanner.GetBound(Direction.Negative) == null)
        {
            _currentSpanner.SetBound(Direction.Negative, column);
        }

        if (_finishedSpanner != null && _finishedSpanner.GetBound(Direction.Positive) == null)
        {
            _finishedSpanner.SetBound(Direction.Positive, column);
        }
    }

    /// <summary>Binds any loose spanner ends and clears the timestep.</summary>
    public override void StopTranslationTimestep()
    {
        if (_finishedSpanner != null && _finishedSpanner.GetBound(Direction.Positive) == null)
        {
            _finishedSpanner.SetBound(
                Direction.Positive, GetProperty(CurrentMusicalColumnSymbol) as Grob);
        }

        if (_currentSpanner != null && _currentSpanner.GetBound(Direction.Negative) == null)
        {
            _currentSpanner.SetBound(
                Direction.Negative, GetProperty(CurrentMusicalColumnSymbol) as Grob);
        }

        _script = null;
        _scriptEvent = null;
        _spanDynamicListener.Reset();
        _finishedSpanner = null;
        _endNewSpanner = false;
    }

    /// <summary>Warns about a dynamic spanner that never ended, and kills it.</summary>
    public override void FinalizeTranslation()
    {
        if (_currentSpanner != null && !_currentSpanner.IsLive)
        {
            _currentSpanner = null;
        }

        if (_currentSpanner != null)
        {
            StreamEvent ev = _currentSpanner.EventCause();
            _currentSpanner.Warning("unterminated " + GetSpannerType(ev));
            _currentSpanner.Suicide();
            _currentSpanner = null;
        }
    }

    private void ListenAbsoluteDynamic(StreamEvent ev)
        => StreamEvent.AssignEventOnce(ref _scriptEvent, ev);

    private void ListenBreakDynamicSpan(StreamEvent ev)
    {
        // Case 1: Already have a start dynamic event -> break applies to new
        //         spanner (created later) -> set a flag
        // Case 2: no new spanner, but spanner already active -> break it now
        if (_spanDynamicListener.Start != null)
        {
            _endNewSpanner = true;
        }
        else if (_currentSpanner != null)
        {
            _currentSpanner.SetProperty(SpannerBrokenSymbol, true);
        }
    }

    private object GetPropertySetting(StreamEvent evt, Symbol evprop, Symbol ctxprop)
    {
        object spannerType = evt.GetProperty(evprop);
        if (spannerType is Nil)
        {
            spannerType = GetProperty(ctxprop);
        }

        return spannerType;
    }

    private string GetSpannerType(StreamEvent ev)
    {
        string type = string.Empty;
        object classes = ev?.GetProperty(ClassSymbol);
        object startSym = classes is Pair pair ? pair.Car : null;
        if (ReferenceEquals(startSym, DecrescendoEventSymbol))
        {
            type = "decrescendo";
        }
        else if (ReferenceEquals(startSym, CrescendoEventSymbol))
        {
            type = "crescendo";
        }
        else
        {
            Warn.ProgrammingError("unknown dynamic spanner type");
        }

        return type;
    }
}

/// <summary>
/// Aligns hairpins and dynamic texts on a horizontal line.
/// </summary>
public class DynamicAlignEngraver : Engraver
{
    private static readonly Symbol SpannerBrokenSymbol = Symbol.Intern("spanner-broken");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol CurrentMusicalColumnSymbol
        = Symbol.Intern("currentMusicalColumn");
    private static readonly Symbol DynamicInterface = Symbol.Intern("dynamic-interface");
    private static readonly Symbol RhythmicHeadInterface = Symbol.Intern("rhythmic-head-interface");
    private static readonly Symbol StemInterface = Symbol.Intern("stem-interface");
    private static readonly Symbol FootnoteSpannerInterface
        = Symbol.Intern("footnote-spanner-interface");

    private static readonly Direction[] Both = { Direction.Negative, Direction.Positive };

    private Spanner _line;

    // Spanner manually broken; don't use it for new grobs
    private Spanner _endedLine;
    private Spanner _currentDynamicSpanner;
    private readonly List<Spanner> _ended = new List<Spanner>();
    private readonly List<Spanner> _started = new List<Spanner>();
    private readonly List<Grob> _scripts = new List<Grob>();
    private readonly List<Grob> _support = new List<Grob>();
    private readonly HashSet<Spanner> _running
        = new HashSet<Spanner>(ReferenceEqualityComparer.Instance);

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public DynamicAlignEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Dynamic_align_engraver";

    /// <summary>Collects everything the alignment line must carry or clear.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        // Upstream's registration order: dynamic, rhythmic_head, stem, footnote_spanner.
        if (info.Grob.HasInterface(DynamicInterface))
        {
            AcknowledgeDynamic(info);
        }

        if (info.Grob.HasInterface(RhythmicHeadInterface)
            || info.Grob.HasInterface(StemInterface))
        {
            _support.Add(info.Grob);
        }

        if (info.Grob.HasInterface(FootnoteSpannerInterface))
        {
            Grob parent = info.Grob.YParent;
            if (_line != null && parent != null && parent.HasInterface(DynamicInterface))
            {
                AxisGroupInterface.AddElement(_line, info.Grob);
            }
        }
    }

    /// <summary>Notes a dynamic spanner ending, and honours a manual break.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeEndGrob(GrobInfo info)
    {
        if (!info.Grob.HasInterface(DynamicInterface) || !(info.Grob is Spanner sp))
        {
            return;
        }

        _ended.Add(sp);

        if (_line == null)
        {
            return;
        }

        /* If the break flag is set, store the current spanner and let new dynamics
         * create a new spanner
         */
        bool spannerBroken = ReferenceEquals(_currentDynamicSpanner, sp)
                             && SchemeUtilities.ToBool(sp.GetProperty(SpannerBrokenSymbol));
        if (spannerBroken)
        {
            if (_endedLine != null)
            {
                Warn.ProgrammingError("already have a force-ended DynamicLineSpanner.");
            }

            _endedLine = _line;
            _line = null;
            _currentDynamicSpanner = null;
        }
    }

    /// <summary>Bounds the alignment line and hangs its support off the notes.</summary>
    public override void StopTranslationTimestep()
    {
        for (int i = 0; i < _started.Count; i++)
        {
            _running.Add(_started[i]);
        }

        for (int i = 0; i < _ended.Count; i++)
        {
            Spanner sp = _ended[i];
            if (!_running.Remove(sp))
            {
                // upstream indexes started_ with i here, which is a different list; the
                // port reports against the spanner that actually went missing.
                sp.ProgrammingError("lost track of this dynamic spanner");
            }
        }

        bool end = _line != null && _running.Count == 0;

        // Set the proper bounds for the current spanner and for a spanner that
        // is ended now
        SetSpannerBounds(_endedLine, true);
        SetSpannerBounds(_line, end);

        // If the flag is set to break the spanner after the current child, don't
        // add any more support points (needed e.g. for style=none, where the
        // invisible spanner should NOT be shifted since we don't have a line).
        bool spannerBroken
            = _currentDynamicSpanner != null
              && SchemeUtilities.ToBool(
                  _currentDynamicSpanner.GetProperty(SpannerBrokenSymbol));
        for (int i = 0; _line != null && !spannerBroken && i < _support.Count; i++)
        {
            SidePositionInterface.AddSupport(_line, _support[i]);
        }

        if (end)
        {
            _line = null;
        }

        _endedLine = null;
        _ended.Clear();
        _started.Clear();
        _scripts.Clear();
        _support.Clear();
    }

    private void CreateLineSpanner(Grob cause)
    {
        if (_line == null)
        {
            _line = MakeSpanner("DynamicLineSpanner", cause);
        }
    }

    private void AcknowledgeDynamic(GrobInfo info)
    {
        StreamEvent cause = info.EventCause;

        // Check whether an existing line spanner has the same direction
        if (_line != null && cause != null)
        {
            Direction lineDir = DirectionalElementInterface.GetGrobDirection(_line);
            Direction grobDir = DirectionalElementInterface.FromScheme(
                cause.GetProperty(DirectionSymbol), Direction.Center);

            // If we have an explicit direction for the new dynamic grob
            // that differs from the current line spanner, break the spanner
            if (grobDir != Direction.Center && lineDir != grobDir)
            {
                if (_endedLine == null)
                {
                    _endedLine = _line;
                }

                _line = null;
                _currentDynamicSpanner = null;
            }
        }

        CreateLineSpanner(info.Grob);

        if (info.Grob is Spanner sp)
        {
            _started.Add(sp);
            _currentDynamicSpanner = sp;
        }
        else if (info.Grob is Item item)
        {
            _scripts.Add(item);
        }
        else
        {
            info.Grob.ProgrammingError("unknown dynamic grob");
        }

        AxisGroupInterface.AddElement(_line, info.Grob);

        if (cause != null)
        {
            Direction d = DirectionalElementInterface.FromScheme(
                cause.GetProperty(DirectionSymbol), Direction.Center);
            if (d != Direction.Center)
            {
                DirectionalElementInterface.SetGrobDirection(_line, d);
            }
        }
    }

    private void SetSpannerBounds(Spanner line, bool end)
    {
        if (line == null)
        {
            return;
        }

        foreach (Direction d in Both)
        {
            if ((d == Direction.Negative && line.GetBound(Direction.Negative) == null)
                || (end && d == Direction.Positive
                    && line.GetBound(Direction.Positive) == null))
            {
                List<Spanner> spanners = d == Direction.Negative ? _started : _ended;
                Grob bound;
                if (_scripts.Count > 0)
                {
                    bound = _scripts[0];
                }
                else if (spanners.Count > 0)
                {
                    bound = spanners[0].GetBound(d);
                }
                else
                {
                    bound = GetProperty(CurrentMusicalColumnSymbol) as Grob;
                    Warn.ProgrammingError(
                        "started DynamicLineSpanner but have no left bound");
                }

                line.SetBound(d, bound);
            }
        }
    }
}

/// <summary>
/// Collects concurrent hairpins, so each knows what it shares its span with.
/// </summary>
public class ConcurrentHairpinEngraver : Engraver
{
    private static readonly Symbol ConcurrentHairpinsSymbol
        = Symbol.Intern("concurrent-hairpins");
    private static readonly Symbol HairpinInterface = Symbol.Intern("hairpin-interface");

    private readonly List<Grob> _arrivingHairpins = new List<Grob>();
    private readonly List<Grob> _departingHairpins = new List<Grob>();
    private readonly List<Grob> _hairpinsHangingOut = new List<Grob>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public ConcurrentHairpinEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Concurrent_hairpin_engraver";

    /// <summary>Notes a hairpin arriving.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob.HasInterface(HairpinInterface))
        {
            _arrivingHairpins.Add(info.Grob);
        }
    }

    /// <summary>Notes a hairpin departing.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeEndGrob(GrobInfo info)
    {
        if (info.Grob.HasInterface(HairpinInterface))
        {
            _departingHairpins.Add(info.Grob);
        }
    }

    /// <summary>Cross-links every hairpin running at the same time as every other.</summary>
    public override void StopTranslationTimestep()
    {
        for (int i = 0; i < _departingHairpins.Count; i++)
        {
            for (int j = 0; j < _hairpinsHangingOut.Count; j++)
            {
                if (ReferenceEquals(_departingHairpins[i], _hairpinsHangingOut[j]))
                {
                    _hairpinsHangingOut.RemoveAt(j);
                    break;
                }
            }
        }

        if (_arrivingHairpins.Count > 0)
        {
            if (_arrivingHairpins.Count > 1)
            {
                for (int i = 0; i < _arrivingHairpins.Count - 1; i++)
                {
                    for (int j = i + 1; j < _arrivingHairpins.Count; j++)
                    {
                        PointerGroupInterface.AddGrob(
                            _arrivingHairpins[i],
                            ConcurrentHairpinsSymbol,
                            _arrivingHairpins[j]);
                        PointerGroupInterface.AddGrob(
                            _arrivingHairpins[j],
                            ConcurrentHairpinsSymbol,
                            _arrivingHairpins[i]);
                    }
                }
            }

            for (int i = 0; i < _arrivingHairpins.Count; i++)
            {
                for (int j = 0; j < _hairpinsHangingOut.Count; j++)
                {
                    PointerGroupInterface.AddGrob(
                        _arrivingHairpins[i],
                        ConcurrentHairpinsSymbol,
                        _hairpinsHangingOut[j]);
                    PointerGroupInterface.AddGrob(
                        _hairpinsHangingOut[j],
                        ConcurrentHairpinsSymbol,
                        _arrivingHairpins[i]);
                }
            }
        }

        _hairpinsHangingOut.AddRange(_arrivingHairpins);
        _arrivingHairpins.Clear();
        _departingHairpins.Clear();
    }

    /// <summary>Drops the hairpins still hanging out.</summary>
    public override void FinalizeTranslation() => _hairpinsHangingOut.Clear();
}
