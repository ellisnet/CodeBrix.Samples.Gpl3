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

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/staff-symbol-engraver.cc, lily/clef-engraver.cc, lily/note-heads-engraver.cc, lily/axis-group-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Creates the constellation of five (default) staff lines.
/// <para>
/// The staff symbol is a spanner, not an item: it runs the length of the music, so it
/// is opened at the first timestep and closed at the last. Every grob announced while
/// it is open gets a <c>staff-symbol</c> object pointing at it, which is how a note
/// head later knows which staff its position is measured against.
/// </para>
/// </summary>
public class StaffSymbolEngraver : Engraver
{
    private static readonly Symbol StaffSymbolSymbol = Symbol.Intern("staff-symbol");
    private static readonly Symbol CurrentCommandColumnSymbol = Symbol.Intern("currentCommandColumn");
    private static readonly Symbol StaffSpanEventSymbol = Symbol.Intern("staff-span-event");

    private readonly UniqueSpanEventListener _staffSpanListener = new UniqueSpanEventListener();

    private Spanner _span;
    private Spanner _finishedSpan;
    private bool _firstStart = true;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public StaffSymbolEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Staff_symbol_engraver";

    /// <summary>Gets the staff symbol currently open, if any.</summary>
    public Spanner Span => _span;

    /// <summary>Starts listening for <c>\startStaff</c> and <c>\stopStaff</c>.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(StaffSpanEventSymbol, _staffSpanListener.Listen);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>
    /// Opens and closes staff symbols as <c>\startStaff</c> and <c>\stopStaff</c> ask.
    /// <para>
    /// //was previously: <c>if (_firstStart) { StartSpanner(); }</c> and nothing else —
    /// the engraver had NO staff-span listener at all, so <c>\stopStaff</c> and
    /// <c>\startStaff</c> did nothing and a staff got exactly ONE StaffSymbol, opened at
    /// the first timestep and closed at finalize. The tell was arithmetic: the
    /// bar-line-placement family draws six <c>\stopStaff … \startStaff</c> segments per
    /// staff and the port drew 21 staff lines where the oracle drew 126.
    /// </para>
    /// </summary>
    public override void ProcessMusic()
    {
        if (_staffSpanListener.Stop != null)
        {
            _finishedSpan = _span;
            _span = null;
            if (_firstStart)
            {
                _firstStart = false;
            }
        }

        if (_staffSpanListener.Start != null
            || (_firstStart && _staffSpanListener.Stop == null))
        {
            StartSpanner();
        }
    }

    /// <summary>Hands every announced grob a pointer to the staff symbol.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        /*
          Todo: staff-symbol-referencer iface.
        */
        Spanner mine = _span ?? _finishedSpan;
        if (mine != null)
        {
            info.Grob.SetObject(StaffSymbolSymbol, mine);
        }
    }

    /// <summary>Closes any staff symbol that ended this timestep.</summary>
    public override void StopTranslationTimestep()
    {
        //was previously: if (_firstStart && _span != null)
        // Upstream's condition is (get_start () || first_start_) && span_, and the
        // listener is RESET here, between the two halves.
        if ((_staffSpanListener.Start != null || _firstStart) && _span != null)
        {
            _firstStart = false;
        }

        _staffSpanListener.Reset();
        StopSpanner();
    }

    /// <summary>Closes the staff symbol at the end of the music.</summary>
    public override void FinalizeTranslation()
    {
        _finishedSpan = _span;
        _span = null;
        StopSpanner();
    }

    private void StartSpanner()
    {
        if (_span != null)
        {
            return;
        }

        _span = MakeSpanner("StaffSymbol", Nil.Instance);
        if (_span == null)
        {
            return;
        }

        if (GetProperty(CurrentCommandColumnSymbol) is Grob column)
        {
            _span.SetBound(Direction.Negative, column);
        }

        // A StaffSymbol's staff symbol is itself.
        _span.SetObject(StaffSymbolSymbol, _span);
    }

    private void StopSpanner()
    {
        if (_finishedSpan == null)
        {
            return;
        }

        if (_finishedSpan.GetBound(Direction.Positive) == null
            && GetProperty(CurrentCommandColumnSymbol) is Grob column)
        {
            _finishedSpan.SetBound(Direction.Positive, column);
        }

        // Upstream announces with the stop event as the cause. Both callers reset the
        // listener FIRST, so in practice the cause is always '() — an upstream quirk
        // reproduced as written (rule 2) rather than simplified away, so that the two
        // move together if the reset order ever changes.
        //was previously: AnnounceEndGrob(_finishedSpan, Nil.Instance);
        AnnounceEndGrob(
            _finishedSpan, (object)_staffSpanListener.Stop ?? Nil.Instance);
        _finishedSpan = null;
    }
}

