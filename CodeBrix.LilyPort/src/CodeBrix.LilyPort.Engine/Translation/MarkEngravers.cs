/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Jan Nieuwenhuizen <janneke@gnu.org>
  Copyright (C) 2018--2026 Daniel Eble <nine.fierce.ballads@gmail.com>

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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/mark-tracking-translator.cc, lily/include/mark-tracking-translator.hh, lily/mark-engraver.cc, lily/include/mark-engraver.hh, lily/metronome-engraver.cc, lily/jump-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Chooses which marks <c>Mark_engraver</c> should engrave.
/// <para>
/// This is 2.27's coordination layer: it hears every mark-ish event at Score level,
/// resolves the conflicts, publishes the survivors as
/// <c>currentPerformanceMarkEvent</c> / <c>currentRehearsalMarkEvent</c>, and keeps
/// the segno/coda/rehearsal counters. <c>Mark_engraver</c> itself listens for NOTHING
/// — it only reads what this translator wrote.
/// </para>
/// </summary>
public class MarkTrackingTranslator : Translator
{
    private static readonly Symbol CurrentPerformanceMarkEventSymbol
        = Symbol.Intern("currentPerformanceMarkEvent");

    private static readonly Symbol CurrentRehearsalMarkEventSymbol
        = Symbol.Intern("currentRehearsalMarkEvent");

    private static readonly Symbol CodaMarkCountSymbol = Symbol.Intern("codaMarkCount");
    private static readonly Symbol SegnoMarkCountSymbol = Symbol.Intern("segnoMarkCount");
    private static readonly Symbol RehearsalMarkSymbol = Symbol.Intern("rehearsalMark");
    private static readonly Symbol LabelSymbol = Symbol.Intern("label");

    /// <summary>The kinds of event the translator tracks.</summary>
    private enum EventType
    {
        None = 0,
        AdHocMark,
        DefaultCodaMark,
        DefaultRehearsalMark,
        DefaultSegnoMark,
        SectionLabel,
        SpecificCodaMark,
        SpecificRehearsalMark,
        SpecificSegnoMark,
    }

    // Rehearsal marks, ad-hoc marks
    private StreamEvent _rehearsalEvent;
    private EventType _rehearsalEventType = EventType.None;

    // Coda marks, section labels, segno marks
    private StreamEvent _performanceEvent;
    private EventType _performanceEventType = EventType.None;

    private bool _firstTime = true;

