/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Fonts; //was previously: lily/font-metric.cc, lily/include/font-metric.hh, lily/modified-font-metric.cc, lily/include/modified-font-metric.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.
// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - OpenTypeFontMetric.IndexedAdvance scales the raw hmtx value by FontScaling over
//     the em, the same factor the SVG backend draws outlines with. It multiplied by
//     the POINT constant before — a unit from a different domain — so every composed
//     text run in the MUSIC font advanced its pen fifty times too far. See
//     MusicFontAdvanceTests and PORT-COVERAGE, STAFF-LINES FOLLOW-UP.

/// <summary>
/// A font as the engine consumes it: a way to turn a glyph NAME into a stencil and a
/// bounding box.
/// <para>
/// The music font is never treated as text. Every music glyph is asked for by name —
/// <c>noteheads.s2</c>, <c>clefs.G</c> — and what the engraver needs back is the box the
/// glyph occupies in staff-space units, which lives in the font's own metadata tables
/// rather than in its outlines.
/// </para>
/// </summary>
public abstract class FontMetric
{
    private static readonly Symbol NamedGlyphSymbol = Symbol.Intern("named-glyph");

    /// <summary>The value <see cref="NameToIndex"/> returns for an unknown name.</summary>
    public const int GlyphIndexInvalid = -1;

    /// <summary>Gets the font's name, as the backend should refer to it.</summary>
    public abstract string FontName { get; }

    /// <summary>Gets the font's design size, in points.</summary>
    public abstract double DesignSize { get; }

    /// <summary>Gets the number of glyphs the font defines.</summary>
    public abstract int Count { get; }

    /// <summary>Returns a glyph's index from its name.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>The index, or <see cref="GlyphIndexInvalid"/> when unknown.</returns>
    public abstract int NameToIndex(string glyphName);

    /// <summary>Returns a glyph's bounding box by index.</summary>
    /// <param name="index">The glyph index.</param>
    /// <returns>The box.</returns>
    public abstract Box GetIndexedCharDimensions(int index);

    /// <summary>Returns a note head's stem attachment point.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <param name="direction">Which side the stem is on.</param>
    /// <param name="rotate">Receives whether the caller must rotate the point.</param>
    /// <returns>The attachment point.</returns>
    public abstract Offset AttachmentPoint(string glyphName, Direction direction, out bool rotate);

    /// <summary>Returns how far ledger lines may be shortened either side of a glyph.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>The range.</returns>
    public abstract Interval LedgerShorteningRange(string glyphName);

    /// <summary>Gets the factor every metric of the underlying font is scaled by.</summary>
    public virtual double Magnification => 1.0;

    /// <summary>
    /// Gets the size the backend draws this font's outlines at, which is
    /// <c>lily-library.scm</c>'s <c>modified-font-metric-font-scaling</c>: the design
    /// size times the magnification.
    /// </summary>
    public double FontScaling => DesignSize * Magnification;

    /// <summary>
    /// Returns a glyph's outline path data in FONT units, or <see langword="null"/>
    /// when this font has no outline for it.
    /// <para>
    /// Outlines are a rendering concern rather than an engraving one — the engine
    /// itself never looks at one — but they belong to the font, and the backend has
    /// only the font object to ask.
    /// </para>
    /// </summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>The path data.</returns>
    public virtual string GlyphOutline(string glyphName) => null;

    /// <summary>
    /// Returns the glyph index a code point maps to through the font's character map,
    /// for fonts that can set a STRING — the music font's fetaText digits, dynamic
    /// letters and punctuation. The base metric maps nothing.
    /// </summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns>The index, or <see cref="GlyphIndexInvalid"/> when unmapped.</returns>
    public virtual int CharToGlyphIndex(int codePoint) => GlyphIndexInvalid;

    /// <summary>Returns a glyph's name from its index, or <see langword="null"/>.</summary>
    /// <param name="index">The glyph index.</param>
    /// <returns>The name.</returns>
    public virtual string IndexToName(int index) => null;

    /// <summary>
    /// Gets the font's glyph substitutions, or <see langword="null"/> when it has none.
    /// The base metric has none.
    /// </summary>
    public virtual SubstitutionTable Substitutions => null;

    /// <summary>
    /// Returns the advance adjustment between two adjacent glyphs of one run, in the
    /// same stencil units <see cref="IndexedAdvance"/> reports. The base metric kerns
    /// nothing.
    /// </summary>
    /// <param name="leftGlyph">The earlier glyph's index.</param>
    /// <param name="rightGlyph">The later glyph's index.</param>
    /// <returns>The adjustment; most kern pairs are negative.</returns>
    public virtual double IndexedKerning(int leftGlyph, int rightGlyph) => 0.0;

