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
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 569-689);

// Modified by Jeremy Ellis on 2026-08-04 as part of the CodeBrix port.

/// <content>
/// RULE ACTION GROUP 2 — embedded Scheme and embedded LilyPond:
/// <c>embedded_scm_bare</c>, <c>embedded_scm_bare_arg</c>, <c>scm_function_call</c>,
/// <c>embedded_lilypond_number</c> and <c>embedded_lilypond</c>. The
/// <c>embedded_scm</c>, <c>embedded_scm_active</c> and <c>embedded_scm_arg</c>
/// alternatives are pass-throughs upstream leaves actionless, so they need nothing
/// here. This is the two-way bridge: <c>#(...)</c> bringing Scheme into LilyPond,
/// and <c>#{ ... #}</c> bringing LilyPond back into Scheme.
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterRag2(RuleActionTable table)
    {
        // ------ embedded_scm_bare (parser.yy 569-575) ------

        // embedded_scm_bare: SCM_TOKEN {
        //     $$ = parser->lexer_->eval_scm_token ($1, @1); }
        //
        // Unlike toplevel_expression: SCM_TOKEN (RAG1), which evaluates AND IGNORES,
        // here the evaluation's result IS the value.
        table.Add(
            "embedded_scm_bare: SCM_TOKEN",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).EvalSchemeToken(values[0], locations[0]));

        // ------ embedded_scm_bare_arg (parser.yy 583-600) ------

        // embedded_scm_bare_arg: SCM_TOKEN {
        //     $$ = parser->lexer_->eval_scm_token ($1, @1); }
        table.Add(
            "embedded_scm_bare_arg: SCM_TOKEN",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).EvalSchemeToken(values[0], locations[0]));

        // ------ scm_function_call (parser.yy 616-621) ------

        // scm_function_call: SCM_FUNCTION function_arglist {
        //     $$ = MAKE_SYNTAX (music_function, @$, $1, $2); }
        table.Add(
            "scm_function_call: SCM_FUNCTION function_arglist",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context)
                    .MakeSyntax("music-function", location, values[0], values[1]));

        // ------ embedded_lilypond_number (parser.yy 623-633) ------

        // embedded_lilypond_number: '-' embedded_lilypond_number {
        //     $$ = scm_difference ($2, SCM_UNDEFINED); } — the one-argument
        // scm_difference, which is negation.
        table.Add(
            "embedded_lilypond_number: '-' embedded_lilypond_number",
            (context, values, locations, location) => SchemeNumber.Negate(values[1]));

        // embedded_lilypond_number: UNSIGNED NUMBER_IDENTIFIER {
        //     $$ = scm_product ($1, $2); }
        table.Add(
            "embedded_lilypond_number: UNSIGNED NUMBER_IDENTIFIER",
            (context, values, locations, location) => SchemeNumber.Multiply(values[0], values[1]));

        // ------ embedded_lilypond (parser.yy 635-689) ------

        // embedded_lilypond: /* empty */ {
        //     // FIXME: @$ does not contain a useful source location
        //     // for empty rules, and the only token in the whole
        //     // production, EMBEDDED_LILY, is synthetic and also
        //     // contains no source location.
        //     $$ = MAKE_SYNTAX (unspecified_music, @$); }
        table.Add(
            "embedded_lilypond: /* empty */",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context)
                    .MakeSyntax("unspecified-music", location));

        // embedded_lilypond: post_event {
        //     if (!unsmob<Music> ($1))
        //         $$ = MY_MAKE_MUSIC ("PostEvents", @$)->unprotect (); }
        //
        // A post event that turned out not to be music becomes an EMPTY PostEvents
        // music; a real one passes through via the implicit $$ = $1.
        table.Add(
            "embedded_lilypond: post_event",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (!(values[0] is MusicObject))
                {
                    return host.MakeMusic("PostEvents", location);
                }

                return values[0];
            });

        // embedded_lilypond: duration post_events %prec ':' — with post events, a
        // NoteEvent carrying the duration and (reversed-into-order) articulations;
        // the duration also becomes the parser's default duration. With none, the
        // implicit $$ = $1 leaves the bare Duration itself as the embedded value.
        table.Add(
            "embedded_lilypond: duration post_events %prec ':'",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (values[1] is Pair)
                {
                    object n = host.MakeMusic("NoteEvent", location);

                    // parser->default_duration_ = *unsmob<Duration> ($1); — assigned
                    // BY VALUE upstream, and Duration is a value type here too.
                    host.DefaultDuration = (Duration)values[0];
                    host.SetMusicProperty(n, "duration", values[0]);
                    host.SetMusicProperty(
                        n,
                        "articulations",
                        ParserActionHelpers.ReverseInPlace(values[1], Nil.Instance));
                    return n;
                }

                return values[0];
            });

        // embedded_lilypond: music_embedded music_embedded music_list — two or more
        // things: cons the leading pair onto the (reversed) music_list, put the whole
        // in document order attaching post events as reverse_music_list goes, then
        // decide what the sequence amounts to.
        table.Add(
            "embedded_lilypond: music_embedded music_embedded music_list",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);

                object tail = Nil.Instance;
                if (values[0] is MusicObject)
                {
                    tail = new Pair(values[0], tail);
                }

                if (values[1] is MusicObject)
                {
                    tail = new Pair(values[1], tail);
                }

                object result = ParserActionHelpers.ReverseMusicList(
                    host,
                    location,
                    ParserActionHelpers.AppendInPlace(values[2], tail),
                    true,
                    true);
                if (result is Pair pair) // unpackaged list
                {
                    if (pair.Cdr is Nil)
                    {
                        result = pair.Car; // single expression
                    }
                    else
                    {
                        result = host.MakeSyntax("sequential-music", location, result);
                    }
                }
                else if (result is Nil)
                {
                    result = host.MakeSyntax("unspecified-music", location);
                }

                // else already packaged post-event
                return result;
            });

        // embedded_lilypond: error { parser->error_level_ = 1; $$ = SCM_UNSPECIFIED; }
        table.Add(
            "embedded_lilypond: error",
            (context, values, locations, location) =>
            {
                if (context.UserState is IParserErrorLevel state)
                {
                    state.ErrorLevel = 1;
                }

                return Unspecified.Instance;
            });

        // embedded_lilypond: INVALID embedded_lilypond {
        //     parser->error_level_ = 1; $$ = $2; }
        table.Add(
            "embedded_lilypond: INVALID embedded_lilypond",
            (context, values, locations, location) =>
            {
                if (context.UserState is IParserErrorLevel state)
                {
                    state.ErrorLevel = 1;
                }

                return values[1];
            });
    }
}
