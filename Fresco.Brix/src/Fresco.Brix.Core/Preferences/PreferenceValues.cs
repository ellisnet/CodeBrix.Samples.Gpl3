// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documentation;
using Fresco.Brix.DocumentFonts;
using Fresco.Brix.Editor;
using Fresco.Brix.Midi;
using Fresco.Brix.ScoreWizard;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;

namespace Fresco.Brix.Preferences;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// What a preference page holds between the settings store and its controls.
/// </summary>
/// <remarks>
/// Upstream's pages read straight out of <c>QSettings</c> into their widgets
/// and back. Splitting the values out is what lets the round trip — which is
/// the whole of what a preferences page DOES — be tested without a window, and
/// it keeps every settings key in one readable place per page.
/// </remarks>
public interface IPreferenceValues
{
    /// <summary>Reads the values out of the store.</summary>
    /// <param name="settings">The store, or null for the defaults.</param>
    void Load(SettingsStore settings);

    /// <summary>Writes the values back into the store.</summary>
    /// <param name="settings">The store.</param>
    void Save(SettingsStore settings);
}

/// <summary>What the General page holds.</summary>
/// <remarks>
/// //was previously: <c>preferences/general.py</c>'s three groups. Four of
/// upstream's settings are gone under rulings or for want of anything to
/// configure: <c>guistyle</c> (Qt's <c>QStyleFactory</c> has no analogue — the
/// application draws itself), <c>system_icons</c> and <c>splash_screen</c>
/// (there are no icon themes and no splash screen), and
/// <c>verbose_toolbuttons</c> (the dock shell has no main toolbar with
/// pull-down menus on it).
/// //was previously: "<c>allow_remote</c> belongs with FD5's single-instance
/// work and is not offered until that lands." It landed: the row is here, in
/// upstream's own group, under upstream's own key.
/// </remarks>
public sealed class GeneralValues : IPreferenceValues
{
    /// <summary>What a new document starts out holding.</summary>
    public enum NewDocumentKind
    {
        /// <summary>Nothing at all.</summary>
        Empty,

        /// <summary>A version statement.</summary>
        Version,

        /// <summary>A snippet marked as a template.</summary>
        Template,
    }

    /// <summary>What session the application opens with.</summary>
    public enum SessionStartupKind
    {
        /// <summary>None.</summary>
        None,

        /// <summary>Whichever was last used.</summary>
        LastUsed,

        /// <summary>A named one.</summary>
        Custom,
    }

    /// <summary>The setting naming the interface language.</summary>
    /// <remarks>Upstream's own key: empty means the system's language, and
    /// <c>C</c> means untranslated English. W-I18N is what fills the list.</remarks>
    public const string LanguageKey = "language";

    /// <summary>The setting naming what a new document starts as.</summary>
    public const string NewDocumentKey = "new_document";

    /// <summary>The setting naming the template a new document starts from.</summary>
    public const string NewDocumentTemplateKey = "new_document_template";

    /// <summary>The setting for stripping trailing whitespace on save.</summary>
    public const string StripWhitespaceKey = "strip_trailing_whitespace";

    /// <summary>The setting for remembering per-document state.</summary>
    public const string MetaInfoKey = "metainfo";

    /// <summary>The setting for re-indenting a document on save.</summary>
    public const string FormatKey = "format";

    /// <summary>The setting naming the folder documents default to.</summary>
    public const string BaseDirectoryKey = "basedir";

    /// <summary>The setting for using a file-name template.</summary>
    public const string CustomFileNameKey = "custom_default_filename";

    /// <summary>The setting holding that template.</summary>
    public const string FileNameTemplateKey = "default_filename_template";

    /// <summary>The default file-name template, upstream's own.</summary>
    public const string DefaultFileNameTemplate = "{composer}-{title}";

    /// <summary>The setting turning unfinished features on.</summary>
    /// <remarks>⚠ Upstream spells this one with a HYPHEN where every other key
    /// in the group uses an underscore; the spelling is kept.</remarks>
    public const string ExperimentalFeaturesKey = "experimental-features";

    /// <summary>Gets or sets the interface language.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether files open in an instance that is already running
    /// (decision FD5).
    /// </summary>
    /// <remarks>Upstream's <c>allow_remote</c>, default on. The key itself
    /// lives on <see cref="RemoteInstance.AllowRemoteKey"/>, because the
    /// startup path reads it before any page exists.</remarks>
    public bool AllowRemote { get; set; } = true;

    /// <summary>Gets or sets whether a document tab shows a close button.</summary>
    /// <remarks>Upstream's <c>tabs_closable</c>, default on, read by
    /// <c>tabbar.py</c>. //was previously: absent — the tab bar already carried
    /// the property and nothing set it.</remarks>
    public bool TabsClosable { get; set; } = true;

    /// <summary>The setting the tab bar's close buttons live on.</summary>
    public const string TabsClosableKey = "tabs_closable";

    /// <summary>Gets or sets what a new document starts as.</summary>
    public NewDocumentKind NewDocument { get; set; } = NewDocumentKind.Empty;

    /// <summary>Gets or sets the template a new document starts from.</summary>
    public string NewDocumentTemplate { get; set; } = string.Empty;

    /// <summary>Gets or sets whether trailing whitespace goes on save.</summary>
    public bool StripTrailingWhitespace { get; set; }

