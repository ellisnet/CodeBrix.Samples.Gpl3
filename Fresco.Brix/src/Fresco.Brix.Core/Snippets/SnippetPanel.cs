// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;
using Panel = Fresco.Brix.Shell.Panel;

namespace Fresco.Brix.Snippets; //was previously: frescobaldi/snippet/tool.py and widget.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One row of the snippet list.</summary>
public sealed class SnippetRow
{
    /// <summary>Creates a row.</summary>
    /// <param name="name">The snippet's stable name.</param>
    /// <param name="actionName">Its <c>name</c> variable.</param>
    /// <param name="title">Its title.</param>
    /// <param name="shortcut">Its shortcut, or the empty string.</param>
    public SnippetRow(string name, string actionName, string title, string shortcut)
    {
        Name = name;
        ActionName = actionName;
        Title = title;
        Shortcut = shortcut;
    }

    /// <summary>Gets the stable name.</summary>
    public string Name { get; }

    /// <summary>Gets the <c>name</c> variable, which macros and the search box
    /// match on.</summary>
    public string ActionName { get; }

    /// <summary>Gets the title.</summary>
    public string Title { get; }

    /// <summary>Gets the shortcut, or the empty string.</summary>
    public string Shortcut { get; }
}

/// <summary>
/// The Snippets panel: the library, a search box that filters it, a preview of
/// the selected snippet's text, and the commands that apply, add, edit, remove
/// and exchange snippets.
/// </summary>
public sealed class SnippetPanel : Panel
{
    private readonly SnippetLibrary _library;
    private readonly SnippetShortcuts _shortcuts;
    private readonly SettingsStore _settings;
    private readonly FontFamily _editorFont;

    private AutoSuggestBox _searchEntry;
    private ListView _list;
    private TextBlock _preview;
    private Button _menuButton;

