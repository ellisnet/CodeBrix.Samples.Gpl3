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

using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Parsing.Driver;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 4156-4169, 4326-4339);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <content>
/// The one helper RULE ACTION GROUP 18 shares: the preparation a <c>\score</c> written
/// INSIDE markup needs before it becomes a markup expression.
/// </content>
internal static partial class ParserActionHelpers
{
    /// <summary>
    /// Locates a score written inside markup and gives it a layout definition if it
    /// brought none — the shared opening of <c>\score</c> and <c>\score-lines</c> in
    /// markup, which differ only in the markup expression they then build.
    /// <para>Upstream: the two bodies at <c>parser.yy</c> 4160-4168 and 4330-4338,
    /// which are identical down to their last two lines. <c>od-&gt;unprotect ()</c> is
    /// deliberately absent: it is Boehm-GC bookkeeping, one of the 201 measured call
    /// sites that delete rather than port under .NET's precise collector.</para>
    /// </summary>
    /// <param name="host">The parser host, for <see cref="GetLayout"/>'s
    /// <c>$defaultlayout</c> lookup.</param>
    /// <param name="score">The score the <c>score_body</c> reduced to.</param>
    /// <param name="location">The <c>@$</c> span the score is stamped with.</param>
    internal static void PrepareMarkupScore(IParserHost host, object score, SourceSpan location)
    {
        Score sc = (Score)score;
        sc.SetSpot(location);
        if (sc.Defs.Count == 0)
        {
            OutputDef od = GetLayout(host);
            sc.AddOutputDef(od);
        }
    }
}
