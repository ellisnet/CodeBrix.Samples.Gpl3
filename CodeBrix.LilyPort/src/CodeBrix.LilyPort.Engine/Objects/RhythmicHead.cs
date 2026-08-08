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

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/rhythmic-head.cc, lily/include/rhythmic-head.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// The statics shared by note heads and rests — anything with a duration that can carry
/// a dot and a stem.
/// <para>
/// Upstream's header also declares an <c>after_line_breaking</c> Scheme callback that
/// no translation unit defines; the declaration is dead upstream and is deliberately
/// not carried here. Recorded in PORT-COVERAGE.
/// </para>
/// </summary>
public static class RhythmicHead
{
    private static readonly Symbol DotSymbol = Symbol.Intern("dot");
    private static readonly Symbol StemSymbol = Symbol.Intern("stem");
    private static readonly Symbol DotCountSymbol = Symbol.Intern("dot-count");
    private static readonly Symbol DurationLogSymbol = Symbol.Intern("duration-log");

    /// <summary>Returns the head's dots item, when it has one.</summary>
    /// <param name="me">The rhythmic head.</param>
    /// <returns>The dots item, or <see langword="null"/>.</returns>
    public static Item GetDots(Grob me) => me.GetObject(DotSymbol) as Item;

    /// <summary>Returns the head's stem, when it has one.</summary>
    /// <param name="me">The rhythmic head.</param>
    /// <returns>The stem item, or <see langword="null"/>.</returns>
    public static Item GetStem(Grob me) => me.GetObject(StemSymbol) as Item;

    /// <summary>Returns how many augmentation dots the head carries.</summary>
    /// <param name="me">The rhythmic head.</param>
    /// <returns>The dot count, or zero when there are no dots.</returns>
    public static int DotCount(Grob me)
    {
        Item dots = GetDots(me);
        if (dots == null)
        {
            return 0;
        }

        object count = dots.GetProperty(DotCountSymbol);
        return SchemeConvert.IsNumber(count) ? SchemeConvert.ToInt(count, "dot-count") : 0;
    }

    /// <summary>Links a dots item to the head.</summary>
    /// <param name="me">The rhythmic head.</param>
    /// <param name="dot">The dots item.</param>
    public static void SetDots(Grob me, Item dot) => me.SetObject(DotSymbol, dot);

    // TODO: shouldn't this be in note-head-interface?  It also
    // declares duration-log in its properties.

    /// <summary>Returns the head's duration log, or zero when it declares none.</summary>
    /// <param name="me">The rhythmic head.</param>
    /// <returns>The duration log.</returns>
    public static int DurationLog(Grob me)
    {
        object s = me.GetProperty(DurationLogSymbol);
        return SchemeConvert.IsNumber(s) ? SchemeConvert.ToInt(s, "duration-log") : 0;
    }
}
