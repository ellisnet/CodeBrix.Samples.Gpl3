// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Fresco.Brix.Widgets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Preferences; //was previously: frescobaldi/preferences/general.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The General page: the interface language, what a new document starts as,
/// what saving does, which session the application opens with, and the
/// experimental-features switch.
/// </summary>
public sealed class GeneralPage : PreferencesPage
{
    private readonly List<string> _languages = new List<string> { "C", string.Empty };
    private readonly List<string> _templates = new List<string>();
    private readonly List<string> _sessions = new List<string>();

    private ComboBox _language;
    private CheckBox _allowRemote;
    private CheckBox _tabsClosable;
    private ComboBox _newDocument;
    private ComboBox _template;
    private CheckBox _verboseToolButtons;
    private CheckBox _stripWhitespace;
    private CheckBox _keepBackup;
    private CheckBox _metaInfo;
    private CheckBox _format;
    private UrlRequester _baseDirectory;
    private CheckBox _customFileName;
    private TextBox _fileNameTemplate;
    private ComboBox _sessionStartup;
    private ComboBox _session;
    private CheckBox _experimental;

    /// <summary>Creates the page.</summary>
    /// <param name="context">What the page configures.</param>
    public GeneralPage(PreferencesContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    public override string Title => I18n.Get("General");

    /// <inheritdoc/>
    public override string Help => "prefs_general";

    /// <inheritdoc/>
    public override string IconName => "preferences-system";

    /// <summary>Gets the values the page reads and writes.</summary>
    public GeneralValues Values { get; } = new GeneralValues();

    /// <inheritdoc/>
    public override void LoadSettings()
    {
        Values.Load(Settings);

        int language = _languages.IndexOf(Values.Language ?? string.Empty);
        _language.SelectedIndex = language < 0 ? 1 : language;
        _tabsClosable.IsChecked = Values.TabsClosable;
        _allowRemote.IsChecked = Values.AllowRemote;

        _newDocument.SelectedIndex = (int)Values.NewDocument;
        int template = _templates.IndexOf(Values.NewDocumentTemplate ?? string.Empty);
        _template.SelectedIndex = template < 0 && _templates.Count > 0 ? 0 : template;
        _template.IsEnabled = Values.NewDocument == GeneralValues.NewDocumentKind.Template;

        _verboseToolButtons.IsChecked = Values.VerboseToolButtons;
        _stripWhitespace.IsChecked = Values.StripTrailingWhitespace;
        _keepBackup.IsChecked = Values.KeepBackup;
        _metaInfo.IsChecked = Values.RememberMetaInfo;
        _format.IsChecked = Values.FormatOnSave;
        _baseDirectory.Path = Values.BaseDirectory;
        _customFileName.IsChecked = Values.UsesFileNameTemplate;
        _fileNameTemplate.Text = Values.FileNameTemplate;
        _fileNameTemplate.IsEnabled = Values.UsesFileNameTemplate;

        RefreshSessions();
        _sessionStartup.SelectedIndex = (int)Values.SessionStartup;
        int session = _sessions.IndexOf(Values.CustomSession ?? string.Empty);
        _session.SelectedIndex = session < 0 && _sessions.Count > 0 ? 0 : session;
        _session.IsEnabled = Values.SessionStartup == GeneralValues.SessionStartupKind.Custom;

        _experimental.IsChecked = Values.ExperimentalFeatures;
    }

    /// <inheritdoc/>
    public override void SaveSettings()
    {
        Values.Language = _language.SelectedIndex >= 0
            && _language.SelectedIndex < _languages.Count
                ? _languages[_language.SelectedIndex]
                : string.Empty;
        Values.TabsClosable = _tabsClosable.IsChecked == true;
        Values.AllowRemote = _allowRemote.IsChecked == true;

        Values.NewDocument = (GeneralValues.NewDocumentKind)Math.Max(
            0, _newDocument.SelectedIndex);
        Values.NewDocumentTemplate = _template.SelectedIndex >= 0
            && _template.SelectedIndex < _templates.Count
                ? _templates[_template.SelectedIndex]
                : string.Empty;

        Values.VerboseToolButtons = _verboseToolButtons.IsChecked == true;
        Values.StripTrailingWhitespace = _stripWhitespace.IsChecked == true;
        Values.KeepBackup = _keepBackup.IsChecked == true;
        Values.RememberMetaInfo = _metaInfo.IsChecked == true;
        Values.FormatOnSave = _format.IsChecked == true;
        Values.BaseDirectory = _baseDirectory.Path;
        Values.UsesFileNameTemplate = _customFileName.IsChecked == true;
        Values.FileNameTemplate = _fileNameTemplate.Text;

        Values.SessionStartup = (GeneralValues.SessionStartupKind)Math.Max(
            0, _sessionStartup.SelectedIndex);
        Values.CustomSession = _session.SelectedIndex >= 0
            && _session.SelectedIndex < _sessions.Count
                ? _sessions[_session.SelectedIndex]
                : string.Empty;

        Values.ExperimentalFeatures = _experimental.IsChecked == true;

        Values.Save(Settings);
    }

    /// <inheritdoc/>
    protected override UIElement Build()
        => Stack(
            Group(I18n.Get("Language and Style"), BuildLanguage()),
            Group(I18n.Get("Sessions and Files"), BuildFiles()),
            Group(I18n.Get("Experimental Features"), BuildExperimental()));

    private UIElement BuildLanguage()
    {
        //Upstream's own list, in upstream's own order: "No Translation" (the
        //setting "C"), "System Default Language (if available)" (the setting
        //empty), then every language a catalog is installed for, NAMED IN
        //ITSELF and sorted by that name — languageName(lang, lang), sorted.
        _language = Choice(
            I18n.Get("No Translation"),
            I18n.Get("System Default Language (if available)"));

        List<(string Name, string Code)> installed = LanguageSetup.Available()
            .Select(code => (Name: LanguageNames.LanguageName(code, code), Code: code))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.Code, StringComparer.Ordinal)
            .ToList();

        foreach (var (name, code) in installed)
        {
            _languages.Add(code);
            _language.Items.Add(new ComboBoxItem { Content = name });
        }

        //was previously: missing, and NOT declared — the tab bar already carries
        //`TabsClosable' and nothing ever set it. Upstream's row sits between the
        //two ticks this application has nothing to configure for, immediately
        //before "Open Files in Running Instance".
        _tabsClosable = Tick(I18n.Get("Show Close Button on Document tabs"));

        //FD5's own preference, in upstream's own group and with upstream's own
        //caption and tool tip. It is read at startup, before the window exists,
        //by RemoteInstance.
        _allowRemote = Tick(
            I18n.Get("Open Files in Running Instance"),
            //was previously: "…a running Frescobaldi application…". FR9: the
            //application is not Frescobaldi, and its own name goes in.
            I18n.Format(
                I18n.Get("If checked, files will be opened in a running {appname} \n"
                    + "application if available, instead of starting a new instance."),
                ("appname", AppInfo.AppName)));

        //was previously: a Style chooser over Qt's QStyleFactory, and tick
        //boxes for symbolic icons and the splash screen. A CodeBrix.Platform
        //application draws its own controls, has no icon themes to choose
        //between and shows no splash screen, so none of the three has anything
        //to configure.
        return Rows(
            Labelled(I18n.Get("Language:"), _language),
            //⚠ A DELIBERATE DIVERGENCE OF MECHANISM, said out loud rather than
            //left for the user to discover. Upstream re-translates every open
            //widget the moment the setting changes (app.translateUI); a
            //CodeBrix.Platform window builds its captions once, so the change
            //lands at the next launch. The sentence is Fresco.Brix's own msgid
            //and is recorded in the harvest tool's renamed-string table.
            Note(I18n.Format(
                I18n.Get("A change of language takes effect when {appname} "
                    + "is started again."),
                ("appname", AppInfo.AppName))),
            _tabsClosable,
            _allowRemote);
    }

