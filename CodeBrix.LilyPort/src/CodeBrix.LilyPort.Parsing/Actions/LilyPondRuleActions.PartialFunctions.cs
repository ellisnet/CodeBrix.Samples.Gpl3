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

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 823-943);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <content>
/// Partial functions, <c>\etc</c>:
/// <c>partial_function_scriptable</c> and <c>partial_function</c>, the grammar behind
/// <c>\etc</c>. Each alternative conses one CALL ENTRY — <c>(function . arglist)</c>
/// for a music/event/scheme function, or a <c>(constructor arg...)</c> list for the
/// <c>\override</c>/<c>\set</c>/<c>\repeat</c>/script shorthands — onto the entries
/// collected so far, and the TopLevel group's <c>identifier_init_nonumber: partial_function ETC</c>
/// reverses the whole chain into <c>partial-music-function</c>. The
/// <c>partial_function: partial_function_scriptable</c> alternative is a pass-through
/// upstream leaves actionless, so it needs nothing here. The <c>Syntax::name</c>
/// sites cons the CONSTRUCTOR PROCEDURE without calling it —
/// <see cref="IParserHost.SyntaxConstructor"/>, the ContextDefinitions group precedent — because the
/// call happens later, inside <c>partial-music-function</c>'s chain.
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterPartialFunctions(RuleActionTable table)
    {
        // ------ partial_function_scriptable (parser.yy 823-860) ------
        //
        // scm_acons (key, value, tail) is scm_cons (scm_cons (key, value), tail):
        // the function and its (partially collected) arglist become one entry.

        // partial_function_scriptable: MUSIC_FUNCTION function_arglist_partial {
        //     $$ = scm_acons ($1, $2, SCM_EOL); }
        table.Add(
            "partial_function_scriptable: MUSIC_FUNCTION function_arglist_partial",
            (context, values, locations, location)
                => new Pair(new Pair(values[0], values[1]), Nil.Instance));

        // partial_function_scriptable: EVENT_FUNCTION function_arglist_partial {
        //     $$ = scm_acons ($1, $2, SCM_EOL); }
        table.Add(
            "partial_function_scriptable: EVENT_FUNCTION function_arglist_partial",
            (context, values, locations, location)
                => new Pair(new Pair(values[0], values[1]), Nil.Instance));

        // partial_function_scriptable: SCM_FUNCTION function_arglist_partial {
        //     $$ = scm_acons ($1, $2, SCM_EOL); }
        table.Add(
            "partial_function_scriptable: SCM_FUNCTION function_arglist_partial",
            (context, values, locations, location)
                => new Pair(new Pair(values[0], values[1]), Nil.Instance));

        // Here the function's LAST argument was already supplied by an inner partial
        // function ($4), whose entries become the tail of the chain.

        // partial_function_scriptable: MUSIC_FUNCTION EXPECT_SCM
        //         function_arglist_optional partial_function {
        //     $$ = scm_acons ($1, $3, $4); }
        table.Add(
            "partial_function_scriptable: MUSIC_FUNCTION EXPECT_SCM function_arglist_optional partial_function",
            (context, values, locations, location)
                => new Pair(new Pair(values[0], values[2]), values[3]));

        // partial_function_scriptable: EVENT_FUNCTION EXPECT_SCM
        //         function_arglist_optional partial_function {
        //     $$ = scm_acons ($1, $3, $4); }
        table.Add(
            "partial_function_scriptable: EVENT_FUNCTION EXPECT_SCM function_arglist_optional partial_function",
            (context, values, locations, location)
                => new Pair(new Pair(values[0], values[2]), values[3]));

        // partial_function_scriptable: SCM_FUNCTION EXPECT_SCM
        //         function_arglist_optional partial_function {
        //     $$ = scm_acons ($1, $3, $4); }
        table.Add(
            "partial_function_scriptable: SCM_FUNCTION EXPECT_SCM function_arglist_optional partial_function",
            (context, values, locations, location)
                => new Pair(new Pair(values[0], values[2]), values[3]));

        // partial_function_scriptable: MUSIC_FUNCTION EXPECT_OPTIONAL EXPECT_SCM
        //         function_arglist_nonbackup partial_function {
        //     $$ = scm_acons ($1, $4, $5); }
        table.Add(
            "partial_function_scriptable: MUSIC_FUNCTION EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup partial_function",
            (context, values, locations, location)
                => new Pair(new Pair(values[0], values[3]), values[4]));

        // partial_function_scriptable: EVENT_FUNCTION EXPECT_OPTIONAL EXPECT_SCM
        //         function_arglist_nonbackup partial_function {
        //     $$ = scm_acons ($1, $4, $5); }
        table.Add(
            "partial_function_scriptable: EVENT_FUNCTION EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup partial_function",
            (context, values, locations, location)
                => new Pair(new Pair(values[0], values[3]), values[4]));

        // partial_function_scriptable: SCM_FUNCTION EXPECT_OPTIONAL EXPECT_SCM
        //         function_arglist_nonbackup partial_function {
        //     $$ = scm_acons ($1, $4, $5); }
        table.Add(
            "partial_function_scriptable: SCM_FUNCTION EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup partial_function",
            (context, values, locations, location)
                => new Pair(new Pair(values[0], values[3]), values[4]));

        // ------ partial_function (parser.yy 862-943) ------
        //
        // (The `partial_function: partial_function_scriptable` alternative has no
        // action upstream and reduces by the $$ = $1 default.)
        //
        // The shorthand alternatives build (constructor arg...) LISTS rather than
        // (function . arglist) pairs — partial-music-function tells them apart with
        // list?. A bad property path from the PropertyPaths group's grob_prop_path/context_prop_spec
        // arrives as SCM_UNDEFINED and becomes the entry (#f), which makes
        // partial-music-function's `(every list? call-list)` answer good but its
        // signature lookup fail over #f exactly as upstream.

        // partial_function: OVERRIDE grob_prop_path '=' {
        //     if (SCM_UNBNDP ($2))
        //         $$ = ly_list (SCM_BOOL_F);
        //     else
        //         $$ = scm_cons (ly_list (Syntax::property_override,
        //                                 scm_cdr ($2), scm_car ($2)),
        //                        SCM_EOL); }
        //
        // NOTE the argument order: (property-override path-rest context), the
        // REVERSE of music_property_def's (context, rest, value) — the value slot is
        // what \etc leaves open, and partial-music-function conses it on the front.
        table.Add(
            "partial_function: OVERRIDE grob_prop_path '='",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (values[1] is DefaultArgument)
                {
                    return Pair.List(false);
                }

                Pair path = (Pair)values[1];
                return new Pair(
                    Pair.List(
                        host.SyntaxConstructor("property-override"), path.Cdr, path.Car),
                    Nil.Instance);
            });

        // partial_function: SET context_prop_spec '=' {
        //     if (SCM_UNBNDP ($2))
        //         $$ = ly_list (SCM_BOOL_F);
        //     else
        //         $$ = scm_cons (ly_list (Syntax::property_set,
        //                                 scm_cadr ($2), scm_car ($2)),
        //                        SCM_EOL); }
        table.Add(
            "partial_function: SET context_prop_spec '='",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (values[1] is DefaultArgument)
                {
                    return Pair.List(false);
                }

                Pair path = (Pair)values[1];
                return new Pair(
                    Pair.List(
                        host.SyntaxConstructor("property-set"),
                        ((Pair)path.Cdr).Car,
                        path.Car),
                    Nil.Instance);
            });

        // partial_function: OVERRIDE grob_prop_path '=' partial_function {
        //     if (SCM_UNBNDP ($2))
        //         $$ = ly_list (SCM_BOOL_F);
        //     else
        //         $$ = scm_cons (ly_list (Syntax::property_override,
        //                                 scm_cdr ($2), scm_car ($2)),
        //                        $4); }
        //
        // A bad path DROPS the inner chain — upstream's error value is the bare
        // (#f), not (#f . $4).
        table.Add(
            "partial_function: OVERRIDE grob_prop_path '=' partial_function",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (values[1] is DefaultArgument)
                {
                    return Pair.List(false);
                }

                Pair path = (Pair)values[1];
                return new Pair(
                    Pair.List(
                        host.SyntaxConstructor("property-override"), path.Cdr, path.Car),
                    values[3]);
            });

        // partial_function: SET context_prop_spec '=' partial_function {
        //     if (SCM_UNBNDP ($2))
        //         $$ = ly_list (SCM_BOOL_F);
        //     else
        //         $$ = scm_cons (ly_list (Syntax::property_set,
        //                                 scm_cadr ($2), scm_car ($2)),
        //                        $4); }
        table.Add(
            "partial_function: SET context_prop_spec '=' partial_function",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (values[1] is DefaultArgument)
                {
                    return Pair.List(false);
                }

                Pair path = (Pair)values[1];
                return new Pair(
                    Pair.List(
                        host.SyntaxConstructor("property-set"),
                        ((Pair)path.Cdr).Car,
                        path.Car),
                    values[3]);
            });

        // partial_function: REPEAT simple_string unsigned_integer {
        //     $$ = scm_cons (ly_list (Syntax::repeat, $3, $2), SCM_EOL); }
        //
        // The COUNT before the TYPE — Syntax::repeat's open slot is the body.
        table.Add(
            "partial_function: REPEAT simple_string unsigned_integer",
            (context, values, locations, location)
                => new Pair(
                    Pair.List(
                        ParserActionHelpers.RequireHost(context).SyntaxConstructor("repeat"),
                        values[2],
                        values[1]),
                    Nil.Instance));

        // partial_function: REPEAT simple_string unsigned_integer partial_function {
        //     $$ = scm_cons (ly_list (Syntax::repeat, $3, $2), $4); }
        table.Add(
            "partial_function: REPEAT simple_string unsigned_integer partial_function",
            (context, values, locations, location)
                => new Pair(
                    Pair.List(
                        ParserActionHelpers.RequireHost(context).SyntaxConstructor("repeat"),
                        values[2],
                        values[1]),
                    values[3]));

        // partial_function: REPEAT simple_string {
        //     $$ = scm_cons (ly_list (Syntax::repeat, $2), SCM_EOL); }
        table.Add(
            "partial_function: REPEAT simple_string",
            (context, values, locations, location)
                => new Pair(
                    Pair.List(
                        ParserActionHelpers.RequireHost(context).SyntaxConstructor("repeat"),
                        values[1]),
                    Nil.Instance));

        // partial_function: REPEAT simple_string partial_function {
        //     $$ = scm_cons (ly_list (Syntax::repeat, $2), $3); }
        table.Add(
            "partial_function: REPEAT simple_string partial_function",
            (context, values, locations, location)
                => new Pair(
                    Pair.List(
                        ParserActionHelpers.RequireHost(context).SyntaxConstructor("repeat"),
                        values[1]),
                    values[2]));

        // Upstream's comment: "Stupid duplication because we already expect ETC
        // here. It will follow anyway."

        // partial_function: script_dir markup_mode markup_partial_function {
        //     if (SCM_UNBNDP ($1))
        //         $1 = SCM_INUM0;
        //     $3 = MAKE_SYNTAX (partial_markup, @3, $3);
        //     parser->lexer_->pop_state ();
        //     // This relies on partial_function always being followed by ETC
        //     $$ = ly_list (ly_list (MAKE_SYNTAX (partial_text_script, @$, $3),
        //                            $3, $1)); }
        //
        // A '-' script_dir is SCM_UNDEFINED and becomes exact 0 — "no direction".
        // Both MAKE_SYNTAX dispatches CALL (unlike the Syntax::name conses above),
        // and the pop_state sits between them exactly as upstream wrote it. The
        // markup_mode nonterminal pushed the markup lexer state (a later group's
        // rule); this action pops it.
        table.Add(
            "partial_function: script_dir markup_mode markup_partial_function",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object direction = values[0] is DefaultArgument ? 0L : values[0];
                object partialMarkup = host.MakeSyntax(
                    "partial-markup", locations[2], values[2]);
                host.PopLexerState();

                // This relies on partial_function always being followed by ETC
                return Pair.List(
                    Pair.List(
                        host.MakeSyntax("partial-text-script", location, partialMarkup),
                        partialMarkup,
                        direction));
            });

        // partial_function: script_dir partial_function_scriptable {
        //     if (SCM_UNBNDP ($1))
        //         $1 = SCM_INUM0;
        //     $$ = scm_acons (Syntax::create_script_function, ly_list ($1), $2); }
        table.Add(
            "partial_function: script_dir partial_function_scriptable",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object direction = values[0] is DefaultArgument ? 0L : values[0];
                return new Pair(
                    new Pair(
                        host.SyntaxConstructor("create-script-function"),
                        Pair.List(direction)),
                    values[1]);
            });

        // partial_function: script_dir {
        //     if (SCM_UNBNDP ($1))
        //         $1 = SCM_INUM0;
        //     $$ = scm_acons (Syntax::create_script_function, ly_list ($1), SCM_EOL); }
        table.Add(
            "partial_function: script_dir",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object direction = values[0] is DefaultArgument ? 0L : values[0];
                return new Pair(
                    new Pair(
                        host.SyntaxConstructor("create-script-function"),
                        Pair.List(direction)),
                    Nil.Instance);
            });
    }
}
