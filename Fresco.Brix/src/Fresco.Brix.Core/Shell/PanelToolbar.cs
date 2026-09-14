// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.CommandBar;
using Fresco.Brix.Commands;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Fresco.Brix.Shell;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The pieces a dock panel's own toolbar is made of: the bar and the buttons
/// on it.
/// </summary>
/// <remarks>
/// <para>
/// The window's two bars live side by side in a <see cref="ToolBarTray"/> and
/// <see cref="MainToolbar"/> builds them; a panel's bar is a single
/// <see cref="ToolBar"/> in the panel's own layout. What the two have in common
/// — the presentation settings, a button over an <see cref="AppAction"/>, and
/// the rule about who writes a tool tip — is here, so each panel says only what
/// is on its bar and in what order.
/// </para>
/// <para>
/// Upstream keeps the same split: <c>viewers/toolbar.py</c> is an abstract
/// toolbar whose <c>populate</c> the concrete viewer configures, and the
/// documentation browser builds its own <c>QToolBar</c> inline.
/// </para>
/// <para>
/// A panel toolbar used to be two rows — a chooser row and a button row inside
/// a horizontally scrolling viewer with its scrollbar hidden — because a dock
/// panel's toolbar had no overflow chevron and sixteen controls simply ran off
/// the edge of a narrow dock (board trap 57). The CommandBar add-in's
/// <see cref="OverflowMode.Chevron"/> IS that chevron, so the scrolling row is
/// gone and each bar is one real toolbar again, which is what upstream has.
/// </para>
/// <para>
/// A panel bar needs NO width rule of its own. <see cref="MainToolbar"/> has to
/// cap its tray at its host's arranged width, because a
/// <see cref="ContentControl"/> measures its content with an unbounded width
/// and a tray offered infinite room never wraps and never chevrons (the package
/// fixlist of 2026-09-13). MEASURED on the X11 head, 2026-09-14: a bar in a
/// dock strip does not have that problem — it is measured against the strip's
/// real width, grows its chevron at the strip's default 324 px and gives every
/// item back when the strip is widened, with no cap anywhere. The same cap was
/// written here first and then removed once it was measured to do nothing.
/// </para>
/// </remarks>
internal static class PanelToolbar
{
    /// <summary>Creates an empty panel toolbar.</summary>
    /// <param name="title">
    /// The bar's name — the panel's own title. It is the bar's accessibility
    /// name and the tail of each button's accessible name ("Reload, Manuscript"),
    /// which is what the panels used to say through
    /// <c>AutomationProperties.SetHelpText</c>.
    /// </param>
    /// <param name="labels">Whether the bar draws icons, text, or both.</param>
    /// <returns>The bar, with nothing on it yet.</returns>
    /// <remarks>
    /// The presentation settings are INHERITED attached properties, so setting
    /// them on the bar settles every button on it and any one button can still
    /// override its own. <see cref="OverflowMode"/> is left at its default,
    /// <see cref="OverflowMode.Chevron"/>.
    /// </remarks>
    internal static ToolBar Create(string title, LabelMode labels)
    {
        ToolBar bar = new ToolBar { Title = title ?? string.Empty };
        ToolBarProperties.SetIconSize(bar, IconTheme.ToolbarIconSize);
        ToolBarProperties.SetLabelMode(bar, labels);
        ToolBarProperties.SetShowToolTips(bar, true);
        return bar;
    }

