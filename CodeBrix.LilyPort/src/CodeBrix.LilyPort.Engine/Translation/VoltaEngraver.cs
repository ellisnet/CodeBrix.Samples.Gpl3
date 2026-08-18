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
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/volta-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - Volta_layer is a private nested class rather than a file-scope one, and the
//     Preinit base that exists only to order C++ member construction is dropped: the
//     layer list is initialised in the constructor, which is what Preinit achieves.

/// <summary>
/// Creates volta spanners by reading the <c>repeatCommands</c> property — usually set by
/// <see cref="VoltaRepeatIterator"/> through <see cref="RepeatAcknowledgeEngraver"/> — and
/// the <c>VoltaSpanEvent</c>s that <see cref="VoltaSpeccedMusicIterator"/> announces.
/// </summary>
public class VoltaEngraver : Engraver
{
    private static readonly Symbol BarLineInterfaceSymbol = Symbol.Intern("bar-line-interface");
    private static readonly Symbol CurrentCommandColumnSymbol
        = Symbol.Intern("currentCommandColumn");

    private static readonly Symbol DalSegnoEventSymbol = Symbol.Intern("dal-segno-event");
    private static readonly Symbol EdgeHeightSymbol = Symbol.Intern("edge-height");
    private static readonly Symbol FineEventSymbol = Symbol.Intern("fine-event");
    private static readonly Symbol GlyphLeftSymbol = Symbol.Intern("glyph-left");
    private static readonly Symbol MusicalLengthSymbol = Symbol.Intern("musical-length");
    private static readonly Symbol OutsideStaffPrioritySymbol
        = Symbol.Intern("outside-staff-priority");

    private static readonly Symbol PrintTrivialVoltaRepeatsSymbol
        = Symbol.Intern("printTrivialVoltaRepeats");

    private static readonly Symbol RepeatCommandsSymbol = Symbol.Intern("repeatCommands");
    private static readonly Symbol RepeatCountSymbol = Symbol.Intern("repeat-count");
    private static readonly Symbol SpanDirectionSymbol = Symbol.Intern("span-direction");
    private static readonly Symbol StavesFoundSymbol = Symbol.Intern("stavesFound");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol VoltaBracketCalcHookVisibilitySymbol
        = Symbol.Intern("volta-bracket::calc-hook-visibility");

    private static readonly Symbol VoltaBracketMusicalLengthSymbol
        = Symbol.Intern("voltaBracketMusicalLength");

    private static readonly Symbol VoltaDepthSymbol = Symbol.Intern("volta-depth");
    private static readonly Symbol VoltaNumbersSymbol = Symbol.Intern("volta-numbers");
    private static readonly Symbol VoltaSpanEventSymbol = Symbol.Intern("volta-span-event");
    private static readonly Symbol VoltaSymbol = Symbol.Intern("volta");

    // Entry [n] pertains to volta spans in the nth-deep folded repeat. [0] is used if
    // \volta appears at the top level, which is not expected, but is easily written.
    private readonly List<VoltaLayer> _layers = new List<VoltaLayer>();

