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

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 3238-3354, 3439-3501);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <content>
/// RULE ACTION GROUP 15 — post events, scripts and text attachments: everything
/// written AFTER a note. <c>post_events</c> accumulates them, <c>post_event</c> and
/// <c>post_event_nofinger</c> attach a direction, <c>script_dir</c> is the
/// <c>^</c>/<c>_</c>/<c>-</c> that supplies one, <c>script_abbreviation</c> is the
/// shorthand articulation set (<c>-.</c>, <c>-&gt;</c>, <c>--</c>),
/// <c>gen_text_def</c> the <c>^"text"</c> attachments, <c>fingering</c> the digits and
/// <c>string_number_event</c> the <c>\3</c> string numbers.
/// <para>
/// THE SHAPE OF THE GROUP: almost every body here takes music the surrounding grammar
/// already built, re-stamps its location, and conditionally writes a
/// <c>direction</c> property onto it. The direction is SCM_UNDEFINED for <c>-</c>,
/// which means "no direction, let the engraver choose" — and that is why every write
/// is guarded rather than defaulted.
/// </para>
/// <para>
/// <c>post_event: post_event_nofinger</c>, <c>direction_less_event:
/// string_number_event</c> and <c>direction_less_event: event_function_event</c> are
/// pass-throughs upstream leaves actionless, so they need nothing here.
/// </para>
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterRag15(RuleActionTable table)
    {
        // ------ post_events (parser.yy 3238-3245) ------

        // post_events: /* empty */ { $$ = SCM_EOL; }
        table.Add(
            "post_events: /* empty */",
            (context, values, locations, location) => Nil.Instance);

        // post_events: post_events post_event { $$ = post_event_cons ($2, $1); }
        //
        // Accumulated in REVERSE — every consumer finishes with scm_reverse_x.
        // post_event_cons is not a plain cons: it UNPACKS a post-event-wrapper,
        // distributing the wrapper's tweaks and properties over its elements.
        table.Add(
            "post_events: post_events post_event",
            (context, values, locations, location)
                => ParserActionHelpers.PostEventCons(values[1], values[0]));

        // ------ post_event_nofinger (parser.yy 3247-3306) ------

        // post_event_nofinger: direction_less_event { $$ = $1; }
        table.Add(
            "post_event_nofinger: direction_less_event",
            (context, values, locations, location) => values[0]);

        // post_event_nofinger: script_dir music_function_call {
        //     Music *m = unsmob<Music> ($2);
        //     if (!m->is_mus_type ("post-event")) {
        //         parser->parser_error (@2, _ ("post-event expected"));
        //         $$ = SCM_UNSPECIFIED;
        //     } else {
        //         m->set_spot (parser->lexer_->override_input (@$));
        //         if (!SCM_UNBNDP ($1))
        //             set_property (m, "direction", $1);
        //         $$ = $2; } }
        //
        // Upstream does NOT null-test its unsmob here (unlike the two rules below),
        // so a non-music music_function_call would be a null dereference there. The
        // port's IsMusicType answers false for a non-music value, which turns that
        // into the "post-event expected" diagnostic — see PORT-COVERAGE.
        table.Add(
            "post_event_nofinger: script_dir music_function_call",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (!host.IsMusicType(values[1], "post-event"))
                {
                    ParserActionHelpers.ParserError(context, locations[1], "post-event expected");
                    return Unspecified.Instance;
                }

                host.SetMusicSpot(values[1], location);
                if (!(values[0] is DefaultArgument))
                {
                    host.SetMusicProperty(values[1], "direction", values[0]);
                }

                return values[1];
            });

        // post_event_nofinger: HYPHEN {
        //     if (!parser->lexer_->is_lyric_state ())
        //         parser->parser_error (@1, _ ("have to be in Lyric mode for lyrics"));
        //     $$ = MY_MAKE_MUSIC ("HyphenEvent", @$)->unprotect (); }
        //
        // The event is made either way — the diagnostic raises the error level but
        // does not stop the parse, so the rest of the file still gets checked.
        table.Add(
            "post_event_nofinger: HYPHEN",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (!host.IsLyricState)
                {
                    ParserActionHelpers.ParserError(
                        context, locations[0], "have to be in Lyric mode for lyrics");
                }

                return host.MakeMusic("HyphenEvent", location);
            });

        // post_event_nofinger: EXTENDER {
        //     if (!parser->lexer_->is_lyric_state ())
        //         parser->parser_error (@1, _ ("have to be in Lyric mode for lyrics"));
        //     $$ = MY_MAKE_MUSIC ("ExtenderEvent", @$)->unprotect (); }
        table.Add(
            "post_event_nofinger: EXTENDER",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (!host.IsLyricState)
                {
                    ParserActionHelpers.ParserError(
                        context, locations[0], "have to be in Lyric mode for lyrics");
                }

                return host.MakeMusic("ExtenderEvent", location);
            });

        // post_event_nofinger: script_dir direction_reqd_event {
        //     if (Music *m = unsmob<Music> ($2)) {
        //         m->set_spot (parser->lexer_->override_input (@$));
        //         if (!SCM_UNBNDP ($1)) { set_property (m, "direction", $1); } }
        //     $$ = $2; }
        //
        // The null test IS reachable: direction_reqd_event's script_abbreviation
        // alternative answers SCM_UNSPECIFIED when the shorthand names no post event.
        table.Add(
            "post_event_nofinger: script_dir direction_reqd_event",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (host.IsMusic(values[1]))
                {
                    host.SetMusicSpot(values[1], location);
                    if (!(values[0] is DefaultArgument))
                    {
                        host.SetMusicProperty(values[1], "direction", values[0]);
                    }
                }

                return values[1];
            });

        // post_event_nofinger: script_dir direction_less_event {
        //     if (auto *m = unsmob<Music> ($2)) { ...the same body... }
        //     $$ = $2; }
        table.Add(
            "post_event_nofinger: script_dir direction_less_event",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (host.IsMusic(values[1]))
                {
                    host.SetMusicSpot(values[1], location);
                    if (!(values[0] is DefaultArgument))
                    {
                        host.SetMusicProperty(values[1], "direction", values[0]);
                    }
                }

                return values[1];
            });

        // post_event_nofinger: '^' fingering {
        //     Music *m = unsmob<Music> ($2);
        //     m->set_spot (parser->lexer_->override_input (@$));
        //     set_property (m, "direction", to_scm (UP));
        //     $$ = $2; }
        //
        // A fingering ALWAYS takes the written direction — there is no SCM_UNBNDP
        // guard, because '^' and '_' are the whole rule rather than a script_dir.
        table.Add(
            "post_event_nofinger: '^' fingering",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.SetMusicSpot(values[1], location);
                host.SetMusicProperty(values[1], "direction", 1L); // UP
                return values[1];
            });

        // post_event_nofinger: '_' fingering { ...the same, with DOWN... }
        table.Add(
            "post_event_nofinger: '_' fingering",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.SetMusicSpot(values[1], location);
                host.SetMusicProperty(values[1], "direction", -1L); // DOWN
                return values[1];
            });

        // ------ post_event (parser.yy 3308-3314) ------

        // post_event: '-' fingering {
        //     unsmob<Music> ($2)->set_spot (parser->lexer_->override_input (@$));
        //     $$ = $2; }
        //
        // `-1` is a fingering with NO direction written: located, and left for the
        // engraver to place.
        table.Add(
            "post_event: '-' fingering",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).SetMusicSpot(values[1], location);
                return values[1];
            });

        // ------ string_number_event (parser.yy 3316-3322) ------

        // string_number_event: E_UNSIGNED {
        //     Music *s = MY_MAKE_MUSIC ("StringNumberEvent", @$);
        //     set_property (s, "string-number", $1);
        //     $$ = s->unprotect (); }
        table.Add(
            "string_number_event: E_UNSIGNED",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object s = host.MakeMusic("StringNumberEvent", location);
                host.SetMusicProperty(s, "string-number", values[0]);
                return s;
            });

        // ------ direction_less_event (parser.yy 3324-3335) ------

        // direction_less_event: EVENT_IDENTIFIER { $$ = $1; }
        table.Add(
            "direction_less_event: EVENT_IDENTIFIER",
            (context, values, locations, location) => values[0]);

        // direction_less_event: tremolo_type {
        //     Music *a = MY_MAKE_MUSIC ("TremoloEvent", @$);
        //     set_property (a, "tremolo-type", $1);
        //     $$ = a->unprotect (); }
        table.Add(
            "direction_less_event: tremolo_type",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object a = host.MakeMusic("TremoloEvent", location);
                host.SetMusicProperty(a, "tremolo-type", values[0]);
                return a;
            });

        RegisterRag15Scripts(table);
    }

    // direction_reqd_event and the two token families that feed it.
    private static void RegisterRag15Scripts(RuleActionTable table)
    {
        // ------ direction_reqd_event (parser.yy 3337-3354) ------

        // direction_reqd_event: gen_text_def { $$ = $1; }
        table.Add(
            "direction_reqd_event: gen_text_def",
            (context, values, locations, location) => values[0]);

        // direction_reqd_event: script_abbreviation {
        //     SCM sym = ly_symbol2scm ("dash" + from_scm<std::string> ($1));
        //     SCM s = parser->lexer_->lookup_identifier_symbol (sym);
        //     Music *original = unsmob<Music> (s);
        //     if (original && original->is_mus_type ("post-event")) {
        //         Music *a = original->clone ();
        //         // origin will be set by post_event_nofinger
        //         $$ = a->unprotect ();
        //     } else {
        //         parser->parser_error (@1, _ ("expecting post-event as script definition"));
        //         $$ = SCM_UNSPECIFIED; } }
        //
        // The shorthand's NAME is the lookup: `-.` gives "Dot", which finds \dashDot
        // in scope. CLONED, because the same \dashDot serves every `-.` in the file
        // and each needs its own location and direction — and the location is
        // deliberately NOT set here: post_event_nofinger, which knows the span
        // including the script_dir, sets it.
        table.Add(
            "direction_reqd_event: script_abbreviation",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object original = host.LookupIdentifier(
                    "dash" + ParserActionHelpers.SchemeStringText(values[0]));

                if (host.IsMusic(original) && host.IsMusicType(original, "post-event"))
                {
                    return host.CloneMusic(original);
                }

                ParserActionHelpers.ParserError(
                    context, locations[0], "expecting post-event as script definition");
                return Unspecified.Instance;
            });

        // ------ gen_text_def (parser.yy 3439-3463) ------

        // gen_text_def: full_markup {
        //     Music *t = MY_MAKE_MUSIC ("TextScriptEvent", @$);
        //     set_property (t, "text", $1);
        //     $$ = t->unprotect (); }
        table.Add(
            "gen_text_def: full_markup",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object t = host.MakeMusic("TextScriptEvent", location);
                host.SetMusicProperty(t, "text", values[0]);
                return t;
            });

        // gen_text_def: STRING {
        //     Music *t = MY_MAKE_MUSIC ("TextScriptEvent", @$);
        //     set_property (t, "text", make_simple_markup ($1));
        //     $$ = t->unprotect (); }
        table.Add(
            "gen_text_def: STRING",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object t = host.MakeMusic("TextScriptEvent", location);
                host.SetMusicProperty(
                    t, "text", ParserActionHelpers.MakeSimpleMarkup(values[0]));
                return t;
            });

        // gen_text_def: SYMBOL {
        //     // Flag a warning? could be unintentional
        //     Music *t = MY_MAKE_MUSIC ("TextScriptEvent", @$);
        //     set_property (t, "text", make_simple_markup ($1));
        //     $$ = t->unprotect (); }
        table.Add(
            "gen_text_def: SYMBOL",
            (context, values, locations, location) =>
            {
                // Flag a warning? could be unintentional
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object t = host.MakeMusic("TextScriptEvent", location);
                host.SetMusicProperty(
                    t, "text", ParserActionHelpers.MakeSimpleMarkup(values[0]));
                return t;
            });

        // gen_text_def: embedded_scm {
        //     // Could be using this for every gen_text_def but for speed
        //     $$ = MAKE_SYNTAX (create_script, @1, $1); }
        //
        // @1, not @$ — the two are the same here (one symbol), and upstream writes
        // @1; kept as written.
        table.Add(
            "gen_text_def: embedded_scm",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context)
                    .MakeSyntax("create-script", locations[0], values[0]));

        // ------ fingering (parser.yy 3465-3471) ------

        // fingering: UNSIGNED {
        //     Music *t = MY_MAKE_MUSIC ("FingeringEvent", @$);
        //     set_property (t, "digit", $1);
        //     $$ = t->unprotect (); }
        table.Add(
            "fingering: UNSIGNED",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object t = host.MakeMusic("FingeringEvent", location);
                host.SetMusicProperty(t, "digit", values[0]);
                return t;
            });

        // ------ script_abbreviation (parser.yy 3473-3495) ------
        //
        // Each answers the NAME half of a \dash<Name> identifier, which
        // direction_reqd_event looks up. Latin-1 strings upstream; CLR strings here,
        // over an alphabet that is ASCII throughout.

        // script_abbreviation: '^' { $$ = scm_from_latin1_string ("Hat"); }
        table.Add(
            "script_abbreviation: '^'",
            (context, values, locations, location) => "Hat");

        // script_abbreviation: '+' { $$ = scm_from_latin1_string ("Plus"); }
        table.Add(
            "script_abbreviation: '+'",
            (context, values, locations, location) => "Plus");

        // script_abbreviation: '-' { $$ = scm_from_latin1_string ("Dash"); }
        table.Add(
            "script_abbreviation: '-'",
            (context, values, locations, location) => "Dash");

        // script_abbreviation: '!' { $$ = scm_from_latin1_string ("Bang"); }
        table.Add(
            "script_abbreviation: '!'",
            (context, values, locations, location) => "Bang");

        // script_abbreviation: ANGLE_CLOSE { $$ = scm_from_latin1_string ("Larger"); }
        table.Add(
            "script_abbreviation: ANGLE_CLOSE",
            (context, values, locations, location) => "Larger");

        // script_abbreviation: '.' { $$ = scm_from_latin1_string ("Dot"); }
        table.Add(
            "script_abbreviation: '.'",
            (context, values, locations, location) => "Dot");

        // script_abbreviation: '_' { $$ = scm_from_latin1_string ("Underscore"); }
        table.Add(
            "script_abbreviation: '_'",
            (context, values, locations, location) => "Underscore");

        // ------ script_dir (parser.yy 3497-3501) ------
        //
        // DOWN and UP are Direction's -1 and 1 (lily/include/direction.hh). The '-'
        // alternative is SCM_UNDEFINED — "no direction written", which every consumer
        // guards with SCM_UNBNDP before writing the property, so the engraver's own
        // choice survives.

        // script_dir: '_' { $$ = to_scm (DOWN); }
        table.Add(
            "script_dir: '_'",
            (context, values, locations, location) => -1L);

        // script_dir: '^' { $$ = to_scm (UP); }
        table.Add(
            "script_dir: '^'",
            (context, values, locations, location) => 1L);

        // script_dir: '-' { $$ = SCM_UNDEFINED; }
        table.Add(
            "script_dir: '-'",
            (context, values, locations, location) => DefaultArgument.Instance);
    }
}
