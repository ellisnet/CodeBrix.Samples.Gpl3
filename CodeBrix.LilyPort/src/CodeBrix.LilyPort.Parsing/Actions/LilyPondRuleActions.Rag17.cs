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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 3603-3710);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <content>
/// RULE ACTION GROUP 17 — figured bass: <c>bass_number</c>, <c>bass_figure</c>,
/// <c>figured_bass_modification</c>, <c>br_bass_figure</c> and <c>figure_list</c> —
/// the <c>&lt;6 4&gt;</c> chords the FIGURES lexer mode produces, each figure a
/// <c>BassFigureEvent</c> music object carrying its figure (or markup text),
/// accumulated alteration, bracket flags and modification flags. The
/// <c>bass_number: UNSIGNED / STRING / SYMBOL / full_markup</c> alternatives are
/// pass-throughs upstream leaves actionless, so they need nothing here.
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterRag17(RuleActionTable table)
    {
        // ------ bass_number (parser.yy 3603-3620) ------

        // bass_number: embedded_scm_bare {
        //     // as an integer, it needs to be non-negative, and otherwise
        //     // it needs to be suitable as a markup.
        //     if (scm_is_integer ($1)
        //         ? scm_is_true (scm_negative_p ($1))
        //         : !Text_interface::is_markup ($1))
        //     {
        //         parser->parser_error (@1, _ ("bass number expected"));
        //         $$ = SCM_INUM0;
        //     } }
        //
        // A value that passes the check rides the implicit $$ = $1.
        table.Add(
            "bass_number: embedded_scm_bare",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (SchemeNumber.IsInteger(values[0])
                    ? SchemeNumber.Compare(values[0], 0L) < 0
                    : !host.IsMarkup(values[0]))
                {
                    ParserActionHelpers.ParserError(
                        context, locations[0], "bass number expected");
                    return 0L;
                }

                return values[0];
            });

        // ------ bass_figure (parser.yy 3622-3675) ------

        // bass_figure: FIGURE_SPACE {
        //     Music *bfr = MY_MAKE_MUSIC ("BassFigureEvent", @$);
        //     $$ = bfr->unprotect (); }
        table.Add(
            "bass_figure: FIGURE_SPACE",
            (context, values, locations, location)
                => ParserActionHelpers.RequireHost(context)
                    .MakeMusic("BassFigureEvent", location));

        // bass_figure: bass_number {
        //     Music *bfr = MY_MAKE_MUSIC ("BassFigureEvent", @$);
        //     $$ = bfr->self_scm ();
        //     if (scm_is_number ($1))
        //         set_property (bfr, "figure", $1);
        //     else if (Text_interface::is_markup ($1))
        //         set_property (bfr, "text", $1);
        //     bfr->unprotect (); }
        table.Add(
            "bass_figure: bass_number",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object bfr = host.MakeMusic("BassFigureEvent", location);

                if (SchemeNumber.IsNumber(values[0]))
                {
                    host.SetMusicProperty(bfr, "figure", values[0]);
                }
                else if (host.IsMarkup(values[0]))
                {
                    host.SetMusicProperty(bfr, "text", values[0]);
                }

                return bfr;
            });

        // bass_figure: bass_figure ']' {
        //     $$ = $1;
        //     set_property (unsmob<Music> ($1), "bracket-stop", SCM_BOOL_T); }
        table.Add(
            "bass_figure: bass_figure ']'",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context)
                    .SetMusicProperty(values[0], "bracket-stop", true);
                return values[0];
            });

        // bass_figure: bass_figure FIGURE_ALTERATION_EXPR {
        //     Music *m = unsmob<Music> ($1);
        //     if (scm_is_number (get_property (m, "alteration")))
        //         m->warning (_f ("Dropping surplus alteration symbols for bass figure."));
        //     else {
        //         auto alter_expr = from_scm<std::string> ($2);
        //         Rational alter (0);
        //         bool bracket = false;
        //         for (string::iterator it=alter_expr.begin(); it != alter_expr.end (); it++)
        //         {
        //             int c = *it & 0xff;
        //             if (c == '[')       bracket = true;
        //             else if (c == '!')  alter = 0;
        //             else if (c == '+')  alter += SHARP_ALTERATION;
        //             else if (c == '-')  alter += FLAT_ALTERATION;
        //         }
        //         set_property (m, "alteration", to_scm (alter));
        //         if (bracket)
        //             set_property (m, "alteration-bracket", SCM_BOOL_T);
        //     } }
        //
        // The expression is the FIGURES-mode lexer's raw text — alteration symbols
        // with optional whitespace, optionally wrapped in [ ] — so everything that
        // is not one of the four significant characters falls through the chain.
        table.Add(
            "bass_figure: bass_figure FIGURE_ALTERATION_EXPR",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                if (SchemeNumber.IsNumber(host.GetMusicProperty(values[0], "alteration")))
                {
                    host.MusicWarning(
                        values[0], "Dropping surplus alteration symbols for bass figure.");
                }
                else
                {
                    string alterExpression = ParserActionHelpers.SchemeStringText(values[1]);
                    Rational alter = new Rational(0);
                    bool bracket = false;

                    foreach (char c in alterExpression)
                    {
                        /* The friendly lexer guarantees that '[' has its matching ']',
                           so we don't have to check here. */
                        if (c == '[')
                        {
                            bracket = true;
                        }

                        /* "!" resets the counter: we mimic this traditional
                           (pre-2.23.4) behavior. */
                        else if (c == '!')
                        {
                            alter = new Rational(0);
                        }
                        else if (c == '+')
                        {
                            alter += new Rational(1, 2); // SHARP_ALTERATION (lily/pitch.cc)
                        }
                        else if (c == '-')
                        {
                            alter += new Rational(-1, 2); // FLAT_ALTERATION (lily/pitch.cc)
                        }
                    }

                    host.SetMusicProperty(
                        values[0], "alteration", SchemeConvert.FromRational(alter));
                    if (bracket)
                    {
                        host.SetMusicProperty(values[0], "alteration-bracket", true);
                    }
                }

                return values[0];
            });

        // bass_figure: bass_figure figured_bass_modification {
        //     Music *m = unsmob<Music> ($1);
        //     set_property (m, $2, SCM_BOOL_T); }
        //
        // $2 is the property-name SYMBOL the figured_bass_modification rules below
        // produce.
        table.Add(
            "bass_figure: bass_figure figured_bass_modification",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context)
                    .SetMusicProperty(values[0], ((Symbol)values[1]).Name, true);
                return values[0];
            });

        // ------ figured_bass_modification (parser.yy 3678-3691) ------

        // figured_bass_modification: E_PLUS { $$ = ly_symbol2scm ("augmented"); }
        table.Add(
            "figured_bass_modification: E_PLUS",
            (context, values, locations, location) => Symbol.Intern("augmented"));

        // figured_bass_modification: E_EXCLAMATION { $$ = ly_symbol2scm ("no-continuation"); }
        table.Add(
            "figured_bass_modification: E_EXCLAMATION",
            (context, values, locations, location) => Symbol.Intern("no-continuation"));

        // figured_bass_modification: '/' { $$ = ly_symbol2scm ("diminished"); }
        table.Add(
            "figured_bass_modification: '/'",
            (context, values, locations, location) => Symbol.Intern("diminished"));

        // figured_bass_modification: E_BACKSLASH { $$ = ly_symbol2scm ("augmented-slash"); }
        table.Add(
            "figured_bass_modification: E_BACKSLASH",
            (context, values, locations, location) => Symbol.Intern("augmented-slash"));

        // ------ br_bass_figure (parser.yy 3693-3701) ------

        // br_bass_figure: bass_figure { $$ = $1; }
        table.Add(
            "br_bass_figure: bass_figure",
            (context, values, locations, location) => values[0]);

        // br_bass_figure: '[' bass_figure {
        //     $$ = $2;
        //     set_property (unsmob<Music> ($$), "bracket-start", SCM_BOOL_T); }
        table.Add(
            "br_bass_figure: '[' bass_figure",
            (context, values, locations, location) =>
            {
                ParserActionHelpers.RequireHost(context)
                    .SetMusicProperty(values[1], "bracket-start", true);
                return values[1];
            });

        // ------ figure_list (parser.yy 3703-3710) ------

        // figure_list: /**/ { $$ = SCM_EOL; }
        table.Add(
            "figure_list: /* empty */",
            (context, values, locations, location) => Nil.Instance);

        // figure_list: figure_list br_bass_figure { $$ = scm_cons ($2, $1); }
        table.Add(
            "figure_list: figure_list br_bass_figure",
            (context, values, locations, location) => new Pair(values[1], values[0]));
    }
}
