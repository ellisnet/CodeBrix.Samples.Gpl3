/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using System.Globalization;
using System.Text;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Fonts; //was previously: lily/pango-font.cc, lily/include/pango-font.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// A text font: a family, a style and a size, plus the machinery to measure a string
/// set in it.
/// <para>
/// This stands where upstream's <c>Pango_font</c> stands, and the ledger records
/// <c>pango-font.cc</c> as replaced rather than ported — the port has no Pango, no
/// FontConfig and no FreeType. What it keeps is the CONTRACT, because two things
/// downstream depend on it exactly:
/// </para>
/// <list type="number">
/// <item>The stencil expression is <c>(utf-8-string DESCRIPTION TEXT INNER)</c>, and
/// the SVG backend turns the DESCRIPTION back into <c>font-family</c>,
/// <c>font-weight</c>, <c>font-style</c> and <c>font-size</c> attributes by pattern
/// matching. Upstream builds that string with Pango; here it is built directly, in the
/// same shape, because the backend on the far side is upstream's own algorithm.</item>
/// <item>The EXTENTS decide layout. Horizontal extent is the SHAPED advance sum —
/// <c>hmtx</c> advances plus the kern feature's pair adjustments between adjacent
/// glyphs of a run (<see cref="KerningTable"/>), because upstream's logical rectangle
/// is Pango's for a run it has already shaped; vertical extent is the INK extent,
/// which is recorded nowhere and has to be computed by running the glyphs'
/// charstrings. Upstream takes exactly this split — logical rectangle for X, ink
/// rectangle for Y — in <c>pango_item_string_stencil</c>.</item>
/// </list>
/// <para>
/// The size arrives in LilyPond's internal length unit and the metrics come out in
/// OUTPUT units, so everything is divided by the layout's <c>output-scale</c>. That
/// division is upstream's <c>scale_</c> member with Pango's resolution constants
/// cancelled out of it: <c>INCH_TO_BP / (PANGO_SCALE * PANGO_RESOLUTION *
/// output_scale)</c> multiplied by an advance Pango measured at
/// <c>PANGO_RESOLUTION</c> dots per inch leaves just <c>size / output_scale</c>.
/// </para>
/// </summary>
public sealed class TextFontMetric : FontMetric
{
    private static readonly Symbol Utf8StringSymbol = Symbol.Intern("utf-8-string");
    private static readonly Symbol CombineSymbol = Symbol.Intern("combine-stencil");
    private static readonly Symbol TranslateSymbol = Symbol.Intern("translate-stencil");

    // NEW-IN-FAMILY head, and it never leaves the utf-8-string node the backend ignores.
    // Upstream's fourth element is Pango's shaped glyph string, which the walk recurses
    // into; the port has no Pango, so it emits the run it resolved ITSELF, one node per
    // glyph, carrying the face, the glyph index and the design-units-to-output-units
    // scale — which is everything CffFont.AddOutlineToSkyline needs. Recorded in
    // PORT-COVERAGE under THE STENCIL EXPRESSION WALK.
    private static readonly Symbol GlyphOutlineSymbol = Symbol.Intern("glyph-outline");

    // lily/include/pango-font.hh:75 — const int PANGO_RESOLUTION = 1200. Upstream never
    // renders to a device, but every text metric it takes still travels through Pango at
    // this notional resolution, and one dot of it is the grid a shaped advance lands on
    // (see DevicePixel).
    private const double PangoResolution = 1200.0;

    // The three characters HarfBuzz's space fallback measures AGAINST rather than
    // synthesises: the substituted glyph itself, a digit for U+2007 FIGURE SPACE, and a
    // period for U+2008 PUNCTUATION SPACE.
    private const int Space = 0x0020;
    private const int Zero = '0';
    private const int Period = '.';

    private readonly IReadOnlyList<TextFace> _chain;

    /// <summary>Initializes a text font.</summary>
    /// <param name="family">The generic family name the backend will write out.</param>
    /// <param name="bold">Whether the bold face was asked for.</param>
    /// <param name="italic">Whether the italic face was asked for.</param>
    /// <param name="smallCaps">Whether small capitals were asked for.</param>
    /// <param name="size">The size, in LilyPond's internal length unit.</param>
    /// <param name="outputScale">The layout's <c>output-scale</c>.</param>
    public TextFontMetric(
        string family,
        bool bold,
        bool italic,
        bool smallCaps,
        double size,
        double outputScale)
    {
        Family = family ?? "serif";
        Bold = bold;
        Italic = italic;
        SmallCaps = smallCaps;
        Size = size;
        OutputScale = outputScale > 0.0 ? outputScale : 1.0;
        _chain = TextFontChain.For(Family, bold, italic);
    }

