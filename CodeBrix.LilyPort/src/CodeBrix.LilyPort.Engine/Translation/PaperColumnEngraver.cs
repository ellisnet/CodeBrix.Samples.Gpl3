/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/paper-column-engraver.cc, lily/include/paper-column-engraver.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.
// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - upstream's three specific acknowledgers are restored beside the plain item one:
//     staff-spacing and note-spacing wishes go onto the columns' spacing-wishes, and
//     the BreakAlignment is stored as the command column's break-alignment object.
//     Without them, every wish-based spacing correction read empty sets from the start,
//     and break_align_width answered points. See PORT-COVERAGE.
//   - upstream's three-way split in StopTranslationTimestep is restored: an accidental
//     placement or an arpeggio is a CONDITIONAL item, an individual accidental goes on
//     no list at all, and everything else is a plain item. The note that stood here
//     said add_conditional_item was unported and that Paper_column::minimum_distance
//     omitted the matching skyline half; both had since landed and nothing re-checked
//     the note (trap 18). Its cost was that a tied, unforced accidental -- a break
//     reminder, which upstream only charges for when it starts a line -- reserved its
//     width in every column. See PORT-COVERAGE.

/// <summary>
/// Creates the paper columns: the horizontal positions everything else hangs off.
/// <para>
/// Two columns are made per timestep, always as a PAIR — a <c>NonMusicalPaperColumn</c>
/// for things that sit between notes (clefs, bar lines, key signatures) and a
/// <c>PaperColumn</c> for the notes themselves. Every item announced in the timestep is
/// given one of them as its horizontal parent in
/// <see cref="StopTranslationTimestep"/>, chosen by whether the item is non-musical.
/// </para>
/// <para>
/// The columns are made in <see cref="PreProcessMusic"/> rather than in
/// <see cref="Translator.ProcessMusic"/> precisely so that every other engraver's
/// <c>process-music</c> can already read <c>currentCommandColumn</c>.
/// </para>
/// </summary>
public class PaperColumnEngraver : Engraver
{
    private static readonly Symbol RootSystemSymbol = Symbol.Intern("rootSystem");
    private static readonly Symbol CurrentCommandColumnSymbol = Symbol.Intern("currentCommandColumn");
    private static readonly Symbol CurrentMusicalColumnSymbol = Symbol.Intern("currentMusicalColumn");
    private static readonly Symbol LineBreakPermissionSymbol = Symbol.Intern("line-break-permission");
    private static readonly Symbol PageBreakPermissionSymbol = Symbol.Intern("page-break-permission");
    private static readonly Symbol PageTurnPermissionSymbol = Symbol.Intern("page-turn-permission");
    private static readonly Symbol AllowSymbol = Symbol.Intern("allow");
    private static readonly Symbol WhenSymbol = Symbol.Intern("when");
    private static readonly Symbol SkipTypesettingSymbol = Symbol.Intern("skipTypesetting");
    private static readonly Symbol MeasurePositionSymbol = Symbol.Intern("measurePosition");
    private static readonly Symbol InternalBarNumberSymbol = Symbol.Intern("internalBarNumber");
    private static readonly Symbol RhythmicLocationSymbol = Symbol.Intern("rhythmic-location");
    private static readonly Symbol AxisGroupParentX = Symbol.Intern("axis-group-parent-X");
    private static readonly Symbol AccidentalPlacementInterface
        = Symbol.Intern("accidental-placement-interface");
    private static readonly Symbol AccidentalInterfaceSymbol
        = Symbol.Intern("accidental-interface");
    private static readonly Symbol ArpeggioInterface = Symbol.Intern("arpeggio-interface");
    private static readonly Symbol PaperColumnSymbol = Symbol.Intern("Paper_column");
    private static readonly Symbol ForbidBreakSymbol = Symbol.Intern("forbidBreak");
    private static readonly Symbol ForceBreakSymbol = Symbol.Intern("forceBreak");
    private static readonly Symbol ScoreSymbol = Symbol.Intern("Score");
    private static readonly Symbol BreakEventSymbol = Symbol.Intern("break-event");
    private static readonly Symbol LabelEventSymbol = Symbol.Intern("label-event");
    private static readonly Symbol ClassSymbol = Symbol.Intern("class");
    private static readonly Symbol BreakPenaltySymbol = Symbol.Intern("break-penalty");
    private static readonly Symbol BreakPermissionSymbol = Symbol.Intern("break-permission");
    private static readonly Symbol PageLabelSymbol = Symbol.Intern("page-label");
    private static readonly Symbol LabelsSymbol = Symbol.Intern("labels");
    private static readonly Symbol MeasureStartNowSymbol = Symbol.Intern("measureStartNow");
    private static readonly Symbol MeasureLengthGrobSymbol = Symbol.Intern("measure-length");
    private static readonly Symbol StaffSpacingInterfaceSymbol
        = Symbol.Intern("staff-spacing-interface");
    private static readonly Symbol NoteSpacingInterfaceSymbol
        = Symbol.Intern("note-spacing-interface");
    private static readonly Symbol BreakAlignmentInterfaceSymbol
        = Symbol.Intern("break-alignment-interface");
    private static readonly Symbol SpacingWishesSymbol = Symbol.Intern("spacing-wishes");
    private static readonly Symbol BreakAlignmentObjectSymbol = Symbol.Intern("break-alignment");

