/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
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
using System.Collections.Generic;
using System.Text;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/text-interface.cc, lily/include/text-interface.hh;

// Modified by Jeremy Ellis on 2026-08-05 as part of the CodeBrix port.

/// <summary>
/// How a markup becomes a stencil.
/// <para>
/// A markup is either a STRING, which is set in the font the property chain selects, or
/// a LIST whose head is a markup command procedure and whose tail is its arguments. The
/// second case is the whole of LilyPond's markup language: every one of the two hundred
/// odd commands in <c>scm/define-markup-commands.scm</c> is an ordinary Scheme
/// procedure taking <c>(layout props . args)</c> and returning a stencil, and
/// interpreting a markup is just applying it.
/// </para>
/// <para>
/// The recursion is bounded. A markup command may build a new markup and interpret it
/// again — that is how <c>\wordwrap</c> and friends work — and a command that does so
/// without shrinking its argument never terminates. Upstream counts depth against
/// <c>max-markup-depth</c> and reports a non-fatal error, which is reproduced here
/// rather than left to the stack.
/// </para>
/// </summary>
public static class TextInterface
{
    private static readonly Symbol StringTransformersSymbol = Symbol.Intern("string-transformers");
    private static readonly Symbol FontEncodingSymbol = Symbol.Intern("font-encoding");
    private static readonly Symbol ReplacementAlistSymbol = Symbol.Intern("replacement-alist");
    private static readonly Symbol MaxMarkupDepthSymbol = Symbol.Intern("max-markup-depth");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol MarkupCommandSignatureSymbol
        = Symbol.Intern("markup-command-signature");
    private static readonly Symbol MarkupListFunctionSymbol
        = Symbol.Intern("markup-list-function?");
    private static readonly Symbol MarkupListPredicateSymbol = Symbol.Intern("markup-list?");
    private static readonly Symbol AllMusicFontEncodingsSymbol
        = Symbol.Intern("all-music-font-encodings");
    private static readonly Symbol MakeConcatMarkupSymbol = Symbol.Intern("make-concat-markup");

    [ThreadStatic]
    private static int _depth;

    /// <summary>
    /// Interprets a markup under a layout and a property chain.
    /// </summary>
    /// <param name="layout">The output definition, which carries the fonts.</param>
    /// <param name="props">The property alist chain.</param>
    /// <param name="markup">The markup: a string, or a command applied to arguments.</param>
    /// <returns>The stencil.</returns>
    public static Stencil InterpretMarkup(OutputDef layout, object props, object markup)
    {
        string text = SchemeStringOrNull(markup);
        if (text != null)
        {
            return InterpretString(layout, props, text);
        }

        if (!IsMarkup(markup))
        {
            Warn.ProgrammingError(
                "Trying to interpret a non-markup object: " + Describe(markup));
            return Stencil.Empty;
        }

        Pair pair = (Pair)markup;
        object function = pair.Car;
        object arguments = pair.Cdr;

        Interpreter interpreter = LilyPondScheme.Current;
        if (interpreter == null)
        {
            return Stencil.Empty;
        }

        long maximum = MaxDepth();

        _depth++;
        try
        {
            if (_depth > maximum)
            {
                Warn.NonFatalError(
                    "Markup depth exceeds maximal value of " + maximum + "; Markup: "
                    + Describe(function));
                return Stencil.Empty;
            }

            List<object> applied = new List<object> { layout, props };
            applied.AddRange(Pair.ToList(arguments));

            object result = interpreter.Evaluator.Apply(function, applied.ToArray());
            if (result is Stencil stencil)
            {
                return stencil;
            }

            Warn.ProgrammingError("markup interpretation must yield stencil");
            return Stencil.Empty;
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>
    /// Sets one string in the font the property chain selects.
    /// <para>
    /// Three things happen before a glyph is chosen, and the ORDER of the first two is
    /// load bearing. Every whitespace character becomes a plain space first, because a
    /// newline reaching the font layer breaks things further down and a string
    /// transformer's OUTPUT has to be cleaned the same way — which is why the
    /// substitution is redone on each recursive call rather than once at the top.
    /// Then the <c>string-transformers</c> run, outermost first, each yielding a markup
    /// LIST that is concatenated and re-interpreted with that transformer removed.
    /// </para>
    /// </summary>
    /// <param name="layout">The output definition.</param>
    /// <param name="props">The property alist chain.</param>
    /// <param name="text">The string to set.</param>
    /// <returns>The stencil.</returns>
    public static Stencil InterpretString(OutputDef layout, object props, string text)
    {
        string cleaned = NormalizeWhitespace(text);

        FontMetric font = FontInterface.SelectFont(layout, props);

        object transformers = SchemeUtilities.ChainAssocGet(
            StringTransformersSymbol, props, Nil.Instance);

        Interpreter interpreter = LilyPondScheme.Current;
        if (transformers is Pair && interpreter != null)
        {
            // Applied outermost to innermost. Quadratic in the number of transformers,
            // and upstream says the same: there are only ever a handful.
            List<object> list = Pair.ToList(transformers);
            object outer = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);

            object transformed = interpreter.Evaluator.Apply(
                outer, new object[] { layout, props, new MutableString(cleaned) });

            object innerProps = new Pair(
                Pair.List(new Pair(StringTransformersSymbol, Pair.ListFrom(list))),
                props);

            return InterpretMarkup(layout, innerProps, transformed);
        }

        if (!(font is TextFontMetric textFont))
        {
            // A music encoding reached the text interface. Upstream still goes through
            // Pango for it, with fallback disabled; the port draws music by glyph NAME
            // and has nothing to set here, so an empty stencil is the honest answer
            // rather than a wrong drawing. Recorded in PORT-COVERAGE.
            return Stencil.Empty;
        }

        return textFont.TextStencil(cleaned);
    }

    /// <summary>
    /// Replaces every whitespace character with a space, leaving multi-byte characters
    /// alone.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The cleaned text.</returns>
    public static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        StringBuilder builder = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // Upstream guards with `!(ch & 0x80)` so it never touches a UTF-8
            // continuation byte. C# strings are already characters, so the equivalent
            // guard is to leave anything outside ASCII alone.
            if (c < 0x80 && char.IsWhiteSpace(c) && c != ' ')
            {
                builder ??= new StringBuilder(text);
                builder[i] = ' ';
            }
        }

