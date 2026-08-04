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

using System;
using System.Text;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (prologue MYBACKUP macro; epilogue: make_reverse_key_list, try_word_variants, property_path_dot_warning);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <content>
/// The epilogue helpers RULE ACTION GROUP 7 leans on — <c>make_reverse_key_list</c>,
/// <c>try_word_variants</c> and <c>property_path_dot_warning</c> — plus the
/// <c>MYBACKUP</c> macro and <c>Lily_lexer::push_extra_token</c> reached through the
/// parse context, and the two libguile list operations (<c>scm_append_x</c>,
/// <c>scm_ilength</c>) these bodies use.
/// </content>
internal static partial class ParserActionHelpers
{
    /// <summary>
    /// Pushes a token, named by its grammar terminal, in front of everything not yet
    /// read.
    /// <para>Upstream: <c>Lily_lexer::push_extra_token (Input, int token_type, SCM)</c>,
    /// whose token number is a Bison-exported constant. The port resolves the NAME
    /// against the tables the running scanner was given
    /// (<see cref="ModalScanner.UseSymbols"/>) — by design there is no other copy of
    /// the numbering to consult, so a token source that cannot resolve names refuses
    /// LOUDLY rather than pushing a wrong number.</para>
    /// </summary>
    /// <param name="context">The parse in progress.</param>
    /// <param name="location">The span the token carries.</param>
    /// <param name="terminal">The terminal's name in the grammar, such as
    /// <c>SCM_IDENTIFIER</c>.</param>
    /// <param name="value">The semantic value.</param>
    internal static void PushExtraToken(
        ParseContext context, SourceSpan location, string terminal, object value)
        => context.Input.PushExtraToken(
            new ParserToken(TerminalNumber(context, terminal), value, location));

    /// <summary>
    /// The <c>MYBACKUP</c> macro from <c>parser.yy</c>'s prologue: push the pending
    /// lookahead back, then the given token, then a synthetic <c>BACKUP</c> — so the
    /// parser next sees <c>BACKUP</c>, the token, and the old lookahead, in that
    /// order.
    /// <para>Upstream guards the lookahead pushback with <c>yychar != YYEMPTY</c>;
    /// <see cref="ParseContext.PushBackLookahead"/> carries the same guard. This is
    /// the construct the port's driver was written to allow: it may only run while
    /// the lookahead "must not yet have made an impact on the state stack other than
    /// causing the reduction of the current rule" (upstream's words).</para>
    /// </summary>
    /// <param name="context">The parse in progress.</param>
    /// <param name="terminal">The token's terminal name, such as <c>SCM_ARG</c>.</param>
    /// <param name="value">The token's semantic value.</param>
    /// <param name="location">The span both pushed tokens carry.</param>
    internal static void MyBackup(
        ParseContext context, string terminal, object value, SourceSpan location)
    {
        // if (yychar != YYEMPTY) push_extra_token (yylloc, yychar, yylval);
        // ... yychar = YYEMPTY;
        context.PushBackLookahead();

        // push_extra_token (Location, Token, Value);
        PushExtraToken(context, location, terminal, value);

        // push_extra_token (Location, BACKUP); — value defaults to SCM_UNSPECIFIED.
        PushExtraToken(context, location, "BACKUP", Unspecified.Instance);
    }

    /// <summary>
    /// Interprets a value as a KEY LIST IN REVERSE: a single key becomes a one-key
    /// list, a string becomes a one-symbol list, and a proper list of keys and
    /// strings is reversed with its strings interned.
    /// <para>Upstream: <c>make_reverse_key_list</c> in <c>parser.yy</c>'s epilogue.
    /// A key (<c>Lily::key_p</c>) is a symbol or a non-negative exact integer —
    /// <see cref="IParserHost.IsKey"/>.</para>
    /// </summary>
    /// <param name="host">The parser host, for the key test.</param>
    /// <param name="keys">The value to interpret.</param>
    /// <returns>The reversed key list, or
    /// <see cref="DefaultArgument.Instance"/> for <c>SCM_UNDEFINED</c> when the value
    /// is not key material.</returns>
    internal static object MakeReverseKeyList(IParserHost host, object keys)
    {
        if (host.IsKey(keys))
        {
            return new Pair(keys, Nil.Instance);
        }

        if (IsSchemeString(keys))
        {
            return new Pair(Symbol.Intern(SchemeStringText(keys)), Nil.Instance);
        }

        if (ListLength(keys) < 0)
        {
            return DefaultArgument.Instance;
        }

        object result = Nil.Instance;
        for (object p = keys; p is Pair pair; p = pair.Cdr)
        {
            object element = pair.Car;
            if (host.IsKey(element))
            {
                result = new Pair(element, result);
            }
            else if (IsSchemeString(element))
            {
                result = new Pair(Symbol.Intern(SchemeStringText(element)), result);
            }
            else
            {
                return DefaultArgument.Instance;
            }
        }

