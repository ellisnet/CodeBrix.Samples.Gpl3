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

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 3356-3436, 3503-3601, 3712-3821);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <content>
/// RULE ACTION GROUP 16 — pitches, octaves and durations: <c>quotes</c> and its
/// <c>sup_quotes</c>/<c>sub_quotes</c> halves, <c>octave_check</c>,
/// <c>erroneous_quotes</c>, <c>steno_pitch</c>, <c>steno_tonic_pitch</c>,
/// <c>pitch</c>, <c>dots</c>, <c>steno_duration</c>, <c>duration</c>,
/// <c>multipliers</c>, <c>maybe_notemode_duration</c>,
/// <c>optional_notemode_duration</c>, <c>tremolo_type</c>, <c>optional_rest</c>,
/// <c>pitch_or_music</c> and <c>simple_element</c>. These are the most-exercised rules
/// in the whole regression suite: every written note goes through
/// <c>pitch_or_music</c>, and every written rhythm through <c>duration</c>.
/// <para>
/// <c>quotes: sub_quotes</c>/<c>sup_quotes</c>, <c>pitch: steno_pitch</c>,
/// <c>pitch_or_tonic_pitch</c> and <c>multiplier_scm</c> are pass-throughs upstream
/// leaves actionless, so they need nothing here.
/// </para>
/// <para>
/// The duration bodies REUSE <c>ParserActionHelpers.MakeDuration</c> and
/// <c>MakeChordElements</c>, which RAG6 ported whole with their defaulted C++
/// parameters made explicit — this group is the caller those defaults were recorded
/// for.
/// </para>
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterRag16(RuleActionTable table)
    {
        // ------ octave_check (parser.yy 3356-3359) ------

        // octave_check: /**/ { $$ = SCM_EOL; }
        //
        // SCM_EOL, not SCM_INUM0: the consumers test with scm_is_number, so "no
        // check at all" has to be distinguishable from "= " with no quotes, which
        // IS the number zero and means the unquoted octave.
        table.Add(
            "octave_check: /* empty */",
            (context, values, locations, location) => Nil.Instance);

        // octave_check: '=' quotes { $$ = $2; }
        table.Add(
            "octave_check: '=' quotes",
            (context, values, locations, location) => values[1]);

        // ------ quotes (parser.yy 3361-3368) ------

        // quotes: /* empty */ { $$ = SCM_INUM0; } %prec ':'
        table.Add(
            "quotes: /* empty */ %prec ':'",
            (context, values, locations, location) => 0L);

        // ------ erroneous_quotes (parser.yy 3370-3376) ------

        // erroneous_quotes: quotes {
        //     if (scm_is_eq (SCM_INUM0, $1))
        //         $$ = SCM_UNDEFINED; }
        //
        // "no quotes, no error: pass *undefined* in that case" — pitch_or_music tests
        // this slot with SCM_UNBNDP to decide whether to complain about octave marks
        // in the wrong place, so zero quotes must not read as "some quotes".
        table.Add(
            "erroneous_quotes: quotes",
            (context, values, locations, location)
                => ParserActionHelpers.IsExactZero(values[0])
                    ? DefaultArgument.Instance
                    : values[0]);

        // ------ sup_quotes / sub_quotes (parser.yy 3378-3394) ------

        // sup_quotes: '\'' { $$ = to_scm (1); }
        table.Add(
            "sup_quotes: '\\''",
            (context, values, locations, location) => 1L);

        // sup_quotes: sup_quotes '\'' { $$ = scm_oneplus ($1); }
        table.Add(
            "sup_quotes: sup_quotes '\\''",
            (context, values, locations, location) => SchemeNumber.Add(values[0], 1L));

        // sub_quotes: ',' { $$ = to_scm (-1); }
        table.Add(
            "sub_quotes: ','",
            (context, values, locations, location) => -1L);

        // sub_quotes: sub_quotes ',' { $$ = scm_oneminus ($1); }
        table.Add(
            "sub_quotes: sub_quotes ','",
            (context, values, locations, location) => SchemeNumber.Subtract(values[0], 1L));

        // ------ steno_pitch / steno_tonic_pitch / pitch (parser.yy 3396-3432) ------
        //
        // Three identical bodies — upstream's own comment between the first two is
        // "ugh. duplication", and the third repeats it once more. They are written out
        // three times here too rather than folded into a helper: each is a literal
        // translation of its own body, and a shared helper would be the port
        // "improving" a duplication a re-sync has to be able to diff.

        // steno_pitch: NOTENAME_PITCH quotes {
        //     if (!scm_is_eq (SCM_INUM0, $2)) {
        //         Pitch p = *unsmob<Pitch> ($1);
        //         p = p.transposed (Pitch (from_scm<int> ($2), 0));
        //         $$ = p.smobbed_copy (); } }
        table.Add(
            "steno_pitch: NOTENAME_PITCH quotes",
            (context, values, locations, location) =>
            {
                if (ParserActionHelpers.IsExactZero(values[1]))
                {
                    return values[0];
                }

                Pitch p = (Pitch)values[0];
                return p.Transposed(
                    new Pitch(
                        SchemeConvert.ToInt(values[1], "steno_pitch"), 0, Rational.Zero));
            });

        // steno_tonic_pitch: TONICNAME_PITCH quotes { ...the same body... }
        table.Add(
            "steno_tonic_pitch: TONICNAME_PITCH quotes",
            (context, values, locations, location) =>
            {
                if (ParserActionHelpers.IsExactZero(values[1]))
                {
                    return values[0];
                }

                Pitch p = (Pitch)values[0];
                return p.Transposed(
                    new Pitch(
                        SchemeConvert.ToInt(values[1], "steno_tonic_pitch"), 0, Rational.Zero));
            });

        // pitch: PITCH_IDENTIFIER quotes { ...and once more... }
        table.Add(
            "pitch: PITCH_IDENTIFIER quotes",
            (context, values, locations, location) =>
            {
                if (ParserActionHelpers.IsExactZero(values[1]))
                {
                    return values[0];
                }

                Pitch p = (Pitch)values[0];
                return p.Transposed(
                    new Pitch(SchemeConvert.ToInt(values[1], "pitch"), 0, Rational.Zero));
            });

        // ------ maybe_notemode_duration (parser.yy 3503-3511) ------

        // maybe_notemode_duration: { $$ = SCM_UNDEFINED; } %prec ':'
        table.Add(
            "maybe_notemode_duration: /* empty */ %prec ':'",
            (context, values, locations, location) => DefaultArgument.Instance);

        // maybe_notemode_duration: duration {
        //     $$ = $1;
        //     parser->default_duration_ = *unsmob<Duration> ($$); }
        //
        // THE STICKY DURATION: writing one sets it for every later note that omits
        // one. Assigned BY VALUE upstream, and Duration is a value type here too.
        table.Add(
            "maybe_notemode_duration: duration",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).DefaultDuration = (Duration)values[0];
                return values[0];
            });

        // ------ optional_notemode_duration (parser.yy 3514-3520) ------

        // optional_notemode_duration: maybe_notemode_duration {
        //     if (SCM_UNBNDP ($$))
        //         $$ = parser->default_duration_.smobbed_copy (); }
        //
        // The read side of the sticky duration. Boxing the host's Duration is the
        // smobbed_copy: a fresh box per read, never the parser's own storage.
        table.Add(
            "optional_notemode_duration: maybe_notemode_duration",
            (context, values, locations, location)
                => values[0] is DefaultArgument
                    ? ParserActionHelpers.RequireHost(context).DefaultDuration
                    : values[0]);

        // ------ steno_duration (parser.yy 3522-3534) ------

        // steno_duration: UNSIGNED dots {
        //     $$ = make_duration ($1, from_scm<int> ($2));
        //     if (SCM_UNBNDP ($$)) {
        //         parser->parser_error (@1, _ ("not a duration"));
        //         $$ = Duration ().smobbed_copy (); } }
        //
        // make_duration answers SCM_UNDEFINED for anything that is not a positive
        // power of two, so `c3` is where this error comes from.
        table.Add(
            "steno_duration: UNSIGNED dots",
            (context, values, locations, location) =>
            {
                object made = ParserActionHelpers.MakeDuration(
                    values[0],
                    SchemeConvert.ToInt(values[1], "steno_duration"),
                    DefaultArgument.Instance);

                if (made is DefaultArgument)
                {
                    ParserActionHelpers.ParserError(context, locations[0], "not a duration");

                    // Duration () — log 0, no dots, factor 1.
                    return new Duration(0, 0);
                }

                return made;
            });

        // steno_duration: DURATION_IDENTIFIER dots {
        //     $$ = make_duration ($1, from_scm<int> ($2)); }
        //
        // No error branch: the identifier already carries a Duration, so
        // make_duration's power-of-two path cannot be reached.
        table.Add(
            "steno_duration: DURATION_IDENTIFIER dots",
            (context, values, locations, location)
                => ParserActionHelpers.MakeDuration(
                    values[0],
                    SchemeConvert.ToInt(values[1], "steno_duration"),
                    DefaultArgument.Instance));

        // ------ duration (parser.yy 3536-3540) ------

        // duration: steno_duration multipliers {
        //     $$ = make_duration ($1, 0, $2); }
        table.Add(
            "duration: steno_duration multipliers",
            (context, values, locations, location)
                => ParserActionHelpers.MakeDuration(values[0], 0, values[1]));

        // ------ dots (parser.yy 3542-3549) ------

        // dots: /* empty */ { $$ = SCM_INUM0; }
        table.Add(
            "dots: /* empty */",
            (context, values, locations, location) => 0L);

        // dots: dots '.' { $$ = scm_oneplus ($1); }
        table.Add(
            "dots: dots '.'",
            (context, values, locations, location) => SchemeNumber.Add(values[0], 1L));

        // ------ multipliers (parser.yy 3556-3586) ------
        //
        // Accumulated left to right, and SCM_UNDEFINED until the first one arrives —
        // which is what lets `duration` pass "no multiplier at all" to make_duration
        // as its factor argument.

        // multipliers: /* empty */ { $$ = SCM_UNDEFINED; }
        table.Add(
            "multipliers: /* empty */",
            (context, values, locations, location) => DefaultArgument.Instance);

        // multipliers: multipliers '*' UNSIGNED {
        //     if (!SCM_UNBNDP ($1)) $$ = scm_product ($1, $3);
        //     else $$ = $3; }
        table.Add(
            "multipliers: multipliers '*' UNSIGNED",
            (context, values, locations, location)
                => values[0] is DefaultArgument
                    ? values[2]
                    : SchemeNumber.Multiply(values[0], values[2]));

        // multipliers: multipliers '*' FRACTION {
        //     if (!SCM_UNBNDP ($1))
        //         $$ = scm_product ($1, scm_divide (scm_car ($3), scm_cdr ($3)));
        //     else
        //         $$ = scm_divide (scm_car ($3), scm_cdr ($3)); }
        //
        // The FRACTION token's value is the (numerator . denominator) PAIR
        // scan_fraction conses; scm_divide of two exacts stays exact on the number
        // tower, so `c4*2/3` scales by exactly 2/3.
        table.Add(
            "multipliers: multipliers '*' FRACTION",
            (context, values, locations, location) =>
            {
                Pair fraction = (Pair)values[2];
                object factor = SchemeNumber.Divide(fraction.Car, fraction.Cdr);
                return values[0] is DefaultArgument
                    ? factor
                    : SchemeNumber.Multiply(values[0], factor);
            });

        // multipliers: multipliers '*' multiplier_scm {
        //     if (scm_is_false (Lily::scale_p ($3)))
        //         parser->parser_error (@3, _ ("not a multiplier"));
        //     else if (SCM_UNBNDP ($1))
        //         $$ = Lily::scale_to_factor ($3);
        //     else
        //         $$ = scm_product ($1, Lily::scale_to_factor ($3)); }
        //
        // The error branch assigns nothing, so the accumulated value rides the
        // implicit $$ = $1 and the bad factor is simply dropped.
        table.Add(
            "multipliers: multipliers '*' multiplier_scm",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (!host.IsScale(values[2]))
                {
                    ParserActionHelpers.ParserError(context, locations[2], "not a multiplier");
                    return values[0];
                }

                object factor = host.ScaleToFactor(values[2]);
                return values[0] is DefaultArgument
                    ? factor
                    : SchemeNumber.Multiply(values[0], factor);
            });

        // ------ tremolo_type (parser.yy 3588-3601) ------

        // tremolo_type: ':' { $$ = to_scm (parser->default_tremolo_type_); }
        table.Add(
            "tremolo_type: ':'",
            (context, values, locations, location)
                => (long)ParserActionHelpers.RequireHost(context).DefaultTremoloType);

        // tremolo_type: ':' UNSIGNED {
        //     if (SCM_UNBNDP (make_duration ($2))) {
        //         parser->parser_error (@2, _ ("not a duration"));
        //         $$ = to_scm (parser->default_tremolo_type_);
        //     } else {
        //         $$ = $2;
        //         parser->default_tremolo_type_ = from_scm<int> ($2); } }
        //
        // make_duration is called only as a VALIDITY TEST — its result is discarded
        // and the written number is what travels on, which is why `c4:6` is refused
        // while `c4:16` is remembered.
        table.Add(
            "tremolo_type: ':' UNSIGNED",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (ParserActionHelpers.MakeDuration(values[1], 0, DefaultArgument.Instance)
                    is DefaultArgument)
                {
                    ParserActionHelpers.ParserError(context, locations[1], "not a duration");
                    return (long)host.DefaultTremoloType;
                }

                host.DefaultTremoloType = SchemeConvert.ToInt(values[1], "tremolo_type");
                return values[1];
            });

        // ------ optional_rest (parser.yy 3712-3715) ------

        // optional_rest: /**/ { $$ = SCM_BOOL_F; }
        table.Add(
            "optional_rest: /* empty */",
            (context, values, locations, location) => false);

        // optional_rest: REST { $$ = SCM_BOOL_T; }
        table.Add(
            "optional_rest: REST",
            (context, values, locations, location) => true);

        RegisterRag16PitchOrMusic(table);
    }

    // pitch_or_music and simple_element, split out only because the first of them is
    // the longest action body in the grammar.
    private static void RegisterRag16PitchOrMusic(RuleActionTable table)
    {
        // ------ pitch_or_music (parser.yy 3717-3798) ------

        // The erroneous_quotes element is for input such as a1'' which is a
        // typical note entry error that we don't want the parser to get
        // confused about.  The resulting grammar, however, is inconsistent
        // enough that accepting it is not doing anybody a favor.
        //
        // pitch exclamations questions octave_check maybe_notemode_duration
        //     erroneous_quotes optional_rest post_events
        //
        // $$ is pre-set to $1, so a plain `c` with nothing attached leaves this
        // action as a bare PITCH — which is what lets pitch_or_music serve both as
        // "a note" and as "a pitch" (identifier_init, chord roots).
        table.Add(
            "pitch_or_music: pitch exclamations questions octave_check"
            + " maybe_notemode_duration erroneous_quotes optional_rest post_events %prec ':'",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object pitch = values[0];
                object check = values[3];

                if (!host.IsNoteState)
                {
                    ParserActionHelpers.ParserError(
                        context, locations[0], "have to be in Note mode for notes");
                }

                if (!(values[5] is DefaultArgument))
                {
                    // It's possible to get here without a duration, like when there
                    // is no octave_check but a question mark.  But we point out the
                    // most frequent error of an interspersed duration specifically.
                    ParserActionHelpers.ParserError(
                        context,
                        locations[5],
                        values[4] is DefaultArgument
                            ? "badly placed octave marks"
                            : "octave marks must precede duration");

                    // Try sorting the quotes to where they likely belong
                    if (SchemeNumber.IsNumber(check))
                    {
                        check = SchemeNumber.Add(check, values[5]);
                    }
                    else
                    {
                        pitch = ((Pitch)pitch).Transposed(
                            new Pitch(
                                SchemeConvert.ToInt(values[5], "pitch_or_music"),
                                0,
                                Rational.Zero));
                    }
                }

                // Anything written after the pitch makes this a note; a naked pitch
                // stays a pitch.
                if (!(values[1] is DefaultArgument)
                    || !(values[2] is DefaultArgument)
                    || SchemeNumber.IsNumber(check)
                    || !(values[4] is DefaultArgument)
                    || ParserActionHelpers.IsSchemeTrue(values[6])
                    || values[7] is Pair)
                {
                    object n = host.MakeMusic(
                        ParserActionHelpers.IsSchemeTrue(values[6]) ? "RestEvent" : "NoteEvent",
                        location);

                    host.SetMusicProperty(n, "pitch", pitch);
                    host.SetMusicProperty(
                        n,
                        "duration",
                        values[4] is DefaultArgument ? host.DefaultDuration : values[4]);

                    if (SchemeNumber.IsNumber(check))
                    {
                        int q = SchemeConvert.ToInt(check, "pitch_or_music");
                        host.SetMusicProperty(n, "absolute-octave", (long)(q - 1));
                    }

                    // from_scm<bool> is scm_is_eq (s, SCM_BOOL_T) — exactly #t, so
                    // the SCM_UNDEFINED "no marks" value reads as false rather than
                    // as Scheme truthiness.
                    bool exclamation = values[1] is bool wrote && wrote;
                    bool question = values[2] is bool asked && asked;

                    if (question)
                    {
                        host.SetMusicProperty(n, "cautionary", true);
                    }

                    if (exclamation || question)
                    {
                        host.SetMusicProperty(n, "force-accidental", true);
                    }

                    if (values[7] is Pair)
                    {
                        host.SetMusicProperty(
                            n,
                            "articulations",
                            ParserActionHelpers.ReverseInPlace(values[7], Nil.Instance));
                    }

                    return n;
                }

                return pitch;
            });

        // pitch_or_music: new_chord post_events {
        //     if (!parser->lexer_->is_chord_state ())
        //         parser->parser_error (@1, _ ("have to be in Chord mode for chords"));
        //     if (scm_is_pair ($2)) {
        //         if (unsmob<Pitch> ($1))
        //             $1 = make_chord_elements (@1, $1,
        //                       parser->default_duration_.smobbed_copy (), SCM_EOL);
        //         SCM elts = ly_append ($1, scm_reverse_x ($2, SCM_EOL));
        //         $$ = MAKE_SYNTAX (event_chord, @1, elts);
        //     } else if (!unsmob<Pitch> ($1))
        //         $$ = MAKE_SYNTAX (event_chord, @1, $1);
        //     // A mere pitch drops through. } %prec ':'
        //
        // new_chord answers EITHER a single pitch (a bare chord root) or the list of
        // elements make_chord_elements built, and both branches turn on that.
        table.Add(
            "pitch_or_music: new_chord post_events %prec ':'",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object chord = values[0];

                if (!host.IsChordState)
                {
                    ParserActionHelpers.ParserError(
                        context, locations[0], "have to be in Chord mode for chords");
                }

                if (values[1] is Pair)
                {
                    if (chord is Pitch)
                    {
                        chord = ParserActionHelpers.MakeChordElements(
                            host, locations[0], chord, host.DefaultDuration, Nil.Instance);
                    }

                    object elements = ParserActionHelpers.Append(
                        chord, ParserActionHelpers.ReverseInPlace(values[1], Nil.Instance));
                    return host.MakeSyntax("event-chord", locations[0], elements);
                }

                if (!(chord is Pitch))
                {
                    return host.MakeSyntax("event-chord", locations[0], chord);
                }

                // A mere pitch drops through.
                return chord;
            });

        // ------ simple_element (parser.yy 3800-3821) ------

        // simple_element: DRUM_PITCH optional_notemode_duration {
        //     Music *n = MY_MAKE_MUSIC ("NoteEvent", @$);
        //     set_property (n, "duration", $2);
        //     set_property (n, "drum-type", $1);
        //     $$ = n->unprotect (); }
        table.Add(
            "simple_element: DRUM_PITCH optional_notemode_duration",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object n = host.MakeMusic("NoteEvent", location);
                host.SetMusicProperty(n, "duration", values[1]);
                host.SetMusicProperty(n, "drum-type", values[0]);
                return n;
            });

        // simple_element: RESTNAME optional_notemode_duration {
        //     Music *ev = 0;
        //     if (from_scm<std::string> ($1) == "s") {
        //         /* Space */
        //         ev = MY_MAKE_MUSIC ("SkipEvent", @$);
        //     } else {
        //         ev = MY_MAKE_MUSIC ("RestEvent", @$);
        //     }
        //     set_property (ev, "duration", $2);
        //     $$ = ev->unprotect (); }
        //
        // RESTNAME is the [rs] lexer class, so the test is exactly "s or r".
        table.Add(
            "simple_element: RESTNAME optional_notemode_duration",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object ev = host.MakeMusic(
                    ParserActionHelpers.SchemeStringText(values[0]) == "s"
                        ? "SkipEvent" /* Space */
                        : "RestEvent",
                    location);

                host.SetMusicProperty(ev, "duration", values[1]);
                return ev;
            });
    }
}
