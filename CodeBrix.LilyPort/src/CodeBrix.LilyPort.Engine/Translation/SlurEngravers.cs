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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/slur-engraver.cc, lily/phrasing-slur-engraver.cc, lily/include/slur-engraver.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - the two engravers share a file because upstream's phrasing-slur-engraver.cc is
//     nothing but a five-method subclass of the slur one.
//   - upstream registers one acknowledger per interface and lets the macro layer dispatch;
//     the port's single AcknowledgeGrob writes every interface test out, IN UPSTREAM'S
//     REGISTRATION ORDER, because a grob carrying two of these interfaces must reach the
//     same handler it reaches upstream.
//   - `fingering-interface` is registered because upstream registers it. NOTHING DECLARES
//     THAT INTERFACE — not scm/, not lily/ — so the acknowledger is dead upstream and dead
//     here. It is kept rather than dropped so the registration stays a faithful record;
//     if Fingering ever gains that interface, this starts working with no change here.

/// <summary>
/// Builds slur grobs from slur events, and collects everything the slur must be shaped
/// around.
/// </summary>
public class SlurEngraver : Engraver
{
    private static readonly Symbol SlurEventSymbol = Symbol.Intern("slur-event");
    private static readonly Symbol PhrasingSlurEventSymbol = Symbol.Intern("phrasing-slur-event");
    private static readonly Symbol NoteEventSymbol = Symbol.Intern("note-event");
    private static readonly Symbol ArticulationsSymbol = Symbol.Intern("articulations");
    private static readonly Symbol SpanDirectionSymbol = Symbol.Intern("span-direction");
    private static readonly Symbol SpannerIdSymbol = Symbol.Intern("spanner-id");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol DoubleSlursSymbol = Symbol.Intern("doubleSlurs");
    private static readonly Symbol SlurMelismaBusySymbol = Symbol.Intern("slurMelismaBusy");
    private static readonly Symbol CurrentCommandColumnSymbol
        = Symbol.Intern("currentCommandColumn");
    private static readonly Symbol CurrentMusicalColumnSymbol
        = Symbol.Intern("currentMusicalColumn");
    private static readonly Symbol NoteHeadsSymbol = Symbol.Intern("note-heads");

    private static readonly Symbol InlineAccidentalInterface
        = Symbol.Intern("inline-accidental-interface");
    private static readonly Symbol FingeringInterface = Symbol.Intern("fingering-interface");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol ScriptInterface = Symbol.Intern("script-interface");
    private static readonly Symbol TextScriptInterface = Symbol.Intern("text-script-interface");
    private static readonly Symbol DotsInterface = Symbol.Intern("dots-interface");
    private static readonly Symbol TupletNumberInterface = Symbol.Intern("tuplet-number-interface");
    private static readonly Symbol TieInterface = Symbol.Intern("tie-interface");
    private static readonly Symbol SlurInterface = Symbol.Intern("slur-interface");
    private static readonly Symbol DynamicInterface = Symbol.Intern("dynamic-interface");

    private static readonly Direction[] Both = { Direction.Negative, Direction.Positive };

    private readonly List<EventInfo> _startEvents = new List<EventInfo>();
    private readonly List<EventInfo> _stopEvents = new List<EventInfo>();

    // upstream's Drul_array<std::multimap<Stream_event *, Spanner *>>. A List preserves
    // insertion order among equal keys, which is exactly what multimap's equal_range gives.
    private readonly DrulArray<List<(StreamEvent Note, Spanner Slur)>> _noteSlurs
        = new DrulArray<List<(StreamEvent, Spanner)>>(
            new List<(StreamEvent, Spanner)>(), new List<(StreamEvent, Spanner)>());

    private readonly List<Spanner> _slurs = new List<Spanner>();
    private readonly List<Spanner> _endSlurs = new List<Spanner>();

    // objects that we need for formatting, eg. scripts and ties.
    private readonly List<Grob> _objectsToAcknowledge = new List<Grob>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public SlurEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Slur_engraver";

