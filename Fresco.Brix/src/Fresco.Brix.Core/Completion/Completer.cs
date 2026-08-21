// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.CodeCompletion;
using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using CodeBrix.Platform.UI.AdvancedTextEdit.Editing;
using Fresco.Brix.Commands;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using System;
using System.Linq;

namespace Fresco.Brix.Completion; //was previously: frescobaldi/autocomplete/__init__.py and completer.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Puts the completion popup in front of the user: automatically once two
/// characters of a word have been typed, or on demand with Ctrl+Space.
/// </summary>
/// <remarks>
/// Upstream wraps a <c>QCompleter</c> and reimplements its event filter to get
/// the popup's keyboard handling right. The editor add-in's
/// <c>CompletionWindow</c> already owns that half — it tracks the caret,
/// filters as the user types, closes when the caret leaves the range and
/// inserts on Enter — so what is ported here is the part that is Frescobaldi's
/// own: WHEN to show the popup, WHERE the completed text begins, and WHAT is
/// in it.
/// </remarks>
public sealed class Completer
{
    /// <summary>How many characters must be typed before the popup appears
    /// on its own.</summary>
    public const int AutoCompleteLength = 2;

    /// <summary>The settings key the automatic-completion switch lives in.</summary>
    public const string AutoCompleteKey = "autocomplete";

    private readonly CompletionAnalyzer _analyzer = new CompletionAnalyzer();
    private EditorView _view;
    private CompletionWindow _window;

    /// <summary>Gets or sets whether the popup appears as the user types.</summary>
    public bool AutoComplete { get; set; } = true;

    /// <summary>Gets the editor the completer is watching, or null.</summary>
    public EditorView View => _view;

    /// <summary>Points the completer at an editor.</summary>
    /// <param name="view">The editor, or null to detach.</param>
    public void SetView(EditorView view)
    {
        if (ReferenceEquals(_view, view)) { return; }

        Close();
        if (_view != null)
        {
            _view.Editor.TextArea.TextEntered -= TextEntered;
        }

        _view = view;
        if (_view != null)
        {
            _view.Editor.TextArea.TextEntered += TextEntered;
        }
    }

    /// <summary>Shows the popup, whether or not enough has been typed.</summary>
    public void ShowCompletionPopup() => ShowCompletionPopup(forced: true);

    /// <summary>Closes the popup if it is open.</summary>
    public void Close()
    {
        _window?.Close();
        _window = null;
    }

    /// <summary>Shows the popup.</summary>
    /// <param name="forced">Whether to show it even when little has been
    /// typed — true for Ctrl+Space, false for typing.</param>
    public void ShowCompletionPopup(bool forced)
    {
        if (_view == null) { return; }

        TextArea textArea = _view.Editor.TextArea;
        TextDocument store = _view.Editor.Document;
        int caret = textArea.Caret.Offset;
        DocumentLine line = store.GetLineByOffset(caret);

        CompletionResult result = _analyzer.Completions(_view.Document, caret);
        if (!result.HasCompletions)
        {
            Close();
            return;
        }

        int start = Math.Clamp(line.Offset + result.Column, line.Offset, caret);
        string prefix = store.GetText(start, caret - start);

        if (!forced && (!AutoComplete || prefix.Length < AutoCompleteLength))
        {
            return;
        }

        //Nothing to choose from: upstream hides the popup when the only match
        //is what the user has already typed.
        if (result.Model.Entries.Count == 1
            && string.Equals(
                result.Model.Entries[0].Insert, prefix, StringComparison.Ordinal))
        {
            Close();
            return;
        }

        Close();
        _window = new CompletionWindow(textArea)
        {
            StartOffset = start,
            EndOffset = caret,
            CloseAutomatically = true,
            CloseWhenCaretAtBeginning = true,
        };
        _window.CompletionList.IsFiltering = true;
        foreach (var entry in result.Model.Entries)
        {
            _window.CompletionList.CompletionData.Add(new LyCompletionItem(entry));
        }

        _window.Closed += (_, _) => _window = null;
        _window.Show();

        //The window filters on what is between its start and the caret, and it
        //does that itself as the user types. What it does NOT do is arrive
        //already narrowed — so the filter is applied here, and again when the
        //list is loaded, because templating the list repopulates it from the
        //unfiltered data. The empty query first is deliberate: SelectItem
        //short-circuits on a query it has already been given.
        void ApplyFilter()
        {
            if (prefix.Length == 0 || _window == null) { return; }

            _window.CompletionList.SelectItem(string.Empty);
            _window.CompletionList.SelectItem(prefix);
        }

        _window.CompletionList.Loaded += (_, _) => ApplyFilter();
        ApplyFilter();
    }

    private void TextEntered(object sender, TextInputEventArgs e)
    {
        if (_window != null) { return; }

        //Upstream's isTextEvent: a printable character, and not Delete.
        string text = e.Text;
        if (string.IsNullOrEmpty(text)) { return; }

        char last = text[text.Length - 1];
        if (last <= ' ') { return; }

        if (!AutoComplete) { return; }

        ShowCompletionPopup(forced: false);
    }
}

/// <summary>The completion commands.</summary>
public sealed class CompletionActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "autocomplete";

    /// <summary>Creates the collection.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public CompletionActions(SettingsStore settings = null)
        : base(CollectionName, settings) => Initialize();

    /// <summary>Gets the automatic-completion toggle.</summary>
    public AppAction AutoComplete { get; private set; }

    /// <summary>Gets the "show the popup now" command.</summary>
    public AppAction PopupCompletions { get; private set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Automatic Completion");

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        AutoComplete = Add("autocomplete").AsToggle(true);
        PopupCompletions = Add("popup_completions").WithShortcut("Ctrl+Space");
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        AutoComplete.Text = I18n.Get("Automatic &Completion");
        PopupCompletions.Text = I18n.Get("Show C&ompletions Popup");
    }
}
