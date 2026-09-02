// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.ScoreWizard;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Preferences; //was previously: frescobaldi/preferences/editor.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Editor page: how the text view behaves, how long a match stays lit, how
/// indenting works, what the keyboard does, what exported source looks like,
/// and which quotation marks are typed.
/// </summary>
public sealed class EditorPage : PreferencesPage
{
    private readonly List<string> _quoteLanguages = new List<string> { "current", "custom" };

    private CheckBox _wrapLines;
    private NumberEntry _contextLines;
    private NumberEntry _matchSeconds;
    private NumberEntry _tabWidth;
    private NumberEntry _indentSpaces;
    private NumberEntry _documentSpaces;
    private CheckBox _smartHome;
    private CheckBox _smartStartEnd;
    private CheckBox _keepCursorInLine;
    private CheckBox _numberLines;
    private CheckBox _inlineCopy;
    private CheckBox _inlineExport;
    private CheckBox _copyAsPlainText;
    private CheckBox _copyBodyOnly;
    private ComboBox _wrapTag;
    private ComboBox _wrapAttribute;
    private TextBox _wrapAttributeName;
    private ComboBox _quotesLanguage;
    private TextBox _primaryLeft;
    private TextBox _primaryRight;
    private TextBox _secondaryLeft;
    private TextBox _secondaryRight;

