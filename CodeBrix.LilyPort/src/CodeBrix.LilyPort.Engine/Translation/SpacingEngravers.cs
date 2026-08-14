/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
  Copyright (C) 2006--2026 Han-Wen Nienhuys <hanwen@lilypond.org>

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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/spacing-engraver.cc, lily/note-spacing-engraver.cc, lily/separating-line-group-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/*
  Acknowledge rhythmic elements, for initializing spacing fields in
  the columns.
*/

/// <summary>
/// Makes the one <c>SpacingSpanner</c> a score is spaced by, and keeps the running
/// record of which note durations are sounding.
/// <para>
/// Two durations are recorded on every musical column, and they are not the same
/// thing: the SHORTEST STARTER is the shortest note that begins here, while the
/// SHORTEST PLAYING is the shortest note sounding across this moment, including ones
/// that began earlier. The spacing between two columns is ruled by the second — which
/// is why a queue of what is still sounding has to be maintained at all.
/// </para>
/// </summary>
public class SpacingEngraver : Engraver
{
    private static readonly Symbol CurrentCommandColumn = Symbol.Intern("currentCommandColumn");
    private static readonly Symbol CurrentMusicalColumn = Symbol.Intern("currentMusicalColumn");
    private static readonly Symbol ProportionalNotationDuration
        = Symbol.Intern("proportionalNotationDuration");

    private static readonly Symbol SpacingSymbol = Symbol.Intern("spacing");
    private static readonly Symbol ShortestPlayingDuration
        = Symbol.Intern("shortest-playing-duration");

    private static readonly Symbol ShortestStarterDuration
        = Symbol.Intern("shortest-starter-duration");

    private static readonly Symbol UsedSymbol = Symbol.Intern("used");
    private static readonly Symbol SpacingSectionSymbol = Symbol.Intern("spacing-section-event");
    private static readonly Symbol RhythmicEvent = Symbol.Intern("rhythmic-event");
    private static readonly Symbol LyricSyllableInterface
        = Symbol.Intern("lyric-syllable-interface");

    private static readonly Symbol MultiMeasureInterface = Symbol.Intern("multi-measure-interface");
    private static readonly Symbol WishesSymbol = Symbol.Intern("wishes");
    private static readonly Symbol StaffSpacingInterface = Symbol.Intern("staff-spacing-interface");
    private static readonly Symbol NoteSpacingInterface = Symbol.Intern("note-spacing-interface");
    private static readonly Symbol RhythmicHeadInterface = Symbol.Intern("rhythmic-head-interface");
    private static readonly Symbol RhythmicGrobInterface = Symbol.Intern("rhythmic-grob-interface");

    /// <summary>
    /// One sounding note: what announced it, and when it stops.
    /// <para>
    /// Ordered by END moment, which is the whole point — the queue is drained from the
    /// front at each timestep to find what has stopped sounding.
    /// </para>
    /// </summary>
    private readonly struct RhythmicTuple
    {
        internal RhythmicTuple(GrobInfo info, Rational end)
        {
            Info = info;
            End = end;
        }

        internal GrobInfo Info { get; }

        internal Rational End { get; }
    }

    // Upstream uses a PQueue keyed on the end moment. A list kept in sorted order does
    // the same job for the handful of notes ever sounding at once, and keeps insertion
    // order among equal ends, which the PQueue's heap does not promise either way.
    private readonly List<RhythmicTuple> _playingDurations = new List<RhythmicTuple>();
    private readonly List<RhythmicTuple> _nowDurations = new List<RhythmicTuple>();
    private readonly List<RhythmicTuple> _stoppedDurations = new List<RhythmicTuple>();

    private Moment _now;
    private Spanner _spacing;
    private StreamEvent _startSection;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public SpacingEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Spacing_engraver";

    /// <summary>Gets the spacing spanner currently being built, for tests.</summary>
    public Spanner Spacing => _spacing;

    /// <summary>
    /// Starts listening for <c>\newSpacingSection</c>.
    /// <para>
    /// Here rather than in the constructor: the context's below-dispatcher is wired up
    /// AFTER the translator is allocated, so a constructor-time registration takes the
    /// whole context tree down with it — silently, because the failure happens while
    /// the Score context is being built and there is no Score engraver left to report it.
    /// </para>
    /// </summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(SpacingSectionSymbol, ListenSpacingSection);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Records the moment translation starts at.</summary>
    public override void Initialize() => _now = NowMoment;

    /// <summary>
    /// Starts a spanner, ending the previous one first when a new spacing section was
    /// asked for.
    /// </summary>
    public override void ProcessMusic()
    {
        if (_startSection != null && _spacing != null)
        {
            StopSpanner();
        }

        if (_spacing == null)
        {
            StartSpanner();
        }
    }