    /// <summary>Starts listening for slur and note events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(EventSymbol, ListenSlur);
        ListenTo(NoteEventSymbol, ListenNote);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Ends the slurs this timestep stops, then starts the ones it starts.</summary>
    public override void ProcessMusic()
    {
        for (int i = 0; i < _stopEvents.Count; i++)
        {
            object id = _stopEvents[i].Slur.GetProperty(SpannerIdSymbol);
            bool ended = TryToEnd(_stopEvents[i]);
            if (ended)
            {
                // Ignore redundant stop events for this id
                for (int j = _stopEvents.Count; --j > i;)
                {
                    if (SchemeUtilities.IsEqual(
                            id, _stopEvents[j].Slur.GetProperty(SpannerIdSymbol)))
                    {
                        _stopEvents.RemoveAt(j);
                    }
                }
            }
            else
            {
                TranslatorSchemeHelpers.EventWarning(_stopEvents[i].Slur, "cannot end " + ObjectName);
            }
        }

        int oldSlurs = _slurs.Count;
        for (int i = _startEvents.Count; i-- > 0;)
        {
            StreamEvent ev = _startEvents[i].Slur;
            object id = ev.GetProperty(SpannerIdSymbol);
            Direction updown = DirectionalElementInterface.FromScheme(
                ev.GetProperty(DirectionSymbol), Direction.Center);

            if (CanCreateSlur(id, oldSlurs, ref i, ev))
            {
                CreateSlur(id, _startEvents[i], null, updown, false);
            }
        }

        SetMelisma(_slurs.Count > 0);
    }

    /// <summary>Collects the note columns and grobs the slur has to be shaped around.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        Grob grob = info.Grob;

        // Upstream's registration order, written out.
        if (grob.HasInterface(InlineAccidentalInterface)
            || grob.HasInterface(FingeringInterface)
            || grob.HasInterface(TextScriptInterface)
            || grob.HasInterface(DotsInterface)
            || grob.HasInterface(TupletNumberInterface)
            || (AcknowledgesSlurGrobs && grob.HasInterface(SlurInterface)))
        {
            AcknowledgeExtraObject(info);
            return;
        }

        if (grob.HasInterface(NoteColumnInterface) && grob is Item item)
        {
            AcknowledgeNoteColumn(item);
            return;
        }

