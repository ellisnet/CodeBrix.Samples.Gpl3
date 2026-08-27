// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Ly;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using MusicTree = Fresco.Brix.Ly.Music.Document;

namespace Fresco.Brix.MusicView; //was previously: frescobaldi/musicpos.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Shows, on a pane's status bar, how far into the music the caret is — or how
/// long the selection lasts.
/// </summary>
/// <remarks>
/// <para>
/// The answer is a musical duration, not a number of characters: "Pos: 3/4"
/// means three quarter notes from the start of the piece. Working it out means
/// parsing the whole document into a music tree, which is why upstream waits
/// 100 ms after the caret stops moving before asking, and why this does too.
/// </para>
/// </remarks>
public sealed class MusicPosition
{
    private readonly TextBlock _label = new TextBlock { Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer _timer
        = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };

    private EditorView _view;

    /// <summary>Puts a position display on a pane's status bar.</summary>
    /// <param name="space">The pane.</param>
    public MusicPosition(ViewSpace space)
    {
        if (space == null) { throw new ArgumentNullException(nameof(space)); }

        //Upstream inserts it after the caret position, before the file name.
        space.StatusBar.Children.Insert(
            Math.Min(1, space.StatusBar.Children.Count), _label);

        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Update();
        };

        space.ViewChanged += (_, _) => SetView(space.ActiveView);
        SetView(space.ActiveView);
    }

    /// <summary>Gets the label, so a head can restyle it.</summary>
    public TextBlock Label => _label;

    private void SetView(EditorView view)
    {
        if (ReferenceEquals(view, _view)) { return; }

        if (_view != null) { _view.CursorPositionChanged -= OnCursorMoved; }

        _view = view;
        if (_view != null) { _view.CursorPositionChanged += OnCursorMoved; }

        Restart();
    }

    private void OnCursorMoved(object sender, EventArgs e) => Restart();

    private void Restart()
    {
        if (_view?.Document == null || _view.Document.IsChanging) { return; }

        _timer.Stop();
        _timer.Start();
    }

    private void Update()
    {
        EditorView view = _view;
        if (view?.Document == null)
        {
            Hide();
            return;
        }

        MusicTree music;
        try
        {
            music = DocumentInfo.For(view.Document).Music();
        }
        catch (InvalidOperationException)
        {
            Hide();
            return;
        }

        if (music == null)
        {
            Hide();
            return;
        }

        int start = view.Editor.SelectionStart;
        int length = view.Editor.SelectionLength;
        Fraction? value = length > 0
            ? music.TimeLength(start, start + length)
            : music.TimePosition(view.Editor.CaretOffset);

        if (value == null)
        {
            Hide();
            return;
        }

        _label.Text = length > 0
            ? I18n.Format(I18n.Get("Length: {length}"), ("length", Durations.FormatFraction(value.Value)))
            : I18n.Format(I18n.Get("Pos: {pos}"), ("pos", Durations.FormatFraction(value.Value)));
        _label.Visibility = Visibility.Visible;
    }

    private void Hide()
    {
        _label.Text = string.Empty;
        _label.Visibility = Visibility.Collapsed;
    }
}
