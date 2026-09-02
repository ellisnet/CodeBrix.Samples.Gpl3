// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace Fresco.Brix.Preferences; //was previously: frescobaldi/preferences/musicviewers.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Music View page: which scores are opened, and the scaling, arrangement
/// and scrolling the view comes up with.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ Upstream's PRINTING group is not here: ruling FR5.5 leaves the application
/// with nothing to print, so a printer resolution configures nothing.
/// </para>
/// <para>
/// ⚠ Four more of upstream's rows and one whole group have no backing behaviour
/// in this port and are absent: "Remember View settings per-document" (the panel
/// keeps ONE view, not one per document), "Kinetic scrolling", "Use Page Up and
/// Page Down keys to change pages", "Show scrollbars" (the view draws its own
/// bars and they are not optional — board trap 2), and the Magnifier group (the
/// glass is made on demand by the magnifier command and reads no settings).
/// <see cref="MusicViewValues"/> records the upstream keys that go with them.
/// </para>
/// <para>
/// Upstream's four exclusive radio buttons for the scaling, and its four for the
/// arrangement, are ComboBoxes here — the same substitution tranche 1 made on
/// the General page, for the same reason (nothing in this application uses a
/// RadioButton and none is proved on the Skia heads).
/// </para>
/// </remarks>
public sealed class MusicViewPage : PreferencesPage
{
    private CheckBox _newerFilesOnly;
    private ComboBox _viewMode;
    private NumberEntry _scale;
    private ComboBox _pageLayout;
    private ComboBox _orientation;
    private CheckBox _continuous;
    private CheckBox _shadow;

    /// <summary>Creates the page.</summary>
    /// <param name="context">What the page configures.</param>
    public MusicViewPage(PreferencesContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Music View");

    /// <inheritdoc/>
    public override string Help => "prefs_musicviewers";

    /// <inheritdoc/>
    public override string IconName => "audio-x-generic";

    /// <summary>Gets the values the page reads and writes.</summary>
    public MusicViewValues Values { get; } = new MusicViewValues();

    /// <inheritdoc/>
    public override void LoadSettings()
    {
        Values.Load(Settings);

        _newerFilesOnly.IsChecked = Values.OnlyNewerFiles;
        _viewMode.SelectedIndex = IndexOf(MusicViewValues.ViewModes, Values.ViewMode);
        _scale.SetValueQuietly(Values.ScalePercent);
        _pageLayout.SelectedIndex = IndexOf(MusicViewValues.PageLayouts, Values.PageLayout);
        _orientation.SelectedIndex = IndexOf(MusicViewValues.Orientations, Values.Orientation);
        _continuous.IsChecked = Values.ContinuousScrolling;
        _shadow.IsChecked = Values.PageShadow;

        UpdateScaleEntry();
    }

    /// <inheritdoc/>
    public override void SaveSettings()
    {
        Values.OnlyNewerFiles = _newerFilesOnly.IsChecked == true;
        Values.ViewMode = At(MusicViewValues.ViewModes, _viewMode.SelectedIndex);
        Values.ScalePercent = _scale.Value;
        Values.PageLayout = At(MusicViewValues.PageLayouts, _pageLayout.SelectedIndex);
        Values.Orientation = At(MusicViewValues.Orientations, _orientation.SelectedIndex);
        Values.ContinuousScrolling = _continuous.IsChecked == true;
        Values.PageShadow = _shadow.IsChecked == true;

        Values.Save(Settings);
    }

    /// <inheritdoc/>
    protected override UIElement Build()
        => Stack(
            Group(I18n.Get("Documents"), BuildDocuments()),
            Group(I18n.Get("Page scaling"), BuildScaling()),
            Group(I18n.Get("Page layout"), BuildPageLayout()),
            Group(I18n.Get("Scrolling"), BuildScrolling()),
            Group(I18n.Get("Viewer options"), BuildViewerOptions()));

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (int index = 0; index < names.Count; index++)
        {
            if (string.Equals(names[index], name, StringComparison.Ordinal)) { return index; }
        }

        return 0;
    }

    private static string At(IReadOnlyList<string> names, int index)
        => index >= 0 && index < names.Count ? names[index] : names[0];