/// <summary>
/// Determines and sets the reference point for pitches — the clef.
/// <para>
/// The clef is created only when the properties describing it CHANGE, which is why the
/// engraver remembers the previous glyph, position and transposition. The initial
/// values are deliberately <see langword="false"/> rather than the empty list, because
/// the empty list is a legitimate <c>clefPosition</c> and starting from it would
/// suppress the very first clef.
/// </para>
/// </summary>
public class ClefEngraver : Engraver
{
    private static readonly Symbol ClefSymbol = Symbol.Intern("Clef");
    private static readonly Symbol GlyphSymbol = Symbol.Intern("glyph");
    private static readonly Symbol ClefGlyphSymbol = Symbol.Intern("clefGlyph");
    private static readonly Symbol ClefPositionSymbol = Symbol.Intern("clefPosition");
    private static readonly Symbol ClefTranspositionSymbol = Symbol.Intern("clefTransposition");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol NonDefaultSymbol = Symbol.Intern("non-default");
    private static readonly Symbol FirstClefSymbol = Symbol.Intern("firstClef");
    private static readonly Symbol ForceClefSymbol = Symbol.Intern("forceClef");
    private static readonly Symbol ExplicitClefVisibilitySymbol = Symbol.Intern("explicitClefVisibility");

    private Item _clef;

    // Trigger a clef at the start, since #f is not '().
    private object _previousGlyph = false;
    private object _previousPosition = false;
    private long _previousTransposition;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public ClefEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Clef_engraver";

    /// <summary>Gets the clef created this timestep, if any.</summary>
    public Item Clef => _clef;

    /// <summary>Creates a clef when the clef properties have changed.</summary>
    public override void ProcessMusic()
    {
        InspectClefProperties();

        // Efficiency: only create a clef at points where it might be visible,
        // namely where a break has not been forbidden (yet).
        //
        // Worth knowing what that means with no Bar_engraver present: nothing ever sets
        // forbidBreak, so a break is allowed at every timestep and a clef is made at
        // every timestep. That is upstream's behaviour too under the same conditions --
        // it is the missing Bar_engraver showing through, not a defect here.
        if (GetProperty(ClefGlyphSymbol) is MutableString && Context.BreakAllowed(Context))
        {
            CreateClef();
        }
    }

    /// <summary>Applies the break visibility and releases the clef.</summary>
    public override void StopTranslationTimestep()
    {
        if (_clef == null)
        {
            return;
        }

        if (SchemeUtilities.ToBool(_clef.GetProperty(NonDefaultSymbol))
            && GetProperty(ExplicitClefVisibilitySymbol) is object[] visibility)
        {
            _clef.SetProperty(Symbol.Intern("break-visibility"), visibility);
        }

        _clef = null;
    }

    private void SetGlyph()
    {
        // A revert then a push: the override has to be replaced, not stacked, or every
        // clef change would leave the previous glyph underneath it.
        GrobPropertyInfo.ExecutePushPopProperty(Context, ClefSymbol, GlyphSymbol, null);
        GrobPropertyInfo.ExecutePushPopProperty(
            Context, ClefSymbol, GlyphSymbol, GetProperty(ClefGlyphSymbol));
    }

    private void CreateClef()
    {
        if (_clef != null)
        {
            return;
        }

        _clef = MakeItem("Clef", Nil.Instance);
        if (_clef == null)
        {
            return;
        }

        object position = GetProperty(ClefPositionSymbol);
        if (SchemeConvert.IsNumber(position))
        {
            _clef.SetProperty(StaffPositionSymbol, position);
        }
    }