        return builder == null ? text : builder.ToString();
    }

    /// <summary>
    /// Interprets a grob's markup, under the grob's TEXT property chain.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <param name="markup">The markup.</param>
    /// <returns>The stencil.</returns>
    public static Stencil GrobInterpretMarkup(Grob grob, object markup)
    {
        if (grob == null)
        {
            throw new ArgumentNullException(nameof(grob));
        }

        return InterpretMarkup(grob.Layout, FontInterface.TextFontAlistChain(grob), markup);
    }

    /// <summary>
    /// The <c>stencil</c> callback of the text interface: interpret the grob's
    /// <c>text</c>.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The stencil.</returns>
    public static Stencil Print(Grob grob)
    {
        object text = grob.GetProperty(TextSymbol);

        // The text callback may have caused this grob to kill itself, in which case
        // there is nothing left to draw and asking would resurrect a dead object.
        if (!grob.IsLive)
        {
            return Stencil.Empty;
        }

        return GrobInterpretMarkup(grob, text);
    }

    /// <summary>
    /// Determines whether a value is a markup: a string, or a pair whose head is a
    /// registered markup command that is not a markup LIST command.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when it is a markup.</returns>
    public static bool IsMarkup(object value)
    {
        if (SchemeStringOrNull(value) != null)
        {
            return true;
        }

        if (!(value is Pair pair))
        {
            return false;
        }

        // Scheme truth, not the C# boolean: a command's markup-command-signature is
        // a LIST, and filtering it through ToBool read every non-string markup as
        // "not a markup" — MetronomeMark and RehearsalMark texts never drew. Found
        // by EPG8, fixed centrally 2026-08-07.
        return SchemeUtilities.IsSchemeTrue(CallLily(MarkupCommandSignatureSymbol, pair.Car))
               && !SchemeUtilities.IsSchemeTrue(CallLily(MarkupListFunctionSymbol, pair.Car));
    }

    /// <summary>Determines whether a value is a markup list.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when it is a markup list.</returns>
    public static bool IsMarkupList(object value)
        => SchemeUtilities.ToBool(CallLily(MarkupListPredicateSymbol, value));

    /// <summary>
    /// Determines whether a property chain asks for a MUSIC font, which is what decides
    /// whether a string is set as text at all.
    /// </summary>
    /// <param name="props">The property alist chain.</param>
    /// <returns><see langword="true"/> for a music encoding.</returns>
    public static bool IsMusicEncoded(object props)
    {
        object encoding = SchemeUtilities.ChainAssocGet(FontEncodingSymbol, props, false);
        object encodings = LilyPondScheme.LookupProcedure(AllMusicFontEncodingsSymbol);

        object cursor = encodings;
        while (cursor is Pair pair)
        {
            if (ReferenceEquals(pair.Car, encoding))
            {
                return true;
            }

            cursor = pair.Cdr;
        }

        return false;
    }

    /// <summary>
    /// Performs the <c>replacement-alist</c> substitutions on a string, returning a
    /// <c>\concat</c> markup of the pieces.
    /// <para>
    /// This is <c>ly:perform-text-replacements</c>, the string transformer LilyPond
    /// installs by default. It is what turns <c>"..."</c> into an ellipsis and
    /// <c>"fi"</c> into a ligature when a user supplies a replacement table.
    /// </para>
    /// <para>
    /// Two rules decide what it produces, and both come straight from upstream's loop.
    /// The LONGEST matching key wins, so a table holding both <c>f</c> and <c>ffi</c>
    /// replaces the ligature rather than the letter. And the result of a replacement is
    /// never itself rescanned — scanning resumes AFTER the inserted text — so a table
    /// mapping <c>a</c> to <c>aa</c> terminates instead of running forever.
    /// </para>
    /// </summary>
    /// <param name="props">The property alist chain, which carries the table.</param>
    /// <param name="input">The string to transform.</param>
    /// <returns>The input unchanged, or a <c>\concat</c> markup of the pieces.</returns>
    public static object PerformReplacements(object props, object input)
    {
        string text = SchemeStringOrNull(input);
        object alist = SchemeUtilities.ChainAssocGet(
            ReplacementAlistSymbol, props, Nil.Instance);

        if (text == null || text.Length == 0 || !(alist is Pair))
        {
            return input;
        }

        // Longest key first is what makes "longest match wins" fall out of a simple
        // ordered scan, without upstream's upper_bound trick.
        List<(string Key, object Value)> replacements = new List<(string, object)>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        object cursor = alist;
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry)
            {
                string key = SchemeStringOrNull(entry.Car);

                // A table with duplicate keys keeps the FIRST, as upstream's
                // insert-if-absent does.
                if (!string.IsNullOrEmpty(key) && seen.Add(key))
                {
                    replacements.Add((key, entry.Cdr));
                }
            }

            cursor = pair.Cdr;
        }

        if (replacements.Count == 0)
        {
            return input;
        }

        replacements.Sort((left, right) => right.Key.Length.CompareTo(left.Key.Length));

        List<object> pieces = new List<object>();
        int start = 0;
        int position = 0;
        bool replaced = false;

        while (position < text.Length)
        {
            (string Key, object Value) match = default;
            foreach ((string Key, object Value) candidate in replacements)
            {
                if (string.CompareOrdinal(
                        text, position, candidate.Key, 0, candidate.Key.Length) == 0
                    && position + candidate.Key.Length <= text.Length)
                {
                    match = candidate;
                    break;
                }
            }

            if (match.Key == null)
            {
                position++;
                continue;
            }

            pieces.Add(new MutableString(text.Substring(start, position - start)));
            pieces.Add(match.Value);
            position += match.Key.Length;
            start = position;
            replaced = true;
        }

        if (!replaced)
        {
            return input;
        }

        pieces.Add(new MutableString(text.Substring(start)));

        object concat = LilyPondScheme.LookupProcedure(MakeConcatMarkupSymbol);
        Interpreter interpreter = LilyPondScheme.Current;
        if (concat == null || interpreter == null)
        {
            return input;
        }

        return interpreter.Evaluator.Apply(concat, new[] { Pair.ListFrom(pieces) });
    }

    /// <summary>
    /// Returns a value's text when it is a Scheme STRING, and null otherwise.
    /// <para>
    /// Deliberately narrower than the general text conversion: a markup is a string or
    /// a command list, and a symbol answering to the string test would make
    /// <c>'foo</c> interpret as text.
    /// </para>
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    private static string SchemeStringOrNull(object value)
    {
        switch (value)
        {
            case MutableString mutable:
                return mutable.ToString();
            case string text:
                return text;
            default:
                return null;
        }
    }

    private static long MaxDepth()
    {
        object option = LilyPondScheme.Options?.Get(MaxMarkupDepthSymbol.Name);
        return SchemeConvert.IsNumber(option)
            ? SchemeConvert.ToLong(option, "max-markup-depth")
            : 1024;
    }

    private static object CallLily(Symbol name, object argument)
    {
        object procedure = LilyPondScheme.LookupProcedure(name);
        Interpreter interpreter = LilyPondScheme.Current;
        if (procedure == null || interpreter == null)
        {
            return false;
        }

        try
        {
            return interpreter.Evaluator.Apply(procedure, new[] { argument });
        }
        catch (Exception exception) when (!(exception is OutOfMemoryException))
        {
            return false;
        }
    }

    private static string Describe(object value) => value == null ? "#f" : value.ToString();
}
