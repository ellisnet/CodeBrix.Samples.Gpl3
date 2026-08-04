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

using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (lines 3029-3107, 3935-4052);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <content>
/// RULE ACTION GROUP 13 — strings, scalars and numbers: <c>text</c>,
/// <c>simple_string</c>, <c>symbol</c>, <c>scalar</c>, the <c>number_expression</c>
/// arithmetic family, <c>bare_number</c>, the exactness-checked unsigned rules, and
/// <c>exclamations</c>/<c>questions</c>. The grammar's leaf rules — almost all
/// one-liners.
/// </content>
public static partial class LilyPondRuleActions
{
    private static void RegisterRag13(RuleActionTable table)
    {
        // ------ text / simple_string / symbol (parser.yy 3029-3080) ------

        // text: embedded_scm_bare — accept a markup; anything else is an error and
        // becomes the empty string, upstream's scm_string (SCM_EOL).
        table.Add(
            "text: embedded_scm_bare",
            (context, values, locations, location) =>
            {
                if (ParserActionHelpers.RequireHost(context).IsMarkup(values[0]))
                {
                    return values[0];
                }

                ParserActionHelpers.ParserError(context, locations[0], "markup expected");
                return string.Empty;
            });

        // simple_string: embedded_scm_bare — accept a string; anything else is an
        // error and becomes the empty string.
        table.Add(
            "simple_string: embedded_scm_bare",
            (context, values, locations, location) =>
            {
                if (ParserActionHelpers.IsSchemeString(values[0]))
                {
                    return values[0];
                }

                ParserActionHelpers.ParserError(context, locations[0], "simple string expected");
                return string.Empty;
            });

        // symbol: STRING { $$ = scm_string_to_symbol ($1); }
        table.Add(
            "symbol: STRING",
            (context, values, locations, location)
                => Symbol.Intern(ParserActionHelpers.SchemeStringText(values[0])));

        // symbol: SYMBOL — only a regular identifier may stand as a symbol bareword.
        table.Add(
            "symbol: SYMBOL",
            (context, values, locations, location) =>
            {
                if (!ParserActionHelpers.IsRegularIdentifier(values[0], false))
                {
                    ParserActionHelpers.ParserError(context, locations[0], "symbol expected");
                }

                return Symbol.Intern(ParserActionHelpers.SchemeStringText(values[0]));
            });

        // symbol: embedded_scm_bare {
        //     // This is a bit of overkill but makes the same
        //     // routine responsible for all symbol interpretations.
        //     $$ = try_string_variants (symbol_p, $1); ... }
        //
        // On failure upstream generates a fresh UNINTERNED symbol named "undefined"
        // (scm_make_symbol), in case it is used for an assignment or similar; the
        // port's Symbol.Generate is its uninterned-symbol maker — see PORT-COVERAGE.
        table.Add(
            "symbol: embedded_scm_bare",
            (context, values, locations, location) =>
            {
                IParserHost host = ParserActionHelpers.RequireHost(context);
                object result = ParserActionHelpers.TryStringVariants(
                    host, value => value is Symbol, values[0]);
                if (result is DefaultArgument)
                {
                    ParserActionHelpers.ParserError(context, locations[0], "symbol expected");

                    // Generate a unique symbol in case it is used
                    // for an assignment or similar
                    return Symbol.Generate("undefined");
                }

                return result;
            });

        // ------ scalar (parser.yy 3082-3107) ------

        // scalar: '-' bare_number { $$ = scm_difference ($2, SCM_UNDEFINED); } — the
        // one-argument scm_difference, which is negation. The grammar admits only
        // this simple kind of negative number in function-argument contexts, so
        // things like -- stay an accent there.
        table.Add(
            "scalar: '-' bare_number",
            (context, values, locations, location) => SchemeNumber.Negate(values[1]));

        // scalar: symbol_list_part_bare '.' property_path { $$ = scm_reverse_x ($1, $3); }
        table.Add(
            "scalar: symbol_list_part_bare '.' property_path",
            (context, values, locations, location)
                => ParserActionHelpers.ReverseInPlace(values[0], values[2]));

        // scalar: symbol_list_part_bare ',' property_path { $$ = scm_reverse_x ($1, $3); }
        table.Add(
            "scalar: symbol_list_part_bare ',' property_path",
            (context, values, locations, location)
                => ParserActionHelpers.ReverseInPlace(values[0], values[2]));

        // ------ number_expression arithmetic (parser.yy 3935-3979) ------

        // number_expression: number_expression '+' number_term { $$ = scm_sum ($1, $3); }
        table.Add(
            "number_expression: number_expression '+' number_term",
            (context, values, locations, location) => SchemeNumber.Add(values[0], values[2]));

        // number_expression: number_expression '-' number_term { $$ = scm_difference ($1, $3); }
        table.Add(
            "number_expression: number_expression '-' number_term",
            (context, values, locations, location) => SchemeNumber.Subtract(values[0], values[2]));

        // number_term: number_factor { $$ = $1; }
        table.Add(
            "number_term: number_factor",
            (context, values, locations, location) => values[0]);

        // number_term: number_factor '*' number_factor { $$ = scm_product ($1, $3); }
        table.Add(
            "number_term: number_factor '*' number_factor",
            (context, values, locations, location) => SchemeNumber.Multiply(values[0], values[2]));

        // number_term: number_factor '/' number_factor { $$ = scm_divide ($1, $3); }
        table.Add(
            "number_term: number_factor '/' number_factor",
            (context, values, locations, location) => SchemeNumber.Divide(values[0], values[2]));

        // number_factor: '-' number_factor { $$ = scm_difference ($2, SCM_UNDEFINED); }
        table.Add(
            "number_factor: '-' number_factor",
            (context, values, locations, location) => SchemeNumber.Negate(values[1]));

        // bare_number_common: REAL NUMBER_IDENTIFIER { $$ = scm_product ($1, $2); } —
        // `2.5\cm` and friends: a number times a number-valued identifier.
        table.Add(
            "bare_number_common: REAL NUMBER_IDENTIFIER",
            (context, values, locations, location) => SchemeNumber.Multiply(values[0], values[1]));

        // bare_number: UNSIGNED NUMBER_IDENTIFIER { $$ = scm_product ($1, $2); }
        table.Add(
            "bare_number: UNSIGNED NUMBER_IDENTIFIER",
            (context, values, locations, location) => SchemeNumber.Multiply(values[0], values[1]));

        // ------ the exactness-checked unsigned rules (parser.yy 3981-4024) ------

        // exact_unsigned_number: NUMBER_IDENTIFIER — checked, since the identifier
        // may hold any number; SCM_INUM0 is the recovery value.
        table.Add(
            "exact_unsigned_number: NUMBER_IDENTIFIER",
            (context, values, locations, location) =>
            {
                if (!SchemeNumber.IsExact(values[0])
                    || SchemeNumber.Compare(values[0], 0L) < 0)
                {
                    ParserActionHelpers.ParserError(
                        context, locations[0], "not an exact unsigned number");
                    return 0L;
                }

                return values[0];
            });

        // exact_unsigned_number: embedded_scm — same check, plus that the Scheme
        // value is a number at all.
        table.Add(
            "exact_unsigned_number: embedded_scm",
            (context, values, locations, location) =>
            {
                if (!SchemeNumber.IsNumber(values[0])
                    || !SchemeNumber.IsExact(values[0])
                    || SchemeNumber.Compare(values[0], 0L) < 0)
                {
                    ParserActionHelpers.ParserError(
                        context, locations[0], "not an exact unsigned number");
                    return 0L;
                }

                return values[0];
            });

        // unsigned_integer: NUMBER_IDENTIFIER
        table.Add(
            "unsigned_integer: NUMBER_IDENTIFIER",
            (context, values, locations, location) =>
            {
                if (!SchemeNumber.IsInteger(values[0])
                    || SchemeNumber.Compare(values[0], 0L) < 0)
                {
                    ParserActionHelpers.ParserError(
                        context, locations[0], "not an unsigned integer");
                    return 0L;
                }

                return values[0];
            });

        // unsigned_integer: embedded_scm
        table.Add(
            "unsigned_integer: embedded_scm",
            (context, values, locations, location) =>
            {
                if (!SchemeNumber.IsNumber(values[0])
                    || !SchemeNumber.IsInteger(values[0])
                    || SchemeNumber.Compare(values[0], 0L) < 0)
                {
                    ParserActionHelpers.ParserError(
                        context, locations[0], "not an unsigned integer");
                    return 0L;
                }

                return values[0];
            });

        // ------ exclamations / questions (parser.yy 4026-4052) ------
        //
        // Both accumulate a toggle: no marks is SCM_UNDEFINED, one mark is #t, and
        // each further mark is scm_not of the value so far.

        // exclamations: /* empty */ { $$ = SCM_UNDEFINED; }
        table.Add(
            "exclamations: /* empty */",
            (context, values, locations, location) => DefaultArgument.Instance);

        // exclamations: exclamations '!'
        table.Add(
            "exclamations: exclamations '!'",
            (context, values, locations, location) =>
            {
                if (values[0] is DefaultArgument)
                {
                    return true;
                }

                // scm_not: #t exactly when the value is #f.
                return values[0] is bool flag && !flag;
            });

        // questions: /* empty */ %prec ':' { $$ = SCM_UNDEFINED; }
        table.Add(
            "questions: /* empty */ %prec ':'",
            (context, values, locations, location) => DefaultArgument.Instance);

        // questions: questions '?'
        table.Add(
            "questions: questions '?'",
            (context, values, locations, location) =>
            {
                if (values[0] is DefaultArgument)
                {
                    return true;
                }

                return values[0] is bool flag && !flag;
            });
    }
}