    private UIElement BuildFiles()
    {
        //was previously: a QTabWidget with New Document, Saving and Sessions
        //tabs. The theme's tab controls paint nothing on the Skia heads (board
        //trap 40), so the three tabs are three titled groups down the page —
        //which is what every other group on every other page already is.
        _newDocument = Choice(
            I18n.Get("Create an empty document"),
            //was previously: "Create a document that contains the LilyPond
            //version statement". No UI element names LilyPond (FR13).
            I18n.Get("Create a document that contains the LilyPort version statement"),
            I18n.Get("Create a document from a template:"));
        _newDocument.SelectionChanged += (_, _) =>
        {
            if (_template != null)
            {
                _template.IsEnabled = _newDocument.SelectedIndex == 2;
            }
        };

        _template = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _template.SelectionChanged += (_, _) => MarkChanged();
        FillTemplates();

        //Upstream's own place for it: the FIRST widget of the Sessions and
        //Files group, above the three tabs (preferences/general.py, class
        //SessionsAndFiles). //was previously: absent, because the window had no
        //toolbar for the menus to hang on — ruling FR16 built both bars.
        _verboseToolButtons = Tick(
            I18n.Get("Add pull-down menus in main toolbar"),
            I18n.Get(
                "If set, the file related buttons in the main toolbar will "
                + "provide pull-down menus with additional functions."));

        _stripWhitespace = Tick(
            I18n.Get("Strip trailing whitespace"),
            I18n.Format(
                I18n.Get(
                    "If checked, {appname} will remove unnecessary whitespace at the "
                    + "end of lines (but not inside multi-line strings)."),
                ("appname", AppInfo.AppName)));
        _keepBackup = Tick(
            I18n.Get("Keep backup copy"),
            I18n.Format(
                I18n.Get(
                    "{appname} always backups a file before overwriting it "
                    + "with a new version.\nIf checked those backup copies are retained."),
                ("appname", AppInfo.AppName)));
        _metaInfo = Tick(I18n.Get("Remember cursor position, bookmarks, etc."));
        _format = Tick(I18n.Get("Format document"));

        _baseDirectory = Path();
        _baseDirectory.EntryToolTip =
            I18n.Get("The default folder for your LilyPort documents (optional).");

        _customFileName = Tick(
            I18n.Get("Use custom default file name:"),
            I18n.Format(
                I18n.Get(
                    //{title} and {composer} are LITERAL here — upstream never
                    //formats this message, and I18n.Format leaves a
                    //placeholder it was not given verbatim.
                    "If checked, {appname} will use the template to generate a "
                    + "default file name.\n{title} and {composer} will be replaced "
                    + "by title and composer of that document."),
                ("appname", AppInfo.AppName)));
        _customFileName.Checked += (_, _) => _fileNameTemplate.IsEnabled = true;
        _customFileName.Unchecked += (_, _) => _fileNameTemplate.IsEnabled = false;

        _fileNameTemplate = Entry();

        _sessionStartup = Choice(
            I18n.Get("Start with no session"),
            I18n.Get("Start with last used session"),
            I18n.Get("Start with session:"));
        _sessionStartup.SelectionChanged += (_, _) =>
        {
            if (_session != null) { _session.IsEnabled = _sessionStartup.SelectedIndex == 2; }
        };

        _session = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _session.SelectionChanged += (_, _) => MarkChanged();

        return Rows(
            _verboseToolButtons,
            Group(I18n.Get("New Document"), Rows(
                Labelled(I18n.Get("New document:"), _newDocument),
                Labelled(I18n.Get("Template:"), _template))),
            Group(I18n.Get("Saving"), Rows(
                _stripWhitespace,
                _keepBackup,
                _metaInfo,
                _format,
                Labelled(I18n.Get("Default directory:"), _baseDirectory),
                _customFileName,
                Labelled(string.Empty, _fileNameTemplate))),
            Group(I18n.Get("Sessions"), Rows(
                Note(I18n.Format(
                    I18n.Get("Session to load if {appname} is started without arguments"),
                    ("appname", AppInfo.AppName))),
                _sessionStartup,
                Labelled(I18n.Get("Session:"), _session))));
    }

