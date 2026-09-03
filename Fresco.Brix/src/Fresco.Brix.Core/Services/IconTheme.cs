// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.QuickInsert;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using Windows.UI;

namespace Fresco.Brix.Services; //was previously: frescobaldi/icons/__init__.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Which of the two icon sets a window is drawing with.</summary>
public enum IconSet
{
    /// <summary>The set drawn for a light background.</summary>
    Light,

    /// <summary>The set drawn for a dark background.</summary>
    Dark,
}

/// <summary>
/// The two window toolbars' icons: Frescobaldi's own Light and Dark sets,
/// chosen by the platform's theme and drawn in the theme's foreground colour.
/// </summary>
/// <remarks>
/// <para>
/// //was previously: nothing — there were no icons in the application at all,
/// which is why the toolbars themselves were once recorded as post-v1 work.
/// Ruling FR16 (Jeremy, 2026-09-02) brings both bars into v1 with upstream's
/// own artwork.
/// </para>
/// <para>
/// Upstream picks the set by comparing its palette's window colour with its
/// window-text colour — <c>icons/__init__.py update_theme()</c> — and re-picks
/// it whenever Qt sends <c>ApplicationPaletteChange</c>
/// (<c>icons/change_theme_eventhandler.py</c>). The same rule here reads the
/// platform's own answer to the same question, <c>FrameworkElement.ActualTheme</c>,
/// and re-reads it on <c>ActualThemeChanged</c>.
/// </para>
/// <para>
/// ⚠ Upstream's rule is guarded by its <c>system_icons</c> preference, which
/// switches to the classic TangoExt look when it is off. Ruling FR16 ships the
/// Light/Dark sets only at v1; the checkbox and the second set are recorded
/// post-v1, so there is nothing to guard and no preference is read here.
/// </para>
/// <para>
/// The icons are <c>EmbeddedResource</c>s of this assembly, put there by
/// <c>tools/iconclean</c>; see <c>assets/icons/README-frescobaldi-icons.txt</c>
/// for where they came from, what licenses them and which one file diverges
/// from upstream under ruling FR14.
/// </para>
/// </remarks>
public static class IconTheme
{
    /// <summary>The resource prefix the light set is embedded under.</summary>
    public const string LightPrefix = "Fresco.Brix.Icons.Light.";

    /// <summary>The resource prefix the dark set is embedded under.</summary>
    public const string DarkPrefix = "Fresco.Brix.Icons.Dark.";

    /// <summary>
    /// The size a toolbar button's icon is drawn at, in pixels.
    /// </summary>
    /// <remarks>
    /// Qt's default toolbar icon size on this platform is 24, which is also the
    /// <c>viewBox</c> every one of these files declares, so a Tabler icon draws
    /// at its own scale with no resampling. The Quick Insert panel's glyphs are
    /// 22 for the same reason — that is the size upstream draws them at.
    /// </remarks>
    public const int ToolbarIconSize = 24;

    /// <summary>
    /// The one file that is NOT a byte-for-byte copy of upstream's (FR14).
    /// </summary>
    /// <remarks>
    /// ⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14). Upstream's
    /// <c>tools-score-wizard.svg</c> is 6,474,769 bytes (Light) and 6,474,783
    /// (Dark) — 228,519 lines, of which 20,760 are orphan
    /// <c>&lt;inkscape:path-effect&gt;</c> elements an Inkscape session left in
    /// <c>&lt;defs&gt;</c> — for a 24-pixel icon whose whole drawing is 7
    /// paths, 2 rectangles and 28 groups. <c>tools/iconclean/iconclean.py</c>
    /// removes them (they are in Inkscape's own namespace, which an SVG
    /// renderer must ignore, so the drawing cannot change) and the shipped
    /// files are 2,761 and 2,742 bytes.
    /// <c>IconThemeTests.the_cleaned_wizard_icon_draws_what_upstream_draws</c>
    /// proves the pixels through this application's own renderer.
    /// </remarks>
    public const string DivergentIconName = "tools-score-wizard";

    /// <summary>
    /// Every icon name the toolbars reference, in the order the bars use them.
    /// </summary>
    /// <remarks>//was previously: "the two window toolbars", which was the
    /// whole list until the Manuscript Viewer's own panel toolbar arrived with
    /// board wave W15.</remarks>
    /// <remarks>
    /// This is the list <c>tools/iconclean</c> copies and the list the tests
    /// walk. Both sets carry every one of them.
    /// </remarks>
    public static readonly IReadOnlyList<string> Names = new[]
    {
        //The Main Toolbar
        "document-new",
        "document-open",
        "document-save",
        "document-close",
        "go-previous",
        "go-next",
        "edit-undo",
        "edit-redo",
        "tools-score-wizard",
        "lilypond-run",
        "lilypond-stop",

        //The Music View Toolbar (go-previous and go-next again, for the pages)
        "zoom-in",
        "zoom-out",
        "zoom-magnifier",
        "edit-clear",

        //The Manuscript Viewer's PANEL toolbar (board wave W15, ruling FR17).
        //Its other buttons reuse the names above; these four are the ones no
        //window toolbar referenced.
        "help-contents",
        "reload",
        "rotate-left",
        "rotate-right",
    };

