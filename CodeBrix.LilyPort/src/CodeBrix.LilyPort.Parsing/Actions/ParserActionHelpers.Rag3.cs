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
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/lily-parser.cc (init_papers, push_paper, pop_paper, set_paper);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <content>
/// The <c>$papers</c> stack helpers from <c>lily-parser.cc</c>, which the book rule
/// actions (RULE ACTION GROUP 3) drive: a <c>\book</c> body opens by resetting the
/// stack and pushing its own paper, keeps the top in step when a <c>\paper</c> block
/// replaces it, and pops on the way out. <c>get_paper</c> — the read side, which the
/// <c>paper_block</c> and <c>output_def_head</c> actions use — belongs to RAG4 and is
/// NOT ported here. Ported over the host seam, like <c>get_header</c> in the main
/// file; upstream's <c>lookup_identifier_symbol</c> is the folded
/// <see cref="IParserHost.LookupIdentifier"/>.
/// </content>
internal static partial class ParserActionHelpers
{
    private static readonly Symbol PapersSymbol = Symbol.Intern("$papers");

    /// <summary>
    /// Initializes (resets) the <c>$papers</c> stack to empty.
    /// <para>Upstream: <c>init_papers</c> in <c>lily-parser.cc</c>.</para>
    /// </summary>
    /// <param name="host">The parser host.</param>
    internal static void InitPapers(IParserHost host)
        => host.SetIdentifier(PapersSymbol, Nil.Instance);

    /// <summary>
    /// Pushes a paper on top of the <c>$papers</c> stack.
    /// <para>Upstream: <c>push_paper</c> in <c>lily-parser.cc</c>.</para>
    /// </summary>
    /// <param name="host">The parser host.</param>
    /// <param name="paper">The paper to push.</param>
    internal static void PushPaper(IParserHost host, OutputDef paper)
        => host.SetIdentifier(
            PapersSymbol,
            new Pair(paper, host.LookupIdentifier("$papers")));

    /// <summary>
    /// Pops a paper from the <c>$papers</c> stack; a non-pair stack is left alone,
    /// exactly as upstream.
    /// <para>Upstream: <c>pop_paper</c> in <c>lily-parser.cc</c>.</para>
    /// </summary>
    /// <param name="host">The parser host.</param>
    internal static void PopPaper(IParserHost host)
    {
        if (host.LookupIdentifier("$papers") is Pair papers)
        {
            host.SetIdentifier(PapersSymbol, papers.Cdr);
        }
    }

    /// <summary>
    /// Changes the paper on top of the <c>$papers</c> stack, IN PLACE — upstream is
    /// <c>scm_set_car_x</c>, so the pair already on the stack is mutated rather than
    /// replaced.
    /// <para>Upstream: <c>set_paper</c> in <c>lily-parser.cc</c>.</para>
    /// </summary>
    /// <param name="host">The parser host.</param>
    /// <param name="paper">The paper to store.</param>
    internal static void SetPaper(IParserHost host, OutputDef paper)
        => ((Pair)host.LookupIdentifier("$papers")).Car = paper;
}
