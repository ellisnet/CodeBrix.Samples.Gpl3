// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Editor;
using Fresco.Brix.Ly.Colorizing;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Fresco.Brix.Widgets;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Windows.UI;

namespace Fresco.Brix.Preferences; //was previously: frescobaldi/preferences/fontscolors.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Fonts &amp; Colors page: the editor's font, its base colours, and every
/// highlighting style — per named scheme.
/// </summary>
/// <remarks>
/// <para>
/// The styles come from python-ly's own mapping
/// (<see cref="Colorize.DefaultMapping"/>), so a style added there appears here
/// with no change; what each style INHERITS is what makes a default style's
/// change reach every style built on it, and a per-style attribute left unset
/// is what lets it.
/// </para>
/// <para>
/// //was previously: a <c>QFontComboBox</c> listing every family the desktop
/// has. Standing rule 6 forbids a system font anywhere in Fresco.Brix — tofu is
/// the desired failure mode — so the list is the faces the application SHIPS.
/// </para>
/// </remarks>
public sealed class FontsColorsPage : PreferencesPage
{
    /// <summary>
    /// The faces the application ships, as the editor may be set to use them.
    /// </summary>
    /// <remarks>Decision FD4: the interface is Roboto and the editor is Roboto
    /// Mono. Both are packages the application depends on, so both are always
    /// there; nothing else is offered.</remarks>
    public static readonly IReadOnlyList<string> AvailableFonts = new[]
    {
        "RobotoMonoFont", "RobotoFont",
    };

    private readonly Dictionary<string, TextFormatData> _data
        = new Dictionary<string, TextFormatData>(StringComparer.Ordinal);
    private readonly Dictionary<string, ColorButton> _baseColors
        = new Dictionary<string, ColorButton>(StringComparer.Ordinal);
    private readonly List<StyleEntry> _entries = new List<StyleEntry>();

    private SchemeSelector _scheme;
    private CheckBox _printScheme;
    private ComboBox _font;
    private NumberEntry _fontSize;
    private ListView _tree;
    private Grid _stack;
    private UIElement _baseColorPanel;
    private UIElement _attributePanel;
    private TextBlock _attributeTitle;
    private TextBlock _attributeInherits;
    private ColorButton _textColor;
    private ColorButton _backgroundColor;
    private ColorButton _underlineColor;
    private CheckBox _bold;
    private CheckBox _italic;
    private CheckBox _underline;

    private string _printSchemeKey;
    private bool _updating;

