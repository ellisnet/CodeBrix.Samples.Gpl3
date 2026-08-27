// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Commands;
using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;

namespace Fresco.Brix.Tools; //was previously: frescobaldi/browseriface.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One remembered place: a document and a spot in it.
/// </summary>
/// <remarks>
/// The spot is an <see cref="ITextAnchor"/> rather than an offset, so that
/// editing the document above it does not silently turn the remembered place
/// into a different one. Upstream gets this for free from <c>QTextCursor</c>,
/// which is itself an anchor.
/// </remarks>
public sealed class BrowsePosition
{
    /// <summary>Gets or sets the document, or null for "nothing yet".</summary>
    public EditorDocument Document { get; set; }

    /// <summary>Gets or sets the anchored spot in it.</summary>
    public ITextAnchor Anchor { get; set; }

    /// <summary>Gets whether the position points somewhere.</summary>
    public bool IsSet => Document != null && Anchor != null;
}

/// <summary>
/// Back and forward through the places the user has jumped to — the same
/// interface a web browser gives, over documents and cursor positions.
/// <para>
/// Every tool that MOVES the user somewhere they did not navigate to
/// themselves — Go to Definition, open-file-at-cursor, a click in the music,
/// an error in the log — goes through here rather than moving the caret
/// directly, so that Alt+Backspace brings them back.
/// </para>
/// </summary>
public sealed class BrowserInterface
{
    private readonly List<BrowsePosition> _history
        = new List<BrowsePosition> { new BrowsePosition() };
    private readonly DocumentManager _documents;
    private int _index;

    /// <summary>Creates the interface over the open documents.</summary>
    /// <param name="documents">The open documents.</param>
    /// <param name="settings">The store its shortcuts are remembered in.</param>
    public BrowserInterface(DocumentManager documents, SettingsStore settings = null)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        Actions = new BrowserActions(settings);
        Actions.GoBack.Handler = GoBack;
        Actions.GoForward.Handler = GoForward;
        documents.DocumentClosed += (_, e) => DocumentClosed(e.Document);
        UpdateActions();
    }

    /// <summary>Gets the Back and Forward commands.</summary>
    public BrowserActions Actions { get; }

    /// <summary>
    /// Gets or sets how to read the caret's current place, so a jump can
    /// remember where it came from.
    /// </summary>
    public Func<BrowsePosition> CurrentPosition { get; set; }

    /// <summary>Gets or sets how to put the caret at a remembered place.</summary>
    public Action<BrowsePosition> GoToPosition { get; set; }

    /// <summary>Gets the number of remembered places, for the tests.</summary>
    public int Count => _history.Count;

    /// <summary>Gets which of them is the current one, for the tests.</summary>
    public int Index => _index;

    /// <summary>
    /// Moves the caret somewhere, remembering where it was.
    /// </summary>
    /// <param name="document">The document to move to.</param>
    /// <param name="offset">The offset in it.</param>
    public void GoTo(EditorDocument document, int offset)
    {
        if (document == null) { return; }

        Remember();
        GoToPosition?.Invoke(new BrowsePosition
        {
            Document = document,
            Anchor = AnchorAt(document, offset),
        });
        UpdateActions();
    }

    /// <summary>
    /// Switches to a document, remembering where the caret was.
    /// </summary>
    /// <param name="document">The document.</param>
    public void SetCurrentDocument(EditorDocument document)
    {
        if (document == null) { return; }

        Remember();
        _documents.CurrentDocument = document;
        UpdateActions();
    }

    /// <summary>Goes back to the previous remembered place.</summary>
    public void GoBack()
    {
        if (_index <= 0) { return; }

        _history[_index] = Current();
        _index--;
        GoToPosition?.Invoke(_history[_index]);
        UpdateActions();
    }

    /// <summary>Goes forward again.</summary>
    public void GoForward()
    {
        if (_index >= _history.Count - 1) { return; }

        _history[_index] = Current();
        _index++;
        GoToPosition?.Invoke(_history[_index]);
        UpdateActions();
    }

    /// <summary>Turns the Back and Forward commands on and off.</summary>
    public void UpdateActions()
    {
        Actions.GoBack.IsEnabled = _index > 0;
        Actions.GoForward.IsEnabled = _index < _history.Count - 1;
    }

    private static ITextAnchor AnchorAt(EditorDocument document, int offset)
    {
        TextDocument store = document?.Document;
        if (store == null) { return null; }

        int clamped = Math.Max(0, Math.Min(offset, store.TextLength));
        return store.CreateAnchor(clamped);
    }

    private BrowsePosition Current()
        => CurrentPosition?.Invoke() ?? new BrowsePosition();

    private void Remember()
    {
        _history[_index] = Current();
        _index++;
        _history.RemoveRange(_index, _history.Count - _index);
        _history.Add(new BrowsePosition());
    }

    private void DocumentClosed(EditorDocument document)
    {
        for (int i = _history.Count - 1; i >= 0; i--)
        {
            if (_history[i].Document != document) { continue; }

            _history.RemoveAt(i);
            if (_index > i) { _index--; }
        }

        if (_history.Count == 0)
        {
            _history.Add(new BrowsePosition());
        }

        _index = Math.Max(0, Math.Min(_index, _history.Count - 1));
        UpdateActions();
    }
}

/// <summary>The Back and Forward commands.</summary>
public sealed class BrowserActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "browseriface";

    /// <summary>Creates the collection.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public BrowserActions(SettingsStore settings = null)
        : base(CollectionName, settings) => Initialize();

    /// <summary>Gets the "go to the previous place" command.</summary>
    public AppAction GoBack { get; private set; }

    /// <summary>Gets the "go to the next place" command.</summary>
    public AppAction GoForward { get; private set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Documents");

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        GoBack = Add("go_back").WithIcon("go-previous").WithShortcut("Alt+Backspace");
        GoForward = Add("go_forward").WithIcon("go-next").WithShortcut("Alt+End");
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        GoBack.Text = I18n.Get("Go to previous position");
        GoBack.IconText = I18n.Get("Back");
        GoForward.Text = I18n.Get("Go to next position");
        GoForward.IconText = I18n.Get("Forward");
    }
}