    private UIElement BuildExperimental()
    {
        _experimental = Tick(
            I18n.Get("Enable Experimental Features"),
            I18n.Format(
                I18n.Get(
                    "If checked, features that are not yet finished are enabled.\n"
                    + "You need to restart {appname} to see the changes."),
                ("appname", AppInfo.AppName)));
        return Rows(_experimental);
    }

    private void FillTemplates()
    {
        _templates.Clear();
        _template.Items.Clear();

        SnippetLibraryTemplates();
        foreach (var name in _templates)
        {
            _template.Items.Add(new ComboBoxItem
            {
                //A snippet's title is the user's own text, not a msgid.
                Content = Context.Snippets?.Title(name) ?? name,
            });
        }

        _template.IsEnabled = false;
    }

    private void SnippetLibraryTemplates()
    {
        if (Context.Snippets == null) { return; }

        foreach (var name in Context.Snippets.NamesByTitle())
        {
            Snippets.SnippetText snippet = Context.Snippets.Get(name);
            if (snippet != null && snippet.Variables.ContainsKey("template"))
            {
                _templates.Add(name);
            }
        }
    }

    private void RefreshSessions()
    {
        _sessions.Clear();
        _session.Items.Clear();
        foreach (var name in Context.SessionStore?.SessionNames()
            ?? Enumerable.Empty<string>())
        {
            _sessions.Add(name);
            _session.Items.Add(new ComboBoxItem { Content = name });
        }
    }
}
