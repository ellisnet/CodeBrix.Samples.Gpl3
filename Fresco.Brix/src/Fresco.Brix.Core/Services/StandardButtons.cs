// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;

namespace Fresco.Brix.Services; //was previously: i18n/messages.py (the Qt half)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The captions of the standard dialog buttons — OK, Cancel and the rest —
/// keyed exactly the way Frescobaldi's translators key them.
/// </summary>
/// <remarks>
/// <para>
/// Upstream never writes these captions: it asks Qt for a standard button and
/// Qt fills the text in, translating it through the running
/// <c>QTranslator</c>. Frescobaldi installs one of its own
/// (<c>i18n/qtranslator.py</c>) whose <c>translate(context, sourceText)</c> is
/// the application's <c>_()</c>, and it lists the strings Qt will ask for in
/// <c>i18n/messages.py</c> so <c>xgettext</c> puts them in the catalogs. They
/// therefore ARE upstream msgids, and they carry the CONTEXT Qt asks with.
/// </para>
/// <para>
/// The context used here is <c>QPlatformTheme</c>, which is the one a Qt 6
/// dialog button box really asks with
/// (<c>QPlatformTheme::defaultStandardButtonText</c>); <c>QDialogButtonBox</c>
/// is in the catalogs too, for the same texts, and is the fallback when a
/// caption exists only there.
/// </para>
/// <para>
/// A translation of one of these may carry an accelerator marker of its own —
/// German's "Schlie&amp;ßen" does — so every caption is stripped at the point
/// of display, which is board trap 18/50's rule for button captions.
/// </para>
/// </remarks>
public static class StandardButtons
{
    /// <summary>Gets the OK button's caption.</summary>
    public static string Ok => Caption(I18n.Get("QPlatformTheme", "OK"));

    /// <summary>Gets the Cancel button's caption.</summary>
    public static string Cancel => Caption(I18n.Get("QPlatformTheme", "Cancel"));

    /// <summary>Gets the Close button's caption.</summary>
    public static string Close => Caption(I18n.Get("QPlatformTheme", "Close"));

    /// <summary>Gets the Save button's caption.</summary>
    public static string Save => Caption(I18n.Get("QPlatformTheme", "Save"));

    /// <summary>Gets the Apply button's caption.</summary>
    public static string Apply => Caption(I18n.Get("QPlatformTheme", "Apply"));

    /// <summary>Gets the Reset button's caption.</summary>
    public static string Reset => Caption(I18n.Get("QPlatformTheme", "Reset"));

    /// <summary>Gets the Overwrite button's caption.</summary>
    /// <remarks>
    /// Not one of Qt's standard buttons: upstream re-captions its Discard
    /// button with <c>_("Overwrite")</c> in the session editor, and that msgid
    /// is in every catalog.
    /// //was previously (this session, briefly): a <c>Yes</c> caption on
    /// <c>QPlatformTheme</c>, which upstream's template-overwrite question
    /// uses. Frescobaldi's own catalogs carry only the ten QPlatformTheme
    /// strings its <c>i18n/messages.py</c> lists, and "Yes" is not one of them
    /// — Qt translates that one from ITS catalogs, which this application does
    /// not ship. "Overwrite" says what the button does and IS translated.
    /// </remarks>
    public static string Overwrite => Caption(I18n.Get("Overwrite"));

    /// <summary>Gets the Discard button's caption.</summary>
    public static string Discard => Caption(I18n.Get("QPlatformTheme", "Discard"));

    /// <summary>Gets the Help button's caption.</summary>
    public static string Help => Caption(I18n.Get("QPlatformTheme", "Help"));

    /// <summary>Gets the Restore Defaults button's caption.</summary>
    public static string RestoreDefaults
        => Caption(I18n.Get("QPlatformTheme", "Restore Defaults"));

    /// <summary>Strips a caption's accelerator marker for display.</summary>
    /// <param name="text">The translated caption.</param>
    /// <returns>The caption as it is shown.</returns>
    private static string Caption(string text)
        => ActionCollectionManager.RemoveAccelerator(text);
}
