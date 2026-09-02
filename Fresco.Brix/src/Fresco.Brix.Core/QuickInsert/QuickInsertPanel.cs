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
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;
using Panel = Fresco.Brix.Shell.Panel;

namespace Fresco.Brix.QuickInsert; //was previously: frescobaldi/quickinsert/

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One button of the Quick Insert panel.</summary>
public sealed class QuickInsertButton
{
    /// <summary>Creates a button.</summary>
    /// <param name="name">Its stable name, which is also its symbol name.</param>
    /// <param name="text">Its tool tip.</param>
    public QuickInsertButton(string name, string text)
    {
        Name = name;
        Text = text;
    }

    /// <summary>Gets the stable name.</summary>
    public string Name { get; }

    /// <summary>Gets the tool tip.</summary>
    public string Text { get; }
}

/// <summary>A titled group of Quick Insert buttons.</summary>
public sealed class QuickInsertGroup
{
    /// <summary>Creates a group.</summary>
    /// <param name="title">Its title.</param>
    /// <param name="buttons">Its buttons.</param>
    public QuickInsertGroup(string title, IReadOnlyList<QuickInsertButton> buttons)
    {
        Title = title;
        Buttons = buttons;
    }

    /// <summary>Gets the title.</summary>
    public string Title { get; }

    /// <summary>Gets the buttons.</summary>
    public IReadOnlyList<QuickInsertButton> Buttons { get; }
}

/// <summary>One page of the Quick Insert panel.</summary>
public sealed class QuickInsertTool
{
    /// <summary>Creates a tool page.</summary>
    /// <param name="name">Its stable name.</param>
    /// <param name="title">Its tab title.</param>
    /// <param name="toolTip">Its tab tool tip.</param>
    /// <param name="groups">Its button groups.</param>
    public QuickInsertTool(
        string name,
        string title,
        string toolTip,
        IReadOnlyList<QuickInsertGroup> groups)
    {
        Name = name;
        Title = title;
        ToolTip = toolTip;
        Groups = groups;
    }

    /// <summary>Gets the stable name.</summary>
    public string Name { get; }

    /// <summary>Gets the tab title.</summary>
    public string Title { get; }

    /// <summary>Gets the tab tool tip.</summary>
    public string ToolTip { get; }

    /// <summary>Gets the button groups.</summary>
    public IReadOnlyList<QuickInsertGroup> Groups { get; }
}

/// <summary>
/// The Quick Insert panel: pages of buttons that put articulations, dynamics,
/// spanners and bar lines into the music, each drawn with the sign it inserts.
/// </summary>
public sealed class QuickInsertPanel : Panel
{
    /// <summary>The setting the open page is remembered in.</summary>
    public const string CurrentToolKey = "quickinsert/current_tool";

    /// <summary>The setting the direction picker is remembered in.</summary>
    public const string DirectionKey = "quickinsert/direction";

    private readonly SettingsStore _settings;
    private ComboBox _direction;
    private CheckBox _shorthands;
    private Button _removeMenu;

    /// <summary>Creates the panel.</summary>
    /// <param name="settings">The settings store, or null.</param>
    public QuickInsertPanel(SettingsStore settings = null)
        : base("quickinsert", DockArea.Left)
    {
        _settings = settings;
        ToggleAction.WithShortcut("Meta+Alt+I");
        Shortcuts = new QuickInsertShortcuts(settings);
        Shortcuts.Apply = name => Insert?.Invoke(name);
    }

    /// <summary>Gets the panel's keyboard shortcuts.</summary>
    public QuickInsertShortcuts Shortcuts { get; }

    /// <summary>Gets or sets what pressing a button does.</summary>
    public Action<string> Insert { get; set; }

    /// <summary>Gets or sets what to do after a button is pressed.</summary>
    public Action FocusEditor { get; set; }

    /// <summary>Gets the direction the picker is on.</summary>
    public InsertDirection Direction
        => _direction == null
            ? InsertDirection.Neutral
            : (InsertDirection)(1 - _direction.SelectedIndex);

