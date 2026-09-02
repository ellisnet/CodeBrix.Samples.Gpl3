// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documentation;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace Fresco.Brix.Preferences; //was previously: frescobaldi/preferences/documentation.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Documentation page: which manual the Documentation Browser opens on, and
/// whether its contents list starts open.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ Upstream's page has two groups and the FIRST is gone: "Paths to LilyPond
/// Documentation" is a list of local folders and lilypond.org URLs to search
/// for a documentation tree. Ruling FR5.1 compiles one engine in and ruling FR8
/// bundles the manuals as assets, so there is no tree to point at and nothing
/// to browse to. Its second group configures the BROWSER, and that is what
/// survives — minus the preferred-language row (the bundled manuals are
/// English) and the font row (a PDF carries its own faces; upstream's font
/// belonged to the HTML browser FR8 removed).
/// </para>
/// <para>
/// ⚠ Upstream's <c>help</c> for this page is <c>prefs_lilydoc</c>, a user-guide
/// page that dies with the feature it documents (FR5.1). This page records
/// <c>prefs_documentation</c> — a Fresco.Brix-original identifier, and one for
/// W-I18N's renamed-string table.
/// </para>
/// </remarks>
public sealed class DocumentationPage : PreferencesPage
{
    private readonly List<string> _manuals = new List<string>();

    private ComboBox _manual;
    private CheckBox _contents;

    /// <summary>Creates the page.</summary>
    /// <param name="context">What the page configures.</param>
    public DocumentationPage(PreferencesContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Documentation");

    /// <inheritdoc/>
    public override string Help => "prefs_documentation";

    /// <inheritdoc/>
    public override string IconName => "help-contents";

    /// <summary>Gets the values the page reads and writes.</summary>
    public DocumentationValues Values { get; } = new DocumentationValues();

    /// <inheritdoc/>
    public override void LoadSettings()
    {
        Values.Load(Settings);
        int index = _manuals.IndexOf(Values.Manual);
        _manual.SelectedIndex = index < 0 ? 0 : index;
        _contents.IsChecked = Values.ShowContents;
    }

    /// <inheritdoc/>
    public override void SaveSettings()
    {
        Values.Manual = _manual.SelectedIndex >= 0 && _manual.SelectedIndex < _manuals.Count
            ? _manuals[_manual.SelectedIndex]
            : ManualCatalog.DefaultName;
        Values.ShowContents = _contents.IsChecked == true;
        Values.Save(Settings);
    }

    /// <inheritdoc/>
    protected override UIElement Build()
        => Stack(Group(I18n.Get("Documentation Browser"), BuildBrowser()));

    private UIElement BuildBrowser()
    {
        _manual = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _manuals.Clear();
        foreach (var manual in ManualCatalog.All)
        {
            //A manual's TITLE is data, not a msgid — the documents themselves
            //are English and are shipped under their own names (decision FD12).
            _manuals.Add(manual.Name);
            _manual.Items.Add(new ComboBoxItem { Content = manual.Title });
        }

        _manual.SelectionChanged += (_, _) => MarkChanged();

        _contents = Tick(I18n.Get("Show the contents list"));

        return Rows(
            Note(I18n.Format(
                I18n.Get(
                    "The {count} manuals are installed with the program and are "
                    + "shown in the Documentation Browser."),
                ("count", ManualCatalog.All.Count))),
            Labelled(I18n.Get("Manual to show first:"), _manual),
            _contents);
    }
}