        if (grob.HasInterface(ScriptInterface))
        {
            AcknowledgeScript(info);
        }
    }

    /// <summary>Collects a tie as it ends, so the slur can be shaped clear of it.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeEndGrob(GrobInfo info)
    {
        if (info.Grob.HasInterface(TieInterface))
        {
            AcknowledgeExtraObject(info);
        }
    }

    /// <summary>Closes the timestep: binds the ended slurs and wires up the extra objects.</summary>
    public override void StopTranslationTimestep()
    {
        if (GetProperty(CurrentCommandColumnSymbol) is Grob g)
        {
            for (int i = 0; i < _endSlurs.Count; i++)
            {
                Slur.AddExtraEncompass(_endSlurs[i], g);
            }

            if (_startEvents.Count == 0)
            {
                for (int i = 0; i < _slurs.Count; i++)
                {
                    Slur.AddExtraEncompass(_slurs[i], g);
                }
            }
        }

        for (int i = 0; i < _endSlurs.Count; i++)
        {
            Spanner s = _endSlurs[i];
            if (s.GetBound(Direction.Positive) == null)
            {
                Grob col = GetProperty(CurrentMusicalColumnSymbol) as Grob;
                s.SetBound(Direction.Positive, col);
            }

            AnnounceEndGrob(s, Nil.Instance);
        }

        for (int i = 0; i < _objectsToAcknowledge.Count; i++)
        {
            Slur.AuxiliaryAcknowledgeExtraObject(_objectsToAcknowledge[i], _slurs, _endSlurs);
        }

        _noteSlurs[Direction.Negative].Clear();
        _noteSlurs[Direction.Positive].Clear();
        _objectsToAcknowledge.Clear();
        _endSlurs.Clear();
        _startEvents.Clear();
        _stopEvents.Clear();
    }

    /// <summary>Warns about slurs that never ended, and kills them.</summary>
    public override void FinalizeTranslation()
    {
        for (int i = 0; i < _slurs.Count; i++)
        {
            _slurs[i].Warning("unterminated " + ObjectName);
            _slurs[i].Suicide();
        }

        _slurs.Clear();
    }

    /// <summary>Gets the event class this engraver's slurs come from.</summary>
    protected virtual Symbol EventSymbol => SlurEventSymbol;

    /// <summary>Gets whether <c>doubleSlurs</c> applies to this engraver.</summary>
    protected virtual bool DoubleProperty
        => SchemeUtilities.IsSchemeTrue(GetProperty(DoubleSlursSymbol));

    /// <summary>Gets the grob name this engraver creates.</summary>
    protected virtual string GrobSymbol => "Slur";

    /// <summary>Gets the name used in this engraver's warnings.</summary>
    protected virtual string ObjectName => "slur";

    /// <summary>Gets whether this engraver treats another slur as an object to avoid.</summary>
    /// <remarks>
    /// Only the phrasing-slur engraver does — upstream registers
    /// <c>ADD_ACKNOWLEDGER_FOR (acknowledge_extra_object, slur)</c> on that one alone, so a
    /// phrasing slur is shaped around the slurs inside it and not the other way round.
    /// </remarks>
    protected virtual bool AcknowledgesSlurGrobs => false;

    /// <summary>Records whether a melisma is in progress.</summary>
    /// <param name="m">Whether a slur is open.</param>
    protected virtual void SetMelisma(bool m) => Context.SetProperty(SlurMelismaBusySymbol, m);

    private void ListenSlur(StreamEvent ev) => ListenNoteSlur(ev, null);

    private void ListenNote(StreamEvent ev)
    {
        object arts = ev.GetProperty(ArticulationsSymbol);
        while (arts is Pair pair)
        {
            if (pair.Car is StreamEvent art && art.IsInEventClass(EventSymbol))
            {
                ListenNoteSlur(art, ev);
            }

            arts = pair.Cdr;
        }
    }

    // A slur on an in-chord note is not actually announced as an event but rather produced
    // by the note listener.
    private void ListenNoteSlur(StreamEvent ev, StreamEvent note)
    {
        Direction d = DirectionalElementInterface.FromScheme(
            ev.GetProperty(SpanDirectionSymbol), Direction.Center);
        if (d == Direction.Negative)
        {
            _startEvents.Add(new EventInfo(ev, note));
        }
        else if (d == Direction.Positive)
        {
            _stopEvents.Add(new EventInfo(ev, note));
        }
        else
        {
            TranslatorSchemeHelpers.EventWarning(
                ev, "direction of " + ev.Name + " invalid: " + (int)d);
        }
    }

    private void AcknowledgeNoteColumn(Item e)
    {
        for (int i = _slurs.Count; i-- > 0;)
        {
            Slur.AddColumn(_slurs[i], e);
        }

        for (int i = _endSlurs.Count; i-- > 0;)
        {
            Slur.AddColumn(_endSlurs[i], e);
        }

        // Now cater for slurs starting/ending at a notehead: those override
        // the column bounds
        if (_noteSlurs[Direction.Negative].Count == 0
            && _noteSlurs[Direction.Positive].Count == 0)
        {
            return;
        }

        IReadOnlyList<Grob> heads = PointerGroupInterface.ExtractGrobSet(e, NoteHeadsSymbol);
        for (int i = heads.Count; i-- > 0;)
        {
            StreamEvent ev = heads[i].EventCause();
            if (ev == null)
            {
                continue;
            }

            foreach (Direction d in Both)
            {
                foreach ((StreamEvent Note, Spanner Slur) entry in _noteSlurs[d])
                {
                    if (ReferenceEquals(entry.Note, ev))
                    {
                        entry.Slur.SetBound(d, heads[i]);
                    }
                }
            }
        }
    }

    private void AcknowledgeExtraObject(GrobInfo info) => _objectsToAcknowledge.Add(info.Grob);

    private void AcknowledgeScript(GrobInfo info)
    {
        if (!info.Grob.HasInterface(DynamicInterface))
        {
            AcknowledgeExtraObject(info);
        }
    }

    private void CreateSlur(
        object spannerId, EventInfo evi, Grob gCause, Direction dir, bool leftBroken)
    {
        Grob ccc = leftBroken
            ? GetProperty(CurrentCommandColumnSymbol) as Grob
            : null; // efficiency
        object cause = evi.Slur != null ? (object)evi.Slur : gCause;
        Spanner slur = MakeSpanner(GrobSymbol, cause);
        slur.SetProperty(SpannerIdSymbol, spannerId);
        if (dir != Direction.Center)
        {
            DirectionalElementInterface.SetGrobDirection(slur, dir);
        }

        if (leftBroken)
        {
            slur.SetBound(Direction.Negative, ccc);
        }

        _slurs.Add(slur);
        if (evi.Note != null)
        {
            _noteSlurs[Direction.Negative].Add((evi.Note, slur));
        }

        if (DoubleProperty)
        {
            DirectionalElementInterface.SetGrobDirection(slur, Direction.Negative);
            slur = MakeSpanner(GrobSymbol, cause);
            slur.SetProperty(SpannerIdSymbol, spannerId);
            DirectionalElementInterface.SetGrobDirection(slur, Direction.Positive);
            if (leftBroken)
            {
                slur.SetBound(Direction.Negative, ccc);
            }

            _slurs.Add(slur);
            if (evi.Note != null)
            {
                _noteSlurs[Direction.Negative].Add((evi.Note, slur));
            }
        }
    }

    private bool CanCreateSlur(object id, int oldSlurs, ref int eventIdx, StreamEvent ev)
    {
        for (int j = _slurs.Count; j-- > 0;)
        {
            Spanner slur = _slurs[j];
            Direction updown = DirectionalElementInterface.FromScheme(
                ev.GetProperty(DirectionSymbol), Direction.Center);

            // Check if we already have a slur with the same spanner-id.
            if (SchemeUtilities.IsEqual(id, slur.GetProperty(SpannerIdSymbol)))
            {
                if (j < oldSlurs)
                {
                    // We already have an old slur, so give a warning
                    // and completely ignore the new slur.
                    TranslatorSchemeHelpers.EventWarning(ev, "already have " + ObjectName);
                    _startEvents.RemoveAt(eventIdx);
                    return false;
                }

                // If this slur event has no direction, it will not
                // contribute anything new to the existing slur(s), so
                // we can ignore it.
                if (updown == Direction.Center)
                {
                    return false;
                }

                StreamEvent c = slur.EventCause();

                if (c == null)
                {
                    slur.ProgrammingError(ObjectName + " without a cause");
                    return true;
                }

                Direction slurDir = DirectionalElementInterface.FromScheme(
                    c.GetProperty(DirectionSymbol), Direction.Center);

                // If the existing slur does not have a direction yet,
                // we'd rather take the new one.
                if (slurDir == Direction.Center)
                {
                    slur.Suicide();
                    _slurs.RemoveAt(j);
                    return true;
                }

                // If the existing slur has the same direction as ours, drop ours
                if (slurDir == updown)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool TryToEnd(EventInfo evi)
    {
        object id = evi.Slur.GetProperty(SpannerIdSymbol);

        // Find the slurs that are ended with this event (by checking the spanner-id)
        bool ended = false;
        for (int j = _slurs.Count; j-- > 0;)
        {
            if (SchemeUtilities.IsEqual(id, _slurs[j].GetProperty(SpannerIdSymbol)))
            {
                ended = true;
                _endSlurs.Add(_slurs[j]);
                if (evi.Note != null)
                {
                    _noteSlurs[Direction.Positive].Add((evi.Note, _slurs[j]));
                }

                _slurs.RemoveAt(j);
            }
        }

        return ended;
    }

    /// <summary>A slur event, and the note it hangs off when it came from a chord.</summary>
    protected readonly struct EventInfo
    {
        /// <summary>Initializes the pair.</summary>
        /// <param name="slur">The slur event.</param>
        /// <param name="note">The note event it was attached to, when there was one.</param>
        public EventInfo(StreamEvent slur, StreamEvent note)
        {
            Slur = slur;
            Note = note;
        }

        /// <summary>Gets the slur event.</summary>
        public StreamEvent Slur { get; }

        /// <summary>Gets the note event, when the slur came from an articulation.</summary>
        public StreamEvent Note { get; }
    }
}

/// <summary>
/// Builds phrasing slurs. Identical to <see cref="SlurEngraver"/> except that it reads a
/// different event class, creates a different grob, never doubles, does NOT drive melismata,
/// and shapes itself around ordinary slurs.
/// </summary>
public sealed class PhrasingSlurEngraver : SlurEngraver
{
    private static readonly Symbol PhrasingSlurEventSymbol
        = Symbol.Intern("phrasing-slur-event");

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public PhrasingSlurEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Phrasing_slur_engraver";

    /// <summary>Gets the event class this engraver's slurs come from.</summary>
    protected override Symbol EventSymbol => PhrasingSlurEventSymbol;

    /// <summary>Gets whether <c>doubleSlurs</c> applies — it does not.</summary>
    protected override bool DoubleProperty => false;

    /// <summary>Gets the grob name this engraver creates.</summary>
    protected override string GrobSymbol => "PhrasingSlur";

    /// <summary>Gets the name used in this engraver's warnings.</summary>
    protected override string ObjectName => "phrasing slur";

    /// <summary>Gets whether this engraver treats another slur as an object to avoid.</summary>
    protected override bool AcknowledgesSlurGrobs => true;

    /// <summary>Does nothing: a phrasing slur does not drive melismata.</summary>
    /// <param name="m">Ignored.</param>
    protected override void SetMelisma(bool m)
    {
    }
}
