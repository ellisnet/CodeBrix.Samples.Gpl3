// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Editing;
using Fresco.Brix.Commands;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Windows.System;

namespace Fresco.Brix.Shell;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Makes the commands' keyboard shortcuts work anywhere in the window.
/// <para>
/// Upstream adds its QActions to the main window, so their shortcuts fire
/// wherever the focus is, whether or not the menu holding them has ever been
/// opened. Here the equivalent is to register the shortcuts on the window's
/// root element: a menu flyout's items are not in the visual tree until the
/// menu is first opened, so accelerators attached to THEM never fire.
/// </para>
/// <para>
/// The registration follows the commands: rebinding a shortcut in the
/// preferences, or a tool registering a command of its own later on, updates
/// what the keyboard does without anything being rebuilt.
/// </para>
/// <para>
/// An accelerator on the window only fires for a keystroke nothing below it
/// took first, and the editor takes plenty — <c>Alt+Return</c> reached it as a
/// plain Return and inserted a newline instead of opening the definition. So
/// every editor also gets a stacked input handler (<see cref="Attach"/>) that
/// offers a keystroke to the commands BEFORE the editor sees it, which is the
/// same first-refusal a QAction on the main window has.
/// </para>
/// </summary>
public sealed class ShortcutRegistrar
{
    private readonly UIElement _host;
    private readonly Dictionary<AppAction, List<KeyboardAccelerator>> _registered
        = new Dictionary<AppAction, List<KeyboardAccelerator>>();

    /// <summary>Creates the registrar over the window's root element.</summary>
    /// <param name="host">The element the shortcuts are registered on.</param>
    public ShortcutRegistrar(UIElement host)
        => _host = host ?? throw new ArgumentNullException(nameof(host));

    /// <summary>Registers every command in a collection.</summary>
    /// <param name="collection">The collection.</param>
    public void Register(ActionCollection collection)
    {
        if (collection == null) { return; }

        foreach (var action in collection.Actions.Values)
        {
            Register(action);
        }
    }

    /// <summary>Registers every command a manager knows about.</summary>
    /// <param name="manager">The manager.</param>
    public void RegisterAll(ActionCollectionManager manager)
    {
        foreach (var collection in manager?.Collections ?? Enumerable.Empty<ActionCollection>())
        {
            Register(collection);
        }
    }

    /// <summary>
    /// Gives an editor's key handling first refusal to the commands.
    /// </summary>
    /// <param name="textArea">The editor's text area.</param>
    public void Attach(TextArea textArea)
    {
        if (textArea == null) { return; }

        textArea.PushStackedInputHandler(new ShortcutInputHandler(this, textArea));
    }

    /// <summary>
    /// Reads which modifiers are actually held down.
    /// </summary>
    /// <param name="reported">What the key event said, used as a fallback.</param>
    /// <returns>The modifiers.</returns>
    /// <remarks>
    /// ⚠ The editor's key arguments report ALT AS SHIFT on the Skia heads —
    /// Alt+Return arrives as <c>Enter</c> with <c>Shift</c>, which is why a
    /// command bound to it silently became a newline. The keyboard source
    /// itself answers correctly, so the modifiers are read from there. (The
    /// window's own accelerators are unaffected; they resolve modifiers
    /// another way.)
    /// </remarks>
    public static VirtualKeyModifiers CurrentModifiers(VirtualKeyModifiers reported)
    {
        try
        {
            VirtualKeyModifiers actual = VirtualKeyModifiers.None;
            if (IsDown(VirtualKey.Control)) { actual |= VirtualKeyModifiers.Control; }

            if (IsDown(VirtualKey.Shift)) { actual |= VirtualKeyModifiers.Shift; }

            if (IsDown(VirtualKey.Menu)) { actual |= VirtualKeyModifiers.Menu; }

            if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
            {
                actual |= VirtualKeyModifiers.Windows;
            }

            return actual;
        }
        catch (Exception)
        {
            //A head with no keyboard source to ask falls back to what the
            //event said, which is right everywhere but for Alt.
            return reported;
        }
    }

    private static bool IsDown(VirtualKey key)
        => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            & Windows.UI.Core.CoreVirtualKeyStates.Down)
            == Windows.UI.Core.CoreVirtualKeyStates.Down;

    /// <summary>
    /// Triggers the command a keystroke is bound to.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifiers held down.</param>
    /// <returns>Whether a command took it.</returns>
    public bool Handle(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        //A plain keystroke, or one with only Shift, belongs to the editor: a
        //command bound to one of those would make typing impossible.
        if ((modifiers & (VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu
            | VirtualKeyModifiers.Windows)) == 0)
        {
            return false;
        }

        foreach (var pair in _registered)
        {
            AppAction action = pair.Key;
            if (!action.IsEnabled) { continue; }

            foreach (var shortcut in action.Shortcuts)
            {
                if (shortcut.Key != key || shortcut.Modifiers != modifiers)
                {
                    continue;
                }

                action.Trigger();
                return true;
            }
        }

        return false;
    }

    /// <summary>Registers one command, and keeps it registered.</summary>
    /// <param name="action">The command.</param>
    public void Register(AppAction action)
    {
        if (action == null || _registered.ContainsKey(action)) { return; }

        _registered[action] = new List<KeyboardAccelerator>();
        Refresh(action);

        //A rebound shortcut takes effect at once, which is what makes the
        //preferences page's shortcut editor work without a restart.
        action.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppAction.Shortcuts))
            {
                Refresh(action);
            }
        };
    }

    private void Refresh(AppAction action)
    {
        List<KeyboardAccelerator> existing = _registered[action];
        foreach (var accelerator in existing)
        {
            _host.KeyboardAccelerators.Remove(accelerator);
        }

        existing.Clear();

        foreach (var shortcut in action.Shortcuts)
        {
            KeyboardAccelerator accelerator = new KeyboardAccelerator
            {
                Key = shortcut.Key,
                Modifiers = shortcut.Modifiers,
            };

            accelerator.Invoked += (_, e) =>
            {
                //A disabled command must not swallow the keystroke: the editor
                //below may well have its own use for it.
                if (!action.IsEnabled) { return; }

                action.Trigger();
                e.Handled = true;
            };

            existing.Add(accelerator);
            _host.KeyboardAccelerators.Add(accelerator);
        }
    }

    /// <summary>
    /// The editor-side half: a keystroke is offered to the commands before the
    /// editor acts on it.
    /// </summary>
    private sealed class ShortcutInputHandler : TextAreaStackedInputHandler
    {
        private readonly ShortcutRegistrar _registrar;

        public ShortcutInputHandler(ShortcutRegistrar registrar, TextArea textArea)
            : base(textArea)
            => _registrar = registrar;

        public override bool OnPreviewKeyDown(
            VirtualKey key, VirtualKeyModifiers modifiers)
            => _registrar.Handle(key, CurrentModifiers(modifiers));
    }
}
