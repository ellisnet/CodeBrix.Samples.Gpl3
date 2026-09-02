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

        SetShortcutsInScheme(Scheme, name, shortcuts);
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
            string scheme = Scheme;
            Dictionary<string, string> entries = ReadScheme(_settings, scheme);
            string prefix = Name + "/";
            bool dropped = false;
            foreach (var entry in entries
                .Where(e => e.Key.StartsWith(prefix, StringComparison.Ordinal))
                .ToList())
            {
                string name = entry.Key.Substring(prefix.Length);
                AppAction action = Action(name);
                if (action == null)
                {
                    //A stored shortcut for an action that no longer exists is
                    //dropped, exactly as upstream drops it.
                    entries.Remove(entry.Key);
                    dropped = true;
                    continue;
                }

                stored.Add(name);
                action.Shortcuts = Decode(entry.Value);
            }

            if (dropped) { WriteScheme(_settings, scheme, entries); }
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
        if (_settings == null) { return; }

        string scheme = Scheme;
        Dictionary<string, string> entries = ReadScheme(_settings, scheme);
        if (entries.Remove(ShortcutEntryKey(Name, name)))
        {
            WriteScheme(_settings, scheme, entries);
        }
    }

    /// <summary>
    /// The settings key ONE SCHEME's shortcuts are stored under — the family
    /// key, holding every collection's every customised action as one JSON
    /// object.
    /// </summary>
    /// <param name="scheme">The shortcut scheme.</param>
    /// <returns>The key.</returns>
    /// <remarks>
    /// //was previously: <see cref="ShortcutEntryKey"/>'s value was appended to
    /// this one with a slash and each action had a settings key of its own —
    /// upstream's <c>f"shortcuts/{scheme}/{collection.name}/{name}"</c>, which
    /// the flat store could enumerate by prefix. The settings add-in has no
    /// prefix-scan API by design (board W13 item 9, route (a)), so a scheme is
    /// one key holding a dictionary, and dropping a scheme is dropping that one
    /// key.
    /// </remarks>
    public static string ShortcutFamilyKey(string scheme)
        => "shortcuts/" + scheme;

    /// <summary>
    /// The name one action's shortcuts are held under INSIDE a scheme's
    /// dictionary — upstream's last two path segments, unchanged.
    /// </summary>
    /// <param name="collectionName">The collection.</param>
    /// <param name="actionName">The action.</param>
    /// <returns>The entry name.</returns>
    public static string ShortcutEntryKey(string collectionName, string actionName)
        => collectionName + "/" + actionName;

    /// <summary>Forgets everything a scheme remembers.</summary>
    /// <param name="settings">The settings store.</param>
    /// <param name="scheme">The scheme being removed.</param>
    /// <remarks>What the Shortcuts preferences page hands
    /// <c>SchemeSelector.SaveSettings</c> so that a removed scheme's shortcuts
    /// go with it.</remarks>
    public static void ForgetScheme(SettingsStore settings, string scheme)
    {
        if (settings == null || string.IsNullOrEmpty(scheme)) { return; }

        settings.Remove(ShortcutFamilyKey(scheme));
    }

    /// <summary>Reads an action's shortcuts IN A NAMED SCHEME.</summary>
    /// <param name="scheme">The scheme.</param>
    /// <param name="name">The action name.</param>
    /// <returns>The stored shortcuts, or the action's defaults when the scheme
    /// has nothing stored for it.</returns>
    public IReadOnlyList<KeySequence> ShortcutsInScheme(string scheme, string name)
    {
        return ReadScheme(_settings, scheme)
            .TryGetValue(ShortcutEntryKey(Name, name), out var stored)
            ? DecodeShortcuts(stored)
            : DefaultShortcuts(name);
    }

    /// <summary>Answers whether a scheme stores nothing for an action, so the
    /// action's own defaults are in force.</summary>
    /// <param name="scheme">The scheme.</param>
    /// <param name="name">The action name.</param>
    /// <returns>Whether the defaults are in force.</returns>
    public bool UsesDefaultShortcuts(string scheme, string name)
        => !ReadScheme(_settings, scheme)
            .ContainsKey(ShortcutEntryKey(Name, name));

    /// <summary>Writes an action's shortcuts IN A NAMED SCHEME.</summary>
    /// <param name="scheme">The scheme.</param>
    /// <param name="name">The action name.</param>
    /// <param name="shortcuts">The shortcuts; the same "equal to the defaults
    /// means forget it" rule as <see cref="SetShortcuts"/>.</param>
    public void SetShortcutsInScheme(
        string scheme, string name, IReadOnlyList<KeySequence> shortcuts)
    {
        if (_settings == null || Action(name) == null) { return; }

        shortcuts ??= Array.Empty<KeySequence>();
        string entryKey = ShortcutEntryKey(Name, name);
        IReadOnlyList<KeySequence> defaults = DefaultShortcuts(name);
        Dictionary<string, string> entries = ReadScheme(_settings, scheme);

        if (SameShortcuts(shortcuts, defaults)
            || (shortcuts.Count == 0 && defaults.Count == 0))
        {
            entries.Remove(entryKey);
        }
        else
        {
            //An empty list is stored deliberately: it means "the user removed
            //the default", which is not the same as "never customised".
            entries[entryKey] = Encode(shortcuts);
        }

        WriteScheme(_settings, scheme, entries);
    }

    /// <summary>Reads a stored shortcut list back.</summary>
    /// <param name="text">The stored text.</param>
    /// <returns>The shortcuts; the ones that no longer parse are dropped.</returns>
    public static IReadOnlyList<KeySequence> DecodeShortcuts(string text) => Decode(text);

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

    private static Dictionary<string, string> ReadScheme(
        SettingsStore settings, string scheme)
        => settings?.Get<Dictionary<string, string>>(ShortcutFamilyKey(scheme))
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

    private static void WriteScheme(
        SettingsStore settings, string scheme, Dictionary<string, string> entries)
    {
        if (settings == null) { return; }

        if (entries.Count == 0)
        {
            settings.Remove(ShortcutFamilyKey(scheme));
        }
        else
        {
            settings.Set(ShortcutFamilyKey(scheme), entries);
        }
    }

    /// <summary>Gets the shortcut scheme in force.</summary>
    /// <remarks>Upstream reads <c>shortcut_scheme</c> from the settings; the
    /// preferences page that lets a user make a second scheme lands in W12,
    /// and the key is already in the right shape for it.</remarks>
    private string Scheme => _settings?.GetString("shortcut_scheme", "default") ?? "default";
}