    private bool _acknowledgedBarLine;
    private bool _shouldCloseEnd;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public VoltaEngraver(Context context)
        : base(context)
    {
        // We need at least one layer to support manual repeat commands. Others may be
        // created as needed.
        _layers.Add(new VoltaLayer());
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Volta_engraver";

    /// <summary>Starts listening for the three event classes.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(DalSegnoEventSymbol, ListenDalSegno);
        ListenTo(FineEventSymbol, ListenFine);
        ListenTo(VoltaSpanEventSymbol, ListenVoltaSpan);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Opens and closes the bracket of every layer.</summary>
    public override void ProcessMusic()
    {
        for (int layerNo = 0; layerNo < _layers.Count; ++layerNo)
        {
            VoltaLayer layer = _layers[layerNo];

            // Ignore events for trivial repeats (when configured).
            bool printTrivial
                = GetProperty(PrintTrivialVoltaRepeatsSymbol) is bool trivial && trivial;

            if (IsTrivial(layer.StartEvent, printTrivial))
            {
                layer.StartEvent = null;
            }

            if (IsTrivial(layer.StopPreviousEvent, printTrivial))
            {
                layer.StopPreviousEvent = null;
            }

            if (IsTrivial(layer.StopCurrentEvent, printTrivial))
            {
                layer.StopCurrentEvent = null;
            }

            bool manualStart = false;
            bool manualEnd = false;

            if (layerNo == 0) // manual repeat commands
            {
                for (object cursor = GetProperty(RepeatCommandsSymbol);
                     cursor is Pair pair;
                     cursor = pair.Cdr)
                {
                    if (pair.Car is Pair command
                        && ReferenceEquals(command.Car, VoltaSymbol)
                        && command.Cdr is Pair rest)
                    {
                        object label = rest.Car;
                        if (label is bool flag && !flag)
                        {
                            manualEnd = true;
                        }
                        else
                        {
                            manualStart = true;
                            layer.Text = label;
                        }
                    }
                }
            }

            bool end = manualEnd || layer.StopPreviousEvent != null;
            if (!end && layer.Bracket != null)
            {
                if (layer.StopMoment < Moment.Infinity)
                {
                    // VoltaBracket.musical-length was specified. Check it and disregard
                    // voltaBracketMusicalLength.
                    end = NowMoment >= layer.StopMoment;
                }
                else
                {
                    Moment voltaBracketMusicalLength = TranslatorSchemeHelpers.ToMoment(
                        GetProperty(VoltaBracketMusicalLengthSymbol), Moment.Infinity);

                    end = voltaBracketMusicalLength <= NowMoment - layer.StartMoment;
                }
            }

            layer.StartBracketThisTimestep = ShouldStartBracket(layer, manualStart, end);

            if (layer.StartBracketThisTimestep && layer.Bracket != null && !end)
            {
                layer.Bracket.Warning("already have a VoltaBracket; ending it prematurely");
                end = true;
            }

            if (end)
            {
                if (layer.Bracket != null)
                {
                    layer.EndBracket = layer.Bracket;
                    layer.Bracket = null;
                }
                else if (manualEnd)
                {
                    Warn.Warning("no VoltaBracket to end");
                }
            }

            if (layer.StartBracketThisTimestep)
            {
                layer.StartMoment = NowMoment;
                layer.StopMoment = Moment.Infinity;
                object cause = layer.StartEvent ?? (object)Nil.Instance;
                layer.Bracket = MakeSpanner("VoltaBracket", cause);

                if (layer.Spanner == null)
                {
                    layer.Spanner = MakeSpanner("VoltaBracketSpanner", cause);

                    // Set the vertical order of the layers by adjusting
                    // outside-staff-priority.
                    if (layerNo != 0)
                    {
                        object priority = layer.Spanner.GetProperty(OutsideStaffPrioritySymbol);
                        if (SchemeConvert.IsNumber(priority))
                        {
                            layer.Spanner.SetProperty(
                                OutsideStaffPrioritySymbol,
                                SchemeConvert.ToDouble(priority, "volta-engraver") - layerNo);
                        }
                    }
                }

                AxisGroupInterface.AddElement(layer.Spanner, layer.Bracket);
            }
        }
    }

    /// <summary>Binds the brackets to bar lines and decides whether the end hooks down.</summary>
    /// <param name="info">The announced grob.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!(info.Grob is Item item) || !item.HasInterface(BarLineInterfaceSymbol))
        {
            return;
        }

        _acknowledgedBarLine = true;

        foreach (VoltaLayer layer in _layers)
        {
            if (layer.Bracket != null)
            {
                VoltaBracketInterface.AddBar(layer.Bracket, item, Direction.Negative);
            }

            if (layer.EndBracket != null)
            {
                VoltaBracketInterface.AddBar(layer.EndBracket, item, Direction.Positive);
            }

            if (layer.Spanner != null)
            {
                SidePositionInterface.AddSupport(layer.Spanner, item);
            }
        }

        // Certain bar lines cause volta brackets to hook down at the end. See the
        // function allow-volta-hook in bar-line.scm.
        if (!_shouldCloseEnd)
        {
            object glyph = item.GetProperty(GlyphLeftSymbol);
            object visible = CallHookVisibility(glyph);
            _shouldCloseEnd = !(visible is bool hook && hook);
        }
    }

