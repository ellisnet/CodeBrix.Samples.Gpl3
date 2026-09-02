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
    /// <summary>
    /// The settings key the user's snippets live under — ONE key holding every
    /// stored snippet as a JSON object, by name.
    /// </summary>
    /// <remarks>//was previously: <c>snippets/</c>, a PREFIX with a
    /// <c>text</c>, <c>title</c> and <c>deleted</c> key per snippet under it,
    /// which the store this replaced could enumerate. The settings add-in has
    /// no prefix-scan API by design (board W13 item 9, route (a)).</remarks>
    public const string SettingsKey = "snippets";

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
        Dictionary<string, StoredSnippet> stored = ReadStored();
        HashSet<string> names = new HashSet<string>(
            BuiltinSnippets.ByName.Keys, StringComparer.Ordinal);
        foreach (var pair in stored)
        {
            if (!string.IsNullOrEmpty(pair.Value?.Text)
                || !string.IsNullOrEmpty(pair.Value?.Title))
            {
                names.Add(pair.Key);
            }
        }

        names.RemoveWhere(
            n => stored.TryGetValue(n, out var snippet) && snippet != null && snippet.Deleted);
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
        string stored = Stored(name)?.Text;
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
        string stored = Stored(name)?.Title;
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

        Dictionary<string, StoredSnippet> stored = ReadStored();
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(text))
        {
            Forget(stored, name);
        }
        else
        {
            stored[name] = new StoredSnippet
            {
                Text = string.IsNullOrEmpty(text) ? null : text,
                Title = string.IsNullOrEmpty(title) ? null : title,
                Deleted = false,
            };
        }

        WriteStored(stored);
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

        Dictionary<string, StoredSnippet> stored = ReadStored();
        Forget(stored, name);
        if (BuiltinSnippets.ByName.ContainsKey(name))
        {
            //A built-in cannot be removed, only marked gone.
            stored[name] = new StoredSnippet { Deleted = true };
        }

        WriteStored(stored);
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

    private IReadOnlyCollection<string> StoredNames()
    {
        //A "deleted" mark is not a stored snippet; it is the absence of one.
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in ReadStored())
        {
            if (!string.IsNullOrEmpty(pair.Value?.Text)
                || !string.IsNullOrEmpty(pair.Value?.Title))
            {
                names.Add(pair.Key);
            }
        }

        return names;
    }

    private StoredSnippet Stored(string name)
        => name != null && ReadStored().TryGetValue(name, out var snippet)
            ? snippet
            : null;

    private Dictionary<string, StoredSnippet> ReadStored()
        => _settings?.Get<Dictionary<string, StoredSnippet>>(SettingsKey)
            ?? new Dictionary<string, StoredSnippet>(StringComparer.Ordinal);

    private void WriteStored(Dictionary<string, StoredSnippet> stored)
    {
        if (_settings == null) { return; }

        if (stored.Count == 0)
        {
            _settings.Remove(SettingsKey);
        }
        else
        {
            _settings.Set(SettingsKey, stored);
        }
    }

    //Forgetting a snippet drops what the user wrote, but not the mark that says
    //a built-in is gone — which is why the entry survives when it carries one.
    private static void Forget(Dictionary<string, StoredSnippet> stored, string name)
    {
        if (!stored.TryGetValue(name, out var snippet)) { return; }

        if (snippet != null && snippet.Deleted)
        {
            snippet.Text = null;
            snippet.Title = null;
        }
        else
        {
            stored.Remove(name);
        }
    }
}

/// <summary>
/// One snippet as the settings store holds it: what the user wrote over the
/// built-in, or the mark saying they removed a built-in altogether.
/// </summary>
public sealed class StoredSnippet
{
    /// <summary>Gets or sets the snippet's text, or null when unchanged.</summary>
    public string Text { get; set; }

    /// <summary>Gets or sets the snippet's title, or null when unchanged.</summary>
    public string Title { get; set; }

    /// <summary>Gets or sets whether a built-in snippet was removed.</summary>
    public bool Deleted { get; set; }
}
