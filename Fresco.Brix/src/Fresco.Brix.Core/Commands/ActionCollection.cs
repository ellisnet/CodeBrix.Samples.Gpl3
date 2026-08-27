// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Commands; //was previously: frescobaldi/actioncollection.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A named group of <see cref="AppAction"/>s whose keyboard shortcuts are
/// remembered: the defaults come from <see cref="CreateActions"/>, the user's
/// changes are stored per shortcut scheme, and
/// <see cref="TranslateUI"/> (re)sets every text when the language changes.
/// </summary>
/// <remarks>
/// Upstream splits this into <c>ActionCollectionBase</c> and
/// <c>ActionCollection</c>, the split existing only so <c>ShortcutCollection</c>
/// can share the settings plumbing. <c>ShortcutCollection</c> — shortcuts for
/// user-created things like snippets and sessions, whose real actions may not
/// be loaded yet — belongs to those tools and is ported with them (W5), so the
/// two halves are one class here.
/// </remarks>
public abstract class ActionCollection
{
    private readonly Dictionary<string, AppAction> _actions
        = new Dictionary<string, AppAction>(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<KeySequence>> _defaults
        = new Dictionary<string, IReadOnlyList<KeySequence>>(StringComparer.Ordinal);
    private readonly SettingsStore _settings;

    /// <summary>Creates the collection and its actions.</summary>
    /// <param name="name">The collection name, e.g. <c>main</c>.</param>
    /// <param name="settings">The store shortcuts are remembered in, or null
    /// to keep them for this run only.</param>
    protected ActionCollection(string name, SettingsStore settings)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _settings = settings;
    }

    /// <summary>Gets the collection name.</summary>
    public string Name { get; }

    /// <summary>
    /// Gets the title the shortcut-settings page groups the actions under, or
    /// null when the actions do not group.
    /// </summary>
    public virtual string Title => null;

    /// <summary>Gets the actions, by name.</summary>
    public IReadOnlyDictionary<string, AppAction> Actions => _actions;

    /// <summary>Gets an action by name, or null.</summary>
    /// <param name="name">The action name.</param>
    /// <returns>The action, or null.</returns>
    public AppAction Action(string name)
        => name != null && _actions.TryGetValue(name, out var action) ? action : null;

    /// <summary>Gets the shortcuts an action currently has.</summary>
    /// <param name="name">The action name.</param>
    /// <returns>The shortcuts; empty when the action is unknown.</returns>
    public IReadOnlyList<KeySequence> Shortcuts(string name)
        => Action(name)?.Shortcuts ?? Array.Empty<KeySequence>();

    /// <summary>Gets the shortcuts an action was created with.</summary>
    /// <param name="name">The action name.</param>
    /// <returns>The defaults; empty when it had none.</returns>
    public IReadOnlyList<KeySequence> DefaultShortcuts(string name)
        => name != null && _defaults.TryGetValue(name, out var shortcuts)
            ? shortcuts
            : Array.Empty<KeySequence>();

    /// <summary>
    /// Sets an action's shortcuts and remembers the change — or forgets it
    /// again when the new list IS the default, so a later change of default
    /// reaches users who never customised it.
    /// </summary>
    /// <param name="name">The action name.</param>
    /// <param name="shortcuts">The shortcuts; empty removes them all.</param>
    public void SetShortcuts(string name, IReadOnlyList<KeySequence> shortcuts)
    {
        AppAction action = Action(name);
        if (action == null) { return; }

        shortcuts ??= Array.Empty<KeySequence>();
        action.Shortcuts = shortcuts;
        if (_settings == null) { return; }

        IReadOnlyList<KeySequence> defaults = DefaultShortcuts(name);
        if (SameShortcuts(shortcuts, defaults))
        {
            _settings.Remove(SettingKey(name));
        }
        else if (shortcuts.Count == 0 && defaults.Count == 0)
        {
            _settings.Remove(SettingKey(name));
        }
        else
        {
            //An empty list is stored deliberately: it means "the user removed
            //the default", which is not the same as "never customised".
            _settings.SetString(SettingKey(name), Encode(shortcuts));
        }
    }

