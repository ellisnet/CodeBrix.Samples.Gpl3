/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2000--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Fonts; //was previously: lily/font-interface.cc, lily/font-select.cc, lily/paper-def.cc (find_scaled_font, output_scale);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// How a grob gets the font it draws itself with.
/// <para>
/// A grob does not name a font. It carries a <c>font-family</c>, a
/// <c>font-encoding</c> and a <c>font-size</c> step, and the layout carries the staff
/// height and the family-to-name mapping. Putting those together is what this does,
/// and the answer is cached on the grob's <c>font</c> property so the work happens once.
/// </para>
/// <para>
/// DIVERGENCE, recorded in PORT-COVERAGE: only the MUSIC encodings are resolved.
/// Upstream's <c>latin1</c> and <c>fetaText</c> branches go through Pango, which is
/// not in the port's dependency set — text is the CodeBrix.Platform TextLayout add-in's
/// job. Asking for one warns and answers null rather than quietly substituting a music
/// font, because a silently wrong font is a whole score laid out slightly wrong.
/// </para>
/// </summary>
public static class FontInterface
{
    private static readonly Symbol FontSymbol = Symbol.Intern("font");
    private static readonly Symbol FontsSymbol = Symbol.Intern("fonts");
    private static readonly Symbol FontNameSymbol = Symbol.Intern("font-name");
    private static readonly Symbol FontFamilySymbol = Symbol.Intern("font-family");
    private static readonly Symbol FontEncodingSymbol = Symbol.Intern("font-encoding");
    private static readonly Symbol FontSizeSymbol = Symbol.Intern("font-size");
    private static readonly Symbol MusicSymbol = Symbol.Intern("music");
    private static readonly Symbol SerifSymbol = Symbol.Intern("serif");
    private static readonly Symbol SansSymbol = Symbol.Intern("sans");
    private static readonly Symbol TypewriterSymbol = Symbol.Intern("typewriter");
    private static readonly Symbol FetaMusicSymbol = Symbol.Intern("fetaMusic");
    private static readonly Symbol FetaBracesSymbol = Symbol.Intern("fetaBraces");
    private static readonly Symbol FetaTextSymbol = Symbol.Intern("fetaText");
    private static readonly Symbol Latin1Symbol = Symbol.Intern("latin1");
    private static readonly Symbol StaffHeightSymbol = Symbol.Intern("staff-height");
    private static readonly Symbol OutputScaleSymbol = Symbol.Intern("output-scale");
    private static readonly Symbol FetaDesignSizeMapping = Symbol.Intern("feta-design-size-mapping");

    // The layout's cache of scaled fonts, keyed by the font it scales. Upstream keeps
    // this in a Scheme hash table hanging off the topmost Output_def; the port keeps a
    // table per definition object, reached the same way -- by walking to the top first.
    private static readonly Dictionary<OutputDef, Dictionary<(FontMetric, double), FontMetric>>
        ScaledFonts = new Dictionary<OutputDef, Dictionary<(FontMetric, double), FontMetric>>();

    /// <summary>
    /// Returns the font a grob draws itself with, resolving and caching it on first ask.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The font, or <see langword="null"/> when none could be resolved.</returns>
    public static FontMetric GetDefaultFont(Grob grob)
    {
        if (grob == null)
        {
            throw new ArgumentNullException(nameof(grob));
        }

        if (grob.GetProperty(FontSymbol) is FontMetric already)
        {
            return already;
        }

        FontMetric font = SelectFont(grob.Layout, MusicFontAlistChain(grob));
        if (font != null)
        {
            grob.SetProperty(FontSymbol, font);
        }

        return font;
    }

    /// <summary>
    /// Returns a grob's property alist chain with the music defaults appended, which is
    /// what makes an unqualified grob resolve to the music font.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The chain.</returns>
    public static object MusicFontAlistChain(Grob grob)
        => grob.GetPropertyAlistChain(Pair.List(
            new Pair(FontFamilySymbol, MusicSymbol),
            new Pair(FontEncodingSymbol, FetaMusicSymbol)));

    /// <summary>
    /// Returns a grob's property alist chain with the text defaults appended.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The chain.</returns>
    public static object TextFontAlistChain(Grob grob)
        => grob.GetPropertyAlistChain(Pair.List(new Pair(FontEncodingSymbol, Latin1Symbol)));

