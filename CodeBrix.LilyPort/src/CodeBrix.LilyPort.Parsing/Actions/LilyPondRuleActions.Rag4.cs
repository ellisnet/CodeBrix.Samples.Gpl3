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

using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 1326-1466, 3920-3927);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <content>
/// RULE ACTION GROUP 4 — output definitions, paper and tempo: <c>paper_block</c>,
/// <c>output_def</c>, <c>output_def_head</c>,
/// <c>output_def_head_with_mode_switch</c>, <c>output_def_body</c> with its
/// mid-rule <c>$@8</c>, and the <c>tempo_event</c>/<c>tempo_range</c> pair.
/// (<c>music_or_context_def</c> is pass-through and carries no actions.) These are
/// the rules that build the Engine's <see cref="OutputDef"/> — what a
/// <c>\paper</c>, <c>\layout</c> or <c>\midi</c> block becomes — through the
/// <c>get_paper</c>/<c>get_midi</c>/<c>get_layout</c> helpers
/// (ParserActionHelpers.Rag4.cs), and they lean on Lily_lexer state more than any
/// group so far: the head pushes the INITIAL lexer mode and the definition's own
/// scope, the body swaps both around an active output-definition value, and the
/// closing brace pops both.
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterRag4(RuleActionTable table)
    {
        // ------ paper_block (parser.yy 1326-1337) ------

        // paper_block: output_def {
        //     Output_def *od = unsmob<Output_def> ($1);
        //     if (!scm_is_eq (od->lookup_variable (ly_symbol2scm ("output-def-kind")),
        //             ly_symbol2scm ("paper")))
        //     {
        //         parser->parser_error (@1, _ ("need \\paper for paper block"));
        //         $$ = get_paper (parser)->unprotect ();
        //     } }
        //
        // A \layout or \midi where a paper block belongs is reported and REPLACED by
        // a fresh paper, so the book being built stays consistent.
        table.Add(
            "paper_block: output_def",
            (context, values, locations, location) =>
            {
                OutputDef od = (OutputDef)values[0];
                if (!ReferenceEquals(
                        od.CVariable("output-def-kind"), Symbol.Intern("paper")))
                {
                    ParserActionHelpers.ParserError(
                        context, locations[0], "need \\paper for paper block");
                    return ParserActionHelpers.GetPaper(
                        ParserActionHelpers.RequireHost(context));
                }

                return values[0];
            });

        // ------ output_def (parser.yy 1340-1348) ------

        // output_def: output_def_body '}' {
        //     if (scm_is_pair ($1))
        //         $$ = scm_car ($1);
        //     parser->lexer_->remove_scope ();
        //     parser->lexer_->pop_state (); }
        //
        // The definition rides in the one-element marker list the body's opening
        // alternative wrapped it in until something unwrapped it; the closing brace
        // unwraps whichever is left, then pops the scope and mode the head pushed.
        table.Add(
            "output_def: output_def_body '}'",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object result = values[0];
                if (result is Pair pair)
                {
                    result = pair.Car;
                }

                host.RemoveScope();
                host.PopLexerState();
                return result;
            });

        // ------ output_def_head (parser.yy 1350-1368) ------

        // output_def_head: PAPER {
        //     Output_def *p = get_paper (parser);
        //     p->input_origin_ = @$;
        //     parser->lexer_->add_scope (p->scope_);
        //     $$ = p->unprotect (); }
        //
        // Only the \paper head stamps input_origin_ here; \midi and \layout leave
        // the stamping to the body rule's set_spot.
        table.Add(
            "output_def_head: PAPER",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                OutputDef p = ParserActionHelpers.GetPaper(host);
                p.SetSpot(location);
                host.AddOutputDefScope(p);
                return p;
            });

        // output_def_head: MIDI {
        //     Output_def *p = get_midi (parser);
        //     $$ = p->unprotect ();
        //     parser->lexer_->add_scope (p->scope_); }
        table.Add(
            "output_def_head: MIDI",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                OutputDef p = ParserActionHelpers.GetMidi(host);
                host.AddOutputDefScope(p);
                return p;
            });

        // output_def_head: LAYOUT {
        //     Output_def *p = get_layout (parser);
        //     parser->lexer_->add_scope (p->scope_);
        //     $$ = p->unprotect (); }
        table.Add(
            "output_def_head: LAYOUT",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                OutputDef p = ParserActionHelpers.GetLayout(host);
                host.AddOutputDefScope(p);
                return p;
            });

        // ------ output_def_head_with_mode_switch (parser.yy 1370-1375) ------

        // output_def_head_with_mode_switch: output_def_head {
        //     parser->lexer_->push_initial_state ();
        //     $$ = $1; }
        table.Add(
            "output_def_head_with_mode_switch: output_def_head",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context).PushInitialState();
                return values[0];
            });

        // ------ output_def_body (parser.yy 1387-1453) ------
        //
        // Upstream explains the pass-through music_or_context_def between these
        // alternatives:
        // // We need this weird nonterminal because both music as well as a
        // // context definition can start with \context and the difference is
        // // only apparent after looking at the next token.  If it is '{', there
        // // is still time to escape from notes mode.

        // output_def_body: output_def_head_with_mode_switch '{' {
        //     unsmob<Output_def> ($1)->input_origin_.set_spot (@$);
        //     // This is a stupid trick to mark the beginning of the
        //     // body for deciding whether to allow
        //     // embedded_scm_active to have an output definition
        //     $$ = ly_list ($1); }
        table.Add(
            "output_def_body: output_def_head_with_mode_switch '{'",
            (context, values, locations, location) =>
            {
                ((OutputDef)values[0]).SetSpot(location);
                return new Pair(values[0], Nil.Instance);
            });

        // output_def_body: output_def_body assignment {
        //     if (scm_is_pair ($1))
        //         $$ = scm_car ($1); }
        //
        // The assignment itself already landed through the scope the head pushed
        // (RAG1's assignment action); the body only unwraps the marker list.
        table.Add(
            "output_def_body: output_def_body assignment",
            (context, values, locations, location)
                => values[0] is Pair pair ? pair.Car : values[0]);

        // output_def_body: output_def_body embedded_scm_active {
        //     // We don't switch into note mode for Scheme functions
        //     // here.  Does not seem warranted/required in output
        //     // definitions.
        //     if (scm_is_pair ($1))
        //     {
        //         Output_def *o = unsmob<Output_def> ($2);
        //         if (o) {
        //             o->input_origin_.set_spot (@$);
        //             $1 = o->self_scm ();
        //             parser->lexer_->remove_scope ();
        //             parser->lexer_->add_scope (o->scope_);
        //             $2 = SCM_UNSPECIFIED;
        //         } else
        //             $1 = scm_car ($1);
        //     }
        //     if (unsmob<Context_def> ($2))
        //         assign_context_def (unsmob<Output_def> ($1), $2);
        //     // Seems unlikely, but let's be complete:
        //     else if (unsmob<Music> ($2))
        //     {
        //         SCM proc = parser->lexer_->lookup_identifier ("output-def-music-handler");
        //         ly_call (proc, $1, $2);
        //     } else if (!scm_is_eq ($2, SCM_UNSPECIFIED))
        //         parser->parser_error (@2, _("bad expression type"));
        //     $$ = $1; }
        //
        // While the marker list is still in place — nothing else has landed in the
        // body yet — an ACTIVE output-definition value replaces the whole definition
        // being built, scope and all. That is the "stupid trick" paying off.
        table.Add(
            "output_def_body: output_def_body embedded_scm_active",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object body = values[0];
                object value = values[1];

                if (body is Pair bodyPair)
                {
                    OutputDef o = value as OutputDef;
                    if (o != null)
                    {
                        o.SetSpot(location);
                        body = o;
                        host.RemoveScope();
                        host.AddOutputDefScope(o);
                        value = Unspecified.Instance;
                    }
                    else
                    {
                        body = bodyPair.Car;
                    }
                }

                if (value is ContextDef)
                {
                    ParserActionHelpers.AssignContextDef((OutputDef)body, value);
                }
                else if (value is MusicObject)
                {
                    object proc = host.LookupIdentifier("output-def-music-handler");
                    host.Call(proc, body, value);
                }
                else if (!(value is Unspecified))
                {
                    ParserActionHelpers.ParserError(
                        context, locations[1], "bad expression type");
                }

                return body;
            });

        // output_def_body: output_def_body SCM_TOKEN {
        //     if (scm_is_pair ($1))
        //         $$ = scm_car ($1);
        //     // Evaluate and ignore #xxx, as opposed to \xxx
        //     parser->lexer_->eval_scm_token ($2, @2); }
        table.Add(
            "output_def_body: output_def_body SCM_TOKEN",
            (context, values, locations, location) =>
            {
                object result = values[0] is Pair pair ? pair.Car : values[0];
                ParserActionHelpers.RequireHost(context)
                    .EvalSchemeToken(values[1], locations[1]);
                return result;
            });

        // The mid-rule action of `output_def_body: output_def_body { ... }
        // music_or_context_def`:
        // { if (scm_is_pair ($1))
        //       $1 = scm_car ($1);
        //   parser->lexer_->push_note_state (); }
        // — music (or a \context definition) inside an output definition is lexed
        // in note mode. $1 is the output_def_body already ON THE STACK below this
        // empty reduction, both read and REASSIGNED — the
        // StackValue/SetStackValue pair's business, as in RAG3's $@7.
        table.Add(
            "$@8: /* empty */",
            (context, values, locations, location) =>
            {
                if (context.StackValue(0) is Pair pair)
                {
                    context.SetStackValue(0, pair.Car);
                }

                ParserActionHelpers.RequireHost(context).PushNoteState();
                return Unspecified.Instance;
            });

        // output_def_body: output_def_body $@8 music_or_context_def {
        //     parser->lexer_->pop_state ();
        //     if (unsmob<Context_def> ($3))
        //         assign_context_def (unsmob<Output_def> ($1), $3);
        //     else {
        //         SCM proc = parser->lexer_->lookup_identifier ("output-def-music-handler");
        //         ly_call (proc, $1, $3);
        //     }
        //     $$ = $1; }
        table.Add(
            "output_def_body: output_def_body $@8 music_or_context_def",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                host.PopLexerState();
                if (values[2] is ContextDef)
                {
                    ParserActionHelpers.AssignContextDef((OutputDef)values[0], values[2]);
                }
                else
                {
                    object proc = host.LookupIdentifier("output-def-music-handler");
                    host.Call(proc, values[0], values[2]);
                }

                return values[0];
            });

        // output_def_body: output_def_body error { } — the empty body: recovery
        // keeps whatever the body had built, and never assigns $$.
        table.Add(
            "output_def_body: output_def_body error",
            (context, values, locations, location) => values[0]);

        // ------ tempo_event (parser.yy 1455-1466) ------

        // tempo_event: TEMPO steno_duration '=' tempo_range {
        //     $$ = MAKE_SYNTAX (tempo, @$, SCM_EOL, $2, $4); }
        table.Add(
            "tempo_event: TEMPO steno_duration '=' tempo_range",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context)
                    .MakeSyntax("tempo", location, Nil.Instance, values[1], values[3]));

        // tempo_event: TEMPO text steno_duration '=' tempo_range {
        //     $$ = MAKE_SYNTAX (tempo, @$, $2, $3, $5); }
        table.Add(
            "tempo_event: TEMPO text steno_duration '=' tempo_range",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context)
                    .MakeSyntax("tempo", location, values[1], values[2], values[4]));

        // tempo_event: TEMPO text %prec ':' {
        //     $$ = MAKE_SYNTAX (tempo, @$, $2); }
        table.Add(
            "tempo_event: TEMPO text %prec ':'",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context)
                    .MakeSyntax("tempo", location, values[1]));

        // ------ tempo_range (parser.yy 3920-3927) ------

        // tempo_range: exact_unsigned_number %prec ':' { $$ = $1; }
        table.Add(
            "tempo_range: exact_unsigned_number %prec ':'",
            (context, values, locations, location) => values[0]);

        // tempo_range: exact_unsigned_number '-' exact_unsigned_number {
        //     $$ = scm_cons ($1, $3); } — a metronome RANGE, such as 96-120.
        table.Add(
            "tempo_range: exact_unsigned_number '-' exact_unsigned_number",
            (context, values, locations, location) => new Pair(values[0], values[2]));
    }
}