    /// <summary>Collects the spacing wishes and the sounding durations.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        Grob grob = info.Grob;
        if (grob == null)
        {
            return;
        }

        if (grob.HasInterface(StaffSpacingInterface) || grob.HasInterface(NoteSpacingInterface))
        {
            if (_spacing != null)
            {
                PointerGroupInterface.AddGrob(_spacing, WishesSymbol, grob);
            }

            return;
        }

        if (grob.HasInterface(RhythmicHeadInterface) || grob.HasInterface(RhythmicGrobInterface))
        {
            AddStarterDuration(info);
        }
    }

    /// <summary>
    /// Records this timestep's shortest starting and shortest sounding durations on the
    /// musical column, and hands both columns the spanner that will space them.
    /// </summary>
    public override void StopTranslationTimestep()
    {
        PaperColumn musicalColumn = GetProperty(CurrentMusicalColumn) as PaperColumn;

        if (_spacing == null)
        {
            StartSpanner();
        }

        musicalColumn?.SetObject(SpacingSymbol, _spacing);
        (GetProperty(CurrentCommandColumn) as Grob)?.SetObject(SpacingSymbol, _spacing);

        if (musicalColumn == null)
        {
            _nowDurations.Clear();
            return;
        }

        Rational proportional = GetProperty(ProportionalNotationDuration) is Moment moment
            ? moment.MainPart
            : -Rational.Infinity;
        if (proportional >= Rational.Zero)
        {
            // The durations go in as SCHEME numbers, not as the port's Rational struct:
            // their property type predicate is positive-musical-length-as-number?, and a
            // CLR value that is not a Scheme number fails it -- loudly, but only after
            // the value has already been rejected.
            object proportionalValue = Bootstrap.SchemeConvert.FromRational(proportional);
            musicalColumn.SetProperty(ShortestPlayingDuration, proportionalValue);
            musicalColumn.SetProperty(ShortestStarterDuration, proportionalValue);
            musicalColumn.SetProperty(UsedSymbol, true);
            _nowDurations.Clear();
            return;
        }

        Rational shortestPlaying = Rational.Infinity;
        for (int i = 0; i < _playingDurations.Count; i++)
        {
            StreamEvent ev = _playingDurations[i].Info.EventCause;
            if (ev != null)
            {
                Rational len = GetEventLength(ev).MainPart;
                shortestPlaying = Min(shortestPlaying, len);
            }
        }

        Rational starter = Rational.Infinity;

        for (int i = 0; i < _nowDurations.Count; i++)
        {
            RhythmicTuple nd = _nowDurations[i];
            Rational len = GetEventLength(nd.Info.EventCause).MainPart;
            if (len.IsNonZero)
            {
                starter = Min(starter, len);
                Insert(nd);
            }
        }

        _nowDurations.Clear();

        shortestPlaying = Min(shortestPlaying, starter);

        musicalColumn.SetProperty(
            ShortestPlayingDuration, Bootstrap.SchemeConvert.FromRational(shortestPlaying));
        musicalColumn.SetProperty(
            ShortestStarterDuration, Bootstrap.SchemeConvert.FromRational(starter));
    }

    /// <summary>Drains the sounding queue of everything that has stopped by now.</summary>
    public override void StartTranslationTimestep()
    {
        _startSection = null;

        _now = NowMoment;
        _stoppedDurations.Clear();

        while (_playingDurations.Count > 0 && _playingDurations[0].End < _now.MainPart)
        {
            _playingDurations.RemoveAt(0);
        }

        while (_playingDurations.Count > 0 && _playingDurations[0].End == _now.MainPart)
        {
            _stoppedDurations.Add(_playingDurations[0]);
            _playingDurations.RemoveAt(0);
        }
    }

    /// <summary>Ends the spanner at the last command column.</summary>
    public override void FinalizeTranslation() => StopSpanner();

    private void ListenSpacingSection(StreamEvent ev) => _startSection = ev;

    private void StartSpanner()
    {
        _spacing = MakeSpanner("SpacingSpanner", Nil.Instance);
        if (GetProperty(CurrentCommandColumn) is Grob col)
        {
            _spacing.SetBound(Direction.Negative, col);
        }
    }

    private void StopSpanner()
    {
        if (_spacing != null)
        {
            if (GetProperty(CurrentCommandColumn) is Grob p)
            {
                _spacing.SetBound(Direction.Positive, p);
            }

            _spacing = null;
        }
    }

    private void AddStarterDuration(GrobInfo info)
    {
        if (info.Grob.HasInterface(LyricSyllableInterface)
            || info.Grob.HasInterface(MultiMeasureInterface))
        {
            return;
        }

        /*
          only pay attention to durations that are not grace notes.
        */
        if (!_now.GracePart.IsNonZero)
        {
            StreamEvent r = info.EventCause;
            if (r != null && r.IsInEventClass(RhythmicEvent))
            {
                Rational len = GetEventLength(r).MainPart;
                _nowDurations.Add(new RhythmicTuple(info, _now.MainPart + len));
            }
        }
    }

    private void Insert(RhythmicTuple tuple)
    {
        int i = _playingDurations.Count;
        while (i > 0 && _playingDurations[i - 1].End > tuple.End)
        {
            i--;
        }

        _playingDurations.Insert(i, tuple);
    }

    private static Rational Min(Rational a, Rational b) => a < b ? a : b;
}

