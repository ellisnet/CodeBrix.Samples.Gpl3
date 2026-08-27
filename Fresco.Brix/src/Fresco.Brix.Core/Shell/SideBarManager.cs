// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Editor;
using Fresco.Brix.Services;
using System;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/sidebar/

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Runs the editor's margin: whether line numbers and the folding margin are
/// shown, and the fold and unfold commands.
/// <para>
/// The two visibility settings are remembered, and are applied to every pane —
/// splitting the window does not leave one half with a margin and the other
/// without.
/// </para>
/// </summary>
public sealed class SideBarManager
{
    private readonly ViewManager _views;
    private readonly SettingsStore _settings;

    /// <summary>Creates the manager and wires the commands.</summary>
    /// <param name="views">The editor area.</param>
    /// <param name="actions">The margin commands.</param>
    /// <param name="settings">The store the choices are remembered in.</param>
    public SideBarManager(
        ViewManager views, SideBarActions actions, SettingsStore settings = null)
    {
        _views = views ?? throw new ArgumentNullException(nameof(views));
        Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _settings = settings;

        Actions.ViewLineNumbers.IsChecked =
            _settings?.GetBool(SideBarActions.LineNumbersSettingKey, true) ?? true;
        Actions.FoldingEnable.IsChecked =
            _settings?.GetBool(SideBarActions.FoldingSettingKey, true) ?? true;

        Actions.ViewLineNumbers.Handler = () =>
        {
            _settings?.SetBool(
                SideBarActions.LineNumbersSettingKey, Actions.ViewLineNumbers.IsChecked);
            Apply();
        };
        Actions.FoldingEnable.Handler = () =>
        {
            _settings?.SetBool(
                SideBarActions.FoldingSettingKey, Actions.FoldingEnable.IsChecked);
            Apply();
        };

        Actions.FoldingFoldAll.Handler
            = () => WithView(v => LyFoldingStrategy.FoldAll(v.FoldingManager));
        Actions.FoldingUnfoldAll.Handler
            = () => WithView(v => LyFoldingStrategy.UnfoldAll(v.FoldingManager));
        Actions.FoldingFoldTop.Handler
            = () => WithView(v => LyFoldingStrategy.FoldTop(v.FoldingManager));
        Actions.FoldingFoldCurrent.Handler = () => WithView(
            v => LyFoldingStrategy.FoldCurrent(v.FoldingManager, v.Editor.CaretOffset));
        Actions.FoldingUnfoldCurrent.Handler = () => WithView(
            v => LyFoldingStrategy.UnfoldCurrent(v.FoldingManager, v.Editor.CaretOffset));

        _views.ViewChanged += (_, _) => Apply();
        Apply();
    }

    /// <summary>Gets the margin commands.</summary>
    public SideBarActions Actions { get; }

    /// <summary>Applies the current choices to every pane.</summary>
    public void Apply()
    {
        bool lineNumbers = Actions.ViewLineNumbers.IsChecked;
        bool folding = Actions.FoldingEnable.IsChecked;

        foreach (var space in _views.ViewSpaces)
        {
            EditorView view = space.ActiveView;
            if (view == null) { continue; }

            view.Editor.ShowLineNumbers = lineNumbers;
            view.SetFoldingEnabled(folding);
        }
    }

    private void WithView(Action<EditorView> work)
    {
        EditorView view = _views.ActiveView;
        if (view != null) { work(view); }
    }
}
