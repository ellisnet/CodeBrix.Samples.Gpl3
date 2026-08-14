/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/fingering-engraver.cc, lily/new-fingering-engraver.cc, lily/fingering-column-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - the three fingering engravers share a file.
//   - upstream's Finger_tuple has an operator< on `position_`; the port sorts with an
//     explicit stable insertion sort, because std::sort is unstable and equal staff
//     positions decide which fingering goes up and which goes down when a chord has to be
//     split — an unstable tie there is a coin flip the port cannot reproduce.

/// <summary>
/// Creates fingering scripts.
/// </summary>
public class FingeringEngraver : Engraver
{
    private static readonly Symbol FingeringEventSymbol = Symbol.Intern("fingering-event");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol ScriptPrioritySymbol = Symbol.Intern("script-priority");
    private static readonly Symbol XAlignOnMainNoteheadsSymbol
        = Symbol.Intern("X-align-on-main-noteheads");
    private static readonly Symbol RhythmicHeadInterface = Symbol.Intern("rhythmic-head-interface");
    private static readonly Symbol StemInterface = Symbol.Intern("stem-interface");
    private static readonly Symbol FlagInterface = Symbol.Intern("flag-interface");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");

    private readonly List<StreamEvent> _events = new List<StreamEvent>();
    private readonly List<Item> _fingerings = new List<Item>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public FingeringEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Fingering_engraver";

    /// <summary>Starts listening for fingering events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(FingeringEventSymbol, ev => _events.Add(ev));
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Makes one fingering script per event, in reverse input order.</summary>
    public override void ProcessMusic()
    {
        for (int i = _events.Count; i-- > 0;)
        {
            object dir = _events[i].GetProperty(DirectionSymbol);
            MakeScript(
                DirectionalElementInterface.FromScheme(dir, Direction.Center), _events[i], i);
        }
    }

    /// <summary>Hangs the fingerings off the heads, stem and flag they must clear.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        // Upstream's registration order: rhythmic_head, stem, flag, note_column.
        if (info.Grob.HasInterface(RhythmicHeadInterface)
            || info.Grob.HasInterface(StemInterface)
            || info.Grob.HasInterface(FlagInterface))
        {
            for (int i = 0; i < _fingerings.Count; i++)
            {
                SidePositionInterface.AddSupport(_fingerings[i], info.Grob);
            }
        }

        if (info.Grob.HasInterface(NoteColumnInterface) && info.Grob is Item column)
        {
            /* set NoteColumn as parent */
            /* and X-align on main noteheads */
            for (int i = 0; i < _fingerings.Count; i++)
            {
                Grob t = _fingerings[i];
                t.XParent = column;
                t.SetProperty(XAlignOnMainNoteheadsSymbol, true);
            }
        }
    }

    /// <summary>Drops this timestep's fingerings and events.</summary>
    public override void StopTranslationTimestep()
    {
        _fingerings.Clear();
        _events.Clear();
    }

    private void MakeScript(Direction d, StreamEvent r, int i)
    {
        Item fingering = MakeItem("Fingering", r);

        /*
          We can't fold these definitions into define-grobs since
          fingerings for chords need different settings.
        */
        SidePositionInterface.SetAxis(fingering, Axis.Y);
        SelfAlignmentInterface.SetAlignedOnParent(fingering, Axis.X);

        /* See script-engraver.cc */
        object priority = fingering.GetProperty(ScriptPrioritySymbol);
        long value = SchemeConvert.IsNumber(priority)
            ? SchemeConvert.ToLong(priority, "script-priority")
            : 200; // TODO: Explain magic.
        fingering.SetProperty(ScriptPrioritySymbol, value + i);

        if (d != Direction.Center)
        {
            fingering.SetProperty(DirectionSymbol, (long)(int)d);
        }
        else if (!DirectionalElementInterface.IsDirection(
                     fingering.GetPropertyData(DirectionSymbol)))
        {
            fingering.SetProperty(DirectionSymbol, (long)(int)Direction.Positive);
        }

        _fingerings.Add(fingering);
    }
}

