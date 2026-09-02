// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Fresco.Brix.Widgets;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;

namespace Fresco.Brix.Preferences; //was previously: frescobaldi/preferences/helpers.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Helper Applications page: the command that opens each kind of file.
/// </summary>
/// <remarks>
/// ⚠ PARTIAL BY DESIGN. Upstream has nine rows; the ninth is <c>git</c>, and
/// ruling FR5.7 keeps version control out of the application, so there is no
/// Git command to configure and the row is not here. The other eight are
/// upstream's own, in upstream's own order, under upstream's own settings keys.
/// </remarks>
public sealed class HelpersPage : PreferencesPage
{
    private readonly Dictionary<string, UrlRequester> _entries
        = new Dictionary<string, UrlRequester>(StringComparer.Ordinal);

    /// <summary>Creates the page.</summary>
    /// <param name="context">What the page configures.</param>
    public HelpersPage(PreferencesContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Helper Apps");

    /// <inheritdoc/>
    public override string Help => "prefs_helpers";

    /// <inheritdoc/>
    public override string IconName => "applications-other";

    /// <summary>Gets the values the page reads and writes.</summary>
    public HelperValues Values { get; } = new HelperValues();

    /// <inheritdoc/>
    public override void LoadSettings()
    {
        Values.Load(Settings);
        foreach (var pair in _entries)
        {
            pair.Value.Path = Values.Command(pair.Key);
        }
    }

    /// <inheritdoc/>
    public override void SaveSettings()
    {
        foreach (var pair in _entries)
        {
            Values.SetCommand(pair.Key, pair.Value.Path);
        }

        Values.Save(Settings);
    }

    /// <inheritdoc/>
    protected override UIElement Build()
    {
        List<UIElement> rows = new List<UIElement>
        {
            Note(I18n.Get(
                "Below you can enter commands to open different file types. "
                + "$f is replaced with the filename, "
                + "$u with the URL. "
                + "Leave a field empty to use the operating system default "
                + "application.")),
        };

        foreach (var (type, label, toolTip) in HelperRows())
        {
            //A helper is a COMMAND LINE, which may name a program on the path
            //rather than a file that exists — so the entry does not insist the
            //path be there, and the picker offers a file.
            UrlRequester entry = new UrlRequester(UrlRequesterMode.ExistingFile)
            {
                PickAsync = Context.PickAsync,
            };
            entry.Changed += (_, _) => MarkChanged();
            if (toolTip != null) { entry.EntryToolTip = toolTip; }

            _entries[type] = entry;
            rows.Add(Labelled(label, entry, 140));
        }

        return Stack(Group(I18n.Get("Helper Applications"), Rows(rows.ToArray())));
    }

    /// <summary>
    /// The helper types, their labels and their tool tips, in upstream's own
    /// order.
    /// </summary>
    /// <returns>The rows.</returns>
    private static IEnumerable<(string Type, string Label, string ToolTip)> HelperRows()
    {
        yield return ("pdf", I18n.Get("PDF:"), null);
        yield return ("midi", I18n.Get("MIDI:"), null);
        yield return ("svg", I18n.Get("SVG:"), null);
        yield return ("image", I18n.Get("Image:"), null);
        yield return ("browser", I18n.Get("Browser:"), null);
        yield return ("email", I18n.Get("E-Mail:"),
            I18n.Get("Command that should accept a mailto: URL."));
        yield return ("directory", I18n.Get("File Manager:"), null);
        yield return ("shell", I18n.Get("Shell:"),
            I18n.Get("Command to open a Terminal or Command window."));

        //was previously: yield "git", _("Git:") — ruling FR5.7 keeps version
        //control out of the application, so there is nothing to configure.
    }
}