    /// <summary>Initializes the translator in a context.</summary>
    /// <param name="context">The context this translator belongs to.</param>
    public MarkTrackingTranslator(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Mark_tracking_translator";

    /// <summary>Starts listening for the five mark event classes.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo("ad-hoc-mark-event", ListenAdHocMark);
        ListenTo("coda-mark-event", ListenCodaMark);
        ListenTo("rehearsal-mark-event", ListenRehearsalMark);
        ListenTo("section-label-event", ListenSectionLabel);
        ListenTo("segno-mark-event", ListenSegnoMark);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    private void ClearEvents()
    {
        if (_performanceEventType != EventType.None)
        {
            _performanceEvent = null;
            _performanceEventType = EventType.None;
            Context?.UnsetProperty(CurrentPerformanceMarkEventSymbol);
        }

        if (_rehearsalEventType != EventType.None)
        {
            _rehearsalEvent = null;
            _rehearsalEventType = EventType.None;
            Context?.UnsetProperty(CurrentRehearsalMarkEventSymbol);
        }
    }

    private void SetPerformanceEvent(EventType type, StreamEvent ev)
    {
        _performanceEvent = ev;
        _performanceEventType = type;
        Context?.SetProperty(CurrentPerformanceMarkEventSymbol, ev);
    }

    private void SetRehearsalEvent(EventType type, StreamEvent ev)
    {
        _rehearsalEvent = ev;
        _rehearsalEventType = type;
        Context?.SetProperty(CurrentRehearsalMarkEventSymbol, ev);
    }

    private bool SetPerformanceEventOnce(EventType type, StreamEvent ev)
    {
        if (!StreamEvent.AssignEventOnce(ref _performanceEvent, ev))
        {
            return false;
        }

        SetPerformanceEvent(type, ev);
        return true;
    }

    private bool SetRehearsalEventOnce(EventType type, StreamEvent ev)
    {
        if (!StreamEvent.AssignEventOnce(ref _rehearsalEvent, ev))
        {
            return false;
        }

        SetRehearsalEvent(type, ev);
        return true;
    }

    /// <summary>Updates the mark counters and forgets the timestep's events.</summary>
    public override void StopTranslationTimestep()
    {
        // Initialize segnoMarkCount to indicate that we are no longer at the
        // beginning.
        if (_firstTime)
        {
            Context?.SetProperty(CodaMarkCountSymbol, 0L);
            Context?.SetProperty(SegnoMarkCountSymbol, 0L);
        }

        // Update the counter for the chosen mark.  Those for segno and coda marks
        // are incremented at the end of the timestep so that there is no
        // inconsistency in value during iteration or translators' process_music ().
        //
        // The rehearsal mark count is handled differently to support its legacy
        // interface: the user may set the property directly rather than with \mark.
        switch (_performanceEventType)
        {
            case EventType.DefaultCodaMark:
            case EventType.SpecificCodaMark:
            {
                long label = GetCodaMarkLabel(Context, _performanceEvent);
                if (label > 0)
                {
                    Context?.SetProperty(CodaMarkCountSymbol, label);
                }

                break;
            }

            case EventType.DefaultSegnoMark:
            case EventType.SpecificSegnoMark:
            {
                long label = GetSegnoMarkLabel(Context, _performanceEvent);
                if (label > 0)
                {
                    Context?.SetProperty(SegnoMarkCountSymbol, label);
                }

                break;
            }

            default:
                break;
        }

        switch (_rehearsalEventType)
        {
            case EventType.DefaultRehearsalMark:
            case EventType.SpecificRehearsalMark:
            {
                long label = GetRehearsalMarkLabel(Context, _rehearsalEvent);
                if (label > 0)
                {
                    Context?.SetProperty(RehearsalMarkSymbol, label + 1);
                }

                break;
            }

            default:
                break;
        }

        ClearEvents();
        _firstTime = false;
    }

    /// <summary>
    /// Gets the label for a coda mark event during <c>process_music</c>. It may be
    /// specified in the event or come from the context.
    /// </summary>
    /// <param name="context">The context holding the counter.</param>
    /// <param name="ev">The coda mark event.</param>
    /// <returns>The label, or zero when there is none.</returns>
    public static long GetCodaMarkLabel(Context context, StreamEvent ev)
    {
        long n = TranslatorSchemeHelpers.ToLong(ev?.GetProperty(LabelSymbol), 0);
        if (n < 1)
        {
            n = TranslatorSchemeHelpers.ToLong(context?.GetProperty(CodaMarkCountSymbol), 0) + 1;
        }

        return n;
    }

    /// <summary>
    /// Gets the label for a rehearsal mark event during <c>process_music</c>. It may
    /// be specified in the event or come from the context.
    /// </summary>
    /// <param name="context">The context holding the counter.</param>
    /// <param name="ev">The rehearsal mark event.</param>
    /// <returns>The label, or zero when there is none.</returns>
    public static long GetRehearsalMarkLabel(Context context, StreamEvent ev)
    {
        long n = TranslatorSchemeHelpers.ToLong(ev?.GetProperty(LabelSymbol), 0);
        if (n < 1)
        {
            n = TranslatorSchemeHelpers.ToLong(context?.GetProperty(RehearsalMarkSymbol), 0);
        }

        return n;
    }

    /// <summary>
    /// Gets the label for a segno event during <c>process_music</c>. It may be
    /// specified in the event or come from the context.
    /// </summary>
    /// <param name="context">The context holding the counter.</param>
    /// <param name="ev">The segno mark event.</param>
    /// <returns>The label, or zero when there is none.</returns>
    public static long GetSegnoMarkLabel(Context context, StreamEvent ev)
    {
        long n = TranslatorSchemeHelpers.ToLong(ev?.GetProperty(LabelSymbol), 0);
        if (n < 1)
        {
            n = TranslatorSchemeHelpers.ToLong(context?.GetProperty(SegnoMarkCountSymbol), 0) + 1;
        }

        return n;
    }

    private void ListenAdHocMark(StreamEvent ev)
    {
        // Ad-hoc marks are not rehearsal marks, but they lead to the creation of
        // RehearsalMark grobs for backward compatibility, so this conflict check is
        // simple: complain about everything to incentivize using something
        // else, such as \sectionLabel, \jump, \textMark or \textEndMark.
        SetRehearsalEventOnce(EventType.AdHocMark, ev);
    }

    private void ListenCodaMark(StreamEvent ev)
    {
        object label = ev.GetProperty(LabelSymbol);
        if (!IsInteger(label)) // \codaMark \default
        {
            // Ignore a default coda mark at the beginning of a piece.  There is no
            // use case in mind here; this is merely for consistency with segni.
            if (!_firstTime)
            {
                switch (_performanceEventType)
                {
                    // Silently ignore default coda mark events after we have any coda
                    // mark event.
                    case EventType.DefaultCodaMark:
                    case EventType.SpecificCodaMark:
                        break;

                    // Check others.
                    default:
                        SetPerformanceEventOnce(EventType.DefaultCodaMark, ev);
                        break;
                }
            }
        }
        else // a specific coda mark
        {
            switch (_performanceEventType)
            {
                // Silently replace a default coda mark.
                case EventType.DefaultCodaMark:
                    SetPerformanceEvent(EventType.SpecificCodaMark, ev);
                    break;

                // Check others.
                default:
                    SetPerformanceEventOnce(EventType.SpecificCodaMark, ev);
                    break;
            }
        }
    }

    private void ListenRehearsalMark(StreamEvent ev)
    {
        object label = ev.GetProperty(LabelSymbol);
        if (!IsInteger(label)) // \mark \default
        {
            // Silently ignore default rehearsal mark events after we have any
            // rehearsal mark.
            switch (_rehearsalEventType)
            {
                case EventType.DefaultRehearsalMark:
                case EventType.SpecificRehearsalMark:
                    break;

                default:
                    SetRehearsalEventOnce(EventType.DefaultRehearsalMark, ev);
                    break;
            }
        }
        else // a specific mark
        {
            switch (_rehearsalEventType)
            {
                // Silently replace a default rehearsal mark.
                case EventType.DefaultRehearsalMark:
                    SetRehearsalEvent(EventType.SpecificRehearsalMark, ev);
                    break;

                // Check others.
                default:
                    SetRehearsalEventOnce(EventType.SpecificRehearsalMark, ev);
                    break;
            }
        }
    }

    private void ListenSectionLabel(StreamEvent ev)
        => SetPerformanceEventOnce(EventType.SectionLabel, ev);

    private void ListenSegnoMark(StreamEvent ev)
    {
        object label = ev.GetProperty(LabelSymbol);
        if (!IsInteger(label)) // \segnoMark \default
        {
            // Ignore a default segno at the beginning of a piece.
            if (!_firstTime)
            {
                switch (_performanceEventType)
                {
                    // Silently ignore default segno events after we have any segno
                    // event.
                    case EventType.DefaultSegnoMark:
                    case EventType.SpecificSegnoMark:
                        break;

                    // Check others.
                    default:
                        SetPerformanceEventOnce(EventType.DefaultSegnoMark, ev);
                        break;
                }
            }
        }
        else // a specific segno
        {
            switch (_performanceEventType)
            {
                // Silently replace a default segno.
                case EventType.DefaultSegnoMark:
                    SetPerformanceEvent(EventType.SpecificSegnoMark, ev);
                    break;

                // Check others.
                default:
                    SetPerformanceEventOnce(EventType.SpecificSegnoMark, ev);
                    break;
            }
        }
    }

    private static bool IsInteger(object value)
        => value is long || value is int || value is System.Numerics.BigInteger;
}

/**
   put stuff over or next to  bars.  Examples: bar numbers, marginal notes,
   rehearsal marks.
*/

/// <summary>
/// Creates rehearsal marks, segno and coda marks, and section labels.
/// <para>
/// <c>Mark_engraver</c> creates marks, formats them, and places them vertically
/// outside the set of staves given in the <c>stavesFound</c> context property.
/// </para>
/// <para>
/// By default, <c>Mark_engraver</c>s in multiple contexts create a common sequence of
/// marks chosen by the <c>Score</c>-level <see cref="MarkTrackingTranslator"/>. If
/// independent sequences are desired, multiple <c>Mark_tracking_translators</c> must
/// be used.
/// </para>
/// </summary>
public class MarkEngraver : Engraver
{
    private static readonly Symbol CurrentPerformanceMarkEventSymbol
        = Symbol.Intern("currentPerformanceMarkEvent");

