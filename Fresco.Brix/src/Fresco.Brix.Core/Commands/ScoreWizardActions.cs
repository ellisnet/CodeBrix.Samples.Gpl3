// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;

namespace Fresco.Brix.Commands; //was previously: frescobaldi/scorewiz/__init__.py (class Actions)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>The two ways into the Score Wizard.</summary>
public sealed class ScoreWizardActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "scorewiz";

    /// <summary>Creates the collection.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public ScoreWizardActions(SettingsStore settings = null)
        : base(CollectionName, settings) => Initialize();

    /// <summary>Gets the command that opens the wizard on a new score.</summary>
    public AppAction ScoreWizard { get; private set; }

    /// <summary>Gets the command that opens it on the current document.</summary>
    public AppAction ScoreWizardFromCurrent { get; private set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Score Wizard");

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        //was previously: neither carried an icon name, because no icon set was
        //shipped. Upstream gives both `tools-score-wizard'
        //(scorewiz/__init__.py:62,66).
        ScoreWizard = Add("scorewiz").WithIcon("tools-score-wizard")
            .WithShortcut("Ctrl+Shift+N");
        ScoreWizardFromCurrent = Add("scorewiz_from_current")
            .WithIcon("tools-score-wizard");
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        ScoreWizard.Text = I18n.Get("Score &Wizard...");
        ScoreWizardFromCurrent.Text = I18n.Get("From C&urrent Document...");
    }
}