    /// <summary>Gets or sets whether a backup copy is kept.</summary>
    public bool KeepBackup { get; set; }

    /// <summary>Gets or sets whether per-document state is remembered.</summary>
    public bool RememberMetaInfo { get; set; } = true;

    /// <summary>Gets or sets whether a document is re-indented on save.</summary>
    public bool FormatOnSave { get; set; }

    /// <summary>Gets or sets the folder documents default to.</summary>
    public string BaseDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets whether a file-name template is used.</summary>
    public bool UsesFileNameTemplate { get; set; }

    /// <summary>Gets or sets that template.</summary>
    public string FileNameTemplate { get; set; } = DefaultFileNameTemplate;

    /// <summary>Gets or sets what session the application opens with.</summary>
    public SessionStartupKind SessionStartup { get; set; } = SessionStartupKind.None;

    /// <summary>Gets or sets the named session it opens with.</summary>
    public string CustomSession { get; set; } = string.Empty;

    /// <summary>Gets or sets whether unfinished features are on.</summary>
    public bool ExperimentalFeatures { get; set; }

    /// <inheritdoc/>
    public void Load(SettingsStore settings)
    {
        Language = settings?.GetString(LanguageKey, string.Empty) ?? string.Empty;
        AllowRemote = RemoteInstance.Enabled(settings);
        TabsClosable = settings?.GetBool(TabsClosableKey, true) ?? true;

        NewDocument = (settings?.GetString(NewDocumentKey, "empty")) switch
        {
            "template" => NewDocumentKind.Template,
            "version" => NewDocumentKind.Version,
            _ => NewDocumentKind.Empty,
        };
        NewDocumentTemplate =
            settings?.GetString(NewDocumentTemplateKey, string.Empty) ?? string.Empty;

        StripTrailingWhitespace = settings?.GetBool(StripWhitespaceKey, false) ?? false;
        KeepBackup = settings?.GetBool(Backup.KeepSettingKey, false) ?? false;
        RememberMetaInfo = settings?.GetBool(MetaInfoKey, true) ?? true;
        FormatOnSave = settings?.GetBool(FormatKey, false) ?? false;
        BaseDirectory = settings?.GetString(BaseDirectoryKey, string.Empty) ?? string.Empty;
        UsesFileNameTemplate = settings?.GetBool(CustomFileNameKey, false) ?? false;
        FileNameTemplate = settings?.GetString(
            FileNameTemplateKey, DefaultFileNameTemplate) ?? DefaultFileNameTemplate;

        SessionStartup = (settings?.GetString(Sessions.SessionStore.StartupKey, "none")) switch
        {
            "lastused" => SessionStartupKind.LastUsed,
            "custom" => SessionStartupKind.Custom,
            _ => SessionStartupKind.None,
        };
        CustomSession = settings?.GetString(
            Sessions.SessionStore.CustomKey, string.Empty) ?? string.Empty;

        ExperimentalFeatures = settings?.GetBool(ExperimentalFeaturesKey, false) ?? false;
    }

    /// <inheritdoc/>
    public void Save(SettingsStore settings)
    {
        if (settings == null) { throw new ArgumentNullException(nameof(settings)); }

        settings.SetString(LanguageKey, Language ?? string.Empty);
        settings.SetBool(RemoteInstance.AllowRemoteKey, AllowRemote);
        settings.SetBool(TabsClosableKey, TabsClosable);

        settings.SetString(NewDocumentKey, NewDocument switch
        {
            NewDocumentKind.Template => "template",
            NewDocumentKind.Version => "version",
            _ => "empty",
        });
        if (NewDocument == NewDocumentKind.Template
            && !string.IsNullOrEmpty(NewDocumentTemplate))
        {
            settings.SetString(NewDocumentTemplateKey, NewDocumentTemplate);
        }

        settings.SetBool(StripWhitespaceKey, StripTrailingWhitespace);
        settings.SetBool(Backup.KeepSettingKey, KeepBackup);
        settings.SetBool(MetaInfoKey, RememberMetaInfo);
        settings.SetBool(FormatKey, FormatOnSave);
        settings.SetString(BaseDirectoryKey, BaseDirectory ?? string.Empty);
        settings.SetBool(CustomFileNameKey, UsesFileNameTemplate);
        settings.SetString(
            FileNameTemplateKey, FileNameTemplate ?? DefaultFileNameTemplate);

        settings.SetString(Sessions.SessionStore.StartupKey, SessionStartup switch
        {
            SessionStartupKind.LastUsed => "lastused",
            SessionStartupKind.Custom => "custom",
            _ => "none",
        });
        settings.SetString(Sessions.SessionStore.CustomKey, CustomSession ?? string.Empty);

        settings.SetBool(ExperimentalFeaturesKey, ExperimentalFeatures);
    }
}

/// <summary>What the Editor page holds.</summary>
/// <remarks>
/// //was previously: <c>preferences/editor.py</c>'s six groups, whole. The
/// indent group keeps upstream's encoding, where a width of ZERO means "use a
/// tab character" (see <see cref="Indenting"/>), so a settings file moves
/// between the two applications unchanged.
/// </remarks>
public sealed class EditorValues : IPreferenceValues
{
    /// <summary>The setting for wrapping long lines.</summary>
    public const string WrapLinesKey = "view_preferences/wrap_lines";

    /// <summary>The setting for how much context a jump shows.</summary>
    public const string ContextLinesKey = "view_preferences/context_lines";