    /// <summary>Gets whether short articulation forms are allowed.</summary>
    public bool AllowShorthands => _shorthands?.IsChecked ?? true;

    /// <summary>
    /// Gets or sets the "quick remove" commands, by their upstream kind —
    /// what the Articulations header's Remove drop-down offers.
    /// </summary>
    public System.Collections.Generic.IReadOnlyDictionary<string, Commands.AppAction>
        QuickRemove { get; set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Quick Insert");

    /// <inheritdoc/>
    public override void TranslateUI()
        => ToggleAction.Text = I18n.Get("Quick &Insert");

    /// <summary>Gets the panel's pages, built fresh for the language.</summary>
    /// <returns>The pages.</returns>
    public static IReadOnlyList<QuickInsertTool> Tools() => new[]
    {
        new QuickInsertTool(
            "articulations",
            I18n.Get("Articulations"),
            I18n.Get("Different kinds of articulations and other signs."),
            new[]
            {
                Group(I18n.Get("Articulations"), "articulation_", new[]
                {
                    ("accent", I18n.Get("Accent")),
                    ("marcato", I18n.Get("Marcato")),
                    ("staccatissimo", I18n.Get("Staccatissimo")),
                    ("staccato", I18n.Get("Staccato")),
                    ("portato", I18n.Get("Portato")),
                    ("tenuto", I18n.Get("Tenuto")),
                    ("espressivo", I18n.Get("Espressivo")),
                }),
                Group(I18n.Get("Ornaments"), "articulation_", new[]
                {
                    ("trill", I18n.Get("Trill")),
                    ("prall", I18n.Get("Prall")),
                    ("mordent", I18n.Get("Mordent")),
                    ("turn", I18n.Get("Turn")),
                    ("prallprall", I18n.Get("Prall prall")),
                    ("prallmordent", I18n.Get("Prall mordent")),
                    ("upprall", I18n.Get("Up prall")),
                    ("downprall", I18n.Get("Down prall")),
                    ("upmordent", I18n.Get("Up mordent")),
                    ("downmordent", I18n.Get("Down mordent")),
                    ("prallup", I18n.Get("Prall up")),
                    ("pralldown", I18n.Get("Prall down")),
                    ("lineprall", I18n.Get("Line prall")),
                    ("reverseturn", I18n.Get("Reverse turn")),
                }),
                Group(I18n.Get("Signs"), "articulation_", new[]
                {
                    ("fermata", I18n.Get("Fermata")),
                    ("shortfermata", I18n.Get("Short fermata")),
                    ("longfermata", I18n.Get("Long fermata")),
                    ("verylongfermata", I18n.Get("Very long fermata")),
                    ("segno", I18n.Get("Segno")),
                    ("coda", I18n.Get("Coda")),
                    ("varcoda", I18n.Get("Varcoda")),
                    ("signumcongruentiae", I18n.Get("Signumcongruentiae")),
                }),
                Group(I18n.Get("Other"), "articulation_", new[]
                {
                    ("upbow", I18n.Get("Upbow")),
                    ("downbow", I18n.Get("Downbow")),
                    ("snappizzicato", I18n.Get("Snappizzicato")),
                    ("open", I18n.Get("Open (e.g. brass)")),
                    ("stopped", I18n.Get("Stopped (e.g. brass)")),
                    ("flageolet", I18n.Get("Flageolet")),
                    ("thumb", I18n.Get("Thumb")),
                    ("lheel", I18n.Get("Left heel")),
                    ("rheel", I18n.Get("Right heel")),
                    ("ltoe", I18n.Get("Left toe")),
                    ("rtoe", I18n.Get("Right toe")),
                    ("halfopen", I18n.Get("Half open (e.g. hi-hat)")),
                }),
            }),

        new QuickInsertTool(
            "dynamics",
            I18n.Get("Dynamics"),
            I18n.Get("Dynamic symbols"),
            new[]
            {
                new QuickInsertGroup(
                    I18n.Get("Dynamics"),
                    QuickInsertLogic.DynamicMarks
                        .Select(m => new QuickInsertButton(
                            "dynamic_" + m,
                            I18n.Format(
                                I18n.Get("Dynamic sign {name}"), ("name", m))))
                        .ToList()),
                new QuickInsertGroup(I18n.Get("Spanners"), new[]
                {
                    new QuickInsertButton(
                        "dynamic_hairpin_cresc", I18n.Get("Hairpin crescendo")),
                    new QuickInsertButton(
                        "dynamic_cresc", I18n.Get("Crescendo")),
                    new QuickInsertButton(
                        "dynamic_hairpin_dim", I18n.Get("Hairpin diminuendo")),
                    new QuickInsertButton("dynamic_dim", I18n.Get("Diminuendo")),
                    new QuickInsertButton(
                        "dynamic_decresc", I18n.Get("Decrescendo")),
                }),
            }),

        new QuickInsertTool(
            "spanners",
            I18n.Get("Spanners"),
            I18n.Get("Slurs, spanners, etc."),
            new[]
            {
                Group(I18n.Get("Arpeggios"), string.Empty, new[]
                {
                    ("arpeggio_normal", I18n.Get("Arpeggio")),
                    ("arpeggio_arrow_up", I18n.Get("Arpeggio with Up Arrow")),
                    ("arpeggio_arrow_down", I18n.Get("Arpeggio with Down Arrow")),
                    ("arpeggio_bracket", I18n.Get("Bracket Arpeggio")),
                    ("arpeggio_parenthesis", I18n.Get("Parenthesis Arpeggio")),
                }),
                Group(I18n.Get("Glissandos"), string.Empty, new[]
                {
                    ("glissando_normal", I18n.Get("Glissando")),
                    ("glissando_dashed", I18n.Get("Dashed Glissando")),
                    ("glissando_dotted", I18n.Get("Dotted Glissando")),
                    ("glissando_zigzag", I18n.Get("Zigzag Glissando")),
                    ("glissando_trill", I18n.Get("Trill Glissando")),
                }),
                Group(I18n.Get("Spanners"), string.Empty, new[]
                {
                    ("spanner_slur", I18n.Get("Slur")),
                    ("spanner_phrasingslur", I18n.Get("Phrasing Slur")),
                    ("spanner_beam16", I18n.Get("Beam")),
                    ("spanner_trill", I18n.Get("Trill")),
                    ("spanner_melisma", I18n.Get("Melisma")),
                }),
                Group(I18n.Get("Grace Notes"), string.Empty, new[]
                {
                    ("grace_grace", I18n.Get("Grace Notes")),
                    ("grace_beam", I18n.Get("Grace Notes w. beaming")),
                    ("grace_accia", I18n.Get("Acciaccatura")),
                    ("grace_appog", I18n.Get("Appoggiatura")),
                    ("grace_slash", I18n.Get("Slashed no slur")),
                    ("grace_after", I18n.Get("After grace")),
                }),
            }),

        new QuickInsertTool(
            "barlines",
            I18n.Get("Bar Lines"),
            I18n.Get("Bar lines, breathing signs, etc."),
            new[]
            {
                new QuickInsertGroup(
                    I18n.Get("Bar Lines"),
                    BarLines.Select(b => new QuickInsertButton(b.Name, b.Title))
                        .ToList()),
                Group(I18n.Get("Breathing Signs"), string.Empty, new[]
                {
                    ("breathe_rcomma", I18n.Get("Default Breathing Sign")),
                    ("breathe_rvarcomma", I18n.Get("Straight Breathing Sign")),
                    ("breathe_caesura_curved", I18n.Get("Curved Caesura")),
                    ("breathe_caesura_straight", I18n.Get("Straight Caesura")),
                }),
            }),
    };

    /// <summary>The bar lines, with the glyph each version wants.</summary>
    /// <remarks>The first glyph is what LilyPond before 2.18 expects, the
    /// second what 2.18 and later expect; the document's own <c>\version</c>
    /// decides which is written.</remarks>
    public static readonly IReadOnlyList<(string Name, string Old, string New, string Title)>
        BarLines = new[]
    {
        ("bar_double", "||", "||", I18n.Get("Double bar line")),
        ("bar_end", "|.", "|.", I18n.Get("Ending bar line")),
        ("bar_dotted", ":", ";", I18n.Get("Dotted bar line")),
        ("bar_dashed", "dashed", "!", I18n.Get("Dashed bar line")),
        ("bar_invisible", "", "", I18n.Get("Invisible bar line")),
        ("bar_repeat_start", "|:", ".|:", I18n.Get("Repeat start")),
        ("bar_repeat_double", ":|:", ":..:", I18n.Get("Repeat both")),
        ("bar_repeat_end", ":|", ":|.", I18n.Get("Repeat end")),
        ("bar_cswc", ":|.:", ":|.:", I18n.Get("Repeat both (old)")),
        ("bar_cswsc", ":|.|:", ":|.|:", I18n.Get("Repeat both (classic)")),
        ("bar_tick", "'", "'", I18n.Get("Tick bar line")),
        ("bar_single", "|", "|", I18n.Get("Single bar line")),
        ("bar_sws", "|.|", "|.|", I18n.Get("Small-Wide-Small bar line")),
        ("bar_ws", ".|", ".|", I18n.Get("Wide-Small bar line")),
        ("bar_ww", ".|.", "..", I18n.Get("Double wide bar line")),
        ("bar_segno", "S", "S", I18n.Get("Segno bar line")),
        ("bar_w", ".", ".", I18n.Get("Single wide bar line")),
        ("bar_repeat_angled_start", null, "[|:", I18n.Get("Angled repeat start")),
        ("bar_repeat_angled_end", null, ":|]", I18n.Get("Angled repeat end")),
        ("bar_repeat_angled_double", null, ":|][|:",
            I18n.Get("Angled repeat both")),
        ("bar_kievan", null, "k", I18n.Get("Kievan bar line")),
    };

    /// <inheritdoc/>
    protected override UIElement CreateWidget()
    {
        FillGrid root = new FillGrid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });

