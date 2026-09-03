// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Fresco.Brix.MusicView; //was previously: qpageview/viewactions.py (class ZoomerAction) + frescobaldi/pagedview.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>What one entry of the Music View toolbar's zoom chooser means.</summary>
public sealed class ZoomEntry
{
    /// <summary>Creates a fit-mode entry.</summary>
    /// <param name="mode">The mode.</param>
    /// <param name="caption">What the list shows.</param>
    public ZoomEntry(ViewMode mode, string caption)
    {
        Mode = mode;
        Caption = caption;
    }

    /// <summary>Creates a zoom-factor entry.</summary>
    /// <param name="factor">The factor, 1.0 being 100%.</param>
    /// <param name="caption">What the list shows.</param>
    public ZoomEntry(double factor, string caption)
    {
        Factor = factor;
        Caption = caption;
    }

    /// <summary>Gets the fit mode, or null when this entry is a factor.</summary>
    public ViewMode? Mode { get; }

    /// <summary>Gets the zoom factor, or null when this entry is a fit mode.</summary>
    public double? Factor { get; }

    /// <summary>Gets what the list shows.</summary>
    public string Caption { get; }
}

/// <summary>
/// The Music View toolbar's zoom chooser: the three fit modes on top of the
/// percentages, exactly as upstream's list is built.
/// </summary>
/// <remarks>
/// <para>
/// //was previously: nothing. The panel's own toolbar carried three plain
/// buttons — Width, Height and Page — where upstream carries this ONE control
/// (audit A GAP-26, EXTRA-03). The three modes are the first three entries of
/// this list, which is why moving them here loses nothing.
/// </para>
/// <para>
/// Upstream's <c>ZoomerAction</c> declares ten factors and Frescobaldi's own
/// <c>pagedview.ViewActions.createActions</c> then filters them to those no
/// larger than its view's maximum zoom of 8.0 — which drops 24.0 and 64.0 and
/// leaves eight. <see cref="MusicViewControl.MaxZoom"/> is the same 8.0, so
/// the same eight survive here and the filter is written the same way rather
/// than the answer being typed out.
/// </para>
/// <para>
/// ⚠ Upstream's combo box is <c>editable</c> with a READ-ONLY line edit
/// (<c>viewactions.py</c>: <c>w.setEditable(True)</c> then
/// <c>w.lineEdit().setReadOnly(True)</c>). That combination is a Qt idiom for
/// "a drop-down that can DISPLAY a value which is not in its list" — the user
/// picks from the list and cannot type — and it is what lets the box read
/// "137%" after a Ctrl+scroll. The behaviour ported here is that one, not a
/// text box: <see cref="CaptionFor"/> is how a factor outside the list is
/// shown.
/// </para>
/// </remarks>
public static class ZoomLevels
{
    /// <summary>
    /// Upstream's own zoom factors, before the view's maximum is applied.
    /// </summary>
    /// <remarks><c>ZoomerAction.__init__</c>'s <c>_zoomFactors</c>.</remarks>
    public static readonly IReadOnlyList<double> DeclaredFactors = new[]
    {
        0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 8.0, 24.0, 64.0,
    };

    /// <summary>The factors the chooser actually offers.</summary>
    /// <remarks>
    /// <c>DeclaredFactors</c> with everything above the view's maximum zoom
    /// dropped, which is what <c>pagedview.ViewActions.createActions</c> does.
    /// </remarks>
    public static readonly IReadOnlyList<double> Factors = DeclaredFactors
        .Where(factor => factor <= MusicViewControl.MaxZoom)
        .ToArray();

    /// <summary>Answers the caption a zoom factor is shown under.</summary>
    /// <param name="factor">The factor, 1.0 being 100%.</param>
    /// <returns>The caption.</returns>
    /// <remarks>
    /// Upstream's format string is <c>"{0:.0%}"</c> — python's percent
    /// presentation, which multiplies by a hundred, rounds to no decimal places
    /// and appends the sign. .NET's <c>P0</c> would put a space before the
    /// sign in most cultures and a different sign in some, so the two pieces
    /// are written out instead, in the invariant culture (board rule 7: casing
    /// and formatting in logic are invariant).
    /// </remarks>
    public static string CaptionFor(double factor)
        => Math.Round(factor * 100.0, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>Builds the chooser's entries, in order.</summary>
    /// <returns>The entries: the three fit modes, then the factors.</returns>
    /// <remarks>
    /// The three captions are qpageview's own msgids, each with the translator
    /// comment upstream gives it ("Width" as in "Fit Width", and so on); they
    /// are looked up with the same context Frescobaldi's catalogs carry.
    /// </remarks>
    public static IReadOnlyList<ZoomEntry> Entries()
    {
        List<ZoomEntry> entries = new List<ZoomEntry>
        {
            new ZoomEntry(ViewMode.FitWidth, I18n.Get("Width")),
            new ZoomEntry(ViewMode.FitHeight, I18n.Get("Height")),
            new ZoomEntry(ViewMode.FitBoth, I18n.Get("Page")),
        };

        foreach (double factor in Factors)
        {
            entries.Add(new ZoomEntry(factor, CaptionFor(factor)));
        }

        return entries;
    }

    /// <summary>
    /// Answers which entry is selected for a view mode and zoom factor.
    /// </summary>
    /// <param name="entries">The entries.</param>
    /// <param name="mode">The view's mode.</param>
    /// <param name="factor">The view's zoom factor.</param>
    /// <returns>
    /// The index, or -1 when the view is at a factor the list does not carry —
    /// which is when the box shows <see cref="CaptionFor"/> of that factor
    /// instead of selecting a row.
    /// </returns>
    /// <remarks>Upstream's <c>_adjustComboBox</c>: a fit mode wins over a
    /// factor, because in a fit mode the factor is whatever the window size
    /// made it.</remarks>
    public static int IndexFor(
        IReadOnlyList<ZoomEntry> entries, ViewMode mode, double factor)
    {
        if (entries == null) { return -1; }

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Mode == mode) { return i; }
        }

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Factor is { } value
                && Math.Abs(value - factor) < 0.0000005)
            {
                return i;
            }
        }

        return -1;
    }
}
