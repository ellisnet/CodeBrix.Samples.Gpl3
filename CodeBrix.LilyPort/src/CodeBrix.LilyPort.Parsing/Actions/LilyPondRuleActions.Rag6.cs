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
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 1470-1600, 1698-1778);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <content>
/// RULE ACTION GROUP 6 — core music assembly: <c>music_list</c>,
/// <c>braced_music_list</c>, <c>pitch_as_music</c>, <c>music_embedded</c> and
/// <c>music_embedded_backup</c> (the group's <c>MYBACKUP</c> site),
/// <c>repeated_music</c>, <c>alternative_music</c>, <c>sequential_music</c>,
/// <c>simultaneous_music</c>, <c>new_lyrics</c>, the <c>LYRICSTO</c> alternatives of
/// <c>basic_music</c>, <c>contexted_basic_music</c> (where RAG5's
/// <c>START_MAKE_SYNTAX</c> prefixes are FINISHED with their music argument),
/// <c>composite_music</c>'s <c>new_lyrics</c> alternative and
/// <c>grouped_music_list</c>. The <c>music</c>, <c>music_assign</c>,
/// <c>simple_music</c>, <c>contextable_music</c>, <c>music_bare</c> and remaining
/// <c>basic_music</c>/<c>composite_music</c>/<c>music_embedded</c> alternatives are
/// pass-throughs upstream leaves actionless, so they need nothing here.
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterRag6(RuleActionTable table)
    {
        // ------ music_list (parser.yy 1470-1485) ------

        /* The representation of a list is reversed to have efficient append. */

        // music_list: /* empty */ { $$ = SCM_EOL; }
        table.Add(
            "music_list: /* empty */",
            (context, values, locations, location) => Nil.Instance);

        // music_list: music_list music_embedded {
        //     if (unsmob<Music> ($2))
        //         $$ = scm_cons ($2, $1); }
        //
        // A non-music element (an evaluated Scheme expression that produced nothing
        // musical) is silently skipped via the implicit $$ = $1.
        table.Add(
            "music_list: music_list music_embedded",
            (context, values, locations, location)
                => values[1] is MusicObject
                    ? new Pair(values[1], values[0])
                    : values[0]);

        // music_list: music_list error {
        //     Music *m = MY_MAKE_MUSIC("Music", @$);
        //     // ugh. code dup
        //     set_property (m, "error-found", SCM_BOOL_T);
        //     $$ = scm_cons (m->self_scm (), $1);
        //     m->unprotect (); /* UGH */ }
        table.Add(
            "music_list: music_list error",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object m = host.MakeMusic("Music", location);
                host.SetMusicProperty(m, "error-found", true);
                return new Pair(m, values[0]);
            });

        // ------ braced_music_list (parser.yy 1487-1492) ------

        // braced_music_list: '{' music_list '}' {
        //     $$ = reverse_music_list (parser, @$, $2, true, false); }
        table.Add(
            "braced_music_list: '{' music_list '}'",
            (context, values, locations, location)
                => ParserActionHelpers.ReverseMusicList(
                    ParserActionHelpers.RequireHost(context),
                    location,
                    values[1],
                    true,
                    false));

        // ------ pitch_as_music (parser.yy 1499-1509) ------

        // pitch_as_music: pitch_or_music {
        //     $$ = make_music_from_simple (parser, @1, $1);
        //     if (!unsmob<Music> ($$)) {
        //         parser->parser_error (@1, _ ("music expected"));
        //         $$ = MAKE_SYNTAX (unspecified_music, @$); } }
        table.Add(
            "pitch_as_music: pitch_or_music",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object result = ParserActionHelpers.MakeMusicFromSimple(
                    host, locations[0], values[0]);
                if (!(result is MusicObject))
                {
                    ParserActionHelpers.ParserError(context, locations[0], "music expected");
                    result = host.MakeSyntax("unspecified-music", location);
                }

                return result;
            });

        // ------ music_embedded (parser.yy 1511-1534) ------
        //
        // (The `music` and `post_event` alternatives are actionless pass-throughs.)

        // music_embedded: music_embedded_backup { $$ = $1; }
        table.Add(
            "music_embedded: music_embedded_backup",
            (context, values, locations, location) => values[0]);

        // music_embedded: music_embedded_backup BACKUP lyric_element_music { $$ = $3; }
        //
        // The consumer of music_embedded_backup's MYBACKUP: the backed-up value came
        // back as a LYRIC_ELEMENT and was parsed as lyric music, which replaces it.
        table.Add(
            "music_embedded: music_embedded_backup BACKUP lyric_element_music",
            (context, values, locations, location) => values[2]);

        // music_embedded: duration post_events %prec ':' {
        //     Music *n = MY_MAKE_MUSIC ("NoteEvent", @$);
        //     parser->default_duration_ = *unsmob<Duration> ($1);
        //     set_property (n, "duration", $1);
        //     if (scm_is_pair ($2))
        //         set_property (n, "articulations",
        //                       scm_reverse_x ($2, SCM_EOL));
        //     $$ = n->unprotect (); }
        //
        // THE TWIN of RAG2's `embedded_lilypond: duration post_events %prec ':'`,
        // which it must NOT be copied from: there a bare duration stays a Duration
        // and nothing happens without post events; HERE the NoteEvent is ALWAYS
        // made, the default duration is ALWAYS assigned, and only the articulations
        // are conditional. Ported from its own upstream body — see PORT-COVERAGE.
        table.Add(
            "music_embedded: duration post_events %prec ':'",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object n = host.MakeMusic("NoteEvent", location);

                // parser->default_duration_ = *unsmob<Duration> ($1); — assigned BY
                // VALUE upstream, and Duration is a value type here too.
                host.DefaultDuration = (Duration)values[0];
                host.SetMusicProperty(n, "duration", values[0]);

                if (values[1] is Pair)
                {
                    host.SetMusicProperty(
                        n,
                        "articulations",
                        ParserActionHelpers.ReverseInPlace(values[1], Nil.Instance));
                }

                return n;
            });

        // ------ music_embedded_backup (parser.yy 1536-1550) ------

        // music_embedded_backup: embedded_scm {
        //     if (scm_is_eq ($1, SCM_UNSPECIFIED)
        //         || unsmob<Music> ($1))
        //         $$ = $1;
        //     else if (parser->lexer_->is_lyric_state ()
        //              && Text_interface::is_markup ($1))
        //         MYBACKUP (LYRIC_ELEMENT, $1, @1);
        //     else {
        //         @$.warning (_ ("Ignoring non-music expression"));
        //         $$ = SCM_UNSPECIFIED; } }
        //
        // In the MYBACKUP branch upstream leaves $$ as the pre-set $1; both
        // consumers of the backed-up alternative ignore that value. Upstream reaches
        // this reduce in a default-reduction state (usually with NO lookahead read);
        // the port's driver reads one eagerly, and MyBackup's opening
        // PushBackLookahead restores the identical token stream — see PORT-COVERAGE
        // for the per-site mode analysis.
        table.Add(
            "music_embedded_backup: embedded_scm",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (values[0] is Unspecified || values[0] is MusicObject)
                {
                    return values[0];
                }

                if (host.IsLyricState && host.IsMarkup(values[0]))
                {
                    ParserActionHelpers.MyBackup(
                        context, "LYRIC_ELEMENT", values[0], locations[0]);
                    return values[0];
                }

                host.Warning(location, "Ignoring non-music expression");
                return Unspecified.Instance;
            });

        // ------ repeated_music (parser.yy 1559-1568) ------

        // repeated_music: REPEAT simple_string unsigned_integer music {
        //     $$ = MAKE_SYNTAX (repeat, @$, $2, $3, $4); }
        table.Add(
            "repeated_music: REPEAT simple_string unsigned_integer music",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context)
                    .MakeSyntax("repeat", location, values[1], values[2], values[3]));

        // repeated_music: REPEAT simple_string unsigned_integer music alternative_music {
        //     $$ = MAKE_SYNTAX (repeat_alt, @$, $2, $3, $4, $5); }
        table.Add(
            "repeated_music: REPEAT simple_string unsigned_integer music alternative_music",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).MakeSyntax(
                    "repeat-alt", location, values[1], values[2], values[3], values[4]));

        // ------ alternative_music (parser.yy 1570-1574) ------

        // alternative_music: ALTERNATIVE basic_music {
        //     $$ = MAKE_SYNTAX (alternative, @$, $2); }
        table.Add(
            "alternative_music: ALTERNATIVE basic_music",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context)
                    .MakeSyntax("alternative", location, values[1]));

        // ------ sequential_music (parser.yy 1576-1583) ------

        // sequential_music: SEQUENTIAL braced_music_list {
        //     $$ = MAKE_SYNTAX (sequential_music, @$, $2); }
        table.Add(
            "sequential_music: SEQUENTIAL braced_music_list",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context)
                    .MakeSyntax("sequential-music", location, values[1]));

        // sequential_music: braced_music_list {
        //     $$ = MAKE_SYNTAX (sequential_music, @$, $1); }
        table.Add(
            "sequential_music: braced_music_list",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context)
                    .MakeSyntax("sequential-music", location, values[0]));

        // ------ simultaneous_music (parser.yy 1585-1594) ------

        // simultaneous_music: SIMULTANEOUS braced_music_list {
        //     $$ = MAKE_SYNTAX (simultaneous_music, @$, $2); }
        table.Add(
            "simultaneous_music: SIMULTANEOUS braced_music_list",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context)
                    .MakeSyntax("simultaneous-music", location, values[1]));

        // simultaneous_music: DOUBLE_ANGLE_OPEN music_list DOUBLE_ANGLE_CLOSE {
        //     $$ = MAKE_SYNTAX (simultaneous_music, @$,
        //                       reverse_music_list (parser, @$, $2,
        //                                           true, false)); }
        table.Add(
            "simultaneous_music: DOUBLE_ANGLE_OPEN music_list DOUBLE_ANGLE_CLOSE",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                return host.MakeSyntax(
                    "simultaneous-music",
                    location,
                    ParserActionHelpers.ReverseMusicList(host, location, values[1], true, false));
            });

        // ------ new_lyrics (parser.yy 1698-1705) ------

        // new_lyrics: ADDLYRICS optional_context_mods lyric_mode_music {
        //     $$ = scm_acons ($3, $2, SCM_EOL); }
        table.Add(
            "new_lyrics: ADDLYRICS optional_context_mods lyric_mode_music",
            (context, values, locations, location)
                => new Pair(new Pair(values[2], values[1]), Nil.Instance));

        // new_lyrics: new_lyrics ADDLYRICS optional_context_mods lyric_mode_music {
        //     $$ = scm_acons ($4, $3, $1); }
        //
        // Consed in FRONT, so the accumulated alist is in reverse; the consumers
        // (contexted_basic_music, composite_music) restore document order with
        // scm_reverse_x.
        table.Add(
            "new_lyrics: new_lyrics ADDLYRICS optional_context_mods lyric_mode_music",
            (context, values, locations, location)
                => new Pair(new Pair(values[3], values[2]), values[0]));

        // ------ basic_music (parser.yy 1723-1735) ------
        //
        // (The music_function_call, repeated_music, alternative_music and music_bare
        // alternatives are actionless pass-throughs.)

        // basic_music: LYRICSTO simple_string lyric_mode_music {
        //     $$ = MAKE_SYNTAX (lyric_combine, @$, $2, SCM_EOL, $3); }
        table.Add(
            "basic_music: LYRICSTO simple_string lyric_mode_music",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).MakeSyntax(
                    "lyric-combine", location, values[1], Nil.Instance, values[2]));

        // basic_music: LYRICSTO symbol '=' simple_string lyric_mode_music {
        //     $$ = MAKE_SYNTAX (lyric_combine, @$, $4, $2, $5); }
        table.Add(
            "basic_music: LYRICSTO symbol '=' simple_string lyric_mode_music",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).MakeSyntax(
                    "lyric-combine", location, values[3], values[1], values[4]));

        // ------ contexted_basic_music (parser.yy 1743-1759) ------
        //
        // The FINISH_MAKE_SYNTAX side of RAG5's context_prefix: the prefix value is
        // the list (constructor type id mods) built WITHOUT a music argument, and
        // these rules apply it to the music with a location.

        // contexted_basic_music: context_prefix contextable_music new_lyrics {
        //     Input i;
        //     i.set_location (@1, @2);
        //     $$ = FINISH_MAKE_SYNTAX ($1, i, $2);
        //     $$ = MAKE_SYNTAX (add_lyrics, @$, $$, scm_reverse_x ($3, SCM_EOL));
        // } %prec COMPOSITE
        //
        // The finished context music is located at @1-@2 ONLY — the lyrics are not
        // part of the context's span — while the add_lyrics wrapper covers @$.
        table.Add(
            "contexted_basic_music: context_prefix contextable_music new_lyrics %prec COMPOSITE",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object result = ParserActionHelpers.FinishMakeSyntax(
                    host,
                    values[0],
                    SourceSpan.Join(locations[0], locations[1]),
                    values[1]);
                return host.MakeSyntax(
                    "add-lyrics",
                    location,
                    result,
                    ParserActionHelpers.ReverseInPlace(values[2], Nil.Instance));
            });

        // contexted_basic_music: context_prefix contextable_music {
        //     $$ = FINISH_MAKE_SYNTAX ($1, @$, $2); } %prec COMPOSITE
        table.Add(
            "contexted_basic_music: context_prefix contextable_music %prec COMPOSITE",
            (context, values, locations, location)
                => ParserActionHelpers.FinishMakeSyntax(
                    ParserActionHelpers.RequireHost(context),
                    values[0],
                    location,
                    values[1]));

        // contexted_basic_music: context_prefix contexted_basic_music {
        //     $$ = FINISH_MAKE_SYNTAX ($1, @$, $2); }
        //
        // \new Staff \new Voice { ... }: one prefix layer at a time, innermost
        // first.
        table.Add(
            "contexted_basic_music: context_prefix contexted_basic_music",
            (context, values, locations, location)
                => ParserActionHelpers.FinishMakeSyntax(
                    ParserActionHelpers.RequireHost(context),
                    values[0],
                    location,
                    values[1]));

        // ------ composite_music (parser.yy 1761-1768) ------
        //
        // (The basic_music %prec COMPOSITE and contexted_basic_music alternatives
        // are actionless pass-throughs.)

        // composite_music: basic_music new_lyrics {
        //     $$ = MAKE_SYNTAX (add_lyrics, @$, $1, scm_reverse_x ($2, SCM_EOL));
        // } %prec COMPOSITE
        table.Add(
            "composite_music: basic_music new_lyrics %prec COMPOSITE",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).MakeSyntax(
                    "add-lyrics",
                    location,
                    values[0],
                    ParserActionHelpers.ReverseInPlace(values[1], Nil.Instance)));

        // ------ grouped_music_list (parser.yy 1776-1779) ------

        // grouped_music_list: simultaneous_music { $$ = $1; }
        table.Add(
            "grouped_music_list: simultaneous_music",
            (context, values, locations, location) => values[0]);

        // grouped_music_list: sequential_music { $$ = $1; }
        table.Add(
            "grouped_music_list: sequential_music",
            (context, values, locations, location) => values[0]);
    }
}
