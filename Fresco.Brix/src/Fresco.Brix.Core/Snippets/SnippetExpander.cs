// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Engrave;
using Fresco.Brix.Services;
using System;
using System.Globalization;

namespace Fresco.Brix.Snippets; //was previously: frescobaldi/snippet/expand.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The three places a snippet can ask the caret to end up.
/// </summary>
/// <remarks>Upstream uses three integer constants that ride along in the same
/// list as the text pieces; naming them makes the same list readable.</remarks>
public enum SnippetMarker
{
    /// <summary>Not a marker — a piece of text.</summary>
    None = 0,

    /// <summary>Where the selection's anchor should end up.</summary>
    Anchor = 1,

    /// <summary>Where the caret should end up.</summary>
    Cursor = 2,

    /// <summary>Where the current selection should be dropped in.</summary>
    Selection = 3,
}

/// <summary>
/// Turns a <c>$VARIABLE</c> in a snippet into the text it stands for.
/// </summary>
/// <remarks>
/// <c>$LILYPOND_VERSION</c> answers the release the engine implements, read
/// from the one declaration that holds it (FR13). Upstream reads it from the
/// version-chooser preference, which FR5.1 removes: there is one engine.
/// </remarks>
public sealed class SnippetExpander
{
    private readonly EditorDocument _document;
    private readonly bool _hasSelection;

    /// <summary>Creates an expander for a document.</summary>
    /// <param name="document">The document the snippet is going into.</param>
    /// <param name="hasSelection">Whether anything is selected.</param>
    public SnippetExpander(EditorDocument document, bool hasSelection)
    {
        _document = document;
        _hasSelection = hasSelection;
    }

    /// <summary>
    /// Expands one variable name.
    /// </summary>
    /// <param name="name">The variable, without its <c>$</c>.</param>
    /// <param name="marker">The marker it stands for, when it is one.</param>
    /// <returns>The text, or null when the name is not a known variable.</returns>
    public string Expand(string name, out SnippetMarker marker)
    {
        marker = SnippetMarker.None;
        switch (name)
        {
            case "DATE":
                return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            case "LILYPOND_VERSION":
                return LilyPortEngine.CompatibleWithVersion;

            case "FRESCOBALDI_VERSION":
                //The msgid keeps upstream's name because a snippet a user
                //wrote against Frescobaldi must go on working; what it answers
                //is THIS application's version.
                return AppInfo.Version;

            case "URL":
                return _document?.Path == null
                    ? string.Empty
                    : new Uri(_document.Path).AbsoluteUri;

            case "FILE_NAME":
                return _document?.Path ?? string.Empty;

            case "DOCUMENT_NAME":
                return _document?.DocumentName() ?? string.Empty;

            case "CURSOR":
                marker = SnippetMarker.Cursor;
                return null;

            case "ANCHOR":
                marker = SnippetMarker.Anchor;
                return null;

            case "SELECTION":
                //With nothing selected, $SELECTION is where the caret goes.
                marker = _hasSelection ? SnippetMarker.Selection : SnippetMarker.Cursor;
                return null;

            default:
                return null;
        }
    }
}
