// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly.MusicXml;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace Fresco.Brix.Export; //was previously: frescobaldi/file_export/__init__.py (exportMusicXML)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Writing the current document out as MusicXML.</summary>
/// <remarks>
/// <para>
/// Upstream's <c>FileExport.exportMusicXML</c>: it asks <c>ly.musicxml</c> for
/// a writer, feeds it the document's text, then reaches into the tree and
/// replaces the <c>&lt;software&gt;</c> element with its own name and version
/// before writing. All three steps are here — the last one because the string
/// python-ly writes there is python-ly's, and this is not python-ly.
/// </para>
/// <para>
/// The conversion is the whole of <c>Fresco.Brix.Ly.MusicXml</c>, which is
/// verified character for character against python-ly's own output over 81
/// documents. Nothing in it touches the engine or the platform, so an export
/// is a pure function of the text.
/// </para>
/// </remarks>
public static class MusicXmlExport
{
    /// <summary>Converts LilyPond source to a MusicXML document.</summary>
    /// <param name="text">The source.</param>
    /// <param name="fileName">The source's file name, for includes, or null.</param>
    /// <param name="warnings">Where the conversion's warnings are collected, or null.</param>
    /// <returns>The document.</returns>
    public static MusicXmlDocument Convert(
        string text, string fileName = null, IList<string> warnings = null)
    {
        var writer = new ParseSource();
        if (warnings != null) { writer.Warn = message => warnings.Add(message); }

        writer.ParseText(text ?? string.Empty, fileName);
        MusicXmlDocument document = writer.MusicXml();
        StampSoftware(document);
        return document;
    }

    /// <summary>Converts LilyPond source and writes the MusicXML to a file.</summary>
    /// <param name="text">The source.</param>
    /// <param name="outputPath">The file to write.</param>
    /// <param name="fileName">The source's file name, for includes, or null.</param>
    /// <param name="warnings">Where the conversion's warnings are collected, or null.</param>
    /// <returns>What happened, and why when nothing was written.</returns>
    /// <exception cref="ArgumentNullException">No output path.</exception>
    /// <remarks>
    /// <para>
    /// ⚠⚠ RULING FR15 IS ENFORCED HERE. ⚠⚠ <b>Fresco.Brix does not write a
    /// MusicXML file that fails to conform to the published schema</b>
    /// (<c>https://www.w3.org/2021/06/musicxml40/</c>), so this refuses rather
    /// than writing one.
    /// </para>
    /// <para>
    /// The refusal is a PRECONDITION check, not a validation: the schema is a
    /// 380-kilobyte test resource, not something the application carries or runs
    /// on every export. The division of labour is deliberate and a future
    /// developer should keep it — <c>MusicXmlSchemaTests</c> proves the WRITER
    /// never emits content the schema forbids, over every document in the
    /// parity corpus, and this method catches the one failure a USER can walk
    /// into: asking to export a file the converter cannot turn into any part at
    /// all. Together they are the rule. Weaken either and the rule is a wish.
    /// </para>
    /// <para>
    /// //was previously: void, and it wrote whatever the converter produced —
    /// including the empty <c>&lt;part-list /&gt;</c> skeleton that upstream
    /// writes for such a document, which is not valid MusicXML and which a user
    /// discovers only when something else refuses to open it.
    /// </para>
    /// </remarks>
    public static MusicXmlExportResult Write(
        string text, string outputPath, string fileName = null, IList<string> warnings = null)
    {
        if (outputPath == null) { throw new ArgumentNullException(nameof(outputPath)); }

        MusicXmlDocument document = Convert(text, fileName, warnings);
        if (!document.HasParts)
        {
            return MusicXmlExportResult.Refused(I18n.Get(
                "There is no music in this document that can be converted to MusicXML, "
                + "so no file was written."));
        }

        document.Write(outputPath);
        return MusicXmlExportResult.Succeeded(outputPath);
    }

    /// <summary>Returns the name to suggest exporting a document under.</summary>
    /// <param name="documentPath">The document's path, or null.</param>
    /// <returns>The suggested name.</returns>
    /// <remarks>Upstream's own: the document's name with <c>.xml</c> in place of
    /// its suffix, which is what a MusicXML reader expects to be offered.</remarks>
    public static string SuggestedName(string documentPath)
        => string.IsNullOrEmpty(documentPath)
            ? "document.xml"
            : Path.ChangeExtension(documentPath, ".xml");

    /// <summary>
    /// Replaces the converter's own name in the document with this application's.
    /// </summary>
    /// <param name="document">The document.</param>
    private static void StampSoftware(MusicXmlDocument document)
    {
        ETreeElement software = document.Root.FindDescendant("software");
        if (software == null) { return; }

        software.Text = AppInfo.AppName + " " + AppInfo.Version;
    }
}

/// <summary>What a MusicXML export did.</summary>
/// <remarks>
/// A result rather than an exception because "this document has no music the
/// converter understands" is an ordinary answer to an ordinary request, not a
/// fault: the user asked, and the honest reply is a sentence rather than a
/// stack trace or an unreadable file.
/// </remarks>
public sealed class MusicXmlExportResult
{
    private MusicXmlExportResult(bool ok, string path, string reason)
    {
        Ok = ok;
        Path = path;
        Reason = reason;
    }

    /// <summary>Gets whether a file was written.</summary>
    public bool Ok { get; }

    /// <summary>Gets the file that was written, or null.</summary>
    public string Path { get; }

    /// <summary>Gets why nothing was written, or null.</summary>
    public string Reason { get; }

    /// <summary>Returns a successful result.</summary>
    /// <param name="path">The file written.</param>
    /// <returns>The result.</returns>
    public static MusicXmlExportResult Succeeded(string path)
        => new MusicXmlExportResult(true, path, null);

    /// <summary>Returns a result that says nothing was written, and why.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>The result.</returns>
    public static MusicXmlExportResult Refused(string reason)
        => new MusicXmlExportResult(false, null, reason);
}
