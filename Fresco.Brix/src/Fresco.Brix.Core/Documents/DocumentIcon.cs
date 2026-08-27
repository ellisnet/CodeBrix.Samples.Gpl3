// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Fresco.Brix.Documents; //was previously: frescobaldi/documenticon.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// What an engrave run has most recently done for a document — the input the
/// document's tab icon is chosen from.
/// </summary>
/// <remarks>The engrave states are reported by the engrave service in W3; a
/// document with no run behind it is <see cref="None"/>.</remarks>
public enum EngraveState
{
    /// <summary>No engrave run has touched this document.</summary>
    None,

    /// <summary>An engrave run is under way.</summary>
    Running,

    /// <summary>The last run finished and produced output.</summary>
    Succeeded,

    /// <summary>The last run finished with errors.</summary>
    Failed,

    /// <summary>The last run was aborted by the user.</summary>
    Aborted,
}

/// <summary>
/// Chooses the icon shown on a document's tab. The order matters: a running
/// engrave outranks everything, then the sticky pin, then unsaved changes, and
/// only then the last run's result.
/// </summary>
public static class DocumentIcon
{
    /// <summary>Shown while an engrave run is under way.</summary>
    public const string Running = "lilypond-run";

    /// <summary>Shown for the document engraving is pinned to.</summary>
    public const string Sticky = "pushpin";

    /// <summary>Shown when the document has unsaved changes.</summary>
    public const string Modified = "document-save";

    /// <summary>Shown after a successful engrave run.</summary>
    public const string CompileSuccess = "document-compile-success";

    /// <summary>Shown after a failed engrave run.</summary>
    public const string CompileFailed = "document-compile-failed";

    /// <summary>Shown for a saved document with no engrave run behind it.</summary>
    public const string Plain = "text-plain";

    /// <summary>Picks the icon name for a document.</summary>
    /// <param name="isModified">Whether the document has unsaved changes.</param>
    /// <param name="isSticky">Whether engraving is pinned to this document.</param>
    /// <param name="engraveState">What the last engrave run did.</param>
    /// <returns>The icon-theme name.</returns>
    public static string NameFor(
        bool isModified,
        bool isSticky = false,
        EngraveState engraveState = EngraveState.None)
    {
        if (engraveState == EngraveState.Running) { return Running; }

        if (isSticky) { return Sticky; }

        if (isModified) { return Modified; }

        return engraveState switch
        {
            EngraveState.Succeeded => CompileSuccess,
            EngraveState.Failed => CompileFailed,
            _ => Plain,
        };
    }

    /// <summary>Picks the icon name for a document.</summary>
    /// <param name="document">The document.</param>
    /// <param name="isSticky">Whether engraving is pinned to this document.</param>
    /// <param name="engraveState">What the last engrave run did.</param>
    /// <returns>The icon-theme name.</returns>
    public static string NameFor(
        EditorDocument document,
        bool isSticky = false,
        EngraveState engraveState = EngraveState.None)
        => NameFor(document?.IsModified ?? false, isSticky, engraveState);
}
