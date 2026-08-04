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
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 2357-2662);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <content>
/// RULE ACTION GROUP 10 — music-function arglists, the COMMON half, the skip/partial
/// plumbing and the call itself: <c>function_arglist</c>,
/// <c>function_arglist_skip_nonbackup</c>, <c>function_arglist_partial</c>,
/// <c>function_arglist_partial_optional</c>, <c>function_arglist_common</c>,
/// <c>function_arglist_common_reparse</c>, <c>function_arglist_optional</c>,
/// <c>function_arglist_skip_backup</c> and <c>music_function_call</c>.
/// <para>
/// The skip rules are where an optional argument that was NOT written gets its
/// default consed on (<c>\default</c> does it explicitly through the
/// <c>DEFAULT</c>-terminated alternatives); the partial rules skim arguments off an
/// incomplete list for <c>\etc</c> (upstream's comment: the remaining arglist "has to
/// be in not-skipping-optional-arguments mode"); and
/// <c>function_arglist_common_reparse</c> is where an argument in final position gets
/// reinterpreted by predicate, reparsing the music-flavored fallback as
/// <c>LYRIC_ELEMENT</c> where the nonbackup twin (RAG8) uses <c>STRING</c>.
/// </para>
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterRag10(RuleActionTable table)
    {
        // ------ function_arglist (parser.yy 2357-2363) ------

        // function_arglist: EXPECT_OPTIONAL EXPECT_SCM function_arglist_skip_nonbackup
        //     DEFAULT {
        //     $$ = scm_cons (loc_on_copy (parser, @4, $1), $3); }
        //
        // A written \default: the optional's default value joins the list, stamped
        // at the \default itself (@4).
        table.Add(
            "function_arglist: EXPECT_OPTIONAL EXPECT_SCM function_arglist_skip_nonbackup DEFAULT",
            (context, values, locations, location)
                => new Pair(
                    ParserActionHelpers.RequireHost(context)
                        .LocOnCopy(values[0], locations[3]),
                    values[2]));

        // ------ function_arglist_skip_nonbackup (parser.yy 2365-2371) ------

        // function_arglist_skip_nonbackup: EXPECT_OPTIONAL EXPECT_SCM
        //     function_arglist_skip_nonbackup {
        //     $$ = scm_cons (loc_on_copy (parser, @3, $1), $3); }
        table.Add(
            "function_arglist_skip_nonbackup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_skip_nonbackup",
            (context, values, locations, location)
                => new Pair(
                    ParserActionHelpers.RequireHost(context)
                        .LocOnCopy(values[0], locations[2]),
                    values[2]));

        // ------ function_arglist_partial (parser.yy 2385-2402) ------
        //
        // Partial arglists are returned in their incomplete state; the missing parts
        // of the signature are reconstructed when the \etc partial function is
        // eventually called (RAG11 / RAG1's partial-music-function).

        // function_arglist_partial: EXPECT_SCM function_arglist_optional { $$ = $2; }
        table.Add(
            "function_arglist_partial: EXPECT_SCM function_arglist_optional",
            (context, values, locations, location) => values[1]);

        // function_arglist_partial: EXPECT_SCM function_arglist_partial_optional { $$ = $2; }
        table.Add(
            "function_arglist_partial: EXPECT_SCM function_arglist_partial_optional",
            (context, values, locations, location) => values[1]);

        // function_arglist_partial: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup { $$ = $3; }
        table.Add(
            "function_arglist_partial: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup",
            (context, values, locations, location) => values[2]);

        // function_arglist_partial: EXPECT_OPTIONAL EXPECT_SCM function_arglist_partial { $$ = $3; }
        table.Add(
            "function_arglist_partial: EXPECT_OPTIONAL EXPECT_SCM function_arglist_partial",
            (context, values, locations, location) => values[2]);

        // ------ function_arglist_partial_optional (parser.yy 2404-2421) ------

        // function_arglist_partial_optional: EXPECT_SCM function_arglist_optional { $$ = $2; }
        table.Add(
            "function_arglist_partial_optional: EXPECT_SCM function_arglist_optional",
            (context, values, locations, location) => values[1]);

        // function_arglist_partial_optional: EXPECT_SCM function_arglist_partial_optional { $$ = $2; }
        table.Add(
            "function_arglist_partial_optional: EXPECT_SCM function_arglist_partial_optional",
            (context, values, locations, location) => values[1]);

        // function_arglist_partial_optional: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup { $$ = $3; }
        table.Add(
            "function_arglist_partial_optional: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup",
            (context, values, locations, location) => values[2]);

        // function_arglist_partial_optional: EXPECT_OPTIONAL EXPECT_SCM function_arglist_partial_optional { $$ = $3; }
        table.Add(
            "function_arglist_partial_optional: EXPECT_OPTIONAL EXPECT_SCM function_arglist_partial_optional",
            (context, values, locations, location) => values[2]);

        // ------ function_arglist_common (parser.yy 2423-2488) ------

        // function_arglist_common: EXPECT_NO_MORE_ARGS { $$ = SCM_EOL; }
        //
        // The signature's floor: every arglist bottoms out here.
        table.Add(
            "function_arglist_common: EXPECT_NO_MORE_ARGS",
            (context, values, locations, location) => Nil.Instance);

        // function_arglist_common: EXPECT_SCM function_arglist_optional embedded_scm_arg {
        //     if (scm_is_true (ly_call ($1, $3)))
        //         $$ = scm_cons ($3, $2);
        //     else
        //         $$ = check_scheme_arg (parser, @3, $3, $2, $1); }
        table.Add(
            "function_arglist_common: EXPECT_SCM function_arglist_optional embedded_scm_arg",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[0], values[2])))
                {
                    return new Pair(values[2], values[1]);
                }

                return ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[1], values[0]);
            });

        // function_arglist_common: EXPECT_SCM function_arglist_optional bare_number_common {
        //     $$ = check_scheme_arg (parser, @3, $3, $2, $1); }
        table.Add(
            "function_arglist_common: EXPECT_SCM function_arglist_optional bare_number_common",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[1], values[0]));

        // function_arglist_common: EXPECT_SCM function_arglist_optional post_event_nofinger {
        //     $$ = check_scheme_arg (parser, @3, $3, $2, $1); }
        table.Add(
            "function_arglist_common: EXPECT_SCM function_arglist_optional post_event_nofinger",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[1], values[0]));

        // function_arglist_common: EXPECT_SCM function_arglist_optional '-' NUMBER_IDENTIFIER {
        //     SCM n = scm_difference ($4, SCM_UNDEFINED);
        //     $$ = check_scheme_arg (parser, @4, n, $2, $1); }
        table.Add(
            "function_arglist_common: EXPECT_SCM function_arglist_optional '-' NUMBER_IDENTIFIER",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context,
                    locations[3],
                    SchemeNumber.Negate(values[3]),
                    values[1],
                    values[0]));

        // ------ the REPARSE tail ------

        // function_arglist_common: function_arglist_common_reparse REPARSE SCM_ARG {
        //     $$ = check_scheme_arg (parser, @3, $3, $1, $2); }
        table.Add(
            "function_arglist_common: function_arglist_common_reparse REPARSE SCM_ARG",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[0], values[1]));

        // function_arglist_common: function_arglist_common_reparse REPARSE lyric_element_music {
        //     $$ = check_scheme_arg (parser, @3, $3, $1, $2); }
        table.Add(
            "function_arglist_common: function_arglist_common_reparse REPARSE lyric_element_music",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[0], values[1]));

        // function_arglist_common: function_arglist_common_reparse REPARSE pitch_or_music {
        //     if (scm_is_true (ly_call ($2, $3)))
        //         $$ = scm_cons ($3, $1);
        //     else
        //         $$ = check_scheme_arg (parser, @3,
        //                                make_music_from_simple (parser, @3, $3),
        //                                $1, $2, $3); }
        table.Add(
            "function_arglist_common: function_arglist_common_reparse REPARSE pitch_or_music",
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

        // function_arglist_common: function_arglist_common_reparse REPARSE bare_number_common {
        //     $$ = check_scheme_arg (parser, @3, $3, $1, $2); }
        table.Add(
            "function_arglist_common: function_arglist_common_reparse REPARSE bare_number_common",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[0], values[1]));

        // function_arglist_common: function_arglist_common_reparse REPARSE duration {
        //     $$ = check_scheme_arg (parser, @3, $3, $1, $2); }
        table.Add(
            "function_arglist_common: function_arglist_common_reparse REPARSE duration",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[0], values[1]));

        // function_arglist_common: function_arglist_common_reparse REPARSE reparsed_rhythm {
        //     $$ = check_scheme_arg (parser, @3, $3, $1, $2); }
        table.Add(
            "function_arglist_common: function_arglist_common_reparse REPARSE reparsed_rhythm",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[0], values[1]));

        // function_arglist_common: function_arglist_common_reparse REPARSE symbol_list_arg {
        //     $$ = check_scheme_arg (parser, @3, $3, $1, $2); }
        table.Add(
            "function_arglist_common: function_arglist_common_reparse REPARSE symbol_list_arg",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[0], values[1]));

        // ------ function_arglist_common_reparse (parser.yy 2490-2638) ------
        //
        // RAG8's reparse family in FINAL-ARGUMENT position: EXPECT_SCM is $1, the
        // arglist $2, the argument $3 — and the music-flavored reinterpretation
        // reparses as LYRIC_ELEMENT, not STRING.

        // function_arglist_common_reparse: EXPECT_SCM function_arglist_optional SCM_IDENTIFIER {
        //     $$ = $2;
        //     SCM res = try_string_variants ($1, $3);
        //     if (!SCM_UNBNDP (res))
        //         if (scm_is_pair (res))
        //             MYREPARSE (@3, $1, SYMBOL_LIST, res);
        //         else
        //             MYREPARSE (@3, $1, SCM_ARG, res);
        //     else if (scm_is_true (ly_call ($1,
        //                  make_music_from_simple (parser, @3, $3))))
        //         MYREPARSE (@3, $1, LYRIC_ELEMENT, $3);
        //     else
        //         // This is going to flag a syntax error, we
        //         // know the predicate to be false.
        //         MYREPARSE (@3, $1, SCM_ARG, $3); }
        table.Add(
            "function_arglist_common_reparse: EXPECT_SCM function_arglist_optional SCM_IDENTIFIER",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object res = ParserActionHelpers.TryStringVariants(
                    host, ParserActionHelpers.HostPredicate(host, values[0]), values[2]);
                if (!(res is DefaultArgument))
                {
                    ParserActionHelpers.MyReparse(
                        context,
                        locations[2],
                        values[0],
                        res is Pair ? "SYMBOL_LIST" : "SCM_ARG",
                        res);
                }
                else if (ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[0],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[2], values[2]))))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[2], values[0], "LYRIC_ELEMENT", values[2]);
                }
                else
                {
                    // This is going to flag a syntax error, we know the predicate to
                    // be false.
                    ParserActionHelpers.MyReparse(
                        context, locations[2], values[0], "SCM_ARG", values[2]);
                }

                return values[1];
            });

        // function_arglist_common_reparse: EXPECT_SCM function_arglist_optional pitch {
        //     $$ = $2;
        //     if (scm_is_true (ly_call ($1,
        //             make_music_from_simple (parser, @3, $3))))
        //         MYREPARSE (@3, $1, PITCH_IDENTIFIER, $3);
        //     else
        //         MYREPARSE (@3, $1, SCM_ARG, $3); }
        table.Add(
            "function_arglist_common_reparse: EXPECT_SCM function_arglist_optional pitch",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                bool asMusic = ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[0],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[2], values[2])));
                ParserActionHelpers.MyReparse(
                    context,
                    locations[2],
                    values[0],
                    asMusic ? "PITCH_IDENTIFIER" : "SCM_ARG",
                    values[2]);
                return values[1];
            });

        // function_arglist_common_reparse: EXPECT_SCM function_arglist_optional steno_tonic_pitch {
        //     the pitch body, reparsing as TONICNAME_PITCH }
        table.Add(
            "function_arglist_common_reparse: EXPECT_SCM function_arglist_optional steno_tonic_pitch",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                bool asMusic = ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[0],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[2], values[2])));
                ParserActionHelpers.MyReparse(
                    context,
                    locations[2],
                    values[0],
                    asMusic ? "TONICNAME_PITCH" : "SCM_ARG",
                    values[2]);
                return values[1];
            });

        // function_arglist_common_reparse: EXPECT_SCM function_arglist_optional STRING {
        //     the SCM_IDENTIFIER body over the written string }
        table.Add(
            "function_arglist_common_reparse: EXPECT_SCM function_arglist_optional STRING",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object res = ParserActionHelpers.TryStringVariants(
                    host, ParserActionHelpers.HostPredicate(host, values[0]), values[2]);
                if (!(res is DefaultArgument))
                {
                    ParserActionHelpers.MyReparse(
                        context,
                        locations[2],
                        values[0],
                        res is Pair ? "SYMBOL_LIST" : "SCM_ARG",
                        res);
                }
                else if (ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[0],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[2], values[2]))))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[2], values[0], "LYRIC_ELEMENT", values[2]);
                }
                else
                {
                    // This is going to flag a syntax error, we know the predicate to
                    // be false.
                    ParserActionHelpers.MyReparse(
                        context, locations[2], values[0], "SCM_ARG", values[2]);
                }

                return values[1];
            });

        // function_arglist_common_reparse: EXPECT_SCM function_arglist_optional SYMBOL {
        //     the STRING body over try_word_variants }
        table.Add(
            "function_arglist_common_reparse: EXPECT_SCM function_arglist_optional SYMBOL",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object res = ParserActionHelpers.TryWordVariants(
                    ParserActionHelpers.HostPredicate(host, values[0]), values[2]);
                if (!(res is DefaultArgument))
                {
                    ParserActionHelpers.MyReparse(
                        context,
                        locations[2],
                        values[0],
                        res is Pair ? "SYMBOL_LIST" : "SCM_ARG",
                        res);
                }
                else if (ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[0],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[2], values[2]))))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[2], values[0], "LYRIC_ELEMENT", values[2]);
                }
                else
                {
                    // This is going to flag a syntax error, we know the predicate to
                    // be false.
                    ParserActionHelpers.MyReparse(
                        context, locations[2], values[0], "SCM_ARG", values[2]);
                }

                return values[1];
            });

        // function_arglist_common_reparse: EXPECT_SCM function_arglist_optional full_markup {
        //     $$ = $2;
        //     if (scm_is_true (ly_call ($1, $3)))
        //         MYREPARSE (@3, $1, SCM_ARG, $3);
        //     else if (scm_is_true (ly_call ($1,
        //                  make_music_from_simple (parser, @3, $3))))
        //         MYREPARSE (@3, $1, LYRIC_ELEMENT, $3);
        //     else
        //         // This is going to flag a syntax error, we
        //         // know the predicate to be false.
        //         MYREPARSE (@3, $1, SCM_ARG, $3); }
        table.Add(
            "function_arglist_common_reparse: EXPECT_SCM function_arglist_optional full_markup",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[0], values[2])))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[2], values[0], "SCM_ARG", values[2]);
                }
                else if (ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[0],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[2], values[2]))))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[2], values[0], "LYRIC_ELEMENT", values[2]);
                }
                else
                {
                    // This is going to flag a syntax error, we know the predicate to
                    // be false.
                    ParserActionHelpers.MyReparse(
                        context, locations[2], values[0], "SCM_ARG", values[2]);
                }

                return values[1];
            });

        // function_arglist_common_reparse: EXPECT_SCM function_arglist_optional UNSIGNED {
        //     RAG8's UNSIGNED ladder in final position — every rung reparses. }
        table.Add(
            "function_arglist_common_reparse: EXPECT_SCM function_arglist_optional UNSIGNED",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object pred = values[0];
                object arg = values[2];
                SourceSpan at = locations[2];

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

                return values[1];
            });

        // function_arglist_common_reparse: EXPECT_SCM function_arglist_optional DURATION_IDENTIFIER {
        //     $$ = $2;
        //     if (scm_is_true (ly_call ($1, $3)))
        //         MYREPARSE (@3, $1, DURATION_IDENTIFIER, $3);
        //     else if (scm_is_true (ly_call ($1,
        //                  make_music_from_simple (parser, @3, $3))))
        //         MYREPARSE (@3, $1, DURATION_ARG, $3);
        //     else
        //         MYREPARSE (@3, $1, SCM_ARG, $3); // trigger error }
        table.Add(
            "function_arglist_common_reparse: EXPECT_SCM function_arglist_optional DURATION_IDENTIFIER",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[0], values[2])))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[2], values[0], "DURATION_IDENTIFIER", values[2]);
                }
                else if (ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[0],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[2], values[2]))))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[2], values[0], "DURATION_ARG", values[2]);
                }
                else
                {
                    // trigger error
                    ParserActionHelpers.MyReparse(
                        context, locations[2], values[0], "SCM_ARG", values[2]);
                }

                return values[1];
            });

        // function_arglist_common_reparse: EXPECT_SCM function_arglist_optional '-' UNSIGNED {
        //     $$ = $2;
        //     SCM n = scm_difference ($4, SCM_UNDEFINED);
        //     if (scm_is_true (ly_call ($1, n)))
        //         MYREPARSE (@4, $1, REAL, n);
        //     else {
        //         Music *t = MY_MAKE_MUSIC ("FingeringEvent", @4);
        //         set_property (t, "digit", $4);
        //         SCM m = t->unprotect ();
        //         if (scm_is_true (ly_call ($1, m)))
        //             MYREPARSE (@4, $1, SCM_ARG, m);
        //         else
        //             MYREPARSE (@4, $1, SCM_ARG, $4); } }
        table.Add(
            "function_arglist_common_reparse: EXPECT_SCM function_arglist_optional '-' UNSIGNED",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object n = SchemeNumber.Negate(values[3]);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[0], n)))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[0], "REAL", n);
                }
                else
                {
                    object t = host.MakeMusic("FingeringEvent", locations[3]);
                    host.SetMusicProperty(t, "digit", values[3]);
                    if (ParserActionHelpers.IsSchemeTrue(host.Call(values[0], t)))
                    {
                        ParserActionHelpers.MyReparse(
                            context, locations[3], values[0], "SCM_ARG", t);
                    }
                    else
                    {
                        ParserActionHelpers.MyReparse(
                            context, locations[3], values[0], "SCM_ARG", values[3]);
                    }
                }

                return values[1];
            });

        // function_arglist_common_reparse: EXPECT_SCM function_arglist_optional '-' REAL {
        //     $$ = $2;
        //     SCM n = scm_difference ($4, SCM_UNDEFINED);
        //     MYREPARSE (@4, $1, REAL, n); }
        table.Add(
            "function_arglist_common_reparse: EXPECT_SCM function_arglist_optional '-' REAL",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.MyReparse(
                    context,
                    locations[3],
                    values[0],
                    "REAL",
                    SchemeNumber.Negate(values[3]));
                return values[1];
            });

        // ------ function_arglist_optional (parser.yy 2640-2647) ------
        //
        // The third alternative, `function_arglist_skip_backup BACKUP`, has no
        // action: the synthetic BACKUP marker is swallowed and Bison's $$ = $1
        // stands, which is exactly upstream.

        // function_arglist_optional: EXPECT_OPTIONAL EXPECT_SCM
        //     function_arglist_skip_backup DEFAULT {
        //     $$ = scm_cons (loc_on_copy (parser, @4, $1), $3); }
        table.Add(
            "function_arglist_optional: EXPECT_OPTIONAL EXPECT_SCM function_arglist_skip_backup DEFAULT",
            (context, values, locations, location)
                => new Pair(
                    ParserActionHelpers.RequireHost(context)
                        .LocOnCopy(values[0], locations[3]),
                    values[2]));

        // ------ function_arglist_skip_backup (parser.yy 2649-2655) ------

        // function_arglist_skip_backup: EXPECT_OPTIONAL EXPECT_SCM
        //     function_arglist_skip_backup {
        //     $$ = scm_cons (loc_on_copy (parser, @3, $1), $3); }
        table.Add(
            "function_arglist_skip_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_skip_backup",
            (context, values, locations, location)
                => new Pair(
                    ParserActionHelpers.RequireHost(context)
                        .LocOnCopy(values[0], locations[2]),
                    values[2]));

        // ------ music_function_call (parser.yy 2657-2662) ------

        // music_function_call: MUSIC_FUNCTION function_arglist {
        //     $$ = MAKE_SYNTAX (music_function, @$, $1, $2); }
        //
        // The arglist arrives REVERSED (last argument first), which is the shape the
        // vendored music-function constructor expects.
        table.Add(
            "music_function_call: MUSIC_FUNCTION function_arglist",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).MakeSyntax(
                    "music-function", location, values[0], values[1]));
    }
}