/// <summary>
/// Creates fingering scripts for notes in a new chord. This engraver is ill-named,
/// since it also takes care of articulations and harmonic note heads.
/// </summary>
public class NewFingeringEngraver : Engraver
{
    private static readonly Symbol ArticulationsSymbol = Symbol.Intern("articulations");
    private static readonly Symbol FingeringEventSymbol = Symbol.Intern("fingering-event");
    private static readonly Symbol TextScriptEventSymbol = Symbol.Intern("text-script-event");
    private static readonly Symbol ScriptEventSymbol = Symbol.Intern("script-event");
    private static readonly Symbol StringNumberEventSymbol = Symbol.Intern("string-number-event");
    private static readonly Symbol StrokeFingerEventSymbol = Symbol.Intern("stroke-finger-event");
    private static readonly Symbol HarmonicEventSymbol = Symbol.Intern("harmonic-event");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol HarmonicSymbol = Symbol.Intern("harmonic");
    private static readonly Symbol DotSymbol = Symbol.Intern("dot");
    private static readonly Symbol HarmonicDotsSymbol = Symbol.Intern("harmonicDots");
    private static readonly Symbol ArticulationTypeSymbol = Symbol.Intern("articulation-type");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol FlagSymbol = Symbol.Intern("flag");
    private static readonly Symbol UpSymbol = Symbol.Intern("up");
    private static readonly Symbol DownSymbol = Symbol.Intern("down");
    private static readonly Symbol LeftSymbol = Symbol.Intern("left");
    private static readonly Symbol RightSymbol = Symbol.Intern("right");
    private static readonly Symbol AvoidSlurSymbol = Symbol.Intern("avoid-slur");
    private static readonly Symbol InsideSymbol = Symbol.Intern("inside");
    private static readonly Symbol AccidentalGrobSymbol = Symbol.Intern("accidental-grob");
    private static readonly Symbol StencilSymbol = Symbol.Intern("stencil");
    private static readonly Symbol YOffsetSymbol = Symbol.Intern("Y-offset");
    private static readonly Symbol ScriptPrioritySymbol = Symbol.Intern("script-priority");
    private static readonly Symbol XAlignOnMainNoteheadsSymbol
        = Symbol.Intern("X-align-on-main-noteheads");
    private static readonly Symbol SideRelativeDirectionSymbol
        = Symbol.Intern("side-relative-direction");
    private static readonly Symbol DirectionSourceSymbol = Symbol.Intern("direction-source");
    private static readonly Symbol AddStemSupportSymbol = Symbol.Intern("add-stem-support");
    private static readonly Symbol FingeringOrientationsSymbol
        = Symbol.Intern("fingeringOrientations");
    private static readonly Symbol StringNumberOrientationsSymbol
        = Symbol.Intern("stringNumberOrientations");
    private static readonly Symbol StrokeFingerOrientationsSymbol
        = Symbol.Intern("strokeFingerOrientations");
    private static readonly Symbol RhythmicHeadInterface = Symbol.Intern("rhythmic-head-interface");
    private static readonly Symbol InlineAccidentalInterface
        = Symbol.Intern("inline-accidental-interface");
    private static readonly Symbol StemInterface = Symbol.Intern("stem-interface");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");

    // upstream's Finger_tuple.
    private sealed class FingerTuple
    {
        internal Grob Head;
        internal Grob Script;
        internal StreamEvent NoteEvent;
        internal StreamEvent FingerEvent;
        internal int Position;
    }

    private readonly List<FingerTuple> _fingerings = new List<FingerTuple>();
    private readonly List<FingerTuple> _strokeFingerings = new List<FingerTuple>();
    private readonly List<FingerTuple> _articulations = new List<FingerTuple>();
    private readonly List<FingerTuple> _stringNumbers = new List<FingerTuple>();
    private readonly List<Grob> _heads = new List<Grob>();
    private readonly List<Grob> _accidentals = new List<Grob>();
    private Grob _stem;
    private Item _noteColumn;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public NewFingeringEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "New_fingering_engraver";

    /// <summary>Reads each note head's articulations and makes the scripts they ask for.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        // Upstream's registration order: rhythmic_head, inline_accidental, stem,
        // note_column.
        if (info.Grob.HasInterface(RhythmicHeadInterface))
        {
            AcknowledgeRhythmicHead(info);
        }

        if (info.Grob.HasInterface(InlineAccidentalInterface))
        {
            _accidentals.Add(info.Grob);
        }

        if (info.Grob.HasInterface(StemInterface))
        {
            _stem = info.Grob;
        }

