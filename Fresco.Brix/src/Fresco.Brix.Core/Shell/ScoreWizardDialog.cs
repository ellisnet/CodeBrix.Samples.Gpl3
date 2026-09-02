// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Editor;
using Fresco.Brix.Engrave;
using Fresco.Brix.ScoreWizard;
using Fresco.Brix.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dom = Fresco.Brix.Ly.Dom;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/scorewiz/dialog.py + header.py + score.py + settings.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Score Wizard: three pages that set the titles, assemble the parts and
/// choose the score's settings, and then write a complete LilyPond document.
/// </summary>
/// <remarks>
/// The wizard's STATE outlives the window, exactly as upstream's does: the
/// dialog is built fresh each time it is opened, over a
/// <see cref="ScoreWizardModel"/> that is not, so a user who cancels and comes
/// back finds their parts where they left them.
/// </remarks>
public sealed class ScoreWizardDialog
{
    private readonly SettingsStore _settings;
    private readonly Dictionary<string, TextBox> _headerBoxes =
        new Dictionary<string, TextBox>(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _headerPreview =
        new Dictionary<string, TextBlock>(StringComparer.Ordinal);
    private readonly Dictionary<TreeViewNode, PartTreeItem> _scoreNodes =
        new Dictionary<TreeViewNode, PartTreeItem>();

    private TreeView _availableParts;
    private TreeView _scoreTree;
    private ContentControl _partSettings;
    /// <summary>The root the dialog was last shown on, for sizing.</summary>
    private XamlRoot _root;

    private Grid _pages;
    private IReadOnlyList<Button> _tabs;
    private int _currentPage;

    /// <summary>Creates the wizard over a fresh score.</summary>
    /// <param name="settings">The settings store the preferences live in.</param>
    public ScoreWizardDialog(SettingsStore settings = null)
    {
        _settings = settings;
        Model = new ScoreWizardModel();
        Model.Load(settings);
    }

    /// <summary>Gets everything the wizard knows; it outlives the window.</summary>
    public ScoreWizardModel Model { get; }

    /// <summary>Gets or sets what the Preview button does with the text.</summary>
    public Func<string, Task> PreviewAction { get; set; }

    /// <summary>Puts the wizard in front of the user.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <returns>The finished document's text, or null when cancelled.</returns>
    public async Task<string> ShowAsync(XamlRoot xamlRoot)
    {
        _root = xamlRoot;
        ContentDialog dialog = new ContentDialog
        {
            Title = I18n.Get("Score Setup Wizard"),
            Content = BuildContent(),
            PrimaryButtonText = StandardButtons.Ok,
            CloseButtonText = StandardButtons.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,

        };

        //⚠ A ContentDialog's width is not its MaxWidth: the theme's template
        //binds it to the ContentDialogMaxWidth resource, which is about 550 —
        //half of what three pages of parts and settings need. Overriding the
        //RESOURCE on the dialog itself is what widens it.
        //was previously: written out. See Shell/DialogSizing — safe at today's
        //880x470 design, and clipped the moment either number grew.
        DialogSizing.Clamp(dialog, 1400, 1000);

        bool accepted = await dialog.ShowAsync() == ContentDialogResult.Primary;
        Model.Save(_settings);
        return accepted ? DocumentText() : null;
    }

    /// <summary>Builds the document the wizard describes.</summary>
    /// <returns>The LilyPond text, indented the way the editor would.</returns>
    /// <remarks>Upstream indents the finished text with the user's own indent
    /// preferences before putting it in a document, so that what lands in the
    /// editor is already in house style rather than the printer's two spaces.</remarks>
    public string DocumentText()
    {
        ScoreBuilder builder = new ScoreBuilder(Model, _settings);
        Dom.Document document = builder.Document();
        if (!Model.GeneralPreferences.RelativePitch.Value)
        {
            //Take the pitch out of every \relative: the user wants the bare
            //command.
            foreach (Dom.Relative relative in document.Find<Dom.Relative>().ToList())
            {
                foreach (Dom.Pitch pitch in relative.Find<Dom.Pitch>(1).ToList())
                {
                    relative.Remove(pitch);
                }
            }
        }

        return Indent(builder.Text(document));
    }

    /// <summary>Builds the document with example music in it.</summary>
    /// <returns>The LilyPond text.</returns>
    public string PreviewText()
    {
        ScoreBuilder builder = new ScoreBuilder(Model, _settings);
        Dom.Document document = builder.Document();
        ScorePreview.Examplify(document);
        return builder.Text(document);
    }

    /// <summary>Re-indents wizard output the way the editor indents.</summary>
    /// <param name="text">The printer's output.</param>
    /// <returns>The indented text.</returns>
    private string Indent(string text)
    {
        Ly.Document document = new Ly.Document(text);
        Indenting.CreateIndenter(_settings).Indent(new Ly.Cursor(document));
        return document.PlainText();
    }

    // ------------------------------------------------------------ the window

    /// <summary>Builds the dialog's contents.</summary>
    /// <returns>The content.</returns>
    private UIElement BuildContent()
    {
        Grid root = new Grid
        {
            RowSpacing = 6,
            MinWidth = 700,
            MaxWidth = 880,
            //What is left in the window, capped at the design height — see
            //Shell/DialogSizing.
            Height = DialogSizing.ContentHeight(_root, 470),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        //A row of buttons over one page at a time: the theme's tab controls
        //paint nothing on the Skia heads (board trap 40).
        (string Title, Func<UIElement> Build)[] pages =
        {
            (I18n.Get("&Titles and Headers"), BuildTitlesPage),
            (I18n.Get("&Parts"), BuildPartsPage),
            (I18n.Get("&Score settings"), BuildSettingsPage),
        };

        StackPanel tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
        };
        _pages = new Grid();
        List<Button> tabButtons = new List<Button>();

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
            tabButtons.Add(tab);
            tabs.Children.Add(tab);
        }

        _tabs = tabButtons;
        Grid.SetRow(tabs, 0);
        Grid.SetRow(_pages, 1);
        root.Children.Add(tabs);
        root.Children.Add(_pages);

        StackPanel actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        Button clear = new Button { Content = I18n.Get("Clear") };
        ToolTipService.SetToolTip(
            clear, I18n.Get("Clears the current page of the Score Wizard."));
        clear.Click += (_, _) => ClearCurrentPage();
        actions.Children.Add(clear);

        Button preview = new Button { Content = I18n.Get("Preview") };
        preview.Click += async (_, _) =>
        {
            Func<string, Task> action = PreviewAction;
            if (action != null) { await action(PreviewText()); }
        };
        actions.Children.Add(preview);

        //Upstream's `userguide.addButton(b, "scorewiz")'. A ContentDialog's
        //three buttons are spent (board trap 50), so Help joins Clear and
        //Preview on the dialog's own action row.
        actions.Children.Add(UserGuide.GuideHelp.Button("scorewiz"));

        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        ShowPage(_currentPage);
        return root;
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

    /// <summary>Empties whatever the page in front of the user holds.</summary>
    private void ClearCurrentPage()
    {
        switch (_currentPage)
        {
            case 0:
                Model.ClearHeaders();
                foreach (TextBox box in _headerBoxes.Values) { box.Text = string.Empty; }

                UpdateHeaderPreview();
                return;
            case 1:
                Model.Root.Clear();
                RefreshScoreTree();
                return;
            default:
                //Upstream clears only the four settings a user is likely to
                //have set for this score, not the preferences.
                Model.ScoreProperties.Tempo.Value = string.Empty;
                Model.ScoreProperties.KeyNote.SelectedIndex = 0;
                Model.ScoreProperties.KeyMode.SelectedIndex = 0;
                Model.ScoreProperties.Pickup.SelectedIndex = 0;
                return;
        }
    }

    // ------------------------------------------------------- titles and headers

    /// <summary>Builds the page that sets the titles.</summary>
    /// <returns>The page.</returns>
    private UIElement BuildTitlesPage()
    {
        Grid page = new Grid { ColumnSpacing = 12 };
        page.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Border preview = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current
                .Resources["TextControlBorderBrush"],
            Padding = new Thickness(10),
            MinWidth = 300,
        };
        preview.Child = BuildHeaderPreview();
        Grid.SetColumn(preview, 0);
        page.Children.Add(preview);

        Grid entries = new Grid { ColumnSpacing = 8, RowSpacing = 2 };
        entries.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        entries.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });

        int row = 0;
        foreach ((string name, Func<string> title) in ScoreWizardModel.HeaderFields)
        {
            entries.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock label = new TextBlock
            {
                Text = title() + ":",
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
            entries.Children.Add(label);

            TextBox box = new TextBox { Text = Model.Header(name) };
            string field = name;
            box.TextChanged += (_, _) =>
            {
                Model.SetHeader(field, box.Text);
                UpdateHeaderPreview();
            };
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            entries.Children.Add(box);
            _headerBoxes[name] = box;
            row++;
        }

        ScrollViewer scroller = new ScrollViewer
        {
            Content = entries,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetColumn(scroller, 1);
        page.Children.Add(scroller);

        UpdateHeaderPreview();
        return page;
    }

    /// <summary>Builds the little picture of where each title lands.</summary>
    /// <returns>The picture.</returns>
    /// <remarks>//was previously: an HTML page in a QTextBrowser. There is no
    /// web view anywhere in this application (FR8), and this shows the same
    /// thing: every title in its place on the page, greyed while it is empty,
    /// and clicking one puts the caret in its entry.</remarks>
    private UIElement BuildHeaderPreview()
    {
        StackPanel page = new StackPanel { Spacing = 2 };

        page.Children.Add(PreviewField("dedication", HorizontalAlignment.Center));
        page.Children.Add(PreviewField(
            "title", HorizontalAlignment.Center, 20, FontWeights.Bold));
        page.Children.Add(PreviewField(
            "subtitle", HorizontalAlignment.Center, 15, FontWeights.Bold));
        page.Children.Add(PreviewField(
            "subsubtitle", HorizontalAlignment.Center, weight: FontWeights.Bold));

        page.Children.Add(PreviewRow("poet", "instrument", "composer", bold: true));
        page.Children.Add(PreviewRow("meter", null, "arranger"));
        page.Children.Add(PreviewRow("piece", null, "opus"));

        page.Children.Add(new TextBlock
        {
            Text = "𝄞  ♪ ♫ ♪ ♫",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 18),
            Opacity = 0.5,
            FontSize = 22,
        });

        page.Children.Add(PreviewField(
            "copyright", HorizontalAlignment.Center, note: I18n.Get("bottom of first page")));
        page.Children.Add(PreviewField(
            "tagline", HorizontalAlignment.Center, note: I18n.Get("bottom of last page")));
        return page;
    }

    /// <summary>Builds one three-column row of the picture.</summary>
    /// <param name="left">The field on the left, or null.</param>
    /// <param name="middle">The field in the middle, or null.</param>
    /// <param name="right">The field on the right, or null.</param>
    /// <param name="bold">Whether the middle field is bold.</param>
    /// <returns>The row.</returns>
    private UIElement PreviewRow(string left, string middle, string right, bool bold = false)
    {
        Grid row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(25, GridUnitType.Star),
        });
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(50, GridUnitType.Star),
        });
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(25, GridUnitType.Star),
        });

        void Cell(string name, int column, HorizontalAlignment alignment)
        {
            if (name == null) { return; }

            UIElement field = PreviewField(
                name, alignment, weight: bold ? FontWeights.Bold : FontWeights.Normal);
            Grid.SetColumn(field, column);
            row.Children.Add(field);
        }

        Cell(left, 0, HorizontalAlignment.Left);
        Cell(middle, 1, HorizontalAlignment.Center);
        Cell(right, 2, HorizontalAlignment.Right);
        return row;
    }

    /// <summary>Builds one clickable field of the picture.</summary>
    /// <param name="name">The header field.</param>
    /// <param name="alignment">Where it sits.</param>
    /// <param name="size">Its font size, or 0 for the usual one.</param>
    /// <param name="weight">Its weight.</param>
    /// <param name="note">A note printed after it, or null.</param>
    /// <returns>The field.</returns>
    private UIElement PreviewField(
        string name,
        HorizontalAlignment alignment,
        double size = 0,
        Windows.UI.Text.FontWeight weight = default,
        string note = null)
    {
        TextBlock text = new TextBlock
        {
            HorizontalAlignment = alignment,
            HorizontalTextAlignment = alignment == HorizontalAlignment.Center
                ? TextAlignment.Center
                : alignment == HorizontalAlignment.Right
                    ? TextAlignment.Right
                    : TextAlignment.Left,
            TextWrapping = TextWrapping.NoWrap,
        };
        if (size > 0) { text.FontSize = size; }

        if (weight.Weight != 0) { text.FontWeight = weight; }

        ToolTipService.SetToolTip(text, I18n.Get("Click to enter a value."));
        text.PointerPressed += (_, _) =>
        {
            if (_headerBoxes.TryGetValue(name, out TextBox box)) { box.Focus(FocusState.Programmatic); }
        };
        _headerPreview[name] = text;

        if (note == null) { return text; }

        StackPanel row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = alignment,
        };
        row.Children.Add(text);
        row.Children.Add(new TextBlock
        {
            Text = "(" + note + ")",
            FontStyle = Windows.UI.Text.FontStyle.Italic,
            Opacity = 0.6,
        });
        return row;
    }

    /// <summary>Writes the entered titles into the picture.</summary>
    private void UpdateHeaderPreview()
    {
        bool anything = ScoreWizardModel.HeaderFields.Any(
            field => Model.Header(field.Name).Trim().Length > 0);

        foreach ((string name, Func<string> title) in ScoreWizardModel.HeaderFields)
        {
            if (!_headerPreview.TryGetValue(name, out TextBlock text)) { continue; }

            string entered = Model.Header(name).Trim();
            text.Text = entered.Length > 0 ? entered : title();

            //Once anything has been entered, what has not been dims further, so
            //that the real titles stand out — upstream's own touch.
            text.Opacity = entered.Length > 0 ? 1.0 : anything ? 0.35 : 0.6;
        }
    }

    // ---------------------------------------------------------------- the parts

    /// <summary>Builds the page that assembles the score.</summary>
    /// <returns>The page.</returns>
    private UIElement BuildPartsPage()
    {
        Grid page = new Grid { ColumnSpacing = 10 };
        page.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        page.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        page.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        //Available parts.
        Grid available = Column(I18n.Get("Available parts:"));
        _availableParts = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Single,
            CanDragItems = false,
            CanReorderItems = false,
        };
        foreach (PartCategory category in PartRegistry.Categories)
        {
            TreeViewNode node = new TreeViewNode { Content = category.Title() };
            foreach (PartEntry entry in category.Items)
            {
                node.Children.Add(new TreeViewNode { Content = entry.Title() });
            }

            _availableParts.RootNodes.Add(node);
        }

        //Upstream adds a part on a DOUBLE click, and a single click only picks
        //one out — which is what lets a user look through the list.
        _availableParts.DoubleTapped += (_, _) => AddPart(_availableParts.SelectedNode);

        Grid.SetRow(_availableParts, 1);
        available.Children.Add(_availableParts);

        Button add = new Button
        {
            Content = MenuBuilder.Display(I18n.Get("&Add")),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        add.Click += (_, _) => AddPart(_availableParts.SelectedNode);
        Grid.SetRow(add, 2);
        available.Children.Add(add);
        Grid.SetColumn(available, 0);
        page.Children.Add(available);

        //The score itself.
        Grid score = Column(I18n.Get("Score:"));
        _scoreTree = new TreeView { SelectionMode = TreeViewSelectionMode.Single };
        _scoreTree.SelectionChanged += (_, _) => ShowPartSettings();
        Grid.SetRow(_scoreTree, 1);
        score.Children.Add(_scoreTree);

        StackPanel buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
        };
        Button remove = new Button
        {
            Content = MenuBuilder.Display(I18n.Get("&Remove")),
        };
        remove.Click += (_, _) => RemoveSelectedPart();
        Button up = new Button { Content = "▲" };
        ToolTipService.SetToolTip(up, I18n.Get("Move up"));
        up.Click += (_, _) => MoveSelectedPart(-1);
        Button down = new Button { Content = "▼" };
        ToolTipService.SetToolTip(down, I18n.Get("Move down"));
        down.Click += (_, _) => MoveSelectedPart(1);
        buttons.Children.Add(remove);
        buttons.Children.Add(up);
        buttons.Children.Add(down);
        Grid.SetRow(buttons, 2);
        score.Children.Add(buttons);
        Grid.SetColumn(score, 1);
        page.Children.Add(score);

        //The chosen part's settings.
        _partSettings = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        ScrollViewer settings = new ScrollViewer
        {
            Content = _partSettings,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetColumn(settings, 2);
        page.Children.Add(settings);

        RefreshScoreTree();
        return page;
    }

    /// <summary>Builds a titled column of the parts page.</summary>
    /// <param name="title">The title.</param>
    /// <returns>The column, with its title already in row 0.</returns>
    private static Grid Column(string title)
    {
        Grid column = new Grid { RowSpacing = 4 };
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.Bold,
        });
        return column;
    }

    /// <summary>Adds the part an available-parts row names to the score.</summary>
    /// <param name="node">The row.</param>
    private void AddPart(TreeViewNode node)
    {
        if (node == null || node.Parent == null) { return; }

        int category = _availableParts.RootNodes.IndexOf(node.Parent);
        if (category < 0) { return; }

        int index = node.Parent.Children.IndexOf(node);
        if (index < 0) { return; }

        PartBase part = PartRegistry.Categories[category].Items[index].Create();

        //Into the chosen row when it will have it, otherwise at the top level.
        PartTreeItem parent = Model.Root;
        if (_scoreTree.SelectedNode != null
            && _scoreNodes.TryGetValue(_scoreTree.SelectedNode, out PartTreeItem current)
            && current.Part != null
            && current.Part.Accepts(part))
        {
            parent = current;
        }

        PartTreeItem added = parent.Add(part);
        Model.ApplyPitchLanguage();
        RefreshScoreTree(added);
    }

    /// <summary>Takes the chosen part out of the score.</summary>
    private void RemoveSelectedPart()
    {
        if (_scoreTree.SelectedNode == null
            || !_scoreNodes.TryGetValue(_scoreTree.SelectedNode, out PartTreeItem item))
        {
            return;
        }

        item.Parent?.Remove(item);
        RefreshScoreTree();
    }

    /// <summary>Moves the chosen part among its siblings.</summary>
    /// <param name="offset">-1 for up, 1 for down.</param>
    private void MoveSelectedPart(int offset)
    {
        if (_scoreTree.SelectedNode == null
            || !_scoreNodes.TryGetValue(_scoreTree.SelectedNode, out PartTreeItem item))
        {
            return;
        }

        if (item.Parent != null && item.Parent.Move(item, offset))
        {
            RefreshScoreTree(item);
        }
    }

    /// <summary>Rebuilds the score tree from the model.</summary>
    /// <param name="select">The row to leave chosen, or null.</param>
    private void RefreshScoreTree(PartTreeItem select = null)
    {
        if (_scoreTree == null) { return; }

        _scoreNodes.Clear();
        _scoreTree.RootNodes.Clear();
        TreeViewNode chosen = null;

        void Fill(PartTreeItem item, IList<TreeViewNode> into)
        {
            foreach (PartTreeItem child in item.Children)
            {
                TreeViewNode node = new TreeViewNode
                {
                    Content = child.Part.Title(),
                    IsExpanded = true,
                };
                _scoreNodes[node] = child;
                into.Add(node);
                if (ReferenceEquals(child, select)) { chosen = node; }

                Fill(child, node.Children);
            }
        }

        Fill(Model.Root, _scoreTree.RootNodes);
        if (chosen != null) { _scoreTree.SelectedNode = chosen; }

        ShowPartSettings();
    }

    /// <summary>Shows the settings of whichever part is chosen.</summary>
    private void ShowPartSettings()
    {
        if (_partSettings == null) { return; }

        if (_scoreTree.SelectedNode == null
            || !_scoreNodes.TryGetValue(_scoreTree.SelectedNode, out PartTreeItem item))
        {
            _partSettings.Content = null;
            return;
        }

        StackPanel panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = item.Part.Title(),
            FontWeight = FontWeights.Bold,
        });

        if (item.Part.Settings.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "(" + I18n.Get("No settings available.") + ")",
                Opacity = 0.7,
            });
        }
        else
        {
            panel.Children.Add(SettingsEditor.Build(item.Part.Settings));
        }

        _partSettings.Content = panel;
    }

    // ------------------------------------------------------------- the settings

    /// <summary>Builds the page that sets the score's settings.</summary>
    /// <returns>The page.</returns>
    private UIElement BuildSettingsPage()
    {
        Grid page = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        page.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        page.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void Place(UIElement element, int column, int row, int rowSpan = 1)
        {
            Grid.SetColumn(element, column);
            Grid.SetRow(element, row);
            Grid.SetRowSpan(element, rowSpan);
            page.Children.Add(element);
        }

        Place(BuildScorePropertiesGroup(), 0, 0);
        Place(
            SettingsEditor.Group(
                I18n.Get("General preferences"), Model.GeneralPreferences.Settings),
            1,
            0);
        Place(BuildEngineGroup(), 0, 1, 2);
        Place(SettingsEditor.Group(Model.InstrumentNames.Group), 1, 1);
        Place(SettingsEditor.Group(Model.MidiOutput.Group), 1, 2);

        return new ScrollViewer
        {
            Content = page,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    /// <summary>
    /// Builds the Score properties group, with the tap-tempo button under it.
    /// </summary>
    /// <returns>The group.</returns>
    /// <remarks>
    /// //was previously: <c>SettingsEditor.Group</c> alone, so the "Round tap
    /// tempo value" checkbox governed a tempo nothing could tap.
    /// ⚠ PLACEMENT: upstream puts the button on the metronome ROW, beside the
    /// value combo (scorewiz/scoreproperties.py). The settings on this page are
    /// drawn from a list by <c>SettingsEditor</c>, one label-and-control row
    /// each, and reaching inside that to widen one row would make the editor
    /// know about this one setting; the button therefore sits under the group,
    /// which is where the checkbox it works with already is.
    /// </remarks>
    private UIElement BuildScorePropertiesGroup()
    {
        StackPanel content = new StackPanel { Spacing = 6 };
        content.Children.Add(SettingsEditor.Build(Model.ScoreProperties.Settings));

        Widgets.TempoButton tap = new Widgets.TempoButton
        {
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        tap.Tempo += (_, beatsPerMinute)
            => Model.ScoreProperties.SetMetronomeValue(beatsPerMinute);
        content.Children.Add(tap);
        content.Children.Add(new TextBlock
        {
            //Upstream's What's This on the same button, said out loud because
            //there is no What's This cursor mode here (see MenuBuilder's Help
            //menu note).
            Text = I18n.Get(
                "Tap this button to set the tempo.\n\n"
                + "The average speed of clicking is used; wait 3 seconds to "
                + "\"reset\"."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        });

        return SettingsEditor.Wrap(I18n.Get("Score properties"), content);
    }

    /// <summary>Builds the group that names the engine and its pitch language.</summary>
    /// <returns>The group.</returns>
    /// <remarks>Upstream's second half of this group is a version chooser.
    /// FR5.1 compiles one engine in, so the version is shown rather than
    /// chosen, and FR13 keeps LilyPort's own version and the LilyPond release
    /// it implements apart.</remarks>
    private UIElement BuildEngineGroup()
    {
        StackPanel content = new StackPanel { Spacing = 6 };
        content.Children.Add(SettingsEditor.Build(
            new[] { Model.EnginePreferences.PitchLanguage }));

        Grid versions = new Grid { ColumnSpacing = 8, RowSpacing = 2 };
        versions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        versions.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        void Row(int row, string label, string value)
        {
            versions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock name = new TextBlock { Text = label };
            Grid.SetRow(name, row);
            versions.Children.Add(name);

            TextBlock text = new TextBlock { Text = value, Opacity = 0.8 };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            versions.Children.Add(text);
        }

        Row(0, I18n.Get("LilyPort version:"), LilyPortEngine.PortVersion);
        Row(1, I18n.Get("Compatible with:"), Model.Version);
        content.Children.Add(versions);

        return SettingsEditor.Wrap(I18n.Get("LilyPort"), content);
    }
}