    /// <summary>Creates the page.</summary>
    /// <param name="context">What the page configures.</param>
    public EditorPage(PreferencesContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Editor");

    /// <inheritdoc/>
    public override string Help => "prefs_editor";

    /// <inheritdoc/>
    public override string IconName => "document-properties";

    /// <summary>Gets the values the page reads and writes.</summary>
    public EditorValues Values { get; } = new EditorValues();

    /// <inheritdoc/>
    public override void LoadSettings()
    {
        Values.Load(Settings);

        _wrapLines.IsChecked = Values.WrapLines;
        _contextLines.SetValueQuietly(Values.ContextLines);
        _matchSeconds.SetValueQuietly(Values.MatchHighlightSeconds);

        _tabWidth.SetValueQuietly(Values.TabWidth);
        _indentSpaces.SetValueQuietly(Values.IndentSpaces);
        _documentSpaces.SetValueQuietly(Values.DocumentSpaces);

        _smartHome.IsChecked = Values.SmartHome;
        _smartStartEnd.IsChecked = Values.SmartStartEnd;
        _keepCursorInLine.IsChecked = Values.KeepCursorInLine;

        _numberLines.IsChecked = Values.NumberLines;
        _inlineCopy.IsChecked = Values.InlineStyleCopy;
        _inlineExport.IsChecked = Values.InlineStyleExport;
        _copyAsPlainText.IsChecked = Values.CopyHtmlAsPlainText;
        _copyBodyOnly.IsChecked = Values.CopyDocumentBodyOnly;
        _wrapTag.SelectedIndex = Math.Max(0, IndexOfItem(_wrapTag, Values.WrapTag));
        _wrapAttribute.SelectedIndex =
            Math.Max(0, IndexOfItem(_wrapAttribute, Values.WrapAttribute));
        _wrapAttributeName.Text = Values.WrapAttributeName;

        int language = _quoteLanguages.IndexOf(Values.QuotesLanguage ?? "current");
        _quotesLanguage.SelectedIndex = language < 0 ? 0 : language;
        _primaryLeft.Text = Values.PrimaryLeft;
        _primaryRight.Text = Values.PrimaryRight;
        _secondaryLeft.Text = Values.SecondaryLeft;
        _secondaryRight.Text = Values.SecondaryRight;
        UpdateQuoteEntries();
    }

    /// <inheritdoc/>
    public override void SaveSettings()
    {
        Values.WrapLines = _wrapLines.IsChecked == true;
        Values.ContextLines = _contextLines.Value;
        Values.MatchHighlightSeconds = _matchSeconds.Value;

        Values.TabWidth = _tabWidth.Value;
        Values.IndentSpaces = _indentSpaces.Value;
        Values.DocumentSpaces = _documentSpaces.Value;

        Values.SmartHome = _smartHome.IsChecked == true;
        Values.SmartStartEnd = _smartStartEnd.IsChecked == true;
        Values.KeepCursorInLine = _keepCursorInLine.IsChecked == true;

        Values.NumberLines = _numberLines.IsChecked == true;
        Values.InlineStyleCopy = _inlineCopy.IsChecked == true;
        Values.InlineStyleExport = _inlineExport.IsChecked == true;
        Values.CopyHtmlAsPlainText = _copyAsPlainText.IsChecked == true;
        Values.CopyDocumentBodyOnly = _copyBodyOnly.IsChecked == true;
        Values.WrapTag = ItemText(_wrapTag) ?? "pre";
        Values.WrapAttribute = ItemText(_wrapAttribute) ?? "id";
        Values.WrapAttributeName = _wrapAttributeName.Text;

        Values.QuotesLanguage = _quotesLanguage.SelectedIndex >= 0
            && _quotesLanguage.SelectedIndex < _quoteLanguages.Count
                ? _quoteLanguages[_quotesLanguage.SelectedIndex]
                : "current";
        Values.PrimaryLeft = _primaryLeft.Text;
        Values.PrimaryRight = _primaryRight.Text;
        Values.SecondaryLeft = _secondaryLeft.Text;
        Values.SecondaryRight = _secondaryRight.Text;

        Values.Save(Settings);
    }

    /// <inheritdoc/>
    protected override UIElement Build()
        => Stack(
            Group(I18n.Get("View Preferences"), BuildView()),
            Group(I18n.Get("Highlighting Options"), BuildHighlighting()),
            Group(I18n.Get("Indenting Preferences"), BuildIndenting()),
            Group(I18n.Get("Keyboard Preferences"), BuildKeyboard()),
            Group(I18n.Get("Source Export Preferences"), BuildSourceExport()),
            Group(I18n.Get("Typographical Quotes"), BuildQuotes()));

    private static int IndexOfItem(ComboBox list, string text)
    {
        for (int index = 0; index < list.Items.Count; index++)
        {
            if (list.Items[index] is ComboBoxItem item
                && string.Equals(item.Content as string, text, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string ItemText(ComboBox list)
        => list.SelectedIndex >= 0 && list.SelectedIndex < list.Items.Count
            && list.Items[list.SelectedIndex] is ComboBoxItem item
                ? item.Content as string
                : null;

    private UIElement BuildView()
    {
        _wrapLines = Tick(
            I18n.Get("Wrap long lines by default"),
            I18n.Get(
                "If enabled, lines that don't fit in the editor width are wrapped "
                + "by default. "
                + "Note: when the document is displayed by multiple views, they all "
                + "share the same line wrapping width, which might look strange."));

        _contextLines = Number(0, 20);
        UIElement row = Labelled(I18n.Get("Number of surrounding lines:"), _contextLines);
        ToolTipService.SetToolTip(_contextLines, I18n.Get(
            "The number of surrounding lines to show when clicking "
            + "on an object of the music view or jumping to a search result. "
            + "The text view will scroll and unfold blocks as needed, "
            + "but always as little as possible. "
            + "Set it to 0 to disable unfolding."));

        return Rows(_wrapLines, row);
    }

    private UIElement BuildHighlighting()
    {
        //Upstream's spin box says "{num} sec" around the number and calls zero
        //"Infinite"; both are kept.
        string suffix = SecondsSuffix();
        _matchSeconds = Number(0, 60, I18n.Get("Infinite"), suffix);
        return Rows(
            Note(I18n.Get(
                "Below you can define how long "
                + "\"matching\" items like matching brackets or the items "
                + "linked through Point-and-Click are highlighted.")),
            Labelled(I18n.Get("Matching Item:"), _matchSeconds));
    }

    /// <summary>
    /// The word after the number of seconds, taken out of upstream's own
    /// <c>{num} sec</c> message so a translation puts it where its language
    /// wants it.
    /// </summary>
    /// <returns>The suffix, including its leading space when it has one.</returns>
    private static string SecondsSuffix()
    {
        //L10N: abbreviation for "n seconds" in spinbox, n >= 1, no plural forms
        string pattern = I18n.Get("{num} sec");
        int placeholder = pattern.IndexOf("{num}", StringComparison.Ordinal);
        return placeholder < 0
            ? " sec"
            : pattern.Substring(placeholder + "{num}".Length);
    }

    private UIElement BuildIndenting()
    {
        _tabWidth = Number(1, 99);
        ToolTipService.SetToolTip(_tabWidth, I18n.Get(
            "The visible width of a Tab character in the editor."));

        //⚠ Zero MEANS a tab character, and upstream's special value text is
        //what says so; the encoding is shared with the settings file.
        string suffix = SpacesSuffix();
        _indentSpaces = Number(0, 99, I18n.Get("Tab"), suffix);
        ToolTipService.SetToolTip(_indentSpaces, I18n.Get(
            "How many spaces to use for indenting one level.\n"
            + "Move to zero to use a Tab character for indenting."));

        _documentSpaces = Number(0, 99, I18n.Get("Tab"), suffix);
        ToolTipService.SetToolTip(_documentSpaces, I18n.Get(
            "How many spaces to insert when Tab is pressed outside the indent, "
            + "elsewhere in the document.\n"
            + "Move to zero to insert a literal Tab character in this case."));

        return Rows(
            Labelled(I18n.Get("Visible Tab Width:"), _tabWidth),
            Labelled(I18n.Get("Indent text with:"), _indentSpaces),
            Labelled(I18n.Get("Tab outside indent inserts:"), _documentSpaces));
    }

    /// <summary>The word after a number of spaces, from upstream's own message.</summary>
    /// <returns>The suffix.</returns>
    private static string SpacesSuffix()
    {
        //L10N: abbreviation for "n spaces" in spinbox, n >= 1, no plural forms
        string pattern = I18n.Get("{num} spaces");
        int placeholder = pattern.IndexOf("{num}", StringComparison.Ordinal);
        return placeholder < 0
            ? " spaces"
            : pattern.Substring(placeholder + "{num}".Length);
    }

    private UIElement BuildKeyboard()
    {
        _smartHome = Tick(
            I18n.Get("Smart Home key"),
            I18n.Get(
                "If enabled, pressing Home will put the cursor at the first non-"
                + "whitespace character on the line. "
                + "When the cursor is on that spot, pressing Home moves the cursor "
                + "to the beginning of the line."));
        _smartStartEnd = Tick(
            I18n.Get("Smart Up/PageUp and Down/PageDown keys"),
            I18n.Get(
                "If enabled, pressing Up or PageUp in the first line will move the "
                + "cursor to the beginning of the document, and pressing Down or "
                + "PageDown in the last line will move the cursor to the end of the "
                + "document."));
        _keepCursorInLine = Tick(
            I18n.Get("Horizontal arrow keys keep cursor in current line"),
            I18n.Get(
                "If enabled, the cursor will stay in the current line when using "
                + "the horizontal arrow keys, and not wrap around to the next or "
                + "previous line."));

        return Rows(_smartHome, _smartStartEnd, _keepCursorInLine);
    }

    private UIElement BuildSourceExport()
    {
        _copyAsPlainText = Tick(
            I18n.Get("Copy HTML as plain text"),
            I18n.Get(
                "If enabled, HTML is copied to the clipboard as plain text. "
                + "Use this when you want to type HTML formatted code in a "
                + "plain text editing environment."));
        _copyBodyOnly = Tick(
            I18n.Get("Copy document body only"),
            I18n.Get(
                "If enabled, only the HTML contents, wrapped in a single tag, will be "
                + "copied to the clipboard instead of a full HTML document with a "
                + "header section. "
                + "May be used in conjunction with the plain text option, with the "
                + "inline style option turned off, to copy highlighted code in a "
                + "text editor when an external style sheet is already available."));
        _inlineCopy = Tick(
            I18n.Get("Use inline style when copying colored HTML"),
            I18n.Get(
                "If enabled, inline style attributes are used when copying "
                + "colored HTML to the clipboard. "
                + "Otherwise, a CSS stylesheet is embedded."));
        _inlineExport = Tick(
            I18n.Get("Use inline style when exporting colored HTML"),
            I18n.Get(
                "If enabled, inline style attributes are used when exporting "
                + "colored HTML to a file. "
                + "Otherwise, a CSS stylesheet is embedded."));
        _numberLines = Tick(
            I18n.Get("Show line numbers"),
            //was previously: "…in exported HTML or printed source". There is no
            //printing (FR5.5), so the message names what is left.
            I18n.Get("If enabled, line numbers are shown in exported HTML."));

        //The element and attribute names are markup, not messages: they are the
        //same words in every language.
        _wrapTag = Choice("pre", "code", "div");
        ToolTipService.SetToolTip(_wrapTag, I18n.Get(
            "Choose what tag the colored HTML will be wrapped into."));
        _wrapAttribute = Choice("id", "class");
        ToolTipService.SetToolTip(_wrapAttribute, I18n.Get(
            "Choose whether the wrapper tag should be of type 'id' or 'class'"));
        _wrapAttributeName = Entry();
        ToolTipService.SetToolTip(_wrapAttributeName, I18n.Get(
            "Arbitrary name for the type attribute. "
            + "This must match the CSS stylesheet if using external CSS."));

        return Rows(
            _copyAsPlainText,
            _copyBodyOnly,
            _inlineCopy,
            _inlineExport,
            _numberLines,
            Labelled(I18n.Get("Tag to wrap around source:" + "  "), _wrapTag),
            Labelled(I18n.Get("Attribute type of wrapper:" + "  "), _wrapAttribute),
            Labelled(I18n.Get("Name of attribute:" + "  "), _wrapAttributeName));
    }

    private UIElement BuildQuotes()
    {
        _quoteLanguages.Clear();
        _quoteLanguages.Add("current");
        _quoteLanguages.Add("custom");
        foreach (var language in LanguageQuotes.Available().Where(l => l != "C"))
        {
            _quoteLanguages.Add(language);
        }

        _quotesLanguage = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var language in _quoteLanguages)
        {
            _quotesLanguage.Items.Add(new ComboBoxItem { Content = QuoteLabel(language) });
        }

        _quotesLanguage.SelectionChanged += (_, _) =>
        {
            UpdateQuoteEntries();
            MarkChanged();
        };

        _primaryLeft = Entry(70);
        _primaryRight = Entry(70);
        _secondaryLeft = Entry(70);
        _secondaryRight = Entry(70);

        return Rows(
            Labelled(I18n.Get("Quotes to use:"), _quotesLanguage),
            Labelled(I18n.Get("Primary (double) quotes:"), Pair(_primaryLeft, _primaryRight)),
            Labelled(
                I18n.Get("Secondary (single) quotes:"),
                Pair(_secondaryLeft, _secondaryRight)));
    }

    private static UIElement Pair(TextBox left, TextBox right)
    {
        StackPanel row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        row.Children.Add(left);
        row.Children.Add(right);
        return row;
    }

    /// <summary>
    /// What one entry of the quotes list reads: the language, then the four
    /// marks it uses.
    /// </summary>
    /// <param name="language">The list entry's language code, or one of
    /// <c>current</c> and <c>custom</c>.</param>
    /// <returns>The label.</returns>
    private static string QuoteLabel(string language)
    {
        if (string.Equals(language, "custom", StringComparison.Ordinal))
        {
            return I18n.Get("Custom quotes (enter below)");
        }

        QuoteSet quotes = string.Equals(language, "current", StringComparison.Ordinal)
            ? LanguageQuotes.For(I18n.Language) ?? LanguageQuotes.Default()
            : LanguageQuotes.For(language) ?? LanguageQuotes.Default();

        //was previously: the raw code ("de", "fr"), standing in until W-I18N
        //brought the name table. It did (Services/LanguageNames.g.cs), so this
        //is upstream's own `language_names.languageName(lang, curlang)' —
        //each language named in the INTERFACE's language, as upstream names it.
        string name = string.Equals(language, "current", StringComparison.Ordinal)
            ? I18n.Get("Current language")
            : Services.LanguageNames.LanguageName(language, I18n.Language);

        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}   {1} {2}    {3} {4}",
            name,
            quotes.Primary.Left,
            quotes.Primary.Right,
            quotes.Secondary.Left,
            quotes.Secondary.Right);
    }

    private void UpdateQuoteEntries()
    {
        bool custom = _quotesLanguage.SelectedIndex == 1;
        _primaryLeft.IsEnabled = custom;
        _primaryRight.IsEnabled = custom;
        _secondaryLeft.IsEnabled = custom;
        _secondaryRight.IsEnabled = custom;
    }
}
