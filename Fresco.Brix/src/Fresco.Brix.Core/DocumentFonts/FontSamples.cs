// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Fresco.Brix.DocumentFonts; //was previously: frescobaldi/fonts/preview.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One entry of the sample chooser.</summary>
/// <param name="Id">The template's file name, or one of the two special
/// markers.</param>
/// <param name="Label">What the chooser calls it.</param>
/// <param name="ToolTip">What the chooser says about it.</param>
public readonly record struct FontSample(string Id, Func<string> Label, Func<string> ToolTip);

/// <summary>
/// The scores the Document Fonts dialog engraves to show a font off.
/// </summary>
/// <remarks>
/// The six <c>.ly</c> files are Frescobaldi's own, shipped as assets exactly as
/// upstream ships them beside its <c>fonts</c> package (see
/// <c>assets/fonttemplates/README.txt</c> and THIRD-PARTY-NOTICES.txt).
/// </remarks>
public static class FontSamples
{
    /// <summary>The chooser entry meaning "a file the user picks".</summary>
    public const string CustomId = "<CUSTOM>";

    /// <summary>The chooser entry meaning "whatever is being edited".</summary>
    public const string CurrentId = "<CURRENT>";

    /// <summary>The prefix every shipped template's file name carries.</summary>
    private const string TemplatePrefix = "musicfont-";

    private static readonly Regex StaffSizeRegex = new Regex(
        @"^#\(set-global-staff-size \d+\)", RegexOptions.CultureInvariant);

    /// <summary>The six shipped samples, in upstream's own order.</summary>
    public static IReadOnlyList<FontSample> Provided { get; } = new[]
    {
        new FontSample(
            "bach.ly",
            () => I18n.Get("Bach (Piano)"),
            () => I18n.Get("Baroque music lends itself to traditional fonts")),
        new FontSample(
            "scriabine.ly",
            () => I18n.Get("Scriabin (Piano)"),
            () => I18n.Get("Late romantic, complex piano music")),
        new FontSample(
            "berg-string-quartet.ly",
            () => I18n.Get("Berg (String Quartet)"),
            () => I18n.Get("Complex score, requires a 'clean' font")),
        new FontSample(
            "realbook.ly",
            () => I18n.Get("Real Book (Lead Sheet)"),
            () => I18n.Get(
                "Jazz-like lead sheet\n"
                + "NOTE: beautiful results rely on appropriate text fonts.\n"
                + "Good choices are \"lilyjazz-text\" for roman and\n"
                + "\"lilyjazz-chords\" for sans text fonts.")),
        new FontSample(
            "schenker.ly",
            () => I18n.Get("Schenker Diagram"),
            () => I18n.Get("Schenker diagram with absolutely\nnon-standard notation")),
        new FontSample(
            "glyphs.ly",
            () => I18n.Get("Glyphs"),
            () => I18n.Get("Non-comprehensive specimen sheet")),
    };

    /// <summary>Gets the folder the templates are shipped in.</summary>
    /// <returns>The folder.</returns>
    public static string TemplateDirectory()
        => Path.Combine(AppContext.BaseDirectory, "assets", "fonttemplates");

    /// <summary>Answers a shipped sample's file.</summary>
    /// <param name="id">The sample's id, such as <c>bach.ly</c>.</param>
    /// <returns>The file's path.</returns>
    public static string TemplatePath(string id)
        => Path.Combine(TemplateDirectory(), TemplatePrefix + id);

    /// <summary>
    /// Takes a leading <c>#(set-global-staff-size n)</c> off a sample.
    /// </summary>
    /// <param name="content">The sample's text.</param>
    /// <returns>The staff-size call (empty when there is none) and what is
    /// left of the sample.</returns>
    /// <remarks>Upstream's <c>handle_staff_size</c>: "If the sample file
    /// *starts with* a staff-size definition it will be injected *after* our
    /// paper block" — because a global staff size set after the fonts is the
    /// one that wins.</remarks>
    public static (string StaffSize, string Content) SplitStaffSize(string content)
    {
        Match match = StaffSizeRegex.Match(content ?? string.Empty);
        return match.Success
            ? (match.Value, content.Substring(match.Length))
            : (string.Empty, content ?? string.Empty);
    }

    /// <summary>
    /// Composes the document the preview engraves.
    /// </summary>
    /// <param name="version">The LilyPond version to declare.</param>
    /// <param name="fontCommand">The FULL font command.</param>
    /// <param name="content">The sample's own text.</param>
    /// <returns>The document.</returns>
    /// <remarks>
    /// <para>
    /// Upstream's <c>sample_document()</c>, part for part: a version statement,
    /// the sample's own staff size if it had one, the full font command, and
    /// the sample — joined with newlines.
    /// </para>
    /// <para>
    /// ⚠ BOARD TRAP 7, and this is the wrapper that answers it. Four of the six
    /// samples open with <c>\include "lilypond-book-preamble.ly"</c>, which
    /// redefines <c>default-toplevel-book-handler</c> to
    /// <c>print-book-with-defaults-as-systems</c> — one file per SYSTEM, which
    /// is what <c>lilypond-book</c> wants and what upstream's preview reads
    /// back. The engine's batch runner collects books through its OWN
    /// <c>default-toplevel-book-handler</c> (it is <c>init.ly</c>'s documented
    /// escape hatch), so a sample that redefines it produces NO PAGES AT ALL
    /// and the preview says "Engraving failed" with nothing in the log. The
    /// runner's handler is therefore held in a name of this application's own
    /// BEFORE the sample is read and put back AFTER it, which leaves the
    /// template files exactly as upstream ships them.
    /// </para>
    /// </remarks>
    public static string Compose(string version, string fontCommand, string content)
    {
        (string staffSize, string body) = SplitStaffSize(content);
        return string.Join(
            "\n",
            "\\version \"" + version + "\"\n",
            "#(define " + BookHandlerName + " default-toplevel-book-handler)\n",
            staffSize.Length > 0 ? staffSize + "\n" : string.Empty,
            fontCommand ?? string.Empty,
            body,
            "\n#(define default-toplevel-book-handler " + BookHandlerName + ")\n");
    }

    /// <summary>The name the runner's own book handler is held in.</summary>
    /// <remarks>Application-specific on purpose: it must not collide with
    /// anything a sample or a user's own document defines.</remarks>
    private const string BookHandlerName = "fresco-brix-preview-book-handler";

    /// <summary>
    /// Answers whether a sample's engraving is worth keeping between runs.
    /// </summary>
    /// <param name="id">The sample's id.</param>
    /// <returns>Whether it is.</returns>
    /// <remarks>Upstream: "Provided sample files will be cached persistently";
    /// a custom file or the current document is cached for the run only,
    /// because either can change under the cache.</remarks>
    public static bool CachePersistently(string id)
        => !string.Equals(id, CustomId, StringComparison.Ordinal)
            && !string.Equals(id, CurrentId, StringComparison.Ordinal);
}
