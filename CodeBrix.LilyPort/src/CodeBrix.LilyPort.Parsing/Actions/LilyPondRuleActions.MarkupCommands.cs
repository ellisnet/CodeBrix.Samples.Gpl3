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
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 4194-4316);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <content>
/// Markup COMMANDS and their argument lists, and the last of
/// the 479 action bodies.
/// <para>
/// How a markup command is parsed at all: the LEXER, not the grammar, knows a
/// command's signature. On reading <c>\bold</c> it looks the command up
/// (<c>ILexerHost.LookupMarkupCommand</c>) and pushes one <c>EXPECT_*</c> token per
/// declared argument, plus a terminating <c>EXPECT_NO_MORE_ARGS</c> — in reverse, so
/// they are delivered in signature order
/// (<c>ModalScanner.PushMarkupPredicates</c>, already ported). The rules here then
/// read those tokens as the announcement they are: <c>EXPECT_MARKUP</c> says "a markup
/// follows", <c>EXPECT_SCM</c> carries the argument's PREDICATE as its semantic value,
/// and <c>EXPECT_NO_MORE_ARGS</c> ends the list. So the grammar can parse a command it
/// has never heard of, and the argument count is right by construction.
/// </para>
/// <para>
/// Every list here accumulates IN REVERSE — each rule conses its argument onto what
/// the recursion below it built — and the reversal happens once, in whichever rule
/// finally conses the command procedure onto the front
/// (<c>markup_command_list</c> and <c>markup_head_1_item</c> here,
/// <c>simple_markup_noword: MARKUP_FUNCTION ...</c> in the MarkupStructure group).
/// </para>
/// <para>
/// <c>markup_arglist_partial</c> is the <c>\etc</c> half: the rules that match the
/// FIRST MISSING argument, with the ones above discarding every remaining
/// expectation. It is the PartialFunctions group's partial-function mechanism, in markup.
/// </para>
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterMarkupCommands(RuleActionTable table)
    {
        // ------ markup_command_list (parser.yy 4194-4198) ------

        // markup_command_list: MARKUP_LIST_FUNCTION markup_command_list_arguments {
        //     $$ = scm_cons ($1, scm_reverse_x ($2, SCM_EOL)); }
        //
        // The markup-LIST command's expression: the procedure consed onto its
        // arguments, put back into written order. The single-markup twin is the MarkupStructure group's
        // `simple_markup_noword: MARKUP_FUNCTION markup_command_basic_arguments`.
        table.Add(
            "markup_command_list: MARKUP_LIST_FUNCTION markup_command_list_arguments",
            (context, values, locations, location)
                => new Pair(values[0], ParserActionHelpers.ReverseInPlace(values[1], Nil.Instance)));

        // ------ markup_command_basic_arguments (parser.yy 4200-4247) ------

        // markup_command_basic_arguments:
        //     EXPECT_MARKUP_LIST markup_command_list_arguments markup_list {
        //         $$ = scm_cons ($3, $2); }
        //
        // A declared markup-list argument needs no checking: the grammar already
        // guaranteed its type by reducing markup_list.
        table.Add(
            "markup_command_basic_arguments: EXPECT_MARKUP_LIST markup_command_list_arguments markup_list",
            (context, values, locations, location) => new Pair(values[2], values[1]));

        // markup_command_basic_arguments:
        //     EXPECT_SCM markup_command_list_arguments embedded_scm {
        //         $$ = check_scheme_arg (parser, @3, $3, $2, $1); }
        //
        // $1 IS THE PREDICATE the lexer put on the EXPECT_SCM token. check_scheme_arg
        // conses the argument on either way and, on a failed predicate, terminates the
        // list with #f so the command is known to be uncallable while keeping its
        // length — the arglist groups' shared helper, unchanged.
        table.Add(
            "markup_command_basic_arguments: EXPECT_SCM markup_command_list_arguments embedded_scm",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[1], values[0]));

        // The mid-rule action of the braced-argument rule below:
        // { parser->lexer_->push_note_state (); } — the braces hold LilyPond, not
        // markup, so the lexer leaves markup mode to read them.
        table.Add(
            "$@14: /* empty */",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushNoteState();
                return Unspecified.Instance;
            });

        // markup_command_basic_arguments:
        //     EXPECT_SCM markup_command_list_arguments '{' $@14 embedded_lilypond '}' {
        //         $$ = SCM_UNDEFINED;
        //         if (scm_is_false (ly_call ($1, $5))) {
        //             // The conversion to a note event needs the lexer to be still
        //             // in note state.
        //             SCM maybe_music = make_music_from_simple (parser, @5, $5);
        //             if (unsmob<Music> (maybe_music)
        //                 && scm_is_true (ly_call ($1, maybe_music))) {
        //                 $$ = scm_cons (maybe_music, $2);
        //                 if (Duration *dur = unsmob<Duration> ($5)) {
        //                     parser->default_duration_ = *dur; } } }
        //         if (SCM_UNBNDP ($$)) {
        //             $$ = check_scheme_arg (parser, @5, $5, $2, $1); }
        //         parser->lexer_->pop_state (); }
        //
        // Upstream's own comment says what this is for: an argument that expects
        // neither markup? nor markup-list? may be written in braces, and `{ 4 }` or
        // `{ cis }` is ambiguous between a duration/pitch and a NOTE EVENT. So the
        // written value is tried first, and only if the predicate refuses it is the
        // note-event reading attempted — "a very limited subset of the flexible
        // interpretation that music functions do".
        //
        // TWO ORDERING POINTS, both load-bearing and neither visible in the result:
        // make_music_from_simple must run while the lexer is STILL in note state
        // (upstream says so in the comment), which is why the pop is last; and the
        // sticky default duration is updated ONLY on the branch that succeeded as
        // music, so a refused `{ 4 }` does not silently change what a later bare note
        // means.
        table.Add(
            "markup_command_basic_arguments: EXPECT_SCM markup_command_list_arguments '{' $@14 embedded_lilypond '}'",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object result = DefaultArgument.Instance;

                if (!ParserActionHelpers.IsSchemeTrue(host.Call(values[0], values[4])))
                {
                    // The conversion to a note event needs the lexer to be still
                    // in note state.
                    object maybeMusic = ParserActionHelpers.MakeMusicFromSimple(
                        host, locations[4], values[4]);
                    if (maybeMusic is MusicObject
                        && ParserActionHelpers.IsSchemeTrue(host.Call(values[0], maybeMusic)))
                    {
                        result = new Pair(maybeMusic, values[1]);
                        if (values[4] is Duration duration)
                        {
                            host.DefaultDuration = duration;
                        }
                    }
                }

                if (result is DefaultArgument)
                {
                    result = ParserActionHelpers.CheckSchemeArg(
                        context, locations[4], values[4], values[1], values[0]);
                }

                host.PopLexerState();
                return result;
            });

        // markup_command_basic_arguments:
        //     EXPECT_SCM markup_command_list_arguments mode_changed_music {
        //         $$ = check_scheme_arg (parser, @3, $3, $2, $1); }
        table.Add(
            "markup_command_basic_arguments: EXPECT_SCM markup_command_list_arguments mode_changed_music",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[1], values[0]));

        // markup_command_basic_arguments:
        //     EXPECT_SCM markup_command_list_arguments MUSIC_IDENTIFIER {
        //         $$ = check_scheme_arg (parser, @3, $3, $2, $1); }
        table.Add(
            "markup_command_basic_arguments: EXPECT_SCM markup_command_list_arguments MUSIC_IDENTIFIER",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[1], values[0]));

        // markup_command_basic_arguments:
        //     EXPECT_SCM markup_command_list_arguments STRING {
        //         $$ = check_scheme_arg (parser, @3, $3, $2, $1); }
        table.Add(
            "markup_command_basic_arguments: EXPECT_SCM markup_command_list_arguments STRING",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[1], values[0]));

        // markup_command_basic_arguments: EXPECT_NO_MORE_ARGS { $$ = SCM_EOL; }
        //
        // The bottom of every argument recursion: the lexer's terminating token
        // starts the (reversed) list off empty.
        table.Add(
            "markup_command_basic_arguments: EXPECT_NO_MORE_ARGS",
            (context, values, locations, location) => Nil.Instance);

        // ------ markup_command_list_arguments (parser.yy 4249-4255) ------

        // markup_command_list_arguments: markup_command_basic_arguments { $$ = $1; }
        table.Add(
            "markup_command_list_arguments: markup_command_basic_arguments",
            (context, values, locations, location) => values[0]);

        // markup_command_list_arguments:
        //     EXPECT_MARKUP markup_command_list_arguments markup {
        //         $$ = scm_cons ($3, $2); }
        //
        // A declared markup argument, like the markup-list one above, is guaranteed by
        // the grammar rather than by a predicate call.
        table.Add(
            "markup_command_list_arguments: EXPECT_MARKUP markup_command_list_arguments markup",
            (context, values, locations, location) => new Pair(values[2], values[1]));

        // ------ markup_partial_function (parser.yy 4257-4267) ------

        // markup_partial_function: MARKUP_FUNCTION markup_arglist_partial {
        //     $$ = ly_list (scm_cons ($1, scm_reverse_x ($2, SCM_EOL))); }
        //
        // A ONE-ELEMENT list holding the incomplete call, because \etc's chain is
        // applied innermost-first — the same shape the PartialFunctions group's partial_function builds for
        // music functions.
        table.Add(
            "markup_partial_function: MARKUP_FUNCTION markup_arglist_partial",
            (context, values, locations, location)
                => Pair.List(
                    new Pair(values[0], ParserActionHelpers.ReverseInPlace(values[1], Nil.Instance))));

        // markup_partial_function: markup_head_1_list MARKUP_FUNCTION markup_arglist_partial {
        //     $$ = scm_cons (scm_cons ($2, scm_reverse_x ($3, SCM_EOL)), $1); }
        //
        // With a command chain in front, the incomplete call goes ON THE FRONT of it —
        // markup_head_1_list is already accumulated in reverse (innermost last), so
        // consing here keeps the incomplete call innermost, which is where the
        // eventual argument arrives.
        table.Add(
            "markup_partial_function: markup_head_1_list MARKUP_FUNCTION markup_arglist_partial",
            (context, values, locations, location)
                => new Pair(
                    new Pair(values[1], ParserActionHelpers.ReverseInPlace(values[2], Nil.Instance)),
                    values[0]));

        // ------ markup_arglist_partial (parser.yy 4269-4296) ------

        // The first three DISCARD a remaining expectation and pass the accumulated
        // list through: everything after the first missing argument is dropped,
        // because the partial application ends there. Upstream's comment: "The rules
        // below match the first missing argument, and the rules above discard all
        // remaining expectations."

        // markup_arglist_partial: EXPECT_MARKUP markup_arglist_partial { $$ = $2; }
        table.Add(
            "markup_arglist_partial: EXPECT_MARKUP markup_arglist_partial",
            (context, values, locations, location) => values[1]);

        // markup_arglist_partial: EXPECT_MARKUP_LIST markup_arglist_partial { $$= $2; }
        table.Add(
            "markup_arglist_partial: EXPECT_MARKUP_LIST markup_arglist_partial",
            (context, values, locations, location) => values[1]);

        // markup_arglist_partial: EXPECT_SCM markup_arglist_partial { $$= $2; }
        table.Add(
            "markup_arglist_partial: EXPECT_SCM markup_arglist_partial",
            (context, values, locations, location) => values[1]);

        // The three below MATCH the first missing argument: the expectation is
        // consumed and the arguments written BEFORE it are what the partial call
        // keeps.

        // markup_arglist_partial: EXPECT_MARKUP markup_command_list_arguments { $$ = $2; }
        table.Add(
            "markup_arglist_partial: EXPECT_MARKUP markup_command_list_arguments",
            (context, values, locations, location) => values[1]);

        // markup_arglist_partial: EXPECT_MARKUP_LIST markup_command_list_arguments { $$ = $2; }
        table.Add(
            "markup_arglist_partial: EXPECT_MARKUP_LIST markup_command_list_arguments",
            (context, values, locations, location) => values[1]);

        // markup_arglist_partial: EXPECT_SCM markup_command_list_arguments { $$ = $2; }
        table.Add(
            "markup_arglist_partial: EXPECT_SCM markup_command_list_arguments",
            (context, values, locations, location) => values[1]);

        // ------ markup_head_1_item (parser.yy 4298-4302) ------

        // markup_head_1_item: MARKUP_FUNCTION EXPECT_MARKUP markup_command_list_arguments {
        //     $$ = scm_cons ($1, scm_reverse_x ($3, SCM_EOL)); }
        //
        // A command whose LAST declared argument is a markup — `\bold`, `\italic` —
        // with that last argument not yet read. Everything before it is the head's own
        // arguments; the markup it will wrap comes from the rule that uses the head.
        // This is what makes `\bold \italic "x"` chain without parentheses.
        table.Add(
            "markup_head_1_item: MARKUP_FUNCTION EXPECT_MARKUP markup_command_list_arguments",
            (context, values, locations, location)
                => new Pair(values[0], ParserActionHelpers.ReverseInPlace(values[2], Nil.Instance)));

        // ------ markup_head_1_list (parser.yy 4304-4311) ------

        // markup_head_1_list: markup_head_1_item { $$ = ly_list ($1); }
        table.Add(
            "markup_head_1_list: markup_head_1_item",
            (context, values, locations, location) => Pair.List(values[0]));

        // markup_head_1_list: markup_head_1_list markup_head_1_item { $$ = scm_cons ($2, $1); }
        //
        // Accumulated in reverse — OUTERMOST LAST — which is the order
        // composed-markup-list applies them in, so `\bold \italic "x"` italicises
        // first and bolds the result.
        table.Add(
            "markup_head_1_list: markup_head_1_list markup_head_1_item",
            (context, values, locations, location) => new Pair(values[1], values[0]));
    }
}