        if (info.Grob.HasInterface(NoteColumnInterface) && info.Grob is Item column)
        {
            _noteColumn = column;
        }
    }

    /// <summary>Positions everything collected, then clears the timestep.</summary>
    public override void StopTranslationTimestep()
    {
        PositionAll();
        _stem = null;
        _noteColumn = null;
        _heads.Clear();
        _accidentals.Clear();
    }

    private void AcknowledgeRhythmicHead(GrobInfo inf)
    {
        StreamEvent noteEv = inf.EventCause;
        if (noteEv == null)
        {
            return;
        }

        object arts = noteEv.GetProperty(ArticulationsSymbol);
        for (object s = arts; s is Pair pair; s = pair.Cdr)
        {
            if (!(pair.Car is StreamEvent ev))
            {
                continue;
            }

            if (ev.IsInEventClass(FingeringEventSymbol))
            {
                AddFingering(inf.Grob, "Fingering", _fingerings, ev, noteEv);
            }
            else if (ev.IsInEventClass(TextScriptEventSymbol))
            {
                TranslatorSchemeHelpers.EventWarning(
                    ev, "cannot add text scripts to individual note heads");
            }
            else if (ev.IsInEventClass(ScriptEventSymbol))
            {
                AddScript(inf.Grob, ev, noteEv);
            }
            else if (ev.IsInEventClass(StringNumberEventSymbol))
            {
                AddFingering(inf.Grob, "StringNumber", _stringNumbers, ev, noteEv);
            }
            else if (ev.IsInEventClass(StrokeFingerEventSymbol))
            {
                AddFingering(inf.Grob, "StrokeFinger", _strokeFingerings, ev, noteEv);
            }
            else if (ev.IsInEventClass(HarmonicEventSymbol))
            {
                inf.Grob.SetProperty(StyleSymbol, HarmonicSymbol);
                Grob d = inf.Grob.GetObject(DotSymbol) as Grob;
                if (d != null && !SchemeUtilities.ToBool(GetProperty(HarmonicDotsSymbol)))
                {
                    d.Suicide();
                }
            }
        }

        _heads.Add(inf.Grob);
    }

    private void AddScript(Grob head, StreamEvent ev, StreamEvent note)
    {
        FingerTuple ft = new FingerTuple();
        Grob g = MakeItem("Script", ev);
        ScriptEngraver.MakeScriptFromEvent(
            g, Context, ev.GetProperty(ArticulationTypeSymbol), 0);
        ft.Script = g;
        ft.Script.XParent = head;

        object forcedDir = ev.GetProperty(DirectionSymbol);
        if (DirectionalElementInterface.FromScheme(forcedDir, Direction.Center)
            != Direction.Center)
        {
            ft.Script.SetProperty(DirectionSymbol, forcedDir);
        }

        _articulations.Add(ft);
    }

    private void AddFingering(
        Grob head,
        string grobSym,
        List<FingerTuple> tupleVector,
        StreamEvent ev,
        StreamEvent hevent)
    {
        FingerTuple ft = new FingerTuple();
        ft.Script = MakeItem(grobSym, ev);
        SidePositionInterface.AddSupport(ft.Script, head);
        ft.FingerEvent = ev;
        ft.NoteEvent = hevent;
        ft.Head = head;
        tupleVector.Add(ft);
    }

    private void PositionScripts(object orientations, List<FingerTuple> scripts)
    {
        for (int i = 0; i < scripts.Count; i++)
        {
            if (_stem != null)
            {
                SidePositionInterface.AddSupport(scripts[i].Script, _stem);
                if (_stem.GetObject(FlagSymbol) is Grob flag)
                {
                    SidePositionInterface.AddSupport(scripts[i].Script, flag);
                }
            }
        }

        /*
          This is not extremely elegant, but we have to do a little
          formatting here, because the parent/child relations should be
          known before we move on to the next time step.

          A more sophisticated approach would be to set both X and Y parents
          to the note head, and write a more flexible function for
          positioning the fingerings, setting both X and Y coordinates.
        */
        for (int i = 0; i < scripts.Count; i++)
        {
            object pos = scripts[i].Head.GetProperty(StaffPositionSymbol);
            scripts[i].Position = SchemeConvert.IsNumber(pos)
                ? (int)SchemeConvert.ToLong(pos, "staff-position")
                : 0;
        }

        for (int i = scripts.Count; i-- > 0;)
        {
            for (int j = _heads.Count; j-- > 0;)
            {
                SidePositionInterface.AddSupport(scripts[i].Script, _heads[j]);
            }
        }

        List<FingerTuple> up = new List<FingerTuple>();
        List<FingerTuple> down = new List<FingerTuple>();
        List<FingerTuple> horiz = new List<FingerTuple>();
        for (int i = scripts.Count; i-- > 0;)
        {
            object d = scripts[i].FingerEvent.GetProperty(DirectionSymbol);
            Direction dir = DirectionalElementInterface.FromScheme(d, Direction.Center);
            if (dir != Direction.Center)
            {
                (dir == Direction.Positive ? up : down).Add(scripts[i]);
                scripts.RemoveAt(i);
            }
        }

        SortByPosition(scripts);

        bool upP = SchemeUtilities.Memq(UpSymbol, orientations);
        bool downP = SchemeUtilities.Memq(DownSymbol, orientations);
        bool leftP = SchemeUtilities.Memq(LeftSymbol, orientations);
        bool rightP = SchemeUtilities.Memq(RightSymbol, orientations);

        Direction hordir = rightP ? Direction.Positive : Direction.Negative;
        if (leftP || rightP)
        {
            if (upP && up.Count == 0 && scripts.Count > 0)
            {
                up.Add(scripts[scripts.Count - 1]);
                scripts.RemoveAt(scripts.Count - 1);
            }

            if (downP && down.Count == 0 && scripts.Count > 0)
            {
                down.Add(scripts[0]);
                scripts.RemoveAt(0);
            }

            horiz.AddRange(scripts);
        }
        else if (upP && downP)
        {
            int center = scripts.Count / 2;
            down.AddRange(scripts.GetRange(0, center));
            up.AddRange(scripts.GetRange(center, scripts.Count - center));
        }
        else if (upP)
        {
            up.AddRange(scripts);
            scripts.Clear();
        }
        else
        {
            if (!downP)
            {
                Warn.Warning("no placement found for fingerings");
                Warn.Warning("placing below");
            }

            down.AddRange(scripts);
            scripts.Clear();
        }

        for (int i = 0; i < horiz.Count; i++)
        {
            FingerTuple ft = horiz[i];
            Grob f = ft.Script;
            f.XParent = ft.Head;
            f.YParent = ft.Head;
            f.SetProperty(AvoidSlurSymbol, InsideSymbol);

            if (hordir == Direction.Negative
                && ft.Head.GetObject(AccidentalGrobSymbol) is Grob accidental)
            {
                SidePositionInterface.AddSupport(f, accidental);
            }
            else if (RhythmicHead.DotCount(ft.Head) != 0)
            {
                for (int j = 0; j < _heads.Count; j++)
                {
                    if (_heads[j].GetObject(DotSymbol) is Grob d)
                    {
                        SidePositionInterface.AddSupport(f, d);
                    }
                }
            }

            if (horiz.Count > 1) /* -> FingeringColumn */
            {
                // Ouch, should do this in the typesetting phase. --JeanAS
                Stencil? stil = f.GetStencil();
                if (stil.HasValue)
                {
                    Stencil aligned = stil.Value;
                    aligned.AlignTo(Axis.Y, 0.0);
                    f.SetProperty(StencilSymbol, aligned);
                }
            }
            else
            {
                double selfAlignY = SelfAlignmentInterface.AlignedOnParent(f, Axis.Y);
                object yoff = f.GetProperty(YOffsetSymbol);
                if (SchemeConvert.IsNumber(yoff))
                {
                    selfAlignY += SchemeConvert.ToDouble(yoff, "Y-offset");
                }

                f.SetProperty(YOffsetSymbol, selfAlignY);
            }

            SidePositionInterface.SetAxis(f, Axis.X);
            f.SetProperty(DirectionSymbol, (long)(int)hordir);
        }

        DrulArray<List<FingerTuple>> vertical = new DrulArray<List<FingerTuple>>(down, up);
        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            for (int i = 0; i < vertical[d].Count; i++)
            {
                FingerTuple ft = vertical[d][i];
                Grob f = ft.Script;
                object prio = f.GetProperty(ScriptPrioritySymbol);
                long fingerPrio = SchemeConvert.IsNumber(prio)
                    ? SchemeConvert.ToLong(prio, "script-priority")
                    : 200;

                if (_heads.Count > 1
                    && SchemeUtilities.ToBool(f.GetProperty(XAlignOnMainNoteheadsSymbol)))
                {
                    f.XParent = _noteColumn;
                }
                else
                {
                    f.XParent = ft.Head;
                    if (_heads.Count > 1)
                    {
                        for (int j = 0; j < _accidentals.Count; j++)
                        {
                            SidePositionInterface.AddSupport(f, _accidentals[j]);
                        }
                    }
                }

                f.SetProperty(ScriptPrioritySymbol, fingerPrio + ((int)d * ft.Position));
                SelfAlignmentInterface.SetAlignedOnParent(f, Axis.X);
                SidePositionInterface.SetAxis(f, Axis.Y);
                f.SetProperty(DirectionSymbol, (long)(int)d);
            }
        }
    }

    private void PositionAll()
    {
        if (_fingerings.Count > 0)
        {
            PositionScripts(GetProperty(FingeringOrientationsSymbol), _fingerings);
            _fingerings.Clear();
        }

        if (_stringNumbers.Count > 0)
        {
            PositionScripts(GetProperty(StringNumberOrientationsSymbol), _stringNumbers);
            _stringNumbers.Clear();
        }

        if (_strokeFingerings.Count > 0)
        {
            PositionScripts(GetProperty(StrokeFingerOrientationsSymbol), _strokeFingerings);
            _strokeFingerings.Clear();
        }

        for (int i = _articulations.Count; i-- > 0;)
        {
            Grob script = _articulations[i].Script;
            for (int j = 0; j < _accidentals.Count; j++)
            {
                SidePositionInterface.AddSupport(script, _accidentals[j]);
            }

            _accidentals.Clear();
            for (int j = _heads.Count; j-- > 0;)
            {
                SidePositionInterface.AddSupport(script, _heads[j]);
            }

            if (_stem != null
                && DirectionalElementInterface.FromScheme(
                       script.GetProperty(SideRelativeDirectionSymbol), Direction.Center)
                   != Direction.Center)
            {
                script.SetObject(DirectionSourceSymbol, _stem);
            }

            if (_stem != null && SchemeUtilities.ToBool(script.GetProperty(AddStemSupportSymbol)))
            {
                SidePositionInterface.AddSupport(script, _stem);
            }
        }

        _articulations.Clear();
    }

    // std::sort with Finger_tuple's operator< on position_. See the file note on why the
    // port sorts stably where upstream does not.
    private static void SortByPosition(List<FingerTuple> items)
    {
        for (int i = 1; i < items.Count; i++)
        {
            FingerTuple current = items[i];
            int j = i - 1;
            while (j >= 0 && current.Position < items[j].Position)
            {
                items[j + 1] = items[j];
                j--;
            }

            items[j + 1] = current;
        }
    }
}

