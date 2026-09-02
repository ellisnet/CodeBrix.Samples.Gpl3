// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Fresco.Brix.Services;
//was previously: frescobaldi/i18n/__init__.py and frescobaldi/i18n/setup.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Raised when a language is asked for that has no catalog.
/// </summary>
/// <remarks>Upstream's <c>UnknownLanguageError</c>.</remarks>
public sealed class UnknownLanguageException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="language">The language that was asked for.</param>
    public UnknownLanguageException(string language)
        : base("No translation is installed for '" + language + "'.")
        => Language = language;

    /// <summary>Gets the language that was asked for.</summary>
    public string Language { get; }
}

/// <summary>
/// A catalog read out of a compiled <c>.mo</c> file, presented to the
/// application's lookup.
/// </summary>
/// <remarks>
/// The four methods <see cref="ITranslationCatalog"/> asks for are upstream's
/// four <c>*gettext</c> methods; a message the catalog does not carry comes
/// back untranslated through <see cref="MoFile"/>'s own fallback, which is
/// what <c>I18n.Get</c> would have done for itself.
/// </remarks>
public sealed class MoCatalog : ITranslationCatalog
{
    /// <summary>
    /// The two product names a translation may not put on screen unless the
    /// English message put them there first.
    /// </summary>
    /// <remarks>See <see cref="Guard"/>.</remarks>
    public static readonly IReadOnlyList<string> ForbiddenNames
        = new[] { "Frescobaldi", "LilyPond" };

    private readonly MoFile _catalog;

    /// <summary>Creates the catalog.</summary>
    /// <param name="language">The language code, e.g. <c>de</c>.</param>
    /// <param name="catalog">The compiled catalog.</param>
    public MoCatalog(string language, MoFile catalog)
    {
        Language = language;
        _catalog = catalog;
    }

    /// <inheritdoc/>
    public string Language { get; }

    /// <summary>Gets the compiled catalog behind this one.</summary>
    public MoFile File => _catalog;

    /// <inheritdoc/>
    public string Lookup(string context, string message)
        => Guard(
            message,
            context == null
                ? _catalog.Gettext(message)
                : _catalog.Pgettext(context, message));

    /// <inheritdoc/>
    public string LookupPlural(string context, string message, string plural, long count)
    {
        string english = count == 1 ? message : plural;
        return Guard(
            english,
            context == null
                ? _catalog.Ngettext(message, plural, count)
                : _catalog.Npgettext(context, message, plural, count));
    }

    /// <summary>
    /// Refuses a translation that names Frescobaldi or LilyPond where the
    /// English message does not.
    /// </summary>
    /// <param name="message">The English message.</param>
    /// <param name="translation">What the catalog answered.</param>
    /// <returns>The translation, or the English message when the translation
    /// would put a forbidden name on screen.</returns>
    /// <remarks>
    /// <para>
    /// ⚠ RULINGS FR9 AND FR13, ENFORCED AT THE ONE PLACE EVERY USER-VISIBLE
    /// STRING PASSES THROUGH. Fresco.Brix presents as its own application
    /// (FR9) and no UI element names LilyPond (FR13) — and the code obeys
    /// both, because every msgid a ruling touched was rewritten and is
    /// recorded in <c>tools/i18nharvest/renamed-strings.tsv</c>. A TRANSLATION
    /// can still break them: a translator writing for Frescobaldi may put the
    /// product name into a message whose English has none, which is correct
    /// for that application and wrong for this one.
    /// </para>
    /// <para>
    /// Six strings across the thirteen shipped catalogs do exactly that (three
    /// Russian, two Italian, one German) — for instance Russian translates the
    /// bare word "About" as "О Frescobaldi", which would label a tab of THIS
    /// application's About window with ANOTHER application's name. They are
    /// refused here and shown in English.
    /// </para>
    /// <para>
    /// The catalogs themselves are never edited: they are third-party work
    /// with their translators' names in them (THIRD-PARTY-NOTICES.txt section
    /// 2.3), and correcting someone's translation in place would be both rude
    /// and a licence question. Refusing to DISPLAY six of their strings is the
    /// smaller and honester act.
    /// </para>
    /// </remarks>
    public static string Guard(string message, string translation)
    {
        if (string.IsNullOrEmpty(translation)
            || ReferenceEquals(message, translation))
        {
            return translation;
        }

        foreach (var name in ForbiddenNames)
        {
            if (translation.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                && (message == null
                    || message.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0))
            {
                return message;
            }
        }

        return translation;
    }
}

