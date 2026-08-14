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

    /// <summary>Gets the font's name, which for a text font is its description.</summary>
    public override string FontName => DescriptionString;

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
    public Stencil TextStencil(string text)
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

        TextFace previousFace = null;
        int previousGlyph = 0;

        for (int i = 0; i < text.Length;)
        {
            int codePoint = char.ConvertToUtf32(text, i);
            i += char.IsSurrogatePair(text, i) ? 2 : 1;

            TextFace face = Resolve(codePoint);
            if (face == null)
            {
                continue;
            }

            // A code point no face in the chain covers deliberately draws .notdef —
            // D23's tofu — and .notdef still occupies its own advance, so it is
            // measured like any other glyph rather than skipped.
            int glyph = face.GlyphIndex(codePoint);
            double scale = Scale(face);

            // Upstream's X extent is Pango's logical rectangle for a SHAPED run, and
            // shaping applies the font's kerning to the advances — a raw hmtx sum is
            // never larger by accident, it is larger by exactly the kerning (trap 6f).
            // The pair adjustment belongs to the FIRST glyph's advance, so it lands
            // before the second glyph is placed; a face change ends the run, because
            // Pango itemizes runs per font and never kerns across two of them.
            if (ReferenceEquals(previousFace, face))
            {
                advance += face.Kerning(previousGlyph, glyph) * scale;
            }

            if (face.Cff != null)
            {
                run.Add(Pair.List(
                    TranslateSymbol,
                    new Pair(advance, 0.0),
                    Pair.List(GlyphOutlineSymbol, face, (long)glyph, scale)));
            }

            advance += face.Advance(glyph) * scale;
            previousFace = face;
            previousGlyph = glyph;

            Box ink = face.GlyphBox(glyph);
            if (!ink.Y.IsEmpty)
            {
                double low = ink.Y.Left * scale;
                double high = ink.Y.Right * scale;
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
    /// Returns the factor that turns one face's design units into output units.
    /// </summary>
    /// <param name="face">The face.</param>
    /// <returns>The factor.</returns>
    private double Scale(TextFace face)
    {
        int unitsPerEm = face.UnitsPerEm > 0 ? face.UnitsPerEm : 1000;
        return Size / (unitsPerEm * OutputScale);
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
