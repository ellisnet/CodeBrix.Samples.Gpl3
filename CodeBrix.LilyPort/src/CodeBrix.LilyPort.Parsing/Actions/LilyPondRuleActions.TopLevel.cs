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
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 412-819);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <content>
/// Top level, identifiers and headers: <c>start_symbol</c>,
/// <c>lilypond</c>, <c>toplevel_expression</c>, <c>lookup</c>, the header family,
/// <c>assignment</c> and <c>identifier_init</c>, plus the three mid-rule actions
/// (<c>$@1</c>–<c>$@3</c>) those rules carry. This is the file's own spine — a
/// LilyPond file IS a sequence of toplevel expressions and assignments.
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterTopLevel(RuleActionTable table)
    {
        // ------ start_symbol (parser.yy 412-420) ------

        // The mid-rule action of `start_symbol: EMBEDDED_LILY ...`:
        // { parser->lexer_->push_note_state (); } — run BEFORE embedded_lilypond is
        // parsed, which is the whole point of it being mid-rule.
        table.Add(
            "$@1: /* empty */",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushNoteState();
                return Unspecified.Instance;
            });

        // start_symbol: EMBEDDED_LILY { push } embedded_lilypond {
        //     parser->lexer_->pop_state (); *retval = $3; }
        //
        // Upstream hands $3 out through the *retval parse parameter and leaves $$
        // alone. The port's Parse() result IS retval, so the action returns $3 —
        // recorded in PORT-COVERAGE.
        table.Add(
            "start_symbol: EMBEDDED_LILY $@1 embedded_lilypond",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PopLexerState();
                return values[2];
            });

        // ------ lilypond (parser.yy 422-433) ------

        // lilypond: /* empty */ { $$ = SCM_UNSPECIFIED; }
        table.Add(
            "lilypond: /* empty */",
            (context, values, locations, location) => Unspecified.Instance);

        // lilypond: lilypond toplevel_expression { } — an EMPTY body, so Bison's
        // implicit $$ = $1 is the whole behaviour.
        table.Add(
            "lilypond: lilypond toplevel_expression",
            (context, values, locations, location) => values[0]);

        // lilypond: lilypond assignment { } — same empty body.
        table.Add(
            "lilypond: lilypond assignment",
            (context, values, locations, location) => values[0]);

        // lilypond: lilypond error { parser->error_level_ = 1; }
        //
        // The recovery point that keeps one bad construct from costing the rest of the
        // file. It records that the file was bad WITHOUT stopping: LilyPond reports
        // every error in a file, not just the first, and this is where that is decided.
        table.Add(
            "lilypond: lilypond error",
            (context, values, locations, location) =>
            {
                if (context.UserState is IParserErrorLevel state)
                {
                    state.ErrorLevel = 1;
                }

                return values[0];
            });

        // lilypond: lilypond INVALID { parser->error_level_ = 1; }
        table.Add(
            "lilypond: lilypond INVALID",
            (context, values, locations, location) =>
            {
                if (context.UserState is IParserErrorLevel state)
                {
                    state.ErrorLevel = 1;
                }

                return values[0];
            });

        // ------ toplevel_expression (parser.yy 436-524) ------

        // toplevel_expression: header_block {
        //     parser->lexer_->set_identifier ("$defaultheader", $1); }
        table.Add(
            "toplevel_expression: header_block",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context)
                    .SetIdentifier(Symbol.Intern("$defaultheader"), values[0]);
                return values[0];
            });

        // toplevel_expression: book_block — look the handler up and call it, so the
        // Scheme layer decides what a finished book becomes.
        table.Add(
            "toplevel_expression: book_block",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.Call(host.LookupIdentifier("toplevel-book-handler"), values[0]);
                return values[0];
            });

        // toplevel_expression: bookpart_block
        table.Add(
            "toplevel_expression: bookpart_block",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.Call(host.LookupIdentifier("toplevel-bookpart-handler"), values[0]);
                return values[0];
            });

        // toplevel_expression: BOOK_IDENTIFIER — a \bookIdentifier at top level is
        // dispatched as a book when it has its own paper block, else as a bookpart.
        table.Add(
            "toplevel_expression: BOOK_IDENTIFIER",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                string handler = host.BookHasPaper(values[0])
                    ? "toplevel-book-handler"
                    : "toplevel-bookpart-handler";
                host.Call(host.LookupIdentifier(handler), values[0]);
                return values[0];
            });

        // toplevel_expression: score_block
        table.Add(
            "toplevel_expression: score_block",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.Call(host.LookupIdentifier("toplevel-score-handler"), values[0]);
                return values[0];
            });

        // toplevel_expression: composite_music
        table.Add(
            "toplevel_expression: composite_music",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.Call(host.LookupIdentifier("toplevel-music-handler"), values[0]);
                return values[0];
            });

        // toplevel_expression: full_markup — the handler receives a LIST holding the
        // one markup; full_markup_list below passes its list through as-is.
        table.Add(
            "toplevel_expression: full_markup",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.Call(
                    host.LookupIdentifier("toplevel-text-handler"),
                    new Pair(values[0], Nil.Instance));
                return values[0];
            });

        // toplevel_expression: full_markup_list
        table.Add(
            "toplevel_expression: full_markup_list",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.Call(host.LookupIdentifier("toplevel-text-handler"), values[0]);
                return values[0];
            });

        // toplevel_expression: SCM_TOKEN {
        //     // Evaluate and ignore #xxx, as opposed to \xxx
        //     parser->lexer_->eval_scm_token ($1, @1); }
        table.Add(
            "toplevel_expression: SCM_TOKEN",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context)
                    .EvalSchemeToken(values[0], locations[0]);
                return values[0];
            });

        // toplevel_expression: embedded_scm_active — classify what a #-expression at
        // top level produced: markup(-list), score, output definition, header module,
        // or nothing; anything else is a bad expression type.
        table.Add(
            "toplevel_expression: embedded_scm_active",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object value = values[0];

                object markup = DefaultArgument.Instance;
                if (host.IsMarkup(value))
                {
                    markup = new Pair(value, Nil.Instance);
                }
                else if (host.IsMarkupList(value))
                {
                    markup = value;
                }

                if (markup is Pair)
                {
                    host.Call(host.LookupIdentifier("toplevel-text-handler"), markup);
                }
                else if (host.IsScore(value))
                {
                    host.Call(host.LookupIdentifier("toplevel-score-handler"), value);
                }
                else if (value is OutputDef outputDef)
                {
                    object id = Nil.Instance;
                    object kind = outputDef.CVariable("output-def-kind");
                    if (ReferenceEquals(kind, Symbol.Intern("paper")))
                    {
                        id = Symbol.Intern("$defaultpaper");
                    }
                    else if (ReferenceEquals(kind, Symbol.Intern("midi")))
                    {
                        id = Symbol.Intern("$defaultmidi");
                    }
                    else if (ReferenceEquals(kind, Symbol.Intern("layout")))
                    {
                        id = Symbol.Intern("$defaultlayout");
                    }

                    host.SetIdentifier(id, value);
                }
                else if (host.IsModule(value))
                {
                    object module = ParserActionHelpers.GetHeader(host);
                    host.ModuleCopy(module, value);
                    host.SetIdentifier(Symbol.Intern("$defaultheader"), module);
                }
                else if (!(value is Unspecified))
                {
                    ParserActionHelpers.ParserError(context, locations[0], "bad expression type");
                }

                return values[0];
            });

        // toplevel_expression: output_def — \paper, \midi and \layout at top level
        // become the session defaults.
        table.Add(
            "toplevel_expression: output_def",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                OutputDef outputDef = (OutputDef)values[0];

                object id = Nil.Instance;
                object kind = outputDef.CVariable("output-def-kind");
                if (ReferenceEquals(kind, Symbol.Intern("paper")))
                {
                    id = Symbol.Intern("$defaultpaper");
                }
                else if (ReferenceEquals(kind, Symbol.Intern("midi")))
                {
                    id = Symbol.Intern("$defaultmidi");
                }
                else if (ReferenceEquals(kind, Symbol.Intern("layout")))
                {
                    id = Symbol.Intern("$defaultlayout");
                }

                host.SetIdentifier(id, values[0]);
                return values[0];
            });

        // ------ lookup (parser.yy 526-567) ------

        // lookup: LOOKUP_IDENTIFIER '.' symbol_list_rev {
        //     $$ = loc_on_copy (parser, @$,
        //                       nested_property ($1, scm_reverse_x ($3, SCM_EOL))); }
        table.Add(
            "lookup: LOOKUP_IDENTIFIER '.' symbol_list_rev",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                return host.LocOnCopy(
                    NestedProperty.Get(
                        values[0],
                        ParserActionHelpers.ReverseInPlace(values[2], Nil.Instance),
                        Nil.Instance),
                    location);
            });

        // lookup: MODULE_IDENTIFIER '.' symbol_list_rev — walk modules along the
        // path until a non-module or the path's end, then fall back to the alist
        // implementation for whatever remains.
        table.Add(
            "lookup: MODULE_IDENTIFIER '.' symbol_list_rev",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object value = values[0];
                object path = ParserActionHelpers.ReverseInPlace(values[2], Nil.Instance);

                // Look up modules until we find a non-module or reach the end
                // of the path.
                while (true)
                {
                    if (!host.TryModuleVariable(value, ((Pair)path).Car, out object found))
                    {
                        ParserActionHelpers.ParserError(context, location, "not found");
                        value = DefaultArgument.Instance;
                        break;
                    }

                    value = found;
                    path = ((Pair)path).Cdr;
                    if (!(path is Pair))
                    {
                        // Return the found value, whatever it is.
                        break;
                    }

                    if (!host.IsModule(value))
                    {
                        // We have reached the end of the modules but not the end of
                        // the path.  Drop into the alist implementation.
                        value = NestedProperty.Get(value, path, Nil.Instance);
                        break;
                    }
                }

                return host.LocOnCopy(value, location);
            });

        // ------ the header family (parser.yy 692-733) ------

        // lilypond_header_body: /* empty */ { $$ = SCM_UNSPECIFIED; }
        table.Add(
            "lilypond_header_body: /* empty */",
            (context, values, locations, location) => Unspecified.Instance);

        // lilypond_header_body: lilypond_header_body assignment { } — empty body;
        // the assignment already landed in the header scope.
        table.Add(
            "lilypond_header_body: lilypond_header_body assignment",
            (context, values, locations, location) => values[0]);

        // lilypond_header_body: lilypond_header_body SCM_TOKEN {
        //     // Evaluate and ignore #xxx, as opposed to \xxx
        //     parser->lexer_->eval_scm_token ($2, @2); }
        table.Add(
            "lilypond_header_body: lilypond_header_body SCM_TOKEN",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context)
                    .EvalSchemeToken(values[1], locations[1]);
                return values[0];
            });

        // lilypond_header_body: lilypond_header_body embedded_scm_active — a module
        // is merged into the header scope; anything else but SCM_UNSPECIFIED is a
        // bad expression type.
        table.Add(
            "lilypond_header_body: lilypond_header_body embedded_scm_active",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object value = values[1];
                if (host.IsModule(value))
                {
                    host.ModuleCopy(host.CurrentModule(), value);
                }
                else if (!(value is Unspecified))
                {
                    ParserActionHelpers.ParserError(context, locations[1], "bad expression type");
                }

                return values[0];
            });

        // lilypond_header: HEADER '{' lilypond_header_body '}' {
        //     $$ = parser->lexer_->remove_scope (); }
        table.Add(
            "lilypond_header: HEADER '{' lilypond_header_body '}'",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).RemoveScope());

        // The mid-rule action of header_block:
        // { parser->lexer_->add_scope (get_header (parser)); } — a block-scope
        // header RETAINS values defined earlier, so it opens on a copy of
        // $defaultheader.
        table.Add(
            "$@2: /* empty */",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.AddScope(ParserActionHelpers.GetHeader(host));
                return Unspecified.Instance;
            });

        // header_block: { add_scope (get_header) } lilypond_header { $$ = $2; }
        table.Add(
            "header_block: $@2 lilypond_header",
            (context, values, locations, location) => values[1]);

        // The mid-rule action of header_modification:
        // { parser->lexer_->add_scope (ly_make_module ()); } — an assignment-side
        // header is initialized CLEAN, holding only its own values.
        table.Add(
            "$@3: /* empty */",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.AddScope(host.MakeModule());
                return Unspecified.Instance;
            });

        // header_modification: { add_scope (ly_make_module) } lilypond_header { $$ = $2; }
        table.Add(
            "header_modification: $@3 lilypond_header",
            (context, values, locations, location) => values[1]);

        // ------ assignments (parser.yy 738-770) ------

        // assignment_id: STRING { $$ = scm_string_to_symbol ($1); }
        table.Add(
            "assignment_id: STRING",
            (context, values, locations, location)
                => Symbol.Intern(ParserActionHelpers.SchemeStringText(values[0])));

        // assignment_id: SYMBOL { $$ = scm_string_to_symbol ($1); }
        table.Add(
            "assignment_id: SYMBOL",
            (context, values, locations, location)
                => Symbol.Intern(ParserActionHelpers.SchemeStringText(values[0])));

        // assignment: assignment_id '=' identifier_init {
        //     parser->lexer_->set_identifier ($1, $3); $$ = SCM_UNSPECIFIED; }
        table.Add(
            "assignment: assignment_id '=' identifier_init",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).SetIdentifier(values[0], values[2]);
                return Unspecified.Instance;
            });

        // assignment: assignment_id '.' property_path '=' identifier_init {
        //     parser->lexer_->set_identifier (scm_cons ($1, $3), $5); }
        table.Add(
            "assignment: assignment_id '.' property_path '=' identifier_init",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context)
                    .SetIdentifier(new Pair(values[0], values[2]), values[4]);
                return Unspecified.Instance;
            });

        // assignment: markup_mode_word '=' identifier_init — `\markup word = ...`
        // defines a markup command, when the right-hand side really is a markup
        // function.
        table.Add(
            "assignment: markup_mode_word '=' identifier_init",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (!host.IsMarkupFunction(values[2]))
                {
                    ParserActionHelpers.ParserError(context, locations[2], "Not a markup function");
                }
                else
                {
                    host.DefineMarkupCommand(
                        Symbol.Intern(ParserActionHelpers.SchemeStringText(values[0])),
                        values[2]);
                }

                return Unspecified.Instance;
            });

        // ------ identifier_init (parser.yy 773-819) ------

        // identifier_init: symbol_list_part_bare '.' property_path {
        //     $$ = scm_reverse_x ($1, $3); }
        table.Add(
            "identifier_init: symbol_list_part_bare '.' property_path",
            (context, values, locations, location)
                => ParserActionHelpers.ReverseInPlace(values[0], values[2]));

        // identifier_init: symbol_list_part_bare ',' property_path {
        //     $$ = scm_reverse_x ($1, $3); }
        table.Add(
            "identifier_init: symbol_list_part_bare ',' property_path",
            (context, values, locations, location)
                => ParserActionHelpers.ReverseInPlace(values[0], values[2]));

        // identifier_init: post_event_nofinger post_events — a lone post event is
        // assigned as itself; several are wrapped in a PostEvents music so one
        // identifier can carry them all.
        table.Add(
            "identifier_init: post_event_nofinger post_events",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object result = ParserActionHelpers.PostEventCons(
                    values[0],
                    ParserActionHelpers.ReverseInPlace(values[1], Nil.Instance));

                if (result is Pair pair && pair.Cdr is Nil)
                {
                    return pair.Car;
                }

                object music = host.MakeMusic("PostEvents", location);
                host.SetMusicProperty(music, "elements", result);
                return music;
            });

        // identifier_init_nonumber: partial_function ETC {
        //     $$ = MAKE_SYNTAX (partial_music_function, @$,
        //                       scm_reverse_x ($1, SCM_EOL)); }
        table.Add(
            "identifier_init_nonumber: partial_function ETC",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).MakeSyntax(
                    "partial-music-function",
                    location,
                    ParserActionHelpers.ReverseInPlace(values[0], Nil.Instance)));
    }
}