    /// <summary>Clears the per-timestep flags.</summary>
    public override void StartTranslationTimestep()
    {
        _acknowledgedBarLine = false;
        _shouldCloseEnd = false;
    }

    /// <summary>Finishes the brackets: bounds, labels, hooks, and the end announcement.</summary>
    public override void StopTranslationTimestep()
    {
        Item ci = GetProperty(CurrentCommandColumnSymbol) as Item;

        foreach (VoltaLayer layer in _layers)
        {
            if (layer.StartBracketThisTimestep)
            {
                layer.StopMoment = layer.StartMoment
                    + TranslatorSchemeHelpers.ToMoment(
                        layer.Bracket.GetProperty(MusicalLengthSymbol), Moment.Infinity);

                // Cancel the bracket if it will not end during a future timestep.
                if (layer.StopMoment <= layer.StartMoment)
                {
                    layer.Bracket.Suicide();
                    layer.Bracket = null;
                    layer.StartBracketThisTimestep = false;
                }
                else if (layer.StopMoment < Moment.Infinity)
                {
                    // VoltaBracket.musical-length is valid; use it.
                    Context?.GlobalContext?.AddMomentToProcess(layer.StopMoment);
                }
                else if (layer.StopCurrentEvent != null)
                {
                    // musical-length is unlimited and the current alternative is empty.
                    layer.Bracket.Suicide();
                    layer.Bracket = null;
                    layer.StartBracketThisTimestep = false;
                }
            }

            if (layer.StartBracketThisTimestep)
            {
                if (!(layer.Text is Nil)) // explicit label from repeatCommands
                {
                    layer.Bracket.SetProperty(TextSymbol, layer.Text);
                }

                if (layer.StartEvent != null)
                {
                    layer.Bracket.SetProperty(
                        VoltaNumbersSymbol, layer.StartEvent.GetProperty(VoltaNumbersSymbol));
                }

                layer.StartBracketThisTimestep = false;
            }

            if (layer.EndBracket != null)
            {
                if (!_acknowledgedBarLine)
                {
                    Spanner.AddBoundItem(layer.EndBracket, ci);
                }

                layer.Spanner?.SetBound(
                    Direction.Positive, layer.EndBracket.GetBound(Direction.Positive));

                if (layer.Bracket == null)
                {
                    for (object cursor = GetProperty(StavesFoundSymbol);
                         cursor is Pair pair;
                         cursor = pair.Cdr)
                    {
                        if (pair.Car is Grob staff && layer.Spanner != null)
                        {
                            SidePositionInterface.AddSupport(layer.Spanner, staff);
                        }
                    }

                    layer.Spanner = null;
                }

                if (!_shouldCloseEnd)
                {
                    DrulArray<double> edgeHeight = SchemeConvert.ToDrulDouble(
                        layer.EndBracket.GetProperty(EdgeHeightSymbol),
                        new DrulArray<double>(2.0, 2.0));

                    if (edgeHeight[Direction.Positive] != 0.0)
                    {
                        edgeHeight[Direction.Positive] = 0.0;
                        layer.EndBracket.SetProperty(
                            EdgeHeightSymbol,
                            new Pair(
                                edgeHeight[Direction.Negative],
                                edgeHeight[Direction.Positive]));
                    }
                }

                AnnounceEndGrob(layer.EndBracket, Nil.Instance);
                layer.EndBracket = null;
            }

            if (layer.Bracket != null && layer.Bracket.GetBound(Direction.Negative) == null)
            {
                layer.Bracket.SetBound(Direction.Negative, ci);
            }

            if (layer.Spanner != null
                && layer.Bracket != null
                && layer.Spanner.GetBound(Direction.Negative) == null)
            {
                layer.Spanner.SetBound(
                    Direction.Negative, layer.Bracket.GetBound(Direction.Negative));
            }

            layer.StartEvent = null;
            layer.StopPreviousEvent = null;
            layer.StopCurrentEvent = null;
            layer.Text = Nil.Instance;
        }
    }