    /// <summary>Creates the panel.</summary>
    /// <param name="library">The snippet library.</param>
    /// <param name="shortcuts">The snippet shortcuts.</param>
    /// <param name="settings">The settings store, or null.</param>
    /// <param name="editorFont">The monospace font the preview uses.</param>
    public SnippetPanel(
        SnippetLibrary library,
        SnippetShortcuts shortcuts,
        SettingsStore settings = null,
        FontFamily editorFont = null)
        : base("snippettool", DockArea.Bottom)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _shortcuts = shortcuts;
        _settings = settings;
        _editorFont = editorFont;
        ToggleAction.WithShortcut("Meta+Alt+S");
        Actions = new SnippetToolActions(settings);
        _library.Changed += (_, _) => Repopulate();
    }

    /// <summary>Gets the panel's own commands.</summary>
    public SnippetToolActions Actions { get; }

    /// <summary>Gets or sets what applying a snippet does.</summary>
    public Action<string> ApplySnippet { get; set; }

    /// <summary>Gets or sets how to put a dialog on screen.</summary>
    public XamlRoot DialogRoot { get; set; }

    /// <summary>Gets or sets how to ask for a file to import.</summary>
    public Func<Task<string>> PickImportPathAsync { get; set; }

    /// <summary>Gets or sets how to ask where to export.</summary>
    public Func<string, Task<string>> PickExportPathAsync { get; set; }

    /// <summary>Gets or sets what to do when the panel gives focus back.</summary>
    public Action FocusEditor { get; set; }

    /// <summary>Gets the snippet the list has selected, or null.</summary>
    public string CurrentSnippet
        => (_list?.SelectedItem as SnippetRow)?.Name;

    /// <inheritdoc/>
    public override string Title => I18n.Get("Snippets");

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        ToggleAction.Text = I18n.Get("&Snippets");
        Actions.TranslateUI();
    }

    /// <summary>Opens the panel and puts the caret in its search box.</summary>
    public new void Activate()
    {
        base.Activate();
        _ = Widget();
        _searchEntry?.Focus(FocusState.Programmatic);
    }

    /// <summary>Opens the panel showing only the templates.</summary>
    public void ManageTemplates()
    {
        Activate();
        if (_searchEntry != null) { _searchEntry.Text = ":template"; }
    }

    /// <summary>Rebuilds the list from the library, keeping the filter.</summary>
    public void Repopulate()
    {
        if (_list == null) { return; }

        string keep = CurrentSnippet;
        SnippetFilterResult filtered = SnippetFilter.Apply(
            _library, _library.NamesByTitle(), _searchEntry?.Text ?? string.Empty);

        _list.ItemsSource = filtered.Names.Select(Row).ToList();

        string select = filtered.ExactMatch ?? keep;
        SnippetRow row = (_list.ItemsSource as IEnumerable<SnippetRow>)?
            .FirstOrDefault(r => r.Name == select);
        _list.SelectedItem = row
            ?? (_list.ItemsSource as IEnumerable<SnippetRow>)?.FirstOrDefault();
        UpdatePreview();
    }

    /// <summary>Applies the snippet the list has selected.</summary>
    public void ApplyCurrent()
    {
        string name = CurrentSnippet;
        if (name != null) { ApplySnippet?.Invoke(name); }
    }

    /// <summary>Opens the editor for a new snippet.</summary>
    /// <param name="text">The text it starts with, or null.</param>
    /// <returns>The task.</returns>
    public async Task AddAsync(string text = null)
    {
        string name = await SnippetEditDialog.ShowAsync(
            DialogRoot, _library, null, text, _editorFont);
        if (name != null) { Repopulate(); }
    }

    /// <summary>Opens the editor for the selected snippet.</summary>
    /// <returns>The task.</returns>
    public async Task EditAsync()
    {
        string name = CurrentSnippet;
        if (name == null) { return; }

        string saved = await SnippetEditDialog.ShowAsync(
            DialogRoot, _library, name, null, _editorFont);
        if (saved != null) { Repopulate(); }
    }

    /// <summary>Removes the selected snippet.</summary>
    public void RemoveCurrent()
    {
        string name = CurrentSnippet;
        if (name == null) { return; }

        _shortcuts?.SetShortcuts(name, Array.Empty<KeySequence>());
        _library.Delete(name);
        Repopulate();
    }

    /// <summary>Brings back every built-in snippet the user changed or
    /// removed.</summary>
    public void RestoreBuiltins()
    {
        foreach (var snippet in BuiltinSnippets.All)
        {
            //Saving the built-in's own text and title makes the library forget
            //the override, which is exactly "restore".
            _library.Save(snippet.Name, snippet.Text, snippet.Title);
        }

        Repopulate();
    }

    /// <summary>Reads snippets from a file the user picks.</summary>
    /// <returns>The task.</returns>
    public async Task ImportAsync()
    {
        Func<Task<string>> pick = PickImportPathAsync;
        if (pick == null) { return; }

        string path = await pick();
        if (string.IsNullOrEmpty(path)) { return; }

        try
        {
            SnippetImportExport.Apply(
                _library, SnippetImportExport.Load(path), _shortcuts);
        }
        catch (Exception error) when (
            error is System.IO.IOException or System.IO.InvalidDataException)
        {
            await InputDialogs.ConfirmAsync(
                DialogRoot,
                I18n.Get("Error"),
                I18n.Format(
                    I18n.Get("Can't read from source:\n\n{url}\n\n{error}"),
                    ("url", path), ("error", error.Message)));
            return;
        }

        Repopulate();
    }

    /// <summary>Writes the listed snippets to a file the user picks.</summary>
    /// <returns>The task.</returns>
    public async Task ExportAsync()
    {
        Func<string, Task<string>> pick = PickExportPathAsync;
        if (pick == null) { return; }

        //The selection if there is one, otherwise everything the filter left.
        List<string> names = CurrentSnippet != null
            ? new List<string> { CurrentSnippet }
            : (_list?.ItemsSource as IEnumerable<SnippetRow>)?
                .Select(r => r.Name).ToList()
                ?? new List<string>();
        if (names.Count == 0) { return; }

        string path = await pick("snippets.xml");
        if (string.IsNullOrEmpty(path)) { return; }

        try
        {
            SnippetImportExport.Save(_library, names, path, _shortcuts);
        }
        catch (System.IO.IOException error)
        {
            await InputDialogs.ConfirmAsync(
                DialogRoot,
                I18n.Get("Error"),
                I18n.Format(
                    I18n.Get("Can't write to destination:\n\n{url}\n\n{error}"),
                    ("url", path), ("error", error.Message)));
        }
    }

    /// <inheritdoc/>
    protected override UIElement CreateWidget()
    {
        FillGrid root = new FillGrid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(2, GridUnitType.Star),
        });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });

        root.Children.Add(BuildToolbar());
        _list = BuildList();
        Grid.SetRow(_list, 1);
        root.Children.Add(_list);

        ScrollViewer previewHost = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(6),
        };
        _preview = new TextBlock { IsTextSelectionEnabled = true };
        if (_editorFont != null) { _preview.FontFamily = _editorFont; }

        previewHost.Content = _preview;
        Grid.SetRow(previewHost, 2);
        root.Children.Add(previewHost);

        WireActions();
        Repopulate();
        return root;
    }

    private Grid BuildToolbar()
    {
        Grid bar = new Grid { ColumnSpacing = 4, Padding = new Thickness(4) };
        bar.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        for (int i = 0; i < 4; i++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        _searchEntry = new AutoSuggestBox
        {
            PlaceholderText = I18n.Get("Search..."),
            QueryIcon = null,
        };
        _searchEntry.TextChanged += (sender, args) =>
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput
                && _searchEntry.Text.StartsWith(':'))
            {
                sender.ItemsSource = SnippetFilter.VariableNames
                    .Where(v => v.StartsWith(_searchEntry.Text, StringComparison.Ordinal))
                    .ToList();
            }
            else
            {
                sender.ItemsSource = null;
            }

            Repopulate();
        };
        _searchEntry.QuerySubmitted += (_, _) =>
        {
            ApplyCurrent();
            IsVisible = false;
            FocusEditor?.Invoke();
        };
        _searchEntry.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Escape) { return; }

            IsVisible = false;
            FocusEditor?.Invoke();
            e.Handled = true;
        };
        ToolTipService.SetToolTip(_searchEntry, I18n.Get(
            "Enter text to search in the snippets list.\n"
            + "See \"What's This\" for more information."));

        Grid.SetColumn(_searchEntry, 0);
        bar.Children.Add(_searchEntry);

        Button apply = ActionButton(Actions.Apply, 1, bar);
        Button add = ActionButton(Actions.AddSnippet, 2, bar);
        Button edit = ActionButton(Actions.Edit, 3, bar);
        _ = apply;
        _ = add;
        _ = edit;

        _menuButton = new Button { Content = MenuBuilder.Display(I18n.Get("&Menu")) };
        MenuFlyout flyout = new MenuFlyout();
        foreach (var action in new[]
        {
            Actions.Import, Actions.Export, null,
            Actions.Apply, null,
            Actions.AddSnippet, Actions.Edit, Actions.Shortcut, Actions.Remove, null,
            Actions.Restore,
        })
        {
            flyout.Items.Add(action == null
                ? new MenuFlyoutSeparator()
                : MenuBuilder.ItemFor(action));
        }

        _menuButton.Flyout = flyout;
        Grid.SetColumn(_menuButton, 4);
        bar.Children.Add(_menuButton);
        return bar;
    }

    private ListView BuildList()
    {
        ListView list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = RowTemplate(),
        };
        list.SelectionChanged += (_, _) => UpdatePreview();
        list.DoubleTapped += (_, _) => ApplyCurrent();
        return list;
    }

    private void WireActions()
    {
        Actions.Apply.Handler = ApplyCurrent;
        Actions.AddSnippet.AsyncHandler = () => AddAsync();
        Actions.Edit.AsyncHandler = EditAsync;
        Actions.Remove.Handler = RemoveCurrent;
        Actions.Restore.Handler = RestoreBuiltins;
        Actions.Import.AsyncHandler = ImportAsync;
        Actions.Export.AsyncHandler = ExportAsync;
        Actions.Shortcut.AsyncHandler = ConfigureShortcutAsync;
    }

    private async Task ConfigureShortcutAsync()
    {
        string name = CurrentSnippet;
        if (name == null || _shortcuts == null) { return; }

        string current = string.Join(
            ", ", _shortcuts.Shortcuts(name).Select(s => s.ToString()));
        string entered = await InputDialogs.GetTextAsync(
            DialogRoot,
            I18n.Get("Configure Keyboard Shortcut"),
            I18n.Get("Please enter the shortcut, for example Ctrl+Shift+S:"),
            current);
        if (entered == null) { return; }

        _shortcuts.SetShortcuts(
            name,
            entered.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => KeySequence.Parse(s.Trim()))
                .Where(k => k != null)
                .ToList());
        Repopulate();
    }

    private SnippetRow Row(string name)
        => new SnippetRow(
            name,
            _library.ActionName(name),
            _library.Title(name),
            _shortcuts == null
                ? string.Empty
                : string.Join(", ", _shortcuts.Shortcuts(name).Select(s => s.ToString())));

    private void UpdatePreview()
    {
        if (_preview == null) { return; }

        string name = CurrentSnippet;
        _preview.Text = name == null ? string.Empty : _library.Get(name).Text;
    }

    private static Button ActionButton(AppAction action, int column, Grid bar)
    {
        Button button = new Button
        {
            Content = MenuBuilder.Display(action.Text),
            Padding = new Thickness(8, 2, 8, 2),
        };
        button.Click += (_, _) => action.Trigger();
        action.PropertyChanged += (_, _)
            => button.Content = MenuBuilder.Display(action.Text);
        Grid.SetColumn(button, column);
        bar.Children.Add(button);
        return button;
    }

    private static DataTemplate RowTemplate()
    {
        string xaml =
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
            + "<Grid ColumnSpacing=\"8\">"
            + "<Grid.ColumnDefinitions><ColumnDefinition Width=\"120\" />"
            + "<ColumnDefinition Width=\"*\" /><ColumnDefinition Width=\"Auto\" />"
            + "</Grid.ColumnDefinitions>"
            + "<TextBlock Grid.Column=\"0\" Text=\"{Binding ActionName}\" Opacity=\"0.7\" />"
            + "<TextBlock Grid.Column=\"1\" Text=\"{Binding Title}\" TextTrimming=\"CharacterEllipsis\" />"
            + "<TextBlock Grid.Column=\"2\" Text=\"{Binding Shortcut}\" Opacity=\"0.7\" />"
            + "</Grid></DataTemplate>";
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }
}

