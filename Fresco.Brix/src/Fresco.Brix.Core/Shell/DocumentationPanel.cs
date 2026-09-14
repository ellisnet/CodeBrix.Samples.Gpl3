// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.CommandBar;
using Fresco.Brix.Commands;
using Fresco.Brix.Documentation;
using Fresco.Brix.MusicView;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/docbrowser/ (__init__.py + browser.py)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Documentation Browser: the bundled manuals, page by page, with their own
/// tables of contents.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's panel is a QWebEngineView over an installed or remote LilyPond
/// documentation tree, with a chooser of documentation SETS, a find-on-page
/// box, a source viewer for the <c>.ly</c> files its pages link to, and a print
/// dialog. Ruling FR8 replaces the mechanism entirely — the manuals are PDFs
/// rendered by LilyPort itself and shown through
/// <c>CodeBrix.PdfRasterizer</c>, and there is NO WebView anywhere — so what
/// is ported is the JOB rather than the widget:
/// </para>
/// <list type="bullet">
/// <item>the chooser lists the nine MANUALS rather than the installations of
/// one documentation set, because ruling FR5.1 leaves exactly one engine and
/// therefore exactly one set;</item>
/// <item>Back, Forward and Home are upstream's own, over the places a reader
/// has been rather than over a browser history;</item>
/// <item>"Open Current Page in Web Browser" becomes "Open in External Viewer",
/// which hands the whole PDF to the desktop's own reader;</item>
/// <item>the contents list is NEW, and it is the thing a set of PDFs can do
/// that upstream's HTML could not: every manual carries its own bookmark tree,
/// so the panel has a real index — 591 headings for the Notation Reference,
/// 810 for the Internals Reference;</item>
/// <item>Print does NOT survive, permanently, under ruling FR5.5;</item>
/// <item>find-on-page does not survive either, and that is a REAL LOSS worth
/// naming: neither CodeBrix.PdfRasterizer nor CodeBrix.PdfDocuments extracts
/// text, so there is nothing to search. The contents list is what stands in
/// for it, and contextual help (Shift+F9) answers the question a reader most
/// often opened the search box to ask.</item>
/// </list>
/// <para>
/// The view is the SAME control the Music View uses, so zoom, the fit modes,
/// continuous scrolling and paging behave identically in both and on all six
/// heads.
/// </para>
/// </remarks>
public sealed class DocumentationPanel : Panel
{
    /// <summary>The settings group the panel's state lives under.</summary>
    public const string SettingsPrefix = "documentation/";

    private readonly ManualLibrary _library;
    private readonly ContextHelp _contextHelp;
    private readonly SettingsStore _settings;
    private readonly List<Place> _back = new List<Place>();
    private readonly List<Place> _forward = new List<Place>();

    private MusicViewControl _view;
    private ToolBar _toolbar;
    private ComboBox _chooser;
    private ListView _contents;
    private TextBlock _pageLabel;
    private TextBlock _message;
    private Grid _body;
    private IReadOnlyList<ManualDefinition> _listed = Array.Empty<ManualDefinition>();
    private PdfManual _manual;
    private MusicDocument _document;
    private bool _updatingChooser;
    private bool _updatingContents;
    private bool _navigating;

    /// <summary>Creates the documentation panel.</summary>
    /// <param name="library">The bundled manuals.</param>
    /// <param name="actions">The panel's commands.</param>
    /// <param name="settings">The settings store, or null.</param>
    public DocumentationPanel(
        ManualLibrary library,
        DocumentationActions actions = null,
        SettingsStore settings = null)
        : base("docbrowser", DockArea.Right)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _contextHelp = new ContextHelp(_library);
        _settings = settings;
        Actions = actions;

        //Upstream's own panel shortcut.
        ToggleAction.WithShortcut("Meta+Alt+D");