    private void InspectClefProperties()
    {
        object glyph = GetProperty(ClefGlyphSymbol);
        object clefPosition = GetProperty(ClefPositionSymbol);
        object transpositionValue = GetProperty(ClefTranspositionSymbol);
        long transposition = SchemeConvert.IsNumber(transpositionValue)
            ? SchemeConvert.ToLong(transpositionValue, "clefTransposition")
            : 0;
        object forceClef = GetProperty(ForceClefSymbol);

        if (clefPosition is Nil
            || !SchemeUtilities.IsEqual(glyph, _previousGlyph)
            || !SchemeUtilities.IsEqual(clefPosition, _previousPosition)
            || transposition != _previousTransposition
            || SchemeUtilities.ToBool(forceClef))
        {
            SetGlyph();

            // Not on the very first inspection unless firstClef says so: the previous
            // position starts as #f precisely so this test can tell "never set" from
            // "set to nothing".
            if (SchemeUtilities.IsSchemeTrue(_previousPosition)
                || SchemeUtilities.ToBool(GetProperty(FirstClefSymbol)))
            {
                CreateClef();
            }

            _clef?.SetProperty(NonDefaultSymbol, true);

            _previousPosition = clefPosition;
            _previousGlyph = glyph;
            _previousTransposition = transposition;
        }

        if (SchemeUtilities.ToBool(forceClef))
        {
            Context where = Context.WhereDefined(ForceClefSymbol, out object _);
            where?.SetProperty(ForceClefSymbol, Nil.Instance);
        }
    }
}

/// <summary>
/// Generates note heads.
/// <para>
/// The engraver collects note events as they are broadcast and makes the heads in
/// <see cref="Translator.ProcessMusic"/> rather than in the listener, because the
/// staff position depends on <c>middleCPosition</c>, which another engraver in the same
/// timestep may still change.
/// </para>
/// </summary>
public class NoteHeadsEngraver : Engraver
{
    private static readonly Symbol NoteEventSymbol = Symbol.Intern("note-event");
    private static readonly Symbol MiddleCPositionSymbol = Symbol.Intern("middleCPosition");
    private static readonly Symbol StaffLineLayoutFunctionSymbol = Symbol.Intern("staffLineLayoutFunction");
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol PitchApproximateSymbol = Symbol.Intern("pitch-approximate");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");

    private readonly List<StreamEvent> _noteEvents = new List<StreamEvent>();
    private readonly List<Item> _heads = new List<Item>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public NoteHeadsEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Note_heads_engraver";

    /// <summary>Gets the note heads created in the most recent timestep.</summary>
    public IReadOnlyList<Item> Heads => _heads;

    /// <summary>Starts listening for note events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(NoteEventSymbol, ListenNote);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Makes one note head per note event heard this timestep.</summary>
    public override void ProcessMusic()
    {
        object middleC = GetProperty(MiddleCPositionSymbol);
        object layoutProcedure = GetProperty(StaffLineLayoutFunctionSymbol);

        _heads.Clear();
        foreach (StreamEvent noteEvent in _noteEvents)
        {
            bool approximate = SchemeUtilities.ToBool(noteEvent.GetProperty(PitchApproximateSymbol));
            Item note = MakeItem(
                approximate ? "ApproximatePitchNoteHead" : "NoteHead",
                noteEvent);
            if (note == null)
            {
                continue;
            }

            object pitchValue = noteEvent.GetProperty(PitchSymbol);
            Pitch pitch = pitchValue as Pitch;

            long position;
            if (pitch == null)
            {
                position = 0;
            }
            else if (layoutProcedure is Procedure)
            {
                object result = SchemeUtilities.CallCallback(layoutProcedure, pitchValue);
                position = SchemeConvert.IsNumber(result)
                    ? SchemeConvert.ToLong(result, "staffLineLayoutFunction")
                    : pitch.Steps();
            }
            else
            {
                position = pitch.Steps();
            }

            if (SchemeConvert.IsNumber(middleC))
            {
                position += SchemeConvert.ToLong(middleC, "middleCPosition");
            }

            note.SetProperty(StaffPositionSymbol, position);
            _heads.Add(note);
        }
    }

    /// <summary>Forgets the events heard this timestep.</summary>
    public override void StopTranslationTimestep() => _noteEvents.Clear();

    private void ListenNote(StreamEvent noteEvent) => _noteEvents.Add(noteEvent);
}

/**
   Put stuff in a Spanner with an Axis_group_interface.
   Use as last element of a context.
*/

/// <summary>
/// Collects everything a context produces into one vertical group.
/// <para>
/// It must run LAST among its siblings — <see cref="Translator.MustBeLast"/> — so that
/// everything else in the context has already announced what it made. It is also
/// self-disabling: if an enclosing context already put an axis group here, this one
/// stands down, which is what stops two of them fighting over the same parent.
/// </para>
/// </summary>
public class AxisGroupEngraver : Engraver
{
    private static readonly Symbol HasAxisGroupSymbol = Symbol.Intern("hasAxisGroup");
    private static readonly Symbol CurrentCommandColumnSymbol = Symbol.Intern("currentCommandColumn");
    private static readonly Symbol AxisGroupParentYSymbol = Symbol.Intern("axis-group-parent-Y");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol RemoveEmptySymbol = Symbol.Intern("remove-empty");
    private static readonly Symbol KeepAliveInterfacesSymbol = Symbol.Intern("keepAliveInterfaces");

