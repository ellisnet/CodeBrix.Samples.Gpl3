// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;

namespace Fresco.Brix.Snippets; //was previously: frescobaldi/snippet/tool.py (SnippetActions)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The keyboard shortcuts that apply snippets.
/// </summary>
/// <remarks>
/// <para>
/// Upstream calls this shape a <c>ShortcutCollection</c>: a collection whose
/// actions are made on demand, because the things they act on — snippets here,
/// named sessions elsewhere — are the user's and may not exist. W2's
/// <c>ActionCollection</c> already carries the settings plumbing that split
/// existed for, so this simply registers an action per snippet and keeps the
/// list in step as snippets come and go.
/// </para>
/// <para>
/// //was previously: eleven more defaults sat in the table below, for snippets
/// //that run Python code and were therefore not shipped (FR5.3). Ruling FD10
/// //makes all twenty-two of those NATIVE COMMANDS, so their keys moved with
/// //them to <c>EditorCommandActions</c> — a snippet and a command holding the
/// //same key would be a conflict the shortcuts page could not resolve.
/// </para>
/// </remarks>
public sealed class SnippetShortcuts : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "snippets";

    /// <summary>The default shortcuts, by snippet name.</summary>
    public static readonly IReadOnlyDictionary<string, string> UpstreamDefaults
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["voice1"] = "Alt+1",
            ["voice2"] = "Alt+2",
            ["voice3"] = "Alt+3",
            ["voice4"] = "Alt+4",
            ["1voice"] = "Alt+0",
            ["times23"] = "Ctrl+3",
            ["ly_version"] = "Ctrl+Shift+V",
            ["blankline"] = "Ctrl+Shift+Return",
            ["repeat"] = "Ctrl+Shift+R",
            //was previously: eleven more rows here — next_blank_line,
            //previous_blank_line, next_blank_line_select,
            //previous_blank_line_select, removelines, quotes_s, quotes_d,
            //uppercase, lowercase, last_note and double. FD10 makes each of
            //them a native command, and the command carries the key now
            //(EditorCommands.All).
        };

    private readonly SnippetLibrary _library;

    /// <summary>Creates the collection.</summary>
    /// <param name="library">The snippets it makes actions for.</param>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public SnippetShortcuts(SnippetLibrary library, SettingsStore settings = null)
        : base(CollectionName, settings)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        Initialize();
        _library.Changed += (_, _) => Refresh();
    }

    /// <summary>Gets or sets what applying a snippet does.</summary>
    public Action<string> Apply { get; set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Snippets");

    /// <summary>Adds actions for any snippet that has appeared.</summary>
    public void Refresh()
    {
        foreach (var name in _library.Names())
        {
            if (Action(name) != null) { continue; }

            Register(name);
        }

        Load(false);
        TranslateUI();
    }

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        foreach (var name in _library.Names())
        {
            Register(name);
        }
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        foreach (var pair in Actions)
        {
            //A snippet's "title" IS its menu text; upstream doubles the
            //ampersand so that a title holding one is not read as an
            //accelerator marker.
            pair.Value.Text = _library.Title(pair.Key).Replace("&", "&&");
        }
    }

    private void Register(string name)
    {
        AppAction action = Add(name);
        if (UpstreamDefaults.TryGetValue(name, out string shortcut))
        {
            action.WithShortcut(shortcut);
        }

        string snippet = name;
        action.Handler = () => Apply?.Invoke(snippet);
    }
}
