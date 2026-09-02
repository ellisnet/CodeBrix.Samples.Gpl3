// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;
using Windows.UI;

namespace Fresco.Brix.Widgets; //was previously: frescobaldi/widgets/urlrequester.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>What a <see cref="UrlRequester"/> asks the user to pick.</summary>
public enum UrlRequesterMode
{
    /// <summary>A folder.</summary>
    Directory,

    /// <summary>A file that already exists.</summary>
    ExistingFile,

    /// <summary>Any file name, existing or not.</summary>
    AnyFile,
}

/// <summary>
/// A text box with a Browse button beside it: type a path, or pick one.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>UrlRequester</c>, with its two signals and its
/// <c>mustExist</c> colouring. The one difference is where the picker comes
/// from: a Qt widget opens its own <c>QFileDialog</c>, while here the HEAD owns
/// the picker, so the window fills in <see cref="PickAsync"/>. A requester with
/// no picker still works — the user types the path — which is what keeps the
/// frame-buffer head honest.
/// </para>
/// <para>
/// //was previously: <c>lineEdit</c> and <c>button</c> are public attributes
/// upstream, because callers reach in to attach a completer or take the focus.
/// The two callers that do are the documentation paths list (FR5.1 — gone) and
/// the general page's default-directory row, which needs neither, so the parts
/// stay private and the class exposes what is actually used.
/// </para>
/// </remarks>
public sealed class UrlRequester : Grid
{
    private readonly TextBox _box = new TextBox
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly Button _browse = new Button { Content = "…" };
    private readonly Brush _normalForeground;

    private string _originalPath = string.Empty;
    private bool _writing;

    /// <summary>Creates a requester.</summary>
    /// <param name="mode">What it picks.</param>
    /// <param name="mustExist">Whether a path that is not there is shown as
    /// wrong.</param>
    public UrlRequester(
        UrlRequesterMode mode = UrlRequesterMode.Directory, bool mustExist = false)
    {
        Mode = mode;
        MustExist = mustExist;
        _normalForeground = _box.Foreground;

        ColumnSpacing = 2;
        ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Children.Add(_box);
        SetColumn(_browse, 1);
        Children.Add(_browse);

        ToolTipService.SetToolTip(_browse, I18n.Get("Open file dialog"));

        _box.TextChanged += (_, _) => OnTextChanged();
        _box.LostFocus += (_, _) => OnEditingFinished();
        _browse.Click += async (_, _) => await BrowseAsync();
    }

    /// <summary>Raised whenever the text changes.</summary>
    public event EventHandler Changed;

    /// <summary>Raised when the user has finished entering a new path.</summary>
    public event EventHandler EditingFinished;

    /// <summary>Gets or sets what the requester picks.</summary>
    public UrlRequesterMode Mode { get; set; }

    /// <summary>
    /// Gets or sets whether only an existing path is accepted; one that is not
    /// there is shown in the error colour and reverted when focus leaves.
    /// </summary>
    public bool MustExist { get; set; }

    /// <summary>Gets or sets the title the picker shows, or null for the
    /// default.</summary>
    public string DialogTitle { get; set; }

    /// <summary>
    /// Gets or sets how the head opens a picker: given the mode and the current
    /// path, it answers the chosen path or null.
    /// </summary>
    /// <remarks>Left null on a head with no picker; the Browse button is then
    /// simply disabled and the box still takes a typed path.</remarks>
    public Func<UrlRequesterMode, string, Task<string>> PickAsync
    {
        get;
        set
        {
            field = value;
            _browse.IsEnabled = value != null;
        }
    }

    /// <summary>Gets or sets the tool tip shown over the text box.</summary>
    public string EntryToolTip
    {
        get;
        set
        {
            field = value;
            if (!string.IsNullOrEmpty(value)) { ToolTipService.SetToolTip(_box, value); }
        }
    }

    /// <summary>Gets or sets the path, without raising
    /// <see cref="EditingFinished"/>.</summary>
    public string Path
    {
        get => _box.Text;
        set
        {
            _originalPath = value ?? string.Empty;
            _writing = true;
            _box.Text = _originalPath;
            _writing = false;
            Colorize();
        }
    }

    private void OnTextChanged()
    {
        Colorize();
        if (_writing) { return; }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Colorize()
    {
        if (!MustExist)
        {
            _box.Foreground = _normalForeground;
            return;
        }

        bool there = !string.IsNullOrEmpty(Path)
            && (System.IO.File.Exists(Path) || System.IO.Directory.Exists(Path));

        //Upstream sets a red style sheet with a TODO asking for the theme's own
        //error colour; the Fonts & Colors scheme HAS one, and the port uses it.
        _box.Foreground = there
            ? _normalForeground
            : new SolidColorBrush(Color.FromArgb(255, 0xC0, 0x30, 0x30));
    }

    private void OnEditingFinished()
    {
        if (MustExist
            && !string.IsNullOrEmpty(Path)
            && !System.IO.File.Exists(Path)
            && !System.IO.Directory.Exists(Path))
        {
            Path = _originalPath;
            return;
        }

        if (string.Equals(Path, _originalPath, StringComparison.Ordinal)) { return; }

        _originalPath = Path;
        EditingFinished?.Invoke(this, EventArgs.Empty);
    }

    private async Task BrowseAsync()
    {
        Func<UrlRequesterMode, string, Task<string>> pick = PickAsync;
        if (pick == null) { return; }

        string chosen = await pick(Mode, Path);
        if (string.IsNullOrEmpty(chosen)) { return; }

        _box.Text = chosen;
        OnEditingFinished();
    }
}
