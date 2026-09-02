// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Editor;
using Fresco.Brix.Services;
using Fresco.Brix.UserGuide;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/userguide/browser.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The user guide's own window: a page, a way back to the ones before it, and
/// the way to the index and the table of contents.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>userguide.browser.Window</c> is a <c>QMainWindow</c> with a
/// toolbar — Back, Forward, Start, Contents, Print — over a
/// <c>QTextBrowser</c>. Four of those five are here, in a dialog rather than a
/// second top-level window because that is this application's own shape for a
/// reader (the Score Wizard, the Document Fonts dialog and the About window are
/// all dialogs). Print is not: ruling FR5.5 says no printing, ever.
/// </para>
/// <para>
/// ⚠ RULING FR8 — there is no web view. The page is DRAWN by
/// <see cref="GuideRenderer"/> from the same parse tree upstream renders to
/// HTML.
/// </para>
/// <para>
/// The instance is kept for the life of the window, exactly as upstream keeps
/// its module-level <c>_browser</c>, so the history behind Back survives
/// closing and re-opening the guide.
/// </para>
/// </remarks>
public sealed class UserGuideDialog
{
    private readonly GuideLibrary _library;
    private readonly GuideRenderer _renderer;
    private readonly List<string> _history = new List<string>();
    private readonly SettingsStore _settings;

    private int _position = -1;
    private ContentDialog _dialog;
    private ScrollViewer _scroller;
    private TextBlock _title;
    private Button _back;
    private Button _forward;

    /// <summary>Creates the guide over the application's own pages.</summary>
    /// <param name="settings">Where the colour scheme is read from, or null.</param>
    /// <param name="actions">The action collections a page's shortcut variable
    /// is looked up in, or null.</param>
    public UserGuideDialog(
        SettingsStore settings = null, ActionCollectionManager actions = null)
        : this(new GuideLibrary(), settings, actions) { }

    /// <summary>Creates the guide over a library of pages.</summary>
    /// <param name="library">The pages.</param>
    /// <param name="settings">Where the colour scheme is read from, or null.</param>
    /// <param name="actions">The action collections, or null.</param>
    public UserGuideDialog(
        GuideLibrary library,
        SettingsStore settings = null,
        ActionCollectionManager actions = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _settings = settings;

        //Upstream's resolver reaches into `actioncollectionmanager' for a
        //{shortcut} variable; here the lookup is handed in, so a page shows the
        //key the user has actually bound rather than upstream's default.
        _library.Context.Shortcut = (collection, action) =>
            actions?.Action(collection, action) is AppAction found
            && found.Shortcuts.Count > 0
                ? found.Shortcuts[0].ToString()
                : null;

        _renderer = new GuideRenderer(_library)
        {
            Navigate = page => Display(page),
            OpenExternal = OpenExternalUrl,
        };
    }

    /// <summary>Gets the pages the guide is showing.</summary>
    public GuideLibrary Library => _library;

    /// <summary>
    /// Gets or sets what an error opening an external link is reported through.
    /// </summary>
    public Action<string> ReportError { get; set; }

    /// <summary>Puts the guide in front of the user, on a page.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="page">The page, or null for the one last looked at.</param>
    /// <returns>The running task.</returns>
    public async Task ShowAsync(XamlRoot xamlRoot, string page = null)
    {
        if (_dialog != null)
        {
            //Already up — this is a Help button pressed from a dialog the guide
            //is in front of. Just turn to the page.
            Display(page ?? GuideLibrary.IndexPage);
            return;
        }

        _dialog = new ContentDialog
        {
            Title = I18n.Format(
                I18n.Get("{appname} User Guide"), ("appname", AppInfo.AppName)),
            Content = BuildContent(),
            CloseButtonText = I18n.Get("Close"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };

        //⚠ The width is the RESOURCE, not MaxWidth (board trap 43).
        _dialog.Resources["ContentDialogMaxWidth"] = 940.0;
        _dialog.Resources["ContentDialogMaxHeight"] = 780.0;

        //The system help key, which upstream puts on the button box's Help
        //button, opens the index from anywhere in the guide.
        _dialog.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.F1)
            {
                Display(GuideLibrary.IndexPage);
                args.Handled = true;
            }
        };

        Display(page ?? Current ?? GuideLibrary.IndexPage);

