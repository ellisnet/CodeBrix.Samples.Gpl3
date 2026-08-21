// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Windows.System;
using Windows.UI;

namespace Fresco.Brix.Search; //was previously: frescobaldi/search/__init__.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The strip that appears under the editor for Find and for Find and Replace.
/// <para>
/// It shows every match at once — highlighted in the text and counted in the
/// bar — rather than stepping through them one at a time, which is what makes
/// Replace All able to work inside a selection and what makes the count
/// meaningful.
/// </para>
/// </summary>
/// <remarks>
/// The editor add-in brings a search panel of its own (AvalonEdit's). It is
/// deliberately not used: parity here means Frescobaldi's bar — its Case and
/// Regex switches, its match count, its search-inside-the-selection rule and
/// its two-row replace mode — and those are behaviours, not decoration.
/// </remarks>
public sealed class SearchBar : Grid
{
    private readonly TextBox _searchEntry;
    private readonly TextBox _replaceEntry;
    private readonly TextBlock _searchLabel;
    private readonly TextBlock _replaceLabel;
    private readonly TextBlock _countLabel;
    private readonly CheckBox _caseCheck;
    private readonly CheckBox _regexCheck;
    private readonly Button _previousButton;
    private readonly Button _nextButton;
    private readonly Button _closeButton;
    private readonly Button _replaceButton;
    private readonly Button _replaceAllButton;
    private readonly List<UIElement> _replaceRow = new List<UIElement>();

    private EditorView _view;
    private IReadOnlyList<SearchMatch> _positions = Array.Empty<SearchMatch>();
    private bool _positionsDirty = true;
    private bool _replaceMode;
    private bool _going;