    /// <summary>Creates the page.</summary>
    /// <param name="context">What the page configures.</param>
    public FontsColorsPage(PreferencesContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Fonts & Colors");

    /// <inheritdoc/>
    public override string Help => "prefs_fontscolors";

    /// <inheritdoc/>
    public override string IconName => "applications-graphics";

    /// <inheritdoc/>
    public override void LoadSettings()
    {
        _data.Clear();
        _printSchemeKey = Settings?.GetString(
            TextFormatData.PrinterSchemeSettingKey, "default") ?? "default";
        _scheme.LoadSettings(
            Settings, TextFormatData.SchemeSettingKey, TextFormatData.SchemeNamesKey);
        ShowScheme();
    }

    /// <inheritdoc/>
    public override void SaveSettings()
    {
        //The scheme names first, so a scheme that was renamed keeps its data,
        //and the removed ones take their `fontscolors' subtree with them.
        //was previously: the prefix "fontscolors/editor", which SchemeSelector
        //deleted by key prefix. A removed scheme's colours are now dropped by
        //the type that owns them (board W13 item 9, route (a)).
        _scheme.SaveSettings(
            Settings,
            TextFormatData.SchemeSettingKey,
            TextFormatData.SchemeNamesKey,
            scheme => TextFormatData.ForgetScheme(Settings, "editor", scheme));

        foreach (var scheme in _scheme.Schemes)
        {
            if (_data.TryGetValue(scheme, out var data)) { data.Save(Settings); }
        }

        if (string.IsNullOrEmpty(_printSchemeKey))
        {
            Settings?.Remove(TextFormatData.PrinterSchemeSettingKey);
        }
        else
        {
            Settings?.SetString(TextFormatData.PrinterSchemeSettingKey, _printSchemeKey);
        }
    }

    /// <inheritdoc/>
    protected override UIElement Build()
    {
        _scheme = new SchemeSelector { DialogRoot = DialogRoot };
        _scheme.CurrentChanged += (_, _) => ShowScheme();
        _scheme.Changed += (_, _) => MarkChanged();

        //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14 / FR5.5), caption
        //only. Upstream's caption is "Use this scheme for printing", and its
        //`printer_scheme' key has exactly one reader —
        //`textformats.formatData('printer')' inside `mainwindow.printSource()'.
        //FR5.5 removes printing for good, so that caption would name a feature
        //this application will never have; what survives of "a second colour
        //scheme for output that is not the editor" is File ▸ Export ▸ colored
        //HTML and Edit ▸ Copy as Colored HTML, and those are what the setting
        //now governs (MainViewModel.ReadHtmlOptions). The stored KEY is
        //unchanged — a Frescobaldi settings file still carries it — and the
        //caption is a Fresco.Brix-original msgid recorded in the harvest tool's
        //renamed-string table, so it falls back to English until translated.
        //was previously: the caption above, on a control that changed nothing.
        _printScheme = Tick(I18n.Get("Use this scheme for exported source"));
        _printScheme.Checked += (_, _) => _printSchemeKey = _scheme.CurrentSchemeKey;
        _printScheme.Unchecked += (_, _) => _printSchemeKey = null;

        _font = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var family in AvailableFonts)
        {
            _font.Items.Add(new ComboBoxItem { Content = FontLabel(family) });
        }

        _font.SelectionChanged += (_, _) => FontChanged();

        _fontSize = Number(6, 32);
        _fontSize.ValueChanged += (_, _) => FontChanged();

        _tree = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = StyleTemplate(),
            MinWidth = 240,
            MinHeight = 340,
        };
        _tree.SelectionChanged += (_, _) => ShowCurrentStyle();

        _stack = new Grid();
        _baseColorPanel = BuildBaseColors();
        _attributePanel = BuildAttributes();
        _stack.Children.Add(_baseColorPanel);
        _stack.Children.Add(_attributePanel);

        Grid split = new Grid { ColumnSpacing = 10 };
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        split.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        split.Children.Add(_tree);
        Grid.SetColumn(_stack, 1);
        split.Children.Add(_stack);