    /// <summary>Drops every layer.</summary>
    public override void FinalizeTranslation()
    {
        _layers.Clear();
        base.FinalizeTranslation();
    }

    private static bool AreVoltaNumbersEqual(StreamEvent a, StreamEvent b)
        => SchemeUtilities.IsEqual(
            a?.GetProperty(VoltaNumbersSymbol), b?.GetProperty(VoltaNumbersSymbol));

    private static object CallHookVisibility(object glyph)
    {
        object procedure
            = LilyPondScheme.LookupProcedure(VoltaBracketCalcHookVisibilitySymbol);

        Interpreter interpreter = LilyPondScheme.Current;
        if (procedure == null || interpreter == null)
        {
            Warn.ProgrammingError("volta-bracket::calc-hook-visibility is not available");

            // Upstream would have a value here; answering "visible" keeps the end open,
            // which is the shape a bracket has when nothing asked it to hook.
            return true;
        }

        return interpreter.Evaluator.Apply(procedure, new object[] { glyph });
    }

    private bool IsTrivial(StreamEvent ev, bool printTrivial)
    {
        if (ev == null)
        {
            return false;
        }

        long repeatCount = TranslatorSchemeHelpers.ToLong(ev.GetProperty(RepeatCountSymbol), 1);
        return repeatCount < 2 && !printTrivial;
    }

    private bool ShouldStartBracket(VoltaLayer layer, bool manualStart, bool end)
    {
        if (!(manualStart || layer.StartEvent != null))
        {
            return false; // We have no reason to start a bracket.
        }

        // If there is no current bracket, or if the current bracket will stop here, we
        // can start a new one without trouble.
        if (layer.Bracket == null || end)
        {
            return true;
        }

        // We have reason to start a new bracket, but no clear reason to stop the current
        // one. Differences in grace notes among elements of simultaneous music cause
        // volta events to be announced in more than one time step (issue #34). Do not
        // split the current bracket just for this; let it continue.
        if (layer.StartMoment.MainPart == NowMoment.MainPart && layer.StartEvent != null)
        {
            // Compare volta specs to gain confidence that we are handling grace
            // synchronization rather than incongruous repeat structure.
            //
            // Manually created (via repeatCommands) brackets do not have volta numbers:
            // they have only markup. We refrain from trying to fix them blindly.
            StreamEvent previousEvent = layer.Bracket.EventCause();
            if (previousEvent != null && AreVoltaNumbersEqual(previousEvent, layer.StartEvent))
            {
                // The volta numbers are the same as for the current bracket. We're
                // probably seeing issue #34: don't start a new bracket.
                return false;
            }
        }

        return true;
    }

    private void ListenDalSegno(StreamEvent ev) => _shouldCloseEnd = true;

    private void ListenFine(StreamEvent ev) => _shouldCloseEnd = true;

