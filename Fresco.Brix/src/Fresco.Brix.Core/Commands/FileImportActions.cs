// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;

namespace Fresco.Brix.Commands; //was previously: frescobaldi/file_import/__init__.py (class Actions)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>The four ways into File &gt; Import.</summary>
/// <remarks>
/// Upstream's own <c>name = "file_import"</c> collection, with its four actions,
/// their names, their texts and their tool tips. The tool tips say "LilyPond
/// tools" and "using musicxml2ly" upstream; ruling FR13 keeps the converters'
/// own names — which are not the word LilyPond — and replaces the one mention
/// of the package.
/// </remarks>
public sealed class FileImportActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "file_import";

    /// <summary>Creates the collection.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public FileImportActions(SettingsStore settings = null)
        : base(CollectionName, settings) => Initialize();

    /// <summary>Gets the generic import, which reads the file's suffix.</summary>
    public AppAction ImportAny { get; private set; }

    /// <summary>Gets File &gt; Import/Export &gt; Import MusicXML.</summary>
    public AppAction ImportMusicXml { get; private set; }

    /// <summary>Gets File &gt; Import/Export &gt; Import Midi.</summary>
    public AppAction ImportMidi { get; private set; }

    /// <summary>Gets File &gt; Import/Export &gt; Import abc.</summary>
    public AppAction ImportAbc { get; private set; }

    /// <inheritdoc/>
    /// <remarks>⚠ A Fresco.Brix-ORIGINAL msgid. Upstream's collection has no
    /// title at all, because its actions are all on a menu and its shortcut
    /// page only titles the collections whose actions are not; this port's
    /// shortcut page groups every collection, so a heading is needed and
    /// "file_import" is not one. Recorded for W-I18N's renamed-string
    /// table.</remarks>
    public override string Title => I18n.Get("Import");

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        ImportAny = Add("import_any").WithIcon("document-import");
        ImportMusicXml = Add("import_musicxml");
        ImportMidi = Add("import_midi");
        ImportAbc = Add("import_abc");
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        ImportAny.Text = I18n.Get("Import...");

        //was previously: "Generic import for all LilyPond tools." — ruling FR13.
        //The three converters are named, which is what the sentence was about.
        ImportAny.ToolTip = I18n.Get(
            "Generic import for all import formats.");

        ImportMusicXml.Text = I18n.Get("Import MusicXML...");
        ImportMusicXml.ToolTip = I18n.Get("Import a MusicXML file using musicxml2ly.");
        ImportMidi.Text = I18n.Get("Import Midi...");
        ImportMidi.ToolTip = I18n.Get("Import a Midi file using midi2ly.");
        ImportAbc.Text = I18n.Get("Import abc...");
        ImportAbc.ToolTip = I18n.Get("Import an abc file using abc2ly.");
    }
}