    private static readonly Symbol CurrentRehearsalMarkEventSymbol
        = Symbol.Intern("currentRehearsalMarkEvent");

    private static readonly Symbol CodaMarkFormatterSymbol = Symbol.Intern("codaMarkFormatter");
    private static readonly Symbol SegnoMarkFormatterSymbol = Symbol.Intern("segnoMarkFormatter");
    private static readonly Symbol RehearsalMarkFormatterSymbol
        = Symbol.Intern("rehearsalMarkFormatter");

    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol BreakVisibilitySymbol = Symbol.Intern("break-visibility");
    private static readonly Symbol StavesFoundSymbol = Symbol.Intern("stavesFound");
    private static readonly Symbol SideSupportElementsSymbol
        = Symbol.Intern("side-support-elements");

    /// <summary>One of the two mark channels the engraver maintains.</summary>
    private sealed class MarkState
    {
        internal Item Text { get; set; }

        internal Item FinalText { get; set; }
    }

    private MarkState _performanceMarkState = new MarkState();
    private MarkState _rehearsalMarkState = new MarkState();
    private bool _firstTime = true;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public MarkEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Mark_engraver";

    /// <summary>Starts each timestep with fresh mark state.</summary>
    public override void StartTranslationTimestep()
    {
        _performanceMarkState = new MarkState();
        _rehearsalMarkState = new MarkState();
    }