    /// <summary>
    /// Reads the remembered shortcuts back over the actions.
    /// </summary>
    /// <param name="restoreDefaults">When true, actions with nothing stored
    /// are reset to their defaults; when false they are left alone (the state
    /// right after construction, where they already hold their defaults).</param>
    public void Load(bool restoreDefaults = true)
    {
        HashSet<string> stored = new HashSet<string>(StringComparer.Ordinal);
        if (_settings != null)
        {
            string prefix = SettingKey(string.Empty);
            foreach (var key in _settings.KeysWithPrefix(prefix))
            {
                string name = key.Substring(prefix.Length);
                AppAction action = Action(name);
                if (action == null)
                {
                    //A stored shortcut for an action that no longer exists is
                    //dropped, exactly as upstream drops it.
                    _settings.Remove(key);
                    continue;
                }

                stored.Add(name);
                action.Shortcuts = Decode(_settings.GetString(key));
            }
        }

        if (!restoreDefaults) { return; }

        foreach (var pair in _actions.Where(a => !stored.Contains(a.Key)))
        {
            pair.Value.Shortcuts = DefaultShortcuts(pair.Key);
        }
    }

    /// <summary>Restores one action's default shortcuts and forgets the
    /// stored override.</summary>
    /// <param name="name">The action name.</param>
    public void RestoreDefaultShortcuts(string name)
    {
        AppAction action = Action(name);
        if (action == null) { return; }

        action.Shortcuts = DefaultShortcuts(name);
        _settings?.Remove(SettingKey(name));
    }

    /// <summary>Sets every action's text. Called on creation and whenever the
    /// language changes.</summary>
    public abstract void TranslateUI();

    /// <summary>Creates the actions. Called once, from
    /// <see cref="Initialize"/>.</summary>
    protected abstract void CreateActions();

    /// <summary>
    /// Creates the actions, records their shortcuts as the defaults, loads the
    /// user's overrides and translates the texts — the sequence upstream runs
    /// in its constructor.
    /// </summary>
    /// <remarks>Subclasses call this at the END of their own constructor: the
    /// actions they create in <see cref="CreateActions"/> usually capture
    /// fields that a base-class call would not have assigned yet.</remarks>
    protected void Initialize()
    {
        CreateActions();
        foreach (var pair in _actions.Where(a => a.Value.Shortcuts.Count > 0))
        {
            _defaults[pair.Key] = pair.Value.Shortcuts;
        }

        //restoreDefaults: false — the actions already hold their defaults, and
        //resetting them here would undo a subclass's deliberate assignment.
        Load(false);
        TranslateUI();
    }

    /// <summary>Registers an action in the collection.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The same action, so creation can read as one expression.</returns>
    protected AppAction Add(AppAction action)
    {
        _actions[action.Name] = action;
        return action;
    }

    /// <summary>Creates and registers an action.</summary>
    /// <param name="name">The action name.</param>
    /// <returns>The action.</returns>
    protected AppAction Add(string name) => Add(new AppAction(name));

    private static bool SameShortcuts(
        IReadOnlyList<KeySequence> left, IReadOnlyList<KeySequence> right)
        => left.Count == right.Count && left.Zip(right, (a, b) => a.Equals(b)).All(x => x);

    private static string Encode(IReadOnlyList<KeySequence> shortcuts)
        => string.Join(";", shortcuts.Select(s => s.ToString()));

    private static IReadOnlyList<KeySequence> Decode(string text)
        => string.IsNullOrEmpty(text)
            ? Array.Empty<KeySequence>()
            : text.Split(';')
                .Select(KeySequence.Parse)
                .Where(k => k != null)
                .ToArray();

    private string SettingKey(string actionName)
        => $"shortcuts/{Scheme}/{Name}/{actionName}";

    /// <summary>Gets the shortcut scheme in force.</summary>
    /// <remarks>Upstream reads <c>shortcut_scheme</c> from the settings; the
    /// preferences page that lets a user make a second scheme lands in W12,
    /// and the key is already in the right shape for it.</remarks>
    private string Scheme => _settings?.GetString("shortcut_scheme", "default") ?? "default";
}