/// <summary>
/// Finds the interface languages this application was built with, and sets the
/// one the user asked for.
/// </summary>
/// <remarks>
/// <para>
/// //was previously: <c>frescobaldi/i18n/__init__.py</c> (<c>available</c>,
/// <c>translator</c>, <c>install</c>) and <c>frescobaldi/i18n/setup.py</c>
/// (<c>preferred</c>, <c>default</c>, <c>current</c>, <c>_setup</c>).
/// Upstream keeps its compiled catalogs beside its <c>i18n</c> package and
/// finds them by walking for <c>LC_MESSAGES</c> directories; this keeps the
/// same layout under the application's own asset folder, so the folder can be
/// emptied and the application runs in English (board rule 13's droppability,
/// the same as the manuals and the dictionaries).
/// </para>
/// <para>
/// ⚠ ONE DELIBERATE DIVERGENCE OF MECHANISM. Upstream applies a language
/// change AT ONCE: every Qt widget it builds is registered with
/// <c>app.translateUI</c> and re-translates itself when
/// <c>app.languageChanged</c> fires. A CodeBrix.Platform window is built once
/// and holds its own strings, so a change here takes effect at the NEXT
/// launch, and the General preferences page says so. The
/// <see cref="I18n.LanguageChanged"/> event is kept — it is upstream's own
/// signal, and it is what the Score Wizard's instrument-name language listens
/// to — but nothing re-builds the shell behind the user's back.
/// </para>
/// </remarks>
public static class LanguageSetup
{
    /// <summary>The languages this application was built with (ruling FR5.6).</summary>
    /// <remarks>
    /// The thirteen European-script catalogs Frescobaldi ships. The five CJK
    /// catalogs (<c>ja</c>, <c>ko</c>, <c>zh_CN</c>, <c>zh_HK</c>,
    /// <c>zh_TW</c>) are deliberately NOT here: they need fonts the
    /// application does not carry and an input method the editor does not have
    /// yet.
    /// </remarks>
    public static readonly IReadOnlyList<string> Languages = new[]
    {
        "cs", "de", "es", "fr", "gl", "it", "nl", "pl", "pt_BR", "ru", "sv", "tr", "uk",
    };

    /// <summary>The catalog domain, which is also the file name.</summary>
    /// <remarks>Upstream's <c>domain="frescobaldi"</c>. The catalogs ARE
    /// Frescobaldi's, translator credits and all, so the domain stays its own
    /// (see THIRD-PARTY-NOTICES.txt section 2.3).</remarks>
    public const string Domain = "frescobaldi";

    private static readonly Dictionary<string, ITranslationCatalog> Loaded
        = new Dictionary<string, ITranslationCatalog>(StringComparer.Ordinal);

    private static string _currentLanguage;

    /// <summary>Gets the folder the catalogs were installed in.</summary>
    /// <remarks>Upstream's <c>modir</c>: the directory the <c>i18n</c> package
    /// sits in.</remarks>
    public static string CatalogDirectory
        => Path.Combine(AppContext.BaseDirectory, "assets", "i18n");

    /// <summary>Gets or sets where the catalogs are looked for.</summary>
    /// <remarks>A seam for the tests, which read the catalogs out of the build
    /// output rather than out of an installed application.</remarks>
    public static string CatalogDirectoryOverride { get; set; }

