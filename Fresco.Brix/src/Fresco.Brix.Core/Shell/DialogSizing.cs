// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

namespace Fresco.Brix.Shell;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Keeps a dialog inside the window it opens in.
/// </summary>
/// <remarks>
/// <para>
/// Board trap 43: a <c>ContentDialog</c>'s width is the
/// <c>ContentDialogMaxWidth</c> RESOURCE, and something inside it must carry a
/// <c>MinWidth</c> or the content collapses. What that trap does NOT say — and
/// what this type is for — is that the dialog is also clipped by the window:
/// a design that asks for 1,180 pixels inside a 1,024-pixel window loses its
/// right-hand column, silently and in every language, because the number is a
/// constant rather than a measurement.
/// </para>
/// <para>
/// So the design size becomes a MAXIMUM, clamped at the moment the dialog is
/// shown to whatever the window actually has, and the content stretches into
/// what it got. The 48 pixels taken off cover the dialog's own chrome and the
/// smoke layer's margin.
/// </para>
/// </remarks>
public static class DialogSizing
{
    /// <summary>The margin left round a dialog inside its window.</summary>
    private const double Margin = 48;

    /// <summary>
    /// Clamps a dialog's maximum size to the window it will open in.
    /// </summary>
    /// <param name="dialog">The dialog, with its <c>XamlRoot</c> already set.</param>
    /// <param name="designWidth">The width the layout was designed for.</param>
    /// <param name="designHeight">The height the layout was designed for.</param>
    /// <remarks>Call it immediately before <c>ShowAsync</c>: the window's size
    /// is only knowable then, and it can change between one opening and the
    /// next.</remarks>
    public static void Clamp(
        ContentDialog dialog, double designWidth, double designHeight)
    {
        if (dialog?.XamlRoot == null) { return; }

        Size available = dialog.XamlRoot.Size;
        double width = available.Width > Margin
            ? Math.Min(designWidth, available.Width - Margin)
            : designWidth;
        double height = available.Height > Margin
            ? Math.Min(designHeight, available.Height - Margin)
            : designHeight;

        dialog.Resources["ContentDialogMaxWidth"] = width;
        dialog.Resources["ContentDialogMaxHeight"] = height;
    }

    /// <summary>
    /// The height a dialog's CONTENT may ask for in the window it will open in.
    /// </summary>
    /// <param name="xamlRoot">The root the dialog will attach to.</param>
    /// <param name="designHeight">The height the layout was designed for.</param>
    /// <returns>The height to give the content.</returns>
    /// <remarks>
    /// <see cref="Clamp"/> caps the DIALOG; a content element that still asks
    /// for a fixed height taller than what is left pushes the dialog's buttons
    /// off the bottom of the window. The chrome allowance covers the dialog's
    /// title row, its button row and the margins round both.
    /// </remarks>
    public static double ContentHeight(XamlRoot xamlRoot, double designHeight)
    {
        double available = xamlRoot?.Size.Height ?? 0;
        return available > Chrome
            ? Math.Min(designHeight, available - Chrome)
            : designHeight;
    }

    /// <summary>What a dialog's own title and button rows take.</summary>
    /// <remarks>
    /// MEASURED on the X11 head at 1024x768 rather than guessed: the smoke
    /// layer leaves the same 48 pixels <see cref="Clamp"/> allows for, and
    /// inside the dialog the title row, the button row and the content
    /// presenter's own padding take 160 more, so a content element may ask for
    /// the window height less 208. //was previously: 200, which was eight
    /// pixels too generous — enough to clip the last row of the Document Fonts
    /// dialog's left-hand column ("Restore Defaults" and "Help") at exactly
    /// that window size.
    /// </remarks>
    private const double Chrome = 208;
}