/// <summary>
/// Makes the <c>NoteSpacing</c> grobs: one per voice per timestep, linking what was
/// engraved at the last timestep to what is being engraved now.
/// <para>
/// Each grob is created holding THIS timestep's note columns as its left items, and is
/// then remembered; at the next timestep the columns engraved then are added to it as
/// its right items. That one-step delay is why the engraver keeps a per-parent-context
/// memory rather than a single field — polyphonic voices share a Staff and each needs
/// its own last-spacing.
/// </para>
/// </summary>
public class NoteSpacingEngraver : Engraver
{
    private static readonly Symbol LeftItems = Symbol.Intern("left-items");
    private static readonly Symbol RightItems = Symbol.Intern("right-items");
    private static readonly Symbol CurrentCommandColumn = Symbol.Intern("currentCommandColumn");
    private static readonly Symbol HasStaffSpacing = Symbol.Intern("hasStaffSpacing");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol RhythmicGrobInterface = Symbol.Intern("rhythmic-grob-interface");

    private readonly Dictionary<Context, Grob> _lastSpacings = new Dictionary<Context, Grob>();

    private Grob _lastSpacing;
    private Grob _spacing;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public NoteSpacingEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Note_spacing_engraver";

    /// <summary>Collects the note columns and rhythmic grobs of this timestep.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob is Item item
            && (item.HasInterface(NoteColumnInterface) || item.HasInterface(RhythmicGrobInterface)))
        {
            AddSpacingItem(item);
        }
    }

    /// <summary>
    /// Closes the last spacing grob by making it reach the current command column, and
    /// promotes this timestep's grob to be the last one.
    /// </summary>
    public override void StopTranslationTimestep()
    {
        Context parent = Context?.Parent;
        Grob lastSpacing = LastSpacingOf(parent);

        if (lastSpacing != null && SchemeUtilities.ToBool(GetProperty(HasStaffSpacing)))
        {
            if (GetProperty(CurrentCommandColumn) is Grob col)
            {
                PointerGroupInterface.AddGrob(lastSpacing, RightItems, col);
            }
        }

        if (_spacing != null)
        {
            if (parent != null)
            {
                _lastSpacings[parent] = _spacing;
            }

            _lastSpacing = _spacing;
            _spacing = null;
        }
    }

    /// <summary>
    /// Gives the last spacing grob a right-hand side when the voice simply stopped, so
    /// that it still states a distance rather than none at all.
    /// </summary>
    public override void FinalizeTranslation()
    {
        Context parent = Context?.Parent;
        Grob lastSpacing = LastSpacingOf(parent);

        if (lastSpacing != null && !(lastSpacing.GetObject(RightItems) is GrobArray))
        {
            if (GetProperty(CurrentCommandColumn) is Grob col)
            {
                PointerGroupInterface.AddGrob(lastSpacing, RightItems, col);
            }
        }
    }

    private Grob LastSpacingOf(Context parent)
        => parent != null && _lastSpacings.TryGetValue(parent, out Grob found) ? found : null;

    private void AddSpacingItem(Grob g)
    {
        if (_spacing == null)
        {
            _spacing = MakeItem("NoteSpacing", g);
        }

        if (_spacing != null)
        {
            PointerGroupInterface.AddGrob(_spacing, LeftItems, g);

            if (_lastSpacing != null)
            {
                PointerGroupInterface.AddGrob(_lastSpacing, RightItems, g);
            }
        }
    }
}