    /// <summary>
    /// Chooses a font for a property chain, under a layout.
    /// </summary>
    /// <param name="layout">The output definition, which carries the staff height.</param>
    /// <param name="chain">The property alist chain.</param>
    /// <returns>The font, or <see langword="null"/> for an unsupported encoding.</returns>
    public static FontMetric SelectFont(OutputDef layout, object chain)
    {
        if (layout == null)
        {
            Warn.ProgrammingError("cannot select a font without a layout");
            return null;
        }

        object fonts = SchemeUtilities.ChainAssocGet(FontsSymbol, chain, Nil.Instance);
        object stringDescription = SchemeUtilities.ChainAssocGet(FontNameSymbol, chain, false);
        object encoding = SchemeUtilities.ChainAssocGet(FontEncodingSymbol, chain, false);

        if (stringDescription is MutableString || stringDescription is string)
        {
            encoding = Latin1Symbol;
        }
        else if (!ReferenceEquals(encoding, FetaMusicSymbol)
                 && !ReferenceEquals(encoding, FetaBracesSymbol)
                 && !ReferenceEquals(encoding, FetaTextSymbol)
                 && !ReferenceEquals(encoding, Latin1Symbol))
        {
            Warn.Warning(
                "font-encoding is invalid, should be 'fetaMusic, 'fetaBraces,"
                + " 'fetaText or 'latin1: " + Describe(encoding));
            Warn.Warning("falling back to latin1");
            encoding = Latin1Symbol;
        }

        if (!IsMusicEncoding(encoding))
        {
            // See the class remark: the text path is Pango's upstream and the
            // TextLayout add-in's here. Refusing is the honest answer.
            Warn.Warning(
                "text fonts are not resolved by the engine port; requested encoding "
                + Describe(encoding));
            return null;
        }

        // A music font's base size comes from the staff height, in points.
        double baseSize = layout.GetDimension(StaffHeightSymbol) / Dimensions.Point;

        object step = SchemeUtilities.ChainAssocGet(FontSizeSymbol, chain, false);
        double requestedStep = SchemeConvert.IsNumber(step)
            ? SchemeConvert.ToDouble(step, "font-size")
            : 0.0;
        double requestedSize = baseSize * Math.Pow(2.0, requestedStep / 6.0);

        object family = SchemeUtilities.ChainAssocGet(FontFamilySymbol, chain, false);
        if (!(family is Symbol)
            || ReferenceEquals(family, SerifSymbol)
            || ReferenceEquals(family, SansSymbol)
            || ReferenceEquals(family, TypewriterSymbol))
        {
            // This is ugly and should be improved. It happens with things like
            // \markup \sans { piu \dynamic p }
            family = MusicSymbol;
        }

        Pair nameEntry = SchemeUtilities.Assq(family, fonts);
        string name = nameEntry == null ? null : TextOf(nameEntry.Cdr);
        if (name == null)
        {
            // Upstream falls back to a TEXT font here, which the port cannot use. The
            // music family is the only honest fallback, and it is what the default
            // \paper supplies anyway.
            name = "emmentaler";
        }

        double actualSize = requestedSize;
        if (ReferenceEquals(encoding, FetaBracesSymbol))
        {
            name += "-brace";

            // The brace font is drawn at one size; upstream still scales it against the
            // requested size through the same path, using its own design size.
            OpenTypeFontMetric braceFont = AllFontMetrics.FindOtfFont(name);
            return braceFont == null
                ? null
                : FindScaledFont(layout, braceFont, requestedSize / braceFont.DesignSize);
        }

        int roundedSize = BestRoundedDesignSize(requestedSize, out actualSize);
        name += "-" + roundedSize.ToString(CultureInfo.InvariantCulture);

        OpenTypeFontMetric font = AllFontMetrics.FindOtfFont(name);
        if (font == null)
        {
            return null;
        }

        return FindScaledFont(layout, font, requestedSize / actualSize);
    }

