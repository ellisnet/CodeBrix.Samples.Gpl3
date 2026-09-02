// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Services; //was previously: i18n/mofile.py (parse_plural_expr)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The <c>plural=</c> expression out of a catalog's <c>Plural-Forms</c> header,
/// compiled to something that can answer "which form does this count take?".
/// </summary>
/// <remarks>
/// <para>
/// //was previously: <c>i18n/mofile.py</c>'s <c>parse_plural_expr</c>, which
/// rewrites the C expression into a PYTHON one and hands it to <c>eval</c>.
/// The rewrite is kept exactly — the same token regular expression, the same
/// recursive shuffle that turns <c>a ? b : c</c> into <c>b if a else c</c>,
/// the same <c>&amp;&amp;</c>/<c>||</c>/<c>!</c> to <c>and</c>/<c>or</c>/<c>not</c>
/// substitution — and what upstream then hands to <c>eval</c> is here handed
/// to <see cref="Evaluate"/>, a reader of that same Python subset. Doing it in
/// two steps rather than parsing the C directly is what makes this port
/// checkable against upstream: <c>tools/i18nharvest/gen-i18n-fixtures.py</c>
/// records BOTH the rewritten Python source and its answers, and the tests
/// assert both.
/// </para>
/// <para>
/// Python's operators are not C's, and the difference is deliberate here:
/// <c>and</c> and <c>or</c> yield an OPERAND rather than a truth value, a
/// comparison yields <c>True</c>/<c>False</c> (1 and 0 to <c>int()</c>), and
/// <c>/</c> is true division. Every real <c>Plural-Forms</c> expression stays
/// well inside the part where C and Python agree; the port follows Python
/// because that is what upstream's answers come from.
/// </para>
/// </remarks>
public sealed class PluralExpression
{
    //Upstream's own token regular expression, character for character.
    private static readonly Regex TokenPattern = new Regex(
        @"\d+|>>|<<|[<>!=]=|&&|\|\||[-+*/%^&<>?:|!()n]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IReadOnlyList<string> _tokens;

    private PluralExpression(IReadOnlyList<string> tokens, string source)
    {
        _tokens = tokens;
        Source = source;
    }

    /// <summary>Gets the rewritten Python expression, as upstream builds it.</summary>
    /// <remarks>Upstream's <c>py_expression</c>: the tokens joined by single
    /// spaces, which is what it compiles.</remarks>
    public string Source { get; }

    /// <summary>
    /// The rule every catalog falls back to: form 0 for one, form 1 for the
    /// rest.
    /// </summary>
    /// <remarks>Upstream's <c>self._plural = lambda n: int(n != 1)</c>.</remarks>
    public static readonly PluralExpression Default = Parse("n != 1");

    /// <summary>Parses a <c>plural=</c> expression.</summary>
    /// <param name="text">The expression, without the <c>plural=</c>.</param>
    /// <returns>The expression, or <see langword="null"/> when there is
    /// nothing to parse — upstream's "returns None if the expression could not
    /// be parsed", which leaves the catalog on its default rule.</returns>
    public static PluralExpression Parse(string text)
    {
        if (string.IsNullOrEmpty(text)) { return null; }

        List<string> source = new List<string>();
        foreach (Match match in TokenPattern.Matches(text))
        {
            source.Add(match.Value);
        }

        int index = 0;
        List<string> rewritten = Rewrite(source, ref index);
        if (rewritten.Count == 0) { return null; }

        return new PluralExpression(rewritten, string.Join(" ", rewritten));
    }

    /// <summary>Answers which plural form a count takes.</summary>
    /// <param name="count">The count.</param>
    /// <returns>The form index.</returns>
    /// <remarks>Upstream's <c>int(&lt;expression&gt;)</c>: a comparison
    /// answers 1 or 0, and a fractional value is truncated towards zero.</remarks>
    public long Evaluate(long count)
    {
        int index = 0;
        double value = ReadConditional(_tokens, ref index, count);
        return (long)Math.Truncate(value);
    }

    /// <summary>
    /// Upstream's <c>_expr()</c>: reads tokens until the expression it was
    /// started for ends, rewriting C's spellings into Python's.
    /// </summary>
    /// <param name="source">The token list.</param>
    /// <param name="index">Where to read from; left after what was read.</param>
    /// <returns>The rewritten tokens.</returns>
    /// <remarks>
    /// The shuffle looks odd and is upstream's: on <c>?</c> the word
    /// <c>if</c> is INSERTED at the front and the true branch is spliced in
    /// before it, so <c>a ? b : c</c> comes out as <c>b if a else c</c>. A
    /// <c>:</c> ends the branch it was reading; a <c>(</c> recurses and the
    /// matching <c>)</c> returns.
    /// </remarks>
    private static List<string> Rewrite(IReadOnlyList<string> source, ref int index)
    {
        List<string> result = new List<string>();
        while (index < source.Count)
        {
            string token = source[index];
            index++;

            if (token == "?")
            {
                result.Insert(0, "if");
                List<string> trueBranch = Rewrite(source, ref index);
                result.InsertRange(0, trueBranch);
                result.Add("else");
                result.AddRange(Rewrite(source, ref index));
            }
            else if (token == ":")
            {
                return result;
            }
            else if (token == "&&")
            {
                result.Add("and");
            }
            else if (token == "||")
            {
                result.Add("or");
            }
            else if (token == "!")
            {
                result.Add("not");
            }
            else
            {
                result.Add(token);
                if (token == "(")
                {
                    result.AddRange(Rewrite(source, ref index));
                }
                else if (token == ")")
                {
                    return result;
                }
            }
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // Reading the rewritten Python expression. The precedence is Python's:
    // conditional < or < and < not < comparison < | < ^ < & < shift < additive
    // < multiplicative < unary < atom.
    // -----------------------------------------------------------------------

    private static double ReadConditional(
        IReadOnlyList<string> tokens, ref int index, long count)
    {
        double value = ReadOr(tokens, ref index, count);
        if (!Peek(tokens, index, "if")) { return value; }

        index++;
        double condition = ReadOr(tokens, ref index, count);
        double otherwise = 0.0;
        if (Peek(tokens, index, "else"))
        {
            index++;
            otherwise = ReadConditional(tokens, ref index, count);
        }

        return condition != 0.0 ? value : otherwise;
    }

    private static double ReadOr(
        IReadOnlyList<string> tokens, ref int index, long count)
    {
        double left = ReadAnd(tokens, ref index, count);
        while (Peek(tokens, index, "or"))
        {
            index++;
            double right = ReadAnd(tokens, ref index, count);

            //Python's `or' yields an OPERAND, not a truth value.
            left = left != 0.0 ? left : right;
        }

        return left;
    }

    private static double ReadAnd(
        IReadOnlyList<string> tokens, ref int index, long count)
    {
        double left = ReadNot(tokens, ref index, count);
        while (Peek(tokens, index, "and"))
        {
            index++;
            double right = ReadNot(tokens, ref index, count);

            //Python's `and' yields an OPERAND too.
            left = left != 0.0 ? right : left;
        }

        return left;
    }

    private static double ReadNot(
        IReadOnlyList<string> tokens, ref int index, long count)
    {
        if (!Peek(tokens, index, "not")) { return ReadComparison(tokens, ref index, count); }

        index++;
        return ReadNot(tokens, ref index, count) == 0.0 ? 1.0 : 0.0;
    }

    private static double ReadComparison(
        IReadOnlyList<string> tokens, ref int index, long count)
    {
        double left = ReadBitwiseOr(tokens, ref index, count);
        while (index < tokens.Count && IsComparison(tokens[index]))
        {
            string op = tokens[index];
            index++;
            double right = ReadBitwiseOr(tokens, ref index, count);
            bool answer = op switch
            {
                "==" => left == right,
                "!=" => left != right,
                "<" => left < right,
                ">" => left > right,
                "<=" => left <= right,
                ">=" => left >= right,
                _ => false,
            };

            left = answer ? 1.0 : 0.0;
        }

        return left;
    }

    private static bool IsComparison(string token)
        => token is "==" or "!=" or "<" or ">" or "<=" or ">=";

    private static double ReadBitwiseOr(
        IReadOnlyList<string> tokens, ref int index, long count)
    {
        double left = ReadBitwiseXor(tokens, ref index, count);
        while (Peek(tokens, index, "|"))
        {
            index++;
            left = (long)left | (long)ReadBitwiseXor(tokens, ref index, count);
        }

        return left;
    }

    private static double ReadBitwiseXor(
        IReadOnlyList<string> tokens, ref int index, long count)
    {
        double left = ReadBitwiseAnd(tokens, ref index, count);
        while (Peek(tokens, index, "^"))
        {
            index++;
            left = (long)left ^ (long)ReadBitwiseAnd(tokens, ref index, count);
        }

        return left;
    }

    private static double ReadBitwiseAnd(
        IReadOnlyList<string> tokens, ref int index, long count)
    {
        double left = ReadShift(tokens, ref index, count);
        while (Peek(tokens, index, "&"))
        {
            index++;
            left = (long)left & (long)ReadShift(tokens, ref index, count);
        }

        return left;
    }

    private static double ReadShift(
        IReadOnlyList<string> tokens, ref int index, long count)
    {
        double left = ReadAdditive(tokens, ref index, count);
        while (index < tokens.Count && (tokens[index] == "<<" || tokens[index] == ">>"))
        {
            string op = tokens[index];
            index++;
            long right = (long)ReadAdditive(tokens, ref index, count);
            left = op == "<<" ? (long)left << (int)right : (long)left >> (int)right;
        }

        return left;
    }

    private static double ReadAdditive(
        IReadOnlyList<string> tokens, ref int index, long count)
    {
        double left = ReadMultiplicative(tokens, ref index, count);
        while (index < tokens.Count && (tokens[index] == "+" || tokens[index] == "-"))
        {
            string op = tokens[index];
            index++;
            double right = ReadMultiplicative(tokens, ref index, count);
            left = op == "+" ? left + right : left - right;
        }

        return left;
    }

    private static double ReadMultiplicative(
        IReadOnlyList<string> tokens, ref int index, long count)
    {
        double left = ReadUnary(tokens, ref index, count);
        while (index < tokens.Count
            && (tokens[index] == "*" || tokens[index] == "/" || tokens[index] == "%"))
        {
            string op = tokens[index];
            index++;
            double right = ReadUnary(tokens, ref index, count);
            left = op switch
            {
                "*" => left * right,

                //Python's `/' is true division; `%' takes the sign of the
                //divisor. A count is never negative, so the two agree, but the
                //answers are recorded from Python and this is how it gets them.
                "/" => right == 0.0 ? 0.0 : left / right,
                _ => right == 0.0 ? 0.0 : left - (right * Math.Floor(left / right)),
            };
        }

        return left;
    }

    private static double ReadUnary(
        IReadOnlyList<string> tokens, ref int index, long count)
    {
        if (index < tokens.Count && (tokens[index] == "-" || tokens[index] == "+"))
        {
            string op = tokens[index];
            index++;
            double value = ReadUnary(tokens, ref index, count);
            return op == "-" ? -value : value;
        }

        return ReadAtom(tokens, ref index, count);
    }

    private static double ReadAtom(
        IReadOnlyList<string> tokens, ref int index, long count)
    {
        if (index >= tokens.Count) { return 0.0; }

        string token = tokens[index];
        index++;

        if (token == "(")
        {
            double value = ReadConditional(tokens, ref index, count);
            if (Peek(tokens, index, ")")) { index++; }
            return value;
        }

        if (token == "n") { return count; }

        return long.TryParse(
            token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : 0.0;
    }

    private static bool Peek(IReadOnlyList<string> tokens, int index, string token)
        => index < tokens.Count && string.Equals(tokens[index], token, StringComparison.Ordinal);
}