    /// <summary>Gets the family name, as the backend will write it.</summary>
    public string Family { get; }

    /// <summary>Gets whether bold was asked for.</summary>
    public bool Bold { get; }

    /// <summary>Gets whether italic was asked for.</summary>
    public bool Italic { get; }

    /// <summary>Gets whether small capitals were asked for.</summary>
    public bool SmallCaps { get; }

    /// <summary>Gets the size, in LilyPond's internal length unit.</summary>
    public double Size { get; }

    /// <summary>Gets the layout's output scale.</summary>
    public double OutputScale { get; }

    /// <summary>Gets the faces this font draws from, in fallback order.</summary>
    public IReadOnlyList<TextFace> Chain => _chain;

    /// <summary>
    /// Gets one of Pango's device dots, expressed in OUTPUT units — the grid every
    /// shaped advance is rounded to.
    /// <para>
    /// Upstream's <c>scale_</c> is <c>INCH_TO_BP / (PANGO_SCALE * PANGO_RESOLUTION *
    /// output_scale)</c> and turns ONE Pango unit into output units; a device dot is
    /// <c>PANGO_SCALE</c> of them, so the <c>PANGO_SCALE</c> cancels and a dot is just
    /// <c>INCH_TO_BP / (PANGO_RESOLUTION * output_scale)</c>. At the default
    /// <c>output-scale</c> that is 0.0341433 staff spaces.
    /// </para>
    /// </summary>
    public double DevicePixel => Dimensions.InchToBigPoint / (PangoResolution * OutputScale);