    /// <summary>Creates the bar.</summary>
    /// <param name="editorFontFamily">The monospace font the entries use, or
    /// null for the inherited one.</param>
    public SearchBar(FontFamily editorFontFamily = null)
    {
        ColumnSpacing = 4;
        RowSpacing = 0;
        Padding = new Thickness(4, 2, 4, 2);
        Background = new SolidColorBrush(Color.FromArgb(0xff, 0xf0, 0xf0, 0xf0));

        for (int i = 0; i < 8; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = i == 1
                    ? new GridLength(1, GridUnitType.Star)
                    : GridLength.Auto,
            });
        }

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _searchLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _searchEntry = new TextBox { MinWidth = 120 };
        _previousButton = SmallButton("▲", () => FindPrevious());
        _nextButton = SmallButton("▼", () => FindNext());
        _caseCheck = new CheckBox { IsChecked = true, MinWidth = 0 };
        _regexCheck = new CheckBox { MinWidth = 0 };
        _countLabel = new TextBlock
        {
            MinWidth = 36,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _closeButton = SmallButton("✕", Hide);

        _replaceLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _replaceEntry = new TextBox { MinWidth = 120 };
        _replaceButton = new Button();
        _replaceAllButton = new Button();
        _replaceButton.Click += (_, _) => ReplaceOne();
        _replaceAllButton.Click += (_, _) => ReplaceAll();

        if (editorFontFamily != null)
        {
            _searchEntry.FontFamily = editorFontFamily;
            _replaceEntry.FontFamily = editorFontFamily;
        }

        Place(_searchLabel, 0, 0);
        Place(_searchEntry, 0, 1);
        Place(_previousButton, 0, 2);
        Place(_nextButton, 0, 3);
        Place(_caseCheck, 0, 4);
        Place(_regexCheck, 0, 5);
        Place(_countLabel, 0, 6);
        Place(_closeButton, 0, 7);

        Place(_replaceLabel, 1, 0);
        Place(_replaceEntry, 1, 1);
        Place(_replaceButton, 1, 4);
        Place(_replaceAllButton, 1, 5);
        _replaceRow.AddRange(new UIElement[]
        {
            _replaceLabel, _replaceEntry, _replaceButton, _replaceAllButton,
        });

        _searchEntry.TextChanged += (_, _) => SearchChanged();
        _caseCheck.Checked += (_, _) => SearchChanged();
        _caseCheck.Unchecked += (_, _) => SearchChanged();
        _regexCheck.Checked += (_, _) => SearchChanged();
        _regexCheck.Unchecked += (_, _) => SearchChanged();
        _searchEntry.KeyDown += EntryKeyDown;
        _replaceEntry.KeyDown += EntryKeyDown;
        KeyDown += EntryKeyDown;

        TranslateUI();
        ShowReplaceRow(false);
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Gets whether the bar is on screen.</summary>
    public bool IsShowing => Visibility == Visibility.Visible;

    /// <summary>Gets or sets what to do when the bar closes.</summary>
    public Action FocusEditor { get; set; }

    /// <summary>Sets every text for the language.</summary>
    public void TranslateUI()
    {
        _searchLabel.Text = I18n.Get("Search:");
        ToolTipService.SetToolTip(_previousButton, I18n.Get("Find Previous"));
        ToolTipService.SetToolTip(_nextButton, I18n.Get("Find Next"));
        _caseCheck.Content = MenuBuilder.Display(I18n.Get("&Case"));
        ToolTipService.SetToolTip(_caseCheck, I18n.Get("Case Sensitive"));
        _regexCheck.Content = MenuBuilder.Display(I18n.Get("&Regex"));
        ToolTipService.SetToolTip(_regexCheck, I18n.Get("Regular Expression"));
        ToolTipService.SetToolTip(_countLabel, I18n.Get("The total number of matches"));
        ToolTipService.SetToolTip(_closeButton, I18n.Get("Close"));
        _replaceLabel.Text = I18n.Get("Replace:");
        _replaceButton.Content = MenuBuilder.Display(I18n.Get("Re&place"));
        ToolTipService.SetToolTip(
            _replaceButton,
            I18n.Get("Replaces the next occurrence of the search term."));
        _replaceAllButton.Content = MenuBuilder.Display(I18n.Get("&All"));
        ToolTipService.SetToolTip(
            _replaceAllButton,
            I18n.Get("Replaces all occurrences of the search term in the "
                + "document or selection."));
    }

    /// <summary>Opens the bar for Find.</summary>
    /// <param name="view">The editor to search.</param>
    public void Find(EditorView view)
    {
        _replaceMode = false;
        ShowReplaceRow(false);
        Attach(view);

        string term = SearchLogic.TermForSelection(
            view.SelectedText, _regexCheck.IsChecked == true);
        if (view.HasSelection && term.Length > 0)
        {
            _searchEntry.Text = term;
        }
        else
        {
            _searchEntry.SelectAll();
        }

        MarkPositionsDirty();
        UpdatePositions();
        HighlightingOn();
        _searchEntry.Focus(FocusState.Programmatic);
    }

    /// <summary>Opens the bar for Find and Replace.</summary>
    /// <param name="view">The editor to search.</param>
    public void Replace(EditorView view)
    {
        bool wasShowing = IsShowing;
        _replaceMode = true;
        ShowReplaceRow(true);
        Attach(view);
        MarkPositionsDirty();
        UpdatePositions();
        HighlightingOn();

        //Upstream puts the caret in the REPLACE box when there is already
        //something to search for, and in the search box otherwise.
        TextBox focus = wasShowing && _searchEntry.Text.Length > 0
            ? _replaceEntry
            : _searchEntry;
        focus.Focus(FocusState.Programmatic);
    }

    /// <summary>Closes the bar and puts the caret back in the editor.</summary>
    public void Hide()
    {
        HighlightingOff();
        Visibility = Visibility.Collapsed;
        if (_view != null) { _view.BottomBar = null; }

        FocusEditor?.Invoke();
    }

    /// <summary>Goes to the next match.</summary>
    public void FindNext()
    {
        _going = true;
        try
        {
            UpdatePositions();
            if (_view == null || _positions.Count == 0) { return; }

            int index = SearchLogic.BisectRight(_positions, _view.Editor.CaretOffset);
            GoToPosition(index < _positions.Count ? index : 0);
        }
        finally
        {
            _going = false;
        }
    }

    /// <summary>Goes to the previous match.</summary>
    public void FindPrevious()
    {
        _going = true;
        try
        {
            UpdatePositions();
            if (_view == null || _positions.Count == 0) { return; }

            int index = SearchLogic.BisectLeft(
                _positions, _view.Editor.CaretOffset) - 1;
            GoToPosition(index < 0 ? _positions.Count - 1 : index);
        }
        finally
        {
            _going = false;
        }
    }

    /// <summary>Tells the bar that the document or the selection changed.</summary>
    public void Invalidate()
    {
        if (_going) { return; }

        MarkPositionsDirty();
        if (!IsShowing) { return; }

        UpdatePositions();
        HighlightingOn();
    }

    /// <summary>Replaces the match at or after the caret.</summary>
    public void ReplaceOne()
    {
        if (_view == null || _positions.Count == 0) { return; }

        int index = SearchLogic.BisectLeft(_positions, _view.Editor.CaretOffset);
        if (index >= _positions.Count) { index = 0; }

        if (DoReplace(_positions[index]))
        {
            MarkPositionsDirty();
            FindNext();
        }
    }

    /// <summary>Replaces every match, or every match inside the selection.</summary>
    public void ReplaceAll()
    {
        if (_view == null || _positions.Count == 0) { return; }

        IEnumerable<SearchMatch> targets = _positions;
        if (_view.HasSelection)
        {
            int start = _view.SelectionStart;
            int end = _view.SelectionEnd;
            targets = targets.Where(m => m.Start >= start && m.End <= end);
        }

        //One undo group, and LAST match first so the earlier offsets stay
        //valid while the later ones are rewritten.
        List<SearchMatch> ordered = targets.OrderByDescending(m => m.Start).ToList();
        if (ordered.Count == 0) { return; }

        TextDocument store = _view.Editor.Document;
        bool replaced = false;
        store.BeginUpdate();
        try
        {
            foreach (var match in ordered)
            {
                if (DoReplace(match, insideUpdate: true)) { replaced = true; }
            }
        }
        finally
        {
            store.EndUpdate();
        }

        if (!replaced) { return; }

        MarkPositionsDirty();
        UpdatePositions();
        HighlightingOn();
    }

    private void Attach(EditorView view)
    {
        if (!ReferenceEquals(_view, view))
        {
            HighlightingOff();
            _view = view;
        }

        if (_view == null) { return; }

        FocusEditor = _view.FocusEditor;
        _view.BottomBar = this;
        Visibility = Visibility.Visible;
        MarkPositionsDirty();
    }

    private bool DoReplace(SearchMatch match, bool insideUpdate = false)
    {
        TextDocument store = _view.Editor.Document;
        if (match.End > store.TextLength) { return false; }

        string current = store.GetText(match.Start, match.Length);
        string replacement = SearchLogic.ReplacementFor(
            current,
            _searchEntry.Text,
            _replaceEntry.Text,
            _caseCheck.IsChecked == true,
            _regexCheck.IsChecked == true);
        if (replacement == null) { return false; }

        if (!insideUpdate) { store.BeginUpdate(); }

        try
        {
            store.Replace(match.Start, match.Length, replacement);
        }
        finally
        {
            if (!insideUpdate) { store.EndUpdate(); }
        }

        return true;
    }

    private void SearchChanged()
    {
        _going = true;
        try
        {
            MarkPositionsDirty();
            UpdatePositions();
            HighlightingOn();
            if (_replaceMode || _positions.Count == 0 || _view == null) { return; }

            //Land on the match nearest to where the caret already is, and step
            //back one when the caret is already INSIDE a match — which happens
            //the moment the box is filled from the selection.
            int caret = _view.SelectionStart;
            int index = SearchLogic.BisectLeft(_positions, caret);
            if (index == _positions.Count)
            {
                index--;
            }
            else if (index > 0 && _positions[index - 1].End >= caret)
            {
                index--;
            }

            GoToPosition(index);
        }
        finally
        {
            _going = false;
        }
    }

    private void MarkPositionsDirty()
    {
        _positions = Array.Empty<SearchMatch>();
        _positionsDirty = true;
    }

    private void UpdatePositions()
    {
        if (_view == null || !_positionsDirty) { return; }

        string term = _searchEntry.Text;
        int start = 0;
        int end = -1;

        //In replace mode, or when the user made the selection themselves
        //rather than the bar moving the caret, the search stays inside it.
        if ((_replaceMode || !_going) && _view.HasSelection)
        {
            start = _view.SelectionStart;
            end = _view.SelectionEnd;
        }

        _positions = SearchLogic.Find(
            _view.Editor.Document.Text,
            term,
            _caseCheck.IsChecked == true,
            _regexCheck.IsChecked == true,
            start,
            end);

        _countLabel.Text = _positions.Count.ToString(CultureInfo.CurrentCulture);
        bool enabled = _positions.Count > 0;
        _replaceButton.IsEnabled = enabled;
        _replaceAllButton.IsEnabled = enabled;
        _previousButton.IsEnabled = enabled;
        _nextButton.IsEnabled = enabled;
        _positionsDirty = false;
    }

    private void GoToPosition(int index)
    {
        if (_view == null || index < 0 || index >= _positions.Count) { return; }

        SearchMatch match = _positions[index];
        _view.Select(match.Start, match.Length);
    }

    private void HighlightingOn()
        => _view?.Highlighter.Highlight(
            HighlightGroups.Search,
            _positions.Select(m => (m.Start, m.Length)),
            Color.FromArgb(0x70, 0xff, 0xd0, 0x40),
            HighlightGroups.PriorityOf(HighlightGroups.Search),
            fullWidth: false);

    private void HighlightingOff() => _view?.Highlighter.Clear(HighlightGroups.Search);

    private void ShowReplaceRow(bool show)
    {
        foreach (var element in _replaceRow)
        {
            element.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        RowDefinitions[1].Height = show ? GridLength.Auto : new GridLength(0);
    }

    private void EntryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                Hide();
                e.Handled = true;
                return;

            case VirtualKey.Enter:
                FindNext();
                e.Handled = true;
                return;

            case VirtualKey.Up when !_replaceMode && _searchEntry.Text.Length > 0:
                FindPrevious();
                e.Handled = true;
                return;

            case VirtualKey.Down when !_replaceMode && _searchEntry.Text.Length > 0:
                FindNext();
                e.Handled = true;
                return;
        }
    }

    private void Place(FrameworkElement element, int row, int column)
    {
        SetRow(element, row);
        SetColumn(element, column);
        Children.Add(element);
    }

    private static Button SmallButton(string glyph, Action action)
    {
        Button button = new Button
        {
            Content = glyph,
            Padding = new Thickness(6, 2, 6, 2),
            MinWidth = 0,
        };
        button.Click += (_, _) => action();
        return button;
    }
}
