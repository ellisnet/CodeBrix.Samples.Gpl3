// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Editor;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/charmap/ and widgets/charmap.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Special Characters panel: a Unicode block picked from a list, its
/// characters laid out in a grid, and a click puts one in the document.
/// </summary>
/// <remarks>
/// The grid draws the characters in the EDITOR's font, so what the user sees
/// in the panel is what the document will show — including the tofu that means
/// this font has no glyph for it, which is the desired failure (standing rule
/// 6).
/// </remarks>
public sealed class CharacterMapPanel : Panel
{
    /// <summary>The setting the last-picked block is remembered in.</summary>
    public const string LastBlockKey = "charmaptool/last_block";

    private const int Columns = 8;

    private readonly SettingsStore _settings;
    private readonly FontFamily _displayFont;
    private ComboBox _blockPicker;
    private GridView _grid;

    /// <summary>Creates the panel.</summary>
    /// <param name="settings">The settings store, or null.</param>
    /// <param name="displayFont">The font the characters are drawn in.</param>
    public CharacterMapPanel(SettingsStore settings = null, FontFamily displayFont = null)
        : base("charmap", DockArea.Right)
    {
        _settings = settings;
        _displayFont = displayFont;
        ToggleAction.WithShortcut("Meta+Alt+U");
    }

    /// <summary>Gets or sets what to do with a character the user picks.</summary>
    public Action<string> InsertText { get; set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Special Characters");

    /// <inheritdoc/>
    public override void TranslateUI()
        => ToggleAction.Text = I18n.Get("Special C&haracters");

    /// <summary>Shows the characters of one block.</summary>
    /// <param name="block">The block.</param>
    public void ShowBlock(UnicodeBlock block)
    {
        if (_grid == null) { return; }

        _grid.ItemsSource = Cells(block).ToList();
        _settings?.SetString(LastBlockKey, block.Name);
    }

    /// <summary>Gets the cells of a block, one per assignable character.</summary>
    /// <param name="block">The block.</param>
    /// <returns>The cells.</returns>
    /// <remarks>Unassigned and non-printing code points are left out: a grid
    /// of blanks would offer the user nothing to pick.</remarks>
    public static IEnumerable<CharacterCell> Cells(UnicodeBlock block)
    {
        for (int code = block.Start; code <= block.End; code++)
        {
            //Surrogates are not characters and must never reach the document.
            if (code >= 0xD800 && code <= 0xDFFF) { continue; }

            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(
                char.ConvertFromUtf32(code), 0);
            if (category is UnicodeCategory.Control
                or UnicodeCategory.OtherNotAssigned
                or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator)
            {
                continue;
            }

            yield return new CharacterCell(code);
        }
    }

    /// <inheritdoc/>
    protected override UIElement CreateWidget()
    {
        FillGrid root = new FillGrid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });

        IReadOnlyList<UnicodeBlock> blocks = UnicodeBlocks.UsableBlocks();
        _blockPicker = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(4, 4, 4, 4),
        };
        foreach (var block in blocks)
        {
            _blockPicker.Items.Add(block.Name);
        }

        _grid = new GridView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            Padding = new Thickness(4),
            ItemTemplate = CellTemplate(),
        };
        _grid.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is CharacterCell cell)
            {
                InsertText?.Invoke(cell.Text);
            }
        };

        Grid.SetRow(_blockPicker, 0);
        Grid.SetRow(_grid, 1);
        root.Children.Add(_blockPicker);
        root.Children.Add(_grid);

        _blockPicker.SelectionChanged += (_, _) =>
        {
            int index = _blockPicker.SelectedIndex;
            if (index >= 0 && index < blocks.Count) { ShowBlock(blocks[index]); }
        };

        string remembered = _settings?.GetString(LastBlockKey, string.Empty);
        int start = string.IsNullOrEmpty(remembered)
            ? 0
            : Math.Max(0, blocks.ToList().FindIndex(
                b => string.Equals(b.Name, remembered, StringComparison.Ordinal)));
        _blockPicker.SelectedIndex = start;
        return root;
    }

    private DataTemplate CellTemplate()
    {
        //The template is built from markup rather than in code because a
        //GridView item needs one; the font is folded in so the cells and the
        //editor agree about what a character looks like.
        string family = _displayFont?.Source ?? "Roboto Mono";
        string xaml =
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
            + "<Border Width=\"34\" Height=\"34\" Margin=\"1\" "
            + "BorderThickness=\"1\" BorderBrush=\"#40808080\">"
            + "<TextBlock Text=\"{Binding Text}\" FontFamily=\"" + family + "\" "
            + "FontSize=\"18\" HorizontalAlignment=\"Center\" "
            + "VerticalAlignment=\"Center\" /></Border></DataTemplate>";
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }
}

/// <summary>One character in the map.</summary>
public sealed class CharacterCell
{
    /// <summary>Creates a cell.</summary>
    /// <param name="codePoint">The code point.</param>
    public CharacterCell(int codePoint)
    {
        CodePoint = codePoint;
        Text = char.ConvertFromUtf32(codePoint);
    }

    /// <summary>Gets the code point.</summary>
    public int CodePoint { get; }

    /// <summary>Gets the character as text.</summary>
    public string Text { get; }

    /// <summary>Gets the tooltip: the code point and its category.</summary>
    public string ToolTip => string.Format(
        CultureInfo.InvariantCulture,
        "U+{0:X4} {1}",
        CodePoint,
        UnicodeBlocks.BlockOf(CodePoint)?.Name ?? string.Empty);
}
