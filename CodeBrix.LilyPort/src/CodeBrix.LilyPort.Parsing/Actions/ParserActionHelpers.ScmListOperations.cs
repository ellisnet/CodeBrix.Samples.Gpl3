/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
                 Jan Nieuwenhuizen <janneke@gnu.org>

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

using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (rule-action list operations);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <content>
/// The list operation the ContextDefinitions group added to the local
/// <c>scm_reverse_x</c>-family, per the partial-class convention on the main file.
/// </content>
internal static partial class ParserActionHelpers
{
    /// <summary>
    /// Destructively appends a list of lists, which is <c>scm_append_x</c> as
    /// <c>optional_context_mods</c> uses it: each sublist's last pair is spliced onto
    /// the next non-empty sublist, empty sublists disappear, and the result shares
    /// every pair with the input. Safe there because <c>Context_mod.GetMods</c>
    /// creates fresh copies — <c>parser.yy</c>'s own comment.
    /// </summary>
    /// <param name="lists">The list of lists; their pairs are reused.</param>
    /// <returns>The appended list.</returns>
    internal static object AppendInPlace(object lists)
    {
        object result = Nil.Instance;
        Pair tail = null;
        for (object cursor = lists; cursor is Pair outer; cursor = outer.Cdr)
        {
            if (!(outer.Car is Pair sublist))
            {
                continue;
            }

            if (tail == null)
            {
                result = sublist;
            }
            else
            {
                tail.Cdr = sublist;
            }

            Pair last = sublist;
            while (last.Cdr is Pair next)
            {
                last = next;
            }

            tail = last;
        }

        return result;
    }
}
