// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fresco.Brix.Engrave; //was previously: frescobaldi/layoutcontrol/__init__.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The layout-control modes: ways of engraving a score that draw what the
/// engraver was THINKING — where it put its anchors, which directions were
/// forced, where the collision skylines lie.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ MEASURED at W3 (2026-08-20): these are NOT engine options and never were.
/// Only <c>debug-skylines</c> exists in LilyPond 2.27.2's own option list; the
/// other six (<c>debug-voices</c>, <c>debug-directions</c>,
/// <c>debug-grob-anchors</c>, <c>debug-grob-names</c>,
/// <c>debug-paper-columns</c>, <c>debug-annotate-spacing</c>) are names
/// upstream Frescobaldi invented for ITS OWN formatter files, which read them
/// with <c>ly:get-option</c> after being pulled in with
/// <c>-dinclude-settings</c>. Porting the modes therefore means shipping those
/// formatter files as assets and being able to set an arbitrary option for one
/// run — and that second half is a change to the engine's per-run seam, which
/// the board batches into the next wave that touches it.
/// </para>
/// <para>
/// So this class is complete and its options are carried onto the job; what
/// does not happen yet is the engine applying them, and a job says so in its
/// own log rather than quietly engraving an ordinary score.
/// </para>
/// </remarks>
public static class LayoutControl
{
    private sealed record Mode(string Option, Func<string> Label, Func<string> ToolTip);

    private static readonly Dictionary<string, Mode> Modes
        = new Dictionary<string, Mode>(StringComparer.Ordinal)
        {
            ["annotate-spacing"] = new Mode(
                "-ddebug-annotate-spacing",
                () => I18n.Get("Annotate Spacing"),
                () => I18n.Get("Use LilyPort's \"annotate spacing\" option to\n"
                    + "display measurement information")),
            ["directions"] = new Mode(
                "-ddebug-directions",
                () => I18n.Get("Color explicit directions"),
                () => I18n.Get(
                    "Highlight elements that are explicitly switched up- or downwards")),
            ["grob-anchors"] = new Mode(
                "-ddebug-grob-anchors",
                () => I18n.Get("Display Grob Anchors"),
                () => I18n.Get("Display a dot at the anchor point of each grob")),
            ["grob-names"] = new Mode(
                "-ddebug-grob-names",
                () => I18n.Get("Display Grob Names"),
                () => I18n.Get("Display the name of each grob")),
            ["paper-columns"] = new Mode(
                "-ddebug-paper-columns",
                () => I18n.Get("Display Paper Columns"),
                () => I18n.Get("Display info on the paper columns")),
            ["skylines"] = new Mode(
                "-ddebug-display-skylines",
                () => I18n.Get("Display Skylines"),
                () => I18n.Get("Display the skylines that LilyPort "
                    + "uses to detect collisions.")),
            ["voices"] = new Mode(
                "-ddebug-voices",
                () => I18n.Get("Color \\voiceXXX"),
                () => I18n.Get("Highlight notes that are explicitly "
                    + "set to \\voiceXXX")),
        };

    /// <summary>Gets the mode names, in the order the panel lists them.</summary>
    public static IReadOnlyList<string> ModeList { get; } = new[]
    {
        "voices", "directions", "grob-anchors", "grob-names",
        "skylines", "paper-columns", "annotate-spacing",
    };

    /// <summary>The settings group the panel's state lives under.</summary>
    public const string SettingsPrefix = "lilypond_settings/";

    /// <summary>Gets the option token a mode is switched on with.</summary>
    /// <param name="mode">The mode name.</param>
    /// <returns>The token.</returns>
    public static string Option(string mode) => Modes[mode].Option;

    /// <summary>Gets a mode's label.</summary>
    /// <param name="mode">The mode name.</param>
    /// <returns>The label.</returns>
    public static string Label(string mode) => Modes[mode].Label();

    /// <summary>Gets a mode's explanation.</summary>
    /// <param name="mode">The mode name.</param>
    /// <returns>The explanation.</returns>
    public static string ToolTip(string mode) => Modes[mode].ToolTip();

    /// <summary>
    /// Gets the directory the formatter files are shipped in.
    /// </summary>
    /// <returns>The directory.</returns>
    /// <remarks>They are assets beside the application, exactly as upstream
    /// keeps them beside its <c>layoutcontrol</c> package.</remarks>
    public static string AssetDirectory()
        => Path.Combine(AppContext.BaseDirectory, "assets", "layoutcontrol");

    /// <summary>
    /// Builds the option list a layout-control run is configured with.
    /// </summary>
    /// <param name="modes">The modes that are switched on.</param>
    /// <param name="pointAndClick">Whether anchors are wanted.</param>
    /// <param name="customFile">A user's own formatter file, or null.</param>
    /// <param name="verbose">Whether the engine should be talkative.</param>
    /// <returns>The tokens, in upstream's order.</returns>
    /// <remarks>The include path and the settings file go on only when at
    /// least one mode is switched on, because an empty run must stay an
    /// ordinary run.</remarks>
    public static IReadOnlyList<string> PreviewOptions(
        IEnumerable<string> modes,
        bool pointAndClick = true,
        string customFile = null,
        bool verbose = false)
    {
        List<string> arguments = new List<string>();
        HashSet<string> chosen = new HashSet<string>(
            modes ?? Array.Empty<string>(), StringComparer.Ordinal);

        foreach (var mode in ModeList)
        {
            if (chosen.Contains(mode)) { arguments.Add(Option(mode)); }
        }

        if (!string.IsNullOrEmpty(customFile))
        {
            arguments.Add("-ddebug-custom-file=" + customFile);
        }

        if (arguments.Count > 0)
        {
            arguments.Insert(0, "-I" + AssetDirectory());
            arguments.Insert(1, "-dinclude-settings=debug-layout-options.ly");
        }

        arguments.Insert(0, pointAndClick ? "-dpoint-and-click" : "-dno-point-and-click");
        if (verbose) { arguments.Insert(0, "--verbose"); }

        return arguments;
    }
}
