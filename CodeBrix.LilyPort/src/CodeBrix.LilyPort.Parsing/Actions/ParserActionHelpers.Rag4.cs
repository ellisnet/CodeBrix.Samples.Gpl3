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
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/lily-parser.cc (get_layout, get_midi, get_paper), lily/output-def.cc (assign_context_def);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <content>
/// The output-definition helpers the RULE ACTION GROUP 4 rules drive: the three
/// <c>get_*</c> constructors from <c>lily-parser.cc</c> that an
/// <c>output_def_head</c> opens a block with — <c>get_paper</c> being the READ side
/// of the <c>$papers</c> stack whose write side
/// (<see cref="InitPapers"/>/<see cref="PushPaper"/>/<see cref="PopPaper"/>/
/// <see cref="SetPaper"/>) is in the Rag3 file — plus <c>assign_context_def</c> from
/// <c>output-def.cc</c>, the one helper this group calls that upstream keeps in a
/// third file. Ported over the host seam like the Rag3 helpers; upstream's
/// <c>lookup_identifier_symbol</c> is the folded
/// <see cref="IParserHost.LookupIdentifier"/>.
/// </content>
internal static partial class ParserActionHelpers
{
    /// <summary>
    /// Builds the output definition a <c>\layout</c> head opens: a clone of
    /// <c>$defaultlayout</c> when one is in scope, else a fresh definition, marked
    /// with <c>output-def-kind</c> <c>layout</c>.
    /// <para>Upstream: <c>get_layout</c> in <c>lily-parser.cc</c>, which carries the
    /// comment "TODO: use a member in the Output_def. Can't do that for now, because
    /// the current paper code uses the output def module, not the output def
    /// itself." — the kind stays a scope variable here for the same reason.</para>
    /// </summary>
    /// <param name="host">The parser host.</param>
    /// <returns>The definition, ready to open a scope on.</returns>
    internal static OutputDef GetLayout(IParserHost host)
    {
        object id = host.LookupIdentifier("$defaultlayout");
        OutputDef layout = id as OutputDef;
        layout = layout != null ? layout.Clone() : new OutputDef();
        layout.SetVariable(Symbol.Intern("output-def-kind"), Symbol.Intern("layout"));
        return layout;
    }

    /// <summary>
    /// Builds the output definition a <c>\midi</c> head opens: a clone of
    /// <c>$defaultmidi</c> when one is in scope, else a fresh definition, marked
    /// with <c>output-def-kind</c> <c>midi</c>.
    /// <para>Upstream: <c>get_midi</c> in <c>lily-parser.cc</c>.</para>
    /// </summary>
    /// <param name="host">The parser host.</param>
    /// <returns>The definition, ready to open a scope on.</returns>
    internal static OutputDef GetMidi(IParserHost host)
    {
        object id = host.LookupIdentifier("$defaultmidi");
        OutputDef layout = id as OutputDef;
        layout = layout != null ? layout.Clone() : new OutputDef();
        layout.SetVariable(Symbol.Intern("output-def-kind"), Symbol.Intern("midi"));
        return layout;
    }

    /// <summary>
    /// Builds the output definition a <c>\paper</c> head opens — upstream's comment:
    /// "Return a copy of the top of $papers stack, or $defaultpaper if the stack is
    /// empty". With neither in scope the definition is fresh; either way it is
    /// marked with <c>output-def-kind</c> <c>paper</c>.
    /// <para>Upstream: <c>get_paper</c> in <c>lily-parser.cc</c>. A <c>$papers</c>
    /// binding that is neither unset, empty nor a pair is a wrong-type error at
    /// <c>scm_car</c> upstream and an <see cref="System.InvalidCastException"/>
    /// here — the same failure, new spelling.</para>
    /// </summary>
    /// <param name="host">The parser host.</param>
    /// <returns>The definition, ready to open a scope on.</returns>
    internal static OutputDef GetPaper(IParserHost host)
    {
        object papers = host.LookupIdentifier("$papers");
        OutputDef layout = (papers is DefaultArgument || papers is Nil)
            ? null
            : ((Pair)papers).Car as OutputDef;
        object defaultPaper = host.LookupIdentifier("$defaultpaper");
        layout = layout ?? defaultPaper as OutputDef;
        layout = layout != null ? layout.Clone() : new OutputDef();
        layout.SetVariable(Symbol.Intern("output-def-kind"), Symbol.Intern("paper"));
        return layout;
    }

    /// <summary>
    /// Stores a context definition into an output definition under the context's own
    /// name — how <c>\context { ... }</c> inside a <c>\layout</c> or <c>\midi</c>
    /// block registers itself.
    /// <para>Upstream: <c>assign_context_def</c> in <c>lily/output-def.cc</c>.
    /// Upstream both asserts and guards its unsmob; the port keeps the release-build
    /// behaviour, where a non-definition is silently ignored — every call site has
    /// already tested the value anyway.</para>
    /// </summary>
    /// <param name="m">The output definition written into.</param>
    /// <param name="transdef">The context definition to store.</param>
    internal static void AssignContextDef(OutputDef m, object transdef)
    {
        ContextDef tp = transdef as ContextDef;
        if (tp != null)
        {
            object sym = tp.ContextName;
            m.SetVariable((Symbol)sym, transdef);
        }
    }
}