    private readonly List<Item> _items = new List<Item>();
    private readonly List<StreamEvent> _breakEvents = new List<StreamEvent>();
    private readonly List<StreamEvent> _labelEvents = new List<StreamEvent>();

    private SystemGrob _system;
    private PaperColumn _commandColumn;
    private PaperColumn _musicalColumn;
    private bool _skipTypesettingAtStartOfTimestep;
    private bool _firstTime = true;
    private bool _haveTiming;
    private int _breaks;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public PaperColumnEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Paper_column_engraver";

    /// <summary>Gets the non-musical column of the current timestep.</summary>
    public PaperColumn CommandColumn => _commandColumn;

    /// <summary>Gets the musical column of the current timestep.</summary>
    public PaperColumn MusicalColumn => _musicalColumn;

    /// <summary>Finds the root system the columns are appended to.</summary>
    public override void Initialize()
    {
        _system = GetProperty(RootSystemSymbol) as SystemGrob;
    }

    /// <summary>Starts listening for the break and label events a score writes by hand.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(BreakEventSymbol, ListenBreak);
        ListenTo(LabelEventSymbol, ListenLabel);
    }

    /// <summary>
    /// Records whether typesetting is being skipped, for the whole timestep, and drops
    /// the previous timestep's break events.
    /// </summary>
    public override void StartTranslationTimestep()
    {
        _breakEvents.Clear();
        _skipTypesettingAtStartOfTimestep
            = SchemeUtilities.ToBool(GetProperty(SkipTypesettingSymbol));
    }

    /// <summary>Makes this timestep's pair of columns and publishes them.</summary>
    public override void PreProcessMusic()
    {
        if (_firstTime)
        {
            _firstTime = false;

            // internalBarNumber is evidence that Timing_translator is working in this
            // context. Not finding it means that we are processing a polymetric score.
            _haveTiming = Context?.WhereDefined(InternalBarNumberSymbol, out object _) != null;
        }

        /* Use the value of skipTypesetting at the start of this time step.
           The effect is that columns are created at the beginning of a
           skipped section, and when music stops being skipped, the columns
           used are those created at the beginning of the skipped section.
           DOCME: why is this necessary? */
        if (!_skipTypesettingAtStartOfTimestep)
        {
            _commandColumn = MakeColumn("NonMusicalPaperColumn");
            Context?.SetProperty(CurrentCommandColumnSymbol, _commandColumn);
            _system?.AddColumn(_commandColumn);

            _musicalColumn = MakeColumn("PaperColumn");
            Context?.SetProperty(CurrentMusicalColumnSymbol, _musicalColumn);
            _system?.AddColumn(_musicalColumn);

            if (_system != null && _system.GetBound(Direction.Negative) == null)
            {
                // first time step
                _system.SetBound(Direction.Negative, _commandColumn);
                _commandColumn.SetProperty(LineBreakPermissionSymbol, AllowSymbol);
            }
        }

        //was previously: an early RETURN under skipTypesetting, which took the manual
        // breaks with it. Upstream guards only the COLUMN MAKING and then calls
        // handle_manual_breaks (false) UNCONDITIONALLY (paper-column-engraver.cc:126-150
        // — the call is outside the if). The distinction is the whole of
        // skiptypesetting-break: a \break in a timestep that STARTED skipped attaches to
        // the column made at the beginning of the skipped section, which is exactly what
        // the retained column is for, and dropping it merged the file's two systems into
        // one.
        HandleManualBreaks(false);
    }

    /// <summary>
    /// Hangs this timestep's page labels on the command column, and records the measure
    /// length in the first command column of every measure.
    /// </summary>
    public override void ProcessMusic()
    {
        foreach (StreamEvent labelEvent in _labelEvents)
        {
            object label = labelEvent.GetProperty(PageLabelSymbol);
            object labels = _commandColumn?.GetProperty(LabelsSymbol);
            _commandColumn?.SetProperty(LabelsSymbol, new Pair(label, labels ?? Nil.Instance));
        }

        // Upstream's own note, kept: this cannot be done in start_translation_timestep
        // because meter changes may occur between there and here. In polymetric scores
        // measureStartNow is never set in this context, so a legacy condition stands in
        // -- upstream's TODO, and its issue #4633, come across with it.
        bool measureStartNow
            = _haveTiming
                ? SchemeUtilities.ToBool(GetProperty(MeasureStartNowSymbol))
                : !MeasureTiming.MeasurePosition(Context).MainPart.IsNonZero;

        if (measureStartNow)
        {
            Moment mlen = new Moment(MeasureTiming.MeasureLength(Context));
            if (GetProperty(CurrentCommandColumnSymbol) is Grob column)
            {
                column.SetProperty(MeasureLengthGrobSymbol, mlen);
            }
            else
            {
                Warn.ProgrammingError("No command column?");
            }
        }
    }

    /// <summary>
    /// Applies every manual <c>\break</c>, <c>\noBreak</c>, <c>\pageBreak</c>,
    /// <c>\pageTurn</c> and their no- and allow- siblings to the command column —
    /// <c>Paper_column_engraver::handle_manual_breaks</c>.
    /// <para>
    /// LATE-PORTED at the line-breaking close-out, and invisible before it: with no
    /// permission-stripping block in <see cref="StopTranslationTimestep"/> every column
    /// was breakable anyway, so a score asking for a break got one by accident and a
    /// score forbidding one was ignored. The strip landed with the broken-spanner
    /// carry-forward stall fix, which is what made the gap visible.
    /// </para>
    /// </summary>
    /// <param name="onlyDoPermissions">
    /// When <see langword="true"/>, penalties are ignored and only the event's
    /// permission is applied — the shape <c>finalize</c> needs, where the score's end
    /// has already granted its own permissions.
    /// </param>
    private void HandleManualBreaks(bool onlyDoPermissions)
    {
        foreach (StreamEvent breakEvent in _breakEvents)
        {
            string name = (breakEvent.GetProperty(ClassSymbol) is Pair classes
                           && classes.Car is Symbol nameSym)
                ? nameSym.Name
                : null;
            int end = name != null ? name.LastIndexOf("-event", System.StringComparison.Ordinal) : -1;
            if (end <= 0)
            {
                Warn.ProgrammingError(
                    "Paper_column_engraver doesn't know about this break-event");
                return;
            }

            string prefix = name.Substring(0, end);
            Symbol permSymbol = Symbol.Intern(prefix + "-permission");
            Symbol penSymbol = Symbol.Intern(prefix + "-penalty");

            object currentPenalty = _commandColumn?.GetProperty(penSymbol);
            object penalty = breakEvent.GetProperty(BreakPenaltySymbol);
            object permission = breakEvent.GetProperty(BreakPermissionSymbol);
            bool forceBreakPermission;

            if (!onlyDoPermissions && Bootstrap.SchemeConvert.IsNumber(penalty))
            {
                double newPenalty
                    = ReadPenalty(currentPenalty)
                      + Bootstrap.SchemeConvert.ToDouble(penalty, "break-penalty");
                _commandColumn?.SetProperty(penSymbol, newPenalty);
                _commandColumn?.SetProperty(permSymbol, AllowSymbol);
                forceBreakPermission = true;
            }
            else
            {
                _commandColumn?.SetProperty(permSymbol, permission ?? Nil.Instance);
                forceBreakPermission = !(permission is null || permission is Nil);
            }

            if (forceBreakPermission)
            {
                Context?.SetProperty(ForceBreakSymbol, true);
            }
        }
    }

    private static double ReadPenalty(object value)
        => Bootstrap.SchemeConvert.IsNumber(value)
            ? Bootstrap.SchemeConvert.ToDouble(value, "break-penalty")
            : 0.0;

    private void ListenBreak(StreamEvent ev) => _breakEvents.Add(ev);

    private void ListenLabel(StreamEvent ev) => _labelEvents.Add(ev);

    /// <summary>
    /// Collects every item announced this timestep, and carries upstream's three
    /// specific acknowledgers besides the plain item one: a StaffSpacing goes onto the
    /// command column's <c>spacing-wishes</c>, a NoteSpacing onto the musical
    /// column's, and the BreakAlignment is stored as the command column's
    /// <c>break-alignment</c> object — which is what
    /// <c>Paper_column::break_align_width</c> measures a staff line's ends against.
    /// </summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob is Item item && !(item is PaperColumn))
        {
            _items.Add(item);
        }

        if (_commandColumn != null && info.Grob.HasInterface(StaffSpacingInterfaceSymbol))
        {
            PointerGroupInterface.AddGrob(_commandColumn, SpacingWishesSymbol, info.Grob);
        }

        if (_musicalColumn != null && info.Grob.HasInterface(NoteSpacingInterfaceSymbol))
        {
            PointerGroupInterface.AddGrob(_musicalColumn, SpacingWishesSymbol, info.Grob);
        }

        if (info.Grob.HasInterface(BreakAlignmentInterfaceSymbol))
        {
            _commandColumn?.SetObject(BreakAlignmentObjectSymbol, info.Grob);
        }
    }

    /// <summary>
    /// Stamps the columns with the moment, and gives every collected item its
    /// horizontal parent.
    /// </summary>
    public override void StopTranslationTimestep()
    {
        if (SchemeUtilities.ToBool(GetProperty(SkipTypesettingSymbol)))
        {
            return;
        }

        // It would be safe to set "when" earlier, but there is no obvious need.
        Moment now = NowMoment;
        _commandColumn?.SetProperty(WhenSymbol, now);
        _musicalColumn?.SetProperty(WhenSymbol, now);

        object measurePosition = GetProperty(MeasurePositionSymbol);
        object barNumber = GetProperty(InternalBarNumberSymbol);
        if (measurePosition is Moment && Bootstrap.SchemeConvert.IsNumber(barNumber))
        {
            object where = new Pair(barNumber, measurePosition);
            _commandColumn?.SetProperty(RhythmicLocationSymbol, where);
            _musicalColumn?.SetProperty(RhythmicLocationSymbol, where);
        }

        foreach (Item element in _items)
        {
            PaperColumn column = Item.IsNonMusical(element) ? _commandColumn : _musicalColumn;
            if (column == null)
            {
                continue;
            }

            if (element.XParent == null)
            {
                element.XParent = column;
            }

            if (!(element.GetObject(AxisGroupParentX) is Grob))
            {
                element.SetObject(AxisGroupParentX, column);
            }

            // An accidental placement and an arpeggio are CONDITIONAL items: whether
            // they occupy space depends on the column they are measured against, so
            // they go on `conditional-elements` and Separation_item::boxes filters them
            // through Accidental_placement::get_relevant_accidentals. A tied, unforced
            // accidental is a break REMINDER -- it is only shown, and only costs width,
            // when it starts a line -- and putting the placement on the ordinary
            // `elements` list is what made the port reserve its width everywhere.
            //
            // An individual Accidental is added to NEITHER list, deliberately: the
            // placement already accounts for it, and adding it here would count it
            // twice.
            if (element.HasInterface(AccidentalPlacementInterface)
                || element.HasInterface(ArpeggioInterface))
            {
                SeparationItem.AddConditionalItem(column, element);
            }
            else if (!element.HasInterface(AccidentalInterfaceSymbol))
            {
                SeparationItem.AddItem(column, element);
            }
        }

        _items.Clear();

        if (!Translation.Context.BreakAllowed(Context)
            && _breaks != 0) /* don't honour forbidBreak if it occurs on the first moment of a score */
        {
            _commandColumn?.SetProperty(PageTurnPermissionSymbol, Nil.Instance);
            _commandColumn?.SetProperty(PageBreakPermissionSymbol, Nil.Instance);
            _commandColumn?.SetProperty(LineBreakPermissionSymbol, Nil.Instance);
        }
        else if (_commandColumn != null && PaperColumn.IsBreakable(_commandColumn))
        {
            _breaks++;
        }

        Context score = Context?.FindContextAbove(ScoreSymbol);
        score?.UnsetProperty(ForbidBreakSymbol);
        score?.UnsetProperty(ForceBreakSymbol);

        _labelEvents.Clear();
    }

    /// <summary>
    /// Closes the score: the last command column allows every kind of break, and the
    /// system ends there.
    /// </summary>
    public override void FinalizeTranslation()
    {
        if (_commandColumn == null)
        {
            return;
        }

        // At the end of the score, allow page breaks and turns by default, but...
        _commandColumn.SetProperty(PageBreakPermissionSymbol, AllowSymbol);
        _commandColumn.SetProperty(PageTurnPermissionSymbol, AllowSymbol);

        // ...allow the user to override them.
        HandleManualBreaks(true);

        // On the other hand, line breaks are always allowed at the end of a score,
        // even if they try to stop us.
        if (!(_commandColumn.GetProperty(LineBreakPermissionSymbol) is Symbol))
        {
            _commandColumn.SetProperty(LineBreakPermissionSymbol, AllowSymbol);
        }

        _system?.SetBound(Direction.Positive, _commandColumn);
    }
}