    /// <summary>Finishes the timestep's marks.</summary>
    public override void StopTranslationTimestep()
    {
        void ProcessMark(MarkState mark)
        {
            if (mark.Text != null)
            {
                if (_firstTime)
                {
                    // A mark created at the very beginning is always visible even if
                    // it would not be visible at the beginning of a broken line.
                    mark.Text.SetProperty(
                        BreakVisibilitySymbol, new object[] { true, true, true });
                }

                mark.Text.SetObject(
                    SideSupportElementsSymbol,
                    TranslatorSchemeHelpers.GrobListToGrobArray(GetProperty(StavesFoundSymbol)));
                mark.FinalText = mark.Text;
                mark.Text = null;
            }
        }

        ProcessMark(_performanceMarkState);
        ProcessMark(_rehearsalMarkState);
        _firstTime = false;
    }

    /// <summary>Keeps a final mark visible at the very end of the music.</summary>
    public override void FinalizeTranslation()
    {
        void FinalizeMark(MarkState mark)
        {
            if (mark.FinalText != null)
            {
                // A mark created at the very end is always visible even if it would
                // not be visible at the end of a broken line.
                mark.FinalText.SetProperty(
                    BreakVisibilitySymbol, new object[] { true, true, true });
            }

            mark.FinalText = null;
        }

        FinalizeMark(_performanceMarkState);
        FinalizeMark(_rehearsalMarkState);
    }

    /// <summary>
    /// Gets the text property of the current performance mark in the given context
    /// (the empty list if there is no mark).
    /// </summary>
    /// <param name="context">The context to read from.</param>
    /// <returns>The markup, or the empty list.</returns>
    public static object GetCurrentPerformanceMarkText(Context context)
    {
        GetCurrentPerformanceMark(context, out _, out object text);
        return text;
    }

    private static object GetCurrentPerformanceMark(
        Context ctx, out string grobName, out object text)
    {
        grobName = null;
        text = Nil.Instance;

        // Get the event chosen by Mark_tracking_translator.
        object evScm = ctx?.GetProperty(CurrentPerformanceMarkEventSymbol);
        if (!(evScm is StreamEvent ev))
        {
            return Nil.Instance;
        }

        if (ev.IsInEventClass("coda-mark-event"))
        {
            grobName = "CodaMark";

            long label = MarkTrackingTranslator.GetCodaMarkLabel(ctx, ev);
            if (label > 0)
            {
                object proc = ctx.GetProperty(CodaMarkFormatterSymbol);
                if (SchemeUtilities.IsProcedure(proc))
                {
                    text = SchemeUtilities.CallCallback(proc, label, ctx);
                }
            }
        }
        else if (ev.IsInEventClass("section-label-event"))
        {
            grobName = "SectionLabel";

            text = ev.GetProperty(TextSymbol);
        }
        else if (ev.IsInEventClass("segno-mark-event"))
        {
            grobName = "SegnoMark";

            long label = MarkTrackingTranslator.GetSegnoMarkLabel(ctx, ev);
            if (label > 0)
            {
                object proc = ctx.GetProperty(SegnoMarkFormatterSymbol);
                if (SchemeUtilities.IsProcedure(proc))
                {
                    text = SchemeUtilities.CallCallback(proc, label, ctx);
                }
            }
        }

        return ev;
    }