/// <summary>
/// Finds potentially colliding scripts and puts them into a <c>FingeringColumn</c>
/// object; that will fix the collisions.
/// </summary>
public class FingeringColumnEngraver : Engraver
{
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol FingerInterface = Symbol.Intern("finger-interface");

    private static readonly Direction[] Both = { Direction.Negative, Direction.Positive };

    private DrulArray<Grob> _fingeringColumns = new DrulArray<Grob>(null, null);
    private readonly DrulArray<List<Grob>> _scripts
        = new DrulArray<List<Grob>>(new List<Grob>(), new List<Grob>());
    private readonly List<Grob> _possibles = new List<Grob>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public FingeringColumnEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Fingering_column_engraver";

    /// <summary>Collects the fingerings of this timestep.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob.HasInterface(FingerInterface) && info.Grob is Item)
        {
            _possibles.Add(info.Grob);
        }
    }

    /// <summary>Makes the columns once more than one fingering wants one.</summary>
    public override void ProcessAcknowledged()
    {
        foreach (Direction d in Both)
        {
            if (_possibles.Count > 1 && _fingeringColumns[d] == null)
            {
                _fingeringColumns[d] = MakeItem("FingeringColumn", Nil.Instance);
            }
        }
    }

    /// <summary>Sorts the fingerings into the left and right columns.</summary>
    public override void StopTranslationTimestep()
    {
        for (int i = 0; i < _possibles.Count; i++)
        {
            Grob item = _possibles[i];
            if (!Item.IsNonMusical(item) && SidePositionInterface.IsOnXAxis(item))
            {
                Direction d = DirectionalElementInterface.FromScheme(
                    item.GetProperty(DirectionSymbol), Direction.Center);
                if (d != Direction.Center)
                {
                    _scripts[d].Add(item);
                }
                else
                {
                    _possibles[i].Warning("Cannot add a fingering without a direction.");
                }
            }
        }

        foreach (Direction d in Both)
        {
            if (_scripts[d].Count < 2 && _fingeringColumns[d] != null)
            {
                _fingeringColumns[d].Suicide();
                _fingeringColumns[d] = null;
            }

            if (_fingeringColumns[d] != null)
            {
                for (int i = 0; i < _scripts[d].Count; i++)
                {
                    FingeringColumn.AddFingering(_fingeringColumns[d], _scripts[d][i]);
                }
            }

            _scripts[d].Clear();
            _fingeringColumns[d] = null;
        }

        _possibles.Clear();
    }
}