/// <summary>The Snippets panel's own commands.</summary>
public sealed class SnippetToolActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "snippettool";

    /// <summary>Creates the collection.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public SnippetToolActions(SettingsStore settings = null)
        : base(CollectionName, settings) => Initialize();

    /// <summary>Gets the "open the panel" command.</summary>
    public AppAction Activate { get; private set; }

    /// <summary>Gets the "save this document as a template" command.</summary>
    public AppAction SaveAsTemplate { get; private set; }

    /// <summary>Gets the "make a snippet of the selection" command.</summary>
    public AppAction CopyToSnippet { get; private set; }

    /// <summary>Gets the "show the templates" command.</summary>
    public AppAction ManageTemplates { get; private set; }

    /// <summary>Gets the "apply the selected snippet" command.</summary>
    public AppAction Apply { get; private set; }

    /// <summary>Gets the "write a new snippet" command.</summary>
    public AppAction AddSnippet { get; private set; }

    /// <summary>Gets the "change this snippet" command.</summary>
    public AppAction Edit { get; private set; }

    /// <summary>Gets the "give this snippet a key" command.</summary>
    public AppAction Shortcut { get; private set; }

    /// <summary>Gets the "delete this snippet" command.</summary>
    public AppAction Remove { get; private set; }

    /// <summary>Gets the "read snippets from a file" command.</summary>
    public AppAction Import { get; private set; }

    /// <summary>Gets the "write snippets to a file" command.</summary>
    public AppAction Export { get; private set; }

    /// <summary>Gets the "bring the built-in snippets back" command.</summary>
    public AppAction Restore { get; private set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Snippets");

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        Activate = Add_("snippettool_activate").WithShortcut("Ctrl+T");
        SaveAsTemplate = Add_("file_save_as_template");
        CopyToSnippet = Add_("copy_to_snippet");
        ManageTemplates = Add_("templates_manage");
        Apply = Add_("snippet_apply").WithIcon("edit-paste");
        AddSnippet = Add_("snippet_add").WithIcon("list-add").WithShortcut("Insert");
        Edit = Add_("snippet_edit").WithIcon("document-edit").WithShortcut("F2");
        Shortcut = Add_("snippet_shortcut");
        Remove = Add_("snippet_remove").WithIcon("list-remove")
            .WithShortcut("Ctrl+Delete");
        Import = Add_("snippet_import").WithIcon("document-open");
        Export = Add_("snippet_export").WithIcon("document-save-as");
        Restore = Add_("snippet_restore");
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        Activate.Text = I18n.Get("Manage &Snippets...");
        SaveAsTemplate.Text = I18n.Get("Save as Template...");
        CopyToSnippet.Text = I18n.Get("Copy to &Snippet...");
        ManageTemplates.Text = I18n.Get("Manage Templates...");
        Apply.Text = I18n.Get("A&pply");
        Apply.ToolTip = I18n.Get("Apply the current snippet.");
        AddSnippet.Text = I18n.Get("&Add...");
        Edit.Text = I18n.Get("&Edit...");
        Shortcut.Text = I18n.Get("Configure Keyboard &Shortcut...");
        Remove.Text = I18n.Get("&Remove");
        Remove.ToolTip = I18n.Get("Remove the selected snippets.");
        Import.Text = I18n.Get("&Import...");
        Import.ToolTip = I18n.Get("Import snippets from a file.");
        Export.Text = I18n.Get("E&xport...");
        Export.ToolTip = I18n.Get("Export snippets to a file.");
        Restore.Text = I18n.Get("Restore &Built-in Snippets...");
        Restore.ToolTip = I18n.Get("Restore deleted or changed built-in snippets.");
    }

    private AppAction Add_(string name) => Add(name);
}