    /// <summary>
    /// Gets the text property of the current rehearsal mark in the given context
    /// (the empty list if there is no mark).
    /// </summary>
    /// <param name="context">The context to read from.</param>
    /// <returns>The markup, or the empty list.</returns>
    public static object GetCurrentRehearsalMarkText(Context context)
    {
        GetCurrentRehearsalMark(context, out _, out object text);
        return text;
    }

    private static object GetCurrentRehearsalMark(
        Context ctx, out string grobName, out object text)
    {
        grobName = null;
        text = Nil.Instance;

        // Get the event chosen by Mark_tracking_translator.
        object evScm = ctx?.GetProperty(CurrentRehearsalMarkEventSymbol);
        if (!(evScm is StreamEvent ev))
        {
            return Nil.Instance;
        }

        if (ev.IsInEventClass("rehearsal-mark-event"))
        {
            grobName = "RehearsalMark";

            long label = MarkTrackingTranslator.GetRehearsalMarkLabel(ctx, ev);
            if (label > 0)
            {
                object proc = ctx.GetProperty(RehearsalMarkFormatterSymbol);
                if (SchemeUtilities.IsProcedure(proc))
                {
                    text = SchemeUtilities.CallCallback(proc, label, ctx);
                }
            }
        }
        else // ad-hoc-mark-event
        {
            grobName = "RehearsalMark";

            text = ev.GetProperty(TextSymbol);
        }

        return ev;
    }

    /// <summary>Makes the timestep's marks from what the tracker chose.</summary>
    public override void ProcessMusic()
    {
        void ProcessMark(
            MarkState mark,
            System.Func<Context, (object EventScm, string GrobName, object Text)> getCurrentMark)
        {
            if (mark.Text == null)
            {
                (object evScm, string grobName, object text) = getCurrentMark(Context);
                if (evScm is StreamEvent ev)
                {
                    mark.Text = MakeItem(grobName, ev);
                    if (Objects.TextInterface.IsMarkup(text))
                    {
                        mark.Text.SetProperty(TextSymbol, text);
                    }
                    else
                    {
                        TranslatorSchemeHelpers.EventWarning(ev, "mark label must be a markup object");
                    }
                }
            }
        }

        ProcessMark(
            _performanceMarkState,
            ctx =>
            {
                object ev = GetCurrentPerformanceMark(ctx, out string name, out object text);
                return (ev, name, text);
            });

        ProcessMark(
            _rehearsalMarkState,
            ctx =>
            {
                object ev = GetCurrentRehearsalMark(ctx, out string name, out object text);
                return (ev, name, text);
            });
    }
}

/// <summary>
/// Engraves metronome markings. This delegates the formatting work to the function in
/// the <c>metronomeMarkFormatter</c> property. The mark is put over all staves. The
/// staves are taken from the <c>stavesFound</c> property, which is maintained by
/// <c>Staff_collecting_engraver</c>.
/// </summary>
public class MetronomeMarkEngraver : Engraver
{
    private static readonly Symbol BreakAlignedInterfaceSymbol
        = Symbol.Intern("break-aligned-interface");

    private static readonly Symbol BreakAlignmentInterfaceSymbol
        = Symbol.Intern("break-alignment-interface");

    private static readonly Symbol BreakAlignSymbolSymbol = Symbol.Intern("break-align-symbol");
    private static readonly Symbol StaffBarSymbol = Symbol.Intern("staff-bar");
    private static readonly Symbol BreakAlignSymbolsSymbol = Symbol.Intern("break-align-symbols");
    private static readonly Symbol NonBreakAlignSymbolsSymbol
        = Symbol.Intern("non-break-align-symbols");

    private static readonly Symbol NonMusicalSymbol = Symbol.Intern("non-musical");
    private static readonly Symbol MultiMeasureRestInterfaceSymbol
        = Symbol.Intern("multi-measure-rest-interface");

    private static readonly Symbol CurrentMusicalColumnSymbol
        = Symbol.Intern("currentMusicalColumn");

