// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.System;

namespace Fresco.Brix.Widgets; //was previously: frescobaldi/widgets/keysequencewidget.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A box the user presses a key combination into: click it, press the keys,
/// and it shows what it recorded. The small button beside it empties it again.
/// </summary>
/// <remarks>
/// <para>
/// Upstream (whose own comment credits KDE's <c>kkeysequencewidget.cpp</c>)
/// records up to FOUR chords into one <c>QKeySequence</c>, ending the recording
/// on a 600&#160;ms timer after the modifiers are let go.
/// <see cref="KeySequence"/> is deliberately ONE chord — Frescobaldi never
/// writes a multi-chord shortcut, and where it wants alternatives it gives the
/// action a LIST — so recording here ends at the first real key instead, and
/// there is no timer to leave running.
/// </para>
/// <para>
/// ⚠ The modifiers are read from the keyboard source rather than from the key
/// arguments: on the Skia heads ALT is reported as SHIFT (board trap 38), and a
/// widget that recorded Shift+Return for Alt+Return would write a shortcut the
/// user never asked for.
/// </para>
/// </remarks>
public sealed class KeySequenceWidget : Grid
{
    private readonly Button _button = new Button
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Left,
        MinWidth = 160,
    };

    private readonly Button _clearButton = new Button { Content = "✕" };

    private bool _isRecording;

    /// <summary>Creates the widget.</summary>
    /// <param name="number">Which alternative it edits — upstream's
    /// <c>num</c>, reported back with <see cref="ShortcutChanged"/>.</param>
    public KeySequenceWidget(int number = 0)
    {
        Number = number;
        ColumnSpacing = 2;
        ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Children.Add(_button);
        SetColumn(_clearButton, 1);
        Children.Add(_clearButton);

        ToolTipService.SetToolTip(_button, I18n.Get("Start recording a key sequence."));
        ToolTipService.SetToolTip(_clearButton, I18n.Get("Clear the key sequence."));

        _button.Click += (_, _) => StartRecording();
        _button.KeyDown += OnKeyDown;
        _button.LostFocus += (_, _) => CancelRecording();
        _clearButton.Click += (_, _) => Clear();

        UpdateDisplay();
    }

    /// <summary>Raised when the recorded shortcut changed.</summary>
    /// <remarks>Upstream this is <c>keySequenceChanged(int)</c>; the number is
    /// <see cref="Number"/>.</remarks>
    public event EventHandler<int> ShortcutChanged;

    /// <summary>Gets which alternative this widget edits.</summary>
    public int Number { get; }

    /// <summary>Gets or sets the shortcut shown, or null for none.</summary>
    public KeySequence Shortcut
    {
        get;
        set
        {
            field = value;
            UpdateDisplay();
        }
    }

    /// <summary>
    /// Gets or sets whether a key with no modifier may be recorded. Off by
    /// default, as upstream's is.
    /// </summary>
    public bool IsModifierlessAllowed { get; set; }

    /// <summary>Gets whether the widget is waiting for keys.</summary>
    public bool IsRecording => _isRecording;

    /// <summary>Empties the shortcut.</summary>
    public void Clear()
    {
        CancelRecording();
        if (Shortcut == null) { return; }

        Shortcut = null;
        ShortcutChanged?.Invoke(this, Number);
    }

    /// <summary>
    /// Decides whether a keystroke is a shortcut this widget may record, and
    /// answers it; null when it is not.
    /// </summary>
    /// <param name="key">The key pressed.</param>
    /// <param name="modifiers">The modifiers actually held down.</param>
    /// <param name="modifierlessAllowed">Whether a bare key counts.</param>
    /// <returns>The shortcut, or null.</returns>
    /// <remarks>
    /// Upstream's <c>keyPressEvent</c> test, as a function so it can be tested
    /// without a keyboard: a modifier on its own is never a shortcut, and a
    /// plain (or merely shifted) character key is one only for the keys that
    /// cannot be typed into a document anyway.
    /// </remarks>
    public static KeySequence Record(
        VirtualKey key, VirtualKeyModifiers modifiers, bool modifierlessAllowed = false)
    {
        if (IsModifierKey(key)) { return null; }

        bool hasRealModifier = (modifiers & (VirtualKeyModifiers.Control
            | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Windows)) != 0;

        if (!modifierlessAllowed && !hasRealModifier && !IsAlwaysAllowed(key))
        {
            return null;
        }

        return new KeySequence(key, modifiers);
    }

    private static bool IsModifierKey(VirtualKey key)
        => key is VirtualKey.Shift or VirtualKey.Control or VirtualKey.Menu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows
            or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.Application or VirtualKey.CapitalLock;

    /// <summary>
    /// The keys upstream lets through with no modifier at all, plus the
    /// function keys — a shortcut on one of these takes nothing away from
    /// typing.
    /// </summary>
    private static bool IsAlwaysAllowed(VirtualKey key)
        => key is VirtualKey.Enter or VirtualKey.Space or VirtualKey.Tab
            or VirtualKey.Back or VirtualKey.Delete or VirtualKey.Escape
            or VirtualKey.Insert or VirtualKey.Home or VirtualKey.End
            or VirtualKey.PageUp or VirtualKey.PageDown
            || (key >= VirtualKey.F1 && key <= VirtualKey.F24);

    private void StartRecording()
    {
        _isRecording = true;
        _button.Focus(FocusState.Programmatic);
        UpdateDisplay();
    }

    private void CancelRecording()
    {
        if (!_isRecording) { return; }

        _isRecording = false;
        UpdateDisplay();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!_isRecording) { return; }

        //Every keystroke belongs to the recording while it is running, Tab
        //included — otherwise the focus moves away mid-shortcut.
        args.Handled = true;

        VirtualKeyModifiers modifiers = ShortcutRegistrar.CurrentModifiers(
            VirtualKeyModifiers.None);
        KeySequence recorded = Record(args.Key, modifiers, IsModifierlessAllowed);
        if (recorded == null)
        {
            //A modifier on its own: keep waiting, and show what is held.
            UpdateDisplay(modifiers);
            return;
        }

        _isRecording = false;
        Shortcut = recorded;
        ShortcutChanged?.Invoke(this, Number);
    }

    private void UpdateDisplay(VirtualKeyModifiers held = VirtualKeyModifiers.None)
    {
        if (!_isRecording)
        {
            _button.Content = Shortcut?.ToString() ?? string.Empty;
            return;
        }

        _button.Content = (held == VirtualKeyModifiers.None
            ? I18n.Get("Input")
            : ModifierText(held)) + " ...";
    }

    /// <summary>Names the modifiers currently held, the way a shortcut spells
    /// them.</summary>
    /// <param name="modifiers">The modifiers.</param>
    /// <returns>The text, e.g. <c>Ctrl+Shift</c>.</returns>
    private static string ModifierText(VirtualKeyModifiers modifiers)
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        void Add(VirtualKeyModifiers modifier, string name)
        {
            if ((modifiers & modifier) != modifier) { return; }

            if (text.Length > 0) { text.Append('+'); }

            text.Append(name);
        }

        //The order shortcuts are written in, which is KeySequence's own.
        Add(VirtualKeyModifiers.Control, "Ctrl");
        Add(VirtualKeyModifiers.Shift, "Shift");
        Add(VirtualKeyModifiers.Menu, "Alt");
        Add(VirtualKeyModifiers.Windows, "Meta");
        return text.ToString();
    }
}
