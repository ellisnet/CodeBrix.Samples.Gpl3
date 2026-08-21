// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Editor;
using Fresco.Brix.Services;
using System;

namespace Fresco.Brix.Documents;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The per-document things every view of that document shares: the one
/// tokenization, the one ly-document bridge over it, and the remembered
/// per-document state.
/// <para>
/// This is what makes split views work: two editors showing the same document
/// share this object, so they share the token cache and every ported ly tool
/// sees the same document whichever view is focused. Upstream gets the same
/// effect with its <c>DocumentPlugin</c>s; here one object holds them, keyed
/// off the document and collected with it.
/// </para>
/// </summary>
public sealed class DocumentEditorState
    : Plugin<EditorDocument, DocumentEditorState>
{
    private DocumentEditorState(EditorDocument document, SettingsStore settings)
        : base(document)
    {
        Settings = settings;
        Highlighter = new LyHighlighter(document.Document);
        LyDocument = new AteLyDocument(document.Document, Highlighter);
        Styler = new SchemeTokenStyler(new TextFormatData("default", settings));
        Highlighter.Styler = Styler;
        MetaInfo = settings == null ? null : new MetaInfo(settings, document.Path);
        Folding = new LyFoldingStrategy(Highlighter);

        //Upstream's ly-document bridge derives its file name from the editor
        //document's URL; ours is a settable property, so it is kept in step
        //here. Everything that resolves an \include relative to "our own file"
        //reads it.
        LyDocument.Filename = document.Path;

        //A load replaces the whole text, so the guessed mode has to be redone
        //and the remembered state re-read for the new file.
        document.Loaded += (_, _) =>
        {
            Highlighter.SetMode(null);
            LyDocument.Filename = document.Path;
            if (MetaInfo != null) { MetaInfo.Path = document.Path; }
        };
        document.UrlChanged += (_, _) =>
        {
            LyDocument.Filename = document.Path;
            if (MetaInfo != null) { MetaInfo.Path = document.Path; }
        };
    }

    /// <summary>Gets the document, or null once it has been collected.</summary>
    public EditorDocument Document => Owner;

    /// <summary>Gets the settings store, or null when there is none.</summary>
    public SettingsStore Settings { get; }

    /// <summary>Gets the single tokenization every view and tool reads.</summary>
    public LyHighlighter Highlighter { get; }

    /// <summary>Gets the ly-document bridge the ported tools edit through.</summary>
    public AteLyDocument LyDocument { get; }

    /// <summary>Gets the token colouring, so a scheme change reaches
    /// every view at once.</summary>
    public SchemeTokenStyler Styler { get; }

    /// <summary>Gets what the app remembers about this document, or null when
    /// there is no settings store.</summary>
    public MetaInfo MetaInfo { get; }

    /// <summary>Gets the folding strategy over the shared tokenization.</summary>
    public LyFoldingStrategy Folding { get; }

    /// <summary>Gets the state for a document, creating it on first use.</summary>
    /// <param name="document">The document.</param>
    /// <param name="settings">The settings store, or null.</param>
    /// <returns>The state.</returns>
    public static DocumentEditorState For(
        EditorDocument document, SettingsStore settings = null)
        => Instance(document, owner => new DocumentEditorState(owner, settings));
}