    /// <summary>The setting for the Up/Down document-edge behaviour.</summary>
    public const string SmartStartEndKey = "view_preferences/smart_start_end";

    /// <summary>The setting stopping the arrow keys wrapping lines.</summary>
    public const string KeepCursorInLineKey = "view_preferences/keep_cursor_in_line";

    /// <summary>The setting for how long a match stays highlighted.</summary>
    public const string MatchHighlightKey = "editor_highlighting/match";

    /// <summary>The setting for the visible width of a tab.</summary>
    public const string TabWidthKey = "indent/tab_width";

    /// <summary>The setting for one indent step; 0 means a tab character.</summary>
    public const string IndentSpacesKey = "indent/indent_spaces";

    /// <summary>The setting for a Tab outside the indent; 0 means a tab.</summary>
    public const string DocumentSpacesKey = "indent/document_spaces";

    /// <summary>The settings group the source-export options live in.</summary>
    public const string SourceExportPrefix = "source_export/";

    /// <summary>The settings group the quotation marks live in.</summary>
    public const string QuotesPrefix = "typographical_quotes/";

    /// <summary>Gets or sets whether long lines wrap by default.</summary>
    public bool WrapLines { get; set; }

    /// <summary>Gets or sets how many lines around a jump are shown.</summary>
    public int ContextLines { get; set; } = 3;

    /// <summary>Gets or sets how many seconds a match stays lit; 0 is
    /// for ever.</summary>
    public int MatchHighlightSeconds { get; set; } = 1;

    /// <summary>Gets or sets the visible width of a tab character.</summary>
    public int TabWidth { get; set; } = 8;

    /// <summary>Gets or sets one indent step, in spaces; 0 means a tab.</summary>
    public int IndentSpaces { get; set; } = 2;

    /// <summary>Gets or sets what a Tab outside the indent inserts, in spaces;
    /// 0 means a tab.</summary>
    public int DocumentSpaces { get; set; } = 8;

    /// <summary>Gets or sets whether Home goes to the first real character.</summary>
    public bool SmartHome { get; set; } = true;

    /// <summary>Gets or sets whether Up and Down reach the document's ends.</summary>
    public bool SmartStartEnd { get; set; } = true;

    /// <summary>Gets or sets whether the arrow keys stay on one line.</summary>
    public bool KeepCursorInLine { get; set; }

    /// <summary>Gets or sets whether exported source is numbered.</summary>
    public bool NumberLines { get; set; }

    /// <summary>Gets or sets whether COPIED HTML carries inline styles.</summary>
    public bool InlineStyleCopy { get; set; } = true;

    /// <summary>Gets or sets whether EXPORTED HTML carries inline styles.</summary>
    public bool InlineStyleExport { get; set; }

    /// <summary>Gets or sets whether HTML is put on the clipboard as text.</summary>
    public bool CopyHtmlAsPlainText { get; set; }

    /// <summary>Gets or sets whether only the body is copied.</summary>
    public bool CopyDocumentBodyOnly { get; set; }

    /// <summary>Gets or sets the element the source is wrapped in.</summary>
    public string WrapTag { get; set; } = "pre";

    /// <summary>Gets or sets whether that element carries an id or a class.</summary>
    public string WrapAttribute { get; set; } = "id";

    /// <summary>Gets or sets the value of that attribute.</summary>
    public string WrapAttributeName { get; set; } = "document";

    /// <summary>
    /// Gets or sets which language's quotation marks are used —
    /// <c>current</c>, <c>custom</c>, or a language code.
    /// </summary>
    public string QuotesLanguage { get; set; } = "current";

    /// <summary>Gets or sets the opening double quote.</summary>
    public string PrimaryLeft { get; set; } = string.Empty;

    /// <summary>Gets or sets the closing double quote.</summary>
    public string PrimaryRight { get; set; } = string.Empty;

    /// <summary>Gets or sets the opening single quote.</summary>
    public string SecondaryLeft { get; set; } = string.Empty;

    /// <summary>Gets or sets the closing single quote.</summary>
    public string SecondaryRight { get; set; } = string.Empty;