        StackPanel fontRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        fontRow.Children.Add(new TextBlock
        {
            Text = I18n.Get("Font:"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        fontRow.Children.Add(_font);
        fontRow.Children.Add(_fontSize);

        return Stack(_scheme, _printScheme, split, fontRow);
    }

    /// <summary>What a shipped font family is called in the list.</summary>
    /// <param name="resourceKey">The application resource key of the family.</param>
    /// <returns>The name.</returns>
    private static string FontLabel(string resourceKey)
        => resourceKey switch
        {
            "RobotoMonoFont" => "Roboto Mono",
            "RobotoFont" => "Roboto",
            _ => resourceKey,
        };

    private static DataTemplate StyleTemplate()
    {
        //A ListView row's Content renders as its TYPE NAME without a template
        //(board trap 40's sibling); the template is what puts the words on the
        //screen, and it carries the style's own look so the list IS the
        //preview upstream's tree is.
        string xaml =
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
            + "<TextBlock Text=\"{Binding Label}\" Foreground=\"{Binding Foreground}\" "
            + "FontWeight=\"{Binding Weight}\" FontStyle=\"{Binding Style}\" "
            + "Margin=\"{Binding Indent}\" />"
            + "</DataTemplate>";
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }

    private UIElement BuildBaseColors()
    {
        Grid grid = new Grid { ColumnSpacing = 8, RowSpacing = 2 };

        //⚠ AUTO, not a fixed 180: these are the longest translated labels on
        //any preferences page, and German runs past 180 pixels — "Hervorhebung
        //in der Vorschau" was clipped mid-word before this. One Grid holds
        //every row, so Auto still lines the colour buttons up; the MinWidth
        //keeps the English page looking exactly as it did (board rule 7 —
        //layouts tolerate German- and French-length expansion).
        //was previously: new GridLength(180).
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            MinWidth = 180,
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        int row = 0;
        foreach (var name in TextFormatData.BaseColorNames)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock label = new TextBlock
            {
                Text = BaseColorName(name),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(label, row);
            grid.Children.Add(label);

            ColorButton button = new ColorButton { DialogRoot = DialogRoot };
            string colorName = name;
            button.ColorChanged += (_, _) =>
            {
                if (_updating) { return; }

                TextFormatData data = CurrentData();
                if (data != null && button.Color != null)
                {
                    data.SetBaseColor(colorName, button.Color.Value);
                }

                RefreshStyleList();
                MarkChanged();
            };

            _baseColors[name] = button;
            Grid.SetRow(button, row);
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
            row++;
        }

        return Group(I18n.Get("Base Colors"), grid);
    }

    private UIElement BuildAttributes()
    {
        _attributeTitle = new TextBlock { FontWeight = FontWeights.SemiBold };
        _attributeInherits = new TextBlock
        {
            Opacity = 0.7,
            HorizontalTextAlignment = TextAlignment.Center,
        };

        _textColor = ColorRow(I18n.Get("Text"), out UIElement textRow);
        _backgroundColor = ColorRow(I18n.Get("Background"), out UIElement backgroundRow);

        _bold = StyleTick(I18n.Get("Bold"));
        _italic = StyleTick(I18n.Get("Italic"));
        _underline = StyleTick(I18n.Get("Underline"));

        _underlineColor = ColorRow(I18n.Get("Underline"), out UIElement underlineRow);

        StackPanel panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(_attributeTitle);
        panel.Children.Add(_attributeInherits);
        panel.Children.Add(textRow);
        panel.Children.Add(backgroundRow);
        panel.Children.Add(_bold);
        panel.Children.Add(_italic);
        panel.Children.Add(_underline);
        panel.Children.Add(underlineRow);

        UIElement group = Group(string.Empty, panel);
        group.Visibility = Visibility.Collapsed;
        return group;
    }

    /// <summary>
    /// A tick box with THREE states: on, off, and "say nothing, and let what
    /// this style inherits decide".
    /// </summary>
    /// <param name="label">The caption.</param>
    /// <returns>The box.</returns>
    /// <remarks>Upstream's <c>setTristate</c>, which is only turned on for a
    /// style that HAS something to inherit; a default style has nothing above
    /// it, so its boxes are plain.</remarks>
    private CheckBox StyleTick(string label)
    {
        CheckBox box = new CheckBox { Content = label };
        box.Checked += (_, _) => AttributeChanged();
        box.Unchecked += (_, _) => AttributeChanged();
        box.Indeterminate += (_, _) => AttributeChanged();
        return box;
    }

    private ColorButton ColorRow(string label, out UIElement row)
    {
        ColorButton button = new ColorButton { DialogRoot = DialogRoot };
        button.ColorChanged += (_, _) => AttributeChanged();

        Button clear = new Button { Content = "✕" };
        ToolTipService.SetToolTip(clear, I18n.Get("Clear the color."));
        clear.Click += (_, _) => button.Clear();

        Grid grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock text = new TextBlock
        {
            Text = label,

            //Each of these rows is its OWN Grid, so the column has to stay a
            //fixed width for the colour buttons to line up down the page — a
            //translation longer than 120 pixels therefore WRAPS rather than
            //widening (board rule 7).
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(text);
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        Grid.SetColumn(clear, 2);
        grid.Children.Add(clear);

        row = grid;
        return button;
    }

    private TextFormatData CurrentData()
    {
        string scheme = _scheme.CurrentSchemeKey;
        if (!_data.TryGetValue(scheme, out var data))
        {
            data = new TextFormatData(scheme, Settings);
            _data[scheme] = data;
        }

        return data;
    }

    private void ShowScheme()
    {
        if (_tree == null) { return; }

        TextFormatData data = CurrentData();
        _updating = true;

        int font = AvailableFonts.ToList().IndexOf(data.FontFamily ?? AvailableFonts[0]);
        _font.SelectedIndex = font < 0 ? 0 : font;
        _fontSize.SetValueQuietly((int)Math.Round(data.FontSize));

        foreach (var name in TextFormatData.BaseColorNames)
        {
            _baseColors[name].Color = data.BaseColor(name);
        }

        _printScheme.IsChecked = string.Equals(
            _printSchemeKey, _scheme.CurrentSchemeKey, StringComparison.Ordinal);

        _updating = false;
        RefreshStyleList();
        ShowCurrentStyle();
    }

    private void FontChanged()
    {
        if (_updating) { return; }

        TextFormatData data = CurrentData();
        data.FontFamily = _font.SelectedIndex >= 0 && _font.SelectedIndex < AvailableFonts.Count
            ? AvailableFonts[_font.SelectedIndex]
            : AvailableFonts[0];
        data.FontSize = _fontSize.Value;
        MarkChanged();
    }

    /// <summary>Rebuilds the style list, with each row drawn as it looks.</summary>
    private void RefreshStyleList()
    {
        TextFormatData data = CurrentData();
        int selected = _tree.SelectedIndex;

        _entries.Clear();
        _entries.Add(StyleEntry.Heading(I18n.Get("Base Colors"), null, null));

        _entries.Add(StyleEntry.Heading(I18n.Get("Default Styles"), null, null));
        foreach (var name in TextFormatData.DefaultStyleNames)
        {
            _entries.Add(StyleEntry.ForStyle(
                DefaultStyleName(name), null, name, data.DefaultStyle(name), data, null));
        }

        foreach (var group in Colorize.DefaultMapping())
        {
            _entries.Add(StyleEntry.Heading(GroupName(group.Mode), group.Mode, null));
            foreach (var style in group.Styles)
            {
                _entries.Add(StyleEntry.ForStyle(
                    StyleName(group.Mode, style.Name),
                    group.Mode,
                    style.Name,
                    data.ModeStyle(group.Mode, style.Name),
                    data,
                    style.Base));
            }
        }

        _updating = true;
        _tree.ItemsSource = null;
        _tree.ItemsSource = _entries;
        _tree.SelectedIndex = selected >= 0 && selected < _entries.Count ? selected : 0;
        _updating = false;
    }

    private void ShowCurrentStyle()
    {
        if (_tree == null || _attributePanel == null) { return; }

        StyleEntry entry = _tree.SelectedIndex >= 0 && _tree.SelectedIndex < _entries.Count
            ? _entries[_tree.SelectedIndex]
            : null;

        if (entry == null || entry.IsHeading)
        {
            //The first row IS "Base Colors"; every other heading shows nothing,
            //which is upstream's empty page.
            bool baseColors = entry != null && _tree.SelectedIndex == 0;
            _baseColorPanel.Visibility = baseColors ? Visibility.Visible : Visibility.Collapsed;
            _attributePanel.Visibility = Visibility.Collapsed;
            return;
        }

        _baseColorPanel.Visibility = Visibility.Collapsed;
        _attributePanel.Visibility = Visibility.Visible;

        _updating = true;
        _attributeTitle.Text = entry.Mode == null
            ? entry.Label
            : GroupName(entry.Mode) + ": " + entry.Label;

        _attributeInherits.Text = entry.Inherits == null
            ? string.Empty
            : I18n.Format(
                I18n.Get("(Inherits: {name})"),
                ("name", DefaultStyleName(entry.Inherits)));
        _attributeInherits.Visibility = entry.Inherits == null
            ? Visibility.Collapsed
            : Visibility.Visible;

        TextFormat format = entry.Format;
        bool tristate = entry.Inherits != null;
        _bold.IsThreeState = tristate;
        _italic.IsThreeState = tristate;
        _underline.IsThreeState = tristate;

        _bold.IsChecked = Tri(format?.IsBold, tristate);
        _italic.IsChecked = Tri(format?.IsItalic, tristate);
        _underline.IsChecked = Tri(format?.IsUnderlined, tristate);

        _textColor.Color = format?.Foreground;
        _backgroundColor.Color = format?.Background;
        _underlineColor.Color = format?.UnderlineColor;
        _updating = false;
    }

    private static bool? Tri(bool? value, bool tristate)
        => value ?? (tristate ? (bool?)null : false);

    private void AttributeChanged()
    {
        if (_updating) { return; }

        StyleEntry entry = _tree.SelectedIndex >= 0 && _tree.SelectedIndex < _entries.Count
            ? _entries[_tree.SelectedIndex]
            : null;
        if (entry == null || entry.IsHeading || entry.Format == null) { return; }

        TextFormat format = entry.Format;
        format.IsBold = _bold.IsChecked;
        format.IsItalic = _italic.IsChecked;
        format.IsUnderlined = _underline.IsChecked;
        format.Foreground = _textColor.Color;
        format.Background = _backgroundColor.Color;
        format.UnderlineColor = _underlineColor.Color;

        RefreshStyleList();
        MarkChanged();
    }

    // ------------------------------------------------------------- style names

    /// <summary>What a base colour is called.</summary>
    /// <param name="name">Its key.</param>
    /// <returns>The name.</returns>
    private static string BaseColorName(string name)
        => name switch
        {
            //L10N: color of Text
            "text" => I18n.Get("Text"),
            //L10N: color of Background
            "background" => I18n.Get("Background"),
            //L10N: color of Selected Text
            "selectiontext" => I18n.Get("Selected Text"),
            //L10N: color of Selection Background
            "selectionbackground" => I18n.Get("Selection Background"),
            //L10N: color of Current Line
            "current" => I18n.Get("Current Line"),
            //L10N: color of Marked Line (bookmark)
            "mark" => I18n.Get("Marked Line"),
            //L10N: color of line with Error
            "error" => I18n.Get("Error Line"),
            //L10N: color of highlighted search result
            "search" => I18n.Get("Search Result"),
            //L10N: color of characters that match (e.g. braces, parentheses)
            "match" => I18n.Get("Matching Character"),
            //L10N: color of paper in music preview
            "paper" => I18n.Get("Preview Background"),
            //L10N: color of objects highlighting in preview
            "musichighlight" => I18n.Get("Preview Highlight"),
            _ => name,
        };

    /// <summary>What a default style is called.</summary>
    /// <param name="name">Its key.</param>
    /// <returns>The name.</returns>
    private static string DefaultStyleName(string name)
        => name switch
        {
            //L10N: a basic type of input in the editor
            "keyword" => I18n.Get("Keyword"),
            "function" => I18n.Get("Function"),
            "variable" => I18n.Get("Variable"),
            "value" => I18n.Get("Value"),
            "string" => I18n.Get("String"),
            "escape" => I18n.Get("Escape"),
            "comment" => I18n.Get("Comment"),
            "error" => I18n.Get("Error"),
            _ => name,
        };

    /// <summary>What a group of styles is called.</summary>
    /// <param name="mode">The mode name.</param>
    /// <returns>The name.</returns>
    /// <remarks>⚠ FR13 EXEMPT, ruled at W13's close-out sweep. The
    /// <c>lilypond</c> group names the LANGUAGE the styles colour, not the
    /// engine the user drives — a heading over Pitch, Octave and Duration can
    /// only mean the input language, and the file being edited really is a
    /// LilyPond source file (the user guide's own front page says so, which
    /// FR13 permits). The ruling governs the ENGINE's name, and that is
    /// LilyPort everywhere it appears. The same reasoning covers "LilyPond
    /// Tag" in the HTML styles and "LilyPond Environment" in the Scheme
    /// styles, both of which name embedded LilyPond CODE.</remarks>
    private static string GroupName(string mode)
        => mode switch
        {
            "lilypond" => I18n.Get("LilyPond"),
            "html" => I18n.Get("HTML"),
            "scheme" => I18n.Get("Scheme"),
            "texinfo" => I18n.Get("Texinfo"),
            _ => mode,
        };

    /// <summary>What one mode's style is called.</summary>
    /// <param name="mode">The mode.</param>
    /// <param name="name">The style.</param>
    /// <returns>The name.</returns>
    private static string StyleName(string mode, string name)
    {
        if (string.Equals(mode, "lilypond", StringComparison.Ordinal))
        {
            return name switch
            {
                "pitch" => I18n.Get("Pitch"),
                "octave" => I18n.Get("Octave"),
                "duration" => I18n.Get("Duration"),
                "accidental" => I18n.Get("Accidental"),
                "octavecheck" => I18n.Get("Octave Check"),
                "fingering" => I18n.Get("Fingering"),
                //L10N: For String instruments like Guitar
                "stringnumber" => I18n.Get("String Number"),
                "slur" => I18n.Get("Slur"),
                "dynamic" => I18n.Get("Dynamic"),
                "articulation" => I18n.Get("Articulation"),
                "chord" => I18n.Get("Chord"),
                "beam" => I18n.Get("Beam"),
                "check" => I18n.Get("Check"),
                "repeat" => I18n.Get("Repeat"),
                "keyword" => I18n.Get("Keyword"),
                "command" => I18n.Get("Command"),
                "specifier" => I18n.Get("Specifier"),
                "usercommand" => I18n.Get("User Command"),
                "markup" => I18n.Get("Markup"),
                "lyricmode" => I18n.Get("Lyric Mode"),
                "lyrictext" => I18n.Get("Lyric Text"),
                "delimiter" => I18n.Get("Delimiter"),
                "figbass" => I18n.Get("Figured Bass"),
                "figbstep" => I18n.Get("Figured Bass Step"),
                "figbmodif" => I18n.Get("Figured Bass Modifier"),
                "context" => I18n.Get("Context"),
                "grob" => I18n.Get("Layout Object"),
                "property" => I18n.Get("Property"),
                "variable" => I18n.Get("Variable"),
                "uservariable" => I18n.Get("User Variable"),
                "value" => I18n.Get("Value"),
                "string" => I18n.Get("String"),
                "stringescape" => I18n.Get("Escaped Character"),
                "comment" => I18n.Get("Comment"),
                "error" => I18n.Get("Error"),
                _ => name,
            };
        }

        if (string.Equals(mode, "html", StringComparison.Ordinal))
        {
            return name switch
            {
                "tag" => I18n.Get("Tag"),
                //FR13 EXEMPT (see GroupName): the tag names the LANGUAGE
                //embedded in the HTML, which is what the user typed.
                "lilypondtag" => I18n.Get("LilyPond Tag"),
                "attribute" => I18n.Get("Attribute"),
                "value" => I18n.Get("Value"),
                "entityref" => I18n.Get("Entity Reference"),
                "comment" => I18n.Get("Comment"),
                "string" => I18n.Get("String"),
                _ => name,
            };
        }

        if (string.Equals(mode, "scheme", StringComparison.Ordinal))
        {
            return name switch
            {
                "scheme" => I18n.Get("Scheme"),
                "number" => I18n.Get("Number"),
                //FR13 EXEMPT (see GroupName): the style colours a stretch of
                //LilyPond CODE embedded in Scheme, not anything about the
                //engine.
                "lilypond" => I18n.Get("LilyPond Environment"),
                "string" => I18n.Get("String"),
                "comment" => I18n.Get("Comment"),
                "keyword" => I18n.Get("Keyword"),
                "function" => I18n.Get("Function"),
                "variable" => I18n.Get("Variable"),
                "constant" => I18n.Get("Constant"),
                "symbol" => I18n.Get("Symbol"),
                "delimiter" => I18n.Get("Delimiter"),
                _ => name,
            };
        }

        if (string.Equals(mode, "texinfo", StringComparison.Ordinal))
        {
            return name switch
            {
                "keyword" => I18n.Get("Keyword"),
                "block" => I18n.Get("Block"),
                "escapechar" => I18n.Get("Escaped Character"),
                "attribute" => I18n.Get("Attribute"),
                "verbatim" => I18n.Get("Verbatim"),
                "comment" => I18n.Get("Comment"),
                _ => name,
            };
        }

        return name;
    }

    /// <summary>One row of the style list.</summary>
    /// <remarks>The row carries its own look, which is what makes the list a
    /// live preview of the scheme as upstream's tree is.</remarks>
    [Microsoft.UI.Xaml.Data.Bindable]
    public sealed class StyleEntry
    {
        /// <summary>Gets the row's text.</summary>
        public string Label { get; private set; }

        /// <summary>Gets the mode the style belongs to, or null.</summary>
        public string Mode { get; private set; }

        /// <summary>Gets the style's own name, or null for a heading.</summary>
        public string Name { get; private set; }

        /// <summary>Gets the default style this one inherits from, or null.</summary>
        public string Inherits { get; private set; }

        /// <summary>Gets the format the row edits, or null for a heading.</summary>
        public TextFormat Format { get; private set; }

        /// <summary>Gets whether the row is a heading.</summary>
        public bool IsHeading => Format == null && Name == null;

        /// <summary>Gets the colour the row is drawn in.</summary>
        public Brush Foreground { get; private set; }

        /// <summary>Gets the weight the row is drawn in.</summary>
        public Windows.UI.Text.FontWeight Weight { get; private set; } = FontWeights.Normal;

        /// <summary>Gets the slant the row is drawn in.</summary>
        public Windows.UI.Text.FontStyle Style { get; private set; }

        /// <summary>Gets the row's left margin, which shows its depth.</summary>
        public Thickness Indent { get; private set; }

        /// <summary>Makes a heading row.</summary>
        /// <param name="label">Its text.</param>
        /// <param name="mode">The mode it heads, or null.</param>
        /// <param name="name">Always null.</param>
        /// <returns>The row.</returns>
        public static StyleEntry Heading(string label, string mode, string name)
            => new StyleEntry
            {
                Label = label,
                Mode = mode,
                Name = name,
                Weight = FontWeights.SemiBold,
                Indent = new Thickness(0),
            };

        /// <summary>Makes a style row, drawn the way the style looks.</summary>
        /// <param name="label">Its text.</param>
        /// <param name="mode">The mode, or null for a default style.</param>
        /// <param name="name">The style's key.</param>
        /// <param name="format">The format the row edits.</param>
        /// <param name="data">The scheme, for the inherited look.</param>
        /// <param name="inherits">The default style it inherits, or null.</param>
        /// <returns>The row.</returns>
        public static StyleEntry ForStyle(
            string label,
            string mode,
            string name,
            TextFormat format,
            TextFormatData data,
            string inherits)
        {
            //What the row LOOKS like is the inherited format with this style's
            //own attributes over it — the same merge the editor draws with.
            TextFormat shown = inherits != null && data.DefaultStyle(inherits) != null
                ? data.DefaultStyle(inherits).Clone()
                : new TextFormat();
            shown.Merge(format);

            return new StyleEntry
            {
                Label = label,
                Mode = mode,
                Name = name,
                Inherits = inherits,
                Format = format,
                Indent = new Thickness(16, 0, 0, 0),
                Foreground = new SolidColorBrush(
                    shown.Foreground ?? data.BaseColor("text")),
                Weight = shown.IsBold == true ? FontWeights.Bold : FontWeights.Normal,
                Style = shown.IsItalic == true
                    ? Windows.UI.Text.FontStyle.Italic
                    : Windows.UI.Text.FontStyle.Normal,
            };
        }
    }
}
