// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Music;
using System;
using System.Linq;
using MusicTree = Fresco.Brix.Ly.Music.Document;

namespace Fresco.Brix.Tools; //was previously: frescobaldi/definition.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Where a name used in the document was defined — the target of Ctrl+click
/// and of Edit &gt; Go to Definition.
/// </summary>
/// <remarks>
/// The definition may be in an <c>\include</c>d file that is not open; the
/// music tree has already read those, so answering the question needs no
/// second parse, only the file opened.
/// </remarks>
public static class GotoDefinition
{
    /// <summary>
    /// Gets the music item at a position when that item is a REFERENCE to
    /// something defined elsewhere — a <c>\variable</c> or a markup command.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="position">The offset to look at.</param>
    /// <param name="selectionEnd">The end of the selection, or the same
    /// position when there is none.</param>
    /// <returns>The item, or null.</returns>
    public static Item ReferenceNode(
        EditorDocument document, int position, int selectionEnd = -1)
    {
        if (document == null) { return null; }

        if (selectionEnd < position) { selectionEnd = position; }

        MusicTree music = DocumentInfo.For(document).Music();
        Item node = music?.NodeAt(position);
        return node != null
            && node.EndPosition() >= selectionEnd
            && (node is UserCommand || node is MarkupUserCommand)
                ? node
                : null;
    }

    /// <summary>Gets the item a reference points at.</summary>
    /// <param name="node">The reference.</param>
    /// <returns>The definition, or null.</returns>
    public static Item Target(Item node)
    {
        Item value = node switch
        {
            UserCommand command => command.Value(),
            MarkupUserCommand markup => markup.Value(),
            _ => null,
        };

        if (value == null) { return null; }

        //Its parent is the assignment that defines it — except for a
        //`#(define-markup-command ...)', whose value hangs directly off the
        //document because there is no assignment to be the parent.
        var target = value.Parent() as Item;
        return target is MusicTree ? value : target;
    }

    /// <summary>
    /// Works out where to go for the reference at a position.
    /// </summary>
    /// <param name="document">The document the caret is in.</param>
    /// <param name="position">The offset.</param>
    /// <param name="selectionEnd">The end of the selection, or -1.</param>
    /// <returns>The destination, or null when there is no definition.</returns>
    public static DefinitionTarget Find(
        EditorDocument document, int position, int selectionEnd = -1)
    {
        Item node = ReferenceNode(document, position, selectionEnd);
        if (node == null) { return null; }

        Item target = Target(node);
        if (target == null) { return null; }

        DocumentBase source = target.SourceDocument;
        return new DefinitionTarget(
            source as AteLyDocument, source?.Filename, target.Position);
    }
}

/// <summary>Where a definition lives.</summary>
/// <remarks>Either an open document (the bridge is not null) or a file that
/// has to be opened first (only the path is known).</remarks>
public sealed class DefinitionTarget
{
    /// <summary>Creates a target.</summary>
    /// <param name="openDocument">The open document's ly bridge, or null.</param>
    /// <param name="filename">The file, or null.</param>
    /// <param name="position">The offset in it.</param>
    public DefinitionTarget(AteLyDocument openDocument, string filename, int position)
    {
        OpenDocument = openDocument;
        Filename = filename;
        Position = position;
    }

    /// <summary>Gets the open document's ly bridge, or null when the
    /// definition is in a file that is not open.</summary>
    public AteLyDocument OpenDocument { get; }

    /// <summary>Gets the file the definition is in, or null.</summary>
    public string Filename { get; }

    /// <summary>Gets the offset the definition starts at.</summary>
    public int Position { get; }

    /// <summary>Finds the open document this target is in, if any.</summary>
    /// <param name="documents">The open documents.</param>
    /// <returns>The document, or null.</returns>
    public EditorDocument DocumentIn(DocumentManager documents)
    {
        if (documents == null) { return null; }

        if (OpenDocument != null)
        {
            EditorDocument found = documents.Documents.FirstOrDefault(
                d => ReferenceEquals(
                    DocumentEditorState.For(d)?.LyDocument, OpenDocument));
            if (found != null) { return found; }
        }

        return Filename == null ? null : documents.FindDocument(Filename);
    }
}
