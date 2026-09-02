// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;

namespace Fresco.Brix.Widgets; //was previously: frescobaldi/widgets/colorbutton.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A button that shows a colour and, when clicked, asks the user for a new one.
/// A button with no colour set shows nothing, which is how the Fonts &amp;
/// Colors page says "this style inherits its colour".
/// </summary>
/// <remarks>
/// Upstream draws the swatch in <c>paintEvent</c> with <c>qDrawShadeRect</c>
/// inside the button's content rectangle. Here the swatch is a
/// <see cref="Border"/> that IS the button's content, which draws the same
/// thing without a custom render pass — and paints on every head, which a
/// template override would have to be proved to do.
/// </remarks>
public sealed class ColorButton : Button
{
    private readonly Border _swatch;

    /// <summary>Creates a colour button with no colour set.</summary>
    public ColorButton()
    {
        _swatch = new Border
        {
            Width = 32,
            Height = 14,
            BorderThickness = new Thickness(1),
            //Qualified: this class has a property called Color, which shadows
            //the type inside it.
            BorderBrush = new SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 0x60, 0x60, 0x60)),
            Background = null,
        };

        Content = _swatch;
        Padding = new Thickness(4, 2, 4, 2);
        Click += async (_, _) => await OpenDialogAsync();
    }

    /// <summary>Raised after <see cref="Color"/> changes.</summary>
    /// <remarks>Upstream this is <c>colorChanged</c>.</remarks>
    public event EventHandler ColorChanged;

    /// <summary>
    /// Gets or sets the colour, or null for "unset" — upstream's invalid
    /// <c>QColor()</c>.
    /// </summary>
    public Color? Color
    {
        get;
        set
        {
            if (Nullable.Equals(field, value)) { return; }

            field = value;
            _swatch.Background = value == null
                ? null
                : new SolidColorBrush(value.Value);
            ColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets or sets the root the colour dialog attaches to. Falls back to the
    /// button's own <see cref="UIElement.XamlRoot"/> when unset.
    /// </summary>
    public XamlRoot DialogRoot { get; set; }

    /// <summary>Gets or sets whether the dialog offers an alpha channel.</summary>
    public bool AllowsAlpha { get; set; }

    /// <summary>Unsets the colour.</summary>
    /// <remarks>Upstream's <c>clear()</c>.</remarks>
    public void Clear() => Color = null;

    private async System.Threading.Tasks.Task OpenDialogAsync()
    {
        //Upstream opens on white when nothing is set, so the dialog always has
        //somewhere to start from.
        Color start = Color ?? Windows.UI.Color.FromArgb(255, 255, 255, 255);
        Color? picked = await InputDialogs.GetColorAsync(
            DialogRoot ?? XamlRoot, null, start, AllowsAlpha);
        if (picked != null) { Color = picked; }
    }
}
