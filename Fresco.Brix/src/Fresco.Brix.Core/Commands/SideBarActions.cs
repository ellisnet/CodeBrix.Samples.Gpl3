// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;

namespace Fresco.Brix.Commands; //was previously: frescobaldi/sidebar/ (class Actions)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The View menu's commands for the editor's margin: whether line numbers and
/// the folding margin are shown, and the folding operations themselves.
/// </summary>
public sealed class SideBarActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "sidebar";

    /// <summary>The setting remembering whether line numbers are shown.</summary>
    public const string LineNumbersSettingKey = "sidebar/linenumbers";

    /// <summary>The setting remembering whether folding is enabled.</summary>
    public const string FoldingSettingKey = "sidebar/folding";

    /// <summary>Creates the margin commands.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public SideBarActions(SettingsStore settings = null)
        : base(CollectionName, settings)
        => Initialize();

    /// <inheritdoc/>
    public override string Title => I18n.Get("Editor Margin");

    /// <summary>View &gt; Line Numbers.</summary>
    public AppAction ViewLineNumbers { get; private set; }

    /// <summary>View &gt; Folding &gt; Enable Folding.</summary>
    public AppAction FoldingEnable { get; private set; }

    /// <summary>View &gt; Folding &gt; Fold Current Region.</summary>
    public AppAction FoldingFoldCurrent { get; private set; }

    /// <summary>View &gt; Folding &gt; Fold Top Region.</summary>
    public AppAction FoldingFoldTop { get; private set; }

    /// <summary>View &gt; Folding &gt; Unfold Current Region.</summary>
    public AppAction FoldingUnfoldCurrent { get; private set; }

    /// <summary>View &gt; Folding &gt; Fold All.</summary>
    public AppAction FoldingFoldAll { get; private set; }

    /// <summary>View &gt; Folding &gt; Unfold All.</summary>
    public AppAction FoldingUnfoldAll { get; private set; }

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        ViewLineNumbers = Add("view_linenumbers").AsToggle(true);
        FoldingEnable = Add("folding_enable").AsToggle(true);
        FoldingFoldCurrent = Add("folding_fold_current");
        FoldingFoldTop = Add("folding_fold_top");
        FoldingUnfoldCurrent = Add("folding_unfold_current");
        FoldingFoldAll = Add("folding_fold_all");
        FoldingUnfoldAll = Add("folding_unfold_all");
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        ViewLineNumbers.Text = I18n.Get("&Line Numbers");
        FoldingEnable.Text = I18n.Get("&Enable Folding");
        FoldingFoldCurrent.Text = I18n.Get("&Fold Current Region");
        FoldingFoldTop.Text = I18n.Get("Fold &Top Region");
        FoldingUnfoldCurrent.Text = I18n.Get("&Unfold Current Region");
        FoldingFoldAll.Text = I18n.Get("Fold &All");
        FoldingUnfoldAll.Text = I18n.Get("U&nfold All");
    }
}
