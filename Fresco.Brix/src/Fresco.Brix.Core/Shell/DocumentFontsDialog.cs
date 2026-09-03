// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.DocumentFonts;
using Fresco.Brix.Engrave;
using Fresco.Brix.Services;
using Fresco.Brix.Widgets;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using View = Fresco.Brix.MusicView;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/fonts/dialog.py + textfonts.py + musicfonts.py + preview.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Document Fonts dialog: four tabs of what the engine can set a document
/// in, a live preview of the result, and the command that says so.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's dialog is a splitter with a tab widget on the left and a score
/// preview on the right, and a five-button box along the bottom. Here the tabs
/// are a row of buttons over one page at a time (board trap 40 — the theme's
/// tab controls paint nothing on the Skia heads), the splitter is the
/// application's own drawn <see cref="SplitContainer"/> (trap 20), and the
/// button box is three <see cref="ContentDialog"/> buttons with the other two
/// inside the content (traps 43 and 50).
/// </para>
/// <para>
/// ⚠ The DIALOG's own state — which five fonts are chosen — outlives the
/// window, exactly as upstream's does (its <c>_selected_fonts</c> is a class
/// variable). Here it is a <see cref="FontSelection"/> the window owns.
/// </para>
/// </remarks>
public sealed class DocumentFontsDialog
{
    /// <summary>The user-guide page this dialog documents itself with.</summary>
    /// <remarks>⚠ Upstream's <c>userguide.addButton(self._button_box,
    /// "documentfonts")</c>. Nothing resolves it until the user guide lands;
    /// that is expected, not a defect — the same arrangement the preferences
    /// pages are already in.</remarks>
    public const string HelpIdentifier = "documentfonts";

    /// <summary>The root the dialog was last shown on, for sizing.</summary>
    private XamlRoot _root;

    private readonly SettingsStore _settings;
    private readonly LilyPortEngine _engine;
    private readonly View.IScoreTypefaceResolver _typefaces;

    private readonly FontCommandOptions _options = new FontCommandOptions();
    private InstalledMusicFonts _musicFonts;
    private TextFontWorld _textFonts;

    // The window's controls.
    private Grid _pages;
    private IReadOnlyList<Button> _tabs;
    private int _currentPage;

    private TextBlock _textStatus;
    private TreeView _textTree;
    private TextBox _textFilter;
    private IReadOnlyList<TextFontSelector> _textChoices;

    private TreeView _musicTree;
    private Button _installRepo;
    private Button _install;
    private Button _download;
    private Button _remove;
    private TextBlock _musicStatus;

    private CheckBox _cbMusic;
    private CheckBox _cbRoman;
    private CheckBox _cbSans;
    private CheckBox _cbTypewriter;
    private CheckBox _cbPaperBlock;
    private CheckBox _cbOll;
    private CheckBox _cbLoadPackage;
    private CheckBox _cbExtensions;
    private ComboBox _styleType;
    private TextBox _styleSheet;
    private ComboBox _approach;
    private TextBox _commandText;
    private readonly Dictionary<string, TextBlock> _fontLabels
        = new Dictionary<string, TextBlock>(StringComparer.Ordinal);

    private TreeView _miscTree;

    private ComboBox _samples;
    private UrlRequester _customSample;
    private View.MusicViewControl _preview;
    private TextBlock _previewStatus;
    private VolatileTextJob _previewJob;
    private string _lastPreviewSource;

    private bool _building;

    /// <summary>Creates the dialog.</summary>
    /// <param name="settings">The store the dialog's state lives in.</param>
    /// <param name="engine">The engine the preview engraves on.</param>
    /// <param name="typefaces">Who answers the preview's font families.</param>
    public DocumentFontsDialog(
        SettingsStore settings,
        LilyPortEngine engine,
        View.IScoreTypefaceResolver typefaces = null)
    {
        _settings = settings;
        _engine = engine;
        _typefaces = typefaces;
        Fonts = new FontSelection();
    }

    /// <summary>Gets the five fonts the dialog has chosen.</summary>
    public FontSelection Fonts { get; }

    /// <summary>Gets or sets what the current document holds, for the
    /// "Current Document" sample.</summary>
    public Func<string> CurrentDocumentText { get; set; }

    /// <summary>Gets or sets the current document's folder, for that sample's
    /// relative includes.</summary>
    public Func<string> CurrentDocumentDirectory { get; set; }

    /// <summary>Gets or sets how the head picks a file or folder.</summary>
    public Func<UrlRequesterMode, string, Task<string>> PickAsync { get; set; }

    /// <summary>Gets or sets how the finished command is put on the clipboard.</summary>
    public Action<string> CopyToClipboard { get; set; }