    /// <summary>
    /// Returns the design size the Emmentaler is actually drawn at that is closest to a
    /// requested size, and the file-name suffix that goes with it.
    /// <para>
    /// The font comes in eight discrete sizes; <c>feta-design-size-mapping</c> in
    /// <c>lily-library.scm</c> maps each file suffix to the precise size that file was
    /// drawn at — 18 means 17.82, not 18. Closeness is measured as a RATIO rather than
    /// a difference, so the choice is scale-invariant.
    /// </para>
    /// </summary>
    /// <param name="requestedSize">The size wanted, in points.</param>
    /// <param name="actualSize">Receives the precise design size of the chosen file.</param>
    /// <returns>The rounded size, which is the file-name suffix.</returns>
    public static int BestRoundedDesignSize(double requestedSize, out double actualSize)
    {
        double minimumRatio = double.PositiveInfinity;
        int bestRounded = 0;
        actualSize = 0;

        foreach ((int Rounded, double Actual) entry in DesignSizeMapping())
        {
            double ratio = requestedSize > entry.Actual
                ? requestedSize / entry.Actual
                : entry.Actual / requestedSize;

            if (ratio < minimumRatio)
            {
                minimumRatio = ratio;
                bestRounded = entry.Rounded;
                actualSize = entry.Actual;
            }
        }

        return bestRounded;
    }

    /// <summary>
    /// Returns a font scaled for a layout, reusing the instance when one already exists.
    /// </summary>
    /// <param name="layout">The output definition.</param>
    /// <param name="font">The font to scale.</param>
    /// <param name="magnification">The magnification wanted, before the output scale.</param>
    /// <returns>The scaled font.</returns>
    public static FontMetric FindScaledFont(OutputDef layout, FontMetric font, double magnification)
    {
        if (layout == null || font == null)
        {
            return font;
        }

        // Always resolved against the TOPMOST definition, so a \layout and the \paper
        // above it share one table and therefore one instance per size.
        OutputDef top = layout;
        while (top.Parent != null)
        {
            top = top.Parent;
        }

        double outputScale = top.GetDimension(OutputScaleSymbol);
        double lookupMagnification = outputScale != 0.0 ? magnification / outputScale : magnification;

        if (!ScaledFonts.TryGetValue(top, out Dictionary<(FontMetric, double), FontMetric> table))
        {
            table = new Dictionary<(FontMetric, double), FontMetric>();
            ScaledFonts[top] = table;
        }

        if (table.TryGetValue((font, lookupMagnification), out FontMetric cached))
        {
            return cached;
        }

        FontMetric scaled = new ModifiedFontMetric(font, lookupMagnification);
        table[(font, lookupMagnification)] = scaled;
        return scaled;
    }

    /// <summary>Discards every layout's scaled-font table.</summary>
    public static void ResetScaledFonts() => ScaledFonts.Clear();

    private static bool IsMusicEncoding(object encoding)
        => ReferenceEquals(encoding, FetaMusicSymbol)
           || ReferenceEquals(encoding, FetaBracesSymbol)
           || ReferenceEquals(encoding, FetaTextSymbol);

    private static IEnumerable<(int Rounded, double Actual)> DesignSizeMapping()
    {
        Variable variable = LilyPondScheme.Current?.CurrentModule?.Lookup(FetaDesignSizeMapping);
        object mapping = variable != null && variable.IsBound ? variable.GetValue() : null;

        List<(int, double)> entries = new List<(int, double)>();
        object cursor = mapping;
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry
                && SchemeConvert.IsNumber(entry.Car)
                && SchemeConvert.IsNumber(entry.Cdr))
            {
                entries.Add((
                    SchemeConvert.ToInt(entry.Car, "feta-design-size-mapping"),
                    SchemeConvert.ToDouble(entry.Cdr, "feta-design-size-mapping")));
            }

            cursor = pair.Cdr;
        }

        if (entries.Count == 0)
        {
            // The Scheme layer has not loaded, so fall back on the same table
            // lily-library.scm carries. Kept as a literal ONLY as a fallback: the
            // vendored value is the authority whenever it is reachable.
            entries.AddRange(new[]
            {
                (11, 11.22), (13, 12.60), (14, 14.14), (16, 15.87),
                (18, 17.82), (20, 20.0), (23, 22.45), (26, 25.20),
            });
        }

        return entries;
    }

    private static string TextOf(object value)
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

    private static string Describe(object value) => value == null ? "#f" : value.ToString();
}
