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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 3109-3236, 3852-3918);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <content>
/// Chords and event chords. TWO different things share the
/// word "chord" here and the group covers both.
/// <para>
/// <c>&lt;c e g&gt;</c> is a NOTE chord: <c>chord_body</c> and
/// <c>chord_body_element</c> collect written note events, and
/// <c>note_chord_element</c> gives them all one duration. <c>c:maj7</c> is a CHORD
/// NAME: <c>new_chord</c>, <c>chord_separator</c>, <c>chord_item</c> and
/// <c>step_number</c> describe a chord symbolically and hand it to the vendored
/// <c>construct-chord-elements</c> to be realized. <c>event_chord</c> sits above both
/// and is also where a lone note, a <c>q</c> repetition and a multi-measure rest
/// arrive.
/// </para>
/// <para>
/// <c>event_chord: tempo_event</c>, <c>event_chord: note_chord_element</c>,
/// <c>chord_body_element: post_event</c> and the three
/// <c>music_function_chord_body</c> alternatives are pass-throughs upstream leaves
/// actionless, so they need nothing here.
/// </para>
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterChords(RuleActionTable table)
    {
        // ------ event_chord (parser.yy 3109-3127) ------

        // event_chord: simple_element post_events {
        //     // Let the rhythmic music iterator sort this mess out.
        //     if (scm_is_pair ($2)) {
        //         set_property (unsmob<Music> ($$), "articulations",
        //                       scm_reverse_x ($2, SCM_EOL)); } } %prec ':'
        //
        // $$ is the simple_element itself; with no post events nothing happens at all.
        table.Add(
            "event_chord: simple_element post_events %prec ':'",
            (context, values, locations, location) =>
            {
                // Let the rhythmic music iterator sort this mess out.
                if (values[1] is Pair)
                {
                    ParserActionHelpers.RequireHost(context).SetMusicProperty(
                        values[0],
                        "articulations",
                        ParserActionHelpers.ReverseInPlace(values[1], Nil.Instance));
                }

                return values[0];
            });

        // event_chord: CHORD_REPETITION optional_notemode_duration post_events {
        //     $$ = MAKE_SYNTAX (repetition_chord, @$,
        //                       $2, scm_reverse_x ($3, SCM_EOL)); } %prec ':'
        table.Add(
            "event_chord: CHORD_REPETITION optional_notemode_duration post_events %prec ':'",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).MakeSyntax(
                    "repetition-chord",
                    location,
                    values[1],
                    ParserActionHelpers.ReverseInPlace(values[2], Nil.Instance)));

        // event_chord: MULTI_MEASURE_REST optional_notemode_duration post_events {
        //     $$ = MAKE_SYNTAX (multi_measure_rest, @$, $2,
        //                       scm_reverse_x ($3, SCM_EOL)); } %prec ':'
        table.Add(
            "event_chord: MULTI_MEASURE_REST optional_notemode_duration post_events %prec ':'",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).MakeSyntax(
                    "multi-measure-rest",
                    location,
                    values[1],
                    ParserActionHelpers.ReverseInPlace(values[2], Nil.Instance)));

        // ------ note_chord_element (parser.yy 3130-3146) ------

        // note_chord_element: chord_body optional_notemode_duration post_events {
        //     Music *m = unsmob<Music> ($1);
        //     SCM dur = unsmob<Duration> ($2)->smobbed_copy ();
        //     SCM es = get_property (m, "elements");
        //     SCM postevs = scm_reverse_x ($3, SCM_EOL);
        //     for (SCM s = es; scm_is_pair (s); s = scm_cdr (s))
        //         set_property (unsmob<Music> (scm_car (s)), "duration", dur);
        //     es = ly_append (es, postevs);
        //     set_property (m, "elements", es);
        //     m->set_spot (parser->lexer_->override_input (@$));
        //     $$ = m->self_scm (); } %prec ':'
        //
        // WHERE A CHORD'S DURATION COMES FROM: it is written once, after the closing
        // angle bracket, and applied to every element — the elements themselves were
        // parsed without one. The post events join the SAME elements list rather than
        // an articulations property, which is what lets `<c e>4->` attach to the chord.
        table.Add(
            "note_chord_element: chord_body optional_notemode_duration post_events %prec ':'",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);

                // unsmob<Duration> ($2)->smobbed_copy () — a fresh box, shared by
                // every element exactly as the smob copy is.
                object duration = (Duration)values[1];
                object elements = host.GetMusicProperty(values[0], "elements");
                object postEvents = ParserActionHelpers.ReverseInPlace(values[2], Nil.Instance);

                for (object s = elements; s is Pair pair; s = pair.Cdr)
                {
                    host.SetMusicProperty(pair.Car, "duration", duration);
                }

                host.SetMusicProperty(
                    values[0], "elements", ParserActionHelpers.Append(elements, postEvents));
                host.SetMusicSpot(values[0], location);
                return values[0];
            });

        // ------ chord_body (parser.yy 3148-3159) ------

        // chord_body: ANGLE_OPEN chord_body_elements ANGLE_CLOSE {
        //     $$ = MAKE_SYNTAX (event_chord, @$,
        //                       reverse_music_list (parser, @$, $2, false, false)); }
        //
        // preserve = false, compress = false: an unattachable post event inside
        // <...> is DROPPED with a warning rather than kept on an empty chord.
        table.Add(
            "chord_body: ANGLE_OPEN chord_body_elements ANGLE_CLOSE",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                return host.MakeSyntax(
                    "event-chord",
                    location,
                    ParserActionHelpers.ReverseMusicList(host, location, values[1], false, false));
            });

        // chord_body: FIGURE_OPEN figure_list FIGURE_CLOSE {
        //     $$ = MAKE_SYNTAX (event_chord, @$, scm_reverse_x ($2, SCM_EOL)); }
        //
        // The figured-bass shape (the FiguredBass group builds figure_list): a plain reverse, with no
        // post-event sorting, because a figure list cannot contain one.
        table.Add(
            "chord_body: FIGURE_OPEN figure_list FIGURE_CLOSE",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).MakeSyntax(
                    "event-chord",
                    location,
                    ParserActionHelpers.ReverseInPlace(values[1], Nil.Instance)));

        // ------ chord_body_elements (parser.yy 3161-3167) ------

        // chord_body_elements: /* empty */ { $$ = SCM_EOL; }
        table.Add(
            "chord_body_elements: /* empty */",
            (context, values, locations, location) => Nil.Instance);

        // chord_body_elements: chord_body_elements chord_body_element {
        //     if (unsmob<Music> ($2))
        //         $$ = scm_cons ($2, $1); }
        //
        // A non-music element — the "not a rhythmic event" case below — is silently
        // skipped through the implicit $$ = $1, the error already having been given.
        table.Add(
            "chord_body_elements: chord_body_elements chord_body_element",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).IsMusic(values[1])
                    ? new Pair(values[1], values[0])
                    : values[0]);

        RegisterChordBodyElements(table);
        RegisterChordNames(table);
    }

    // The written note events inside < >, plus the event-function call.
    private static void RegisterChordBodyElements(RuleActionTable table)
    {
        // ------ chord_body_element (parser.yy 3169-3223) ------

        // chord_body_element: pitch_or_tonic_pitch exclamations questions octave_check
        //                     post_events %prec ':' {
        //     bool q = from_scm<bool> ($3);
        //     bool ex = from_scm<bool> ($2);
        //     SCM check = $4;
        //     SCM post = $5;
        //     Music *n = MY_MAKE_MUSIC ("NoteEvent", @$);
        //     set_property (n, "pitch", $1);
        //     if (q) set_property (n, "cautionary", SCM_BOOL_T);
        //     if (ex || q) set_property (n, "force-accidental", SCM_BOOL_T);
        //     if (scm_is_pair (post)) {
        //         SCM arts = scm_reverse_x (post, SCM_EOL);
        //         set_property (n, "articulations", arts); }
        //     if (scm_is_number (check)) {
        //         int q = from_scm<int> (check);
        //         set_property (n, "absolute-octave", to_scm (q-1)); }
        //     $$ = n->unprotect (); }
        //
        // the PitchesAndDurations group's pitch_or_music without the duration: inside a chord the elements
        // carry no duration of their own, and note_chord_element gives them one.
        table.Add(
            "chord_body_element: pitch_or_tonic_pitch exclamations questions octave_check"
            + " post_events %prec ':'",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);

                // from_scm<bool> is scm_is_eq (s, SCM_BOOL_T) — exactly #t, so the
                // SCM_UNDEFINED "no marks" value reads as false. See PORT-COVERAGE.
                bool q = values[2] is bool asked && asked;
                bool ex = values[1] is bool wrote && wrote;
                object check = values[3];
                object post = values[4];

                object n = host.MakeMusic("NoteEvent", location);
                host.SetMusicProperty(n, "pitch", values[0]);

                if (q)
                {
                    host.SetMusicProperty(n, "cautionary", true);
                }

                if (ex || q)
                {
                    host.SetMusicProperty(n, "force-accidental", true);
                }

                if (post is Pair)
                {
                    host.SetMusicProperty(
                        n, "articulations", ParserActionHelpers.ReverseInPlace(post, Nil.Instance));
                }

                if (SchemeNumber.IsNumber(check))
                {
                    host.SetMusicProperty(
                        n,
                        "absolute-octave",
                        (long)(SchemeConvert.ToInt(check, "chord_body_element") - 1));
                }

                return n;
            });

        // chord_body_element: DRUM_PITCH post_events %prec ':' {
        //     Music *n = MY_MAKE_MUSIC ("NoteEvent", @$);
        //     set_property (n, "drum-type", $1);
        //     if (scm_is_pair ($2)) {
        //         SCM arts = scm_reverse_x ($2, SCM_EOL);
        //         set_property (n, "articulations", arts); }
        //     $$ = n->unprotect (); }
        table.Add(
            "chord_body_element: DRUM_PITCH post_events %prec ':'",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object n = host.MakeMusic("NoteEvent", location);
                host.SetMusicProperty(n, "drum-type", values[0]);

                if (values[1] is Pair)
                {
                    host.SetMusicProperty(
                        n,
                        "articulations",
                        ParserActionHelpers.ReverseInPlace(values[1], Nil.Instance));
                }

                return n;
            });

        // chord_body_element: music_function_chord_body {
        //     Music *m = unsmob<Music> ($1);
        //     if (m && !m->is_mus_type ("post-event")) {
        //         while (m && m->is_mus_type ("music-wrapper-music")) {
        //             $$ = get_property (m, "element");
        //             m = unsmob<Music> ($$); }
        //         if (!(m && m->is_mus_type ("rhythmic-event"))) {
        //             parser->parser_error (@$, _ ("not a rhythmic event"));
        //             $$ = SCM_UNSPECIFIED; } } }
        //
        // A music function used inside <...> must produce something that can BE a
        // chord note. A post event passes untouched (it attaches instead); anything
        // else is UNWRAPPED layer by layer — and the unwrapping REPLACES $$, so a
        // \tweak'd note contributes the note, not the wrapper.
        table.Add(
            "chord_body_element: music_function_chord_body",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object m = values[0];
                object result = values[0];

                if (host.IsMusic(m) && !host.IsMusicType(m, "post-event"))
                {
                    while (host.IsMusic(m) && host.IsMusicType(m, "music-wrapper-music"))
                    {
                        result = host.GetMusicProperty(m, "element");
                        m = result;
                    }

                    if (!(host.IsMusic(m) && host.IsMusicType(m, "rhythmic-event")))
                    {
                        ParserActionHelpers.ParserError(context, location, "not a rhythmic event");
                        return Unspecified.Instance;
                    }
                }

                return result;
            });

        // ------ event_function_event (parser.yy 3231-3236) ------

        // event_function_event: EVENT_FUNCTION function_arglist {
        //     $$ = MAKE_SYNTAX (music_function, @$, $1, $2); }
        //
        // The same constructor the ArglistCommon group's music_function_call uses; the token differs
        // because the lexer knows the identifier makes an EVENT.
        table.Add(
            "event_function_event: EVENT_FUNCTION function_arglist",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context).MakeSyntax(
                    "music-function", location, values[0], values[1]));
    }

    // The chord-NAME syntax: c:maj7, c:5.9-, c/g.
    private static void RegisterChordNames(RuleActionTable table)
    {
        // ------ new_chord (parser.yy 3852-3863) ------

        // Can return a single pitch rather than a list.
        //
        // new_chord: steno_tonic_pitch maybe_notemode_duration {
        //     if (SCM_UNBNDP ($2)) $$ = $1;
        //     else $$ = make_chord_elements (@$, $1, $2, SCM_EOL); }
        //
        // A bare root with no duration stays a PITCH — the PitchesAndDurations group's
        // `pitch_or_music: new_chord post_events` is what turns it into elements, and
        // only when it has to.
        table.Add(
            "new_chord: steno_tonic_pitch maybe_notemode_duration",
            (context, values, locations, location)
                => values[1] is DefaultArgument
                    ? values[0]
                    : ParserActionHelpers.MakeChordElements(
                        ParserActionHelpers.RequireHost(context),
                        location,
                        values[0],
                        values[1],
                        Nil.Instance));

        // new_chord: steno_tonic_pitch optional_notemode_duration chord_separator
        //            chord_items {
        //     SCM its = scm_reverse_x ($4, SCM_EOL);
        //     $$ = make_chord_elements (@$, $1, $2, scm_cons ($3, its)); } %prec ':'
        //
        // The separator leads the modification list — construct-chord-elements reads
        // it first to learn what KIND of chord this is.
        table.Add(
            "new_chord: steno_tonic_pitch optional_notemode_duration chord_separator"
            + " chord_items %prec ':'",
            (context, values, locations, location)
                => ParserActionHelpers.MakeChordElements(
                    ParserActionHelpers.RequireHost(context),
                    location,
                    values[0],
                    values[1],
                    new Pair(
                        values[2],
                        ParserActionHelpers.ReverseInPlace(values[3], Nil.Instance))));

        // ------ chord_items (parser.yy 3865-3872) ------

        // chord_items: /**/ { $$ = SCM_EOL; }
        table.Add(
            "chord_items: /* empty */",
            (context, values, locations, location) => Nil.Instance);

        // chord_items: chord_items chord_item { $$ = scm_cons ($2, $$); }
        //
        // $$ rather than $1 upstream — the same value, since Bison pre-sets $$ to $1.
        table.Add(
            "chord_items: chord_items chord_item",
            (context, values, locations, location) => new Pair(values[1], values[0]));

        // ------ chord_separator (parser.yy 3874-3887) ------

        // chord_separator: CHORD_COLON { $$ = ly_symbol2scm ("chord-colon"); }
        table.Add(
            "chord_separator: CHORD_COLON",
            (context, values, locations, location) => Symbol.Intern("chord-colon"));

        // chord_separator: CHORD_CARET { $$ = ly_symbol2scm ("chord-caret"); }
        table.Add(
            "chord_separator: CHORD_CARET",
            (context, values, locations, location) => Symbol.Intern("chord-caret"));

        // chord_separator: CHORD_SLASH steno_tonic_pitch {
        //     $$ = ly_list (ly_symbol2scm ("chord-slash"), $2); }
        //
        // These two carry a pitch, so they are LISTS where the first two are bare
        // symbols — construct-chord-elements dispatches on the head either way.
        table.Add(
            "chord_separator: CHORD_SLASH steno_tonic_pitch",
            (context, values, locations, location)
                => Pair.List(Symbol.Intern("chord-slash"), values[1]));

        // chord_separator: CHORD_BASS steno_tonic_pitch {
        //     $$ = ly_list (ly_symbol2scm ("chord-bass"), $2); }
        table.Add(
            "chord_separator: CHORD_BASS steno_tonic_pitch",
            (context, values, locations, location)
                => Pair.List(Symbol.Intern("chord-bass"), values[1]));

        // ------ chord_item (parser.yy 3889-3899) ------

        // chord_item: chord_separator { $$ = $1; }
        table.Add(
            "chord_item: chord_separator",
            (context, values, locations, location) => values[0]);

        // chord_item: step_numbers { $$ = scm_reverse_x ($1, SCM_EOL); }
        //
        // step_numbers accumulated in reverse; this is where the dotted group
        // `5.9.11` is put back into written order.
        table.Add(
            "chord_item: step_numbers",
            (context, values, locations, location)
                => ParserActionHelpers.ReverseInPlace(values[0], Nil.Instance));

        // chord_item: CHORD_MODIFIER { $$ = $1; }
        table.Add(
            "chord_item: CHORD_MODIFIER",
            (context, values, locations, location) => values[0]);

        // ------ step_numbers (parser.yy 3901-3906) ------

        // step_numbers: step_number { $$ = scm_cons ($1, SCM_EOL); }
        table.Add(
            "step_numbers: step_number",
            (context, values, locations, location) => new Pair(values[0], Nil.Instance));

        // step_numbers: step_numbers '.' step_number { $$ = scm_cons ($3, $$); }
        table.Add(
            "step_numbers: step_numbers '.' step_number",
            (context, values, locations, location) => new Pair(values[2], values[0]));

        // ------ step_number (parser.yy 3908-3918) ------

        // step_number: UNSIGNED { $$ = make_chord_step ($1, 0); }
        table.Add(
            "step_number: UNSIGNED",
            (context, values, locations, location)
                => ParserActionHelpers.MakeChordStep(values[0], Rational.Zero));

        // step_number: UNSIGNED '+' { $$ = make_chord_step ($1, SHARP_ALTERATION); }
        table.Add(
            "step_number: UNSIGNED '+'",
            (context, values, locations, location)
                => ParserActionHelpers.MakeChordStep(values[0], new Rational(1, 2)));

        // step_number: UNSIGNED CHORD_MINUS { $$ = make_chord_step ($1, FLAT_ALTERATION); }
        table.Add(
            "step_number: UNSIGNED CHORD_MINUS",
            (context, values, locations, location)
                => ParserActionHelpers.MakeChordStep(values[0], new Rational(-1, 2)));
    }
}
