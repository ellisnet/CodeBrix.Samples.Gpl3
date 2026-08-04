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
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (prologue MYREPARSE macro; epilogue: check_scheme_arg);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <content>
/// The helpers the music-function arglist groups (RAG8, RAG9 and RAG10) share: the
/// <c>MYREPARSE</c> macro, the epilogue's <c>check_scheme_arg</c>, and the two small
/// wrappers this family's predicate calls need — <c>scm_is_true</c> over the value
/// model, and an argument predicate (the semantic value an <c>EXPECT_SCM</c> token
/// carries) as the CLR delegate <see cref="TryStringVariants"/> and
/// <see cref="TryWordVariants"/> take.
/// </content>
internal static partial class ParserActionHelpers
{
    /// <summary>
    /// Answers <c>scm_is_true</c> over the port's value model: everything is true
    /// except <see langword="false"/> itself.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> unless the value is Scheme's <c>#f</c>.</returns>
    internal static bool IsSchemeTrue(object value) => !(value is bool flag && !flag);

    /// <summary>
    /// Wraps an argument predicate — the Scheme procedure an <c>EXPECT_SCM</c> token
    /// carries — as the CLR delegate the <c>try_*_variants</c> helpers take, calling
    /// it through the host as upstream's <c>ly_call</c> does.
    /// </summary>
    /// <param name="host">The parser host.</param>
    /// <param name="predicate">The predicate procedure.</param>
    /// <returns>The delegate.</returns>
    internal static Func<object, bool> HostPredicate(IParserHost host, object predicate)
        => value => IsSchemeTrue(host.Call(predicate, value));

    /// <summary>
    /// The <c>MYREPARSE</c> macro from <c>parser.yy</c>'s prologue: push the pending
    /// lookahead back, then the reinterpreted token, then a synthetic <c>REPARSE</c>
    /// carrying the predicate — so the parser next sees <c>REPARSE</c>, the token, and
    /// the old lookahead, in that order.
    /// <para>Upstream guards the lookahead pushback with <c>yychar != YYEMPTY</c>;
    /// <see cref="ParseContext.PushBackLookahead"/> carries the same guard. As with
    /// <see cref="MyBackup"/>, this may only run while the lookahead "must not yet
    /// have made an impact on the state stack other than causing the reduction of the
    /// current rule" (upstream's words).</para>
    /// </summary>
    /// <param name="context">The parse in progress.</param>
    /// <param name="location">The span both pushed tokens carry.</param>
    /// <param name="predicate">The <c>REPARSE</c> token's semantic value: the argument
    /// predicate the reinterpretation was chosen against.</param>
    /// <param name="terminal">The reinterpreted token's terminal name, such as
    /// <c>SYMBOL_LIST</c> or <c>DURATION_ARG</c>.</param>
    /// <param name="value">The reinterpreted token's semantic value.</param>
    internal static void MyReparse(
        ParseContext context, SourceSpan location, object predicate, string terminal, object value)
    {
        // if (yychar != YYEMPTY) push_extra_token (yylloc, yychar, yylval);
        // ... yychar = YYEMPTY;
        context.PushBackLookahead();

        // push_extra_token (Location, Token, Value);
        PushExtraToken(context, location, terminal, value);

        // push_extra_token (Location, REPARSE, Pred);
        PushExtraToken(context, location, "REPARSE", predicate);
    }

    /// <summary>
    /// Checks one argument with a given predicate for use in an argument list and
    /// reports a syntax error if it is unusable. The argument is prepended to the
    /// argument list in any case; after an error the list is terminated with
    /// <see langword="false"/> as its last cdr, marking it uncallable while keeping
    /// its length.
    /// <para>Upstream: <c>check_scheme_arg</c> in <c>parser.yy</c>'s epilogue, with
    /// <c>disp</c> defaulted to <c>SCM_UNDEFINED</c>.</para>
    /// </summary>
    /// <param name="context">The parse in progress.</param>
    /// <param name="location">Where the argument is.</param>
    /// <param name="arg">The argument; <see cref="DefaultArgument"/>.Instance makes
    /// the predicate count as failed unconditionally.</param>
    /// <param name="args">The argument list so far.</param>
    /// <param name="predicate">The predicate to satisfy.</param>
    /// <returns>The extended argument list.</returns>
    internal static object CheckSchemeArg(
        ParseContext context, SourceSpan location, object arg, object args, object predicate)
        => CheckSchemeArg(context, location, arg, args, predicate, DefaultArgument.Instance);

    /// <summary>
    /// <see cref="CheckSchemeArg(ParseContext, SourceSpan, object, object, object)"/>
    /// with an explicit display value — used in a prospective error message instead of
    /// the argument, when the argument is a transformation of what was written.
    /// </summary>
    /// <param name="context">The parse in progress.</param>
    /// <param name="location">Where the argument is.</param>
    /// <param name="arg">The argument.</param>
    /// <param name="args">The argument list so far.</param>
    /// <param name="predicate">The predicate to satisfy.</param>
    /// <param name="display">What to show in the error message, or
    /// <see cref="DefaultArgument"/>.Instance for the argument itself.</param>
    /// <returns>The extended argument list.</returns>
    internal static object CheckSchemeArg(
        ParseContext context,
        SourceSpan location,
        object arg,
        object args,
        object predicate,
        object display)
    {
        IParserHost host = RequireHost(context);

        if (arg is DefaultArgument)
        {
            args = new Pair(display, args);
        }
        else
        {
            args = new Pair(arg, args);
            if (IsSchemeTrue(host.Call(predicate, arg)))
            {
                return args;
            }
        }

        // The tail may already be #f from an earlier error, so it is restored to a
        // proper list before measuring, exactly as upstream orders the two writes.
        ((Pair)LastPair(args)).Cdr = Nil.Instance;
        host.MakeSyntax(
            "argument-error",
            location,
            (long)ListLength(args),
            predicate,
            display is DefaultArgument ? arg : display);
        ((Pair)LastPair(args)).Cdr = false;
        return args;
    }
}
