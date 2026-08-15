/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2007--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/tie-specification.cc, lily/include/tie-specification.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// What a tie grob ASKS for, as opposed to what the scorer decides: its own staff
/// position, the note heads it joins, and any placement the user pinned down by hand.
/// </summary>
public sealed class TieSpecification
{
    /// <summary>The staff position the tie's own note head sits at.</summary>
    public int Position;

    /// <summary>The note heads on each side, either of which may be absent.</summary>
    public DrulArray<Grob> NoteHeadDrul;

    /// <summary>The paper-column ranks the tie spans.</summary>
    public DrulArray<int> ColumnRanks;

    /// <summary>The tie grob this describes.</summary>
    public Grob TieGrob;

    /// <summary>Whether the user pinned the staff position.</summary>
    public bool HasManualPosition;

    /// <summary>Whether the user pinned the direction.</summary>
    public bool HasManualDir;

    /// <summary>Whether the pinned position is a fine offset rather than a whole step.</summary>
    public bool HasManualDeltaY;

    /// <summary>Whether the tie's note head carries an accidental.</summary>
    public bool HasAccidental;

    /// <summary>The pinned staff position.</summary>
    public double ManualPosition;

    /// <summary>The pinned direction.</summary>
    public Direction ManualDir;

    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");

    /// <summary>Initializes a specification with upstream's constructed defaults.</summary>
    public TieSpecification()
    {
        TieGrob = null;
        HasManualPosition = false;
        HasManualDir = false;
        HasManualDeltaY = false;
        Position = 0;
        ManualPosition = 0;
        ManualDir = Direction.Center;
        NoteHeadDrul = new DrulArray<Grob>(null, null);
        ColumnRanks = new DrulArray<int>(0, 0);
    }

    /// <summary>Reads the specification off a tie or semi-tie grob.</summary>
    /// <param name="tie">The grob.</param>
    public void FromGrob(Grob tie)
    {
        // In this method, Tie and Semi_tie require the same logic with different types.
        TieGrob = tie;
        if (SchemeConvert.IsNumber(tie.GetPropertyData(DirectionSymbol)))
        {
            ManualDir = DirectionalElementInterface.FromScheme(
                tie.GetProperty(DirectionSymbol), Direction.Center);
            HasManualDir = true;
        }

        if (tie is Spanner spanner)
        {
            Position = Tie.GetPosition(spanner);
        }
        else if (tie is Item item)
        {
            Position = SemiTie.GetPosition(item);
        }
        else
        {
            Warn.ProgrammingError("grob is neither a tie nor a semi-tie");
            Position = 0;
        }

        object posScm = tie.GetProperty(StaffPositionSymbol);
        if (SchemeConvert.IsNumber(posScm))
        {
            HasManualDeltaY = !SchemeConvert.IsExactOrInfiniteReal(posScm);
            ManualPosition = SchemeConvert.ToDouble(
                tie.GetProperty(StaffPositionSymbol), "staff-position");
            HasManualPosition = true;
        }
    }

    /// <summary>Returns how many paper columns the tie spans.</summary>
    /// <returns>The span, zero for a semi-tie.</returns>
    public int ColumnSpan() => ColumnRanks[Direction.Positive] - ColumnRanks[Direction.Negative];

    //was previously: a private copy of upstream's is_scm<Rational>, which decides whether
    // a pinned staff-position is a WHOLE step (exact — the tie moves to that position) or
    // a fine offset (inexact — the tie keeps its position and takes the difference as
    // delta_y). The rule now lives once, in SchemeConvert, because ly:moment-mul and
    // ly:moment-div apply the same check and two copies of one rule drift apart.
}
