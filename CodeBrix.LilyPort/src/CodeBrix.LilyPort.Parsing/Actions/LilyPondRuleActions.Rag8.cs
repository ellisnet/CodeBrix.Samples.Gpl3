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

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 1898-2112);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <content>
/// RULE ACTION GROUP 8 — music-function arglists, the NON-BACKUP half:
/// <c>function_arglist_nonbackup</c>, <c>function_arglist_nonbackup_reparse</c> and
/// <c>reparsed_rhythm</c>. These rules run while the argument being read can still be
/// accepted or rejected in place — no optional argument remains to fall back to — so
/// they never push a <c>BACKUP</c>; what they DO do, eleven times, is reinterpret the
/// token just read through <c>MYREPARSE</c>, which re-lexes it as the token the
/// argument predicate accepted.
/// <para>
/// In every rule here the <c>EXPECT_SCM</c> value is the argument's PREDICATE and the
/// <c>EXPECT_OPTIONAL</c> value is its default — the tokens the lexer's signature
/// announcement (<c>scan_scm_id</c>) pushed when the function's name was read.
/// </para>
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterRag8(RuleActionTable table)
    {
        // ------ function_arglist_nonbackup (parser.yy 1898-1973) ------

        // function_arglist_nonbackup: EXPECT_OPTIONAL EXPECT_SCM
        //     function_arglist_nonbackup post_event_nofinger {
        //     $$ = check_scheme_arg (parser, @4, $4, $3, $2); }
        table.Add(
            "function_arglist_nonbackup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup post_event_nofinger",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[3], values[3], values[2], values[1]));

        // function_arglist_nonbackup: ... function_arglist_nonbackup '-' UNSIGNED {
        //     SCM n = scm_difference ($5, SCM_UNDEFINED);
        //     if (scm_is_true (ly_call ($2, n)))
        //         $$ = scm_cons (n, $3);
        //     else {
        //         Music *t = MY_MAKE_MUSIC ("FingeringEvent", @5);
        //         set_property (t, "digit", $5);
        //         $$ = check_scheme_arg (parser, @4, t->unprotect (), $3, $2, n); } }
        //
        // A negative number the predicate refuses gets one more chance as a
        // fingering: `\foo -3` may be \foo applied to the post-event -3.
        table.Add(
            "function_arglist_nonbackup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup '-' UNSIGNED",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object n = SchemeNumber.Negate(values[4]);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], n)))
                {
                    return new Pair(n, values[2]);
                }

                object t = host.MakeMusic("FingeringEvent", locations[4]);
                host.SetMusicProperty(t, "digit", values[4]);
                return ParserActionHelpers.CheckSchemeArg(
                    context, locations[3], t, values[2], values[1], n);
            });

        // function_arglist_nonbackup: ... function_arglist_nonbackup '-' REAL {
        //     $$ = check_scheme_arg (parser, @4,
        //                            scm_difference ($5, SCM_UNDEFINED), $3, $2); }
        table.Add(
            "function_arglist_nonbackup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup '-' REAL",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context,
                    locations[3],
                    SchemeNumber.Negate(values[4]),
                    values[2],
                    values[1]));

        // function_arglist_nonbackup: ... function_arglist_nonbackup '-' NUMBER_IDENTIFIER {
        //     $$ = check_scheme_arg (parser, @4,
        //                            scm_difference ($5, SCM_UNDEFINED), $3, $2); }
        table.Add(
            "function_arglist_nonbackup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup '-' NUMBER_IDENTIFIER",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context,
                    locations[3],
                    SchemeNumber.Negate(values[4]),
                    values[2],
                    values[1]));

        // function_arglist_nonbackup: ... function_arglist_nonbackup embedded_scm_arg {
        //     if (scm_is_true (ly_call ($2, $4)))
        //         $$ = scm_cons ($4, $3);
        //     else
        //         $$ = check_scheme_arg (parser, @4, $4, $3, $2); }
        //
        // The else branch re-tests a predicate known false so check_scheme_arg
        // reports the error — upstream's own shape.
        table.Add(
            "function_arglist_nonbackup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup embedded_scm_arg",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], values[3])))
                {
                    return new Pair(values[3], values[2]);
                }

                return ParserActionHelpers.CheckSchemeArg(
                    context, locations[3], values[3], values[2], values[1]);
            });

        // function_arglist_nonbackup: ... function_arglist_nonbackup bare_number_common {
        //     $$ = check_scheme_arg (parser, @4, $4, $3, $2); }
        table.Add(
            "function_arglist_nonbackup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup bare_number_common",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[3], values[3], values[2], values[1]));

        // ------ the REPARSE tail: what a MYREPARSE'd token reduces through ------
        //
        // $1 is the arglist the reparse rule answered, $2 the predicate the REPARSE
        // token carried, $3 the re-lexed argument.

        // function_arglist_nonbackup: function_arglist_nonbackup_reparse REPARSE pitch_or_music {
        //     if (scm_is_true (ly_call ($2, $3)))
        //         $$ = scm_cons ($3, $1);
        //     else
        //         $$ = check_scheme_arg (parser, @3,
        //                                make_music_from_simple (parser, @3, $3),
        //                                $1, $2, $3); }
        table.Add(
            "function_arglist_nonbackup: function_arglist_nonbackup_reparse REPARSE pitch_or_music",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], values[2])))
                {
                    return new Pair(values[2], values[0]);
                }

                return ParserActionHelpers.CheckSchemeArg(
                    context,
                    locations[2],
                    ParserActionHelpers.MakeMusicFromSimple(host, locations[2], values[2]),
                    values[0],
                    values[1],
                    values[2]);
            });

        // function_arglist_nonbackup: function_arglist_nonbackup_reparse REPARSE duration {
        //     $$ = check_scheme_arg (parser, @3, $3, $1, $2); }
        table.Add(
            "function_arglist_nonbackup: function_arglist_nonbackup_reparse REPARSE duration",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[0], values[1]));

        // function_arglist_nonbackup: function_arglist_nonbackup_reparse REPARSE reparsed_rhythm {
        //     $$ = check_scheme_arg (parser, @3, $3, $1, $2); }
        table.Add(
            "function_arglist_nonbackup: function_arglist_nonbackup_reparse REPARSE reparsed_rhythm",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[0], values[1]));

        // function_arglist_nonbackup: function_arglist_nonbackup_reparse REPARSE bare_number_common {
        //     $$ = check_scheme_arg (parser, @3, $3, $1, $2); }
        table.Add(
            "function_arglist_nonbackup: function_arglist_nonbackup_reparse REPARSE bare_number_common",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[0], values[1]));

        // function_arglist_nonbackup: function_arglist_nonbackup_reparse REPARSE SCM_ARG {
        //     $$ = check_scheme_arg (parser, @3, $3, $1, $2); }
        table.Add(
            "function_arglist_nonbackup: function_arglist_nonbackup_reparse REPARSE SCM_ARG",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[0], values[1]));

        // function_arglist_nonbackup: function_arglist_nonbackup_reparse REPARSE lyric_element_music {
        //     $$ = check_scheme_arg (parser, @3, $3, $1, $2); }
        table.Add(
            "function_arglist_nonbackup: function_arglist_nonbackup_reparse REPARSE lyric_element_music",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[0], values[1]));

        // function_arglist_nonbackup: function_arglist_nonbackup_reparse REPARSE symbol_list_arg {
        //     $$ = check_scheme_arg (parser, @3, $3, $1, $2); }
        table.Add(
            "function_arglist_nonbackup: function_arglist_nonbackup_reparse REPARSE symbol_list_arg",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[0], values[1]));

        // ------ reparsed_rhythm (parser.yy 1976-1988) ------

        // reparsed_rhythm: DURATION_ARG dots multipliers post_events {
        //     SCM d = make_duration ($1, from_scm<int> ($2), $3);
        //     parser->default_duration_ = *unsmob<Duration> (d);
        //     $$ = make_music_from_simple (parser, @$, d);
        //     Music *m = unsmob<Music> ($$);
        //     assert (m);
        //     if (scm_is_pair ($4))
        //         set_property (m, "articulations", scm_reverse_x ($4, SCM_EOL));
        // } %prec ':'
        //
        // The DURATION_ARG only arrives via MYREPARSE, so make_duration cannot
        // answer SCM_UNDEFINED here. Upstream's assert holds because a
        // DURATION_ARG reparse is only chosen when the duration-as-music
        // interpretation satisfied the predicate; the port's cast inside
        // SetMusicProperty plays the assert when articulations exist, as upstream's
        // set_property would crash on the null.
        table.Add(
            "reparsed_rhythm: DURATION_ARG dots multipliers post_events %prec ':'",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object d = ParserActionHelpers.MakeDuration(
                    values[0], SchemeConvert.ToInt(values[1], "reparsed_rhythm"), values[2]);
                host.DefaultDuration = (Duration)d;
                object music = ParserActionHelpers.MakeMusicFromSimple(host, location, d);
                if (values[3] is Pair)
                {
                    host.SetMusicProperty(
                        music,
                        "articulations",
                        ParserActionHelpers.ReverseInPlace(values[3], Nil.Instance));
                }

                return music;
            });

        // ------ function_arglist_nonbackup_reparse (parser.yy 1990-2112) ------
        //
        // Every body here assigns $$ = $3 (the arglist stays as it was) and then
        // decides, by predicate, WHICH TOKEN the argument just read should be
        // re-lexed as — MYREPARSE pushes it in front of the input for the REPARSE
        // tail above to consume.

        // function_arglist_nonbackup_reparse: EXPECT_OPTIONAL EXPECT_SCM
        //     function_arglist_nonbackup SCM_IDENTIFIER {
        //     $$ = $3;
        //     SCM res = try_string_variants ($2, $4);
        //     if (!SCM_UNBNDP (res))
        //         if (scm_is_pair (res))
        //             MYREPARSE (@4, $2, SYMBOL_LIST, res);
        //         else
        //             MYREPARSE (@4, $2, SCM_ARG, res);
        //     else if (scm_is_true (ly_call ($2,
        //                  make_music_from_simple (parser, @4, $4))))
        //         MYREPARSE (@4, $2, STRING, $4);
        //     else
        //         MYREPARSE (@4, $2, SCM_ARG, $4); }
        table.Add(
            "function_arglist_nonbackup_reparse: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup SCM_IDENTIFIER",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object res = ParserActionHelpers.TryStringVariants(
                    host, ParserActionHelpers.HostPredicate(host, values[1]), values[3]);
                if (!(res is DefaultArgument))
                {
                    ParserActionHelpers.MyReparse(
                        context,
                        locations[3],
                        values[1],
                        res is Pair ? "SYMBOL_LIST" : "SCM_ARG",
                        res);
                }
                else if (ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[1],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[3], values[3]))))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "STRING", values[3]);
                }
                else
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "SCM_ARG", values[3]);
                }

                return values[2];
            });

        // function_arglist_nonbackup_reparse: ... function_arglist_nonbackup pitch {
        //     $$ = $3;
        //     if (scm_is_true (ly_call ($2,
        //             make_music_from_simple (parser, @4, $4))))
        //         MYREPARSE (@4, $2, PITCH_IDENTIFIER, $4);
        //     else
        //         MYREPARSE (@4, $2, SCM_ARG, $4); }
        table.Add(
            "function_arglist_nonbackup_reparse: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup pitch",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                bool asMusic = ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[1],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[3], values[3])));
                ParserActionHelpers.MyReparse(
                    context,
                    locations[3],
                    values[1],
                    asMusic ? "PITCH_IDENTIFIER" : "SCM_ARG",
                    values[3]);
                return values[2];
            });

        // function_arglist_nonbackup_reparse: ... function_arglist_nonbackup steno_tonic_pitch {
        //     same shape as pitch, reparsing as TONICNAME_PITCH }
        table.Add(
            "function_arglist_nonbackup_reparse: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup steno_tonic_pitch",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                bool asMusic = ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[1],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[3], values[3])));
                ParserActionHelpers.MyReparse(
                    context,
                    locations[3],
                    values[1],
                    asMusic ? "TONICNAME_PITCH" : "SCM_ARG",
                    values[3]);
                return values[2];
            });

        // function_arglist_nonbackup_reparse: ... function_arglist_nonbackup STRING {
        //     the SCM_IDENTIFIER body over the written string }
        table.Add(
            "function_arglist_nonbackup_reparse: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup STRING",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object res = ParserActionHelpers.TryStringVariants(
                    host, ParserActionHelpers.HostPredicate(host, values[1]), values[3]);
                if (!(res is DefaultArgument))
                {
                    ParserActionHelpers.MyReparse(
                        context,
                        locations[3],
                        values[1],
                        res is Pair ? "SYMBOL_LIST" : "SCM_ARG",
                        res);
                }
                else if (ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[1],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[3], values[3]))))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "STRING", values[3]);
                }
                else
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "SCM_ARG", values[3]);
                }

                return values[2];
            });

        // function_arglist_nonbackup_reparse: ... function_arglist_nonbackup SYMBOL {
        //     the STRING body over try_word_variants }
        table.Add(
            "function_arglist_nonbackup_reparse: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup SYMBOL",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object res = ParserActionHelpers.TryWordVariants(
                    ParserActionHelpers.HostPredicate(host, values[1]), values[3]);
                if (!(res is DefaultArgument))
                {
                    ParserActionHelpers.MyReparse(
                        context,
                        locations[3],
                        values[1],
                        res is Pair ? "SYMBOL_LIST" : "SCM_ARG",
                        res);
                }
                else if (ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[1],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[3], values[3]))))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "STRING", values[3]);
                }
                else
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "SCM_ARG", values[3]);
                }

                return values[2];
            });

        // function_arglist_nonbackup_reparse: ... function_arglist_nonbackup full_markup {
        //     $$ = $3;
        //     if (scm_is_true (ly_call ($2, $4)))
        //         MYREPARSE (@4, $2, SCM_ARG, $4);
        //     else if (scm_is_true (ly_call ($2,
        //                  make_music_from_simple (parser, @4, $4))))
        //         MYREPARSE (@4, $2, STRING, $4);
        //     else
        //         MYREPARSE (@4, $2, SCM_ARG, $4); }
        table.Add(
            "function_arglist_nonbackup_reparse: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup full_markup",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], values[3])))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "SCM_ARG", values[3]);
                }
                else if (ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[1],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[3], values[3]))))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "STRING", values[3]);
                }
                else
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "SCM_ARG", values[3]);
                }

                return values[2];
            });

        // function_arglist_nonbackup_reparse: ... function_arglist_nonbackup UNSIGNED {
        //     $$ = $3;
        //     if (scm_is_true (ly_call ($2, $4)))
        //         // May be 3 \cm or similar
        //         MYREPARSE (@4, $2, REAL, $4);
        //     else if (scm_is_true (ly_call ($2, ly_list ($4))))
        //         MYREPARSE (@4, $2, SYMBOL_LIST, ly_list ($4));
        //     else {
        //         SCM d = make_duration ($4);
        //         if (!SCM_UNBNDP (d)) {
        //             if (scm_is_true (ly_call ($2, d)))
        //                 MYREPARSE (@4, $2, DURATION_IDENTIFIER, d);
        //             else if (scm_is_true (ly_call ($2,
        //                          make_music_from_simple (parser, @4, d))))
        //                 MYREPARSE (@4, $2, DURATION_ARG, d);
        //             else
        //                 MYREPARSE (@4, $2, SCM_ARG, $4); // trigger error
        //         } else
        //             MYREPARSE (@4, $2, SCM_ARG, $4); // trigger error
        //     } }
        //
        // The tested list and the pushed list are separate constructions, exactly as
        // upstream calls ly_list ($4) twice.
        table.Add(
            "function_arglist_nonbackup_reparse: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup UNSIGNED",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object pred = values[1];
                object arg = values[3];
                SourceSpan at = locations[3];

                if (ParserActionHelpers.IsSchemeTrue(host.Call(pred, arg)))
                {
                    // May be 3 \cm or similar
                    ParserActionHelpers.MyReparse(context, at, pred, "REAL", arg);
                }
                else if (ParserActionHelpers.IsSchemeTrue(
                    host.Call(pred, new Pair(arg, Nil.Instance))))
                {
                    ParserActionHelpers.MyReparse(
                        context, at, pred, "SYMBOL_LIST", new Pair(arg, Nil.Instance));
                }
                else
                {
                    object d = ParserActionHelpers.MakeDuration(
                        arg, 0, DefaultArgument.Instance);
                    if (!(d is DefaultArgument))
                    {
                        if (ParserActionHelpers.IsSchemeTrue(host.Call(pred, d)))
                        {
                            ParserActionHelpers.MyReparse(
                                context, at, pred, "DURATION_IDENTIFIER", d);
                        }
                        else if (ParserActionHelpers.IsSchemeTrue(
                            host.Call(
                                pred,
                                ParserActionHelpers.MakeMusicFromSimple(host, at, d))))
                        {
                            ParserActionHelpers.MyReparse(
                                context, at, pred, "DURATION_ARG", d);
                        }
                        else
                        {
                            // trigger error
                            ParserActionHelpers.MyReparse(context, at, pred, "SCM_ARG", arg);
                        }
                    }
                    else
                    {
                        // trigger error
                        ParserActionHelpers.MyReparse(context, at, pred, "SCM_ARG", arg);
                    }
                }

                return values[2];
            });

        // function_arglist_nonbackup_reparse: ... function_arglist_nonbackup DURATION_IDENTIFIER {
        //     $$ = $3;
        //     if (scm_is_true (ly_call ($2, $4)))
        //         MYREPARSE (@4, $2, DURATION_IDENTIFIER, $4);
        //     else if (scm_is_true (ly_call ($2,
        //                  make_music_from_simple (parser, @4, $4))))
        //         MYREPARSE (@4, $2, DURATION_ARG, $4);
        //     else
        //         MYREPARSE (@4, $2, SCM_ARG, $4); // trigger error }
        table.Add(
            "function_arglist_nonbackup_reparse: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup DURATION_IDENTIFIER",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], values[3])))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "DURATION_IDENTIFIER", values[3]);
                }
                else if (ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[1],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[3], values[3]))))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "DURATION_ARG", values[3]);
                }
                else
                {
                    // trigger error
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "SCM_ARG", values[3]);
                }

                return values[2];
            });
    }
}
