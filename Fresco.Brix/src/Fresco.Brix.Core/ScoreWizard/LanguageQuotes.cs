// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/lasptyqu.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>An opening and a closing quotation mark.</summary>
public sealed class QuotePair
{
    /// <summary>Initializes the pair.</summary>
    /// <param name="left">The opening mark.</param>
    /// <param name="right">The closing mark.</param>
    public QuotePair(string left, string right)
    {
        Left = left;
        Right = right;
    }

    /// <summary>Gets the opening mark.</summary>
    public string Left { get; }

    /// <summary>Gets the closing mark.</summary>
    public string Right { get; }
}

/// <summary>
/// A language's quotation marks: primary (the double-quote-like pair) and
/// secondary (the single-quote-like pair).
/// </summary>
public sealed class QuoteSet
{
    /// <summary>Initializes the set.</summary>
    /// <param name="primary">The primary pair.</param>
    /// <param name="secondary">The secondary pair.</param>
    public QuoteSet(QuotePair primary, QuotePair secondary)
    {
        Primary = primary;
        Secondary = secondary;
    }

    /// <summary>Gets the primary pair.</summary>
    public QuotePair Primary { get; }

    /// <summary>Gets the secondary pair.</summary>
    public QuotePair Secondary { get; }
}

/// <summary>
/// LAnguage-SPecific TYpographical QUotes: which quotation marks a language
/// writes, so that a title typed with plain ASCII quotes comes out right.
/// </summary>
/// <remarks>
/// //was previously: <c>lasptyqu.py</c>, the one module in Frescobaldi with an
/// incomprehensible name — kept here as a readable one, since nothing outside
/// the port would ever guess what the old one meant.
/// </remarks>
public static class LanguageQuotes
{
    private const string SettingsGroup = "typographical_quotes";

    private static readonly Dictionary<string, QuoteSet> Quotes = Build();

    /// <summary>Gets the language codes there are quotes for.</summary>
    /// <returns>The codes, sorted.</returns>
    public static IReadOnlyList<string> Available()
        => Quotes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    /// <summary>Gets the quotes of the neutral <c>C</c> locale.</summary>
    /// <returns>The quotes.</returns>
    public static QuoteSet Default() => Quotes["C"];

    /// <summary>Gets one language's quotes.</summary>
    /// <param name="language">The language code.</param>
    /// <returns>The quotes, or null when the language has none — a
    /// <c>xx_YY</c> code falls back to its <c>xx</c>.</returns>
    public static QuoteSet For(string language)
    {
        if (string.IsNullOrEmpty(language)) { return null; }

        if (Quotes.TryGetValue(language, out QuoteSet quotes)) { return quotes; }

        int underscore = language.IndexOf('_');
        if (underscore > 0
            && Quotes.TryGetValue(language.Substring(0, underscore), out quotes))
        {
            return quotes;
        }

        return null;
    }

    /// <summary>Gets the quotes the user wants; never null.</summary>
    /// <param name="settings">The settings store, or null for the defaults.</param>
    /// <returns>The quotes.</returns>
    /// <remarks>The store holds either <c>current</c> (follow the interface
    /// language), <c>custom</c> (four marks of the user's own) or a language
    /// code — upstream's three cases, in upstream's order.</remarks>
    public static QuoteSet Preferred(SettingsStore settings)
    {
        QuoteSet fallback = Default();
        if (settings == null) { return fallback; }

        string language = settings.GetString(SettingsGroup + "/language", "current");
        if (string.Equals(language, "custom", StringComparison.Ordinal))
        {
            return new QuoteSet(
                new QuotePair(
                    settings.GetString(
                        SettingsGroup + "/primary_left", fallback.Primary.Left),
                    settings.GetString(
                        SettingsGroup + "/primary_right", fallback.Primary.Right)),
                new QuotePair(
                    settings.GetString(
                        SettingsGroup + "/secondary_left", fallback.Secondary.Left),
                    settings.GetString(
                        SettingsGroup + "/secondary_right", fallback.Secondary.Right)));
        }

        if (string.Equals(language, "current", StringComparison.Ordinal))
        {
            language = I18n.Language;
        }

        return For(language) ?? fallback;
    }

    /// <summary>Builds the table.</summary>
    /// <returns>The table.</returns>
    private static Dictionary<string, QuoteSet> Build()
    {
        Dictionary<string, QuoteSet> quotes =
            new Dictionary<string, QuoteSet>(StringComparer.Ordinal);

        void Set(QuoteSet value, params string[] languages)
        {
            foreach (string language in languages) { quotes[language] = value; }
        }

        Set(
            new QuoteSet(
                new QuotePair("“", "”"),
                new QuotePair("‘", "’")),
            "C", "en", "nl", "tr");
        Set(
            new QuoteSet(
                new QuotePair("«", "»"),
                new QuotePair("‹", "›")),
            "es", "fr", "gl", "it");
        Set(
            new QuoteSet(
                new QuotePair("„", "“"),
                new QuotePair("‚", "‘")),
            "de");
        Set(
            new QuoteSet(
                new QuotePair("„", "”"),
                new QuotePair("«", "»")),
            "pl");
        Set(
            new QuoteSet(
                new QuotePair("«", "»"),
                new QuotePair("„", "“")),
            "ru", "uk");
        Set(
            new QuoteSet(
                new QuotePair("«", "»"),
                new QuotePair("“", "”")),
            "pt_BR");

        //Upstream also carries zh and ja. FR5.6 keeps the CJK languages out of
        //v1 (no font coverage, and no system-font fallback to hide it), but the
        //table is data, not interface, and a Chinese TITLE in an otherwise
        //English application should still be quoted the way Chinese quotes.
        Set(
            new QuoteSet(
                new QuotePair("『", "』"),
                new QuotePair("「", "」")),
            "zh", "ja");

        return quotes;
    }
}
