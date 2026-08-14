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

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (epilogue: make_simple_markup);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <content>
/// The one epilogue helper the PostEvents group brings in.
/// </content>
internal static partial class ParserActionHelpers
{
    /// <summary>
    /// Returns a value as a markup — which, since a string IS a markup, is the value
    /// itself.
    /// <para>Upstream: <c>make_simple_markup</c> in <c>parser.yy</c>'s epilogue, whose
    /// whole body is <c>return a;</c>. It is ported rather than inlined because it is
    /// called from three groups (here, and <c>parser.yy</c> 4095 and 4321 in the MarkupStructure group),
    /// and because a reader who met a bare pass-through at those sites would
    /// reasonably wonder what had been dropped: nothing has. The name is upstream's,
    /// and the day it stops being the identity every call site is already routed
    /// through it.</para>
    /// </summary>
    /// <param name="value">The value to present as markup.</param>
    /// <returns>The markup.</returns>
    internal static object MakeSimpleMarkup(object value) => value;
}
