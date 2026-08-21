// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/viewmanager.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One pane of the editor area. It holds a view of every document that has
/// been shown in it, with the most recent on top, and a small status bar of
/// its own showing where the caret is and which document this pane is on.
/// </summary>
public sealed class ViewSpace : Grid
{
    private readonly Grid _stack = new Grid();
    private readonly TextBlock _positionLabel = new TextBlock();
    private readonly TextBlock _nameLabel = new TextBlock();
    private readonly List<EditorView> _views = new List<EditorView>();
    private readonly ViewManager _manager;

    /// <summary>Creates a pane.</summary>
    /// <param name="manager">The editor area it belongs to.</param>
    internal ViewSpace(ViewManager manager)
    {
        _manager = manager;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        SetRow(_stack, 0);
        Children.Add(_stack);

        StackPanel status = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Padding = new Thickness(6, 2, 6, 2),
        };
        status.Children.Add(_positionLabel);
        status.Children.Add(_nameLabel);
        SetRow(status, 1);
        Children.Add(status);
        StatusBar = status;

        IsActive = false;
    }

    /// <summary>Raised when this pane shows a different view.</summary>
    public event EventHandler ViewChanged;

    /// <summary>Raised when a view is made for a document in this pane.</summary>
    /// <remarks>The window uses this to give the commands first refusal on the
    /// new editor's keystrokes; anything else that has to reach into an editor
    /// as it is born belongs here too.</remarks>
    public event EventHandler<EditorViewEventArgs> ViewCreated;

    /// <summary>Gets the pane's status bar, so a head can restyle it.</summary>
    public StackPanel StatusBar { get; }

    /// <summary>Gets the view on top, or null when the pane is empty.</summary>
    public EditorView ActiveView => _views.Count == 0 ? null : _views[_views.Count - 1];

    /// <summary>Gets the document on top, or null.</summary>
    public EditorDocument Document => ActiveView?.Document;

    /// <summary>Gets or sets whether this is the pane the user is working in;
    /// only the active pane's status bar is drawn in full strength.</summary>
    public bool IsActive
    {
        get;
        set
        {
            field = value;
            StatusBar.Opacity = value ? 1.0 : 0.55;
        }
    }

    /// <summary>
    /// Shows a document, making a view for it if this pane has never shown it.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="state">Its shared editor state.</param>
    /// <param name="editorFontFamily">The editor font, or null.</param>
    public void ShowDocument(
        EditorDocument document,
        DocumentEditorState state,
        FontFamily editorFontFamily = null)
    {
        if (document == null || document == Document) { return; }

        EditorView view = _views.FirstOrDefault(v => v.Document == document);
        if (view != null)
        {
            _views.Remove(view);
        }
        else
        {
            view = new EditorView(document, state, editorFontFamily);
            ViewCreated?.Invoke(this, new EditorViewEventArgs(view));
            view.CursorPositionChanged += (_, _) => UpdateStatusBar();

            //The name half of the status bar carries the modified star, so it
            //has to follow the document as well as the caret — otherwise a
            //save leaves a star behind until the user next moves the caret.
            document.ModificationChanged += (_, _) => UpdateStatusBar();
            document.UrlChanged += (_, _) => UpdateStatusBar();
            view.Focused += (_, _) => _manager?.SetActiveViewSpace(this);
            _stack.Children.Add(view);
        }

        _views.Add(view);
        UpdateVisibility();
        UpdateStatusBar();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops a document's view from this pane.</summary>
    /// <param name="document">The document.</param>
    public void RemoveDocument(EditorDocument document)
    {
        EditorView view = _views.FirstOrDefault(v => v.Document == document);
        if (view == null) { return; }

        _views.Remove(view);
        _stack.Children.Remove(view);
        UpdateVisibility();
        UpdateStatusBar();
    }

    /// <summary>Gives the pane's editor keyboard focus.</summary>
    public void FocusActiveView() => ActiveView?.FocusEditor();

    /// <summary>Refreshes the status bar after a rename or a modification.</summary>
    public void UpdateStatusBar()
    {
        EditorView view = ActiveView;
        if (view == null)
        {
            _positionLabel.Text = string.Empty;
            _nameLabel.Text = string.Empty;
            return;
        }

        _positionLabel.Text = I18n.Format(
            I18n.Get("Line: {line}, Col: {column}"),
            ("line", view.Line), ("column", view.Column));
        _nameLabel.Text = view.Document.DocumentName()
            + (view.Document.IsModified ? " *" : string.Empty);
    }

    private void UpdateVisibility()
    {
        EditorView active = ActiveView;
        foreach (var view in _views)
        {
            view.Visibility = view == active ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}

/// <summary>
/// The editor area: one pane to begin with, which the user can split as often
/// as they like, horizontally or vertically, to see several documents — or
/// several places in one document — at once.
/// </summary>
/// <remarks>
/// The pane list is kept most-recently-active LAST, exactly as upstream keeps
/// it, because that ordering is what makes "the active pane" and "the pane to
/// fall back to when this one closes" fall out for free.
/// </remarks>
public sealed class ViewManager : SplitContainer
{
    private readonly List<ViewSpace> _viewSpaces = new List<ViewSpace>();
    private readonly Func<EditorDocument, DocumentEditorState> _stateFor;
    private readonly FontFamily _editorFontFamily;

    /// <summary>Creates the editor area with one pane.</summary>
    /// <param name="stateFor">Answers a document's shared editor state.</param>
    /// <param name="actions">The Window menu's view commands, wired up here.</param>
    /// <param name="editorFontFamily">The editor font, or null.</param>
    public ViewManager(
        Func<EditorDocument, DocumentEditorState> stateFor,
        ViewActions actions = null,
        FontFamily editorFontFamily = null)
    {
        _stateFor = stateFor ?? throw new ArgumentNullException(nameof(stateFor));
        _editorFontFamily = editorFontFamily;
        Actions = actions;

        ViewSpace first = new ViewSpace(this) { IsActive = true };
        _viewSpaces.Add(first);
        AddPane(first);
        ViewSpaceCreated?.Invoke(this, new ViewSpaceEventArgs(first));

        WireActions();
    }

    /// <summary>Raised when the active pane, or the view in it, changes.</summary>
    public event EventHandler ViewChanged;

    /// <summary>
    /// Raised when a pane is created, so per-pane extras can attach to it.
    /// </summary>
    /// <remarks>
    /// Upstream's <c>app.viewSpaceCreated</c>, which is how its music-position
    /// display finds its way onto every pane's status bar.
    /// </remarks>
    public static event EventHandler<ViewSpaceEventArgs> ViewSpaceCreated;

    /// <summary>Gets the Window menu's view commands, or null.</summary>
    public ViewActions Actions { get; }

    /// <summary>Gets the pane the user is working in.</summary>
    public ViewSpace ActiveViewSpace => _viewSpaces[_viewSpaces.Count - 1];

    /// <summary>Gets the view the user is working in, or null.</summary>
    public EditorView ActiveView => ActiveViewSpace.ActiveView;

    /// <summary>Gets the panes, least-recently-active first.</summary>
    public IReadOnlyList<ViewSpace> ViewSpaces => _viewSpaces;

    /// <summary>
    /// Shows a document in the active pane — or raises the pane already
    /// showing it, when asked to look.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="findOpenView">Whether to prefer a pane already on it.</param>
    public void SetCurrentDocument(EditorDocument document, bool findOpenView = false)
    {
        if (document == null) { return; }

        if (document != ActiveViewSpace.Document)
        {
            ViewSpace found = findOpenView
                ? _viewSpaces.Take(_viewSpaces.Count - 1).LastOrDefault(
                    s => s.Document == document)
                : null;

            if (found != null)
            {
                SetActiveViewSpace(found);
            }
            else
            {
                ActiveViewSpace.ShowDocument(
                    document, _stateFor(document), _editorFontFamily);
            }
        }

        //A pane showing nothing yet gets this document too, so a fresh split
        //is never blank.
        foreach (var space in _viewSpaces.Take(_viewSpaces.Count - 1)
            .Where(s => s.Document == null))
        {
            space.ShowDocument(document, _stateFor(document), _editorFontFamily);
        }

        ViewChanged?.Invoke(this, EventArgs.Empty);
        ActiveViewSpace.FocusActiveView();
    }

    /// <summary>Makes a pane the active one.</summary>
    /// <param name="space">The pane.</param>
    public void SetActiveViewSpace(ViewSpace space)
    {
        if (space == null || space == ActiveViewSpace) { return; }

        ViewSpace previous = ActiveViewSpace;
        _viewSpaces.Remove(space);
        _viewSpaces.Add(space);
        previous.IsActive = false;
        space.IsActive = true;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes a closed document's views out of every pane.</summary>
    /// <param name="document">The document.</param>
    public void DocumentClosed(EditorDocument document)
    {
        EditorDocument active = ActiveViewSpace.Document;
        foreach (var space in _viewSpaces)
        {
            space.RemoveDocument(document);
        }

        if (document == active || active == null) { return; }

        foreach (var space in _viewSpaces.Take(_viewSpaces.Count - 1)
            .Where(s => s.Document == null))
        {
            space.ShowDocument(active, _stateFor(active), _editorFontFamily);
        }
    }

    /// <summary>Splits a pane, putting the new one below or beside it.</summary>
    /// <param name="space">The pane to split.</param>
    /// <param name="orientation">Vertical puts the new pane below;
    /// horizontal puts it beside.</param>
    public void SplitViewSpace(ViewSpace space, Orientation orientation)
    {
        if (space == null) { return; }

        bool wasActive = space == ActiveViewSpace;
        SplitContainer container = ContainerOf(space);
        if (container == null) { return; }

        ViewSpace created = new ViewSpace(this);
        ViewSpaceCreated?.Invoke(this, new ViewSpaceEventArgs(created));

        if (container.Count == 1)
        {
            //A container holding one pane has no orientation worth keeping.
            container.Orientation = orientation;
            container.AddPane(created);
        }
        else if (container.Orientation == orientation)
        {
            container.InsertPane(container.IndexOf(space) + 1, created);
        }
        else
        {
            //Splitting the other way needs a nested container in this slot.
            int index = container.IndexOf(space);
            IReadOnlyList<double> sizes = container.Sizes();
            SplitContainer nested = new SplitContainer { Orientation = orientation };
            container.RemovePane(space);
            nested.AddPane(space);
            nested.AddPane(created);
            container.InsertPane(index, nested);
            container.SetSizes(sizes);
        }

        //Least-recently-active first: a brand new pane has never been active.
        _viewSpaces.Insert(0, created);
        if (space.Document != null)
        {
            created.ShowDocument(
                space.Document, _stateFor(space.Document), _editorFontFamily);
        }

        if (wasActive)
        {
            SetActiveViewSpace(created);
            created.FocusActiveView();
        }

        UpdateActionsEnabled();
    }

    /// <summary>Closes a pane, giving its space back to its neighbours.</summary>
    /// <param name="space">The pane to close.</param>
    public void CloseViewSpace(ViewSpace space)
    {
        if (space == null || !CanCloseViewSpace) { return; }

        if (space == ActiveViewSpace)
        {
            SetActiveViewSpace(_viewSpaces[_viewSpaces.Count - 2]);
        }

        SplitContainer container = ContainerOf(space);
        container?.RemovePane(space);
        _viewSpaces.Remove(space);

        //A container left holding one pane is redundant: its child moves up
        //into its place, so the tree never grows a chain of single-pane
        //containers as panes are opened and closed.
        Collapse(container);
        UpdateActionsEnabled();
    }

    /// <summary>Closes every pane but the active one.</summary>
    public void CloseOtherViewSpaces()
    {
        foreach (var space in _viewSpaces.Take(_viewSpaces.Count - 1).Reverse().ToList())
        {
            CloseViewSpace(space);
        }
    }

    /// <summary>Moves to the next pane.</summary>
    public void NextViewSpace() => CycleViewSpace(1);

    /// <summary>Moves to the previous pane.</summary>
    public void PreviousViewSpace() => CycleViewSpace(-1);

    /// <summary>Gets whether there is more than one pane to close.</summary>
    public bool CanCloseViewSpace => _viewSpaces.Count > 1;

    private void CycleViewSpace(int direction)
    {
        if (_viewSpaces.Count < 2) { return; }

        //Cycling walks the panes in LAYOUT order, not activation order, so it
        //moves predictably around the screen.
        List<ViewSpace> ordered = LayoutOrder(this).ToList();
        int index = ordered.IndexOf(ActiveViewSpace);
        if (index < 0) { return; }

        ViewSpace next = ordered[
            ((index + direction) % ordered.Count + ordered.Count) % ordered.Count];
        SetActiveViewSpace(next);
        next.FocusActiveView();
    }

    private static IEnumerable<ViewSpace> LayoutOrder(SplitContainer container)
    {
        foreach (var pane in container.Panes)
        {
            if (pane is ViewSpace space)
            {
                yield return space;
            }
            else if (pane is SplitContainer nested)
            {
                foreach (var inner in LayoutOrder(nested))
                {
                    yield return inner;
                }
            }
        }
    }

    private SplitContainer ContainerOf(ViewSpace space) => FindContainer(this, space);

    private static SplitContainer FindContainer(SplitContainer container, UIElement pane)
    {
        if (container.IndexOf(pane) >= 0) { return container; }

        foreach (var nested in container.Panes.OfType<SplitContainer>())
        {
            SplitContainer found = FindContainer(nested, pane);
            if (found != null) { return found; }
        }

        return null;
    }

    private void Collapse(SplitContainer container)
    {
        if (container == null || container == this || container.Count != 1) { return; }

        SplitContainer parent = FindContainer(this, container);
        if (parent == null) { return; }

        UIElement only = container.Pane(0);
        int index = parent.IndexOf(container);
        IReadOnlyList<double> sizes = parent.Sizes();
        container.RemovePane(only);
        parent.RemovePane(container);
        parent.InsertPane(index, only);
        parent.SetSizes(sizes);
        Collapse(parent);
    }

    private void WireActions()
    {
        if (Actions == null) { return; }

        //Upstream names these the way the user reads them: splitting
        //"horizontally" puts the new pane BELOW, which is a vertical layout.
        Actions.WindowSplitHorizontal.Handler
            = () => SplitViewSpace(ActiveViewSpace, Orientation.Vertical);
        Actions.WindowSplitVertical.Handler
            = () => SplitViewSpace(ActiveViewSpace, Orientation.Horizontal);
        Actions.WindowCloseView.Handler = () => CloseViewSpace(ActiveViewSpace);
        Actions.WindowCloseOthers.Handler = CloseOtherViewSpaces;
        Actions.WindowNextView.Handler = NextViewSpace;
        Actions.WindowPreviousView.Handler = PreviousViewSpace;
        UpdateActionsEnabled();
    }

    private void UpdateActionsEnabled()
    {
        if (Actions == null) { return; }

        Actions.WindowCloseView.IsEnabled = CanCloseViewSpace;
        Actions.WindowCloseOthers.IsEnabled = CanCloseViewSpace;
    }
}

/// <summary>Names the pane that has just been created.</summary>
public sealed class ViewSpaceEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="space">The new pane.</param>
    public ViewSpaceEventArgs(ViewSpace space) => Space = space;

    /// <summary>Gets the new pane.</summary>
    public ViewSpace Space { get; }
}

/// <summary>Names an editor view.</summary>
public sealed class EditorViewEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="view">The view.</param>
    public EditorViewEventArgs(EditorView view) => View = view;

    /// <summary>Gets the view.</summary>
    public EditorView View { get; }
}