    /// <summary>
    /// Puts the dialog in front of the user.
    /// </summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <returns>The font command to insert, or null when nothing is to be
    /// inserted.</returns>
    /// <remarks>Upstream's <c>dlg.result</c>: the text is only there when the
    /// user pressed "Use", and it always ends in a newline.</remarks>
    public async Task<string> ShowAsync(XamlRoot xamlRoot)
    {
        _root = xamlRoot;
        LoadSettings();
        RegisterMusicFontFolder();

        ContentDialog dialog = new ContentDialog
        {
            Title = I18n.Get("Document Fonts"),
            Content = BuildContent(),
            PrimaryButtonText = MenuBuilder.Display(I18n.Get("&Use")),
            SecondaryButtonText = MenuBuilder.Display(I18n.Get("&Copy")),
            CloseButtonText = I18n.Get("Close"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        //Board trap 43: a ContentDialog's width is the ContentDialogMaxWidth
        //RESOURCE, not its MaxWidth. Upstream opens this dialog at 1024x700.
        //was previously: 1400x1000 written out, over a root Grid with a FIXED
        //Width of 1180 — so in a 1024-pixel-wide window the content still
        //demanded 1180 and the right-hand column (the preview, and on the Font
        //Command tab the generated \paper command) was simply cut off. The
        //design size is a MAXIMUM now, clamped to the window (DialogSizing).
        DialogSizing.Clamp(dialog, 1400, 1000);

        InvalidateCommand();

        ContentDialogResult result = await dialog.ShowAsync();
        SaveSettings();
        CleanupPreview();

        if (result == ContentDialogResult.Secondary)
        {
            //Upstream's copy_result(): the command goes to the clipboard and
            //nothing is inserted.
            CopyToClipboard?.Invoke(WithNewline(CurrentCommand().Command));
            return null;
        }

        return result == ContentDialogResult.Primary
            ? WithNewline(CurrentCommand().Command)
            : null;
    }

    // -------------------------------------------------------------- settings

    /// <summary>Reads what the dialog remembered.</summary>
    private void LoadSettings()
    {
        Fonts.Load(_settings);
        _options.Load(_settings);
    }

    /// <summary>Writes what the dialog is to remember.</summary>
    private void SaveSettings()
    {
        Fonts.Save(_settings);
        _options.Save(_settings);
        if (_settings == null) { return; }

        _settings.SetString(
            DocumentFontSettings.Key("default-music-sample"), SelectedSampleId());
        _settings.SetString(
            DocumentFontSettings.Key("custom-music-sample-url"),
            _customSample?.Path ?? string.Empty);
    }

    /// <summary>
    /// Puts the application's music-font folder in front of the engine's own
    /// faces, and reads what is in it.
    /// </summary>
    private void RegisterMusicFontFolder()
    {
        string folder = InstalledMusicFonts.Register();
        _musicFonts = new InstalledMusicFonts(folder);

        //Upstream installs the configured repository as the tab is built when
        //"auto-install" is on, so a user who keeps their fonts in one folder
        //never has to press anything.
        if (!DocumentFontSettings.AutoInstall(_settings)) { return; }

        MusicFontRepo repo = DocumentFontSettings.MusicFontsRepo(_settings);
        if (repo == null) { return; }

        try
        {
            repo.FlagForInstall(_musicFonts);
            repo.InstallFlagged(_musicFonts);
        }
        catch (MusicFontException)
        {
            //The tab shows the failure when the user asks for it explicitly;
            //an automatic install that cannot happen is not worth a dialog in
            //front of a user who did not ask for one.
        }
    }

    // ---------------------------------------------------------------- window

    /// <summary>Builds the dialog's contents.</summary>
    /// <returns>The content.</returns>
    private UIElement BuildContent()
    {
        _building = true;

        //was previously: Width = 1180, Height = 620 — a demand rather than a
        //preference. The MinWidth is the smallest layout that still works (a
        //300-pixel options column beside a usable preview) and satisfies trap
        //43's "something inside must carry a MinWidth"; the MaxWidth is the
        //size the layout was designed at.
        Grid root = new Grid
        {
            MinWidth = 820,
            MaxWidth = 1180,
            //The height is what is LEFT in the window, capped at the design
            //620: a fixed 620 pushed the dialog's own buttons off the bottom of
            //a 768-pixel window.
            Height = DialogSizing.ContentHeight(_root, 620),
            RowSpacing = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        SplitContainer split = new SplitContainer
        {
            Orientation = Orientation.Horizontal,
        };
        split.AddPane(BuildTabs());
        split.AddPane(BuildPreview());
        split.SetSizes(new[] { 0.52, 0.48 });
        root.Children.Add(split);

        StackPanel actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };

        Button restore = new Button { Content = StandardButtons.RestoreDefaults };
        restore.Click += (_, _) =>
        {
            Fonts.Restore();
            InvalidateCommand();
        };
        actions.Children.Add(restore);

        //Upstream's `userguide.addButton(self._button_box, "documentfonts")'.
        //A ContentDialog's three buttons are spent (board trap 50), so Help
        //sits here beside Restore Defaults, on the identifier this dialog
        //records.
        actions.Children.Add(UserGuide.GuideHelp.Button(HelpIdentifier));
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);

        _building = false;
        return root;
    }

    /// <summary>Builds the tab row and its four pages.</summary>
    /// <returns>The left-hand column.</returns>
    private UIElement BuildTabs()
    {
        Grid column = new Grid { RowSpacing = 6, Margin = new Thickness(0, 0, 6, 0) };
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });

        (string Title, Func<UIElement> Build)[] pages =
        {
            (I18n.Get("Text Fonts"), BuildTextFontsPage),
            (I18n.Get("Music Fonts"), BuildMusicFontsPage),
            (I18n.Get("Font Command"), BuildFontCommandPage),
            (I18n.Get("Miscellaneous"), BuildMiscellaneousPage),
        };