    /// <inheritdoc/>
    public void Load(SettingsStore settings)
    {
        WrapLines = settings?.GetBool(WrapLinesKey, false) ?? false;
        ContextLines = settings?.GetInt(ContextLinesKey, 3) ?? 3;
        MatchHighlightSeconds = settings?.GetInt(MatchHighlightKey, 1) ?? 1;

        TabWidth = settings?.GetInt(TabWidthKey, 8) ?? 8;
        IndentSpaces = settings?.GetInt(IndentSpacesKey, 2) ?? 2;
        DocumentSpaces = settings?.GetInt(DocumentSpacesKey, 8) ?? 8;

        SmartHome = settings?.GetBool(CursorKeys.SmartHomeSettingKey, true) ?? true;
        SmartStartEnd = settings?.GetBool(SmartStartEndKey, true) ?? true;
        KeepCursorInLine = settings?.GetBool(KeepCursorInLineKey, false) ?? false;

        NumberLines = settings?.GetBool(SourceExportPrefix + "number_lines", false) ?? false;
        InlineStyleCopy = settings?.GetBool(SourceExportPrefix + "inline_copy", true) ?? true;
        InlineStyleExport =
            settings?.GetBool(SourceExportPrefix + "inline_export", false) ?? false;
        CopyHtmlAsPlainText = settings?.GetBool(
            SourceExportPrefix + "copy_html_as_plain_text", false) ?? false;
        CopyDocumentBodyOnly = settings?.GetBool(
            SourceExportPrefix + "copy_document_body_only", false) ?? false;
        WrapTag = settings?.GetString(SourceExportPrefix + "wrap_tag", "pre") ?? "pre";
        WrapAttribute = settings?.GetString(SourceExportPrefix + "wrap_attrib", "id") ?? "id";
        WrapAttributeName = settings?.GetString(
            SourceExportPrefix + "wrap_attrib_name", "document") ?? "document";

        QuoteSet fallback = LanguageQuotes.Default();
        QuotesLanguage = settings?.GetString(QuotesPrefix + "language", "current") ?? "current";
        PrimaryLeft = settings?.GetString(
            QuotesPrefix + "primary_left", fallback.Primary.Left) ?? fallback.Primary.Left;
        PrimaryRight = settings?.GetString(
            QuotesPrefix + "primary_right", fallback.Primary.Right) ?? fallback.Primary.Right;
        SecondaryLeft = settings?.GetString(
            QuotesPrefix + "secondary_left", fallback.Secondary.Left) ?? fallback.Secondary.Left;
        SecondaryRight = settings?.GetString(
            QuotesPrefix + "secondary_right", fallback.Secondary.Right)
            ?? fallback.Secondary.Right;
    }

    /// <inheritdoc/>
    public void Save(SettingsStore settings)
    {
        if (settings == null) { throw new ArgumentNullException(nameof(settings)); }

        settings.SetBool(WrapLinesKey, WrapLines);
        settings.SetInt(ContextLinesKey, ContextLines);
        settings.SetInt(MatchHighlightKey, MatchHighlightSeconds);

        settings.SetInt(TabWidthKey, TabWidth);
        settings.SetInt(IndentSpacesKey, IndentSpaces);
        settings.SetInt(DocumentSpacesKey, DocumentSpaces);

        settings.SetBool(CursorKeys.SmartHomeSettingKey, SmartHome);
        settings.SetBool(SmartStartEndKey, SmartStartEnd);
        settings.SetBool(KeepCursorInLineKey, KeepCursorInLine);

        settings.SetBool(SourceExportPrefix + "number_lines", NumberLines);
        settings.SetBool(SourceExportPrefix + "inline_copy", InlineStyleCopy);
        settings.SetBool(SourceExportPrefix + "inline_export", InlineStyleExport);
        settings.SetBool(
            SourceExportPrefix + "copy_html_as_plain_text", CopyHtmlAsPlainText);
        settings.SetBool(
            SourceExportPrefix + "copy_document_body_only", CopyDocumentBodyOnly);
        settings.SetString(SourceExportPrefix + "wrap_tag", WrapTag ?? "pre");
        settings.SetString(SourceExportPrefix + "wrap_attrib", WrapAttribute ?? "id");
        settings.SetString(
            SourceExportPrefix + "wrap_attrib_name", WrapAttributeName ?? "document");

        settings.SetString(QuotesPrefix + "language", QuotesLanguage ?? "current");
        settings.SetString(QuotesPrefix + "primary_left", PrimaryLeft ?? string.Empty);
        settings.SetString(QuotesPrefix + "primary_right", PrimaryRight ?? string.Empty);
        settings.SetString(QuotesPrefix + "secondary_left", SecondaryLeft ?? string.Empty);
        settings.SetString(QuotesPrefix + "secondary_right", SecondaryRight ?? string.Empty);
    }
}

/// <summary>What the MIDI page holds.</summary>
/// <remarks>
/// ⚠ NOT a port of <c>preferences/midi.py</c>: every one of that page's
/// settings is a MIDI PORT setting, and ruling FR6 replaced ports with
/// in-process synthesis. What a user of Fresco.Brix can usefully set is which
/// instrument bank is sounded and how loudly, which is what this holds.
/// </remarks>
public sealed class MidiValues : IPreferenceValues
{
    /// <summary>Gets or sets the instrument file, or empty for the bundled
    /// bank.</summary>
    public string InstrumentPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the playback volume, as a percentage.</summary>
    /// <remarks>Stored as a percentage so the settings file stays readable;
    /// the player's own property is a factor between 0 and 2.</remarks>
    public int VolumePercent { get; set; } = 100;

    /// <inheritdoc/>
    public void Load(SettingsStore settings)
    {
        InstrumentPath = settings?.GetString(
            SoundFonts.InstrumentSettingKey, string.Empty) ?? string.Empty;
        VolumePercent = Math.Clamp(
            settings?.GetInt(MidiPlayerService.VolumeSettingKey, 100) ?? 100, 0, 200);
    }

    /// <inheritdoc/>
    public void Save(SettingsStore settings)
    {
        if (settings == null) { throw new ArgumentNullException(nameof(settings)); }

        settings.SetString(
            SoundFonts.InstrumentSettingKey, InstrumentPath ?? string.Empty);
        settings.SetInt(
            MidiPlayerService.VolumeSettingKey, Math.Clamp(VolumePercent, 0, 200));
    }
}