    private static readonly Symbol CurrentCommandColumnSymbol
        = Symbol.Intern("currentCommandColumn");

    private static readonly Symbol SideSupportElementsSymbol
        = Symbol.Intern("side-support-elements");

    private static readonly Symbol StavesFoundSymbol = Symbol.Intern("stavesFound");
    private static readonly Symbol MetronomeMarkFormatterSymbol
        = Symbol.Intern("metronomeMarkFormatter");

    private static readonly Symbol TextSymbol = Symbol.Intern("text");

    private Item _text;
    private Grob _support;
    private Grob _bar;
    private StreamEvent _tempoEvent;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public MetronomeMarkEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Metronome_mark_engraver";

    /// <summary>Starts listening for tempo changes.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo("tempo-change-event", ListenTempoChange);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    private void ListenTempoChange(StreamEvent ev)
        => StreamEvent.AssignEventOnce(ref _tempoEvent, ev);

    private static bool SafeIsMember(object value, object list)
    {
        // ly_is_list, then scm_member with equal? semantics.
        object cursor = list;
        while (cursor is Pair pair)
        {
            if (CodeBrix.LilyScheme.Primitives.CorePrimitives.SchemeEqual(value, pair.Car))
            {
                return true;
            }

            cursor = pair.Cdr;
        }

        return false;
    }

    /// <summary>
    /// Finds the mark's supports: upstream's three acknowledgers
    /// (<c>break_aligned</c>, <c>break_alignment</c> and the catch-all <c>grob</c>)
    /// folded into the port's single virtual, filtered by interface in the same order.
    /// </summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        Grob g = info.Grob;

        if (g is Item && g.HasInterface(BreakAlignedInterfaceSymbol))
        {
            if (_text != null
                && ReferenceEquals(g.GetProperty(BreakAlignSymbolSymbol), StaffBarSymbol))
            {
                _bar = g;
            }
            else if (_text != null && _support == null
                     && SafeIsMember(
                         g.GetProperty(BreakAlignSymbolSymbol),
                         _text.GetProperty(BreakAlignSymbolsSymbol)))
            {
                _support = g;
                _text.XParent = g;
            }

            if (_bar != null || _support != null)
            {
                _text?.SetProperty(NonMusicalSymbol, true);
            }
        }

        if (g is Item && g.HasInterface(BreakAlignmentInterfaceSymbol))
        {
            if (_text != null && _support != null)
            {
                _text.XParent = g;
            }
        }

        // the catch-all acknowledge_grob
        if (_text != null)
        {
            object cursor = _text.GetProperty(NonBreakAlignSymbolsSymbol);
            while (cursor is Pair pair)
            {
                if (pair.Car is Symbol interfaceName && g.HasInterface(interfaceName))
                {
                    _text.XParent = g;
                }

                cursor = pair.Cdr;
            }
        }
    }

    /// <summary>Aligns and finishes the timestep's metronome mark.</summary>
    public override void StopTranslationTimestep()
    {
        if (_text != null)
        {
            if (_text.XParent != null
                && _text.XParent.HasInterface(MultiMeasureRestInterfaceSymbol)
                && _bar != null)
            {
                _text.XParent = _bar;
            }
            else if (_support == null)
            {
                /*
                  Gardner Read "Music Notation", p.278

                  Align the metronome mark over the time signature (or the
                  first notational element of the measure if no time
                  signature is present in that measure).
                */
                if (GetProperty(CurrentMusicalColumnSymbol) is Grob mc)
                {
                    _text.XParent = mc;
                }
                else if (GetProperty(CurrentCommandColumnSymbol) is Grob cc)
                {
                    _text.XParent = cc;
                }
            }

            _text.SetObject(
                SideSupportElementsSymbol,
                TranslatorSchemeHelpers.GrobListToGrobArray(GetProperty(StavesFoundSymbol)));
            _text = null;
            _support = null;
            _bar = null;
            _tempoEvent = null;
        }
    }

    /// <summary>Makes the <c>MetronomeMark</c> for a heard tempo change.</summary>
    public override void ProcessMusic()
    {
        if (_tempoEvent != null)
        {
            _text = MakeItem("MetronomeMark", _tempoEvent);

            object proc = GetProperty(MetronomeMarkFormatterSymbol);
            object result = SchemeUtilities.CallCallback(proc, _tempoEvent, Context);

            _text.SetProperty(TextSymbol, result);
        }
    }
}

