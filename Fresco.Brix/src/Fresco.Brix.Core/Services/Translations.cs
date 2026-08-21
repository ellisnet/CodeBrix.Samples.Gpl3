// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Fresco.Brix.Services; //was previously: frescobaldi/i18n/__init__.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A catalog of translated messages for one language, keyed exactly the way
/// gettext keys them: by the VERBATIM English message, optionally qualified by
/// a disambiguating context, with a separate plural form.
/// </summary>
public interface ITranslationCatalog
{
    /// <summary>Gets the language this catalog is for (e.g. <c>de</c>).</summary>
    string Language { get; }

    /// <summary>Translates a message.</summary>
    /// <param name="context">The disambiguating context, or null.</param>
    /// <param name="message">The verbatim English message.</param>
    /// <returns>The translation, or null when the catalog has none.</returns>
    string Lookup(string context, string message);

    /// <summary>Translates a message with a plural form.</summary>
    /// <param name="context">The disambiguating context, or null.</param>
    /// <param name="message">The verbatim English singular.</param>
    /// <param name="plural">The verbatim English plural.</param>
    /// <param name="count">The count deciding the form.</param>
    /// <returns>The translation, or null when the catalog has none.</returns>
    string LookupPlural(string context, string message, string plural, long count);
}

/// <summary>
/// A translation function: a <c>_()</c> that can be handed around.
/// </summary>
/// <param name="context">The disambiguating context, or null.</param>
/// <param name="message">The verbatim English message.</param>
/// <returns>The translation, or the message itself.</returns>
/// <remarks>
/// Upstream passes <c>_</c> itself as an argument — a part type's
/// <c>title(_=translate)</c> is called with the application's translator
/// normally and with a DIFFERENT language's translator when the score wizard
/// is set to write instrument names in a chosen language.
/// </remarks>
public delegate string Translator(string context, string message);

/// <summary>
/// The application's translation lookup — the <c>_()</c> of the port.
/// <para>
/// Every user-visible string in Fresco.Brix goes through here keyed by the
/// verbatim upstream English message (standing rule 7), so the W-I18N harvest
/// tool can match our strings against Frescobaldi's own catalogs. Until a
/// catalog is installed — and always for our own divergent strings — the
/// English message is returned unchanged.
/// </para>
/// </summary>
public static class I18n
{
    /// <summary>
    /// Gets or sets the installed catalog. Null (the default) means English.
    /// </summary>
    /// <remarks>Upstream this is <c>builtins._</c>, replaced by
    /// <c>i18n.install(language)</c>.</remarks>
    public static ITranslationCatalog Catalog { get; set; }

    /// <summary>Gets the current language, or <c>en</c> when untranslated.</summary>
    public static string Language => Catalog?.Language ?? "en";

    /// <summary>Raised after <see cref="Install"/> changes the catalog, so
    /// open windows can re-translate themselves.</summary>
    /// <remarks>Upstream this is <c>app.languageChanged</c>.</remarks>
    public static event EventHandler LanguageChanged;

    /// <summary>Installs a catalog (null for English) and announces it.</summary>
    /// <param name="catalog">The catalog, or null.</param>
    public static void Install(ITranslationCatalog catalog)
    {
        Catalog = catalog;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Gets the translator that reads the installed catalog.</summary>
    /// <remarks>This is upstream's <c>_</c> as a value rather than a call.</remarks>
    public static Translator Current => Get;

    /// <summary>Answers a translator for one named language.</summary>
    /// <param name="language">The language code, <c>C</c> for untranslated
    /// English, or null/empty for whatever is installed.</param>
    /// <returns>The translator.</returns>
    /// <remarks>
    /// Upstream's <c>i18n.translator(lang)</c>, which loads that language's
    /// catalog and hands back its <c>gettext</c>. Until W-I18N brings the
    /// catalogs there is only English to hand back, so every answer but the
    /// installed one is English — which is exactly what the score wizard's
    /// instrument-name language setting then produces.
    /// </remarks>
    public static Translator TranslatorFor(string language)
    {
        if (string.IsNullOrEmpty(language)) { return Current; }

        if (string.Equals(language, Language, StringComparison.Ordinal))
        {
            return Current;
        }

        return static (_, message) => message;
    }

    /// <summary>Translates a message.</summary>
    /// <param name="message">The verbatim English message.</param>
    /// <returns>The translation, or the message itself.</returns>
    public static string Get(string message)
        => Catalog?.Lookup(null, message) ?? message;

    /// <summary>Translates a message in a disambiguating context.</summary>
    /// <param name="context">The context, e.g. <c>menu title</c>.</param>
    /// <param name="message">The verbatim English message.</param>
    /// <returns>The translation, or the message itself.</returns>
    public static string Get(string context, string message)
        => Catalog?.Lookup(context, message) ?? message;

    /// <summary>Translates a message with a plural form.</summary>
    /// <param name="message">The verbatim English singular.</param>
    /// <param name="plural">The verbatim English plural.</param>
    /// <param name="count">The count deciding the form.</param>
    /// <returns>The translation, or the English form for the count.</returns>
    public static string Get(string message, string plural, long count)
        => Catalog?.LookupPlural(null, message, plural, count)
            ?? (count == 1 ? message : plural);

    /// <summary>Translates a contextual message with a plural form.</summary>
    /// <param name="context">The context.</param>
    /// <param name="message">The verbatim English singular.</param>
    /// <param name="plural">The verbatim English plural.</param>
    /// <param name="count">The count deciding the form.</param>
    /// <returns>The translation, or the English form for the count.</returns>
    public static string Get(string context, string message, string plural, long count)
        => Catalog?.LookupPlural(context, message, plural, count)
            ?? (count == 1 ? message : plural);

    /// <summary>
    /// Fills <c>{name}</c> placeholders in a translated message, the way
    /// upstream's <c>.format(name=…)</c> does.
    /// </summary>
    /// <param name="text">The translated message.</param>
    /// <param name="arguments">The placeholder name/value pairs.</param>
    /// <returns>The filled message; unknown placeholders are left alone.</returns>
    /// <remarks>
    /// Deliberately NOT <c>string.Format</c>: msgids carry NAMED placeholders
    /// (translators reorder them), and a stray <c>{</c> in a translation must
    /// not throw in front of the user.
    /// </remarks>
    public static string Format(
        string text, params (string Name, object Value)[] arguments)
    {
        if (string.IsNullOrEmpty(text) || arguments == null || arguments.Length == 0)
        {
            return text;
        }

        Dictionary<string, object> values =
            new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var argument in arguments)
        {
            values[argument.Name] = argument.Value;
        }

        StringBuilder result = new StringBuilder(text.Length);
        int index = 0;
        while (index < text.Length)
        {
            int open = text.IndexOf('{', index);
            if (open < 0)
            {
                result.Append(text, index, text.Length - index);
                break;
            }

            result.Append(text, index, open - index);
            int close = text.IndexOf('}', open + 1);
            if (close < 0)
            {
                result.Append(text, open, text.Length - open);
                break;
            }

            string name = text.Substring(open + 1, close - open - 1);
            if (values.TryGetValue(name, out var value))
            {
                result.Append(Convert.ToString(value, CultureInfo.CurrentCulture));
            }
            else
            {
                //An unknown placeholder stays verbatim — a translator typo
                //must never blank out a label.
                result.Append(text, open, close - open + 1);
            }

            index = close + 1;
        }

        return result.ToString();
    }
}
