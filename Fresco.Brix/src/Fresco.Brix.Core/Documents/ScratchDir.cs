// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly.Lex;
using Fresco.Brix.Services;
using System.IO;
using System.Linq;

namespace Fresco.Brix.Documents; //was previously: frescobaldi/scratchdir.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A private temporary directory for a document that cannot be engraved where
/// it lives — because it has never been saved, or because it has unsaved
/// changes the user wants engraved without saving them.
/// </summary>
/// <remarks>
/// The directory is created only when it is first needed, so a document that
/// is always saved before engraving never gets one.
/// </remarks>
public sealed class ScratchDir : Plugin<EditorDocument, ScratchDir>
{
    private string _directory;

    private ScratchDir(EditorDocument document)
        : base(document)
    {
    }

    /// <summary>Gets the document this area belongs to.</summary>
    public EditorDocument Document => Owner;

    /// <summary>Gets the directory, or null when none was ever needed.</summary>
    public string Directory => _directory;

    /// <summary>Gets the area for a document, creating it on first use.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The area.</returns>
    public static ScratchDir For(EditorDocument document)
        => Instance(document, owner => new ScratchDir(owner));

    /// <summary>
    /// Finds the open document a file name belongs to, counting a scratch copy
    /// as belonging to the document it was written for.
    /// </summary>
    /// <param name="documents">The open documents.</param>
    /// <param name="fileName">The file name.</param>
    /// <returns>The document, or null.</returns>
    /// <remarks>This is what lets an error message about the scratch copy be
    /// shown at the right line of the document the user is editing.</remarks>
    public static EditorDocument FindDocument(
        DocumentManager documents, string fileName)
    {
        if (documents == null || string.IsNullOrEmpty(fileName)) { return null; }

        foreach (var document in documents.Documents)
        {
            if (document.Path != null
                && PathUtil.EqualPaths(document.Path, fileName))
            {
                return document;
            }
        }

        foreach (var document in documents.Documents)
        {
            ScratchDir scratch = For(document);
            if (scratch.Directory != null
                && scratch.Path() != null
                && PathUtil.EqualPaths(fileName, scratch.Path()))
            {
                return document;
            }
        }

        return null;
    }

    /// <summary>Creates the temporary directory if it does not exist yet.</summary>
    public void Create() => _directory ??= PathUtil.TempDir();

    /// <summary>
    /// Gets the path the document's text would be saved to, or null when no
    /// area was created.
    /// </summary>
    /// <returns>The path, or null.</returns>
    public string Path()
    {
        if (_directory == null) { return null; }

        EditorDocument document = Document;
        if (document == null) { return null; }

        string baseName = document.Path == null
            ? null
            : System.IO.Path.GetFileName(document.Path);
        if (string.IsNullOrEmpty(baseName))
        {
            //A nameless document still needs a name with the right extension:
            //the engine decides how to read a file by its contents, but the
            //rest of the pipeline finds output by base name.
            string mode = DocumentInfo.For(document).Mode();
            baseName = "document"
                + (Modes.Extensions.TryGetValue(mode ?? string.Empty, out var extension)
                    ? extension
                    : ".ly");
        }

        return System.IO.Path.Combine(_directory, baseName);
    }

    /// <summary>Writes the document's current text into the area.</summary>
    public void SaveDocument()
    {
        Create();
        EditorDocument document = Document;
        if (document == null) { return; }

        File.WriteAllBytes(Path(), document.EncodedText());
    }
}