        WireActions();
    }

    /// <summary>Gets the panel's commands.</summary>
    public DocumentationActions Actions { get; }

    /// <summary>
    /// Gets or sets how a file or URL is handed to the desktop.
    /// </summary>
    /// <remarks>The panel does not start programs; the window owns the helper
    /// service that does (upstream's <c>helpers.openUrl</c>).</remarks>
    public Action<string> OpenExternal { get; set; }

    /// <summary>
    /// Gets or sets how the word under the caret is read out of the editor.
    /// </summary>
    /// <remarks>Only the window knows which editor has focus and what its
    /// tokeniser says; the panel only needs the word.</remarks>
    public Func<string> WordAtCursor { get; set; }

    /// <summary>Gets or sets how a message is put on the window's status line.</summary>
    public Action<string> ShowStatus { get; set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Documentation Browser");

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        ToggleAction.Text = I18n.Get("&Documentation Browser");

        //The bar's title is the panel's, and it is read out as the tail of
        //every button's accessible name, so it follows the language too.
        if (_toolbar != null) { _toolbar.Title = Title; }

        UpdatePageLabel();
    }

    /// <summary>Shows a manual, at a page.</summary>
    /// <param name="name">The manual's short name.</param>
    /// <param name="page">The 1-based page.</param>
    /// <returns>The running task.</returns>
    public async Task ShowManualAsync(string name, int page = 1)
    {
        Widget();
        ManualDefinition definition = ManualCatalog.Find(name);
        if (definition == null || !_library.IsInstalled(definition)) { return; }

        await OpenAsync(definition, page, remember: true).ConfigureAwait(true);
    }

    /// <summary>Looks up the word at the caret and shows what documents it.</summary>
    /// <returns>The running task.</returns>
    public async Task ShowContextHelpAsync()
    {
        Widget();
        Activate();

        string word = WordAtCursor?.Invoke();
        ContextHelpTarget target = _contextHelp.Resolve(word);
        if (target == null)
        {
            ShowStatus?.Invoke(I18n.Get("No manuals are installed."));
            return;
        }

        await OpenAsync(target.Manual, target.Page, remember: true).ConfigureAwait(true);

        ShowStatus?.Invoke(target.IsExact
            ? I18n.Format(
                I18n.Get("{term}: {section}"),
                ("term", target.Term), ("section", target.Entry.Title))
            : target.Term == null
                ? I18n.Get("There is no word at the cursor to look up.")
                : I18n.Format(
                    I18n.Get("Nothing in the manuals is headed {term}."),
                    ("term", target.Term)));
    }

    /// <inheritdoc/>
    protected override UIElement CreateWidget()
    {
        _view = new MusicViewControl
        {
            ViewMode = ViewMode.FitWidth,

            //A manual has no point-and-click anchors and nothing to highlight;
            //the link machinery would only cost hit-testing on every move.
            LinksEnabled = false,
        };
        _view.CurrentPageChanged += (_, _) => OnPageChanged();

        _contents = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = ContentsTemplate(),
            Width = 210,
            Visibility = Visibility.Collapsed,
        };
        _contents.SelectionChanged += (_, _) => OnContentsSelected();

        _message = new TextBlock
        {
            Margin = new Thickness(12),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        _body = new Grid();
        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _body.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _body.Children.Add(_contents);
        Grid.SetColumn(_view, 1);
        _body.Children.Add(_view);
        Grid.SetColumn(_message, 1);
        _body.Children.Add(_message);

        Grid root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(
            new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(BuildToolBar());
        Grid.SetRow(_body, 1);
        root.Children.Add(_body);

        FillChooser();
        _ = OpenStartingManualAsync();

        //The panel measures to nothing without this: its children scroll and a
        //dock tab hands out the DESIRED height (board trap 30).
        return new FillGrid { Children = { root } };
    }

    private static DataTemplate ContentsTemplate()
    {
        //Indented by level, so the shape of the manual is visible in a flat
        //list — a TreeView would need a node per heading and the Internals
        //Reference has 810 of them.
        string xaml =
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
            + "<TextBlock Text=\"{Binding Text}\" Margin=\"{Binding Indent}\" "
            + "FontWeight=\"{Binding Weight}\" TextTrimming=\"CharacterEllipsis\" /></DataTemplate>";
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }

    /// <summary>
    /// Builds the toolbar: the manual chooser on its own line, the buttons
    /// under it.
    /// </summary>
    /// <returns>The toolbar.</returns>
    /// <remarks>
    /// <para>
    /// The button row is one CodeBrix.Platform <see cref="ToolBar"/>, so what
    /// does not fit in a narrow dock moves behind the overflow chevron, in
    /// order, and comes back when the room does. It used to be a horizontally
    /// scrolling row with its scrollbar hidden, because the platform had no
    /// chevron and eleven controls in one row simply ran off the edge and the
    /// last of them could not be reached at all (board trap 57).
    /// </para>
    /// <para>
    /// THE CHOOSER KEEPS ITS OWN LINE. Upstream has it inline, at the end of the
    /// one toolbar, because a web view's toolbar carries five buttons; this one
    /// carries thirteen and the chooser's text is a MANUAL'S TITLE — "LilyPond
    /// Learning Manual" — so inline it would either be clipped or push half the
    /// bar behind the chevron. The Manuscript Viewer's chooser, which carries a
    /// file name, IS inline, where upstream has it.
    /// </para>
    /// <para>
    /// THE BAR SHOWS TEXT, not icons: of the six commands on it only Back,
    /// Forward and Open in External Viewer name artwork the two shipped icon
    /// sets carry (<c>go-home</c>, <c>go-up</c> and <c>go-down</c> are not among
    /// the nineteen), and the five view buttons have no command at all, so an
    /// icon bar would be three decorated buttons among ten bare ones.
    /// </para>
    /// <para>
    /// Where a caption IS the command's name — Home, Contents — the add-in
    /// composes the tool tip from it. Where it is not — "&lt;&lt;", "1:1", "+"
    /// — the application writes the whole tip and the add-in leaves it alone.
    /// </para>
    /// </remarks>
    internal UIElement BuildToolBar()
    {
        Grid rows = new Grid();
        rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _chooser = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(4, 2, 4, 2),
        };
        _chooser.SelectionChanged += (_, _) =>
        {
            if (_updatingChooser) { return; }

            int index = _chooser.SelectedIndex;
            if (index >= 0 && index < _listed.Count)
            {
                _ = OpenAsync(_listed[index], 1, remember: true);
            }
        };
        rows.Children.Add(_chooser);

        ToolBar bar = PanelToolbar.Create(Title, LabelMode.TextOnly);
        bar.Items.Add(PanelToolbar.ButtonFor(Actions?.HelpBack, "<<"));
        bar.Items.Add(PanelToolbar.ButtonFor(Actions?.HelpForward, ">>"));

        //Upstream's own separator, between the two history buttons and the rest
        //(docbrowser/browser.py).
        bar.Items.Add(new ToolBarSeparator());
        bar.Items.Add(PanelToolbar.ButtonFor(Actions?.HelpHome));
        bar.Items.Add(BuildContentsToggle());

        bar.Items.Add(new ToolBarSeparator());
        bar.Items.Add(PanelToolbar.Button(
            "-", MenuBuilder.Display(I18n.Get("Zoom &Out")), () => _view?.ZoomOut()));
        bar.Items.Add(PanelToolbar.Button(
            "1:1", MenuBuilder.Display(I18n.Get("Original &Size")), () => _view?.ZoomOriginal()));
        bar.Items.Add(PanelToolbar.Button(
            "+", MenuBuilder.Display(I18n.Get("Zoom &In")), () => _view?.ZoomIn()));
        bar.Items.Add(PanelToolbar.Button(
            I18n.Get("Width"),
            MenuBuilder.Display(I18n.Get("Fit &Width")),
            () => SetViewMode(ViewMode.FitWidth)));
        bar.Items.Add(PanelToolbar.Button(
            I18n.Get("Page"),
            MenuBuilder.Display(I18n.Get("Fit &Page")),
            () => SetViewMode(ViewMode.FitBoth)));

        bar.Items.Add(new ToolBarSeparator());
        bar.Items.Add(PanelToolbar.ButtonFor(Actions?.HelpPreviousPage, "<"));

        _pageLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 70 };
        bar.Items.Add(_pageLabel);

        bar.Items.Add(PanelToolbar.ButtonFor(Actions?.HelpNextPage, ">"));
        bar.Items.Add(new ToolBarSeparator());
        bar.Items.Add(PanelToolbar.ButtonFor(Actions?.HelpExternalViewer, I18n.Get("Open")));

        Grid.SetRow(bar, 1);
        rows.Children.Add(bar);
        _toolbar = bar;
        return rows;
    }

    /// <summary>Builds the button that shows and hides the contents list.</summary>
    /// <returns>The toggle.</returns>
    /// <remarks>
    /// There is no <see cref="AppAction"/> behind this one — the contents list
    /// is this panel's own, and upstream has no such list at all — so there is
    /// no second flip to keep in step with: the click flips the toggle, and the
    /// toggle is what the list follows.
    /// </remarks>
    private ToolToggleButton BuildContentsToggle()
    {
        ToolToggleButton contents = new ToolToggleButton
        {
            Text = I18n.Get("Contents"),
            IsChecked = _settings?.GetBool(SettingsPrefix + "contents", true) ?? true,
        };
        contents.IsCheckedChanged += (sender, _) => SetContentsVisible(sender.IsChecked);
        SetContentsVisible(contents.IsChecked);
        return contents;
    }

    private void WireActions()
    {
        if (Actions == null) { return; }

        Actions.HelpDocumentation.Handler = () => { Widget(); Activate(); };
        Actions.HelpContext.AsyncHandler = ShowContextHelpAsync;
        Actions.HelpBack.Handler = GoBack;
        Actions.HelpForward.Handler = GoForward;
        Actions.HelpHome.Handler = () => GoToPage(1);
        Actions.HelpPreviousPage.Handler = () => { Widget(); _view?.PreviousPage(); };
        Actions.HelpNextPage.Handler = () => { Widget(); _view?.NextPage(); };
        Actions.HelpExternalViewer.Handler = OpenInExternalViewer;
        UpdateHistoryActions();
    }

    private async Task OpenStartingManualAsync()
    {
        ManualDefinition start = ManualCatalog.Find(
            _settings?.GetString(SettingsPrefix + "manual", ManualCatalog.DefaultName)
            ?? ManualCatalog.DefaultName) ?? ManualCatalog.Find(ManualCatalog.DefaultName);

        if (start == null || !_library.IsInstalled(start))
        {
            start = _library.Installed.FirstOrDefault();
        }

        if (start == null)
        {
            ShowMessage(I18n.Get(
                "No manuals are installed.\n\nThe manuals are PDF files in the "
                + "application's assets/docs folder; see its README for how they "
                + "are made."));
            return;
        }

        await OpenAsync(start, _settings?.GetInt(SettingsPrefix + "page", 1) ?? 1,
            remember: false).ConfigureAwait(true);
    }

    private async Task OpenAsync(ManualDefinition definition, int page, bool remember)
    {
        if (definition == null) { return; }

        if (remember && _manual != null)
        {
            PushHistory(_back, new Place(_manual.Definition.Name, CurrentPage));
            _forward.Clear();
            UpdateHistoryActions();
        }

        if (_manual?.Definition == definition)
        {
            GoToPage(page);
            return;
        }

        PdfManual opened = await _library.OpenAsync(definition).ConfigureAwait(true);
        if (opened == null)
        {
            ShowMessage(I18n.Format(
                I18n.Get("{name} is not installed."), ("name", definition.Title)));
            return;
        }

        _manual = opened;

        //A view document per manual, kept, so returning to a manual keeps
        //everything the pages have already rendered.
        _document = opened.ToDocument();

        //Every page announces its own picture; one handler repaints the view.
        foreach (IPageImageSource source in opened.Pages)
        {
            source.ImageReady -= OnImageReady;
            source.ImageReady += OnImageReady;
        }

        ShowMessage(null);
        _view.SetDocument(_document);
        FillContents();
        SelectChooser(definition);
        GoToPage(page);
        _settings?.SetString(SettingsPrefix + "manual", definition.Name);
    }

    private void OnImageReady(object sender, EventArgs e) => _view?.Invalidate();

    private void FillChooser()
    {
        _listed = _library.Installed;
        _updatingChooser = true;
        _chooser.ItemsSource = _listed.Select(m => m.Title).ToList();
        _chooser.IsEnabled = _listed.Count > 0;
        _updatingChooser = false;
    }

    private void SelectChooser(ManualDefinition definition)
    {
        int index = _listed.ToList().FindIndex(m => m == definition);
        if (index < 0) { return; }

        _updatingChooser = true;
        _chooser.SelectedIndex = index;
        _updatingChooser = false;
    }

    private void FillContents()
    {
        _updatingContents = true;
        _contents.ItemsSource = (_manual?.Outline ?? Array.Empty<ManualOutlineEntry>())
            .Where(e => e.Page >= 1)
            .Select(e => new ContentsRow(e))
            .ToList();
        _contents.SelectedIndex = -1;
        _updatingContents = false;
    }

    private void OnContentsSelected()
    {
        if (_updatingContents || _navigating) { return; }

        if (_contents.SelectedItem is ContentsRow row)
        {
            PushHistory(_back, new Place(_manual?.Definition.Name, CurrentPage));
            _forward.Clear();
            UpdateHistoryActions();
            GoToPage(row.Entry.Page);
        }
    }

    private void SetContentsVisible(bool visible)
    {
        //The preference is written first: the toolbar is built before the panel
        //body in a test that asks for the bar on its own, and what the reader
        //last chose is worth remembering either way.
        _settings?.SetBool(SettingsPrefix + "contents", visible);
        if (_contents == null) { return; }

        _contents.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetViewMode(ViewMode mode)
    {
        if (_view != null) { _view.ViewMode = mode; }
    }

    private int CurrentPage => _view?.CurrentPageNumber ?? 1;

    private void GoToPage(int page)
    {
        if (_view == null || _manual == null) { return; }

        _navigating = true;
        _view.SetCurrentPageNumber(Math.Clamp(page, 1, Math.Max(1, _manual.PageCount)));
        _navigating = false;
        UpdatePageLabel();
        _settings?.SetInt(SettingsPrefix + "page", CurrentPage);
    }

    private void OnPageChanged()
    {
        UpdatePageLabel();
        _settings?.SetInt(SettingsPrefix + "page", CurrentPage);
    }

    private void UpdatePageLabel()
    {
        if (_pageLabel == null) { return; }

        _pageLabel.Text = _manual == null
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture, "{0} / {1}", CurrentPage, _manual.PageCount);
    }

    private void ShowMessage(string text)
    {
        if (_message == null) { return; }

        _message.Text = text ?? string.Empty;
        bool any = !string.IsNullOrEmpty(text);
        _message.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        if (_view != null) { _view.Visibility = any ? Visibility.Collapsed : Visibility.Visible; }
    }

    private void OpenInExternalViewer()
    {
        Widget();
        if (_manual == null)
        {
            ShowStatus?.Invoke(I18n.Get("No manuals are installed."));
            return;
        }

        OpenExternal?.Invoke(_manual.Path);
    }

    private static void PushHistory(List<Place> stack, Place place)
    {
        if (place.Manual == null) { return; }

        stack.Add(place);

        //Upstream's browser history is unbounded because a QWebEngineView owns
        //it; this one is a list of names and page numbers, so a limit costs
        //nothing and stops a long reading session growing forever.
        if (stack.Count > 100) { stack.RemoveAt(0); }
    }

    private void GoBack() => Step(_back, _forward);

    private void GoForward() => Step(_forward, _back);

    private void Step(List<Place> from, List<Place> to)
    {
        Widget();
        if (from.Count == 0) { return; }

        Place place = from[^1];
        from.RemoveAt(from.Count - 1);
        PushHistory(to, new Place(_manual?.Definition.Name, CurrentPage));
        UpdateHistoryActions();
        _ = OpenAsync(ManualCatalog.Find(place.Manual), place.Page, remember: false);
    }

    private void UpdateHistoryActions()
    {
        if (Actions == null) { return; }

        Actions.HelpBack.IsEnabled = _back.Count > 0;
        Actions.HelpForward.IsEnabled = _forward.Count > 0;
    }

    /// <summary>One place a reader has been: a manual and a page in it.</summary>
    private readonly struct Place
    {
        internal Place(string manual, int page)
        {
            Manual = manual;
            Page = page;
        }

        internal string Manual { get; }

        internal int Page { get; }
    }

    /// <summary>One row of the contents list.</summary>
    /// <remarks>Public because the row template binds to it.</remarks>
    [Microsoft.UI.Xaml.Data.Bindable]
    public sealed class ContentsRow
    {
        internal ContentsRow(ManualOutlineEntry entry)
        {
            Entry = entry;
            Text = entry.Title;
            Indent = new Thickness(Math.Min(entry.Level, 4) * 10, 0, 0, 0);
            Weight = entry.Level <= 1
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
        }

        internal ManualOutlineEntry Entry { get; }

        /// <summary>Gets the heading, section number and all.</summary>
        public string Text { get; }

        /// <summary>Gets the indent that shows how deep the heading sits.</summary>
        public Thickness Indent { get; }

        /// <summary>Gets the weight the heading is drawn in.</summary>
        public Windows.UI.Text.FontWeight Weight { get; }
    }
}
