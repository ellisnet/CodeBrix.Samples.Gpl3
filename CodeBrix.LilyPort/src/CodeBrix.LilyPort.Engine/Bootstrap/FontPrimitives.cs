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
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap; //was previously: lily/open-type-font-scheme.cc, lily/otf-scheme.cc, lily/all-font-metrics-scheme.cc, lily/font-metric-scheme.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The font entry points the Scheme layer reads metadata through.
/// <para>
/// The <c>ly:font-*</c> half that a grob uses while engraving lives with the grob
/// callbacks; this is the introspective half — glyph counts, glyph lists, raw table
/// bytes — which <c>scm/font.scm</c>, the documentation generators and the font-file
/// embedding code use.
/// </para>
/// <para>
/// Several of these exist ONLY to embed a font into a PostScript or PDF file, and the
/// port has no such backend (D15). Those are filed as D25 N/A candidates and THROW
/// rather than answering <see langword="false"/> — an entry point that quietly answers
/// "no" is how a missing subsystem turns into a wrong drawing three layers away.
/// </para>
/// </summary>
public static class FontPrimitives
{
    /// <summary>Installs the font introspection entry points.</summary>
    /// <param name="interpreter">The interpreter to install into.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            return;
        }

        interpreter.DefinePrimitive("ly:font-glyph-name-to-index", 2, 2, a =>
        {
            if (!(a[0] is FontMetric font))
            {
                throw SchemeErrors.WrongType(
                    "ly:font-glyph-name-to-index", "font metric", a[0]);
            }

            return (long)font.NameToIndex(
                StringPrimitives.Text(a[1], "ly:font-glyph-name-to-index"));
        });

        interpreter.DefinePrimitive("ly:font-index-to-charcode", 2, 2, a =>
        {
            if (!(a[0] is FontMetric font))
            {
                throw SchemeErrors.WrongType(
                    "ly:font-index-to-charcode", "font metric", a[0]);
            }

            return (long)IndexToCharCode(
                font, (int)SchemeConvert.ToLong(a[1], "ly:font-index-to-charcode"));
        });

        interpreter.DefinePrimitive("ly:otf-glyph-count", 1, 1, a =>
            (long)Otf(a[0], "ly:otf-glyph-count").GlyphCount);

        interpreter.DefinePrimitive("ly:otf-glyph-list", 1, 1, a =>
        {
            OpenTypeFont font = Otf(a[0], "ly:otf-glyph-list");
            List<object> names = new List<object>(font.GlyphCount);
            foreach (string name in font.GlyphNames)
            {
                names.Add(new MutableString(name));
            }

            return Pair.ListFrom(names);
        });

        interpreter.DefinePrimitive("ly:otf-font-glyph-info", 2, 2, a =>
        {
            OpenTypeFont font = Otf(a[0], "ly:otf-font-glyph-info");
            object entry = font.CharacterEntry(
                StringPrimitives.Text(a[1], "ly:otf-font-glyph-info"));

            // Upstream reads its char table with a default of '(), so a glyph with no
            // recorded metadata answers the empty list rather than #f.
            return entry ?? Nil.Instance;
        });

        interpreter.DefinePrimitive("ly:otf-font-table-data", 2, 2, a =>
        {
            OpenTypeFont font = Otf(a[0], "ly:otf-font-table-data");
            string tag = StringPrimitives.Text(a[1], "ly:otf-font-table-data");
            if (tag.Length != 4)
            {
                throw SchemeErrors.WrongType(
                    "ly:otf-font-table-data", "4-letter table tag", a[1]);
            }

            byte[] table = font.Reader.GetTable(tag);

            // Latin-1 because the bytes are BYTES: upstream returns them through
            // scm_from_latin1_stringn precisely so that every octet survives, and
            // decoding as UTF-8 would corrupt any table with a high byte in it.
            return table == null
                ? (object)false
                : new MutableString(System.Text.Encoding.Latin1.GetString(table));
        });

        // ly:reset-all-fonts, ly:font-name, ly:font-file-name, ly:font-design-size,
        // ly:font-magnification and ly:font-get-glyph are installed with the grob
        // callbacks, where the engraving side of the font interface lives.
        InstallNotApplicable(interpreter);
        InstallFontPredicates(interpreter);
    }

    /// <summary>
    /// The two font TYPE PREDICATES <c>open-type-font-scheme.cc</c> and
    /// <c>pango-font-scheme.cc</c> declare.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    /// <remarks>
    /// ⚠ Both are declared with <c>LY_DEFINE</c> rather than as smob <c>type_p_name_</c>
    /// predicates, so <see cref="EnginePrimitives.InstallStubs"/> does NOT give them the
    /// type-predicate stub that answers <see langword="false"/> — they got the ordinary
    /// stub, which answers the inert <see cref="UnportedValue"/>, and an
    /// <c>UnportedValue</c> is TRUTHY in Scheme. So until the long-tail closure both of these answered
    /// YES to every value they were handed, including each other's.
    /// <para>
    /// <c>ly:pango-font?</c> is a constant <see langword="false"/> and that is its real
    /// implementation, not a placeholder for one: D13/D23 replace Pango with the port's
    /// own font layer, so no value in the engine can ever be a Pango font, and the
    /// correct answer to "is this one" is no. It is deliberately NOT filed N/A — a
    /// predicate that THROWS would break <c>lily.scm</c>'s <c>type-name-alist</c>, which
    /// calls every predicate in it to name a bad argument.
    /// </para>
    /// </remarks>
    private static void InstallFontPredicates(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:otf-font?", 1, 1, a => IsOtf(a[0]));

        interpreter.DefinePrimitive("ly:pango-font?", 1, 1, a => false);
    }

    /// <summary>
    /// Returns the OpenType font under a value, or <see langword="null"/> when it is not
    /// one — the non-throwing form <c>ly:otf-font?</c> needs.
    /// </summary>
    /// <param name="value">The value passed from Scheme.</param>
    /// <returns>The font, or <see langword="null"/>.</returns>
    private static bool IsOtf(object value)
    {
        FontMetric metric = value as FontMetric;

        // Follow the scaled wrapper the way upstream's original_font () does: the font a
        // grob holds is a ModifiedFontMetric at the requested magnification, so testing
        // the wrapper itself would answer no for every font in real use.
        if (metric is ModifiedFontMetric scaled)
        {
            metric = scaled.OriginalFont;
        }

        return metric is OpenTypeFontMetric;
    }

    /// <summary>
    /// The entry points that exist only to embed a font in a PostScript or PDF stream,
    /// plus the one that would load a font off the host — all of them out of scope by a
    /// ratified decision, all of them loud.
    /// </summary>
    /// <param name="interpreter">The interpreter to install into.</param>
    private static void InstallNotApplicable(Interpreter interpreter)
    {
        NotApplicable(interpreter, "ly:otf->cff", 1, 2,
            "the CFF table is extracted to embed a font in a PostScript stream; the "
            + "SVG-only port never embeds font data (D15)");

        NotApplicable(interpreter, "ly:get-cff-offset", 1, 2,
            "the CFF table offset is needed to embed a subsetted font in PostScript (D15)");

        NotApplicable(interpreter, "ly:has-glyph-names?", 1, 2,
            "asked before writing a PostScript font resource, to decide between a "
            + "name-keyed and a CID-keyed embedding (D15)");

        NotApplicable(interpreter, "ly:get-font-format", 1, 2,
            "the font format decides which PostScript embedding path to take (D15)");

        NotApplicable(interpreter, "ly:extract-subfont-from-collection", 3, 3,
            "writes one face out of an OpenType collection to a file, for PostScript "
            + "embedding; the port's faces are vendored individually (D15)");

        NotApplicable(interpreter, "ly:system-font-load", 1, 1,
            "loads a font by name from the host's font configuration; D23 forbids "
            + "system-font fallback outright, so there is no host lookup to make");
    }

    private static void NotApplicable(
        Interpreter interpreter, string name, int minimum, int maximum, string reason)
        => interpreter.DefinePrimitive(name, minimum, maximum, a =>
            throw new InvalidOperationException(
                name + ": not applicable: " + reason));

    /// <summary>
    /// Returns the OpenType font under a metric, following the scaled wrapper the way
    /// upstream's <c>original_font ()</c> does.
    /// </summary>
    /// <param name="value">The value passed from Scheme.</param>
    /// <param name="procedureName">The entry point's name, for the error.</param>
    /// <returns>The font.</returns>
    private static OpenTypeFont Otf(object value, string procedureName)
    {
        FontMetric metric = value as FontMetric;
        if (metric is ModifiedFontMetric scaled)
        {
            metric = scaled.OriginalFont;
        }

        if (metric is OpenTypeFontMetric open)
        {
            return open.Font;
        }

        throw SchemeErrors.WrongType(procedureName, "OpenType font metric", value);
    }

    /// <summary>
    /// Returns the code point a glyph index is reached by, which is what the PostScript
    /// and SVG font machinery indexes glyphs with.
    /// <para>
    /// Emmentaler maps its glyphs into the private use area in glyph order, so this is
    /// a reverse <c>cmap</c> lookup. A glyph reachable by no code point answers 0,
    /// matching upstream's behaviour for an unmapped index.
    /// </para>
    /// </summary>
    /// <param name="font">The font.</param>
    /// <param name="index">The glyph index.</param>
    /// <returns>The code point, or 0.</returns>
    private static int IndexToCharCode(FontMetric font, int index)
    {
        FontMetric metric = font;
        if (metric is ModifiedFontMetric scaled)
        {
            metric = scaled.OriginalFont;
        }

        if (!(metric is OpenTypeFontMetric open))
        {
            return 0;
        }

        foreach (KeyValuePair<int, int> entry in open.Font.Reader.ReadCmap())
        {
            if (entry.Value == index)
            {
                return entry.Key;
            }
        }

        return 0;
    }
}