    /// <summary>
    /// Returns a glyph's horizontal advance in this metric's stencil units — the same
    /// scale <see cref="GetIndexedCharDimensions"/> reports boxes in.
    /// </summary>
    /// <param name="index">The glyph index.</param>
    /// <returns>The advance, or zero.</returns>
    public virtual double IndexedAdvance(int index) => 0.0;

    /// <summary>
    /// Returns a glyph's INK bounding box — what the outline actually covers, as opposed
    /// to the box the font's metadata DECLARES for it.
    /// <para>
    /// The two are different numbers and upstream uses both, in different places. A lone
    /// <c>named-glyph</c> is measured by <see cref="GetIndexedCharDimensions"/>, which is
    /// the declared box; a Pango-SHAPED RUN takes its Y extent from Pango's INK rectangle,
    /// which is FreeType's rendering of the outline. Emmentaler declares one height for
    /// every digit and draws them a few units apart, so the two answers differ — by
    /// exactly 0.0125 staff spaces between <c>fattened.one</c> and <c>fattened.two</c> at
    /// a default staff size, which is the constant 46 comparator rows carried.
    /// </para>
    /// <para>The base metric has no outline to run and falls back to the declared box.</para>
    /// </summary>
    /// <param name="index">The glyph index.</param>
    /// <returns>The ink box, in this metric's stencil units.</returns>
    public virtual Box GetIndexedInkDimensions(int index) => GetIndexedCharDimensions(index);

    /// <summary>
    /// Returns a glyph's stencil, looked up by name.
    /// <para>
    /// Note the substitution of <c>M</c> for <c>-</c>. Glyph names in the font use
    /// <c>M</c> where LilyPond's own vocabulary uses a hyphen (breve, longa and maxima
    /// heads are the cases that bite), and upstream does the replacement HERE rather
    /// than in <see cref="NameToIndex"/> — which is a known inconsistency in the
    /// interface, remarked on in <c>note-head.cc</c>, not something to tidy up.
    /// </para>
    /// <para>
    /// A name the font does not have yields an EMPTY stencil, not an error. Callers
    /// test emptiness to fall back — <c>Note_head::select_glyph</c> tries a symmetric
    /// head first and a directed one only when the symmetric one comes back empty.
    /// </para>
    /// </summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>The stencil.</returns>
    public Stencil FindByName(string glyphName)
    {
        string name = (glyphName ?? string.Empty).Replace('-', 'M');

        int index = NameToIndex(name);
        Box box = default;
        object expression = Nil.Instance;

        if (index != GlyphIndexInvalid)
        {
            expression = Pair.List(NamedGlyphSymbol, this, new MutableString(name));
            box = GetIndexedCharDimensions(index);
        }

        return new Stencil(box, expression);
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The font's name.</returns>
    public override string ToString() => "#<Font_metric " + FontName + ">";
}

/// <summary>
/// A <see cref="FontMetric"/> over an Emmentaler OpenType file.
/// </summary>
public sealed class OpenTypeFontMetric : FontMetric
{
    private readonly OpenTypeFont _font;
    private readonly string _name;

