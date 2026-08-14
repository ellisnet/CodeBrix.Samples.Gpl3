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

using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 4054-4355);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <content>
/// The STRUCTURE of markup: the <c>\markup</c> and
/// <c>\markuplist</c> heads that put the lexer into markup mode, the braced lists,
/// the composed lists, and the four ways a markup can end (a word, a command, an
/// identifier, a whole <c>\score</c>).
/// <para>
/// The thing to understand before reading any of it: A MARKUP IS ITS OWN EXPRESSION.
/// Nothing here interprets markup — <c>\markup \line { "a" "b" }</c> reduces to the
/// LIST <c>(line-markup ("a" "b"))</c>, whose head is the procedure
/// <see cref="IParserHost.LilyImport"/> fetched out of <c>(lily)</c> and whose tail is
/// the arguments. The markup is applied much later, when it is typeset. That is why so
/// many of these bodies are <c>scm_cons</c> and <c>ly_list</c> and nothing else, and
/// why a string needs no conversion at all to BE a markup
/// (<see cref="ParserActionHelpers.MakeSimpleMarkup"/>, the PostEvents group's identity).
/// </para>
/// <para>
/// The second thing is that markup LISTS and single markups are different types that
/// the grammar keeps apart by rule rather than by test: <c>markup_list</c> reduces to
/// a list OF markups, <c>markup</c> to one markup. The <c>markup_braced_list_body</c>
/// rules are where the two meet — a markup conses on, a markup LIST splices in — and
/// getting that pair the wrong way round would produce a nesting level that only
/// showed up as wrong output.
/// </para>
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterMarkupStructure(RuleActionTable table)
    {
        // ------ full_markup_list (parser.yy 4054-4061) ------

        // The mid-rule action of `full_markup_list: MARKUPLIST { ... } markup_list`:
        // { parser->lexer_->push_markup_state (); } — \markuplist switches the lexer
        // into markup mode BEFORE its body is read, which is the whole reason the
        // push is a mid-rule action rather than part of the final body.
        table.Add(
            "$@11: /* empty */",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushMarkupState();
                return Unspecified.Instance;
            });

        // full_markup_list: MARKUPLIST $@11 markup_list {
        //     $$ = $3;
        //     parser->lexer_->pop_state (); }
        table.Add(
            "full_markup_list: MARKUPLIST $@11 markup_list",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PopLexerState();
                return values[2];
            });

        // ------ markup_mode (parser.yy 4063-4068) ------

        // markup_mode: MARKUP { parser->lexer_->push_markup_state (); }
        //
        // A FINAL action, not a mid-rule one, and upstream's comment above
        // markup_mode_word says why that matters: making the push its own
        // reduction is what keeps `\markup "string" = ...` from lexing the '='
        // in markup mode. $$ rides Bison's default, $$ = $1.
        table.Add(
            "markup_mode: MARKUP",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushMarkupState();
                return values[0];
            });

        // ------ markup_mode_word (parser.yy 4079-4085) ------

        // markup_mode_word: markup_mode markup_word {
        //     $$ = $2;
        //     parser->lexer_->pop_state (); }
        table.Add(
            "markup_mode_word: markup_mode markup_word",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PopLexerState();
                return values[1];
            });

        // ------ full_markup (parser.yy 4088-4096) ------

        // full_markup: markup_mode markup_top {
        //     $$ = $2;
        //     parser->lexer_->pop_state (); }
        table.Add(
            "full_markup: markup_mode markup_top",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PopLexerState();
                return values[1];
            });

        // full_markup: markup_mode_word { $$ = make_simple_markup ($1); }
        //
        // markup_mode_word has already popped the mode; this is the \markup "word"
        // case, and a string IS a markup — the PostEvents group's MakeSimpleMarkup, reused here as
        // that group's session recorded it would be.
        table.Add(
            "full_markup: markup_mode_word",
            (context, values, locations, location)
                => ParserActionHelpers.MakeSimpleMarkup(values[0]));

        // ------ partial_markup (parser.yy 4099-4105) ------

        // partial_markup: markup_mode markup_partial_function ETC {
        //     $$ = MAKE_SYNTAX (partial_markup, @2, $2);
        //     parser->lexer_->pop_state (); }
        //
        // MAKE_SYNTAX takes @2, not @$: the location that matters is the function's,
        // not the whole \markup ... \etc expression's.
        table.Add(
            "partial_markup: markup_mode markup_partial_function ETC",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object result = host.MakeSyntax("partial-markup", locations[1], values[1]);
                host.PopLexerState();
                return result;
            });

        // ------ markup_top (parser.yy 4107-4119) ------

        // markup_top: markup_list { $$ = ly_list (Lily::line_markup, $1); }
        //
        // A markup LIST at the top of a \markup is laid out as one line — which is
        // what makes `\markup { a b }` put its words side by side.
        table.Add(
            "markup_top: markup_list",
            (context, values, locations, location)
                => Pair.List(
                    ParserActionHelpers.RequireHost(context).LilyImport("line-markup"),
                    values[0]));

        // markup_top: markup_head_1_list simple_markup {
        //     $$ = scm_car (MAKE_SYNTAX (composed_markup_list, @2, $1, ly_list ($2))); }
        //
        // composed-markup-list distributes a chain of one-argument commands over a
        // LIST of markups; here the list is one element long, so its car is the single
        // composed markup. scm_car of a non-pair is a wrong-type error upstream and an
        // InvalidCastException here — the same failure, new spelling.
        table.Add(
            "markup_top: markup_head_1_list simple_markup",
            (context, values, locations, location)
                => ((Pair)ParserActionHelpers.RequireHost(context).MakeSyntax(
                    "composed-markup-list",
                    locations[1],
                    values[0],
                    Pair.List(values[1]))).Car);

        // markup_top: simple_markup_noword { $$ = $1; }
        table.Add(
            "markup_top: simple_markup_noword",
            (context, values, locations, location) => values[0]);

        // ------ markup_scm (parser.yy 4121-4136) ------

        // The mid-rule action of `markup_scm: embedded_scm { ... } BACKUP`, which is
        // the WHOLE of that rule's behaviour:
        //     if (Text_interface::is_markup ($1))
        //         MYBACKUP (MARKUP_IDENTIFIER, $1, @1);
        //     else if (Text_interface::is_markup_list ($1))
        //         MYBACKUP (MARKUPLIST_IDENTIFIER, $1, @1);
        //     else if (scm_is_eq ($1, SCM_UNSPECIFIED))
        //         MYBACKUP (MARKUPLIST_IDENTIFIER, SCM_EOL, @1);
        //     else {
        //         parser->parser_error (@1, _ ("not a markup"));
        //         MYBACKUP (MARKUP_IDENTIFIER, scm_string (SCM_EOL), @1); }
        //
        // An embedded #(...) in markup position is CLASSIFIED here and handed back to
        // the lexer stream as the identifier token it turned out to be, so that the
        // two rules that consume markup_scm — `markup_scm MARKUP_IDENTIFIER` and
        // `markup_scm MARKUPLIST_IDENTIFIER` — can tell a markup from a markup list
        // without the grammar needing a conflict-ridden lookahead. SCM_UNSPECIFIED
        // (an expression evaluated for effect) reads as the EMPTY markup list, and
        // anything else is an error that still yields an empty markup so the parse
        // continues.
        //
        // $1 and @1 are the embedded_scm ALREADY ON THE STACK below this empty
        // reduction — StackValue(0) and StackLocation(0).
        table.Add(
            "$@12: /* empty */",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object value = context.StackValue(0);
                SourceSpan span = context.StackLocation(0);

                if (host.IsMarkup(value))
                {
                    ParserActionHelpers.MyBackup(context, "MARKUP_IDENTIFIER", value, span);
                }
                else if (host.IsMarkupList(value))
                {
                    ParserActionHelpers.MyBackup(context, "MARKUPLIST_IDENTIFIER", value, span);
                }
                else if (value is Unspecified)
                {
                    ParserActionHelpers.MyBackup(
                        context, "MARKUPLIST_IDENTIFIER", Nil.Instance, span);
                }
                else
                {
                    ParserActionHelpers.ParserError(context, span, "not a markup");
                    ParserActionHelpers.MyBackup(context, "MARKUP_IDENTIFIER", string.Empty, span);
                }

                return Unspecified.Instance;
            });

        // ------ markup_list (parser.yy 4138-4143) ------

        // markup_list: markup_composed_list { $$ = $1; }
        // (the `| markup_uncomposed_list` alternative carries no action upstream)
        table.Add(
            "markup_list: markup_composed_list",
            (context, values, locations, location) => values[0]);

        // ------ markup_uncomposed_list (parser.yy 4145-4169) ------

        // markup_uncomposed_list: markup_braced_list { $$ = $1; }
        table.Add(
            "markup_uncomposed_list: markup_braced_list",
            (context, values, locations, location) => values[0]);

        // markup_uncomposed_list: markup_command_list { $$ = ly_list ($1); }
        //
        // One markup-LIST command is a markup list of one element — the command's own
        // expression, wrapped so it stands where a list is expected.
        table.Add(
            "markup_uncomposed_list: markup_command_list",
            (context, values, locations, location) => Pair.List(values[0]));

        // markup_uncomposed_list: markup_scm MARKUPLIST_IDENTIFIER { $$ = $2; }
        //
        // $2 is the value markup_scm's mid-rule backed up as a MARKUPLIST_IDENTIFIER.
        table.Add(
            "markup_uncomposed_list: markup_scm MARKUPLIST_IDENTIFIER",
            (context, values, locations, location) => values[1]);

        // The mid-rule action of the SCORELINES rule below:
        // { parser->lexer_->push_note_state (); } — the \score's body is MUSIC, so
        // the lexer leaves markup mode for the duration of it.
        table.Add(
            "$@13: /* empty */",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushNoteState();
                return Unspecified.Instance;
            });

        // markup_uncomposed_list: SCORELINES $@13 '{' score_body '}' {
        //     Score *sc = unsmob<Score> ($4);
        //     sc->origin ()->set_spot (@$);
        //     if (sc->defs_.empty ()) {
        //         Output_def *od = get_layout (parser);
        //         sc->add_output_def (od);
        //         od->unprotect (); }
        //     $$ = ly_list (ly_list (Lily::score_lines_markup_list, $4));
        //     parser->lexer_->pop_state (); }
        //
        // \score-lines produces a markup LIST — one line per system — so the
        // expression is wrapped twice: the inner list is the command applied to the
        // score, the outer makes it a one-element markup list.
        table.Add(
            "markup_uncomposed_list: SCORELINES $@13 '{' score_body '}'",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                ParserActionHelpers.PrepareMarkupScore(host, values[3], location);
                object result = Pair.List(
                    Pair.List(host.LilyImport("score-lines-markup-list"), values[3]));
                host.PopLexerState();
                return result;
            });

        // ------ markup_composed_list (parser.yy 4171-4176) ------

        // markup_composed_list: markup_head_1_list markup_uncomposed_list {
        //     $$ = MAKE_SYNTAX (composed_markup_list, @2, $1, $2); }
        //
        // The chain of one-argument commands is distributed over EVERY markup in the
        // list — `\markuplist \bold { a b }` bolds both — which is exactly what
        // composed-markup-list does in the vendored ly-syntax-constructors.scm.
        table.Add(
            "markup_composed_list: markup_head_1_list markup_uncomposed_list",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).MakeSyntax(
                    "composed-markup-list", locations[1], values[0], values[1]));

        // ------ markup_braced_list (parser.yy 4178-4182) ------

        // markup_braced_list: '{' markup_braced_list_body '}' {
        //     $$ = scm_reverse_x ($2, SCM_EOL); }
        table.Add(
            "markup_braced_list: '{' markup_braced_list_body '}'",
            (context, values, locations, location)
                => ParserActionHelpers.ReverseInPlace(values[1], Nil.Instance));

        // ------ markup_braced_list_body (parser.yy 4184-4192) ------

        // markup_braced_list_body: /* empty */ { $$ = SCM_EOL; }
        table.Add(
            "markup_braced_list_body: /* empty */",
            (context, values, locations, location) => Nil.Instance);

        // markup_braced_list_body: markup_braced_list_body markup {
        //     $$ = scm_cons ($2, $1); }
        //
        // ONE markup conses on. The body accumulates in reverse, which is what
        // markup_braced_list's scm_reverse_x undoes.
        table.Add(
            "markup_braced_list_body: markup_braced_list_body markup",
            (context, values, locations, location) => new Pair(values[1], values[0]));

        // markup_braced_list_body: markup_braced_list_body markup_list {
        //     $$ = Srfi_1::append_reverse ($2, $1); }
        //
        // A markup LIST SPLICES: its elements join the accumulator individually, still
        // in reverse. Consing it whole instead would nest a list where the reader
        // wrote a sequence — the same text, a different markup, and nothing would say
        // so. This is the one place the markup/markup-list distinction is load-bearing
        // in a single rule pair.
        table.Add(
            "markup_braced_list_body: markup_braced_list_body markup_list",
            (context, values, locations, location)
                => ParserActionHelpers.AppendReverse(values[1], values[0]));

        // ------ simple_markup (parser.yy 4318-4324) ------

        // simple_markup: markup_word { $$ = make_simple_markup ($1); }
        // (the `| simple_markup_noword` alternative carries no action upstream)
        table.Add(
            "simple_markup: markup_word",
            (context, values, locations, location)
                => ParserActionHelpers.MakeSimpleMarkup(values[0]));

        // ------ simple_markup_noword (parser.yy 4326-4347) ------

        // The mid-rule action of the SCORE rule below — the same note-mode push as
        // $@13, for the same reason.
        table.Add(
            "$@15: /* empty */",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushNoteState();
                return Unspecified.Instance;
            });

        // simple_markup_noword: SCORE $@15 '{' score_body '}' {
        //     Score *sc = unsmob<Score> ($4);
        //     sc->origin ()->set_spot (@$);
        //     if (sc->defs_.empty ()) {
        //         Output_def *od = get_layout (parser);
        //         sc->add_output_def (od);
        //         od->unprotect (); }
        //     $$ = ly_list (Lily::score_markup, $4);
        //     parser->lexer_->pop_state (); }
        //
        // The single-markup twin of the SCORELINES rule above: one wrap, not two,
        // because \score is a markup rather than a markup list.
        table.Add(
            "simple_markup_noword: SCORE $@15 '{' score_body '}'",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                ParserActionHelpers.PrepareMarkupScore(host, values[3], location);
                object result = Pair.List(host.LilyImport("score-markup"), values[3]);
                host.PopLexerState();
                return result;
            });

        // simple_markup_noword: MARKUP_FUNCTION markup_command_basic_arguments {
        //     $$ = scm_cons ($1, scm_reverse_x ($2, SCM_EOL)); }
        //
        // THE MARKUP EXPRESSION ITSELF: the command procedure consed onto its
        // arguments, which the MarkupCommands group's argument rules accumulated in reverse.
        table.Add(
            "simple_markup_noword: MARKUP_FUNCTION markup_command_basic_arguments",
            (context, values, locations, location)
                => new Pair(values[0], ParserActionHelpers.ReverseInPlace(values[1], Nil.Instance)));

        // simple_markup_noword: markup_scm MARKUP_IDENTIFIER { $$ = $2; }
        table.Add(
            "simple_markup_noword: markup_scm MARKUP_IDENTIFIER",
            (context, values, locations, location) => values[1]);

        // ------ markup (parser.yy 4349-4358) ------

        // markup: markup_head_1_list simple_markup {
        //     $$ = scm_car (MAKE_SYNTAX (composed_markup_list, @2, $1, ly_list ($2))); }
        //
        // The twin of markup_top's second alternative, body for body. They are
        // separate productions because one may stand at the top of a \markup and the
        // other inside a braced list, not because they differ.
        table.Add(
            "markup: markup_head_1_list simple_markup",
            (context, values, locations, location)
                => ((Pair)ParserActionHelpers.RequireHost(context).MakeSyntax(
                    "composed-markup-list",
                    locations[1],
                    values[0],
                    Pair.List(values[1]))).Car);

        // markup: simple_markup { $$ = $1; }
        table.Add(
            "markup: simple_markup",
            (context, values, locations, location) => values[0]);
    }
}