    private readonly List<Grob> _elements = new List<Grob>();
    private bool _active;
    private Spanner _staffLine;
    private object _interesting = Nil.Instance;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public AxisGroupEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Axis_group_engraver";

    /// <summary>Gets a value indicating whether this engraver must run last.</summary>
    public override bool MustBeLast => true;

    /// <summary>Gets the vertical group spanner, if one was made.</summary>
    public Spanner StaffLine => _staffLine;

    /// <summary>Claims the context's axis group, unless one already exists.</summary>
    public override void Initialize()
    {
        _active = !SchemeUtilities.ToBool(GetProperty(HasAxisGroupSymbol));
        if (_active)
        {
            Context?.SetProperty(HasAxisGroupSymbol, true);
        }
    }

    /// <summary>Opens the vertical group spanner.</summary>
    public override void ProcessMusic()
    {
        if (_staffLine == null && _active)
        {
            _staffLine = GetSpanner();
            if (_staffLine != null && GetProperty(CurrentCommandColumnSymbol) is Grob column)
            {
                _staffLine.SetBound(Direction.Negative, column);
            }
        }

        _interesting = GetProperty(KeepAliveInterfacesSymbol);
    }

    /// <summary>
    /// Records every announced grob for grouping — and, on a <c>remove-empty</c>
    /// group, marks the ones whose interfaces are on <c>keepAliveInterfaces</c> as
    /// <c>items-worth-living</c>.
    /// </summary>
    /// <remarks>
    /// The keep-alive half was MISSING at first: nothing anywhere populated
    /// <c>items-worth-living</c>, so the moment line breaking's
    /// <see cref="HaraKiriGroupSpanner"/> landed, every <c>remove-empty</c> group in
    /// every score read as EMPTY and suicided with all its children — which is how
    /// the tablature group's flagged no-BassFigure-reaches-the-page gap turned out to end: the
    /// figures were drawn into a FiguredBass axis group whose staffline killed them.
    /// </remarks>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (_staffLine == null)
        {
            return;
        }

        _elements.Add(info.Grob);

        if (SchemeUtilities.ToBool(_staffLine.GetProperty(RemoveEmptySymbol)))
        {
            object cursor = _interesting;
            while (cursor is Pair pair)
            {
                if (pair.Car is Symbol interfaceName && info.Grob.HasInterface(interfaceName))
                {
                    HaraKiriGroupSpanner.AddInterestingItem(_staffLine, info.Grob);
                    break;
                }

                cursor = pair.Cdr;
            }
        }
    }

    /// <summary>
    /// Adds every acknowledged grob that has no vertical parent yet to the group.
    /// </summary>
    public override void ProcessAcknowledged()
    {
        /*
          maybe should check if our parent is set, because we now get a
          cyclic parent relationship if we have two Axis_group_engravers in
          the context.
        */
        if (_staffLine == null)
        {
            return;
        }

        foreach (Grob element in _elements)
        {
            if (element.GetObject(AxisGroupParentYSymbol) is Grob)
            {
                continue;
            }

            if (_staffLine.YParent != null && ReferenceEquals(_staffLine.YParent, element))
            {
                Warn.Warning("Axis_group_engraver: vertical group already has a parent");
                Warn.Warning("are there two Axis_group_engravers?");
                Warn.Warning("removing this vertical group");
                _staffLine.Suicide();
                _staffLine = null;
                break;
            }

            AxisGroupInterface.AddElement(_staffLine, element);
        }

        _elements.Clear();
    }

    /// <summary>Closes the vertical group spanner.</summary>
    public override void FinalizeTranslation()
    {
        if (_staffLine != null && GetProperty(CurrentCommandColumnSymbol) is Grob column)
        {
            _staffLine.SetBound(Direction.Positive, column);
            PointerGroupInterface.SetOrdered(_staffLine, ElementsSymbol, false);
        }
    }

    /// <summary>Makes the group spanner. Overridden by the hara-kiri variant upstream.</summary>
    /// <returns>The spanner.</returns>
    protected virtual Spanner GetSpanner() => MakeSpanner("VerticalAxisGroup", Nil.Instance);
}