    /// <summary>Initializes a metric over a loaded font.</summary>
    /// <param name="font">The font.</param>
    /// <param name="name">The name the backend refers to it by, such as <c>emmentaler-20</c>.</param>
    public OpenTypeFontMetric(OpenTypeFont font, string name)
    {
        _font = font ?? throw new ArgumentNullException(nameof(font));
        _name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>Gets the underlying font.</summary>
    public OpenTypeFont Font => _font;

    /// <summary>Gets the font's name.</summary>
    public override string FontName => _name;

    /// <summary>
    /// Gets the design size in POINTS, read from the <c>LILY</c> table.
    /// <para>
    /// Upstream multiplies the recorded value by <c>point_constant</c>, and defaults to
    /// 1 rather than to something plausible — deliberately, so that a font with no
    /// recorded design size trips errors quickly instead of laying out almost right.
    /// </para>
    /// </summary>
    public override double DesignSize
    {
        get
        {
            object value = _font.GlobalTable.TryGetValue(
                Symbol.Intern("design_size"), out object recorded)
                ? recorded
                : null;

            double size = value == null ? 1.0 : ToDouble(value);
            return size * Dimensions.Point;
        }
    }

    /// <summary>Gets the number of glyphs the font defines.</summary>
    public override int Count => _font.GlyphCount;

    /// <summary>Returns a glyph's index from its name.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>The index, or <see cref="FontMetric.GlyphIndexInvalid"/>.</returns>
    public override int NameToIndex(string glyphName) => _font.NameToIndex(glyphName);

    /// <summary>Returns a glyph's bounding box by index.</summary>
    /// <param name="index">The glyph index.</param>
    /// <returns>The box.</returns>
    public override Box GetIndexedCharDimensions(int index)
        => _font.GetIndexedGlyphDimensions(index);

    /// <summary>Returns a glyph's INK box, run out of the charstrings.</summary>
    /// <param name="index">The glyph index.</param>
    /// <returns>The ink box, in stencil units.</returns>
    public override Box GetIndexedInkDimensions(int index)
    {
        if (_font.Cff == null)
        {
            return GetIndexedCharDimensions(index);
        }

        Box box = _font.Cff.GlyphBox(index);
        if (box.IsEmpty)
        {
            return box;
        }

        // The charstring runs in FONT DESIGN units; the same factor the SVG backend
        // scales the same outline by puts it in stencil units.
        int unitsPerEm = _font.UnitsPerEm > 0 ? _font.UnitsPerEm : 1000;
        box.Scale(FontScaling / unitsPerEm);
        return box;
    }

    /// <summary>Returns a note head's stem attachment point.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <param name="direction">Which side the stem is on.</param>
    /// <param name="rotate">Receives whether the caller must rotate the point.</param>
    /// <returns>The attachment point.</returns>
    public override Offset AttachmentPoint(string glyphName, Direction direction, out bool rotate)
        => _font.AttachmentPoint(glyphName, direction, out rotate);

    /// <summary>Returns how far ledger lines may be shortened either side of a glyph.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>The range.</returns>
    public override Interval LedgerShorteningRange(string glyphName)
        => _font.LedgerShorteningRange(glyphName);

    /// <summary>Returns a glyph's outline path data, from the matching SVG font.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>The path data, or <see langword="null"/> when there is none.</returns>
    public override string GlyphOutline(string glyphName)
        => _font.Outlines?.Outline(glyphName);

    /// <summary>Returns the glyph index for a code point, through the font's <c>cmap</c>.</summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns>The index, or <see cref="FontMetric.GlyphIndexInvalid"/>.</returns>
    public override int CharToGlyphIndex(int codePoint) => _font.CharToGlyphIndex(codePoint);

    /// <summary>Returns a glyph's name from its index.</summary>
    /// <param name="index">The glyph index.</param>
    /// <returns>The name, or <see langword="null"/> for an invalid index.</returns>
    public override string IndexToName(int index)
        => index >= 0 && index < _font.GlyphNames.Count ? _font.GlyphNames[index] : null;

    /// <summary>Gets the font's glyph substitutions.</summary>
    public override SubstitutionTable Substitutions => _font.Substitutions;

    /// <summary>
    /// Returns a pair's kern, on the same <c>FontScaling / units-per-em</c> factor
    /// <see cref="IndexedAdvance"/> uses, because a kern is an advance.
    /// </summary>
    /// <param name="leftGlyph">The earlier glyph's index.</param>
    /// <param name="rightGlyph">The later glyph's index.</param>
    /// <returns>The adjustment.</returns>
    public override double IndexedKerning(int leftGlyph, int rightGlyph)
        => _font.Kerning == null
            ? 0.0
            : _font.Kerning.Adjustment(leftGlyph, rightGlyph) * FontScaling / 1000.0;

    /// <summary>
    /// Returns a glyph's advance in the stencil's units: the raw <c>hmtx</c> value —
    /// which is in FONT units, on the same em as the outlines — times the same
    /// <c>FontScaling / units-per-em</c> factor the SVG backend draws those outlines
    /// with, so a composed run's pen positions live in the same space as its glyphs.
    /// <para>
    /// This once multiplied by the POINT
    /// constant instead, which is a unit from a different domain entirely: every
    /// advance came out exactly 50× the glyph's width, and a time signature whose
    /// spec makes the numerals a STRING (`(1/2 . 3/4)` prints "1/2" over "3/4")
    /// spread three characters across sixty staff spaces — wide enough to explode
    /// line breaking on the whole time-signature-grob-* family.
    /// </para>
    /// </summary>
    /// <param name="index">The glyph index.</param>
    /// <returns>The advance.</returns>
    public override double IndexedAdvance(int index)
        // TODO: not urgent, but do not hard-code the em. Carried over from
        // output-svg.scm's extract-glyph, which hardcodes 1000 with that same remark;
        // every shipped music font is drawn on a 1000-unit em, so the two agree.
        => _font.RawAdvance(index) * FontScaling / 1000.0;

    private static double ToDouble(object value)
    {
        switch (value)
        {
            case double d:
                return d;
            case long l:
                return l;
            case int i:
                return i;
            case System.Numerics.BigInteger big:
                return (double)big;
            case CodeBrix.LilyScheme.Numeric.Ratio ratio:
                return (double)ratio.Numerator / (double)ratio.Denominator;
            default:
                return 1.0;
        }
    }
}

/// <summary>
/// A font at a size other than its design size: every metric the original reports,
/// multiplied by a magnification.
/// <para>
/// The Emmentaler is drawn at eight discrete design sizes, so a score at any other
/// staff size gets the nearest design and this scaling on top. At the default staff
/// size of 20 the magnification is exactly 1, which is why the very first engraving
/// would look right even if this class were wrong.
/// </para>
/// </summary>
public sealed class ModifiedFontMetric : FontMetric
{
    private readonly FontMetric _original;

