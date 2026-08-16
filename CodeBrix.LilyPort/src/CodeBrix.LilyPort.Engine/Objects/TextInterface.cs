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

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

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
    private static readonly Symbol FontFeaturesSymbol = Symbol.Intern("font-features");
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
    private static readonly Symbol GlyphStringSymbol = Symbol.Intern("glyph-string");
    private static readonly Symbol OutputScaleSymbol = Symbol.Intern("output-scale");

    // lily/include/pango-font.hh:75 — const int PANGO_RESOLUTION = 1200.
    private const double PangoResolution = 1200.0;

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
                // Upstream names the markup command through scm_procedure_name, so the
                // message reads "Markup: cycle-markup". Printing the procedure ITSELF
                // gave "#<procedure ...>" — which the file's own ly:expect-warning could
                // never match, so a file that reproduces the defect on purpose reported
                // the expectation as unmet AND the message as unexpected.
                Warn.NonFatalError(
                    "Markup depth exceeds maximal value of " + maximum + "; Markup: "
                    + ProcedureName(function));
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

        string features = FontFeatures(props);

        if (!(font is TextFontMetric textFont))
        {
            // A music encoding reached the text interface: fetaText's digits, dynamic
            // letters and figured-bass punctuation. Upstream sets these through Pango
            // over the SAME font; the port composes the run itself from the font's own
            // cmap and hmtx, and applies the font's own GSUB substitutions, which
            // together are what Pango's shaping amounts to for these runs. This branch
            // once answered an EMPTY stencil (the divergence was recorded in
            // PORT-COVERAGE) — which made \number, \dynamic and every figured-bass
            // digit silently invisible.
            return MusicFontTextStencil(layout, font, cleaned, features);
        }

        return textFont.TextStencil(cleaned, features);
    }

    /// <summary>
    /// Reads the <c>font-features</c> property chain and joins it for the shaper.
    /// <para>
    /// Upstream stores the value as a Scheme LIST and joins the entries with commas for
    /// processing with Pango, which is the form
    /// <c>pango_attr_font_features_new</c> takes; both of upstream's rejections are
    /// reproduced verbatim, because a diagnostic with an upstream counterpart owes
    /// upstream's wording (ruling R1).
    /// </para>
    /// </summary>
    /// <param name="props">The property alist chain.</param>
    /// <returns>The comma-joined feature string, empty when none is asked for.</returns>
    private static string FontFeatures(object props)
    {
        object features = SchemeUtilities.ChainAssocGet(FontFeaturesSymbol, props, false);

        if (!(features is Pair))
        {
            if (SchemeUtilities.IsSchemeTrue(features))
            {
                throw SchemeErrors.MiscError(
                    "interpret_string", "Expecting a list for font-features value");
            }

            return string.Empty;
        }

        StringBuilder result = new StringBuilder();
        for (object s = features; s is Pair pair; s = pair.Cdr)
        {
            if (!(pair.Car is MutableString || pair.Car is string))
            {
                throw SchemeErrors.MiscError(
                    "interpret_string", "Found non-string in font-features list");
            }

            if (result.Length > 0)
            {
                result.Append(',');
            }

            result.Append(pair.Car.ToString());
        }

        return result.ToString();
    }

    /// <summary>
    /// Sets a string in the MUSIC font: each code point maps through the font's
    /// character map to a named glyph, and the pen advances by the glyph's own
    /// <c>hmtx</c> advance. A code point the font does not map draws nothing and the
    /// run continues — the same silence the empty-stencil era produced, now confined
    /// to genuinely unmapped characters.
    /// <para>
    /// The run leaves here as ONE <c>glyph-string</c> expression, which is the shape
    /// <c>Pango_font::pango_item_string_stencil</c> produces: the font, its name, its
    /// size, the CID flag, a list of
    /// <c>(width (down . up) x-offset y-offset glyph-index glyph-name)</c> — one entry
    /// per glyph of the run, in order — the file name, the face index, the original
    /// text and the cluster map. The backend places each glyph by the CUMULATIVE
    /// advance of the entries before it, which is what
    /// <c>output-svg.scm</c>'s <c>next-horiz-adv</c> carries. Composing the run as a
    /// pile of separately translated <c>named-glyph</c> stencils draws the same marks
    /// in the same places, but it is not the same document.
    /// </para>
    /// <para>
    /// DIVERGENCES from upstream's expression, recorded in PORT-COVERAGE. The size is
    /// the metric's own scaling rather than a Pango point size (the port has no Pango
    /// and no <c>lily-unit-length</c> in the Engine — the backend takes the drawing
    /// scale from the FONT, exactly as it does for a <c>named-glyph</c>). The file
    /// name, face index and cluster map are the values upstream itself writes when it
    /// has none: the empty string, zero, and <c>#f</c> — they exist for PostScript and
    /// PDF embedding and for PDF copy-and-paste, neither of which the SVG backend
    /// reads. And a glyph the font can index but cannot NAME stays in the list with a
    /// <c>#f</c> name, where upstream drops it: dropping it would drop its advance
    /// with it and move every glyph after it.
    /// </para>
    /// </summary>
    /// <param name="layout">The output definition, for the device-dot grid.</param>
    /// <param name="font">The music font metric, possibly scaled.</param>
    /// <param name="text">The cleaned text.</param>
    /// <param name="features">The comma-joined <c>font-features</c> string.</param>
    /// <returns>The composed stencil.</returns>
    private static Stencil MusicFontTextStencil(
        OutputDef layout, FontMetric font, string text, string features)
    {
        // The whole run is mapped through the cmap FIRST, because a substitution reads
        // more than one glyph: Emmentaler's dlig feature turns a digit followed by a
        // backslash into a single slashed figured-bass glyph, so the run cannot be
        // composed one code point at a time.
        List<int> glyphs = new List<int>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            int codePoint = char.IsHighSurrogate(text[i]) && i + 1 < text.Length
                ? char.ConvertToUtf32(text, i)
                : text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length)
            {
                i++;
            }

            int index = font.CharToGlyphIndex(codePoint);
            if (index != FontMetric.GlyphIndexInvalid)
            {
                glyphs.Add(index);
            }
        }

        // Upstream reaches this through Pango, which hands the feature string to
        // HarfBuzz; the port applies the same features out of the font's own GSUB. This
        // is what draws `fattened.three` where a Fingering asks for ss01 and
        // `fixedwidth.four.alt` where a BassFigure asks for tnum and cv47.
        font.Substitutions?.Apply(glyphs, features);

        Stencil result = Stencil.Empty;
        double x = 0;
        Interval ink = Interval.Empty;
        List<object> descriptions = new List<object>(glyphs.Count);

        // THE DEVICE-DOT GRID, which is D10 one font class over. Upstream shapes a
        // music-font run through Pango exactly as it shapes a text one, and Pango rounds
        // each shaped glyph's advance with PANGO_UNITS_ROUND before anything reads the
        // run — so the logical rectangle is a sum of WHOLE dots and never of exact real
        // advances. TextFontMetric has rounded since D10; this path summed exact reals,
        // which is what left a music-font run's width out by up to a third of a dot.
        double pixel = DeviceDot(layout);

        for (int i = 0; i < glyphs.Count; i++)
        {
            int index = glyphs[i];

            // The kern belongs to the PAIR, so it rides on the first glyph of it — and
            // it is applied AFTER substitution, because the pair it applies to is the
            // one substitution left behind. Emmentaler kerns its digits on purpose.
            // This sum is upstream's per-glyph WIDTH, which is Pango's own kern-adjusted
            // advance and is what the cumulative placement accumulates.
            double advance = font.IndexedAdvance(index);
            if (i + 1 < glyphs.Count)
            {
                advance += font.IndexedKerning(index, glyphs[i + 1]);
            }

            string name = font.IndexToName(index);
            if (name != null)
            {
                Stencil glyph = font.FindByName(name);
                if (!Stencil.IsNullExpression(glyph.Expression))
                {
                    glyph.Translate(new Offset(x, 0));
                    result.AddStencil(glyph);
                }
            }

            // The glyph's INK height, which is what Pango's ink rectangle reports and
            // what upstream puts in the description. NOT the declared box: Emmentaler
            // declares one height for all its digits and draws them a few units apart.
            Interval height = font.GetIndexedInkDimensions(index)[Axis.Y];
            if (!height.IsEmpty)
            {
                ink.Unite(height);
            }

            // The kern is INSIDE the rounding, not outside it — settled by measurement
            // for the text faces at PARITY 5 and reproduced here for the same reason:
            // Pango rounds what the shaper produced, and the shaper had already applied
            // the kern to the pair's first advance.
            double stepped = pixel > 0.0
                ? Math.Floor((advance / pixel) + 0.5) * pixel
                : advance;

            descriptions.Add(Pair.List(
                stepped,
                new Pair(height.Left, height.Right),
                0.0,
                0.0,
                (long)index,
                name == null ? (object)false : new MutableString(name)));

            x += stepped;
        }

        // A run in which nothing drew keeps answering the empty stencil.
        if (Stencil.IsNullExpression(result.Expression))
        {
            return result;
        }

        // THE RUN'S EXTENT IS UPSTREAM'S, AND IT IS NOT ONE BOX FROM ONE SOURCE.
        // Pango_font::pango_item_string_stencil builds it as
        //   Box (Interval (PANGO_LBEARING (logical_rect), PANGO_RBEARING (logical_rect)),
        //        Interval (-PANGO_DESCENT (ink_rect),     PANGO_ASCENT (ink_rect)))
        // — X from the LOGICAL rectangle, which is where the pen starts and stops, and Y
        // from the INK rectangle, which is what the outlines actually cover. The port
        // used the union of the DECLARED glyph boxes for both axes, so a run of digits
        // reported the same height whichever digits it held.
        Box box = default;
        box.X = new Interval(0.0, x);
        box.Y = ink;

        return new Stencil(
            box,
            Pair.List(
                GlyphStringSymbol,
                font,
                new MutableString(font.FontName),
                font.FontScaling,
                false,
                Pair.ListFrom(descriptions),
                new MutableString(string.Empty),
                0L,
                new MutableString(text),
                false));
    }

    /// <summary>
    /// One Pango device dot in output units — the grid a shaped advance lands on.
    /// <para>
    /// The same quantity <see cref="Fonts.TextFontMetric.DevicePixel"/> answers, computed
    /// from the same two numbers: <c>PANGO_RESOLUTION</c> (1200, from
    /// <c>lily/include/pango-font.hh</c>) and the TOP output definition's
    /// <c>output-scale</c>. It is read off the top definition rather than the one in hand
    /// because a score's layout is a scaled child and the scale is the book's.
    /// </para>
    /// </summary>
    /// <param name="layout">The output definition.</param>
    /// <returns>The dot, or zero when there is no layout to ask.</returns>
    private static double DeviceDot(OutputDef layout)
    {
        if (layout == null)
        {
            return 0.0;
        }

        OutputDef top = layout;
        while (top.Parent != null)
        {
            top = top.Parent;
        }

        double outputScale = top.GetDimension(OutputScaleSymbol);
        if (outputScale <= 0.0)
        {
            outputScale = 1.0;
        }

        return Dimensions.InchToBigPoint / (PangoResolution * outputScale);
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
        // by the bars/meter/keys/marks group, fixed centrally.
        return SchemeUtilities.IsSchemeTrue(CallLily(MarkupCommandSignatureSymbol, pair.Car))
               && !SchemeUtilities.IsSchemeTrue(CallLily(MarkupListFunctionSymbol, pair.Car));
    }

    /// <summary>Determines whether a value is a markup list.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when it is a markup list.</returns>
    //was previously: => SchemeUtilities.ToBool(CallLily(MarkupListPredicateSymbol, value));
    // Upstream is `return scm_is_true (Lily::markup_list_p (x));` — the same Scheme truth
    // the two lines above already use for is_markup. This one was missed when that pair
    // was corrected.
    public static bool IsMarkupList(object value)
        => SchemeUtilities.IsSchemeTrue(CallLily(MarkupListPredicateSymbol, value));

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

    // scm_procedure_name: the procedure's own name, not its printed representation.
    // Upstream passes the result through ly_symbol2string, which answers the empty
    // string for an anonymous procedure.
    private static string ProcedureName(object function)
        => (function as Procedure)?.EffectiveName ?? string.Empty;
}
