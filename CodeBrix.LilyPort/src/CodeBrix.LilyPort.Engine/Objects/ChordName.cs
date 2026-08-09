/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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

using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/chord-name.cc, lily/include/chord-name.hh;

/// <summary>A chord label (name or fretboard).</summary>
public static class ChordName
{
    private static readonly Symbol BeginOfLineVisibleSymbol
        = Symbol.Intern("begin-of-line-visible");

    /// <summary>
    /// The <c>after-line-breaking</c> callback: a chord label marked
    /// <c>begin-of-line-visible</c> survives only where it really is at the start of
    /// a line.
    /// </summary>
    /// <param name="me">The chord label.</param>
    /// <returns><c>*unspecified*</c>, as upstream.</returns>
    public static object AfterLineBreaking(Item me)
    {
        object s = me.GetProperty(BeginOfLineVisibleSymbol);
        if (SchemeUtilities.ToBool(s))
        {
            if (me.GetColumn().Rank
                    - me.GetSystem().SpannedColumnRankInterval()[Direction.Negative]
                > 1)
            {
                me.Suicide();
            }
        }

        return Unspecified.Instance;
    }
}