/// <summary>
/// Makes the <c>StaffSpacing</c> grobs and tells them which break-aligned symbols sit
/// on either side of them.
/// <para>
/// One is made per timestep that contains a non-musical item and whose context asks for
/// spacing, and it reaches from that timestep's command column to its musical column —
/// which is the span a clef or a bar line actually has to make room in.
/// </para>
/// </summary>
public class SeparatingLineGroupEngraver : Engraver
{
    private static readonly Symbol LeftItems = Symbol.Intern("left-items");
    private static readonly Symbol RightItems = Symbol.Intern("right-items");
    private static readonly Symbol LeftBreakAligned = Symbol.Intern("left-break-aligned");
    private static readonly Symbol RightBreakAligned = Symbol.Intern("right-break-aligned");
    private static readonly Symbol CurrentCommandColumn = Symbol.Intern("currentCommandColumn");
    private static readonly Symbol CurrentMusicalColumn = Symbol.Intern("currentMusicalColumn");
    private static readonly Symbol CreateSpacing = Symbol.Intern("createSpacing");
    private static readonly Symbol HasStaffSpacing = Symbol.Intern("hasStaffSpacing");
    private static readonly Symbol NoteSpacingInterface = Symbol.Intern("note-spacing-interface");
    private static readonly Symbol BreakAlignedInterface = Symbol.Intern("break-aligned-interface");

    /// <summary>
    /// The spacing grobs made in one timestep: at most one staff spacing, and whatever
    /// note spacings the voices below produced.
    /// </summary>
    private sealed class Spacings
    {
        internal Item StaffSpacing { get; set; }

        internal List<Item> NoteSpacings { get; private set; } = new List<Item>();

        internal bool IsEmpty => StaffSpacing == null && NoteSpacings.Count == 0;

        internal void Clear()
        {
            StaffSpacing = null;
            NoteSpacings = new List<Item>();
        }

        internal Spacings Snapshot() => new Spacings
        {
            StaffSpacing = StaffSpacing,
            NoteSpacings = NoteSpacings,
        };
    }

    private readonly List<Item> _breakAligned = new List<Item>();

    private Spacings _currentSpacings = new Spacings();
    private Spacings _lastSpacings = new Spacings();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public SeparatingLineGroupEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Separating_line_group_engraver";

    /// <summary>
    /// Collects note spacings, makes the timestep's staff spacing on the first
    /// non-musical item, and collects the break-aligned symbols.
    /// </summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!(info.Grob is Item it))
        {
            return;
        }

        if (it.HasInterface(BreakAlignedInterface))
        {
            _breakAligned.Add(it);
        }

        if (it.HasInterface(NoteSpacingInterface))
        {
            _currentSpacings.NoteSpacings.Add(it);
            return;
        }

        if (Item.IsNonMusical(it)
            && _currentSpacings.StaffSpacing == null
            && SchemeUtilities.ToBool(GetProperty(CreateSpacing)))
        {
            Grob col = GetProperty(CurrentCommandColumn) as Grob;

            _currentSpacings.StaffSpacing = MakeItem("StaffSpacing", Nil.Instance);
            Context?.SetProperty(HasStaffSpacing, true);

            PointerGroupInterface.AddGrob(_currentSpacings.StaffSpacing, LeftItems, col);

            if (_lastSpacings.NoteSpacings.Count == 0 && _lastSpacings.StaffSpacing != null)
            {
                // The previous timestep's staff spacing reached whatever the voices
                // produced. There were none, so it is REPLACED — not appended to — with
                // this column, which is the only thing there now is to reach.
                GrobArray ga = PointerGroupInterface.GetGrobArray(
                    _lastSpacings.StaffSpacing, RightItems);
                ga.Clear();
                if (col != null)
                {
                    ga.Add(col);
                }
            }
        }
    }

    /// <summary>Clears the per-timestep staff-spacing flag.</summary>
    public override void StartTranslationTimestep() => Context?.UnsetProperty(HasStaffSpacing);

    /// <summary>
    /// Hands every break-aligned symbol to the spacing grobs on either side of it, and
    /// closes the timestep's staff spacing at the musical column.
    /// </summary>
    public override void StopTranslationTimestep()
    {
        for (int i = 0; i < _breakAligned.Count; i++)
        {
            Item smob = _breakAligned[i];

            if (_currentSpacings.StaffSpacing is Item sp)
            {
                PointerGroupInterface.AddGrob(sp, LeftBreakAligned, smob);
            }

            for (int j = 0; j < _lastSpacings.NoteSpacings.Count; j++)
            {
                PointerGroupInterface.AddGrob(
                    _lastSpacings.NoteSpacings[j], RightBreakAligned, smob);
            }
        }

        if (!_currentSpacings.IsEmpty)
        {
            _lastSpacings = _currentSpacings.Snapshot();
        }

        if (_currentSpacings.StaffSpacing is Item staffSpacing)
        {
            if (GetProperty(CurrentMusicalColumn) is Grob col)
            {
                PointerGroupInterface.AddGrob(staffSpacing, RightItems, col);
            }
        }

        _currentSpacings.Clear();
        _breakAligned.Clear();
    }
}