    /// <summary>
    /// The sign after a percentage, from upstream's own one-character message.
    /// </summary>
    /// <returns>The suffix.</returns>
    private static string PercentSuffix()
        //L10N: percent unit sign
        => I18n.Get("percent unit sign", "%");

    private UIElement BuildDocuments()
    {
        _newerFilesOnly = Tick(
            I18n.Get("Only load updated PDF documents"),
            //was previously: "…Frescobaldi will not open…" (FR9).
            I18n.Format(
                I18n.Get(
                    "If checked, {appname} will not open PDF documents that are not\n"
                    + "up-to-date (i.e. the source file has been modified later)."),
                ("appname", AppInfo.AppName)));

        //was previously: upstream's "Remember View settings per-document" tick,
        //over musicview/document_properties. The Music View here shows every
        //score in ONE view with one set of properties, so there is nothing for
        //the setting to switch on. See the class remarks.

        return Rows(_newerFilesOnly);
    }

    private UIElement BuildScaling()
    {
        //was previously: four exclusive radio buttons. "Fixed scale" loses the
        //colon upstream's caption carries, because upstream's caption LABELS the
        //slider beside it — the colon belongs on the row below, where the number
        //actually is, and that row uses upstream's own message verbatim.
        _scale = Number(
            MusicViewValues.MinimumScalePercent,
            MusicViewValues.MaximumScalePercent,
            null,
            PercentSuffix());

        _viewMode = Choice(
            I18n.Get("Fixed scale"),
            I18n.Get("Fit height"),
            I18n.Get("Fit width"),
            //Upstream's own comment: "to match the Music menu".
            I18n.Get("Fit page"));
        //The entry exists before this is wired, so a selection cannot arrive
        //before there is something to grey out.
        _viewMode.SelectionChanged += (_, _) => UpdateScaleEntry();

        return Rows(
            Labelled(I18n.Get("Scaling:"), _viewMode),
            Labelled(I18n.Get("Fixed scale:"), _scale));
    }

    private UIElement BuildPageLayout()
    {
        _pageLayout = Choice(
            I18n.Get("Single"),
            I18n.Get("Two pages (first page right)"),
            I18n.Get("Two pages (first page left)"),
            I18n.Get("Grid layout"));

        //Upstream hangs this on the "Grid layout" radio button alone; with one
        //control for the whole choice it belongs on the control.
        ToolTipService.SetToolTip(_pageLayout, I18n.Get(
            "The layout of pages (horizontal or vertical) adjusts dynamically "
            + "based on the zoom level and the available space in the Music View. "
            + "Continuous scrolling option must be checked."));

        return Rows(_pageLayout);
    }

    private UIElement BuildScrolling()
    {
        _orientation = Choice(I18n.Get("Horizontal"), I18n.Get("Vertical"));
        _continuous = Tick(I18n.Get("Continuous scrolling"));

        //was previously: upstream's "Kinetic scrolling" tick (kinetic_scrolling)
        //and its "Use Page Up and Page Down keys to change pages" tick
        //(strict_paging). Neither behaviour exists in this view, so neither key
        //is read and neither row would do anything. See the class remarks.

        return Rows(
            Labelled(I18n.Get("Orientation:"), _orientation),
            _continuous);
    }

    private UIElement BuildViewerOptions()
    {
        _shadow = Tick(
            I18n.Get("Show shadow under pages"),
            //was previously: "…Frescobaldi draws a shadow…" (FR9).
            I18n.Format(
                I18n.Get("If checked, {appname} draws a shadow around the pages."),
                ("appname", AppInfo.AppName)));

        //was previously: upstream's "Show scrollbars" tick, over
        //musicview/show_scrollbars. The view's scroll bars are drawn by the view
        //itself because the theme's paint nothing on the Skia heads (board trap
        //2); they are not a thing that can be turned off.

        return Rows(_shadow);
    }

    /// <summary>
    /// Greys the fixed scale out while a fit mode is chosen.
    /// </summary>
    /// <remarks>Upstream's <c>toggleFixedScaleControls</c>.</remarks>
    private void UpdateScaleEntry()
        => _scale.IsEntryEnabled = _viewMode.SelectedIndex == 0;
}