    private void ListenVoltaSpan(StreamEvent ev)
    {
        long layerNo = TranslatorSchemeHelpers.ToLong(ev.GetProperty(VoltaDepthSymbol), 0);
        if (layerNo < 0)
        {
            layerNo = 0;
        }

        while (layerNo >= _layers.Count)
        {
            _layers.Add(new VoltaLayer());
        }

        VoltaLayer layer = _layers[(int)layerNo];

        // It is common to have the same repeat structure in multiple voices, so we ignore
        // simultaneous events; but it is nice to perform some consistency checks to catch
        // likely errors and improve the user's debugging experience.
        Direction dir = DirectionalElementInterface.FromScheme(
            ev.GetProperty(SpanDirectionSymbol), Direction.Center);

        if (dir == Direction.Negative)
        {
            if (layer.StartEvent == null)
            {
                layer.StartEvent = ev;
            }
            else if (!AreVoltaNumbersEqual(layer.StartEvent, ev))
            {
                if (layer.StopCurrentEvent != null)
                {
                    // We previously observed a zero-duration volta span, but now we are
                    // starting a new span. Discard the old one. The reason we didn't
                    // discard it on receipt of the stop event is so that a final
                    // zero-duration span can be forced to appear by overriding
                    // VoltaBracket.musical-length.
                    layer.StartEvent = ev;
                    layer.StopCurrentEvent = null;
                }
                else
                {
                    // Include the volta numbers in the message because they might not be
                    // obvious if the source has the legacy \alternative syntax with
                    // implied \volta.
                    //was previously: SchemeUtilities.RobustSymbolToString(..., "?").
                    // `volta-numbers' is a LIST, and a symbol reader answers its fallback
                    // for one — so the message read "...: ?" and `ly:expect-warning' could
                    // not match it, which is both halves of
                    // volta-bracket-warning-start-conflicting-numbers' diagnostics row.
                    // Upstream writes ly_scm_write_string, which is scm_write to a string
                    // port, which is Printer.Write.
                    TranslatorSchemeHelpers.EventWarning(
                        ev,
                        "discarding conflicting volta numbers: "
                        + Printer.Write(ev.GetProperty(VoltaNumbersSymbol)));
                }
            }
        }
        else if (dir == Direction.Positive)
        {
            if (layer.StartEvent != null && AreVoltaNumbersEqual(layer.StartEvent, ev))
            {
                // A simultaneous start and stop with the same volta spec can be generated
                // by a zero-duration alternative.
                layer.StopCurrentEvent = ev;
            }
            else if (layer.StopPreviousEvent == null)
            {
                // If there is a current bracket which was created by an event, ignore
                // unrelated stop events, which are recognized by a difference in their
                // list of volta numbers.
                //
                // Unrelated stop events might arise from differences in grace notes among
                // elements of simultaneous music (issue #34). In this case, at the
                // earliest grace time, we expect the current bracket to end. Any events
                // for that ended bracket which arrive at a later grace time are quietly
                // ignored rather than allowed to end a different bracket.
                bool acceptEvent;
                if (layer.Bracket == null)
                {
                    acceptEvent = true; // lacking information required to filter
                }
                else
                {
                    StreamEvent bracketStartEvent = layer.Bracket.EventCause();
                    acceptEvent = bracketStartEvent == null
                        || AreVoltaNumbersEqual(bracketStartEvent, ev);
                }

                if (acceptEvent)
                {
                    layer.StopPreviousEvent = ev;
                }
            }
        }
        else
        {
            TranslatorSchemeHelpers.EventProgrammingError(
                ev, "invalid direction of volta-span-event");
        }
    }

    // State pertaining to volta spans at a specific depth of nested folded repeats.
    private sealed class VoltaLayer
    {
        public StreamEvent StartEvent { get; set; }

        public StreamEvent StopPreviousEvent { get; set; }

        // To handle an empty bracket.
        public StreamEvent StopCurrentEvent { get; set; }

        public Moment StartMoment { get; set; }

        public Moment StopMoment { get; set; }

        public Spanner Bracket { get; set; }

        public Spanner EndBracket { get; set; }

        public Spanner Spanner { get; set; }

        public object Text { get; set; } = Nil.Instance;

        public bool StartBracketThisTimestep { get; set; }
    }
}