    /// <summary>Initializes a scaled view of a font.</summary>
    /// <param name="original">The font to scale.</param>
    /// <param name="magnification">The factor to scale every metric by.</param>
    public ModifiedFontMetric(FontMetric original, double magnification)
    {
        _original = original ?? throw new ArgumentNullException(nameof(original));
        Magnification = magnification;
    }

    /// <summary>Gets the factor every metric is scaled by.</summary>
    public override double Magnification { get; }

    /// <summary>Gets the font this one scales.</summary>
    public FontMetric OriginalFont => _original;

    /// <summary>Gets the font's name, which is the original's.</summary>
    public override string FontName => _original.FontName;

    /// <summary>Gets the design size, which is the original's — it is not scaled.</summary>
    public override double DesignSize => _original.DesignSize;

    /// <summary>Gets the number of glyphs the font defines.</summary>
    public override int Count => _original.Count;

    /// <summary>Returns a glyph's index from its name.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>The index, or <see cref="FontMetric.GlyphIndexInvalid"/>.</returns>
    public override int NameToIndex(string glyphName) => _original.NameToIndex(glyphName);

    /// <summary>Returns a glyph's bounding box by index, scaled.</summary>
    /// <param name="index">The glyph index.</param>
    /// <returns>The box.</returns>
    public override Box GetIndexedCharDimensions(int index)
    {
        Box box = _original.GetIndexedCharDimensions(index);
        box.Scale(Magnification);
        return box;
    }

    /// <summary>Returns a glyph's INK box, scaled.</summary>
    /// <param name="index">The glyph index.</param>
    /// <returns>The ink box.</returns>
    public override Box GetIndexedInkDimensions(int index)
    {
        Box box = _original.GetIndexedInkDimensions(index);
        box.Scale(Magnification);
        return box;
    }

    /// <summary>Returns a note head's stem attachment point, scaled.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <param name="direction">Which side the stem is on.</param>
    /// <param name="rotate">Receives whether the caller must rotate the point.</param>
    /// <returns>The attachment point.</returns>
    public override Offset AttachmentPoint(string glyphName, Direction direction, out bool rotate)
        => _original.AttachmentPoint(glyphName, direction, out rotate) * Magnification;

    /// <summary>Returns how far ledger lines may be shortened either side of a glyph, scaled.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>The range.</returns>
    public override Interval LedgerShorteningRange(string glyphName)
        => _original.LedgerShorteningRange(glyphName) * Magnification;

    /// <summary>
    /// Gets the original's glyph substitutions: substitution chooses a glyph, and the
    /// magnification decides how big it is drawn, so the two do not interact.
    /// </summary>
    public override SubstitutionTable Substitutions => _original.Substitutions;

    /// <summary>
    /// Returns a glyph's outline path data, which is the original's — outlines are in
    /// font units and the magnification reaches them through the drawing scale, not
    /// through the path.
    /// </summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>The path data, or <see langword="null"/> when there is none.</returns>
    public override string GlyphOutline(string glyphName) => _original.GlyphOutline(glyphName);

    /// <summary>Returns the glyph index for a code point, which is the original's.</summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns>The index, or <see cref="FontMetric.GlyphIndexInvalid"/>.</returns>
    public override int CharToGlyphIndex(int codePoint) => _original.CharToGlyphIndex(codePoint);

    /// <summary>Returns a glyph's name from its index, which is the original's.</summary>
    /// <param name="index">The glyph index.</param>
    /// <returns>The name, or <see langword="null"/>.</returns>
    public override string IndexToName(int index) => _original.IndexToName(index);

    /// <summary>Returns a glyph's advance, scaled.</summary>
    /// <param name="index">The glyph index.</param>
    /// <returns>The advance.</returns>
    public override double IndexedAdvance(int index)
        => _original.IndexedAdvance(index) * Magnification;

    /// <summary>Returns a pair's kern, scaled the same way the advance is.</summary>
    /// <param name="leftGlyph">The earlier glyph's index.</param>
    /// <param name="rightGlyph">The later glyph's index.</param>
    /// <returns>The adjustment.</returns>
    public override double IndexedKerning(int leftGlyph, int rightGlyph)
        => _original.IndexedKerning(leftGlyph, rightGlyph) * Magnification;
}
