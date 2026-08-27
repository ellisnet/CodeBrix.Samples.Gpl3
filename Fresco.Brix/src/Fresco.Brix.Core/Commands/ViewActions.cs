// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;

namespace Fresco.Brix.Commands; //was previously: frescobaldi/viewmanager.py (class ViewActions)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Window menu's view commands: splitting the editor area, closing a
/// split, and moving between splits.
/// </summary>
public sealed class ViewActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "view";

    /// <summary>Creates the view commands.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public ViewActions(SettingsStore settings = null)
        : base(CollectionName, settings)
        => Initialize();

    /// <inheritdoc/>
    public override string Title => I18n.Get("Views");

    /// <summary>Window &gt; Split Horizontally (a new view below).</summary>
    public AppAction WindowSplitHorizontal { get; private set; }

    /// <summary>Window &gt; Split Vertically (a new view beside).</summary>
    public AppAction WindowSplitVertical { get; private set; }

    /// <summary>Window &gt; Close Current View.</summary>
    public AppAction WindowCloseView { get; private set; }

    /// <summary>Window &gt; Close Other Views.</summary>
    public AppAction WindowCloseOthers { get; private set; }

    /// <summary>Window &gt; Next View.</summary>
    public AppAction WindowNextView { get; private set; }

    /// <summary>Window &gt; Previous View.</summary>
    public AppAction WindowPreviousView { get; private set; }

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        WindowSplitHorizontal = Add("window_split_horizontal")
            .WithIcon("view-split-top-bottom");
        WindowSplitVertical = Add("window_split_vertical")
            .WithIcon("view-split-left-right");
        WindowCloseView = Add("window_close_view").WithIcon("view-close")
            .WithShortcut("Ctrl+Shift+W");
        WindowCloseOthers = Add("window_close_others");
        WindowNextView = Add("window_next_view").WithIcon("go-next-view")
            .WithShortcuts(StandardKeys.NextChild);
        WindowPreviousView = Add("window_previous_view").WithIcon("go-previous-view")
            .WithShortcuts(StandardKeys.PreviousChild);

        //Nothing to close until the area has been split.
        WindowCloseView.IsEnabled = false;
        WindowCloseOthers.IsEnabled = false;
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        WindowSplitHorizontal.Text = I18n.Get("Split &Horizontally");
        WindowSplitVertical.Text = I18n.Get("Split &Vertically");
        WindowCloseView.Text = I18n.Get("&Close Current View");
        WindowCloseOthers.Text = I18n.Get("Close &Other Views");
        WindowNextView.Text = I18n.Get("&Next View");
        WindowPreviousView.Text = I18n.Get("&Previous View");
    }
}