/// <summary>What the Documentation page holds.</summary>
/// <remarks>
/// //was previously: <c>preferences/documentation.py</c>. Its first group is a
/// list of paths and URLs to LilyPond documentation, which ruling FR5.1 leaves
/// nothing to point at — the manuals are bundled assets (FR8). Its second
/// group configures the browser, and that is what survives: which manual the
/// reader is shown, and whether the contents list is open. Upstream's font
/// choice goes with the HTML browser it belonged to; a PDF carries its own
/// faces.
/// </remarks>
public sealed class DocumentationValues : IPreferenceValues
{
    /// <summary>Gets or sets the manual shown when the panel opens.</summary>
    public string Manual { get; set; } = ManualCatalog.DefaultName;

    /// <summary>Gets or sets whether the contents list starts open.</summary>
    public bool ShowContents { get; set; } = true;

    /// <inheritdoc/>
    public void Load(SettingsStore settings)
    {
        string manual = settings?.GetString(
            Shell.DocumentationPanel.SettingsPrefix + "manual", ManualCatalog.DefaultName);
        Manual = ManualCatalog.Find(manual) == null ? ManualCatalog.DefaultName : manual;
        ShowContents = settings?.GetBool(
            Shell.DocumentationPanel.SettingsPrefix + "contents", true) ?? true;
    }

    /// <inheritdoc/>
    public void Save(SettingsStore settings)
    {
        if (settings == null) { throw new ArgumentNullException(nameof(settings)); }

        settings.SetString(
            Shell.DocumentationPanel.SettingsPrefix + "manual",
            ManualCatalog.Find(Manual) == null ? ManualCatalog.DefaultName : Manual);
        settings.SetBool(
            Shell.DocumentationPanel.SettingsPrefix + "contents", ShowContents);
    }
}

/// <summary>What the Helper Applications page holds.</summary>
/// <remarks>
/// //was previously: <c>preferences/helpers.py</c>, minus its <c>git</c> row —
/// ruling FR5.7 keeps version control out of the application, so there is no
/// Git command to configure.
/// </remarks>
public sealed class HelperValues : IPreferenceValues
{
    /// <summary>
    /// The helper types, in upstream's own order and with upstream's own key
    /// names.
    /// </summary>
    public static readonly IReadOnlyList<string> Types = new[]
    {
        "pdf", "midi", "svg", "image", "browser", "email", "directory", "shell",
    };

    private readonly Dictionary<string, string> _commands
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the command configured for a helper type.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The command, or the empty string.</returns>
    public string Command(string type)
        => type != null && _commands.TryGetValue(type, out var command)
            ? command
            : string.Empty;

    /// <summary>Sets the command for a helper type.</summary>
    /// <param name="type">The type.</param>
    /// <param name="command">The command; empty means "use the desktop's own".</param>
    public void SetCommand(string type, string command)
    {
        if (type == null) { return; }

        _commands[type] = command ?? string.Empty;
    }

    /// <inheritdoc/>
    public void Load(SettingsStore settings)
    {
        _commands.Clear();
        foreach (var type in Types)
        {
            _commands[type] = settings?.GetString(
                HelperApplications.SettingsPrefix + type, string.Empty) ?? string.Empty;
        }
    }

    /// <inheritdoc/>
    public void Save(SettingsStore settings)
    {
        if (settings == null) { throw new ArgumentNullException(nameof(settings)); }

        foreach (var type in Types)
        {
            settings.SetString(
                HelperApplications.SettingsPrefix + type, Command(type));
        }
    }
}