    /// <summary>Builds a button whose label IS its command's name.</summary>
    /// <param name="action">The command the button fires.</param>
    /// <returns>The button.</returns>
    /// <remarks>
    /// The add-in composes the tool tip from the two strings handed over here —
    /// "Reload (Ctrl+R)" — which is the wording
    /// <see cref="ToolbarLayout.ToolTipFor"/> writes. An action carrying a tool
    /// tip of its OWN is not describing its label, so the application sets that
    /// one and the add-in leaves it alone; once it has, the flag stays on and
    /// the application keeps saying the whole tip.
    /// </remarks>
    internal static ToolButton ButtonFor(AppAction action)
    {
        ToolButton button = New(action);
        if (action == null) { return button; }

        bool applicationOwnsToolTip = false;

        void Update()
        {
            button.Text = MenuBuilder.Display(action.Text);
            button.Shortcut = action.Shortcuts.Count > 0
                ? action.Shortcuts[0].ToString()
                : null;

            //A name no set ships hands back nothing at all, which is what makes
            //the add-in show the button's text rather than draw a blank square.
            button.Icon = IconTheme.Source(action.IconName);

            if (applicationOwnsToolTip || !string.IsNullOrEmpty(action.ToolTip))
            {
                applicationOwnsToolTip = true;
                ToolTipService.SetToolTip(button, ToolbarLayout.ToolTipFor(action));
            }

            ShowChecked(button, action);
        }

        Update();
        action.PropertyChanged += (_, _) => Update();
        return button;
    }

    /// <summary>
    /// Builds a button whose label is a caption of the panel's own rather than
    /// its command's name.
    /// </summary>
    /// <param name="action">The command the button fires, or null.</param>
    /// <param name="caption">The caption the button shows.</param>
    /// <returns>The button.</returns>
    /// <remarks>
    /// A caption such as "&lt;&lt;" is not what the command is called, so the
    /// add-in must not compose a tool tip out of it: the application sets the
    /// whole tip, in <see cref="ToolbarLayout.ToolTipFor"/>'s wording, and the
    /// add-in leaves a tip it did not compose alone.
    /// </remarks>
    internal static ToolButton ButtonFor(AppAction action, string caption)
    {
        if (action == null) { return Button(caption, caption, null); }

        ToolButton button = New(action);

        void Update()
        {
            button.Text = caption;
            ToolTipService.SetToolTip(button, ToolbarLayout.ToolTipFor(action));
            ShowChecked(button, action);
        }

        Update();
        action.PropertyChanged += (_, _) => Update();
        return button;
    }

    /// <summary>Builds a button that has no command behind it.</summary>
    /// <param name="caption">The caption the button shows.</param>
    /// <param name="tip">What the button says it does.</param>
    /// <param name="click">What a click does, or null.</param>
    /// <returns>The button.</returns>
    /// <remarks>The Documentation Browser's view buttons are the panel's own
    /// rather than upstream's, and drive the view directly; there is no
    /// <see cref="AppAction"/> to name them, so the caption and the tip are
    /// written here.</remarks>
    internal static ToolButton Button(string caption, string tip, Action click)
    {
        ToolButton button = new ToolButton { Text = caption };
        ToolTipService.SetToolTip(button, tip);
        if (click != null) { button.Click += (_, _) => click(); }

        return button;
    }

    /// <summary>Makes the button an action asks for, bound to it.</summary>
    /// <param name="action">The command, or null.</param>
    /// <returns>The button.</returns>
    /// <remarks>
    /// IsEnabled is NOT set: the button follows the command's
    /// <c>CanExecute</c>, which <see cref="AppAction"/> answers from its own
    /// IsEnabled and re-raises on every change. An explicit IsEnabled would
    /// outrank the command for good.
    /// </remarks>
    private static ToolButton New(AppAction action)
    {
        if (action == null) { return new ToolButton(); }

        ToolButton button = action.IsCheckable
            ? new ToolToggleButton { IsChecked = action.IsChecked }
            : new ToolButton();
        button.Command = action;
        return button;
    }

    /// <summary>Shows a checkable action's state on its toggle.</summary>
    /// <param name="button">The button.</param>
    /// <param name="action">The command behind it.</param>
    /// <remarks>
    /// A click flips the toggle's own state and, separately, runs the command,
    /// which flips the action's — one flip each, ending in step. The action is
    /// never written from here, which is what keeps the two from cancelling
    /// each other out.
    /// </remarks>
    private static void ShowChecked(ToolButton button, AppAction action)
    {
        if (button is ToolToggleButton box) { box.IsChecked = action.IsChecked; }
    }
}