        try
        {
            await _dialog.ShowAsync();
        }
        finally
        {
            _dialog = null;
            _scroller = null;
        }
    }

    /// <summary>Draws one page, for somewhere other than this window.</summary>
    /// <param name="page">The page name.</param>
    /// <returns>The drawn page, or null when the guide is not installed.</returns>
    /// <remarks>The About window's Credits panel is upstream's own caller:
    /// <c>about.py</c> puts <c>userguide.page.Page('credits').body()</c> in a
    /// text browser. Links to other PAGES are not followed from there — as
    /// upstream's own About does not follow them either — and the credits page
    /// carries only outside links.</remarks>
    public UIElement RenderPage(string page)
    {
        if (!_library.Exists(page)) { return null; }

        _renderer.CodeScheme = SchemeFor(_settings);
        _renderer.CodeFont = MonospaceFont();
        //Body only: upstream's About shows `Page(...).body()', which carries no
        //navigation, and the links round a page would go nowhere from there.
        return _renderer.Render(_library.Page(page), withNavigation: false);
    }

    /// <summary>Gets the page being shown, or null.</summary>
    public string Current
        => _position >= 0 && _position < _history.Count ? _history[_position] : null;

    /// <summary>Turns to a page, remembering where the reader came from.</summary>
    /// <param name="page">The page name.</param>
    public void Display(string page)
    {
        if (string.IsNullOrEmpty(page)) { page = GuideLibrary.IndexPage; }

        if (!string.Equals(Current, page, StringComparison.Ordinal))
        {
            //Anything forward of here is replaced, the way a browser's history
            //works and the way upstream's QTextBrowser does.
            if (_position < _history.Count - 1)
            {
                _history.RemoveRange(_position + 1, _history.Count - _position - 1);
            }

            _history.Add(page);
            _position = _history.Count - 1;
        }

        Draw();
    }

    private void Draw()
    {
        if (_scroller == null) { return; }

        string page = Current ?? GuideLibrary.IndexPage;
        _renderer.CodeScheme = SchemeFor(_settings);
        _renderer.CodeFont = MonospaceFont();

        UIElement content = string.Equals(
            page, GuideLibrary.ContentsPage, StringComparison.Ordinal)
            ? ContentsPage()
            : _renderer.Render(_library.Page(page));

        _scroller.Content = content;
        _scroller.ChangeView(null, 0, null, true);
        _title.Text = _library.Title(page);
        _back.IsEnabled = _position > 0;
        _forward.IsEnabled = _position < _history.Count - 1;
    }

    private UIElement ContentsPage()
    {
        //`toc.md' is one variable — {table_of_contents} — and the renderer
        //draws it as real links rather than as the HTML list upstream's
        //`resolve.table_of_contents' writes. Rendering the PAGE is what does
        //that; this exists so a missing toc.md still shows a contents list.
        return _library.Exists(GuideLibrary.ContentsPage)
            ? _renderer.Render(_library.Page(GuideLibrary.ContentsPage))
            : _renderer.RenderContents();
    }

    private UIElement BuildContent()
    {
        _back = ToolButton("◀", I18n.Get("Back"), () => Move(-1));
        _forward = ToolButton("▶", I18n.Get("Forward"), () => Move(1));
        Button home = ToolButton(
            "⌂", I18n.Get("Start"), () => Display(GuideLibrary.IndexPage));
        Button contents = ToolButton(
            "☰", I18n.Get("Contents"), () => Display(GuideLibrary.ContentsPage));

        //was previously: the toolbar's Print action. Ruling FR5.5 — no
        //printing, ever — so there is no fifth button.

        _title = new TextBlock
        {
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        StackPanel bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
        };
        bar.Children.Add(_back);
        bar.Children.Add(_forward);
        bar.Children.Add(home);
        bar.Children.Add(contents);
        bar.Children.Add(_title);

        _scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 12, 0),
        };

        Grid root = new Grid { RowSpacing = 8, Width = 860, Height = 600 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        root.Children.Add(bar);
        Grid.SetRow(_scroller, 1);
        root.Children.Add(_scroller);

        if (!_library.Exists(GuideLibrary.IndexPage))
        {
            //The folder is droppable, like every other asset folder here.
            _scroller.Content = new TextBlock
            {
                Text = I18n.Get("The user guide is not installed."),
                TextWrapping = TextWrapping.Wrap,
            };
        }

        return root;
    }

    /// <summary>Hands an outside link to the helper application for it.</summary>
    /// <param name="url">The link.</param>
    /// <remarks>Upstream's browser calls <c>helpers.openUrl</c> for anything
    /// whose scheme is not <c>help:</c>; ruling FR8 keeps the same split, with
    /// no web view on either side of it.</remarks>
    private void OpenExternalUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri parsed)) { return; }

        HelperApplications helpers = new HelperApplications(_settings)
        {
            ReportError = message => ReportError?.Invoke(message),
        };
        _ = helpers.OpenUrlAsync(parsed);
    }

    private void Move(int direction)
    {
        int wanted = _position + direction;
        if (wanted < 0 || wanted >= _history.Count) { return; }

        _position = wanted;
        Draw();
    }

    private static Button ToolButton(string caption, string toolTip, Action action)
    {
        Button button = new Button
        {
            Content = caption,
            Padding = new Thickness(10, 4, 10, 4),
        };
        ToolTipService.SetToolTip(button, toolTip);
        button.Click += (_, _) => action();
        return button;
    }

    private FontFamily MonospaceFont()
    {
        TextFormatData data = new TextFormatData(
            TextFormatData.CurrentScheme(_settings), _settings);
        return string.IsNullOrEmpty(data.FontFamily)
            ? new FontFamily("Roboto Mono")
            : new FontFamily(data.FontFamily);
    }

    private static Ly.Colorizing.CssScheme SchemeFor(SettingsStore settings)
        => new TextFormatData(
            TextFormatData.CurrentScheme(settings), settings).ToCssScheme();
}
