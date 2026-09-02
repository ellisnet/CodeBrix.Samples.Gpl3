// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.DocumentFonts;
using Fresco.Brix.Services;
using Fresco.Brix.Widgets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace Fresco.Brix.Preferences; //was previously: frescobaldi/preferences/paths.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Paths page: the folders searched for hyphenation dictionaries, and the
/// two folders the document-font feature uses.
/// </summary>
/// <remarks>
/// //was previously: PARTIAL BY DESIGN, with only the hyphenation group. W12B
/// added upstream's second group — the music-font repository and the music-font
/// sample cache — and changed nothing else in this file, which is what the seam
/// left here said would happen.
/// </remarks>
public sealed class PathsPage : PreferencesPage
{
    private ListEdit _hyphenPaths;
    private Button _defaults;
    private UrlRequester _fontRepo;
    private UrlRequester _fontCache;
    private CheckBox _autoInstall;

    /// <summary>Creates the page.</summary>
    /// <param name="context">What the page configures.</param>
    public PathsPage(PreferencesContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Paths");

    /// <inheritdoc/>
    public override string Help => "prefs_paths";

    /// <inheritdoc/>
    public override string IconName => "folder-open";

    /// <summary>Gets the values the page reads and writes.</summary>
    public PathValues Values { get; } = new PathValues();

    /// <inheritdoc/>
    public override void LoadSettings()
    {
        Values.Load(Settings);
        _hyphenPaths.Value = Values.HyphenationPaths;
        _fontRepo.Path = Values.MusicFontRepository;
        _fontCache.Path = Values.MusicFontCache;
        _autoInstall.IsChecked = Values.AutoInstallMusicFonts;
    }

    /// <inheritdoc/>
    public override void SaveSettings()
    {
        Values.HyphenationPaths = _hyphenPaths.Value;
        Values.MusicFontRepository = _fontRepo.Path;
        Values.MusicFontCache = _fontCache.Path;
        Values.AutoInstallMusicFonts = _autoInstall.IsChecked == true;
        Values.Save(Settings);
    }

    /// <inheritdoc/>
    protected override UIElement Build()
    {
        _hyphenPaths = new ListEdit
        {
            OpenEditorAsync = PickFolderAsync,
            //The list is a SEARCH order — the first folder holding a language's
            //dictionary is the one used — so the order is the user's to set.
            CanReorder = true,
        };
        _hyphenPaths.Changed += (_, _) => MarkChanged();

        _defaults = new Button { Content = I18n.Get("Default") };
        ToolTipService.SetToolTip(
            _defaults, I18n.Get("Restores the built-in folders."));
        _defaults.Click += (_, _) =>
        {
            _hyphenPaths.Value = HyphenDictionaries.DefaultPaths;
            MarkChanged();
        };
        _hyphenPaths.AddButton(_defaults);

        //Upstream's second group, "Music Fonts": where music fonts are
        //installed FROM, and where the Document Fonts dialog keeps the sample
        //scores it has already engraved.
        _fontRepo = Path();
        _fontRepo.EntryToolTip = I18n.Get(
            "The directory containing a music font repository");

        //Upstream puts this checkbox on the SAME ROW as the repository LABEL,
        //before the requester below it (paths.py MusicFonts: label, checkbox,
        //stretch — then the requester on its own line).
        //was previously: no control at all, while `music-fonts/auto-install'
        //defaulted to TRUE and was acted on every time the Document Fonts
        //dialog opened — a live, on-by-default side effect the user could not
        //turn off.
        //⚠ The CAPTION is upstream's own msgid; the TOOLTIP is not — upstream's
        //says "to the current LilyPond installation", and there is no
        //installation here (FR5.1): fonts are copied into this application's own
        //font folder. The reworded tooltip is in the renamed-string table.
        _autoInstall = Tick(
            I18n.Get("Auto install"),
            I18n.Format(
                I18n.Get(
                    "Always install fonts from the music font repository\n"
                    + "into {folder} when opening the Document Fonts dialog."),
                ("folder", InstalledMusicFonts.DefaultDirectory())));

        _fontCache = Path();
        _fontCache.EntryToolTip = I18n.Get(
            "A directory where music font previews are cached.\n"
            + "Leave empty to use the system's temporary directory,\n"
            + "which will be purged upon computer shutdown.");

        return Stack(
            Group(
                I18n.Get("Folders containing hyphenation dictionaries"),
                Rows(
                    Note(I18n.Format(
                        I18n.Get(
                            "A relative folder is looked for under /usr and "
                            + "/usr/local. The dictionaries that came with "
                            + "{appname} are always searched as well."),
                        ("appname", AppInfo.AppName))),
                    _hyphenPaths)),
            Group(
                I18n.Get("Music Fonts"),
                Rows(
                    Note(I18n.Format(
                        I18n.Get(
                            "Music fonts are installed into {folder}, which "
                            + "LilyPort searches before its own built-in "
                            + "fonts. Nothing is installed there until you ask "
                            + "for it in Tools ▸ Document Fonts."),
                        ("folder", InstalledMusicFonts.DefaultDirectory()))),
                    Labelled(I18n.Get("Music Font Repository:"), _autoInstall),
                    _fontRepo,
                    Labelled(I18n.Get("Music Font Preview Cache:"), _fontCache))));
    }

    private async Task<string> PickFolderAsync(string current)
    {
        Func<UrlRequesterMode, string, Task<string>> pick = Context.PickAsync;
        if (pick == null) { return null; }

        return await pick(UrlRequesterMode.Directory, current);
    }
}
