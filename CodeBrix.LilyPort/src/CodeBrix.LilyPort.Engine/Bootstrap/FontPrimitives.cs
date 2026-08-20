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
        InstallDocumentFonts(interpreter);
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
    /// <c>ly:font-config-add-font</c> and <c>ly:font-config-add-directory</c> — a
    /// DOCUMENT registering fonts it carries with it.
    /// <para>
    /// RULING R16 (2026-08-17) took these OFF the accepted-N/A list under D25's
    /// reversible-by-demand clause. They are a documented Notation Reference feature
    /// (§Finding fonts) whose stated purpose is portability: "Both commands accept either
    /// absolute or relative paths, which makes it possible to compile a score on any
    /// system by simply distributing the relevant font files together with the LilyPond
    /// input files." Upstream implements them as fontconfig APPLICATION fonts
    /// (<c>all-font-metrics.cc:306,319</c> → <c>FcConfigAppFontAddDir</c>/<c>AddFile</c>),
    /// the same set LilyPond's own bundled faces go into and NOT the system directories
    /// D23 forbids — <c>font-config.cc</c> builds the two sources separately.
    /// </para>
    /// <para>
    /// The diagnostics are upstream's, wording and severity both (rule 15): a failure is
    /// <c>error</c>, which is fatal, and a success is <c>debug_output</c>.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter to install into.</param>
    private static void InstallDocumentFonts(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:font-config-add-font", 1, 1, a =>
        {
            string path = StringPrimitives.Text(a[0], "ly:font-config-add-font");
            if (!TextFontChain.AddDocumentFont(path))
            {
                Flower.Warn.Error("failed adding font file: " + path);
            }

            Flower.Warn.Debug("Adding font file: " + path);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:font-config-add-directory", 1, 1, a =>
        {
            string directory
                = StringPrimitives.Text(a[0], "ly:font-config-add-directory");

            // Upstream's failure test is FcConfigAppFontAddDir's, which fails on a
            // directory it cannot read — not on one that holds no fonts. An empty but
            // readable directory is a success there and is a success here.
            if (!System.IO.Directory.Exists(directory))
            {
                Flower.Warn.Error("failed adding font directory: " + directory);
            }

            TextFontChain.AddDocumentFontDirectory(directory);
            Flower.Warn.Debug("Adding font directory: " + directory);
            return Unspecified.Instance;
        });

        InstallFontWorldQueries(interpreter);
    }

    /// <summary>
    /// The two entry points that ANSWER FOR THE PORT'S OWN FONT WORLD rather than for a
    /// host fontconfig — <c>ly:font-config-get-font-file</c> and
    /// <c>ly:font-config-display-fonts</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ RULING R18 (Jeremy, 2026-08-17), AND IT IS THE PROJECT'S ONLY DELIBERATE
    /// EXCEPTION TO RULE 2 — these are to do something SIMILAR to upstream, not identical.
    /// Upstream reports the HOST's fontconfig view; the port has no host font world to
    /// report and D23 forbids ever acquiring one, so reporting its OWN world is the whole
    /// point. Do not "correct" either of these toward upstream's output later.
    /// </para>
    /// <para>
    /// Nothing depends on the answers, which is why a divergence is affordable HERE and
    /// nowhere else: measured before the ruling,
    /// <c>ly:font-config-get-font-file</c> has ZERO callers anywhere — not the vendored
    /// Scheme layer, not the 2,146-file corpus, not upstream's own tree outside its
    /// definition — and <c>ly:font-config-display-fonts</c> has ONE, <c>lily.scm:1044</c>,
    /// gated on the <c>show-available-fonts</c> option, which prints and exits.
    /// </para>
    /// <para>
    /// Two divergences from upstream's letter are deliberate and recorded in
    /// PORT-COVERAGE: a name that matches nothing answers <c>#f</c> where upstream's
    /// <c>All_font_metrics::get_font_file</c> answers the EMPTY STRING, and the listing is
    /// the port's 24 vendored faces plus this document's own rather than a fontconfig dump.
    /// </para>
    /// </remarks>
    /// <param name="interpreter">The interpreter to install into.</param>
    private static void InstallFontWorldQueries(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:font-config-get-font-file", 1, 1, a =>
        {
            string name = StringPrimitives.Text(a[0], "ly:font-config-get-font-file");

            // The DOCUMENT's own fonts first, exactly as TextFontChain.For consults them
            // before the generic-family walk: a document that supplies a face has said
            // which file it means, so the real path on disk is the honest answer.
            TextFace supplied = TextFontChain.DocumentFont(name);
            if (supplied != null && supplied.SourcePath != null)
            {
                return new MutableString(supplied.SourcePath);
            }

            string vendored = TextFontChain.VendoredFaceLocation(name);
            return vendored == null ? (object)false : new MutableString(vendored);
        });

        interpreter.DefinePrimitive("ly:font-config-display-fonts", 0, 1, a =>
        {
            object port = a.Length > 0 ? a[0] : null;
            WriteFontWorld(interpreter, port);
            return Unspecified.Instance;
        });
    }

    /// <summary>
    /// Writes the port's font world to a port — the vendored faces, then whatever the
    /// document registered.
    /// </summary>
    /// <param name="interpreter">The interpreter, for the default error port.</param>
    /// <param name="port">The port to write to, or <see langword="null"/> for the default.</param>
    private static void WriteFontWorld(Interpreter interpreter, object port)
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder();

        IReadOnlyList<TextFace> vendored = TextFontChain.VendoredFaces();
        text.Append("vendored faces (")
            .Append(vendored.Count)
            .Append("):")
            .Append('\n');
        foreach (TextFace face in vendored)
        {
            text.Append("  ")
                .Append(face.FamilyName)
                .Append(" -- ")
                .Append(FontAssets.TextFontLocation(face.FileName))
                .Append('\n');
        }

        IReadOnlyList<KeyValuePair<string, TextFace>> supplied
            = TextFontChain.DocumentFontRegistrations();
        text.Append("document-supplied fonts (")
            .Append(supplied.Count)
            .Append("):")
            .Append('\n');
        foreach (KeyValuePair<string, TextFace> entry in supplied)
        {
            text.Append("  ")
                .Append(entry.Key)
                .Append(" -- ")
                .Append(entry.Value.SourcePath)
                .Append('\n');
        }

        // The optional-port argument and its (current-error-port) default are upstream's
        // and cost nothing, so they are kept.
        System.IO.TextWriter writer = port is SchemeOutputPort target
            ? target.Writer
            : interpreter.ErrorWriter;
        writer.Write(text.ToString());
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

        // ⚠ NOT A HOST LOOKUP, DESPITE THE NAME. This entry point was filed as a D25
        // N/A on the reading that "system font" meant the host's font configuration and
        // that D23 therefore forbade it. That reading was WRONG, and it was taken from
        // the primitive's NAME rather than from what it does.
        //
        // Upstream (all-font-metrics-scheme.cc:46) is all_fonts_global->find_otf_font,
        // and find_otf_font (all-font-metrics.cc:163) is a FILE search:
        // search_path_.find (name + ".otf") over LilyPond's own data directory — the
        // fonts LilyPond SHIPS. Its own documentation says as much: "Fonts loaded with
        // this command must contain two additional SFNT font tables called LILC and
        // LILY... Currently, only the Emmentaler and the Emmentaler-Brace fonts fulfill
        // these requirements." Those are this port's vendored assets, read here by the
        // same name, through the same cache, as every music glyph the port has ever
        // engraved (FontInterface goes through AllFontMetrics.FindOtfFont too).
        //
        // D23 is untouched by this. Its prohibition — amended and restated by R16 on
        // 2026-08-17 — is on falling back to fonts the port assumes the MACHINE has,
        // and it governs the TEXT chain (URW face -> TeX Gyre face -> tofu). No host
        // lookup is added here, because there was never one to add.
        //
        // Found by wave LD3 of Phase 5: the notation manual's "Modern glyph charts"
        // appendix is 26 snippets that all \include en/included/font-table.ly, whose
        // third line is (ly:system-font-load "emmentaler-20"). Ruled by Jeremy
        // 2026-08-19.
        interpreter.DefinePrimitive("ly:system-font-load", 1, 1, a =>
        {
            string name = StringPrimitives.Text(a[0], "ly:system-font-load");
            OpenTypeFontMetric font = AllFontMetrics.FindOtfFont(name);
            if (font == null)
            {
                // Upstream ERRORS here rather than answering false, and the difference
                // is load-bearing: the caller's next move is ly:otf-glyph-list, so a
                // null would surface as a wrong-type argument naming a different
                // procedure. AllFontMetrics.FindOtfFont has already WARNED by this
                // point — that is its contract for the internal path, where a missing
                // font is recoverable — so the two messages together say what upstream's
                // single error says.
                Flower.Warn.Error("cannot find font '" + name + "'");
            }

            return font;
        });
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
