/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2014--2026 Janek Warchoł <lemniskata.bernoullego@gmail.com>

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

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/clef-modifier.cc;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// The number describing the transposition of a clef — usually the 8 or 15 below or
/// above the sign. This class carries only the alignment logic; the digit itself is
/// drawn by <c>clef-modifier::print</c> in Scheme.
/// </summary>
public static class ClefModifier
{
    private static readonly Symbol GlyphSymbol = Symbol.Intern("glyph");
    private static readonly Symbol ClefAlignmentsSymbol = Symbol.Intern("clef-alignments");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");

    /// <summary>
    /// The <c>parent-alignment-X</c> callback: looks the clef's short name up in the
    /// modifier's <c>clef-alignments</c> alist and answers the alignment for the side
    /// the modifier sits on — car below the clef, cdr above — or centred when the
    /// clef has no entry.
    /// </summary>
    /// <param name="me">The <c>ClefModifier</c> grob.</param>
    /// <returns>The alignment number.</returns>
    public static object CalcParentAlignment(Grob me)
    {
        Grob clef = me.XParent;
        string fullClefName = clef != null && clef.GetProperty(GlyphSymbol) is MutableString glyph
            ? glyph.ToString()
            : string.Empty;
        string clefName = fullClefName.Replace("clefs.", string.Empty);

        // find entry with keyname clef_type in clef-alignments
        Pair alistEntry = SchemeUtilities.Assq(
            Symbol.Intern(clefName), me.GetProperty(ClefAlignmentsSymbol));

        if (alistEntry != null)
        {
            object entryValue = alistEntry.Cdr;

            // the value should be a pair of numbers - first is the alignment
            // for modifiers below the clef, second for those above.
            if (entryValue is Pair valuePair)
            {
                object directionValue = me.GetProperty(DirectionSymbol);
                Direction direction = Bootstrap.SchemeConvert.IsNumber(directionValue)
                    ? new Direction(Bootstrap.SchemeConvert.ToLong(directionValue, "direction"))
                    : Direction.Negative;

                return direction == Direction.Negative ? valuePair.Car : valuePair.Cdr;
            }

            // default alignment = centered
            return 0L;
        }

        // default alignment = centered
        return 0L;
    }
}