    /// <summary>Answers which set a theme asks for.</summary>
    /// <param name="theme">The theme the element resolved to.</param>
    /// <returns>The set.</returns>
    /// <remarks>
    /// Upstream's own rule, in the platform's own terms: the set drawn for a
    /// dark background when the background IS dark, the other one otherwise.
    /// <c>ElementTheme.Default</c> cannot occur on an
    /// <c>ActualTheme</c> — the platform has already resolved it — and is
    /// treated as light, which is what a palette with no answer looks like.
    /// </remarks>
    public static IconSet SetFor(ElementTheme theme)
        => theme == ElementTheme.Dark ? IconSet.Dark : IconSet.Light;

    /// <summary>Answers the resource prefix a set is embedded under.</summary>
    /// <param name="set">The set.</param>
    /// <returns>The prefix.</returns>
    public static string PrefixFor(IconSet set)
        => set == IconSet.Dark ? DarkPrefix : LightPrefix;

    /// <summary>Answers the colour icons are drawn in under a theme.</summary>
    /// <param name="theme">The theme the element resolved to.</param>
    /// <returns>The colour.</returns>
    /// <remarks>
    /// The same two colours the Quick Insert panel uses for its glyphs, for the
    /// same reason: upstream's two sets encode the foreground as a literal
    /// black (Light) or white (Dark), and recolouring to the theme's own
    /// foreground is how that intent survives a renderer that resolves
    /// <c>currentColor</c> to nothing in particular.
    /// </remarks>
    public static Color ForegroundFor(ElementTheme theme)
        => theme == ElementTheme.Dark
            ? Color.FromArgb(0xff, 0xe8, 0xe8, 0xe8)
            : Color.FromArgb(0xff, 0x10, 0x10, 0x10);

    /// <summary>Answers whether an icon exists in a set.</summary>
    /// <param name="set">The set.</param>
    /// <param name="name">The icon name, without its extension.</param>
    /// <returns>Whether it does.</returns>
    public static bool Has(IconSet set, string name)
        => SymbolIcons.Has(PrefixFor(set), name);

    /// <summary>Renders an icon into a bitmap.</summary>
    /// <param name="theme">The theme the element resolved to.</param>
    /// <param name="name">The icon name, without its extension.</param>
    /// <param name="size">The square size in pixels.</param>
    /// <returns>The bitmap, or null when there is no such icon.</returns>
    public static WriteableBitmap Bitmap(
        ElementTheme theme, string name, int size = ToolbarIconSize)
        => SymbolIcons.Bitmap(
            PrefixFor(SetFor(theme)), name, ForegroundFor(theme), size);

    /// <summary>Makes an image showing an icon.</summary>
    /// <param name="theme">The theme the element resolved to.</param>
    /// <param name="name">The icon name, without its extension.</param>
    /// <param name="size">The square size in pixels.</param>
    /// <returns>The image, or null when there is no such icon.</returns>
    public static Image Image(
        ElementTheme theme, string name, int size = ToolbarIconSize)
        => SymbolIcons.Icon(
            PrefixFor(SetFor(theme)), name, ForegroundFor(theme), size);

    /// <summary>
    /// Calls back whenever the element's resolved theme changes, and answers
    /// how to stop.
    /// </summary>
    /// <param name="element">The element whose theme is followed.</param>
    /// <param name="changed">What to do, given the new theme.</param>
    /// <returns>An action that unsubscribes.</returns>
    /// <remarks>
    /// Upstream installs an event filter for Qt's
    /// <c>ApplicationPaletteChange</c>; this is the platform's own equivalent.
    /// Whether the Linux heads ever RAISE it when the desktop's colour scheme
    /// changes is a platform question, answered on X11 at wave W14 and recorded
    /// in the wave's STATUS file; the icons are correct at launch either way.
    /// </remarks>
    public static Action Follow(FrameworkElement element, Action<ElementTheme> changed)
    {
        if (element == null || changed == null) { return () => { }; }

        void OnThemeChanged(FrameworkElement sender, object args)
            => changed(sender.ActualTheme);

        element.ActualThemeChanged += OnThemeChanged;
        return () => element.ActualThemeChanged -= OnThemeChanged;
    }
}
