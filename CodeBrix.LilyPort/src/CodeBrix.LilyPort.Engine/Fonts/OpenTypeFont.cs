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
using System.IO;
using System.IO.Compression;
using System.Text;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Fonts; //was previously: lily/open-type-font.cc, lily/include/open-type-font.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// An Emmentaler font, read for the engraver-critical metadata LilyPond stores in two
/// custom OpenType tables.
/// <para>
/// The music font is not consumed as text. The engine asks for glyphs by NAME
/// (<c>noteheads.s2</c>, <c>clefs.G</c>) and needs their extents in staff-space units,
/// their stem attachment points, and their ledger shortening ranges — none of which
/// live in the outlines. All of it lives in <c>LILC</c> and <c>LILY</c>.
/// </para>
/// <para>
/// Three corrections established by measurement (master plan section 11) are load
/// bearing here, and a reader written to the obvious assumptions gets all three wrong:
/// </para>
/// <list type="number">
/// <item>Only <c>LILC</c> is zlib-compressed. <c>LILY</c> is stored raw, and the
/// reader must fall back to reading it as-is rather than failing.</item>
/// <item>Glyph names come from the CFF charset. The <c>post</c> table is format 3.0
/// and carries no names at all.</item>
/// <item>The table contents are EVALUATED, not merely parsed — upstream wraps them as
/// <c>(quote ( ... ))</c> and calls <c>scm_eval_string</c>. That is why LilyScheme is
/// required to load the music font, not just to run <c>scm/</c>.</item>
/// </list>
/// </summary>
public sealed class OpenTypeFont
{
    private static readonly Symbol BboxSymbol = Symbol.Intern("bbox");
    private static readonly Symbol AttachmentSymbol = Symbol.Intern("attachment");
    private static readonly Symbol AttachmentDownSymbol = Symbol.Intern("attachment-down");
    private static readonly Symbol LedgerShorteningRangeSymbol
        = Symbol.Intern("ledger-shortening-range");
    private static readonly Symbol DesignSizeSymbol = Symbol.Intern("design_size");

    private readonly Dictionary<Symbol, object> _characterTable = new Dictionary<Symbol, object>();
    private readonly Dictionary<Symbol, object> _globalTable = new Dictionary<Symbol, object>();
    private readonly Dictionary<string, int> _nameToIndex = new Dictionary<string, int>(StringComparer.Ordinal);

    private Dictionary<int, int> _cmap;
    private double[] _advances;
    private readonly List<string> _glyphNames;
    private readonly Dictionary<int, Box> _indexToBox = new Dictionary<int, Box>();

    private SvgFontOutlines _outlines;
    private bool _outlinesLoaded;

    private CffFont _cff;
    private bool _cffLoaded;

    private SubstitutionTable _substitutions;
    private bool _substitutionsLoaded;

    private KerningTable _kerning;
    private bool _kerningLoaded;

    /// <summary>
    /// Initializes a font from its file, evaluating its metadata tables with the
    /// ambient interpreter.
    /// </summary>
    /// <param name="fileName">The path to the OTF.</param>
    /// <returns>The font.</returns>
    public static OpenTypeFont Load(string fileName)
        => new OpenTypeFont(
            File.ReadAllBytes(fileName),
            Path.GetFileNameWithoutExtension(fileName),
            LilyPondScheme.Current);

    /// <summary>Initializes a font from its file, evaluating its metadata tables.</summary>
    /// <param name="fileName">The path to the OTF.</param>
    /// <param name="interpreter">
    /// The interpreter to evaluate the tables with. <see langword="null"/> means NO
    /// interpreter — glyph names and indices still work, the metadata tables stay
    /// empty. It deliberately does NOT fall back to the ambient interpreter: that
    /// made the behaviour depend on whatever else in the process had bootstrapped
    /// one. Use <see cref="Load"/> when the ambient interpreter is what you want.
    /// </param>
    public OpenTypeFont(string fileName, Interpreter interpreter)
        : this(
            File.ReadAllBytes(fileName ?? throw new ArgumentNullException(nameof(fileName))),
            Path.GetFileNameWithoutExtension(fileName),
            interpreter)
    {
        FileName = fileName;
    }

