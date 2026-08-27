// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Fresco.Brix.Commands; //was previously: frescobaldi/actioncollectionmanager.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Every <see cref="ActionCollection"/> in the window, so the shortcut
/// settings page can edit them all and so a new shortcut can be checked
/// against the ones already taken.
/// </summary>
public sealed class ActionCollectionManager
{
    private readonly Dictionary<string, ActionCollection> _collections
        = new Dictionary<string, ActionCollection>(StringComparer.Ordinal);

    /// <summary>Gets the collections.</summary>
    public IEnumerable<ActionCollection> Collections => _collections.Values;

    /// <summary>Adds a collection, ignoring a repeated name.</summary>
    /// <param name="collection">The collection.</param>
    public void Add(ActionCollection collection)
    {
        if (collection == null) { throw new ArgumentNullException(nameof(collection)); }

        if (!_collections.ContainsKey(collection.Name))
        {
            _collections[collection.Name] = collection;
        }
    }

    /// <summary>Removes a collection.</summary>
    /// <param name="collection">The collection.</param>
    public void Remove(ActionCollection collection)
    {
        if (collection != null)
        {
            _collections.Remove(collection.Name);
        }
    }

    /// <summary>Gets a named action from a named collection.</summary>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="actionName">The action name.</param>
    /// <returns>The action, or null when either name is unknown.</returns>
    public AppAction Action(string collectionName, string actionName)
        => collectionName != null
            && _collections.TryGetValue(collectionName, out var collection)
                ? collection.Action(actionName)
                : null;

    /// <summary>
    /// Enumerates every shortcut in every collection, so conflicts can be
    /// spotted.
    /// </summary>
    /// <param name="skipCollection">A collection to skip an action in, or null.</param>
    /// <param name="skipAction">The action name to skip, or null.</param>
    /// <returns>The shortcut, its collection, and its action.</returns>
    public IEnumerable<(KeySequence Shortcut, ActionCollection Collection, AppAction Action)>
        AllShortcuts(ActionCollection skipCollection = null, string skipAction = null)
    {
        foreach (var collection in _collections.Values)
        {
            foreach (var pair in collection.Actions)
            {
                if (collection == skipCollection
                    && string.Equals(pair.Key, skipAction, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var shortcut in pair.Value.Shortcuts)
                {
                    yield return (shortcut, collection, pair.Value);
                }
            }
        }
    }

    /// <summary>
    /// Finds what a shortcut would collide with, and answers that action's
    /// text with its accelerator marker stripped — or null when the shortcut
    /// is free.
    /// </summary>
    /// <param name="shortcut">The proposed shortcut.</param>
    /// <param name="skipCollection">The collection being edited, or null.</param>
    /// <param name="skipAction">The action being edited, or null.</param>
    /// <returns>The conflicting action's text, or null.</returns>
    public string FindShortcutConflict(
        KeySequence shortcut,
        ActionCollection skipCollection = null,
        string skipAction = null)
    {
        if (shortcut == null) { return null; }

        foreach (var entry in AllShortcuts(skipCollection, skipAction))
        {
            if (shortcut.Equals(entry.Shortcut))
            {
                return RemoveAccelerator(entry.Action.Text);
            }
        }

        return null;
    }

    /// <summary>Removes the shortcuts in a list wherever they are in use.</summary>
    /// <param name="shortcuts">The shortcuts to free up.</param>
    public void RemoveShortcuts(IReadOnlyList<KeySequence> shortcuts)
    {
        if (shortcuts == null || shortcuts.Count == 0) { return; }

        foreach (var entry in AllShortcuts().ToList())
        {
            if (!shortcuts.Any(s => s.Equals(entry.Shortcut))) { continue; }

            List<KeySequence> remaining = entry.Action.Shortcuts
                .Where(s => !s.Equals(entry.Shortcut)).ToList();
            entry.Collection.SetShortcuts(entry.Action.Name, remaining);
        }
    }

    /// <summary>
    /// Strips the accelerator markers from a label: a doubled marker is a
    /// literal ampersand, a single one marks the next letter.
    /// </summary>
    /// <param name="text">The label.</param>
    /// <returns>The plain text.</returns>
    /// <remarks>Upstream this is <c>qutil.removeAccelerator</c>.</remarks>
    public static string RemoveAccelerator(string text)
    {
        if (string.IsNullOrEmpty(text)) { return text; }

        StringBuilder plain = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '&')
            {
                plain.Append(text[i]);
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '&')
            {
                plain.Append('&');
                i++;
            }
        }

        return plain.ToString();
    }
}
