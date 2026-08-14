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

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 1834-1897, 2769-2856, 2895-3021);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <content>
/// Symbol lists, property paths and overrides: the
/// <c>symbol_list_*</c> family, <c>property_path</c> and <c>property_operation</c>,
/// the <c>revert_arg</c> machinery (whose <c>MYBACKUP</c> sites rewrite the parser's
/// own lookahead), <c>grob_prop_spec</c>/<c>grob_prop_path</c>,
/// <c>context_prop_spec</c>, <c>simple_revert_context</c> and
/// <c>music_property_def</c> — the grammar behind <c>\override</c>, <c>\revert</c>,
/// <c>\set</c> and <c>\unset</c>.
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterPropertyPaths(RuleActionTable table)
    {
        // ------ symbol_list_arg (parser.yy 1834-1844) ------
        //
        // A SYMBOL_LIST token is what revert_arg_backup's MYBACKUP pushes when it
        // already holds several path components; the '.'/',' alternatives let the
        // rest of the written path follow it.

        // symbol_list_arg: SYMBOL_LIST '.' symbol_list_rev {
        //     $$ = ly_append ($1, scm_reverse_x ($3, SCM_EOL)); }
        table.Add(
            "symbol_list_arg: SYMBOL_LIST '.' symbol_list_rev",
            (context, values, locations, location)
                => ParserActionHelpers.Append(
                    values[0],
                    ParserActionHelpers.ReverseInPlace(values[2], Nil.Instance)));

        // symbol_list_arg: SYMBOL_LIST ',' symbol_list_rev {
        //     $$ = ly_append ($1, scm_reverse_x ($3, SCM_EOL)); }
        table.Add(
            "symbol_list_arg: SYMBOL_LIST ',' symbol_list_rev",
            (context, values, locations, location)
                => ParserActionHelpers.Append(
                    values[0],
                    ParserActionHelpers.ReverseInPlace(values[2], Nil.Instance)));

        // ------ symbol_list_rev (parser.yy 1846-1857) ------

        // symbol_list_rev: symbol_list_rev '.' symbol_list_part {
        //     $$ = scm_append_x (ly_list ($3, $1)); } — the new part, itself in
        // reverse, is destructively appended IN FRONT of the accumulated reverse
        // list.
        table.Add(
            "symbol_list_rev: symbol_list_rev '.' symbol_list_part",
            (context, values, locations, location)
                => ParserActionHelpers.AppendInPlace(values[2], values[0]));

        // symbol_list_rev: symbol_list_rev ',' symbol_list_part {
        //     $$ = scm_append_x (ly_list ($3, $1)); }
        table.Add(
            "symbol_list_rev: symbol_list_rev ',' symbol_list_part",
            (context, values, locations, location)
                => ParserActionHelpers.AppendInPlace(values[2], values[0]));

        // ------ symbol_list_part (parser.yy 1859-1871) ------
        //
        // symbol_list_part delivers elements in reverse copy, no lookahead — the
        // property upstream's revert machinery depends on.

        // symbol_list_part: embedded_scm_bare {
        //     $$ = make_reverse_key_list ($1);
        //     if (SCM_UNBNDP ($$)) { parser->parser_error (@1, _("not a key"));
        //         $$ = SCM_EOL; } }
        table.Add(
            "symbol_list_part: embedded_scm_bare",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object result = ParserActionHelpers.MakeReverseKeyList(host, values[0]);
                if (result is DefaultArgument)
                {
                    ParserActionHelpers.ParserError(context, locations[0], "not a key");
                    return Nil.Instance;
                }

                return result;
            });

        // ------ symbol_list_element (parser.yy 1874-1880) ------

        // symbol_list_element: STRING { $$ = scm_string_to_symbol ($1); }
        table.Add(
            "symbol_list_element: STRING",
            (context, values, locations, location)
                => Symbol.Intern(ParserActionHelpers.SchemeStringText(values[0])));

        // ------ symbol_list_part_bare (parser.yy 1883-1897) ------

        // symbol_list_part_bare: SYMBOL {
        //     $$ = try_word_variants (Lily::key_list_p, $1);
        //     if (SCM_UNBNDP ($$)) { parser->parser_error (@1, _("not a key"));
        //         $$ = SCM_EOL; } else $$ = scm_reverse ($$); }
        //
        // With key_list_p the accepted variant is always a list, so the copying
        // scm_reverse is safe.
        table.Add(
            "symbol_list_part_bare: SYMBOL",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object result = ParserActionHelpers.TryWordVariants(host.IsKeyList, values[0]);
                if (result is DefaultArgument)
                {
                    ParserActionHelpers.ParserError(context, locations[0], "not a key");
                    return Nil.Instance;
                }

                return ParserActionHelpers.AppendReverse(result, Nil.Instance);
            });

        // symbol_list_part_bare: symbol_list_element { $$ = ly_list ($1); }
        table.Add(
            "symbol_list_part_bare: symbol_list_element",
            (context, values, locations, location) => new Pair(values[0], Nil.Instance));

        // ------ property_path (parser.yy 2769-2773) ------

        // property_path: symbol_list_rev { $$ = scm_reverse_x ($1, SCM_EOL); }
        table.Add(
            "property_path: symbol_list_rev",
            (context, values, locations, location)
                => ParserActionHelpers.ReverseInPlace(values[0], Nil.Instance));

        // ------ property_operation (parser.yy 2775-2796) ------

        // property_operation: symbol '=' scalar {
        //     $$ = ly_list (ly_symbol2scm ("assign"), $1, $3); }
        table.Add(
            "property_operation: symbol '=' scalar",
            (context, values, locations, location)
                => Pair.List(Symbol.Intern("assign"), values[0], values[2]));

        // property_operation: UNSET symbol {
        //     $$ = ly_list (ly_symbol2scm ("unset"), $2); }
        table.Add(
            "property_operation: UNSET symbol",
            (context, values, locations, location)
                => Pair.List(Symbol.Intern("unset"), values[1]));

        // property_operation: OVERRIDE revert_arg '=' scalar {
        //     if (scm_ilength ($2) < 2) {
        //         parser->parser_error (@2, _("bad grob property path"));
        //         $$ = SCM_UNDEFINED;
        //     } else {
        //         $$ = scm_cons (ly_symbol2scm ("push"),
        //                        scm_cons2 (scm_car ($2), $4, scm_cdr ($2))); } }
        table.Add(
            "property_operation: OVERRIDE revert_arg '=' scalar",
            (context, values, locations, location) =>
            {
                if (ParserActionHelpers.ListLength(values[1]) < 2)
                {
                    ParserActionHelpers.ParserError(context, locations[1], "bad grob property path");
                    return DefaultArgument.Instance;
                }

                Pair path = (Pair)values[1];

                // scm_cons2: ("push" grob scalar . rest-of-path)
                return new Pair(
                    Symbol.Intern("push"),
                    new Pair(path.Car, new Pair(values[3], path.Cdr)));
            });

        // property_operation: REVERT revert_arg {
        //     $$ = scm_cons (ly_symbol2scm ("pop"), $2); }
        table.Add(
            "property_operation: REVERT revert_arg",
            (context, values, locations, location)
                => new Pair(Symbol.Intern("pop"), values[1]));

        // ------ revert_arg (parser.yy 2798-2856) ------
        //
        // Upstream's comment, kept because the machinery is opaque without it:
        // "This is all quite awkward for the sake of substantial backward
        // compatibility while at the same time allowing a more 'natural' form of
        // specification not separating grob specification from grob property
        // path. The purpose of this definition of revert_arg is to allow the
        // symbol list which specifies grob and property to revert to be
        // optionally split into two parts after the grob (which in this case is
        // just the first element of the list). symbol_list_part is only one path
        // component, but it can be parsed without lookahead, so we can follow it
        // with a synthetic BACKUP token when needed. If the first
        // symbol_list_part already contains multiple elements (only possible if
        // a Scheme expression provides them), we just parse for additional
        // elements introduced by '.', which is what the SYMBOL_LIST backup in
        // connection with the immediately following rule using symbol_list_arg
        // does. ... This is for both allowing the traditional
        // \revert Accidental #'color as well as the 'naive' form
        // \revert Accidental.color"

        // revert_arg: revert_arg_backup BACKUP symbol_list_arg { $$ = $3; }
        table.Add(
            "revert_arg: revert_arg_backup BACKUP symbol_list_arg",
            (context, values, locations, location) => values[2]);

        // revert_arg_backup: revert_arg_part {
        //     if (scm_is_null ($1) || scm_is_null (scm_cdr ($1)))
        //         MYBACKUP (SCM_ARG, $1, @1);
        //     else
        //         MYBACKUP (SYMBOL_LIST, scm_reverse_x ($1, SCM_EOL), @1); }
        //
        // No $$ assignment: Bison's implicit $$ = $1 stands (the value is never
        // consumed — both uses of revert_arg_backup ignore it).
        table.Add(
            "revert_arg_backup: revert_arg_part",
            (context, values, locations, location) =>
            {
                if (values[0] is Nil || ((Pair)values[0]).Cdr is Nil)
                {
                    ParserActionHelpers.MyBackup(context, "SCM_ARG", values[0], locations[0]);
                }
                else
                {
                    ParserActionHelpers.MyBackup(
                        context,
                        "SYMBOL_LIST",
                        ParserActionHelpers.ReverseInPlace(values[0], Nil.Instance),
                        locations[0]);
                }

                return values[0];
            });

        // revert_arg_part delivers results in reverse.

        // revert_arg_part: revert_arg_backup BACKUP SCM_ARG '.' symbol_list_part {
        //     $$ = scm_append_x (ly_list ($5, $3)); }
        table.Add(
            "revert_arg_part: revert_arg_backup BACKUP SCM_ARG '.' symbol_list_part",
            (context, values, locations, location)
                => ParserActionHelpers.AppendInPlace(values[4], values[2]));

        // revert_arg_part: revert_arg_backup BACKUP SCM_ARG ',' symbol_list_part {
        //     $$ = scm_append_x (ly_list ($5, $3)); }
        table.Add(
            "revert_arg_part: revert_arg_backup BACKUP SCM_ARG ',' symbol_list_part",
            (context, values, locations, location)
                => ParserActionHelpers.AppendInPlace(values[4], values[2]));

        // revert_arg_part: revert_arg_backup BACKUP SCM_ARG symbol_list_part {
        //     $$ = scm_append_x (ly_list ($4, $3));
        //     property_path_dot_warning (@4, scm_reverse ($$)); }
        table.Add(
            "revert_arg_part: revert_arg_backup BACKUP SCM_ARG symbol_list_part",
            (context, values, locations, location) =>
            {
                object result = ParserActionHelpers.AppendInPlace(values[3], values[2]);
                ParserActionHelpers.PropertyPathDotWarning(
                    ParserActionHelpers.RequireHost(context),
                    locations[3],
                    ParserActionHelpers.AppendReverse(result, Nil.Instance));
                return result;
            });

        // ------ grob_prop_spec / grob_prop_path (parser.yy 2895-2938) ------

        // grob_prop_spec: symbol_list_rev { $$ = scm_reverse_x ($1, SCM_EOL); }
        table.Add(
            "grob_prop_spec: symbol_list_rev",
            (context, values, locations, location)
                => ParserActionHelpers.ReverseInPlace(values[0], Nil.Instance));

        // If defined, at least three members.
        //
        // grob_prop_path: grob_prop_spec {
        //     if (scm_is_pair ($1) && from_scm<bool> (scm_object_property
        //             (scm_car ($1), ly_symbol2scm ("is-grob?"))))
        //         $$ = scm_cons (ly_symbol2scm ("Bottom"), $1);
        //     if (!scm_is_pair ($$) || !scm_is_pair (scm_cdr ($$))
        //         || !scm_is_pair (scm_cddr ($$))) {
        //         parser->parser_error (@1, _ ("bad grob property path"));
        //         $$ = SCM_UNDEFINED; } }
        //
        // Bison pre-sets $$ = $1, so the second test runs over the original list
        // when no Bottom was prepended.
        table.Add(
            "grob_prop_path: grob_prop_spec",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object result = values[0];
                if (values[0] is Pair spec && host.IsGrobSymbol(spec.Car))
                {
                    result = new Pair(Symbol.Intern("Bottom"), values[0]);
                }

                if (!(result is Pair first)
                    || !(first.Cdr is Pair second)
                    || !(second.Cdr is Pair))
                {
                    ParserActionHelpers.ParserError(context, locations[0], "bad grob property path");
                    return DefaultArgument.Instance;
                }

                return result;
            });

        // grob_prop_path: grob_prop_spec property_path {
        //     ... same Bottom insertion; the spec must then be EXACTLY two long
        //     and the path non-empty, and joining the two earns the deprecation
        //     warning for the missing dot:
        //     property_path_dot_warning (@2, ly_append ($1, $2));
        //     $$ = scm_append_x (ly_list ($$, $2)); }
        table.Add(
            "grob_prop_path: grob_prop_spec property_path",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object result = values[0];
                if (values[0] is Pair spec && host.IsGrobSymbol(spec.Car))
                {
                    result = new Pair(Symbol.Intern("Bottom"), values[0]);
                }

                if (!(result is Pair first)
                    || !(first.Cdr is Pair second)
                    || second.Cdr is Pair
                    || !(values[1] is Pair))
                {
                    ParserActionHelpers.ParserError(context, locations[0], "bad grob property path");
                    return DefaultArgument.Instance;
                }

                ParserActionHelpers.PropertyPathDotWarning(
                    host,
                    locations[1],
                    ParserActionHelpers.Append(values[0], values[1]));
                return ParserActionHelpers.AppendInPlace(result, values[1]);
            });

        // ------ context_prop_spec (parser.yy 2940-2956) ------

        // Exactly two elements or undefined.
        //
        // context_prop_spec: symbol_list_rev {
        //     SCM l = scm_reverse_x ($1, SCM_EOL);
        //     switch (scm_ilength (l)) {
        //     case 1: l = scm_cons (ly_symbol2scm ("Bottom"), l);
        //     case 2: break;
        //     default: parser->parser_error (@1, _ ("bad context property path"));
        //         l = SCM_UNDEFINED; }
        //     $$ = l; }
        //
        // Note the C fallthrough: case 1 prepends Bottom and then falls into
        // case 2's break.
        table.Add(
            "context_prop_spec: symbol_list_rev",
            (context, values, locations, location) =>
            {
                object list = ParserActionHelpers.ReverseInPlace(values[0], Nil.Instance);
                switch (ParserActionHelpers.ListLength(list))
                {
                    case 1:
                        list = new Pair(Symbol.Intern("Bottom"), list);
                        break;
                    case 2:
                        break;
                    default:
                        ParserActionHelpers.ParserError(
                            context, locations[0], "bad context property path");
                        list = DefaultArgument.Instance;
                        break;
                }

                return list;
            });

        // ------ simple_revert_context (parser.yy 2958-2989) ------
        //
        // Upstream's comment: "simple_revert_context just caters for the context
        // and delegates the rest of the job to revert_arg" — the first path
        // component either IS the context, or names a grob, in which case Bottom
        // is supplied and the whole component is handed on. Either way the
        // remainder is pushed back into the token stream as an SCM_IDENTIFIER
        // for revert_arg to consume.

        // simple_revert_context: symbol_list_part {
        //     $1 = scm_reverse_x ($1, SCM_EOL);
        //     if (scm_is_null ($1) || from_scm<bool> (scm_object_property
        //             (scm_car ($1), ly_symbol2scm ("is-grob?")))) {
        //         $$ = ly_symbol2scm ("Bottom");
        //         parser->lexer_->push_extra_token (@1, SCM_IDENTIFIER, $1);
        //     } else {
        //         $$ = scm_car ($1);
        //         parser->lexer_->push_extra_token (@1, SCM_IDENTIFIER,
        //                                           scm_cdr ($1)); } }
        //
        // Upstream reaches this reduce with NO lookahead read ("symbol_list_part
        // ... can be parsed without lookahead") — and since the driver reproduces
        // Bison's lazy lookahead, so does the port: the pushed SCM_IDENTIFIER is
        // the next token the parser sees.
        table.Add(
            "simple_revert_context: symbol_list_part",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object list = ParserActionHelpers.ReverseInPlace(values[0], Nil.Instance);

                if (list is Nil || host.IsGrobSymbol(((Pair)list).Car))
                {
                    ParserActionHelpers.PushExtraToken(
                        context, locations[0], "SCM_IDENTIFIER", list);
                    return Symbol.Intern("Bottom");
                }

                Pair pair = (Pair)list;
                ParserActionHelpers.PushExtraToken(
                    context, locations[0], "SCM_IDENTIFIER", pair.Cdr);
                return pair.Car;
            });

        // ------ music_property_def (parser.yy 2991-3021) ------

        // music_property_def: OVERRIDE grob_prop_path '=' scalar {
        //     if (SCM_UNBNDP ($2))
        //         $$ = MAKE_SYNTAX (unspecified_music, @$);
        //     else
        //         $$ = MAKE_SYNTAX (property_override, @$,
        //                           scm_car ($2), scm_cdr ($2), $4); }
        table.Add(
            "music_property_def: OVERRIDE grob_prop_path '=' scalar",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (values[1] is DefaultArgument)
                {
                    return host.MakeSyntax("unspecified-music", location);
                }

                Pair path = (Pair)values[1];
                return host.MakeSyntax(
                    "property-override", location, path.Car, path.Cdr, values[3]);
            });

        // music_property_def: REVERT simple_revert_context revert_arg {
        //     $$ = MAKE_SYNTAX (property_revert, @$, $2, $3); }
        table.Add(
            "music_property_def: REVERT simple_revert_context revert_arg",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).MakeSyntax(
                    "property-revert", location, values[1], values[2]));

        // music_property_def: SET context_prop_spec '=' scalar {
        //     if (SCM_UNBNDP ($2))
        //         $$ = MAKE_SYNTAX (unspecified_music, @$);
        //     else
        //         $$ = MAKE_SYNTAX (property_set, @$,
        //                           scm_car ($2), scm_cadr ($2), $4); }
        table.Add(
            "music_property_def: SET context_prop_spec '=' scalar",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (values[1] is DefaultArgument)
                {
                    return host.MakeSyntax("unspecified-music", location);
                }

                Pair path = (Pair)values[1];
                return host.MakeSyntax(
                    "property-set", location, path.Car, ((Pair)path.Cdr).Car, values[3]);
            });

        // music_property_def: UNSET context_prop_spec {
        //     if (SCM_UNBNDP ($2))
        //         $$ = MAKE_SYNTAX (unspecified_music, @$);
        //     else
        //         $$ = MAKE_SYNTAX (property_unset, @$,
        //                           scm_car ($2), scm_cadr ($2)); }
        table.Add(
            "music_property_def: UNSET context_prop_spec",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (values[1] is DefaultArgument)
                {
                    return host.MakeSyntax("unspecified-music", location);
                }

                Pair path = (Pair)values[1];
                return host.MakeSyntax(
                    "property-unset", location, path.Car, ((Pair)path.Cdr).Car);
            });
    }
}