        _iconColor = root.ActualTheme == ElementTheme.Dark
            ? Color.FromArgb(0xff, 0xe8, 0xe8, 0xe8)
            : Color.FromArgb(0xff, 0x10, 0x10, 0x10);

        root.Children.Add(BuildHeader());

        //A row of buttons over one page at a time, rather than a Pivot: the
        //theme's tab controls paint nothing on the Skia heads (the same sharp
        //edge as the standalone ScrollBar and the bare Thumb), and this is the
        //house answer — build the chrome out of primitives that do.
        IReadOnlyList<QuickInsertTool> tools = Tools();
        Grid pages = new Grid();
        StackPanel tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Padding = new Thickness(4, 0, 4, 4),
        };

        List<Button> tabButtons = new List<Button>();
        for (int i = 0; i < tools.Count; i++)
        {
            QuickInsertTool tool = tools[i];
            UIElement page = BuildTool(tool);
            page.Visibility = Visibility.Collapsed;
            pages.Children.Add(page);

            Button tab = new Button
            {
                Content = tool.Title,
                Padding = new Thickness(8, 3, 8, 3),
            };
            ToolTipService.SetToolTip(tab, tool.ToolTip);
            tabButtons.Add(tab);
            tabs.Children.Add(tab);

            int index = i;
            tab.Click += (_, _) => ShowPage(tools, pages, tabButtons, index);
        }

        Grid.SetRow(tabs, 1);
        Grid.SetRow(pages, 2);
        root.RowDefinitions.Insert(1, new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(tabs);
        root.Children.Add(pages);

        string remembered = _settings?.GetString(CurrentToolKey, string.Empty);
        int start = string.IsNullOrEmpty(remembered)
            ? 0
            : Math.Max(0, tools.ToList().FindIndex(
                t => string.Equals(t.Name, remembered, StringComparison.Ordinal)));
        ShowPage(tools, pages, tabButtons, start);

        Shortcuts.Register(tools.SelectMany(t => t.Groups)
            .SelectMany(g => g.Buttons)
            .Select(b => b.Name));
        return root;
    }

    private void ShowPage(
        IReadOnlyList<QuickInsertTool> tools,
        Grid pages,
        IReadOnlyList<Button> tabs,
        int index)
    {
        for (int i = 0; i < pages.Children.Count; i++)
        {
            pages.Children[i].Visibility = i == index
                ? Visibility.Visible
                : Visibility.Collapsed;
            tabs[i].FontWeight = i == index
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
        }

        if (index >= 0 && index < tools.Count)
        {
            _settings?.SetString(CurrentToolKey, tools[index].Name);
        }
    }

    private Grid BuildHeader()
    {
        Grid header = new Grid { ColumnSpacing = 6, Padding = new Thickness(6) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock label = new TextBlock
        {
            Text = I18n.Get("Direction:"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _direction = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _direction.Items.Add(I18n.Get("Up"));
        _direction.Items.Add(I18n.Get("Neutral"));
        _direction.Items.Add(I18n.Get("Down"));
        _direction.SelectedIndex = Math.Clamp(_settings?.GetInt(DirectionKey) ?? 1, 0, 2);
        _direction.SelectionChanged += (_, _)
            => _settings?.SetInt(DirectionKey, _direction.SelectedIndex);

        _shorthands = new CheckBox
        {
            Content = I18n.Get("Allow shorthands"),
            IsChecked = true,
        };
        ToolTipService.SetToolTip(_shorthands, I18n.Get(
            "Use short notation for some articulations like staccato."));

        //Upstream puts a drop-down beside the shorthands box holding the three
        //"take these off again" commands, enabled only with a selection
        //(quickinsert/articulations.py). All three commands were already here;
        //nothing in this panel reached them.
        _removeMenu = new Button
        {
            //Upstream's button is icon-only (`edit-clear'); there is no icon
            //set here, so it carries a caption — the existing "&Remove" msgid,
            //rather than a new one, because that is exactly the word.
            Content = Shell.MenuBuilder.Display(I18n.Get("&Remove")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(_removeMenu, I18n.Get("Remove articulations etc."));
        _removeMenu.Click += (_, _) => ShowRemoveMenu();

        StackPanel right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        right.Children.Add(_shorthands);
        right.Children.Add(_removeMenu);

        Grid.SetColumn(label, 0);
        Grid.SetColumn(_direction, 1);
        Grid.SetColumn(right, 2);
        header.Children.Add(label);
        header.Children.Add(_direction);
        header.Children.Add(right);
        return header;
    }

    /// <summary>Drops the three "remove these" commands down.</summary>
    /// <remarks>Upstream's <c>QToolButton</c> in <c>InstantPopup</c> mode. The
    /// entries follow their commands' own enablement, which the window turns
    /// on and off with the selection.</remarks>
    private void ShowRemoveMenu()
    {
        if (QuickRemove == null || _removeMenu == null) { return; }

        MenuFlyout flyout = new MenuFlyout();
        foreach (var kind in new[] { "articulations", "ornaments", "instrument_scripts" })
        {
            if (QuickRemove.TryGetValue(kind, out var action))
            {
                flyout.Items.Add(Shell.MenuBuilder.ItemFor(action));
            }
        }

        if (flyout.Items.Count > 0) { flyout.ShowAt(_removeMenu); }
    }

    /// <summary>
    /// Gets the colour the glyphs are drawn in: the theme's own text colour,
    /// so a dark theme gets light symbols and a light theme dark ones.
    /// </summary>
    /// <remarks>Upstream recolours its SVGs to the palette's text colour for
    /// exactly this reason. Read once when the panel is built; a theme change
    /// while the application is running is W12's polish.</remarks>
    private Color _iconColor = Color.FromArgb(0xff, 0x10, 0x10, 0x10);

    private UIElement BuildTool(QuickInsertTool tool)
    {
        StackPanel stack = new StackPanel { Spacing = 8, Padding = new Thickness(4) };
        foreach (var group in tool.Groups)
        {
            stack.Children.Add(new TextBlock
            {
                Text = group.Title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(2, 6, 2, 2),
            });

            //Five buttons to a row, as upstream's grid lays them out.
            Grid grid = new Grid();
            for (int i = 0; i < 5; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = GridLength.Auto,
                });
            }

            int rows = (group.Buttons.Count + 4) / 5;
            for (int i = 0; i < rows; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (int i = 0; i < group.Buttons.Count; i++)
            {
                Button button = MakeButton(group.Buttons[i]);
                Grid.SetRow(button, i / 5);
                Grid.SetColumn(button, i % 5);
                grid.Children.Add(button);
            }

            stack.Children.Add(grid);
        }

        return new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private Button MakeButton(QuickInsertButton definition)
    {
        Button button = new Button
        {
            Width = 34,
            Height = 34,
            Padding = new Thickness(2),
            Content = SymbolIcons.Icon(definition.Name, _iconColor)
                ?? (object)new TextBlock
                {
                    //A symbol with no engraved glyph still gets a button: it
                    //shows the name's last word rather than nothing at all.
                    Text = definition.Name.Split('_').Last(),
                    FontSize = 9,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
        };

        string key = Shortcuts.Shortcuts(definition.Name).FirstOrDefault()?.ToString();
        ToolTipService.SetToolTip(
            button,
            key == null
                ? definition.Text
                : I18n.Format(
                    I18n.Get("{name} ({key})"),
                    ("name", definition.Text), ("key", key)));

        button.Click += (_, _) =>
        {
            Insert?.Invoke(definition.Name);
            FocusEditor?.Invoke();
        };
        return button;
    }

    private static QuickInsertGroup Group(
        string title, string prefix, IReadOnlyList<(string Name, string Text)> buttons)
        => new QuickInsertGroup(
            title,
            buttons.Select(b => new QuickInsertButton(prefix + b.Name, b.Text)).ToList());
}

/// <summary>The Quick Insert panel's keyboard shortcuts.</summary>
public sealed class QuickInsertShortcuts : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "quickinsert";

    /// <summary>The default shortcuts, by button name.</summary>
    public static readonly IReadOnlyDictionary<string, string> UpstreamDefaults
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["articulation_staccato"] = "Ctrl+.",
            ["spanner_slur"] = "Ctrl+(",
            ["breathe_rcomma"] = "Alt+'",
        };

    /// <summary>Creates the collection.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public QuickInsertShortcuts(SettingsStore settings = null)
        : base(CollectionName, settings) => Initialize();

    /// <summary>Gets or sets what pressing a shortcut does.</summary>
    public Action<string> Apply { get; set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Quick Insert");

    /// <summary>Adds actions for the panel's buttons.</summary>
    /// <param name="names">The button names.</param>
    /// <remarks>The buttons are built when the panel is first shown, which is
    /// after this collection exists — so like the panel toggles, this one
    /// grows and reloads its stored shortcuts when it does.</remarks>
    public void Register(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (Action(name) != null) { continue; }

            AppAction action = Add(name);
            if (UpstreamDefaults.TryGetValue(name, out string shortcut))
            {
                action.WithShortcut(shortcut);
            }

            string button = name;
            action.Handler = () => Apply?.Invoke(button);
        }

        Load(false);
    }

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        //The actions belong to the buttons; they arrive through Register.
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        //The texts are the buttons' tool tips, set where the buttons are made.
    }
}