    /// <summary>Initializes a font from its bytes, evaluating its metadata tables.</summary>
    /// <param name="bytes">The whole font file.</param>
    /// <param name="name">
    /// The font's name without a suffix, such as <c>emmentaler-20</c>. It is what the
    /// matching SVG font carrying the glyph OUTLINES is looked up by.
    /// </param>
    /// <param name="interpreter">
    /// The interpreter to evaluate the metadata tables with, or <see langword="null"/>
    /// for none.
    /// </param>
    public OpenTypeFont(byte[] bytes, string name, Interpreter interpreter)
    {
        if (bytes == null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        Name = name ?? string.Empty;
        FileName = Name;

        Reader = new SfntReader(bytes);
        UnitsPerEm = Reader.UnitsPerEm;

        _glyphNames = Reader.ReadCffGlyphNames();
        for (int i = 0; i < _glyphNames.Count; i++)
        {
            _nameToIndex[_glyphNames[i]] = i;
        }

        LoadSchemeTable("LILC", interpreter, _characterTable);
        LoadSchemeTable("LILY", interpreter, _globalTable);
    }

    /// <summary>Gets the font's name without a suffix, such as <c>emmentaler-20</c>.</summary>
    public string Name { get; } = string.Empty;

    /// <summary>Gets the path the font was read from, or its name when it was embedded.</summary>
    public string FileName { get; }

    /// <summary>
    /// Gets the glyph outlines, read from the SVG font shipped beside this one, or
    /// <see langword="null"/> when there is none. Loaded on first ask.
    /// </summary>
    public SvgFontOutlines Outlines
    {
        get
        {
            if (!_outlinesLoaded)
            {
                _outlinesLoaded = true;
                string document = FontAssets.OutlineFont(Name);
                _outlines = document == null ? null : new SvgFontOutlines(document);
            }

            return _outlines;
        }
    }

    /// <summary>
    /// Gets the font's glyph programs, or <see langword="null"/> when it carries no
    /// <c>CFF </c> table. Loaded on first ask.
    /// <para>
    /// Rendering does not go through here — a music glyph is drawn from the outline the
    /// shipped SVG font already holds. This is for SKYLINES, which need the outline as
    /// segments rather than as a string, and for which upstream asks FreeType.
    /// </para>
    /// </summary>
    public CffFont Cff
    {
        get
        {
            if (!_cffLoaded)
            {
                _cffLoaded = true;
                byte[] table = Reader.GetTable("CFF ");
                _cff = table == null ? null : new CffFont(table);
            }

            return _cff;
        }
    }

    /// <summary>
    /// Gets the font's GSUB substitutions, or <see langword="null"/> when it declares
    /// none this port can act on. Loaded on first ask.
    /// <para>
    /// This is what selects Emmentaler's <c>fattened</c>, <c>fixedwidth</c> and
    /// <c>.alt</c> digit variants, which a grob asks for through <c>font-features</c>
    /// and which upstream reaches through Pango.
    /// </para>
    /// </summary>
    public SubstitutionTable Substitutions
    {
        get
        {
            if (!_substitutionsLoaded)
            {
                _substitutionsLoaded = true;
                _substitutions = SubstitutionTable.Read(Reader);
            }

            return _substitutions;
        }
    }

    /// <summary>
    /// Gets the font's kerning, or <see langword="null"/> when it carries none. Loaded
    /// on first ask.
    /// <para>
    /// A music font kerns: Emmentaler ships a GPOS <c>kern</c> feature whose whole
    /// subject is the digits (<c>mf/emmentaler_kerning.py</c> computes it, including
    /// pairs for the <c>fattened</c> and <c>fixedwidth</c> variants). Upstream sets
    /// these runs through Pango like any other, so the kerning is part of the advance
    /// it measures.
    /// </para>
    /// </summary>
    public KerningTable Kerning
    {
        get
        {
            if (!_kerningLoaded)
            {
                _kerningLoaded = true;
                _kerning = KerningTable.Read(Reader);
            }

            return _kerning;
        }
    }

    /// <summary>Gets the underlying container reader.</summary>
    public SfntReader Reader { get; }

    /// <summary>Gets the font's design units per em.</summary>
    public int UnitsPerEm { get; }

    /// <summary>
    /// Gets the number of glyphs the font MAPS — upstream's <c>Open_type_font::count</c>,
    /// which answers <c>index_to_charcode_map_.size ()</c> and so counts only the glyphs a
    /// charcode reaches.
    /// <para>
    /// //was previously: <c>=> _glyphNames.Count</c>, the size of the CFF charset, which
    /// also counts <c>.notdef</c> — one too many. The vendored <c>\left-brace</c> uses
    /// <c>(1- (ly:otf-glyph-count font))</c> as the top of a binary search over glyph Y
    /// extents, so the extra index handed the search a glyph that does not exist and whose
    /// extent is empty.
    /// </para>
    /// </summary>
    public int GlyphCount
    {
        get
        {
            _cmap ??= Reader?.ReadCmap() ?? new Dictionary<int, int>();
            if (_cmap.Count == 0)
            {
                // No cmap to count: fall back to the charset less .notdef, which is at
                // index 0 and never carries a charcode (SfntReader records this).
                return Math.Max(0, _glyphNames.Count - 1);
            }

            HashSet<int> mapped = new HashSet<int>();
            foreach (KeyValuePair<int, int> entry in _cmap)
            {
                mapped.Add(entry.Value);
            }

            return mapped.Count;
        }
    }

    /// <summary>Gets the glyph names, in glyph-index order.</summary>
    public IReadOnlyList<string> GlyphNames => _glyphNames;

    /// <summary>Gets the per-glyph metadata table, keyed by glyph name.</summary>
    public IReadOnlyDictionary<Symbol, object> CharacterTable => _characterTable;

    /// <summary>Gets the font-wide metadata table.</summary>
    public IReadOnlyDictionary<Symbol, object> GlobalTable => _globalTable;

    /// <summary>The value that <see cref="NameToIndex"/> returns for an unknown name.</summary>
    public const int GlyphIndexInvalid = -1;

    /// <summary>
    /// Gets the font's design size in points, from the <c>LILY</c> table.
    /// </summary>
    public double DesignSize
        => _globalTable.TryGetValue(DesignSizeSymbol, out object value)
            ? ToDouble(value)
            : 12.0;

    /// <summary>Returns a glyph's index from its name.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>The index, or <see cref="GlyphIndexInvalid"/> when unknown.</returns>
    public int NameToIndex(string glyphName)
        => glyphName != null && _nameToIndex.TryGetValue(glyphName, out int index)
            ? index
            : GlyphIndexInvalid;

    /// <summary>
    /// Returns the glyph index a code point maps to through the font's <c>cmap</c>,
    /// reading the table once on first ask.
    /// <para>
    /// This is what lets a STRING be set in the music font: fetaText maps the ASCII
    /// digits, the dynamic letters and the figured-bass punctuation onto the same
    /// glyphs upstream reaches through Pango.
    /// </para>
    /// </summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns>The index, or <see cref="GlyphIndexInvalid"/> when unmapped.</returns>
    public int CharToGlyphIndex(int codePoint)
    {
        _cmap ??= Reader?.ReadCmap() ?? new Dictionary<int, int>();
        return _cmap.TryGetValue(codePoint, out int index) ? index : GlyphIndexInvalid;
    }

    /// <summary>
    /// Returns a glyph's horizontal advance in raw font units, from <c>hmtx</c>,
    /// reading the table once on first ask.
    /// </summary>
    /// <param name="glyphIndex">The glyph index.</param>
    /// <returns>The advance, or zero for an invalid index.</returns>
    public double RawAdvance(int glyphIndex)
    {
        _advances ??= Reader?.ReadAdvances() ?? Array.Empty<double>();
        return glyphIndex >= 0 && glyphIndex < _advances.Length ? _advances[glyphIndex] : 0.0;
    }

    /// <summary>Returns the metadata alist for a glyph.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>The alist, or <see langword="null"/> when the glyph has no entry.</returns>
    public object CharacterEntry(string glyphName)
        => _characterTable.TryGetValue(Symbol.Intern(glyphName), out object entry) ? entry : null;

    /// <summary>
    /// Returns a glyph's bounding box in staff-space units, as recorded in
    /// <c>LILC</c>.
    /// <para>
    /// Read from the metadata table, NOT from the outline. That is what makes the
    /// port's own font build usable even though its outline bounding boxes differ from
    /// the official release by one to three units of a thousand-unit em.
    /// </para>
    /// </summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>The box, empty when the glyph has no recorded dimensions.</returns>
    public Box GetGlyphDimensions(string glyphName)
    {
        object entry = CharacterEntry(glyphName);
        if (entry == null)
        {
            return default;
        }

        Pair bboxEntry = SchemeUtilities.Assq(BboxSymbol, entry);
        if (bboxEntry == null)
        {
            return default;
        }

        List<object> bounds = Pair.ToList(bboxEntry.Cdr);
        if (bounds.Count < 4)
        {
            return default;
        }

        Box box = default;
        box.X = new Interval(ToDouble(bounds[0]), ToDouble(bounds[2]));
        box.Y = new Interval(ToDouble(bounds[1]), ToDouble(bounds[3]));
        box.Scale(Dimensions.Point);
        return box;
    }

    /// <summary>Returns a glyph's bounding box by index, caching the answer.</summary>
    /// <param name="index">The glyph index.</param>
    /// <returns>The box.</returns>
    public Box GetIndexedGlyphDimensions(int index)
    {
        if (_indexToBox.TryGetValue(index, out Box cached))
        {
            return cached;
        }

        if (index < 0 || index >= _glyphNames.Count)
        {
            return default;
        }

        Box box = GetGlyphDimensions(_glyphNames[index]);
        _indexToBox[index] = box;
        return box;
    }

    /// <summary>
    /// Returns the stem attachment point for a note head, and whether it had to be
    /// derived by rotation.
    /// <para>
    /// Reads <c>attachment</c> or <c>attachment-down</c> depending on the direction,
    /// for SMuFL compliance. When the direction is down and no <c>attachment-down</c>
    /// exists, the caller is told to rotate the up attachment around the note head's
    /// centre instead — that fallback keeps fonts predating <c>attachment-down</c>
    /// working.
    /// </para>
    /// </summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <param name="direction">Which side the stem is on.</param>
    /// <param name="rotate">
    /// Receives <see langword="true"/> when the caller must rotate the returned point.
    /// </param>
    /// <returns>The attachment point, in staff-space units.</returns>
    public Offset AttachmentPoint(string glyphName, Direction direction, out bool rotate)
    {
        rotate = false;

        object entry = CharacterEntry(glyphName);
        if (entry == null)
        {
            return Offset.Zero;
        }

        Pair attachment = null;
        if (direction == Direction.Negative)
        {
            attachment = SchemeUtilities.Assq(AttachmentDownSymbol, entry);
            if (attachment == null)
            {
                rotate = true;
            }
        }

        if (direction == Direction.Positive || rotate)
        {
            attachment = SchemeUtilities.Assq(AttachmentSymbol, entry);
            if (attachment == null)
            {
                Warn.Warning("no stem attachment found in font for glyph " + glyphName);
                return Offset.Zero;
            }
        }

        if (attachment?.Cdr is Pair point)
        {
            return Dimensions.Point * new Offset(ToDouble(point.Car), ToDouble(point.Cdr));
        }

        return Offset.Zero;
    }

    /// <summary>
    /// Returns how far ledger lines may be shortened either side of a glyph.
    /// </summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>The range, empty when the glyph records none.</returns>
    public Interval LedgerShorteningRange(string glyphName)
    {
        object entry = CharacterEntry(glyphName);
        if (entry == null)
        {
            return Interval.Empty;
        }

        Pair range = SchemeUtilities.Assq(LedgerShorteningRangeSymbol, entry);
        if (!(range?.Cdr is Pair bounds))
        {
            return Interval.Empty;
        }

        return new Interval(ToDouble(bounds.Car), ToDouble(bounds.Cdr)) * Dimensions.Point;
    }

    private void LoadSchemeTable(string tag, Interpreter interpreter, Dictionary<Symbol, object> table)
    {
        byte[] raw = Reader.GetTable(tag);
        if (raw == null)
        {
            return;
        }

        string contents = DecodeTable(raw);
        if (interpreter == null)
        {
            // Without an interpreter the tables cannot be evaluated. That is a real
            // limitation and not a silent one: the font is usable for glyph names and
            // metrics-free work only.
            return;
        }

        object alist = interpreter.EvalString("(quote (" + contents + "))", tag);

        object cursor = alist;
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry && entry.Car is Symbol key)
            {
                table[key] = entry.Cdr;
            }

            cursor = pair.Cdr;
        }
    }

    /// <summary>
    /// Decodes a metadata table: zlib-inflated when it is compressed, verbatim when it
    /// is not.
    /// <para>
    /// Upstream attempts inflation on BOTH tables and falls back on a zlib data error
    /// with the comment "Apparently not a compressed table, so load it as-is." Only
    /// <c>LILC</c> is actually compressed.
    /// </para>
    /// </summary>
    /// <param name="raw">The table bytes.</param>
    /// <returns>The table text.</returns>
    public static string DecodeTable(byte[] raw)
    {
        if (raw == null)
        {
            throw new ArgumentNullException(nameof(raw));
        }

        try
        {
            using MemoryStream input = new MemoryStream(raw);
            using ZLibStream inflate = new ZLibStream(input, CompressionMode.Decompress);
            using MemoryStream output = new MemoryStream();
            inflate.CopyTo(output);
            return Encoding.Latin1.GetString(output.ToArray());
        }
        catch (InvalidDataException)
        {
            // Apparently not a compressed table, so load it as-is.
            return Encoding.Latin1.GetString(raw);
        }
    }

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
                return 0.0;
        }
    }
}
