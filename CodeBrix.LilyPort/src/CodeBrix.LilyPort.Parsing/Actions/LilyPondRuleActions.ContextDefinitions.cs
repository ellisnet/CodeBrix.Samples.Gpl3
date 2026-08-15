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

using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 945-1019, 1602-1696, 2762-2766, 2857-2893);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <content>
/// Context definitions and modifications:
/// <c>context_def_spec_block</c> and <c>context_def_spec_body</c> (the
/// <c>\context { ... }</c> block that builds a <see cref="ContextDef"/>),
/// <c>context_modification</c>, <c>context_mod_list</c> and friends (the
/// <c>\with { ... }</c> block that builds a <see cref="ContextMod"/>),
/// <c>context_prefix</c> (<c>\context</c>/<c>\new</c> in front of music),
/// <c>context_change</c> (<c>\change</c>), the <c>context_def_mod</c> keyword
/// family, and the two mid-rule actions (<c>$@4</c>, <c>$@9</c>) those rules
/// carry. This is where <c>Context_def</c> comes from — the definitions the
/// engine's interim <c>Context.ContextFactory</c> seam stands in for.
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterContextDefinitions(RuleActionTable table)
    {
        // ------ context_def_spec_block (parser.yy 945-956) ------

        // context_def_spec_block: CONTEXT '{' context_def_spec_body '}' {
        //     $$ = $3; if not a Context_def, make one; td->origin ()->set_spot (@$); }
        table.Add(
            "context_def_spec_block: CONTEXT '{' context_def_spec_body '}'",
            (context, values, locations, location) =>
            {
                object result = values[2];
                ContextDef td = result as ContextDef;
                if (td == null)
                {
                    td = new ContextDef();
                    result = td;
                }

                td.SetSpot(
                    ParserActionHelpers.RequireHost(context).SchemeLocation(location));
                return result;
            });

        // ------ context_mod_arg (parser.yy 958-969) ------

        // The mid-rule action of `context_mod_arg: ... composite_music`:
        // { parser->lexer_->push_note_state (); } — run BEFORE the music is parsed.
        table.Add(
            "$@4: /* empty */",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushNoteState();
                return Unspecified.Instance;
            });

        // context_mod_arg: { push } composite_music {
        //     parser->lexer_->pop_state (); $$ = $2; }
        //
        // (The other alternative, `context_mod_arg: embedded_scm`, has no action
        // upstream and reduces by the $$ = $1 default.)
        table.Add(
            "context_mod_arg: $@4 composite_music",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PopLexerState();
                return values[1];
            });

        // ------ context_def_spec_body (parser.yy 972-1019) ------

        // context_def_spec_body: /* empty */ { $$ = SCM_UNSPECIFIED; }
        table.Add(
            "context_def_spec_body: /* empty */",
            (context, values, locations, location) => Unspecified.Instance);

        // context_def_spec_body: context_def_spec_body context_mod — a single mod
        // (unless SCM_UNDEFINED, an errored one) lands in the Context_def, which is
        // created on first need.
        table.Add(
            "context_def_spec_body: context_def_spec_body context_mod",
            (context, values, locations, location) =>
            {
                object result = values[0];
                if (!(values[1] is DefaultArgument))
                {
                    ContextDef td = result as ContextDef;
                    if (td == null)
                    {
                        td = new ContextDef();
                        result = td;
                    }

                    td.AddContextMod(values[1]);
                }

                return result;
            });

        // context_def_spec_body: context_def_spec_body context_modification — a
        // \with block inside \context { }: every one of its mods is added.
        table.Add(
            "context_def_spec_body: context_def_spec_body context_modification",
            (context, values, locations, location) =>
            {
                object result = values[0];
                ContextDef td = result as ContextDef;
                if (td == null)
                {
                    td = new ContextDef();
                    result = td;
                }

                object newMods = ((ContextMod)values[1]).GetMods();
                for (object m = newMods; m is Pair pair; m = pair.Cdr)
                {
                    td.AddContextMod(pair.Car);
                }

                return result;
            });

        // context_def_spec_body: context_def_spec_body context_mod_arg — a Scheme
        // value or music inside \context { }: SCM_UNSPECIFIED is ignored, a
        // Context_def replaces an empty body, music is converted through
        // context-mod-music-handler, and a Context_mod's mods are added.
        table.Add(
            "context_def_spec_body: context_def_spec_body context_mod_arg",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object result = values[0];
                ContextDef td = values[0] as ContextDef;
                object arg = values[1];

                if (arg is Unspecified)
                {
                }
                else if (td == null && arg is ContextDef)
                {
                    result = arg;
                }
                else
                {
                    if (td == null)
                    {
                        td = new ContextDef();
                        result = td;
                    }

                    if (arg is MusicObject)
                    {
                        object proc = host.LookupIdentifier("context-mod-music-handler");
                        arg = host.Call(proc, arg);
                    }

                    if (arg is ContextMod cm)
                    {
                        for (object m = cm.GetMods(); m is Pair pair; m = pair.Cdr)
                        {
                            td.AddContextMod(pair.Car);
                        }
                    }
                    else
                    {
                        ParserActionHelpers.ParserError(context, locations[1], "not a context mod");
                    }
                }

                return result;
            });

        // ------ context_modification (parser.yy 1602-1627) ------

        // The mid-rule action of `context_modification: WITH ... '{' ... '}'`:
        // { parser->lexer_->push_note_state (); }
        table.Add(
            "$@9: /* empty */",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushNoteState();
                return Unspecified.Instance;
            });

        // context_modification: WITH { push } '{' context_mod_list '}' {
        //     parser->lexer_->pop_state (); $$ = $4; }
        table.Add(
            "context_modification: WITH $@9 '{' context_mod_list '}'",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PopLexerState();
                return values[3];
            });

        // context_modification: WITH context_modification_arg — \with #... or
        // \with \musicIdentifier: music goes through context-mod-music-handler,
        // a Context_mod passes through, and anything else is an error — except
        // that \with #*unspecified* is permitted as an empty context mod.
        table.Add(
            "context_modification: WITH context_modification_arg",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object arg = values[1];
                if (arg is MusicObject)
                {
                    object proc = host.LookupIdentifier("context-mod-music-handler");
                    arg = host.Call(proc, arg);
                }

                if (arg is ContextMod)
                {
                    return arg;
                }

                // let's permit \with #*unspecified* to go for
                // an empty context mod
                if (!(arg is Unspecified))
                {
                    ParserActionHelpers.ParserError(context, locations[1], "not a context mod");
                }

                return new ContextMod();
            });

        // ------ optional_context_mods (parser.yy 1634-1661) ------

        /* A list of single mods collected from a (possibly empty) sequence of
         * context modifications, usually written as \with ... \with ...
         */

        // optional_context_mods: context_modification_mods_list {
        //     if (scm_is_pair ($1))
        //         $$ = scm_append_x (scm_reverse_x ($1, SCM_EOL)); }
        table.Add(
            "optional_context_mods: context_modification_mods_list",
            (context, values, locations, location) =>
            {
                if (values[0] is Pair)
                {
                    return ParserActionHelpers.AppendInPlace(
                        ParserActionHelpers.ReverseInPlace(values[0], Nil.Instance));
                }

                return values[0];
            });

        /* The worker for optional_context_mods conses a (reversed) list where
         * each element contains the list of single context mods from one
         * context modification block.  Context_mod::get_mods creates fresh
         * copies, so it's okay to use append! on them.
         */

        // context_modification_mods_list: /* empty */ { $$ = SCM_EOL; }
        table.Add(
            "context_modification_mods_list: /* empty */",
            (context, values, locations, location) => Nil.Instance);

        // context_modification_mods_list: context_modification_mods_list context_modification {
        //     if (Context_mod *m = unsmob<Context_mod> ($2))
        //         $$ = scm_cons (m->get_mods (), $1); }
        table.Add(
            "context_modification_mods_list: context_modification_mods_list context_modification",
            (context, values, locations, location) =>
            {
                if (values[1] is ContextMod m)
                {
                    return new Pair(m.GetMods(), values[0]);
                }

                return values[0];
            });

        // ------ context_mod_list (parser.yy 1663-1687) ------

        /* A Context_mod is a container for a list of context mods like
         * \consists ...  \override ... .  context_mod_list produces a
         * Context_mod from the inside of a \with { ... } statement.
         */

        // context_mod_list: /* empty */ { $$ = Context_mod ().smobbed_copy (); }
        table.Add(
            "context_mod_list: /* empty */",
            (context, values, locations, location) => new ContextMod());

        // context_mod_list: context_mod_list context_mod {
        //     if (!SCM_UNBNDP ($2))
        //         unsmob<Context_mod> ($1)->add_context_mod ($2); }
        table.Add(
            "context_mod_list: context_mod_list context_mod",
            (context, values, locations, location) =>
            {
                if (!(values[1] is DefaultArgument))
                {
                    ((ContextMod)values[0]).AddContextMod(values[1]);
                }

                return values[0];
            });

        // context_mod_list: context_mod_list context_mod_arg — a Scheme value or
        // music inside \with { }: music is converted through
        // context-mod-music-handler, a Context_mod's mods are merged, and anything
        // else but SCM_UNSPECIFIED is an error.
        table.Add(
            "context_mod_list: context_mod_list context_mod_arg",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object arg = values[1];
                if (arg is MusicObject)
                {
                    object proc = host.LookupIdentifier("context-mod-music-handler");
                    arg = host.Call(proc, arg);
                }

                if (arg is ContextMod source)
                {
                    ((ContextMod)values[0]).AddContextMods(source.GetMods());
                }
                else if (!(arg is Unspecified))
                {
                    ParserActionHelpers.ParserError(context, locations[1], "not a context mod");
                }

                return values[0];
            });

        // ------ context_prefix (parser.yy 1689-1696) ------

        // context_prefix: CONTEXT symbol optional_id optional_context_mods {
        //     $$ = START_MAKE_SYNTAX (context_find_or_create, $2, $3, $4); }
        //
        // START_MAKE_SYNTAX builds the list (constructor $2 $3 $4) WITHOUT calling
        // it; contexted_basic_music (the MusicAssembly group) finishes it with the music argument.
        table.Add(
            "context_prefix: CONTEXT symbol optional_id optional_context_mods",
            (context, values, locations, location)
                => Pair.List(
                    ParserActionHelpers.RequireHost(context)
                        .SyntaxConstructor("context-find-or-create"),
                    values[1],
                    values[2],
                    values[3]));

        // context_prefix: NEWCONTEXT symbol optional_id optional_context_mods {
        //     $$ = START_MAKE_SYNTAX (context_create, $2, $3, $4); }
        table.Add(
            "context_prefix: NEWCONTEXT symbol optional_id optional_context_mods",
            (context, values, locations, location)
                => Pair.List(
                    ParserActionHelpers.RequireHost(context)
                        .SyntaxConstructor("context-create"),
                    values[1],
                    values[2],
                    values[3]));

        // ------ context_change (parser.yy 2762-2766) ------

        // context_change: CHANGE symbol '=' simple_string {
        //     $$ = MAKE_SYNTAX (context_change, @$, $2, $4); }
        table.Add(
            "context_change: CHANGE symbol '=' simple_string",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).MakeSyntax(
                    "context-change", location, values[1], values[3]));

        // ------ context_def_mod (parser.yy 2857-2869) ------

        // context_def_mod: CONSISTS { $$ = ly_symbol2scm ("consists"); }
        table.Add(
            "context_def_mod: CONSISTS",
            (context, values, locations, location) => Symbol.Intern("consists"));

        // context_def_mod: REMOVE { $$ = ly_symbol2scm ("remove"); }
        table.Add(
            "context_def_mod: REMOVE",
            (context, values, locations, location) => Symbol.Intern("remove"));

        // context_def_mod: ACCEPTS { $$ = ly_symbol2scm ("accepts"); }
        table.Add(
            "context_def_mod: ACCEPTS",
            (context, values, locations, location) => Symbol.Intern("accepts"));

        // context_def_mod: DEFAULTCHILD { $$ = ly_symbol2scm ("default-child"); }
        table.Add(
            "context_def_mod: DEFAULTCHILD",
            (context, values, locations, location) => Symbol.Intern("default-child"));

        // context_def_mod: DENIES { $$ = ly_symbol2scm ("denies"); }
        table.Add(
            "context_def_mod: DENIES",
            (context, values, locations, location) => Symbol.Intern("denies"));

        // context_def_mod: ALIAS { $$ = ly_symbol2scm ("alias"); }
        table.Add(
            "context_def_mod: ALIAS",
            (context, values, locations, location) => Symbol.Intern("alias"));

        // context_def_mod: TYPE { $$ = ly_symbol2scm ("translator-type"); }
        table.Add(
            "context_def_mod: TYPE",
            (context, values, locations, location) => Symbol.Intern("translator-type"));

        // context_def_mod: DESCRIPTION { $$ = ly_symbol2scm ("description"); }
        table.Add(
            "context_def_mod: DESCRIPTION",
            (context, values, locations, location) => Symbol.Intern("description"));

        // context_def_mod: NAME { $$ = ly_symbol2scm ("context-name"); }
        table.Add(
            "context_def_mod: NAME",
            (context, values, locations, location) => Symbol.Intern("context-name"));

        // ------ context_mod (parser.yy 2871-2893) ------

        // context_mod: property_operation { $$ = $1; }
        table.Add(
            "context_mod: property_operation",
            (context, values, locations, location) => values[0]);

        // context_mod: context_def_mod STRING { $$ = ly_list ($1, $2); }
        table.Add(
            "context_mod: context_def_mod STRING",
            (context, values, locations, location) => Pair.List(values[0], values[1]));

        // context_mod: context_def_mod SYMBOL { $$ = ly_list ($1, $2); }
        table.Add(
            "context_mod: context_def_mod SYMBOL",
            (context, values, locations, location) => Pair.List(values[0], values[1]));

        // context_mod: context_def_mod embedded_scm — only \consists and \remove
        // accept a non-string argument (a translator or a symbol); everything else
        // errors to SCM_EOL.
        table.Add(
            "context_mod: context_def_mod embedded_scm",
            (context, values, locations, location) =>
            {
                if (!ParserActionHelpers.IsSchemeString(values[1])
                    && !ReferenceEquals(values[0], Symbol.Intern("consists"))
                    && !ReferenceEquals(values[0], Symbol.Intern("remove")))
                {
                    ParserActionHelpers.ParserError(
                        context,
                        locations[0],
                        "only \\consists and \\remove take non-string argument.");
                    return Nil.Instance;
                }

                return Pair.List(values[0], values[1]);
            });
    }
}