/// <summary>What the Paths page holds.</summary>
/// <remarks>
/// //was previously: <c>preferences/paths.py</c>'s first group. ⚠ Its SECOND
/// group — the music-font repository and cache folders — belongs to W12B's
/// document-font feature and is added to this page there; see the seam in
/// <see cref="PathsPage"/>.
/// </remarks>
public sealed class PathValues : IPreferenceValues
{
    /// <summary>Gets or sets the folders hyphenation dictionaries are looked
    /// for in; empty means the built-in list.</summary>
    public IReadOnlyList<string> HyphenationPaths { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets the folder music fonts are installed FROM.</summary>
    /// <remarks>Upstream's <c>music-fonts/font-repo</c>. Empty means there is
    /// no repository, and the Document Fonts dialog's "Install (repo)" button
    /// is then disabled — which is upstream's own behaviour.</remarks>
    public string MusicFontRepository { get; set; } = string.Empty;

    /// <summary>Gets or sets the folder sample engravings are kept in.</summary>
    /// <remarks>Upstream's <c>music-fonts/font-cache</c>. Empty means the
    /// default under the temporary directory.</remarks>
    public string MusicFontCache { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the repository's fonts are installed into the
    /// application's own font folder whenever the Document Fonts dialog opens.
    /// </summary>
    /// <remarks>Upstream's <c>music-fonts/auto-install</c>, default TRUE and
    /// written unconditionally — unlike the two paths, which are removed when
    /// they are empty.</remarks>
    public bool AutoInstallMusicFonts { get; set; } = true;

    /// <inheritdoc/>
    public void Load(SettingsStore settings)
    {
        HyphenationPaths = HyphenDictionaries.ConfiguredPaths(settings);
        MusicFontRepository = settings?.GetString(
            DocumentFontSettings.FontRepoKey, string.Empty) ?? string.Empty;
        MusicFontCache = settings?.GetString(
            DocumentFontSettings.FontCacheKey, string.Empty) ?? string.Empty;
        AutoInstallMusicFonts = DocumentFontSettings.AutoInstall(settings);
    }

    /// <inheritdoc/>
    public void Save(SettingsStore settings)
    {
        if (settings == null) { throw new ArgumentNullException(nameof(settings)); }

        settings.SetString(
            DocumentFontSettings.FontRepoKey, MusicFontRepository ?? string.Empty);
        settings.SetString(
            DocumentFontSettings.FontCacheKey, MusicFontCache ?? string.Empty);
        settings.SetBool(
            DocumentFontSettings.AutoInstallKey, AutoInstallMusicFonts);

        //A list that IS the built-in one is forgotten rather than written, so a
        //later change of built-in list reaches a user who never edited it —
        //the same reasoning that keeps a default shortcut out of the store.
        IReadOnlyList<string> paths = HyphenationPaths ?? Array.Empty<string>();
        bool isDefault = paths.Count == HyphenDictionaries.DefaultPaths.Count;
        for (int index = 0; isDefault && index < paths.Count; index++)
        {
            isDefault = string.Equals(
                paths[index], HyphenDictionaries.DefaultPaths[index], StringComparison.Ordinal);
        }

        HyphenDictionaries.SetConfiguredPaths(settings, isDefault ? null : paths);
    }
}

/// <summary>What the Tools page holds.</summary>
/// <remarks>
/// <para>
/// //was previously: <c>preferences/tools.py</c>'s four groups. Three survive
/// whole — the log's behaviour, the document list's grouping and the outline
/// patterns — and each sits on the settings key the panel behind it ALREADY
/// reads, so no second key is introduced for anything.
/// </para>
/// <para>
/// ⚠ Two of upstream's rows are gone because there is nothing behind them:
/// the log's font (<c>log/fontfamily</c>, <c>log/fontsize</c>) and the
/// character map's font (<c>charmaptool/fontfamily</c>,
/// <c>charmaptool/fontsize</c>). Neither is read anywhere in this application —
/// the log draws in the bundled monospace face and the character map draws in
/// the EDITOR's face on purpose, so that what the panel shows is what the
/// document will show (standing rule 6). Upstream's Special Characters group is
/// only that font row, so the whole group goes with it.
/// </para>
/// </remarks>
public sealed class ToolsValues : IPreferenceValues
{
    /// <summary>Gets or sets whether a run saves the document first.</summary>
    /// <remarks>Upstream's <c>lilypond_settings/save_on_run</c>, default off.
    /// The key was ALREADY live here — <c>Engraver.SaveDocumentIfDesired</c>
    /// reads it — with no control anywhere to write it.</remarks>
    public bool SaveDocumentOnRun { get; set; }

    /// <summary>Gets or sets the Engrave-custom dialog's delete default.</summary>
    public bool DeleteIntermediateFiles { get; set; } = true;

    /// <summary>Gets or sets the Engrave-custom dialog's embed default.</summary>
    public bool EmbedSourceCode { get; set; }

    /// <summary>Gets or sets the application-wide include path.</summary>
    public IReadOnlyList<string> IncludePath { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets whether the log appears when a run starts.</summary>
    public bool ShowLogOnStart { get; set; } = true;

    /// <summary>Gets or sets whether the log shows whole paths.</summary>
    public bool RawLogView { get; set; } = true;

    /// <summary>Gets or sets whether automatic engraves are kept out of the
    /// log.</summary>
    public bool HideAutomaticEngraves { get; set; }

    /// <summary>Gets or sets whether the Documents panel groups by folder.</summary>
    public bool GroupDocumentsByFolder { get; set; }

    /// <summary>Gets or sets the patterns matched OUTSIDE comments.</summary>
    public IReadOnlyList<string> OutlinePatterns { get; set; }
        = Documents.DocumentStructure.DefaultPatterns;

    /// <summary>Gets or sets the patterns matched INSIDE comments too.</summary>
    public IReadOnlyList<string> OutlineCommentPatterns { get; set; }
        = Documents.DocumentStructure.DefaultCommentPatterns;

    /// <inheritdoc/>
    public void Load(SettingsStore settings)
    {
        SaveDocumentOnRun =
            settings?.GetBool(Engrave.Engraver.SaveOnRunSettingKey, false) ?? false;
        DeleteIntermediateFiles =
            settings?.GetBool(Engrave.Engraver.DeleteIntermediateSettingKey, true) ?? true;
        EmbedSourceCode =
            settings?.GetBool(Engrave.Engraver.EmbedSourceSettingKey, false) ?? false;
        IncludePath = Engrave.Engraver.IncludePath(settings);

        ShowLogOnStart = settings?.GetBool(Shell.LogPanel.ShowOnStartSettingKey, true) ?? true;
        RawLogView = settings?.GetBool(Shell.LogPanel.RawViewSettingKey, true) ?? true;
        HideAutomaticEngraves =
            settings?.GetBool(Shell.LogPanel.HideAutoEngraveSettingKey, false) ?? false;
        GroupDocumentsByFolder =
            settings?.GetBool(Shell.DocumentListPanel.GroupSettingKey, false) ?? false;

        //Read through the structure's own accessor, so the page and the outline
        //can never disagree about how the stored list is encoded.
        OutlinePatterns = Documents.DocumentStructure.Patterns(false, settings);
        OutlineCommentPatterns = Documents.DocumentStructure.Patterns(true, settings);
    }

    /// <inheritdoc/>
    public void Save(SettingsStore settings)
    {
        if (settings == null) { throw new ArgumentNullException(nameof(settings)); }

        settings.SetBool(Engrave.Engraver.SaveOnRunSettingKey, SaveDocumentOnRun);
        settings.SetBool(
            Engrave.Engraver.DeleteIntermediateSettingKey, DeleteIntermediateFiles);
        settings.SetBool(Engrave.Engraver.EmbedSourceSettingKey, EmbedSourceCode);
        Engrave.Engraver.SetIncludePath(settings, IncludePath);

        //The include path is read into the document service, which every
        //`\include' resolution and every engrave run goes through.
        Documents.DocumentInfo.ApplicationIncludePath = IncludePath
            ?? Array.Empty<string>();

        settings.SetBool(Shell.LogPanel.ShowOnStartSettingKey, ShowLogOnStart);
        settings.SetBool(Shell.LogPanel.RawViewSettingKey, RawLogView);
        settings.SetBool(Shell.LogPanel.HideAutoEngraveSettingKey, HideAutomaticEngraves);
        settings.SetBool(Shell.DocumentListPanel.GroupSettingKey, GroupDocumentsByFolder);

        SavePatterns(
            settings,
            Documents.DocumentStructure.PatternsKey,
            OutlinePatterns,
            Documents.DocumentStructure.DefaultPatterns);
        SavePatterns(
            settings,
            Documents.DocumentStructure.CommentPatternsKey,
            OutlineCommentPatterns,
            Documents.DocumentStructure.DefaultCommentPatterns);

        //⚠ The outline expressions are COMPILED ONCE and cached statically. A
        //new pattern list that is not announced leaves the running application
        //finding the old outline until it is restarted.
        Documents.DocumentStructure.ResetPatterns();
    }

    /// <summary>
    /// Writes one pattern list, or forgets it when it IS the built-in list.
    /// </summary>
    /// <param name="settings">The store.</param>
    /// <param name="key">The setting key.</param>
    /// <param name="patterns">The patterns.</param>
    /// <param name="defaults">The built-in patterns.</param>
    /// <remarks>
    /// Upstream's own <c>s.remove(...)</c> in the <c>else</c> branch, and the
    /// same reasoning as <see cref="PathValues"/>: a later change of built-in
    /// list then reaches a user who never edited one.
    /// ⚠ The list is newline-joined because that is how
    /// <c>DocumentStructure.Patterns</c> already reads it back, and it splits
    /// with empty entries removed — so a pattern cannot itself be empty.
    /// </remarks>
    private static void SavePatterns(
        SettingsStore settings,
        string key,
        IReadOnlyList<string> patterns,
        IReadOnlyList<string> defaults)
    {
        IReadOnlyList<string> value = patterns ?? Array.Empty<string>();
        bool isDefault = value.Count == defaults.Count;
        for (int index = 0; isDefault && index < value.Count; index++)
        {
            isDefault = string.Equals(value[index], defaults[index], StringComparison.Ordinal);
        }

        if (isDefault)
        {
            settings.Remove(key);
            return;
        }

        settings.SetString(key, string.Join("\n", value));
    }
}

/// <summary>What the Music View page holds.</summary>
/// <remarks>
/// <para>
/// //was previously: <c>preferences/musicviewers.py</c>. Every row here sits on
/// a key the Music View ALREADY reads — <c>ScoreDocuments.Update</c> for the
/// first, and <c>MusicViewPanel.ReadSettings</c> for the rest — so the
/// page sets the defaults the panel comes up with rather than introducing a
/// parallel set of settings. Where the port's key name differs from upstream's
/// (<c>viewmode</c> for <c>viewMode</c>, <c>zoom</c> for <c>zoomFactor</c>,
/// <c>layout</c> for <c>pageLayoutMode</c>, <c>continuous</c> for
/// <c>continuousMode</c>) the key already in the settings file wins;
/// <c>newer_files_only</c> and <c>shadow</c> are upstream's own, unchanged.
/// </para>
/// <para>
/// ⚠ Upstream rows with nothing behind them here, and therefore absent:
/// "Remember View settings per-document" (<c>document_properties</c> — the
/// panel keeps ONE view, not one per document), "Kinetic scrolling"
/// (<c>kinetic_scrolling</c>) and "Use Page Up and Page Down keys to change
/// pages" (<c>strict_paging</c>) — neither behaviour exists in the view —
/// "Show scrollbars" (<c>show_scrollbars</c> — the view's own drawn bars are
/// not optional), and the Magnifier group (<c>magnifier/size</c>,
/// <c>magnifier/scalef</c> — the glass is created on demand by the magnifier
/// command and reads no settings). The Printing group is dead under ruling
/// FR5.5.
/// </para>
/// </remarks>
public sealed class MusicViewValues : IPreferenceValues
{
    /// <summary>The setting naming how the score is scaled.</summary>
    public const string ViewModeKey
        = MusicView.MusicViewPanel.ViewSettingsPrefix + "viewmode";

    /// <summary>The setting holding the fixed scale, as a factor.</summary>
    public const string ZoomKey
        = MusicView.MusicViewPanel.ViewSettingsPrefix + "zoom";

    /// <summary>The setting naming how pages are arranged.</summary>
    public const string LayoutKey
        = MusicView.MusicViewPanel.ViewSettingsPrefix + "layout";

    /// <summary>The setting naming which way a row of pages runs.</summary>
    public const string OrientationKey
        = MusicView.MusicViewPanel.ViewSettingsPrefix + "orientation";

    /// <summary>The setting for scrolling past a page boundary.</summary>
    public const string ContinuousKey
        = MusicView.MusicViewPanel.ViewSettingsPrefix + "continuous";

    /// <summary>The setting for the shadow behind each page.</summary>
    public const string ShadowKey
        = MusicView.MusicViewPanel.ViewSettingsPrefix + "shadow";

    /// <summary>The smallest fixed scale offered, as a percentage.</summary>
    public const int MinimumScalePercent = 50;

    /// <summary>The largest fixed scale offered, as a percentage.</summary>
    /// <remarks>Upstream's <c>int(PagedView.MAX_ZOOM * 100)</c>; here the view's
    /// own <c>MusicViewControl.MaxZoom</c> is 8.0.</remarks>
    public const int MaximumScalePercent = 800;

    /// <summary>The scaling the view comes up with when nothing is set.</summary>
    /// <remarks>⚠ Upstream defaults to a fixed scale; this port's Music View has
    /// always come up fitting the width, and the page shows what the panel
    /// does.</remarks>
    public const string DefaultViewMode = "fitwidth";

    /// <summary>The page arrangement the view comes up with.</summary>
    public const string DefaultPageLayout = "single";

    /// <summary>The scrolling direction the view comes up with.</summary>
    public const string DefaultOrientation = "vertical";

    /// <summary>The scalings offered, in upstream's own order.</summary>
    public static readonly IReadOnlyList<string> ViewModes = new[]
    {
        "fixed", "fitheight", "fitwidth", "fitboth",
    };

    /// <summary>The page arrangements offered, in upstream's own order.</summary>
    public static readonly IReadOnlyList<string> PageLayouts = new[]
    {
        "single", "double_right", "double_left", "raster",
    };

    /// <summary>The scrolling directions offered, in upstream's own order.</summary>
    public static readonly IReadOnlyList<string> Orientations = new[]
    {
        "horizontal", "vertical",
    };

    /// <summary>Gets or sets whether a score older than its source is
    /// skipped.</summary>
    public bool OnlyNewerFiles { get; set; } = true;

    /// <summary>Gets or sets how the score is scaled; one of
    /// <see cref="ViewModes"/>.</summary>
    public string ViewMode { get; set; } = DefaultViewMode;

    /// <summary>Gets or sets the fixed scale, as a percentage.</summary>
    /// <remarks>The store holds upstream's FACTOR; the page shows a percentage,
    /// as upstream's own spin box does.</remarks>
    public int ScalePercent { get; set; } = 100;

    /// <summary>Gets or sets how pages are arranged; one of
    /// <see cref="PageLayouts"/>.</summary>
    public string PageLayout { get; set; } = DefaultPageLayout;

    /// <summary>Gets or sets which way a row of pages runs; one of
    /// <see cref="Orientations"/>.</summary>
    public string Orientation { get; set; } = DefaultOrientation;

    /// <summary>Gets or sets whether scrolling crosses a page boundary.</summary>
    public bool ContinuousScrolling { get; set; } = true;

    /// <summary>Gets or sets whether a shadow is drawn behind each page.</summary>
    public bool PageShadow { get; set; } = true;

    /// <inheritdoc/>
    public void Load(SettingsStore settings)
    {
        OnlyNewerFiles = settings?.GetBool(
            MusicView.ScoreDocuments.NewerFilesOnlySettingKey, true) ?? true;
        ViewMode = Known(
            settings?.GetString(ViewModeKey, DefaultViewMode), ViewModes, DefaultViewMode);
        ScalePercent = Math.Clamp(
            (int)Math.Round((settings?.GetDouble(ZoomKey, 1.0) ?? 1.0) * 100.0),
            MinimumScalePercent,
            MaximumScalePercent);
        PageLayout = Known(
            settings?.GetString(LayoutKey, DefaultPageLayout), PageLayouts, DefaultPageLayout);
        Orientation = Known(
            settings?.GetString(OrientationKey, DefaultOrientation),
            Orientations,
            DefaultOrientation);
        ContinuousScrolling = settings?.GetBool(ContinuousKey, true) ?? true;
        PageShadow = settings?.GetBool(ShadowKey, true) ?? true;
    }

    /// <inheritdoc/>
    public void Save(SettingsStore settings)
    {
        if (settings == null) { throw new ArgumentNullException(nameof(settings)); }

        settings.SetBool(
            MusicView.ScoreDocuments.NewerFilesOnlySettingKey, OnlyNewerFiles);
        settings.SetString(ViewModeKey, Known(ViewMode, ViewModes, DefaultViewMode));
        settings.SetDouble(
            ZoomKey,
            Math.Clamp(ScalePercent, MinimumScalePercent, MaximumScalePercent) / 100.0);
        settings.SetString(LayoutKey, Known(PageLayout, PageLayouts, DefaultPageLayout));
        settings.SetString(
            OrientationKey, Known(Orientation, Orientations, DefaultOrientation));
        settings.SetBool(ContinuousKey, ContinuousScrolling);
        settings.SetBool(ShadowKey, PageShadow);
    }

    private static string Known(
        string value, IReadOnlyList<string> allowed, string fallback)
    {
        foreach (var candidate in allowed)
        {
            if (string.Equals(candidate, value, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return fallback;
    }
}
