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

namespace Fresco.Brix.Snippets; //was previously: frescobaldi/snippet/snippets.py and model.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Every snippet the user can reach: the ones that ship with the application
/// and the ones they have written, with the user's edits winning.
/// </summary>
/// <remarks>
/// A built-in snippet the user has EDITED is stored under its own name; one
/// they have DELETED gets a <c>deleted</c> mark rather than being removed,
/// because it is still in the shipped list and would otherwise come back. A
/// user's edit that ends up matching the built-in exactly is forgotten again,
/// so that a later change to the built-in reaches them.
/// </remarks>
public sealed class SnippetLibrary
{
    /// <summary>The settings prefix snippets live under.</summary>
    public const string SettingsPrefix = "snippets/";

    private readonly SettingsStore _settings;
    private readonly Dictionary<string, SnippetText> _parsed
        = new Dictionary<string, SnippetText>(StringComparer.Ordinal);
    private int _nextGeneratedNumber;

    /// <summary>Creates the library.</summary>
    /// <param name="settings">The store the user's snippets live in.</param>
    public SnippetLibrary(SettingsStore settings = null) => _settings = settings;

    /// <summary>Raised when a snippet is saved or deleted.</summary>
    public event EventHandler Changed;

    /// <summary>Gets the names of every available snippet.</summary>
    /// <returns>The names.</returns>
    public IReadOnlyCollection<string> Names()
    {
        HashSet<string> names = new HashSet<string>(
            BuiltinSnippets.ByName.Keys, StringComparer.Ordinal);
        foreach (var name in StoredNames())
        {
            names.Add(name);
        }

        names.RemoveWhere(IsDeleted);
        return names;
    }

    /// <summary>Gets the names in the order the snippet list shows them.</summary>
    /// <returns>The names, by title.</returns>
    public IReadOnlyList<string> NamesByTitle()
        => Names().OrderBy(n => Title(n), StringComparer.CurrentCulture).ToList();

    /// <summary>Gets a snippet's full text.</summary>
    /// <param name="name">The snippet name.</param>
    /// <returns>The text, or the empty string.</returns>
    public string Text(string name)
    {
        string stored = _settings?.GetString(Key(name, "text"));
        if (!string.IsNullOrEmpty(stored)) { return stored; }

        return BuiltinSnippets.ByName.TryGetValue(name, out var builtin)
            ? builtin.Text
            : string.Empty;
    }

    /// <summary>Gets a snippet's title.</summary>
    /// <param name="name">The snippet name.</param>
    /// <param name="fallback">Whether to abridge the text when there is no
    /// title.</param>
    /// <returns>The title.</returns>
    public string Title(string name, bool fallback = true)
    {
        string stored = _settings?.GetString(Key(name, "title"));
        if (!string.IsNullOrEmpty(stored)) { return stored; }

        if (BuiltinSnippets.ByName.TryGetValue(name, out var builtin)
            && !string.IsNullOrEmpty(builtin.Title))
        {
            //The stored msgid is upstream's; the translation happens here.
            return I18n.Get(builtin.Title);
        }

        return fallback ? ShortText(name) : string.Empty;
    }

    /// <summary>Gets a snippet's abridged text.</summary>
    /// <param name="name">The snippet name.</param>
    /// <returns>The abridged text.</returns>
    public string ShortText(string name)
        => SnippetParser.MakeTitle(Get(name).Text);

    /// <summary>Gets a snippet's text and variables.</summary>
    /// <param name="name">The snippet name.</param>
    /// <returns>The parsed snippet.</returns>
    public SnippetText Get(string name)
    {
        if (_parsed.TryGetValue(name, out var cached)) { return cached; }

        SnippetText parsed = SnippetParser.Parse(Text(name));
        _parsed[name] = parsed;
        return parsed;
    }

    /// <summary>Gets a snippet's <c>name</c> variable, used by macros.</summary>
    /// <param name="name">The snippet name.</param>
    /// <returns>The variable, or the empty string.</returns>
    public string ActionName(string name) => Get(name).Variable("name");

    /// <summary>Answers whether a built-in snippet is untouched.</summary>
    /// <param name="name">The snippet name.</param>
    /// <returns>Whether it is.</returns>
    public bool IsOriginal(string name)
        => BuiltinSnippets.ByName.ContainsKey(name) && !StoredNames().Contains(name);

    /// <summary>Answers whether a snippet ships with the application.</summary>
    /// <param name="name">The snippet name.</param>
    /// <returns>Whether it does.</returns>
    public static bool IsBuiltin(string name)
        => BuiltinSnippets.ByName.ContainsKey(name);

    /// <summary>Stores a snippet.</summary>
    /// <param name="name">The snippet name, or null for a new one.</param>
    /// <param name="text">The text.</param>
    /// <param name="title">The title, or null.</param>
    /// <returns>The name it was stored under.</returns>
    public string Save(string name, string text, string title = null)
    {
        name ??= NewName();
        _parsed.Remove(name);

        if (BuiltinSnippets.ByName.TryGetValue(name, out var builtin))
        {
            //An edit that matches the built-in exactly is not an edit.
            if (string.IsNullOrEmpty(title)
                || string.Equals(title, I18n.Get(builtin.Title), StringComparison.Ordinal))
            {
                title = null;
            }

            if (string.Equals(text, builtin.Text, StringComparison.Ordinal))
            {
                text = null;
            }
        }

        if (_settings == null)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return name;
        }

        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(text))
        {
            Forget(name);
        }
        else
        {
            Write(Key(name, "text"), text);
            Write(Key(name, "title"), title);
            _settings.Remove(Key(name, "deleted"));
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return name;
    }

    /// <summary>Deletes a snippet.</summary>
    /// <param name="name">The snippet name.</param>
    public void Delete(string name)
    {
        _parsed.Remove(name);
        if (_settings == null)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        Forget(name);
        if (BuiltinSnippets.ByName.ContainsKey(name))
        {
            //A built-in cannot be removed, only marked gone.
            _settings.SetString(Key(name, "deleted"), "1");
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Makes a name no existing snippet uses.</summary>
    /// <returns>The name.</returns>
    /// <remarks>Upstream picks a random six-digit number and retries on a
    /// collision. A counter over the names already in use answers the same
    /// question without a random source, which the tests can then rely on.
    /// </remarks>
    public string NewName()
    {
        IReadOnlyCollection<string> taken = Names();
        while (true)
        {
            string candidate = "n" + (++_nextGeneratedNumber)
                .ToString("000000", CultureInfo.InvariantCulture);
            if (!taken.Contains(candidate)) { return candidate; }
        }
    }

    private bool IsDeleted(string name)
        => !string.IsNullOrEmpty(_settings?.GetString(Key(name, "deleted")));

    private IReadOnlyCollection<string> StoredNames()
    {
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        if (_settings == null) { return names; }

        foreach (var key in _settings.KeysWithPrefix(SettingsPrefix))
        {
            string rest = key.Substring(SettingsPrefix.Length);
            int slash = rest.IndexOf('/');
            if (slash <= 0) { continue; }

            string name = rest.Substring(0, slash);
            //A "deleted" mark is not a stored snippet; it is the absence of one.
            if (!rest.EndsWith("/deleted", StringComparison.Ordinal))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private void Forget(string name)
    {
        _settings.Remove(Key(name, "text"));
        _settings.Remove(Key(name, "title"));
    }

    private void Write(string key, string value)
    {
        if (string.IsNullOrEmpty(value)) { _settings.Remove(key); } else { _settings.SetString(key, value); }
    }

    private static string Key(string name, string part)
        => SettingsPrefix + name + "/" + part;
}
