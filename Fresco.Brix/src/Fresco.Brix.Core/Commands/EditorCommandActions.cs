// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Fresco.Brix.Tools;
using System;

namespace Fresco.Brix.Commands; //was previously: frescobaldi/snippet/actions.py, for the python-typed entries of snippet/builtin.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The twenty-two editor commands ruling FD10 makes native, as commands: their
/// names, their menu texts and their keyboard shortcuts.
/// </summary>
/// <remarks>
/// <para>
/// Upstream ships these as SNIPPETS whose body is Python code, so their
/// shortcuts live in its snippet shortcut collection and their menu entries are
/// made on the fly from the library. Here they are ordinary commands in an
/// ordinary collection: the Shortcuts preferences page lists them under this
/// collection's own title, the Snippets menu holds them where upstream's
/// <c>menu</c> variable put them, and <see cref="SnippetShortcutsNote"/>
/// records where their defaults came from.
/// </para>
/// <para>
/// The names are UPSTREAM'S OWN snippet names, so that a reader of
/// Frescobaldi's source finds the same thing here, and so that the recorded
/// defaults keep meaning what they meant.
/// </para>
/// </remarks>
public sealed class EditorCommandActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "editorcommands";

    /// <summary>
    /// Where the default shortcuts came from: they were carried in
    /// <c>SnippetShortcuts.UpstreamDefaults</c> while the twenty-two were not
    /// shipped at all, and they moved here with the commands.
    /// </summary>
    public const string SnippetShortcutsNote
        = "frescobaldi/snippet/builtin.py, the python-typed entries";

    /// <summary>Creates the collection.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public EditorCommandActions(SettingsStore settings = null)
        : base(CollectionName, settings) => Initialize();

    /// <summary>Gets or sets what running one of the commands does.</summary>
    public Action<string> Apply { get; set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Editor Commands");

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        foreach (EditorCommandInfo info in EditorCommands.All)
        {
            AppAction action = Add(info.Name);
            if (info.Shortcut != null)
            {
                action.WithShortcut(info.Shortcut);
            }

            string name = info.Name;
            action.Handler = () => Apply?.Invoke(name);
        }
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        foreach (EditorCommandInfo info in EditorCommands.All)
        {
            AppAction action = Action(info.Name);
            if (action != null) { action.Text = info.TranslatedTitle(); }
        }
    }
}
