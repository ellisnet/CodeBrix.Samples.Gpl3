// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Fresco.Brix.Import; //was previously: frescobaldi/file_import/__init__.py (targets, is_importable)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>The kinds of file File &gt; Import can read.</summary>
public enum ImportFormat
{
    /// <summary>MusicXML, plain or in a compressed container.</summary>
    MusicXml,

    /// <summary>A Standard MIDI File.</summary>
    Midi,

    /// <summary>ABC notation.</summary>
    Abc,
}

/// <summary>
/// Which file extension is read by which importer, and the file-dialog filters
/// the four menu entries put in front of the user.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>FileImport.targets</c> table and its <c>is_importable</c>
/// guard. The table maps an extension to the MODULE that configures the
/// conversion (<c>.musicxml</c>, <c>.midi</c>, <c>.abc</c>), which is what this
/// enumeration stands in for; the guard is the same list of six extensions,
/// written out a second time upstream.
/// </para>
/// <para>
/// ⚠ THE COMPARISON IS CASE-SENSITIVE, as upstream's is: a file called
/// <c>Score.XML</c> is not importable, and the generic import says so rather
/// than guessing. That is upstream's behaviour and the file-dialog filters
/// agree with it, so it is ported rather than quietly improved.
/// </para>
/// </remarks>
public static class ImportFormats
{
    /// <summary>The extensions each format is read from, in upstream's order.</summary>
    /// <remarks>Upstream's <c>targets</c> dictionary, whose insertion order is
    /// also the order its <c>is_importable</c> list is written in.</remarks>
    public static readonly IReadOnlyDictionary<string, ImportFormat> Targets
        = new ReadOnlyDictionary<string, ImportFormat>(
            new Dictionary<string, ImportFormat>(StringComparer.Ordinal)
            {
                [".xml"] = ImportFormat.MusicXml,
                [".musicxml"] = ImportFormat.MusicXml,
                [".mxl"] = ImportFormat.MusicXml,
                [".midi"] = ImportFormat.Midi,
                [".mid"] = ImportFormat.Midi,
                [".abc"] = ImportFormat.Abc,
            });

    /// <summary>The MusicXML extensions, in upstream's own order.</summary>
    public static readonly IReadOnlyList<string> MusicXmlExtensions
        = new[] { ".xml", ".musicxml", ".mxl" };

    /// <summary>The MIDI extensions, in upstream's own order.</summary>
    public static readonly IReadOnlyList<string> MidiExtensions
        = new[] { ".midi", ".mid" };

    /// <summary>The ABC extensions.</summary>
    public static readonly IReadOnlyList<string> AbcExtensions = new[] { ".abc" };

    /// <summary>Every extension the generic import offers, in upstream's order.</summary>
    public static readonly IReadOnlyList<string> AllExtensions
        = new[] { ".xml", ".musicxml", ".mxl", ".midi", ".mid", ".abc" };

    /// <summary>Answers whether a file can be imported at all.</summary>
    /// <param name="fileName">The file name or path.</param>
    /// <returns>Whether it can.</returns>
    /// <remarks>Upstream's <c>is_importable</c>.</remarks>
    public static bool IsImportable(string fileName)
        => FormatOf(fileName) != null;

    /// <summary>Answers which importer reads a file, or null for none.</summary>
    /// <param name="fileName">The file name or path.</param>
    /// <returns>The format, or null.</returns>
    public static ImportFormat? FormatOf(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) { return null; }

        string extension = PathUtil.SplitExtension(fileName, out _);
        return Targets.TryGetValue(extension, out ImportFormat format)
            ? format
            : (ImportFormat?)null;
    }

    /// <summary>Answers whether a file is a compressed MusicXML container.</summary>
    /// <param name="fileName">The file name or path.</param>
    /// <returns>Whether it is.</returns>
    /// <remarks>Upstream hands <c>.mxl</c> to <c>musicxml2ly</c> and lets it
    /// notice; the port has to choose the entry point, and
    /// <c>ImportCompressed</c> is upstream's own <c>-z</c>/<c>--compressed</c>
    /// route.</remarks>
    public static bool IsCompressedMusicXml(string fileName)
        => string.Equals(
            PathUtil.SplitExtension(fileName, out _), ".mxl", StringComparison.Ordinal);

    /// <summary>The extensions one format's file dialog offers.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The extensions.</returns>
    public static IReadOnlyList<string> ExtensionsFor(ImportFormat format)
        => format switch
        {
            ImportFormat.MusicXml => MusicXmlExtensions,
            ImportFormat.Midi => MidiExtensions,
            _ => AbcExtensions,
        };

    /// <summary>What a format's file dialog calls the files it offers.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The label.</returns>
    /// <remarks>Upstream's own msgids, from the filter strings its four import
    /// actions build.</remarks>
    public static string LabelFor(ImportFormat format)
        => format switch
        {
            ImportFormat.MusicXml => I18n.Get("MusicXML Files"),
            ImportFormat.Midi => I18n.Get("Midi Files"),
            _ => I18n.Get("ABC Files"),
        };

    /// <summary>What the generic import calls the whole set.</summary>
    /// <returns>The label.</returns>
    public static string AllLabel() => I18n.Get("All importable formats");

    /// <summary>The title of a format's own file dialog.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The caption.</returns>
    /// <remarks>Upstream's <c>_("dialog title", …)</c> msgids.</remarks>
    public static string CaptionFor(ImportFormat format)
        => format switch
        {
            ImportFormat.MusicXml => I18n.Get("dialog title", "Import a MusicXML file"),
            ImportFormat.Midi => I18n.Get("dialog title", "Import a midi file"),
            _ => I18n.Get("dialog title", "Import an abc file"),
        };

    /// <summary>The title of the generic import's file dialog.</summary>
    /// <returns>The caption.</returns>
    public static string AllCaption() => I18n.Get("dialog title", "Import");

    /// <summary>The name of the converter a format is read by.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The name.</returns>
    /// <remarks>
    /// Upstream's <c>imp_prgm</c>: the name of the script it would have run.
    /// The scripts are not run here — <c>CodeBrix.LilyPort.Importers</c> is the
    /// same program in this process — but the NAMES are what the dialogs, the
    /// user guide and LilyPond's own documentation call these conversions, and
    /// ruling FR13 is about the word "LilyPond", which none of them is.
    /// </remarks>
    public static string ConverterName(ImportFormat format)
        => format switch
        {
            ImportFormat.MusicXml => "musicxml2ly",
            ImportFormat.Midi => "midi2ly",
            _ => "abc2ly",
        };

    /// <summary>The settings group a format's dialog remembers itself in.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The group name.</returns>
    /// <remarks>Upstream's <c>settings.beginGroup(...)</c> names, kept so the
    /// keys read the same as they do there.</remarks>
    public static string SettingsGroup(ImportFormat format)
        => format switch
        {
            ImportFormat.MusicXml => "musicxml_import",
            ImportFormat.Midi => "midi_import",
            _ => "abc_import",
        };

    /// <summary>The user-guide page a format's dialog opens.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The page name.</returns>
    /// <remarks>Upstream's <c>userg</c> argument.</remarks>
    public static string HelpPage(ImportFormat format)
        => format switch
        {
            ImportFormat.MusicXml => "musicxml_import",
            ImportFormat.Midi => "midi_import",
            _ => "abc_import",
        };
}