/**
   Create marks such as "D.C. al Fine" outside the system.
*/

/// <summary>
/// Creates instructions such as <em>D.C.</em> and <em>Fine</em>, placing them
/// vertically outside the set of staves given in the <c>stavesFound</c> context
/// property.
/// </summary>
public class JumpEngraver : Engraver
{
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol SegnoMarkCountSymbol = Symbol.Intern("segnoMarkCount");
    private static readonly Symbol SegnoMarkFormatterSymbol = Symbol.Intern("segnoMarkFormatter");
    private static readonly Symbol AlternativeNumberSymbol = Symbol.Intern("alternative-number");
    private static readonly Symbol CodaMarkCountSymbol = Symbol.Intern("codaMarkCount");
    private static readonly Symbol CodaMarkFormatterSymbol = Symbol.Intern("codaMarkFormatter");
    private static readonly Symbol FineTextSymbol = Symbol.Intern("fineText");
    private static readonly Symbol DalSegnoTextFormatterSymbol
        = Symbol.Intern("dalSegnoTextFormatter");

    private static readonly Symbol ReturnCountSymbol = Symbol.Intern("return-count");
    private static readonly Symbol FinalFineTextVisibilitySymbol
        = Symbol.Intern("finalFineTextVisibility");

    private static readonly Symbol StavesFoundSymbol = Symbol.Intern("stavesFound");
    private static readonly Symbol SideSupportElementsSymbol
        = Symbol.Intern("side-support-elements");

    // Upstream declares `bool first_time_ = true;` and assigns it in
    // stop_translation_timestep without ever reading it. The port drops the field:
    // an assigned-but-unread private field is compiler warning CS0414, and 0 warnings
    // is a hard gate. Recorded in PORT-COVERAGE.
    private bool _printedFine;
    private bool _finalFineTextVisibility;
    private Item _adHocJumpText;
    private Item _dsText;
    private Item _fineText;
    private StreamEvent _adHocJumpEvent;
    private StreamEvent _dsEvent;
    private StreamEvent _fineEvent;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public JumpEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Jump_engraver";

    /// <summary>Starts listening for the jump event classes.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo("ad-hoc-jump-event", ListenAdHocJump);
        ListenTo("dal-segno-event", ListenDalSegno);
        ListenTo("fine-event", ListenFine);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Releases the previous timestep's Fine text.</summary>
    public override void StartTranslationTimestep() => _fineText = null;

    private void ListenAdHocJump(StreamEvent ev)
        => StreamEvent.AssignEventOnce(ref _adHocJumpEvent, ev);

    private void ListenDalSegno(StreamEvent ev) => StreamEvent.AssignEventOnce(ref _dsEvent, ev);

    private void ListenFine(StreamEvent ev) => StreamEvent.AssignEventOnce(ref _fineEvent, ev);

