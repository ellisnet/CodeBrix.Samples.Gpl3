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

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

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
    private static readonly Symbol PaperColumnSymbol = Symbol.Intern("Paper_column");
    private static readonly Symbol ForbidBreakSymbol = Symbol.Intern("forbidBreak");
    private static readonly Symbol ForceBreakSymbol = Symbol.Intern("forceBreak");
    private static readonly Symbol ScoreSymbol = Symbol.Intern("Score");

    private readonly List<Item> _items = new List<Item>();

    private SystemGrob _system;
    private PaperColumn _commandColumn;
    private PaperColumn _musicalColumn;
    private bool _skipTypesettingAtStartOfTimestep;
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

    /// <summary>Records whether typesetting is being skipped, for the whole timestep.</summary>
    public override void StartTranslationTimestep()
        => _skipTypesettingAtStartOfTimestep
            = SchemeUtilities.ToBool(GetProperty(SkipTypesettingSymbol));

    /// <summary>Makes this timestep's pair of columns and publishes them.</summary>
    public override void PreProcessMusic()
    {
        /* Use the value of skipTypesetting at the start of this time step.
           The effect is that columns are created at the beginning of a
           skipped section, and when music stops being skipped, the columns
           used are those created at the beginning of the skipped section.
           DOCME: why is this necessary? */
        if (_skipTypesettingAtStartOfTimestep)
        {
            return;
        }

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

    /// <summary>Collects every item announced this timestep.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob is Item item && !(item is PaperColumn))
        {
            _items.Add(item);
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

            // Upstream splits accidentals and arpeggios off as CONDITIONAL items, which
            // needs Separation_item::add_conditional_item -- not ported, and paired with
            // the skyline half that Paper_column::minimum_distance also omits. Every
            // other item takes the same route it does upstream.
            SeparationItem.AddItem(column, element);
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

        // On the other hand, line breaks are always allowed at the end of a score,
        // even if they try to stop us.
        if (!(_commandColumn.GetProperty(LineBreakPermissionSymbol) is Symbol))
        {
            _commandColumn.SetProperty(LineBreakPermissionSymbol, AllowSymbol);
        }

        _system?.SetBound(Direction.Positive, _commandColumn);
    }
}
