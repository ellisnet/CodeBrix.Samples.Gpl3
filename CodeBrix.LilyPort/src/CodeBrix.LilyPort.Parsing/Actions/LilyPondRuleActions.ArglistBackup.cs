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

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 2115-2355);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <content>
/// Music-function arglists, the BACKUP half:
/// <c>function_arglist_backup</c>. These rules run while a SKIPPABLE OPTIONAL
/// argument is still open: an argument the predicate refuses is not an error yet,
/// because the optional can be skipped instead — the action conses the optional's
/// DEFAULT (the <c>EXPECT_OPTIONAL</c> value, location-stamped by
/// <c>loc_on_copy</c>) onto the arglist and pushes the refused token back with a
/// synthetic <c>BACKUP</c> in front, for <c>function_arglist_optional</c>'s
/// <c>BACKUP</c> alternative to swallow. Upstream's comment: "function_arglist_backup
/// can't occur at the end of an argument list".
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterArglistBackup(RuleActionTable table)
    {
        // ------ function_arglist_backup (parser.yy 2117-2355) ------

        // function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup
        //     embedded_scm_arg {
        //     if (scm_is_true (ly_call ($2, $4)))
        //         $$ = scm_cons ($4, $3);
        //     else {
        //         $$ = scm_cons (loc_on_copy (parser, @3, $1), $3);
        //         MYBACKUP (SCM_ARG, $4, @4); } }
        table.Add(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup embedded_scm_arg",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], values[3])))
                {
                    return new Pair(values[3], values[2]);
                }

                object result = new Pair(host.LocOnCopy(values[0], locations[2]), values[2]);
                ParserActionHelpers.MyBackup(context, "SCM_ARG", values[3], locations[3]);
                return result;
            });

        // function_arglist_backup: ... function_arglist_backup post_event_nofinger {
        //     accepted: cons; refused: skip the optional and back the event up as an
        //     EVENT_IDENTIFIER }
        table.Add(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup post_event_nofinger",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], values[3])))
                {
                    return new Pair(values[3], values[2]);
                }

                object result = new Pair(host.LocOnCopy(values[0], locations[2]), values[2]);
                ParserActionHelpers.MyBackup(
                    context, "EVENT_IDENTIFIER", values[3], locations[3]);
                return result;
            });

        // function_arglist_backup: ... function_arglist_backup pitch {
        //     if (scm_is_true (ly_call ($2,
        //             make_music_from_simple (parser, @4, $4)))) {
        //         $$ = $3;
        //         MYREPARSE (@4, $2, PITCH_IDENTIFIER, $4);
        //     } else if (scm_is_true (ly_call ($2, $4)))
        //         $$ = scm_cons ($4, $3);
        //     else {
        //         $$ = scm_cons (loc_on_copy (parser, @3, $1), $3);
        //         MYBACKUP (PITCH_IDENTIFIER, $4, @4); } }
        table.Add(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup pitch",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[1],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[3], values[3]))))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "PITCH_IDENTIFIER", values[3]);
                    return values[2];
                }

                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], values[3])))
                {
                    return new Pair(values[3], values[2]);
                }

                object result = new Pair(host.LocOnCopy(values[0], locations[2]), values[2]);
                ParserActionHelpers.MyBackup(
                    context, "PITCH_IDENTIFIER", values[3], locations[3]);
                return result;
            });

        // function_arglist_backup: ... function_arglist_backup steno_tonic_pitch {
        //     the pitch body, reparsing/backing up as TONICNAME_PITCH }
        table.Add(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup steno_tonic_pitch",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[1],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[3], values[3]))))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "TONICNAME_PITCH", values[3]);
                    return values[2];
                }

                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], values[3])))
                {
                    return new Pair(values[3], values[2]);
                }

                object result = new Pair(host.LocOnCopy(values[0], locations[2]), values[2]);
                ParserActionHelpers.MyBackup(
                    context, "TONICNAME_PITCH", values[3], locations[3]);
                return result;
            });

        // function_arglist_backup: ... function_arglist_backup full_markup {
        //     accepted: cons; refused: skip the optional and back the markup up as an
        //     SCM_IDENTIFIER }
        table.Add(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup full_markup",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], values[3])))
                {
                    return new Pair(values[3], values[2]);
                }

                object result = new Pair(host.LocOnCopy(values[0], locations[2]), values[2]);
                ParserActionHelpers.MyBackup(
                    context, "SCM_IDENTIFIER", values[3], locations[3]);
                return result;
            });

        // function_arglist_backup: ... function_arglist_backup UNSIGNED {
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
        //             else {
        //                 $$ = scm_cons (loc_on_copy (parser, @3, $1), $3);
        //                 MYBACKUP (UNSIGNED, $4, @4); }
        //         } else {
        //             $$ = scm_cons (loc_on_copy (parser, @3, $1), $3);
        //             MYBACKUP (UNSIGNED, $4, @4); } } }
        //
        // the ArglistNonBackup group's UNSIGNED ladder, except that the trigger-error rungs are BACKUPS
        // here — the optional can still be skipped.
        table.Add(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup UNSIGNED",
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
                    return values[2];
                }

                if (ParserActionHelpers.IsSchemeTrue(
                    host.Call(pred, new Pair(arg, Nil.Instance))))
                {
                    ParserActionHelpers.MyReparse(
                        context, at, pred, "SYMBOL_LIST", new Pair(arg, Nil.Instance));
                    return values[2];
                }

                object d = ParserActionHelpers.MakeDuration(arg, 0, DefaultArgument.Instance);
                if (!(d is DefaultArgument))
                {
                    if (ParserActionHelpers.IsSchemeTrue(host.Call(pred, d)))
                    {
                        ParserActionHelpers.MyReparse(
                            context, at, pred, "DURATION_IDENTIFIER", d);
                        return values[2];
                    }

                    if (ParserActionHelpers.IsSchemeTrue(
                        host.Call(
                            pred, ParserActionHelpers.MakeMusicFromSimple(host, at, d))))
                    {
                        ParserActionHelpers.MyReparse(context, at, pred, "DURATION_ARG", d);
                        return values[2];
                    }
                }

                object result = new Pair(host.LocOnCopy(values[0], locations[2]), values[2]);
                ParserActionHelpers.MyBackup(context, "UNSIGNED", arg, at);
                return result;
            });

        // function_arglist_backup: ... function_arglist_backup REAL {
        //     if (scm_is_true (ly_call ($2, $4))) {
        //         $$ = $3;
        //         MYREPARSE (@4, $2, REAL, $4);
        //     } else {
        //         $$ = scm_cons (loc_on_copy (parser, @3, $1), $3);
        //         MYBACKUP (REAL, $4, @4); } }
        table.Add(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup REAL",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], values[3])))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "REAL", values[3]);
                    return values[2];
                }

                object result = new Pair(host.LocOnCopy(values[0], locations[2]), values[2]);
                ParserActionHelpers.MyBackup(context, "REAL", values[3], locations[3]);
                return result;
            });

        // function_arglist_backup: ... function_arglist_backup NUMBER_IDENTIFIER {
        //     accepted: cons; refused: skip the optional and back the number up }
        table.Add(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup NUMBER_IDENTIFIER",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], values[3])))
                {
                    return new Pair(values[3], values[2]);
                }

                object result = new Pair(host.LocOnCopy(values[0], locations[2]), values[2]);
                ParserActionHelpers.MyBackup(
                    context, "NUMBER_IDENTIFIER", values[3], locations[3]);
                return result;
            });

        // function_arglist_backup: ... function_arglist_backup '-' UNSIGNED {
        //     SCM n = scm_difference ($5, SCM_UNDEFINED);
        //     if (scm_is_true (ly_call ($2, n))) {
        //         $$ = $3;
        //         MYREPARSE (@5, $2, REAL, n);
        //     } else {
        //         Music *t = MY_MAKE_MUSIC ("FingeringEvent", @5);
        //         set_property (t, "digit", $5);
        //         $$ = t->unprotect ();
        //         if (scm_is_true (ly_call ($2, $$)))
        //             $$ = scm_cons ($$, $3);
        //         else {
        //             $$ = scm_cons (loc_on_copy (parser, @3, $1), $3);
        //             MYBACKUP (UNSIGNED, $5, @5);
        //             parser->lexer_->push_extra_token (@4, '-'); } } }
        //
        // The '-' is pushed AFTER MYBACKUP, so it is delivered FIRST — the input is
        // restored to '-' UNSIGNED behind the BACKUP marker, exactly as written.
        table.Add(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup '-' UNSIGNED",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object n = SchemeNumber.Negate(values[4]);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], n)))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[4], values[1], "REAL", n);
                    return values[2];
                }

                object t = host.MakeMusic("FingeringEvent", locations[4]);
                host.SetMusicProperty(t, "digit", values[4]);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], t)))
                {
                    return new Pair(t, values[2]);
                }

                object result = new Pair(host.LocOnCopy(values[0], locations[2]), values[2]);
                ParserActionHelpers.MyBackup(context, "UNSIGNED", values[4], locations[4]);
                ParserActionHelpers.PushExtraToken(
                    context, locations[3], "'-'", Unspecified.Instance);
                return result;
            });

        // function_arglist_backup: ... function_arglist_backup '-' REAL {
        //     SCM n = scm_difference ($5, SCM_UNDEFINED);
        //     if (scm_is_true (ly_call ($2, n))) {
        //         MYREPARSE (@5, $2, REAL, n);
        //         $$ = $3;
        //     } else {
        //         $$ = scm_cons (loc_on_copy (parser, @3, $1), $3);
        //         MYBACKUP (REAL, n, @5); } }
        //
        // Note the refused value backs up NEGATED — upstream pushes n, not $5.
        table.Add(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup '-' REAL",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object n = SchemeNumber.Negate(values[4]);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], n)))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[4], values[1], "REAL", n);
                    return values[2];
                }

                object result = new Pair(host.LocOnCopy(values[0], locations[2]), values[2]);
                ParserActionHelpers.MyBackup(context, "REAL", n, locations[4]);
                return result;
            });

        // function_arglist_backup: ... function_arglist_backup '-' NUMBER_IDENTIFIER {
        //     SCM n = scm_difference ($5, SCM_UNDEFINED);
        //     if (scm_is_true (ly_call ($2, n)))
        //         $$ = scm_cons (n, $3);
        //     else {
        //         $$ = scm_cons (loc_on_copy (parser, @3, $1), $3);
        //         MYBACKUP (NUMBER_IDENTIFIER, n, @5); } }
        table.Add(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup '-' NUMBER_IDENTIFIER",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object n = SchemeNumber.Negate(values[4]);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], n)))
                {
                    return new Pair(n, values[2]);
                }

                object result = new Pair(host.LocOnCopy(values[0], locations[2]), values[2]);
                ParserActionHelpers.MyBackup(
                    context, "NUMBER_IDENTIFIER", n, locations[4]);
                return result;
            });

        // function_arglist_backup: ... function_arglist_backup DURATION_IDENTIFIER {
        //     $$ = $3;
        //     if (scm_is_true (ly_call ($2, $4)))
        //         MYREPARSE (@4, $2, DURATION_IDENTIFIER, $4);
        //     else if (scm_is_true (ly_call ($2,
        //                  make_music_from_simple (parser, @4, $4))))
        //         MYREPARSE (@4, $2, DURATION_ARG, $4);
        //     else {
        //         $$ = scm_cons (loc_on_copy (parser, @3, $1), $3);
        //         MYBACKUP (DURATION_IDENTIFIER, $4, @4); } }
        table.Add(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup DURATION_IDENTIFIER",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (ParserActionHelpers.IsSchemeTrue(host.Call(values[1], values[3])))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "DURATION_IDENTIFIER", values[3]);
                    return values[2];
                }

                if (ParserActionHelpers.IsSchemeTrue(
                    host.Call(
                        values[1],
                        ParserActionHelpers.MakeMusicFromSimple(host, locations[3], values[3]))))
                {
                    ParserActionHelpers.MyReparse(
                        context, locations[3], values[1], "DURATION_ARG", values[3]);
                    return values[2];
                }

                object result = new Pair(host.LocOnCopy(values[0], locations[2]), values[2]);
                ParserActionHelpers.MyBackup(
                    context, "DURATION_IDENTIFIER", values[3], locations[3]);
                return result;
            });

        // function_arglist_backup: ... function_arglist_backup SCM_IDENTIFIER {
        //     SCM res = try_string_variants ($2, $4);
        //     if (!SCM_UNBNDP (res))
        //         if (scm_is_pair (res)) {
        //             $$ = $3;
        //             MYREPARSE (@4, $2, SYMBOL_LIST, res);
        //         }
        //         else
        //             $$ = scm_cons (res, $3);
        //     else {
        //         $$ = scm_cons (loc_on_copy (parser, @3, $1), $3);
        //         MYBACKUP (SCM_IDENTIFIER, $4, @4); } }
        table.Add(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup SCM_IDENTIFIER",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object res = ParserActionHelpers.TryStringVariants(
                    host, ParserActionHelpers.HostPredicate(host, values[1]), values[3]);
                if (!(res is DefaultArgument))
                {
                    if (res is Pair)
                    {
                        ParserActionHelpers.MyReparse(
                            context, locations[3], values[1], "SYMBOL_LIST", res);
                        return values[2];
                    }

                    return new Pair(res, values[2]);
                }

                object result = new Pair(host.LocOnCopy(values[0], locations[2]), values[2]);
                ParserActionHelpers.MyBackup(
                    context, "SCM_IDENTIFIER", values[3], locations[3]);
                return result;
            });

        // function_arglist_backup: ... function_arglist_backup STRING {
        //     the SCM_IDENTIFIER body, backing up as STRING }
        table.Add(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup STRING",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object res = ParserActionHelpers.TryStringVariants(
                    host, ParserActionHelpers.HostPredicate(host, values[1]), values[3]);
                if (!(res is DefaultArgument))
                {
                    if (res is Pair)
                    {
                        ParserActionHelpers.MyReparse(
                            context, locations[3], values[1], "SYMBOL_LIST", res);
                        return values[2];
                    }

                    return new Pair(res, values[2]);
                }

                object result = new Pair(host.LocOnCopy(values[0], locations[2]), values[2]);
                ParserActionHelpers.MyBackup(context, "STRING", values[3], locations[3]);
                return result;
            });

        // function_arglist_backup: ... function_arglist_backup SYMBOL {
        //     the STRING body over try_word_variants — and the refused SYMBOL backs
        //     up as a STRING, exactly as upstream writes it }
        table.Add(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup SYMBOL",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object res = ParserActionHelpers.TryWordVariants(
                    ParserActionHelpers.HostPredicate(host, values[1]), values[3]);
                if (!(res is DefaultArgument))
                {
                    if (res is Pair)
                    {
                        ParserActionHelpers.MyReparse(
                            context, locations[3], values[1], "SYMBOL_LIST", res);
                        return values[2];
                    }

                    return new Pair(res, values[2]);
                }

                object result = new Pair(host.LocOnCopy(values[0], locations[2]), values[2]);
                ParserActionHelpers.MyBackup(context, "STRING", values[3], locations[3]);
                return result;
            });

        // ------ the REPARSE tail (parser.yy 2326-2354) ------

        // function_arglist_backup: function_arglist_backup REPARSE pitch_or_music {
        //     if (scm_is_true (ly_call ($2, $3)))
        //         $$ = scm_cons ($3, $1);
        //     else
        //         $$ = check_scheme_arg (parser, @3,
        //                                make_music_from_simple (parser, @3, $3),
        //                                $1, $2); }
        //
        // Unlike the ArglistNonBackup group's twin, no display argument — upstream passes none here.
        table.Add(
            "function_arglist_backup: function_arglist_backup REPARSE pitch_or_music",
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
                    values[1]);
            });

        // function_arglist_backup: function_arglist_backup REPARSE bare_number_common {
        //     $$ = check_scheme_arg (parser, @3, $3, $1, $2); }
        table.Add(
            "function_arglist_backup: function_arglist_backup REPARSE bare_number_common",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[0], values[1]));

        // function_arglist_backup: function_arglist_backup REPARSE duration {
        //     $$ = check_scheme_arg (parser, @3, $3, $1, $2); }
        table.Add(
            "function_arglist_backup: function_arglist_backup REPARSE duration",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[0], values[1]));

        // function_arglist_backup: function_arglist_backup REPARSE reparsed_rhythm {
        //     $$ = check_scheme_arg (parser, @3, $3, $1, $2); }
        table.Add(
            "function_arglist_backup: function_arglist_backup REPARSE reparsed_rhythm",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[0], values[1]));

        // function_arglist_backup: function_arglist_backup REPARSE symbol_list_arg {
        //     $$ = check_scheme_arg (parser, @3, $3, $1, $2); }
        table.Add(
            "function_arglist_backup: function_arglist_backup REPARSE symbol_list_arg",
            (context, values, locations, location)
                => ParserActionHelpers.CheckSchemeArg(
                    context, locations[2], values[2], values[0], values[1]));
    }
}