    /// <summary>
    /// Lists the languages a compiled catalog is installed for.
    /// </summary>
    /// <returns>The language codes, in order.</returns>
    /// <remarks>
    /// Upstream's <c>available()</c>, and its note holds here too: this is not
    /// the full list of languages the application offers, because English and
    /// the special language <c>C</c> are always there and have no catalog.
    /// </remarks>
    public static IReadOnlyList<string> Available()
    {
        string root = CatalogDirectoryOverride ?? CatalogDirectory;
        List<string> found = new List<string>();

        try
        {
            foreach (var directory in Directory.GetDirectories(root))
            {
                if (File.Exists(Path.Combine(directory, "LC_MESSAGES", Domain + ".mo")))
                {
                    found.Add(Path.GetFileName(directory));
                }
            }
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>Answers a translator for one language.</summary>
    /// <param name="language">The language code, or <c>C</c>/<c>en</c>/
    /// <c>en_XX</c> for untranslated English.</param>
    /// <returns>The translator.</returns>
    /// <exception cref="UnknownLanguageException">The language has no
    /// catalog.</exception>
    /// <remarks>Upstream's <c>translator(language, domain)</c>, including its
    /// rule that <c>de_DE</c> settles for <c>de</c>.</remarks>
    public static Translator TranslatorFor(string language)
    {
        ITranslationCatalog catalog = CatalogFor(language);
        if (catalog == null) { return static (_, message) => message; }

        return (context, message) => catalog.Lookup(context, message) ?? message;
    }

    /// <summary>Loads the catalog for one language.</summary>
    /// <param name="language">The language code.</param>
    /// <returns>The catalog, or null for untranslated English.</returns>
    /// <exception cref="UnknownLanguageException">The language has no
    /// catalog.</exception>
    /// <remarks>Upstream builds a <c>NullTranslations</c> for English and
    /// raises for anything with no catalog; the same two answers are here, as
    /// null and as the exception. A catalog is read once and kept.</remarks>
    public static ITranslationCatalog CatalogFor(string language)
    {
        if (IsEnglish(language)) { return null; }

        lock (Loaded)
        {
            if (Loaded.TryGetValue(language, out var known)) { return known; }
        }

        string file = FileFor(language);
        if (file == null) { throw new UnknownLanguageException(language); }

        MoCatalog catalog = new MoCatalog(language, MoFile.FromFile(file));
        lock (Loaded)
        {
            Loaded[language] = catalog;
        }

        return catalog;
    }

    /// <summary>Installs the translations for one language.</summary>
    /// <param name="language">The language code.</param>
    /// <exception cref="UnknownLanguageException">The language has no
    /// catalog.</exception>
    /// <remarks>Upstream's <c>install(language)</c>, which replaces
    /// <c>builtins._</c>.</remarks>
    public static void Install(string language)
    {
        I18n.Install(CatalogFor(language));
        _currentLanguage = language;
    }

    /// <summary>
    /// Lists the language codes the operating system prefers, most wanted
    /// first.
    /// </summary>
    /// <returns>The codes, their country separated by an underscore.</returns>
    /// <remarks>Upstream's <c>preferred()</c>, which asks Qt for
    /// <c>QLocale().uiLanguages()</c> and falls back to the C locale's default;
    /// the framework's own <see cref="CultureInfo.CurrentUICulture"/> and its
    /// parents are the same answer, with the same underscore spelling.</remarks>
    public static IReadOnlyList<string> Preferred()
    {
        List<string> languages = new List<string>();

        for (CultureInfo culture = CultureInfo.CurrentUICulture;
            culture != null && !string.IsNullOrEmpty(culture.Name);
            culture = culture.Parent)
        {
            string name = culture.Name.Replace('-', '_');
            if (!languages.Contains(name, StringComparer.Ordinal))
            {
                languages.Add(name);
            }

            if (ReferenceEquals(culture, culture.Parent)) { break; }
        }

        return languages;
    }

    /// <summary>
    /// Answers the first system language this application has, or <c>C</c>.
    /// </summary>
    /// <returns>The language code.</returns>
    /// <remarks>Upstream's <c>default()</c>. <c>C</c> means "none of the
    /// system's languages is available", which behaves like <c>en</c> but is
    /// not the user asking for English.</remarks>
    public static string Default()
    {
        List<string> available = new List<string>(Available()) { "en" };
        foreach (var language in Preferred())
        {
            if (available.Contains(language, StringComparer.Ordinal)
                || available.Contains(BaseOf(language), StringComparer.Ordinal))
            {
                return language;
            }
        }

        return "C";
    }

    /// <summary>Answers the interface language that is in force.</summary>
    /// <param name="settings">The store the preference lives in.</param>
    /// <returns>The language code; <c>C</c> or <c>en</c> mean untranslated.</returns>
    /// <remarks>Upstream's <c>current()</c>: the setting when it is set, and
    /// the system's default when it is empty.</remarks>
    public static string Current(SettingsStore settings)
    {
        string chosen = settings?.GetString("language");
        return string.IsNullOrEmpty(chosen) ? Default() : chosen;
    }

    /// <summary>
    /// Sets the application's language from the settings store.
    /// </summary>
    /// <param name="settings">The store.</param>
    /// <returns>The language that was installed; <c>C</c> when the one asked
    /// for has no catalog.</returns>
    /// <remarks>
    /// Upstream's <c>_setup()</c>. When the language has no catalog upstream
    /// shows an error box and carries on in English; here the same fall-back
    /// happens silently, because the only way to reach it is to have emptied
    /// the assets folder — the picker never offers a language that
    /// <see cref="Available"/> did not answer.
    /// </remarks>
    public static string Setup(SettingsStore settings)
    {
        string language = Current(settings);
        try
        {
            Install(language);
        }
        catch (UnknownLanguageException)
        {
            language = "C";
            Install(language);
        }
        catch (IOException)
        {
            language = "C";
            Install(language);
        }

        return language;
    }

    /// <summary>Gets the language installed by the last <see cref="Setup"/>.</summary>
    public static string CurrentLanguage => _currentLanguage;

    /// <summary>Forgets every catalog that has been read.</summary>
    /// <remarks>A seam for the tests; nothing in the application calls it.</remarks>
    public static void Reset()
    {
        lock (Loaded) { Loaded.Clear(); }

        _currentLanguage = null;
        I18n.Install(null);
    }

    /// <summary>Answers whether a language code means untranslated English.</summary>
    /// <param name="language">The code.</param>
    /// <returns>True for <c>C</c>, <c>en</c> and <c>en_XX</c>.</returns>
    /// <remarks>Upstream's own test, verbatim.</remarks>
    public static bool IsEnglish(string language)
        => string.IsNullOrEmpty(language)
            || string.Equals(language, "C", StringComparison.Ordinal)
            || string.Equals(language, "en", StringComparison.Ordinal)
            || string.Equals(BaseOf(language), "en", StringComparison.Ordinal);

    /// <summary>Finds the catalog file for a language, or null.</summary>
    /// <param name="language">The code.</param>
    /// <returns>The path, or null when there is none.</returns>
    /// <remarks>Upstream accepts either the code itself or its base — the rule
    /// that lets <c>de_DE</c> read the <c>de</c> catalog.</remarks>
    public static string FileFor(string language)
    {
        string root = CatalogDirectoryOverride ?? CatalogDirectory;
        foreach (var candidate in new[] { language, BaseOf(language) })
        {
            if (string.IsNullOrEmpty(candidate)) { continue; }

            string path = Path.Combine(root, candidate, "LC_MESSAGES", Domain + ".mo");
            if (File.Exists(path)) { return path; }
        }

        return null;
    }

    private static string BaseOf(string language)
    {
        if (string.IsNullOrEmpty(language)) { return language; }

        int underscore = language.IndexOf('_');
        return underscore < 0 ? language : language.Substring(0, underscore);
    }
}