        StackPanel tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
        };
        _pages = new Grid();
        List<Button> buttons = new List<Button>();

        for (int index = 0; index < pages.Length; index++)
        {
            UIElement page = pages[index].Build();
            page.Visibility = Visibility.Collapsed;
            _pages.Children.Add(page);

            Button tab = new Button
            {
                Content = MenuBuilder.Display(pages[index].Title),
                Padding = new Thickness(10, 4, 10, 4),
            };
            int wanted = index;
            tab.Click += (_, _) => ShowPage(wanted);
            buttons.Add(tab);
            tabs.Children.Add(tab);
        }

        _tabs = buttons;
        Grid.SetRow(_pages, 1);
        column.Children.Add(tabs);
        column.Children.Add(_pages);
        ShowPage(_currentPage);
        return column;
    }

    /// <summary>Shows one page and marks its tab.</summary>
    /// <param name="index">The page.</param>
    private void ShowPage(int index)
    {
        _currentPage = index;
        for (int page = 0; page < _pages.Children.Count; page++)
        {
            _pages.Children[page].Visibility = page == index
                ? Visibility.Visible
                : Visibility.Collapsed;
            _tabs[page].FontWeight = page == index
                ? FontWeights.SemiBold
                : FontWeights.Normal;
        }
    }

    // ------------------------------------------------------------ text fonts

    /// <summary>Builds the Text Fonts tab.</summary>
    /// <returns>The page.</returns>
    private UIElement BuildTextFontsPage()
    {
        Grid page = new Grid { RowSpacing = 4 };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _textStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        page.Children.Add(_textStatus);

        _textTree = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Single,
            CanDragItems = false,
            CanReorderItems = false,
        };
        _textTree.ItemInvoked += (_, e) =>
        {
            if (e.InvokedItem is TreeViewNode node) { _textTree.SelectedNode = node; }
        };
        Grid.SetRow(_textTree, 1);
        page.Children.Add(_textTree);

        _textFilter = new TextBox
        {
            PlaceholderText = I18n.Get(
                "Filter results (type any part of the font family name. "
                + "Regular Expressions supported.)"),
        };
        _textFilter.TextChanged += (_, _) => RefreshTextFonts();
        Grid.SetRow(_textFilter, 2);
        page.Children.Add(_textFilter);

        //was previously: upstream offers "Set as Roman/Sans/Typewriter
        //(current: X)" on a right-click context menu over the tree. Three
        //buttons say the same thing with no hidden gesture, and they carry the
        //same "(current: …)" reading in their tool tips.
        StackPanel setters = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        foreach (var (caption, family) in new[]
        {
            (I18n.Get("Roman"), "roman"),
            (I18n.Get("Sans"), "sans"),
            (I18n.Get("Typewriter"), "typewriter"),
        })
        {
            Button button = new Button
            {
                Content = I18n.Format(
                    I18n.Get("Set as {family}"), ("family", caption)),
            };
            string key = family;
            ToolTipService.SetToolTip(button, ToolTipFor(key, caption));
            button.Click += (_, _) =>
            {
                string chosen = SelectedTextFontFamily();
                if (chosen == null) { return; }

                Fonts[key] = chosen;
                ToolTipService.SetToolTip(button, ToolTipFor(key, caption));
                InvalidateCommand();
            };
            setters.Children.Add(button);
        }

        Grid.SetRow(setters, 3);
        page.Children.Add(setters);

        _textFonts = TextFontWorld.Load();
        RefreshTextFonts();
        return page;
    }

    /// <summary>Answers the "Set as X (current: Y)" tool tip.</summary>
    /// <param name="family">The family key.</param>
    /// <param name="caption">Its caption.</param>
    /// <returns>The tool tip.</returns>
    private string ToolTipFor(string family, string caption)
        => I18n.Format(
            I18n.Get("Set as {family} (current: {current})"),
            ("family", caption),
            ("current", Fonts[family]));

    /// <summary>Fills the text-font tree, applying the filter.</summary>
    private void RefreshTextFonts()
    {
        if (_textTree == null || _textFonts == null) { return; }

        IReadOnlyList<string> notationFonts = _musicFonts?.Families()
            ?? (IReadOnlyList<string>)Array.Empty<string>();
        _textChoices = _textFonts.FilterSelectable(
            _textFilter?.Text ?? string.Empty, notationFonts.ToList());

        _textTree.RootNodes.Clear();
        foreach (TextFontSelector choice in _textChoices)
        {
            TreeViewNode node = new TreeViewNode
            {
                Content = choice.Name + "    " + string.Join(", ", choice.FamilyNames),
            };
            foreach (TextFontFace face in choice.Faces)
            {
                node.Children.Add(new TreeViewNode
                {
                    Content = face.Family + "    " + face.FileName + "    " + face.Location,
                });
            }

            _textTree.RootNodes.Add(node);
        }

        //Upstream: "{count} font families detected by {version}", where the
        //version is the LilyPond install the fonts were listed from. FR13: the
        //engine the user drives is LilyPort. The count is of the names a
        //document can ASK FOR — see TextFontWorld.Selectors for why that is
        //not the same as the number of families the engine ships.
        _textStatus.Text = I18n.Format(
            I18n.Get("{count} font families detected by {version}"),
            ("count", _textChoices.Count),
            ("version", "LilyPort " + LilyPortEngine.PortVersion));
    }

    /// <summary>Answers the name the text tree has selected.</summary>
    /// <returns>The name a document would ask for, or null.</returns>
    /// <remarks>Upstream reads the selected index, and takes its PARENT's data
    /// when a FACE row rather than a name row is selected — the name is what
    /// goes in the command either way.</remarks>
    private string SelectedTextFontFamily()
    {
        TreeViewNode node = _textTree?.SelectedNode;
        if (node == null || _textChoices == null) { return null; }

        TreeViewNode top = node;
        while (top.Parent != null && _textTree.RootNodes.IndexOf(top) < 0)
        {
            top = top.Parent;
        }

        int index = _textTree.RootNodes.IndexOf(top);
        return index >= 0 && index < _textChoices.Count
            ? _textChoices[index].Name
            : null;
    }

    // ----------------------------------------------------------- music fonts

    /// <summary>Builds the Music Fonts tab.</summary>
    /// <returns>The page.</returns>
    private UIElement BuildMusicFontsPage()
    {
        Grid page = new Grid { RowSpacing = 4 };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });

        StackPanel buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };

        _installRepo = new Button { Content = I18n.Get("Install (repo)") };
        ToolTipService.SetToolTip(_installRepo, I18n.Get(
            "Install fonts from the global music font repository\n"
            + "into the folder LilyPort searches."));
        _installRepo.Click += async (_, _) => await InstallAsync(fromRepo: true);
        buttons.Children.Add(_installRepo);

        _install = new Button { Content = I18n.Get("Install...") };
        ToolTipService.SetToolTip(_install, I18n.Get(
            "Install fonts from a directory into the folder LilyPort searches"));
        _install.Click += async (_, _) => await InstallAsync(fromRepo: false);
        buttons.Children.Add(_install);

        //Upstream's Download button, disabled: "Not implemented yet". Its
        //`download.py' is the one module of the fonts package this port drops,
        //because upstream's own button never calls it.
        _download = new Button { Content = I18n.Get("Download..."), IsEnabled = false };
        ToolTipService.SetToolTip(_download, I18n.Get(
            "Download music fonts from a repository on Github.\n"
            + "NOTE: Not implemented yet."));
        buttons.Children.Add(_download);

        _remove = new Button { Content = I18n.Get("Remove..."), IsEnabled = false };
        ToolTipService.SetToolTip(_remove, I18n.Get("Remove selected music font"));
        _remove.Click += async (_, _) => await RemoveAsync();
        buttons.Children.Add(_remove);

        page.Children.Add(buttons);

        _musicStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
        Grid.SetRow(_musicStatus, 1);
        page.Children.Add(_musicStatus);

        _musicTree = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Single,
            CanDragItems = false,
            CanReorderItems = false,
        };
        //⚠ Board trap 45: a TreeView answers a SINGLE click through
        //ItemInvoked, and that is the event this application's other trees use.
        //SelectionChanged alone did not fire here.
        _musicTree.ItemInvoked += (_, e) =>
        {
            if (e.InvokedItem is TreeViewNode node)
            {
                _musicTree.SelectedNode = node;
            }

            MusicFontSelectionChanged();
        };
        _musicTree.SelectionChanged += (_, _) => MusicFontSelectionChanged();
        Grid.SetRow(_musicTree, 2);
        page.Children.Add(_musicTree);

        RefreshMusicFonts();
        return page;
    }

    /// <summary>Fills the music-font list.</summary>
    private void RefreshMusicFonts()
    {
        if (_musicTree == null) { return; }

        _musicTree.RootNodes.Clear();
        foreach (string name in _musicFonts.Families())
        {
            MusicFontFamily family = _musicFonts.Family(name);
            _musicTree.RootNodes.Add(new TreeViewNode
            {
                Content = new MusicFontRow(family).Describe(),
            });
        }

        //The engine's own faces are EMBEDDED, so an empty folder is the normal
        //state and has to read as one rather than as a failure.
        _musicStatus.Text = _musicFonts.Families().Count == 0
            ? I18n.Format(
                I18n.Get(
                    "No music fonts are installed. {appname} always has "
                    + "emmentaler, which is built into LilyPort; installing a "
                    + "font here adds it to what LilyPort can find."),
                ("appname", AppInfo.AppName))
            : I18n.Format(
                I18n.Get("Installed in {folder}"),
                ("folder", _musicFonts.FontRoot));

        _remove.IsEnabled = false;
        _installRepo.IsEnabled = DocumentFontSettings.MusicFontsRepo(_settings) != null;
        RefreshTextFonts();
    }

    /// <summary>Takes the music font the list has selected.</summary>
    /// <remarks>
    /// Upstream's <c>music_fonts_selection_changed</c>: the brace font follows
    /// the music font when that family HAS a brace face, and falls back to
    /// emmentaler when it has not.
    /// <para>
    /// ⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14). Upstream's
    /// no-selection branch reads
    /// <c>self.window().select_font('emmentaler')</c> — twice — and
    /// <c>select_font</c> takes <c>(family, name)</c>, so deselecting a row
    /// raises <c>TypeError: select_font() missing 1 required positional
    /// argument: 'name'</c>. What was meant is plainly the two lines below:
    /// put the music and brace fonts back to emmentaler.
    /// </para>
    /// </remarks>
    private void MusicFontSelectionChanged()
    {
        string family = SelectedMusicFontFamily();
        if (family != null)
        {
            MusicFontFamily entry = _musicFonts.Family(family);
            Fonts["music"] = family;
            Fonts["brace"] = entry != null && entry.HasBrace("otf")
                ? family
                : "emmentaler";
        }
        else
        {
            Fonts["music"] = "emmentaler";
            Fonts["brace"] = "emmentaler";
        }

        _remove.IsEnabled = family != null;
        InvalidateCommand();
    }

    /// <summary>Answers the family the music list has selected.</summary>
    /// <returns>The family name, or null.</returns>
    private string SelectedMusicFontFamily()
    {
        if (_musicTree?.SelectedNode?.Content is not string row) { return null; }

        int index = _musicTree.RootNodes.IndexOf(_musicTree.SelectedNode);
        IReadOnlyList<string> families = _musicFonts.Families();
        return index >= 0 && index < families.Count ? families[index] : null;
    }

    /// <summary>Installs music fonts from the repository or a chosen folder.</summary>
    /// <param name="fromRepo">Whether to use the configured repository.</param>
    /// <returns>The running task.</returns>
    private async Task InstallAsync(bool fromRepo)
    {
        MusicFontRepo repo;
        if (fromRepo)
        {
            repo = DocumentFontSettings.MusicFontsRepo(_settings);
            if (repo == null) { return; }
        }
        else
        {
            Func<UrlRequesterMode, string, Task<string>> pick = PickAsync;
            if (pick == null) { return; }

            string folder = await pick(UrlRequesterMode.Directory, null);
            if (string.IsNullOrEmpty(folder)) { return; }

            repo = new MusicFontRepo(folder);
        }

        try
        {
            repo.FlagForInstall(_musicFonts);
            repo.InstallFlagged(_musicFonts);
            if (fromRepo) { _installRepo.IsEnabled = false; }
        }
        catch (MusicFontPermissionException error)
        {
            _musicStatus.Text = I18n.Get("Fonts could not be installed!")
                + "\n" + error.Message;
            return;
        }
        catch (MusicFontException error)
        {
            _musicStatus.Text = error.Message;
            return;
        }

        _musicFonts.Reload();
        RefreshMusicFonts();
        InvalidateCommand();
    }

    /// <summary>Removes the selected music font family.</summary>
    /// <returns>The running task.</returns>
    private Task RemoveAsync()
    {
        string family = SelectedMusicFontFamily();
        if (family == null) { return Task.CompletedTask; }

        try
        {
            _musicFonts.Remove(new[] { family });
        }
        catch (MusicFontFileRemoveException error)
        {
            _musicStatus.Text = I18n.Get("Font family could not be removed!")
                + "\n"
                + I18n.Format(
                    I18n.Get(
                        "{appname} only removes music fonts it installed into "
                        + "its own font folder. This family includes files "
                        + "from elsewhere and is left alone."),
                    ("appname", AppInfo.AppName))
                + "\n" + error.Message;
            return Task.CompletedTask;
        }
        catch (MusicFontPermissionException error)
        {
            _musicStatus.Text = error.Message;
            return Task.CompletedTask;
        }

        _musicFonts.Reload();
        RefreshMusicFonts();
        InvalidateCommand();
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------- font command

    /// <summary>Builds the Font Command tab.</summary>
    /// <returns>The page.</returns>
    private UIElement BuildFontCommandPage()
    {
        Grid page = new Grid { ColumnSpacing = 8 };
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        page.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        StackPanel options = new StackPanel { Spacing = 10 };

        //"Set font families": the three text families, each with the name it
        //is currently set to beside its tick.
        StackPanel families = new StackPanel { Spacing = 2 };
        _cbRoman = FamilyRow(families, I18n.Get("Roman"), "roman");
        _cbSans = FamilyRow(families, I18n.Get("Sans"), "sans");
        _cbTypewriter = FamilyRow(families, I18n.Get("Typewriter"), "typewriter");
        options.Children.Add(SettingsEditor.Wrap(I18n.Get("Set font families"), families));

        //"Configure command generation": upstream's two-tab choice becomes a
        //ComboBox over one panel at a time (board trap 40, and the same
        //substitution the preferences pages make for a radio group).
        StackPanel generation = new StackPanel { Spacing = 6 };
        _approach = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _approach.Items.Add(new ComboBoxItem { Content = I18n.Get("Traditional") });
        _approach.Items.Add(new ComboBoxItem { Content = I18n.Get("openLilyLib") });
        ToolTipService.SetToolTip(_approach, I18n.Get(
            "Specify fonts using the setting in a \\paper block."));
        generation.Children.Add(_approach);

        StackPanel traditional = new StackPanel { Spacing = 2 };
        _cbMusic = FamilyRow(traditional, I18n.Get("Set music font"), "music");
        _cbPaperBlock = new CheckBox { Content = I18n.Get("Complete \\paper block") };
        ToolTipService.SetToolTip(_cbPaperBlock, I18n.Get(
            "Wrap setting in a complete \\paper block.\n"
            + "If unchecked, generate the raw font setting command."));
        _cbPaperBlock.Checked += (_, _) => InvalidateCommand();
        _cbPaperBlock.Unchecked += (_, _) => InvalidateCommand();
        traditional.Children.Add(_cbPaperBlock);
        generation.Children.Add(traditional);

        StackPanel oll = new StackPanel { Spacing = 2, Visibility = Visibility.Collapsed };
        CheckBox ollMusic = new CheckBox
        {
            Content = I18n.Get("Set music font"),
            IsChecked = true,
            IsEnabled = false,
        };
        ToolTipService.SetToolTip(ollMusic, I18n.Get(
            "Specify the music font.\n"
            + "This is a reminder only and can not be unchecked "
            + "because the openLilyLib approach necessarily sets "
            + "the music font."));
        oll.Children.Add(ollMusic);

        _cbOll = new CheckBox { Content = I18n.Get("Load openLilyLib") };
        ToolTipService.SetToolTip(_cbOll, I18n.Get(
            "Load openLilyLib (oll-core) explicitly.\n"
            + "Uncheck if oll-core is already loaded elsewhere."));
        _cbOll.Checked += (_, _) => InvalidateCommand();
        _cbOll.Unchecked += (_, _) => InvalidateCommand();
        oll.Children.Add(_cbOll);

        _cbLoadPackage = new CheckBox { Content = I18n.Get("Load notation-fonts package") };
        ToolTipService.SetToolTip(_cbLoadPackage, I18n.Get(
            "Load the notation-fonts package explicitly.\n"
            + "Uncheck if it is already loaded elsewhere."));
        _cbLoadPackage.Checked += (_, _) => InvalidateCommand();
        _cbLoadPackage.Unchecked += (_, _) => InvalidateCommand();
        oll.Children.Add(_cbLoadPackage);

        _cbExtensions = new CheckBox
        {
            Content = I18n.Get("Load font extensions (if available)"),
        };
        ToolTipService.SetToolTip(_cbExtensions, I18n.Get(
            "Ask for loading font extensions.\n"
            + "Note that *some* fonts provide additional features\n"
            + "(e.g. glyphs) that can be made available through an\n"
            + "extension stylesheet if provided."));
        _cbExtensions.Checked += (_, _) => InvalidateCommand();
        _cbExtensions.Unchecked += (_, _) => InvalidateCommand();
        oll.Children.Add(_cbExtensions);

        StackPanel stylesheet = new StackPanel { Spacing = 4 };
        _styleType = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _styleType.Items.Add(new ComboBoxItem { Content = I18n.Get("Default stylesheet") });
        _styleType.Items.Add(new ComboBoxItem { Content = I18n.Get("No stylesheet") });
        _styleType.Items.Add(new ComboBoxItem { Content = I18n.Get("Custom stylesheet") });
        _styleType.SelectionChanged += (_, _) =>
        {
            if (_styleSheet != null)
            {
                _styleSheet.IsEnabled = _styleType.SelectedIndex == 2;
            }

            InvalidateCommand();
        };
        stylesheet.Children.Add(_styleType);

        _styleSheet = new TextBox { IsEnabled = false };
        _styleSheet.LostFocus += (_, _) => InvalidateCommand();
        stylesheet.Children.Add(_styleSheet);

        StackPanel stylesheetGroup = new StackPanel();
        stylesheetGroup.Children.Add(
            SettingsEditor.Wrap(I18n.Get("Font stylesheet"), stylesheet));
        //was previously: upstream's own text, which names LilyPond twice —
        //"LilyPond's visuals" and "in LilyPond's search path". A tooltip is
        //chrome and FR13 names tooltips; the engine here is LilyPort, and its
        //search path is the one a custom stylesheet is looked for on. The new
        //msgid is in the harvest tool's renamed-string table.
        ToolTipService.SetToolTip(stylesheetGroup, I18n.Get(
            "Select alternative stylesheet.\n"
            + "Fonts natively supported by the notation-fonts\n"
            + "package provide a default stylesheet to adjust\n"
            + "the engraver's visuals (e.g. line thicknesses) to the\n"
            + "characteristic of the music font.\n"
            + "Check 'No stylesheet' to avoid a preconfigured\n"
            + "stylesheet to customize the appearance manually,\n"
            + "or check 'Custom stylesheet' to load another stylesheet\n"
            + "on the engraver's search path."));
        oll.Children.Add(stylesheetGroup);
        generation.Children.Add(oll);

        _approach.SelectionChanged += (_, _) =>
        {
            bool openLilyLib = _approach.SelectedIndex == 1;
            traditional.Visibility = openLilyLib
                ? Visibility.Collapsed
                : Visibility.Visible;
            oll.Visibility = openLilyLib ? Visibility.Visible : Visibility.Collapsed;
            ToolTipService.SetToolTip(_approach, openLilyLib
                ? I18n.Get(
                    "Specify fonts using the setting using openLilyLib.\n"
                    + "NOTE: This requires openLilyLib (oll-core)\n"
                    + "and the 'notation-fonts' openLilyLib package.")
                : I18n.Get("Specify fonts using the setting in a \\paper block."));
            InvalidateCommand();
        };

        options.Children.Add(
            SettingsEditor.Wrap(I18n.Get("Configure command generation"), generation));

        //A scroller of the column's own. This is the tallest of the four pages
        //— two groups of check boxes, two combo boxes and a text box — and in
        //a short window it is the one thing here that cannot shrink. Upstream's
        //own tab is a QScrollArea for the same reason. //was previously: the
        //StackPanel went straight into the column and was clipped instead.
        ScrollViewer optionsScroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            Content = options,
        };
        page.Children.Add(optionsScroller);

        _commandText = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            //was previously: NoWrap. It is the one piece of content in this
            //dialog that genuinely cannot shrink — a long font name makes a long
            //`\paper' line — so in a narrow window it degrades by WRAPPING
            //rather than by being clipped away.
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Roboto Mono"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetColumn(_commandText, 1);
        page.Children.Add(_commandText);

        //The controls take the loaded state only now that they all exist, so
        //nothing regenerates the command half-built.
        ApplyOptionsToControls();
        return page;
    }

    /// <summary>Adds a tick with the chosen font's name beside it.</summary>
    /// <param name="panel">Where the row goes.</param>
    /// <param name="caption">The tick's caption.</param>
    /// <param name="family">The family the name comes from.</param>
    /// <returns>The tick.</returns>
    private CheckBox FamilyRow(StackPanel panel, string caption, string family)
    {
        Grid row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        CheckBox tick = new CheckBox { Content = caption };
        tick.Checked += (_, _) => InvalidateCommand();
        tick.Unchecked += (_, _) => InvalidateCommand();
        row.Children.Add(tick);

        TextBlock label = new TextBlock
        {
            Text = Fonts[family],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        _fontLabels[family] = label;

        panel.Children.Add(row);
        return tick;
    }

    /// <summary>Puts the loaded options into the controls.</summary>
    private void ApplyOptionsToControls()
    {
        _building = true;
        _cbMusic.IsChecked = _options.SetMusic;
        _cbRoman.IsChecked = _options.SetRoman;
        _cbSans.IsChecked = _options.SetSans;
        _cbTypewriter.IsChecked = _options.SetTypewriter;
        _cbPaperBlock.IsChecked = _options.SetPaperBlock;
        _cbOll.IsChecked = _options.LoadOll;
        _cbLoadPackage.IsChecked = _options.LoadPackage;
        _cbExtensions.IsChecked = _options.FontExtensions;
        _styleType.SelectedIndex = Math.Clamp(_options.StyleType, 0, 2);
        _styleSheet.Text = _options.FontStylesheet ?? string.Empty;
        _styleSheet.IsEnabled = _options.StyleType == 2;
        _approach.SelectedIndex = _options.Approach == FontCommandApproach.OpenLilyLib
            ? 1
            : 0;
        _building = false;
    }

    /// <summary>Reads the controls back into the options.</summary>
    private void ReadOptionsFromControls()
    {
        if (_cbMusic == null) { return; }

        _options.SetMusic = _cbMusic.IsChecked == true;
        _options.SetRoman = _cbRoman.IsChecked == true;
        _options.SetSans = _cbSans.IsChecked == true;
        _options.SetTypewriter = _cbTypewriter.IsChecked == true;
        _options.SetPaperBlock = _cbPaperBlock.IsChecked == true;
        _options.LoadOll = _cbOll.IsChecked == true;
        _options.LoadPackage = _cbLoadPackage.IsChecked == true;
        _options.FontExtensions = _cbExtensions.IsChecked == true;
        _options.StyleType = Math.Max(0, _styleType.SelectedIndex);
        _options.FontStylesheet = _styleSheet.Text ?? string.Empty;
        _options.Approach = _approach.SelectedIndex == 1
            ? FontCommandApproach.OpenLilyLib
            : FontCommandApproach.Lily;
    }

    /// <summary>Gets the command as the Font Command tab shows it.</summary>
    /// <returns>The command and its full form.</returns>
    private FontCommandText CurrentCommand()
    {
        ReadOptionsFromControls();
        return FontCommand.Generate(_options.Approach, Fonts, _options);
    }

    /// <summary>
    /// Regenerates the command, redraws the font names and asks for a new
    /// sample.
    /// </summary>
    /// <remarks>Upstream's <c>invalidate_command</c>, which is connected to
    /// every control that can change the answer.</remarks>
    private void InvalidateCommand()
    {
        if (_building) { return; }

        FontCommandText command = CurrentCommand();
        if (_commandText != null) { _commandText.Text = command.Command; }

        foreach (var (family, label) in _fontLabels) { label.Text = Fonts[family]; }

        RefreshMiscellaneous();
        _ = ShowSampleAsync(command.Full);
    }

    // -------------------------------------------------------- miscellaneous

    /// <summary>Builds the Miscellaneous tab.</summary>
    /// <returns>The page.</returns>
    private UIElement BuildMiscellaneousPage()
    {
        Grid page = new Grid { RowSpacing = 4 };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });

        //was previously: upstream's "Fontconfig data:". There is no
        //fontconfig here and D23 forbids ever acquiring one, so the label says
        //what the tree beneath it actually holds (ruling R18).
        page.Children.Add(new TextBlock
        {
            Text = I18n.Get("Where LilyPort looks for fonts:"),
        });

        _miscTree = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.None,
            CanDragItems = false,
            CanReorderItems = false,
        };
        Grid.SetRow(_miscTree, 1);
        page.Children.Add(_miscTree);

        RefreshMiscellaneous();
        return page;
    }

    /// <summary>Fills the Miscellaneous tree.</summary>
    private void RefreshMiscellaneous()
    {
        if (_miscTree == null || _textFonts == null) { return; }

        _miscTree.RootNodes.Clear();
        foreach (var (title, entries) in _textFonts.Describe(_musicFonts?.FontRoot))
        {
            TreeViewNode group = new TreeViewNode
            {
                Content = title,
                IsExpanded = true,
            };
            foreach (string entry in entries)
            {
                group.Children.Add(new TreeViewNode { Content = entry });
            }

            _miscTree.RootNodes.Add(group);
        }
    }

    // --------------------------------------------------------------- preview

    /// <summary>Builds the preview pane.</summary>
    /// <returns>The right-hand column.</returns>
    private UIElement BuildPreview()
    {
        Grid pane = new Grid { RowSpacing = 4, Margin = new Thickness(6, 0, 0, 0) };
        pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pane.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });

        Grid chooser = new Grid { ColumnSpacing = 6 };
        chooser.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        chooser.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        //Upstream's own label, which it forgot to put through _() — so this is
        //the first time the string is translatable.
        chooser.Children.Add(new TextBlock
        {
            Text = I18n.Get("Example:"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        _samples = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (FontSample sample in FontSamples.Provided)
        {
            ComboBoxItem item = new ComboBoxItem { Content = sample.Label(), Tag = sample.Id };
            ToolTipService.SetToolTip(item, sample.ToolTip());
            _samples.Items.Add(item);
        }

        ComboBoxItem custom = new ComboBoxItem
        {
            Content = I18n.Get("Custom"),
            Tag = FontSamples.CustomId,
        };
        ToolTipService.SetToolTip(custom, I18n.Get(
            "Use custom sample for music font.\n"
            + "NOTE: This should not include a version statement "
            + "or a \\paper {...} block."));
        _samples.Items.Add(custom);

        ComboBoxItem current = new ComboBoxItem
        {
            Content = I18n.Get("Current Document"),
            Tag = FontSamples.CurrentId,
        };
        ToolTipService.SetToolTip(current, I18n.Get(
            "Use current document as music font sample.\n"
            + "NOTE: This is not robust if the document contains "
            + "a \\paper {...} block."));
        _samples.Items.Add(current);

        Grid.SetColumn(_samples, 1);
        chooser.Children.Add(_samples);
        pane.Children.Add(chooser);

        _customSample = new UrlRequester(UrlRequesterMode.ExistingFile, mustExist: true)
        {
            PickAsync = PickAsync,
            DialogTitle = I18n.Get("Select sample score"),
        };
        SetRequesterEnabled(_customSample, false);
        ToolTipService.SetToolTip(_customSample, I18n.Get(
            "Use custom sample for music font.\n"
            + "NOTE: This should not include a version statement "
            + "or a \\paper {...} block."));
        _customSample.EditingFinished += (_, _) => InvalidateCommand();
        Grid.SetRow(_customSample, 1);
        pane.Children.Add(_customSample);

        Grid surface = new Grid();
        _preview = new View.MusicViewControl
        {
            ViewMode = View.ViewMode.FitWidth,
            LinksEnabled = false,
        };
        surface.Children.Add(_preview);

        _previewStatus = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85,
        };
        surface.Children.Add(_previewStatus);

        Grid.SetRow(surface, 2);
        pane.Children.Add(surface);

        string remembered = _settings?.GetString(
            DocumentFontSettings.Key("default-music-sample"), string.Empty);
        _samples.SelectedIndex = Math.Max(0, IndexOfSample(remembered));
        _customSample.Path = _settings?.GetString(
            DocumentFontSettings.Key("custom-music-sample-url"), string.Empty);
        _samples.SelectionChanged += (_, _) =>
        {
            SetRequesterEnabled(_customSample, string.Equals(
                SelectedSampleId(), FontSamples.CustomId, StringComparison.Ordinal));
            InvalidateCommand();
        };

        return pane;
    }

    /// <summary>Greys a path requester out.</summary>
    /// <param name="requester">The requester.</param>
    /// <param name="enabled">Whether it is usable.</param>
    /// <remarks><see cref="UrlRequester"/> is a <c>Grid</c> rather than a
    /// <c>Control</c>, so it has no <c>IsEnabled</c> of its own; this says the
    /// same thing the way a panel can.</remarks>
    private static void SetRequesterEnabled(UrlRequester requester, bool enabled)
    {
        requester.IsHitTestVisible = enabled;
        requester.Opacity = enabled ? 1.0 : 0.5;
    }

    /// <summary>Answers where a sample id sits in the chooser.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The index, or -1.</returns>
    private int IndexOfSample(string id)
    {
        if (string.IsNullOrEmpty(id)) { return -1; }

        for (int index = 0; index < _samples.Items.Count; index++)
        {
            if (_samples.Items[index] is ComboBoxItem item
                && string.Equals(item.Tag as string, id, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Answers the chosen sample's id.</summary>
    /// <returns>The id.</returns>
    private string SelectedSampleId()
        => _samples?.SelectedItem is ComboBoxItem item
            ? item.Tag as string ?? string.Empty
            : string.Empty;

    /// <summary>
    /// Engraves the chosen sample with the current fonts and shows it.
    /// </summary>
    /// <param name="fullCommand">The full font command.</param>
    /// <returns>The running task.</returns>
    /// <remarks>Upstream's <c>show_sample</c>: the provided samples are cached
    /// between runs (their source cannot change), a custom file or the current
    /// document only for the run.</remarks>
    private async Task ShowSampleAsync(string fullCommand)
    {
        if (_preview == null || _engine == null) { return; }

        string id = SelectedSampleId();
        string content;
        string baseDirectory = null;

        if (string.Equals(id, FontSamples.CurrentId, StringComparison.Ordinal))
        {
            content = CurrentDocumentText?.Invoke() ?? string.Empty;
            baseDirectory = CurrentDocumentDirectory?.Invoke();
        }
        else
        {
            string file = string.Equals(id, FontSamples.CustomId, StringComparison.Ordinal)
                ? _customSample?.Path
                : FontSamples.TemplatePath(
                    string.IsNullOrEmpty(id) ? FontSamples.Provided[0].Id : id);

            //Upstream: a "Custom" choice with no file falls back to the first
            //provided sample.
            if (string.IsNullOrEmpty(file) || !System.IO.File.Exists(file))
            {
                file = FontSamples.TemplatePath(FontSamples.Provided[0].Id);
                id = FontSamples.Provided[0].Id;
            }

            if (!System.IO.File.Exists(file))
            {
                _previewStatus.Text = I18n.Get("The sample score is not installed.");
                return;
            }

            baseDirectory = System.IO.Path.GetDirectoryName(file);
            content = System.IO.File.ReadAllText(file);
        }

        string source = FontSamples.Compose(
            LilyPortEngine.CompatibleWithVersion, fullCommand, content);
        if (string.Equals(source, _lastPreviewSource, StringComparison.Ordinal)) { return; }

        _lastPreviewSource = source;
        _previewStatus.Text = I18n.Get("Engraving...");

        string cacheDirectory = FontSamples.CachePersistently(id)
            ? DocumentFontSettings.PersistentCacheDirectory(_settings)
            : null;

        VolatileTextJob job = cacheDirectory != null
            ? new CachedPreviewJob(
                _engine, source, cacheDirectory, I18n.Get("Music font preview"),
                baseDirectory)
            : new VolatileTextJob(
                _engine, source, I18n.Get("Music font preview"), baseDirectory);

        VolatileTextJob previous = _previewJob;
        _previewJob = job;

        try
        {
            if (job.NeedsCompilation()) { await job.StartAsync(); }

            if (!ReferenceEquals(_previewJob, job)) { return; }

            IReadOnlyList<string> pages = job.ResultFiles;
            if (pages.Count == 0)
            {
                _previewStatus.Text = I18n.Get("Engraving failed.");
                _preview.SetDocument(null);
                return;
            }

            _previewStatus.Text = string.Empty;
            _preview.SetDocument(View.MusicDocument.LoadSvgs(pages, _typefaces));
        }
        catch (Exception error)
        {
            _previewStatus.Text = error.Message;
        }
        finally
        {
            if (previous != null && !ReferenceEquals(previous, job)) { previous.Cleanup(); }
        }
    }

    /// <summary>Throws the last preview's scratch directory away.</summary>
    private void CleanupPreview()
    {
        _preview?.SetDocument(null);
        _previewJob?.Cleanup();
        _previewJob = null;
    }

    /// <summary>Makes sure a command ends in a newline.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The command, newline-terminated.</returns>
    /// <remarks>Upstream does this in two places, once for the clipboard and
    /// once for the insertion.</remarks>
    private static string WithNewline(string command)
    {
        command ??= string.Empty;
        return command.EndsWith('\n') ? command : command + "\n";
    }
}