    /// <summary>Makes the timestep's <c>JumpScript</c> grobs.</summary>
    public override void ProcessMusic()
    {
        if (_adHocJumpEvent != null)
        {
            _adHocJumpText = MakeItem("JumpScript", _adHocJumpEvent);

            object m = _adHocJumpEvent.GetProperty(TextSymbol);
            if (Objects.TextInterface.IsMarkup(m))
            {
                _adHocJumpText.SetProperty(TextSymbol, m);
            }
            else
            {
                TranslatorSchemeHelpers.EventWarning(
                    _adHocJumpEvent, "jump text must be a markup object");
            }
        }

        if (_dsEvent != null)
        {
            _dsText = MakeItem("JumpScript", _dsEvent);

            // We indicate D.S. to the most recent segno mark.  This would not be
            // correct for nested segno repeats, but we don't care to support those.
            object bodyStartMarkup = false; // D.C.
            long segnoCount = TranslatorSchemeHelpers.ToLong(GetProperty(SegnoMarkCountSymbol), 0);
            if (segnoCount > 0)
            {
                object proc = GetProperty(SegnoMarkFormatterSymbol);
                if (SchemeUtilities.IsProcedure(proc))
                {
                    bodyStartMarkup = SchemeUtilities.CallCallback(proc, segnoCount, Context);
                }
            }

            object bodyEndMarkup = false;
            object nextMarkup = false;
            long altNum = TranslatorSchemeHelpers.ToLong(_dsEvent.GetProperty(AlternativeNumberSymbol), 0);
            if (altNum > 0)
            {
                // Assuming that the coda marks of the current group of alternatives
                // are sequential, we compute the sequence number of the first one.
                long codaMarkCount = TranslatorSchemeHelpers.ToLong(GetProperty(CodaMarkCountSymbol), 0);
                codaMarkCount -= altNum - 1;
                object proc = GetProperty(CodaMarkFormatterSymbol);
                if (SchemeUtilities.IsProcedure(proc))
                {
                    bodyEndMarkup = SchemeUtilities.CallCallback(proc, codaMarkCount, Context);
                }

                nextMarkup = MarkEngraver.GetCurrentPerformanceMarkText(Context);

                // Mark_engraver may return SCM_EOL like a failed property lookup,
                // but our formatter expects either markup or SCM_BOOL_F.
                if (nextMarkup is Nil)
                {
                    nextMarkup = false;
                }
            }

            if (nextMarkup is bool nextFlag && !nextFlag && _printedFine)
            {
                // Print "al Fine" if there was a "Fine" at any prior point.  This
                // heuristic might not be correct in scores with multiple segno
                // repeats, but we don't care enough to complicate this.
                bodyEndMarkup = GetProperty(FineTextSymbol);
            }

            object m = Nil.Instance;
            object formatter = GetProperty(DalSegnoTextFormatterSymbol);
            if (SchemeUtilities.IsProcedure(formatter))
            {
                long count = TranslatorSchemeHelpers.ToLong(_dsEvent.GetProperty(ReturnCountSymbol), 1);
                m = SchemeUtilities.CallCallback(
                    formatter,
                    Context,
                    count,
                    new Pair(
                        bodyStartMarkup,
                        new Pair(bodyEndMarkup, new Pair(nextMarkup, Nil.Instance))));
            }

            if (Objects.TextInterface.IsMarkup(m))
            {
                _dsText.SetProperty(TextSymbol, m);
            }
            else
            {
                TranslatorSchemeHelpers.EventWarning(_dsEvent, "jump text must be a markup object");
            }
        }

        if (_fineEvent != null)
        {
            _fineText = MakeItem("JumpScript", _fineEvent);

            object m = GetProperty(FineTextSymbol);
            if (Objects.TextInterface.IsMarkup(m))
            {
                _fineText.SetProperty(TextSymbol, m);
            }
            else
            {
                TranslatorSchemeHelpers.EventWarning(_fineEvent, "jump text must be a markup object");
            }

            // We don't know yet whether this is the last timestep, but if it is, we
            // will need to honor finalFineTextVisibility.
            _finalFineTextVisibility
                = TranslatorSchemeHelpers.ToBool(GetProperty(FinalFineTextVisibilitySymbol));
        }
    }

    /// <summary>Finishes the timestep's jump texts.</summary>
    public override void StopTranslationTimestep()
    {
        object stavesFound = null;
        foreach (Item text in new[] { _dsText, _fineText, _adHocJumpText })
        {
            if (text != null)
            {
                if (stavesFound == null)
                {
                    stavesFound = GetProperty(StavesFoundSymbol);
                }

                text.SetObject(
                    SideSupportElementsSymbol,
                    TranslatorSchemeHelpers.GrobListToGrobArray(stavesFound));
            }
        }

        if (_fineEvent != null)
        {
            _printedFine = true;
        }

        _dsEvent = null;
        _dsText = null;
        _fineEvent = null;
        _adHocJumpEvent = null;
        _adHocJumpText = null;
    }

    /// <summary>Suppresses a <c>Fine</c> at the written end of the music.</summary>
    public override void FinalizeTranslation()
    {
        // By default, avoid printing "Fine" at the written end of the music.
        // These cases are noteworthy:
        //
        // * Repeats have been unfolded.  No other repeat notation remains, so
        //   leaving "Fine" would look strange.
        //
        // * It is more convenient to code an optionally unfoldable piece as
        //       \repeat volta 2 { ... } \fine
        //   than
        //       \repeat volta 2 { ... \volta 2 \unfolded \bar "|." }
        if (_fineText != null && !_finalFineTextVisibility)
        {
            _fineText.Suicide();
            _fineText = null;
        }
    }
}