        return result;
    }

    /// <summary>
    /// Tries the ways a WORD can satisfy a predicate: as the string itself, or — when
    /// it reads as a regular identifier — split on <c>.</c> and <c>,</c> into a
    /// symbol list, or as the single symbol that list holds when it has just one.
    /// <para>Upstream: <c>try_word_variants</c> in <c>parser.yy</c>'s epilogue. As
    /// with <see cref="TryStringVariants"/>, the predicate is a CLR delegate rather
    /// than a Scheme procedure — the caller ported so far passes
    /// <see cref="IParserHost.IsKeyList"/> for the fixed primitive
    /// <c>Lily::key_list_p</c>; action sites in later groups whose predicate IS an
    /// SCM value wrap it over <see cref="IParserHost.Call"/>.</para>
    /// </summary>
    /// <param name="predicate">The predicate to satisfy.</param>
    /// <param name="value">The word; always a string at the upstream call sites.</param>
    /// <returns>The accepted interpretation, or
    /// <see cref="DefaultArgument.Instance"/> for <c>SCM_UNDEFINED</c> when none
    /// fits.</returns>
    internal static object TryWordVariants(Func<object, bool> predicate, object value)
    {
        // str is always a string when we come here
        if (predicate(value))
        {
            return value;
        }

        // If this cannot be a string representation of a symbol list,
        // we are through.
        if (!IsRegularIdentifier(value, true))
        {
            return DefaultArgument.Instance;
        }

        // scm_string_split on '.', each piece split again on ',', the pieces
        // appended in order and interned.
        string text = SchemeStringText(value);
        object list = Nil.Instance;
        Pair last = null;
        foreach (string dotPiece in text.Split('.'))
        {
            foreach (string piece in dotPiece.Split(','))
            {
                Pair next = new Pair(Symbol.Intern(piece), Nil.Instance);
                if (last == null)
                {
                    list = next;
                }
                else
                {
                    last.Cdr = next;
                }

                last = next;
            }
        }

        // Let's attempt the symbol list interpretation first.
        if (predicate(list))
        {
            return list;
        }

        // If there is just one symbol in the list, we might interpret
        // it as a single symbol
        if (list is Pair single && single.Cdr is Nil && predicate(single.Car))
        {
            return single.Car;
        }

        return DefaultArgument.Instance;
    }

    /// <summary>
    /// Warns that a property path was written without its separating dots, printing
    /// the path the way it should have been written.
    /// <para>Upstream: <c>property_path_dot_warning</c> in <c>parser.yy</c>'s
    /// epilogue, over <c>Input::warning</c> — a WARNING, not a parser error: the
    /// error level does not move.</para>
    /// </summary>
    /// <param name="host">The parser host, whose <see cref="IParserHost.Warning"/>
    /// plays <c>Input::warning</c>.</param>
    /// <param name="location">Where the dot is missing.</param>
    /// <param name="list">The path, a list of symbols in proper order.</param>
    internal static void PropertyPathDotWarning(IParserHost host, SourceSpan location, object list)
    {
        // if lst is empty, don't even venture a guess...
        if (list is Pair pair)
        {
            StringBuilder text = new StringBuilder(((Symbol)pair.Car).Name);
            for (object p = pair.Cdr; p is Pair rest; p = rest.Cdr)
            {
                text.Append('.');
                text.Append(((Symbol)rest.Car).Name);
            }

            host.Warning(location, "deprecated: missing `.' in property path " + text);
        }
    }

    // scm_append_x over two lists is ParserActionHelpers.Rag2's AppendInPlace —
    // one definition serves every group.

    /// <summary>
    /// Measures a proper list, which is <c>scm_ilength</c>: the number of elements,
    /// or -1 when the value is not a proper list.
    /// </summary>
    /// <param name="list">The value to measure.</param>
    /// <returns>The length, or -1.</returns>
    internal static int ListLength(object list)
    {
        int count = 0;
        object cursor = list;
        while (cursor is Pair pair)
        {
            count++;
            cursor = pair.Cdr;
        }

        return cursor is Nil ? count : -1;
    }

    private static int TerminalNumber(ParseContext context, string name)
    {
        if (context.Input is ModalScanner scanner)
        {
            int number = scanner.Terminal(name);
            if (number >= 0)
            {
                return number;
            }
        }

        throw new InvalidOperationException(
            "This rule's action pushes a " + name + " token back into the input, so"
            + " the token source must be a ModalScanner that was given the grammar's"
            + " symbols (UseSymbols) — otherwise the token number cannot be resolved,"
            + " and pushing a guess would be the silent half-reproduction this port"
            + " refuses.");
    }
}
