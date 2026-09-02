// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace Fresco.Brix.UserGuide; //was previously: frescobaldi/userguide/__init__.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The one place in the application that asks for a user-guide page.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>userguide.show(name)</c> and <c>userguide.addButton(box,
/// name)</c>, which are module-level functions over a module-level browser
/// window — so a static seam here is the faithful shape, not a shortcut. The
/// window sets <see cref="Show"/> once; every dialog that records a help
/// identifier then reaches the guide through this and through nothing else.
/// </para>
/// <para>
/// //was previously: <c>openWhatsThis(widget)</c>, which installs a Qt event
/// filter so a <c>help:</c> link inside a "What's This?" balloon opens the
/// browser. There is no What's This mode in this platform — the Help menu's
/// <c>help_whatsthis</c> entry is not ported either (see
/// <c>Shell/MenuBuilder.cs</c>) — and what the mechanism was FOR, reaching the
/// right guide page from the control in front of you, is what
/// <see cref="Button"/> and each dialog's recorded identifier do instead.
/// </para>
/// </remarks>
public static class GuideHelp
{
    /// <summary>
    /// Gets or sets how a page is put in front of the user; the window sets it
    /// once at start-up.
    /// </summary>
    public static Func<string, Task> Show { get; set; }

    /// <summary>Puts a page in front of the user.</summary>
    /// <param name="page">The page name, or null for the index.</param>
    /// <returns>The running task; nothing happens when no window is up.</returns>
    public static Task ShowAsync(string page = null)
        => Show?.Invoke(page) ?? Task.CompletedTask;

    /// <summary>
    /// Makes a Help button that opens the named page.
    /// </summary>
    /// <param name="page">The page name.</param>
    /// <returns>The button.</returns>
    /// <remarks>
    /// Upstream's <c>addButton</c> puts a standard Help button in a
    /// <c>QDialogButtonBox</c> and gives it the system help key. A
    /// <c>ContentDialog</c> carries three buttons and this application's
    /// dialogs have spent them (board trap 50), so the button goes INSIDE the
    /// content, beside whatever else the dialog puts there — and the help key
    /// is offered on the dialog itself.
    /// </remarks>
    public static Button Button(string page)
    {
        Button button = new Button { Content = I18n.Get("Help") };
        ToolTipService.SetToolTip(
            button, I18n.Get("Opens this window's page in the user guide (F1)."));
        button.Click += (_, _) => _ = ShowAsync(page);
        return button;
    }

    /// <summary>
    /// A right-aligned row holding nothing but the Help button, ready to be
    /// added to the bottom of a dialog's content.
    /// </summary>
    /// <param name="page">The user guide page it opens.</param>
    /// <returns>The row.</returns>
    /// <remarks>The same placement <c>Widgets/WidgetDialog</c> uses for its own
    /// <c>HelpPage</c>, factored out so the dialogs that are not built on that
    /// base put their Help button in the same place.</remarks>
    public static Microsoft.UI.Xaml.Controls.StackPanel ButtonRow(string page)
    {
        Microsoft.UI.Xaml.Controls.StackPanel row
            = new Microsoft.UI.Xaml.Controls.StackPanel
            {
                Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right,
            };
        row.Children.Add(Button(page));
        return row;
    }
}