    /// <summary>
    /// Gets the Pango-style description string the stencil expression carries, such as
    /// <c>serif Bold Italic 8.25</c>.
    /// <para>
    /// The style words and their order are not free: the backend recovers them with the
    /// regular expression <c>( Bold)?( Italic)?( Small-Caps)?[ -]([0-9.]+)$</c>, and
    /// everything before the match becomes the font family verbatim. A different order
    /// does not fail — it silently lands in the family name.
    /// </para>
    /// </summary>
    public string DescriptionString
    {
        get
        {
            StringBuilder text = new StringBuilder(Family);
            if (Bold)
            {
                text.Append(" Bold");
            }

            if (Italic)
            {
                text.Append(" Italic");
            }

            if (SmallCaps)
            {
                text.Append(" Small-Caps");
            }

            text.Append(' ');

            // THREE DECIMALS, and it is not cosmetic: this string is the ONLY route the
            // size takes to the backend, which parses it back out with upstream's own
            // regular expression (output-svg.scm's pango-description-to-text) and divides
            // by output-scale to write font-size. Upstream's string comes from
            // pango_font_description_to_string, which formats the description's size to
            // three decimals; writing more digits here made the port emit sizes the
            // oracle cannot express. MEASURED against the pinned oracle across 31 font
            // sizes (steps -24..+24): with FontInterface.QuantizeToPangoUnits ahead of it,
            // this format reproduces every one. See that method's note.
            text.Append(Size.ToString("0.000", CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }

    // NO FontName OVERRIDE, deliberately. Upstream's Pango font does not override
    // Font_metric::font_name () either, so a text font answers "unknown" — which is
    // exactly what cross-style tests for. See FontMetric.FontName. The description is
    // still available as DescriptionString for the callers that genuinely want it.

    /// <summary>
    /// Gets the design size. Upstream's <c>Font_metric</c> answers 1 for anything that
    /// is not drawn at a fixed design size, and a text font is scaled continuously.
    /// </summary>
    public override double DesignSize => 1.0;

    /// <summary>Gets the number of glyphs, which a text font does not enumerate.</summary>
    public override int Count => 0;

    /// <summary>Returns a glyph index for a NAME, which a text font has none of.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>Always <see cref="FontMetric.GlyphIndexInvalid"/>.</returns>
    public override int NameToIndex(string glyphName) => GlyphIndexInvalid;

    /// <summary>Returns a glyph's box by index, which a text font does not offer.</summary>
    /// <param name="index">The glyph index.</param>
    /// <returns>An empty box.</returns>
    public override Box GetIndexedCharDimensions(int index) => default;

    /// <summary>Returns a stem attachment point, which text glyphs do not carry.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <param name="direction">Which side the stem is on.</param>
    /// <param name="rotate">Receives <see langword="false"/>.</param>
    /// <returns>The origin.</returns>
    public override Offset AttachmentPoint(string glyphName, Direction direction, out bool rotate)
    {
        rotate = false;
        return Offset.Zero;
    }

    /// <summary>Returns a ledger shortening range, which text glyphs do not carry.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>An empty interval.</returns>
    public override Interval LedgerShorteningRange(string glyphName) => Interval.Empty;

    /// <summary>
    /// Measures a string and returns the stencil that draws it.
    /// </summary>
    /// <param name="text">The text to set.</param>
    /// <returns>
    /// The stencil, whose expression is <c>(utf-8-string DESCRIPTION TEXT ())</c> and
    /// whose extents are the run's advance width and ink height.
    /// </returns>
    public Stencil TextStencil(string text) => TextStencil(text, string.Empty);

    /// <summary>
    /// Measures a string set with the features a run asks for, and returns the stencil
    /// that draws it.
    /// </summary>
    /// <param name="text">The text to set.</param>
    /// <param name="features">
    /// The comma-joined <c>font-features</c> string. Empty still applies the features
    /// HarfBuzz turns on unasked — <c>liga</c> above all — which is why
    /// <c>\typewriter</c> has to ask for <c>-liga</c> to get them off.
    /// </param>
    /// <returns>
    /// The stencil, whose expression is <c>(utf-8-string DESCRIPTION TEXT ())</c> and
    /// whose extents are the run's advance width and ink height.
    /// </returns>
    public Stencil TextStencil(string text, string features)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Stencil.Empty;
        }

        double advance = 0.0;
        bool haveInk = false;
        double bottom = 0.0;
        double top = 0.0;

        // The shaped run, in the same slot upstream puts Pango's glyph string in. The SVG
        // backend deliberately ignores this element — it sets real text and lets the
        // viewer's font engine draw it — but the skyline walk recurses into it, which is
        // what lets a text stencil contribute its REAL outlines instead of its box.
        List<object> run = new List<object>();

        // Resolve the whole string FIRST. Upstream itemizes into per-font runs and then
        // shapes each run, and a glyph's step along the line is not known until the NEXT
        // glyph is, because the kern belongs to the pair; a single forward pass cannot
        // round the step at the moment it places the glyph.
        List<ShapedGlyph> shaped = new List<ShapedGlyph>();
        for (int i = 0; i < text.Length;)
        {
            int codePoint = char.ConvertToUtf32(text, i);
            i += char.IsSurrogatePair(text, i) ? 2 : 1;

            // HarfBuzz normalizes before it maps, and for two of the space characters
            // that changes which glyph answers rather than merely which fallback rule
            // applies. See UnicodeSpaceFallback.Canonicalize.
            codePoint = UnicodeSpaceFallback.Canonicalize(codePoint);

            TextFace face = Resolve(codePoint);
            if (face == null)
            {
                continue;
            }

            // A SPACE character no face covers is NOT tofu. HarfBuzz substitutes the
            // ordinary space glyph for it and then rewrites the advance from the
            // character's space type, so the port owes both halves — otherwise a hair
            // space measures as .notdef, which in C059 is exactly as wide as an ordinary
            // space, and the run comes out too long with a tofu box in the skyline.
            if (!face.Covers(codePoint))
            {
                SpaceFallbackKind kind = UnicodeSpaceFallback.KindOf(codePoint);
                if (kind != SpaceFallbackKind.None && face.Covers(Space))
                {
                    double scale = Scale(face);
                    double advanceOf(int character) =>
                        face.Covers(character)
                            ? face.Advance(face.GlyphIndex(character)) * scale
                            : 0.0;

                    shaped.Add(new ShapedGlyph(
                        face,
                        face.GlyphIndex(Space),
                        scale,
                        UnicodeSpaceFallback.Advance(
                            kind,
                            face.UnitsPerEm * scale,
                            advanceOf(Space),
                            advanceOf(Zero),
                            advanceOf(Period))));
                    continue;
                }
            }

            // A code point no face in the chain covers deliberately draws .notdef —
            // D23's tofu — and .notdef still occupies its own advance, so it is
            // measured like any other glyph rather than skipped. Upstream cannot draw
            // it either, and SAYS SO (Pango_font::get_glyph_desc), naming the face it
            // asked: the tofu is the picture and this is the sentence.
            if (!face.Covers(codePoint) && !MusicFontCovers(codePoint))
            {
                MissingGlyphWarning.Warn(codePoint, face.FileName);
            }

            shaped.Add(new ShapedGlyph(face, face.GlyphIndex(codePoint), Scale(face)));
        }

        shaped = Substitute(shaped, features);

        double pixel = DevicePixel;

        for (int i = 0; i < shaped.Count; i++)
        {
            ShapedGlyph current = shaped[i];

            if (current.Face.Cff != null)
            {
                run.Add(Pair.List(
                    TranslateSymbol,
                    new Pair(advance, 0.0),
                    Pair.List(
                        GlyphOutlineSymbol, current.Face, (long)current.Glyph, current.Scale)));
            }

            // Upstream's X extent is Pango's logical rectangle for a SHAPED run, and
            // shaping applies the font's kerning to the advances — a raw hmtx sum is
            // never larger by accident, it is larger by exactly the kerning (trap 6f).
            // The pair adjustment belongs to the FIRST glyph of the pair, and a face
            // change ends the run, because Pango itemizes runs per font and never kerns
            // across two of them.
            double step;
            if (current.HasSynthesizedAdvance)
            {
                // HarfBuzz REPLACES the substituted glyph's advance rather than adjusting
                // it, and it does so after shaping, so no kern applies to a synthesised
                // space either.
                step = current.SynthesizedAdvance;
            }
            else
            {
                step = current.Face.Advance(current.Glyph) * current.Scale;
                if (i + 1 < shaped.Count && ReferenceEquals(shaped[i + 1].Face, current.Face))
                {
                    step += current.Face.Kerning(current.Glyph, shaped[i + 1].Glyph)
                        * current.Scale;
                }
            }

            // ROUND THE STEP TO A WHOLE DEVICE DOT. Pango rounds each shaped glyph's
            // advance with PANGO_UNITS_ROUND before anything reads the run, so upstream's
            // logical rectangle is a sum of WHOLE dots and never of exact real advances —
            // measured on the pinned oracle, whose every single-glyph advance in C059 is
            // an exact integer number of dots (H 54, x 35, o 32, i 20, "." 18). The kern
            // is inside the rounding, not outside it: "AV" comes out 87 dots where
            // 47 + 47 - round(6.186) would be 88, and "AVAVAVAV" 327 where the other
            // grouping gives 333.
            advance += Math.Floor(step / pixel + 0.5) * pixel;

            Box ink = current.Face.GlyphBox(current.Glyph);
            if (!ink.Y.IsEmpty)
            {
                double low = ink.Y.Left * current.Scale;
                double high = ink.Y.Right * current.Scale;
                bottom = haveInk ? Math.Min(bottom, low) : low;
                top = haveInk ? Math.Max(top, high) : high;
                haveInk = true;
            }
        }

        Box box = new Box(
            new Interval(0.0, advance),
            haveInk ? new Interval(bottom, top) : new Interval(0.0, 0.0));

        object inner = Nil.Instance;
        for (int i = run.Count - 1; i >= 0; i--)
        {
            inner = Pair.List(CombineSymbol, run[i], inner);
        }

        object expression = Pair.List(
            Utf8StringSymbol,
            new MutableString(DescriptionString),
            new MutableString(text),
            inner);

        return new Stencil(box, expression);
    }

    /// <summary>
    /// Applies each face's GSUB substitutions to the run, one contiguous same-face
    /// stretch at a time.
    /// <para>
    /// The stretch is the unit because Pango itemizes a string into per-font runs and
    /// shapes each one on its own: a ligature never spans two faces, and neither does a
    /// substitution. A stretch whose face substitutes nothing is left alone rather than
    /// rebuilt, which keeps the common case free.
    /// </para>
    /// </summary>
    /// <param name="shaped">The resolved run.</param>
    /// <param name="features">The comma-joined feature string.</param>
    /// <returns>The run after substitution, which may be shorter or longer.</returns>
    private List<ShapedGlyph> Substitute(List<ShapedGlyph> shaped, string features)
    {
        List<ShapedGlyph> result = null;
        List<int> glyphs = new List<int>();

        for (int start = 0; start < shaped.Count;)
        {
            TextFace face = shaped[start].Face;
            int end = start + 1;
            while (end < shaped.Count && ReferenceEquals(shaped[end].Face, face))
            {
                end++;
            }

            glyphs.Clear();
            for (int i = start; i < end; i++)
            {
                glyphs.Add(shaped[i].Glyph);
            }

            if (face.Substitute(glyphs, features))
            {
                if (result == null)
                {
                    result = new List<ShapedGlyph>(shaped.Count);
                    result.AddRange(shaped.GetRange(0, start));
                }

                double scale = shaped[start].Scale;

                // HarfBuzz applies the space fallback AFTER substitution and skips any
                // glyph a lookup LIGATED, so a synthesised advance survives a stretch
                // that substituted one-for-one and is dropped by one that did not.
                bool sameLength = glyphs.Count == end - start;
                for (int i = 0; i < glyphs.Count; i++)
                {
                    result.Add(sameLength
                        ? new ShapedGlyph(
                            face, glyphs[i], scale, shaped[start + i].SynthesizedAdvance)
                        : new ShapedGlyph(face, glyphs[i], scale));
                }
            }
            else
            {
                result?.AddRange(shaped.GetRange(start, end - start));
            }

            start = end;
        }

        return result ?? shaped;
    }

    /// <summary>
    /// Returns the factor that turns one face's design units into output units.
    /// </summary>
    /// <param name="face">The face.</param>
    /// <returns>The factor.</returns>
    private double Scale(TextFace face)
    {
        int unitsPerEm = face.UnitsPerEm > 0 ? face.UnitsPerEm : 1000;
        return Size / (unitsPerEm * OutputScale);
    }

    // One resolved glyph of a run: which face answered for the code point, the glyph it
    // maps to, and that face's design-units-to-output-units factor. Upstream's
    // PangoGlyphInfo, minus the geometry, which is computed in the second pass.
    private readonly struct ShapedGlyph
    {
        public ShapedGlyph(TextFace face, int glyph, double scale)
            : this(face, glyph, scale, double.NaN)
        {
        }

        public ShapedGlyph(TextFace face, int glyph, double scale, double synthesizedAdvance)
        {
            Face = face;
            Glyph = glyph;
            Scale = scale;
            SynthesizedAdvance = synthesizedAdvance;
        }

        public TextFace Face { get; }

        public int Glyph { get; }

        public double Scale { get; }

        // NaN where the glyph's own advance is the answer. A space HarfBuzz synthesised
        // for carries its width here instead, because the glyph it was substituted with
        // is the ordinary space and that glyph's advance is the wrong number.
        public double SynthesizedAdvance { get; }

        public bool HasSynthesizedAdvance => !double.IsNaN(SynthesizedAdvance);
    }

    private static readonly object MusicCoverageGate = new object();
    private static HashSet<int> _musicCoverage;

    /// <summary>
    /// Determines whether the MUSIC font can draw a code point, which decides only
    /// whether a text run WARNS about it — never which glyph is drawn.
    /// </summary>
    /// <remarks>
    /// Upstream's chain for a text run is fontconfig's, and under the corpus's own
    /// pinning that chain does not stop at the two text faces: MEASURED with
    /// <c>fc-match -s serif</c>, it continues into the Emmentaler faces. So a character
    /// only the music font carries — a MUSIC FLAT SIGN in a custom note name is the
    /// corpus's case — is one Pango finds and never warns about, while the port's D23
    /// chain, which stops at two text faces on purpose, does not. Both engines then
    /// emit the same <c>&lt;text&gt;</c> run and the page MATCHes; only the sentence
    /// differed.
    /// <para>
    /// CONTROLLED, because a suppression rule is only as good as what it does NOT
    /// suppress: of the 79 code points the oracle warns about across the whole
    /// reference corpus, the only one any bundled face covers is U+0069, covered by the
    /// TEXT faces — and its warning comes from the MUSIC path, which this does not
    /// touch. No Emmentaler face covers any of the other 78.
    /// </para>
    /// <para>
    /// One optical size is read because they share a character map; the size a run is
    /// actually set at cannot change which characters exist.
    /// </para>
    /// </remarks>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns><see langword="true"/> when a music font maps it.</returns>
    private static bool MusicFontCovers(int codePoint)
    {
        lock (MusicCoverageGate)
        {
            if (_musicCoverage == null)
            {
                _musicCoverage = new HashSet<int>();
                byte[] bytes = FontAssets.MusicFont("emmentaler-20");
                if (bytes != null)
                {
                    foreach (int character in new SfntReader(bytes).ReadCmap().Keys)
                    {
                        _musicCoverage.Add(character);
                    }
                }
            }

            return _musicCoverage.Contains(codePoint);
        }
    }

    private TextFace Resolve(int codePoint)
    {
        foreach (TextFace face in _chain)
        {
            if (face.Covers(codePoint))
            {
                return face;
            }
        }

        // Nothing covers it. The FIRST face still supplies the advance and the .notdef
        // box, which is what makes the tofu take up room instead of collapsing the
        // line — deliberately NOT a system-font lookup (D23).
        return _chain.Count > 0 ? _chain[0] : null;
    }
}
