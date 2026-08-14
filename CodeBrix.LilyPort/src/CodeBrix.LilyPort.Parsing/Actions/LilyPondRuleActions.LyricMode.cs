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

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 2665-2760, 3823-3849);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <content>
/// Mode changes and lyric mode: <c>optional_id</c>,
/// <c>lyric_mode_music</c> (with its mid-rule <c>$@10</c>),
/// <c>mode_changed_music</c>, the <c>mode_changing_head</c> keyword family
/// (<c>\notemode</c>, <c>\drummode</c>, <c>\figuremode</c>, <c>\chordmode</c>,
/// <c>\lyricmode</c>), the <c>mode_changing_head_with_context</c> family
/// (<c>\drums</c>, <c>\figures</c>, <c>\chords</c>, <c>\lyrics</c>),
/// <c>lyric_element</c> and <c>lyric_element_music</c>. This is the group where
/// the PARSER drives the LEXER's mode switching — every head pushes a lexer state
/// and every <c>mode_changed_music</c>/<c>lyric_mode_music</c> reduction pops it.
/// <para>
/// LOOKAHEAD CAVEAT (wave-1 finding, examined for this group): upstream reaches
/// the head reductions WITHOUT a lookahead read (each is a default-reduction
/// state), so the token after the mode keyword is lexed in the NEW mode; the
/// port's driver has already lexed it in the OLD mode. The pop sites mirror it:
/// the token after the closing brace is lexed in the STILL-PUSHED mode here, in
/// the restored mode upstream. Only <c>$@10</c> matches upstream exactly — its
/// state must also shift <c>MUSIC_IDENTIFIER</c>, so upstream reads the lookahead
/// too (the grammar's own "We must not have lookahead tokens parsed in lyric
/// mode" comment). No action-level fix exists — <c>PushBackLookahead</c>
/// re-delivers the already-formed token without re-lexing — so the divergence is
/// recorded in PORT-COVERAGE for the driver session to close.
/// </para>
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterLyricMode(RuleActionTable table)
    {
        // ------ optional_id (parser.yy 2665-2670) ------

        // optional_id: /**/ { $$ = SCM_EOL; }
        table.Add(
            "optional_id: /* empty */",
            (context, values, locations, location) => Nil.Instance);

        // optional_id: '=' simple_string { $$ = $2; }
        table.Add(
            "optional_id: '=' simple_string",
            (context, values, locations, location) => values[1]);

        // ------ lyric_mode_music (parser.yy 2672-2687) ------

        // We must not have lookahead tokens parsed in lyric mode.  In order
        // to save confusion, we take almost the same set as permitted with
        // \lyricmode and/or \lyrics.  However, music identifiers are also
        // allowed, and they obviously do not require switching into lyrics
        // mode for parsing.

        // The mid-rule action of `lyric_mode_music: { ... } grouped_music_list`:
        // { parser->lexer_->push_lyric_state (); } — run BEFORE the music is
        // parsed. Upstream also reads the lookahead before this reduction (the
        // state can shift MUSIC_IDENTIFIER instead), so the port's eager driver
        // matches upstream here exactly.
        table.Add(
            "$@10: /* empty */",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushLyricState();
                return Unspecified.Instance;
            });

        // lyric_mode_music: { push } grouped_music_list {
        //     parser->lexer_->pop_state (); $$ = $2; }
        //
        // (The other alternative, `lyric_mode_music: MUSIC_IDENTIFIER`, has no
        // action upstream and reduces by the $$ = $1 default.)
        table.Add(
            "lyric_mode_music: $@10 grouped_music_list",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PopLexerState();
                return values[1];
            });

        // ------ mode_changed_music (parser.yy 2689-2709) ------

        // mode_changed_music: mode_changing_head grouped_music_list {
        //     if (scm_is_eq ($1, ly_symbol2scm ("chords")))
        //         $$ = MAKE_SYNTAX (unrelativable_music, @$, $2);
        //     else
        //         $$ = $2;
        //     parser->lexer_->pop_state (); }
        table.Add(
            "mode_changed_music: mode_changing_head grouped_music_list",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object result;
                if (ReferenceEquals(values[0], Symbol.Intern("chords")))
                {
                    result = host.MakeSyntax("unrelativable-music", location, values[1]);
                }
                else
                {
                    result = values[1];
                }

                host.PopLexerState();
                return result;
            });

        // mode_changed_music: mode_changing_head_with_context optional_context_mods grouped_music_list {
        //     $$ = MAKE_SYNTAX (context_create, @$, $1, SCM_EOL, $2, $3);
        //     if (scm_is_eq ($1, ly_symbol2scm ("ChordNames")))
        //         $$ = MAKE_SYNTAX (unrelativable_music, @$, $$);
        //     parser->lexer_->pop_state (); }
        table.Add(
            "mode_changed_music: mode_changing_head_with_context optional_context_mods grouped_music_list",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object result = host.MakeSyntax(
                    "context-create", location, values[0], Nil.Instance, values[1], values[2]);
                if (ReferenceEquals(values[0], Symbol.Intern("ChordNames")))
                {
                    result = host.MakeSyntax("unrelativable-music", location, result);
                }

                host.PopLexerState();
                return result;
            });

        // ------ mode_changing_head (parser.yy 2711-2738) ------

        // mode_changing_head: NOTEMODE {
        //     parser->lexer_->push_note_state ();
        //     $$ = ly_symbol2scm ("notes"); }
        table.Add(
            "mode_changing_head: NOTEMODE",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushNoteState();
                return Symbol.Intern("notes");
            });

        // mode_changing_head: DRUMMODE {
        //     parser->lexer_->push_drum_state ();
        //     $$ = ly_symbol2scm ("drums"); }
        table.Add(
            "mode_changing_head: DRUMMODE",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushDrumState();
                return Symbol.Intern("drums");
            });

        // mode_changing_head: FIGUREMODE {
        //     parser->lexer_->push_figuredbass_state ();
        //     $$ = ly_symbol2scm ("figures"); }
        table.Add(
            "mode_changing_head: FIGUREMODE",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushFiguredBassState();
                return Symbol.Intern("figures");
            });

        // mode_changing_head: CHORDMODE {
        //     SCM mods = parser->lexer_->lookup_identifier_symbol (ly_symbol2scm ("chordmodifiers"));
        //     parser->lexer_->chordmodifier_tab_ = Hash_table::alist_to_hashq_table (mods);
        //     parser->lexer_->push_chord_state ();
        //     $$ = ly_symbol2scm ("chords"); }
        table.Add(
            "mode_changing_head: CHORDMODE",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object mods = host.LookupIdentifier("chordmodifiers");
                host.SetChordModifiers(mods);
                host.PushChordState();
                return Symbol.Intern("chords");
            });

        // mode_changing_head: LYRICMODE {
        //     parser->lexer_->push_lyric_state ();
        //     $$ = ly_symbol2scm ("lyrics"); }
        table.Add(
            "mode_changing_head: LYRICMODE",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushLyricState();
                return Symbol.Intern("lyrics");
            });

        // ------ mode_changing_head_with_context (parser.yy 2740-2760) ------

        // mode_changing_head_with_context: DRUMS {
        //     parser->lexer_->push_drum_state();
        //     $$ = ly_symbol2scm ("DrumStaff"); }
        table.Add(
            "mode_changing_head_with_context: DRUMS",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushDrumState();
                return Symbol.Intern("DrumStaff");
            });

        // mode_changing_head_with_context: FIGURES {
        //     parser->lexer_->push_figuredbass_state ();
        //     $$ = ly_symbol2scm ("FiguredBass"); }
        table.Add(
            "mode_changing_head_with_context: FIGURES",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushFiguredBassState();
                return Symbol.Intern("FiguredBass");
            });

        // mode_changing_head_with_context: CHORDS {
        //     SCM mods = parser->lexer_->lookup_identifier_symbol (ly_symbol2scm ("chordmodifiers"));
        //     parser->lexer_->chordmodifier_tab_ = Hash_table::alist_to_hashq_table (mods);
        //     parser->lexer_->push_chord_state ();
        //     $$ = ly_symbol2scm ("ChordNames"); }
        table.Add(
            "mode_changing_head_with_context: CHORDS",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object mods = host.LookupIdentifier("chordmodifiers");
                host.SetChordModifiers(mods);
                host.PushChordState();
                return Symbol.Intern("ChordNames");
            });

        // mode_changing_head_with_context: LYRICS {
        //     parser->lexer_->push_lyric_state ();
        //     $$ = ly_symbol2scm ("Lyrics"); }
        table.Add(
            "mode_changing_head_with_context: LYRICS",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushLyricState();
                return Symbol.Intern("Lyrics");
            });

        // ------ lyric_element (parser.yy 3823-3840) ------

        // lyric_element: full_markup {
        //     if (!parser->lexer_->is_lyric_state ())
        //         parser->parser_error (@1, _ ("markup outside of text script or \\lyricmode"));
        //     $$ = $1; }
        table.Add(
            "lyric_element: full_markup",
            (context, values, locations, location) =>
            {
                if (!ParserActionHelpers.RequireHost(context).IsLyricState)
                {
                    ParserActionHelpers.ParserError(
                        context,
                        locations[0],
                        "markup outside of text script or \\lyricmode");
                }

                return values[0];
            });

        // lyric_element: SYMBOL {
        //     if (!parser->lexer_->is_lyric_state ())
        //         parser->parser_error (@1, _f ("not a note name: %s", from_scm<std::string> ($1)));
        //     $$ = $1; }
        table.Add(
            "lyric_element: SYMBOL",
            (context, values, locations, location) =>
            {
                if (!ParserActionHelpers.RequireHost(context).IsLyricState)
                {
                    ParserActionHelpers.ParserError(
                        context,
                        locations[0],
                        "not a note name: " + ParserActionHelpers.SchemeStringText(values[0]));
                }

                return values[0];
            });

        // lyric_element: STRING {
        //     if (!parser->lexer_->is_lyric_state ())
        //         parser->parser_error (@1, _ ("string outside of text script or \\lyricmode"));
        //     $$ = $1; }
        //
        // (The last alternative, `lyric_element: LYRIC_ELEMENT`, has no action
        // upstream and reduces by the $$ = $1 default.)
        table.Add(
            "lyric_element: STRING",
            (context, values, locations, location) =>
            {
                if (!ParserActionHelpers.RequireHost(context).IsLyricState)
                {
                    ParserActionHelpers.ParserError(
                        context,
                        locations[0],
                        "string outside of text script or \\lyricmode");
                }

                return values[0];
            });

        // ------ lyric_element_music (parser.yy 3842-3849) ------

        // lyric_element_music: lyric_element optional_notemode_duration post_events {
        //     $$ = MAKE_SYNTAX (lyric_event, @$, $1, $2);
        //     if (scm_is_pair ($3))
        //         set_property
        //             (unsmob<Music> ($$), "articulations", scm_reverse_x ($3, SCM_EOL));
        // } %prec ':'
        //
        // unsmob<Music> is the direct Engine cast (the TopLevel group/the ContextDefinitions group convention): the
        // lyric-event constructor always makes music, and a host whose MakeSyntax
        // answered something else fails here the way upstream's null unsmob would.
        table.Add(
            "lyric_element_music: lyric_element optional_notemode_duration post_events %prec ':'",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object result = host.MakeSyntax("lyric-event", location, values[0], values[1]);
                if (values[2] is Pair)
                {
                    ((MusicObject)result).SetProperty(
                        "articulations",
                        ParserActionHelpers.ReverseInPlace(values[2], Nil.Instance));
                }

                return result;
            });
    }
}
