// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Colorizing;
using Fresco.Brix.Services;
using System;

namespace Fresco.Brix.Export; //was previously: frescobaldi/highlight2html.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>What the colored-HTML export was asked for.</summary>
/// <remarks>
/// Upstream passes these as eight keyword arguments through four functions that
/// differ only in where they get their cursor. Naming them once says the same
/// thing and puts their DEFAULTS in one place — and the defaults matter,
/// because the export and the clipboard copy do not share them: upstream saves
/// with <c>inline_export</c> off (a stylesheet) and copies with
/// <c>inline_copy</c> ON (style attributes, because a clipboard has nowhere to
/// put a stylesheet).
/// </remarks>
public sealed class ColoredHtmlOptions
{
    /// <summary>Gets or sets the colour scheme's name.</summary>
    public string Scheme { get; set; } = "editor";

    /// <summary>Gets or sets whether styles are written on each span.</summary>
    public bool Inline { get; set; } = true;

    /// <summary>Gets or sets whether line numbers are shown.</summary>
    public bool NumberLines { get; set; }

    /// <summary>Gets or sets whether a whole document is produced.</summary>
    public bool FullHtml { get; set; } = true;

    /// <summary>Gets or sets the tag the document is wrapped in.</summary>
    public string WrapTag { get; set; } = "pre";

    /// <summary>Gets or sets the attribute the wrapper is identified by.</summary>
    public string WrapAttribute { get; set; } = "id";

    /// <summary>Gets or sets the wrapper's identifier.</summary>
    public string WrapAttributeName { get; set; } = "document";
}

/// <summary>
/// Exporting syntax-highlighted source as HTML.
/// </summary>
/// <remarks>
/// Upstream's <c>highlight2html</c>. Its whole job is to fill in an
/// <c>ly.colorize.HtmlWriter</c> from the CURRENT Fonts &amp; Colors scheme and
/// hand it a cursor — which is what makes an exported file look like the editor
/// the user was looking at rather than like some default.
/// </remarks>
public static class ColoredHtml
{
    /// <summary>Highlights a piece of text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="mode">The mode to read it in, or null to guess.</param>
    /// <param name="options">What to produce, or null for the defaults.</param>
    /// <param name="settings">Where the colour scheme is read from, or null.</param>
    /// <returns>The HTML.</returns>
    public static string FromText(
        string text, string mode = null, ColoredHtmlOptions options = null,
        SettingsStore settings = null)
    {
        var document = new Document(text ?? string.Empty, mode);
        return Html(new Cursor(document), options, settings);
    }

    /// <summary>Highlights an editor document's selection.</summary>
    /// <param name="state">The document's editor state.</param>
    /// <param name="start">Where the selection starts.</param>
    /// <param name="length">How long it is.</param>
    /// <param name="options">What to produce, or null for the defaults.</param>
    /// <param name="settings">Where the colour scheme is read from, or null.</param>
    /// <returns>The HTML.</returns>
    /// <remarks>
    /// Upstream's <c>html_inline</c>, and its default is INLINE styles: what is
    /// going to the clipboard has nowhere to carry a stylesheet.
    /// </remarks>
    public static string FromSelection(
        DocumentEditorState state, int start, int length,
        ColoredHtmlOptions options = null, SettingsStore settings = null)
    {
        if (state == null) { throw new ArgumentNullException(nameof(state)); }

        var cursor = new Cursor(state.LyDocument, start, start + length);
        return Html(cursor, options ?? new ColoredHtmlOptions(), settings);
    }

    /// <summary>Highlights a whole editor document.</summary>
    /// <param name="state">The document's editor state.</param>
    /// <param name="options">What to produce, or null for the defaults.</param>
    /// <param name="settings">Where the colour scheme is read from, or null.</param>
    /// <returns>The HTML.</returns>
    /// <remarks>
    /// Upstream's <c>html_document</c>, whose default is a STYLESHEET rather
    /// than inline styles: a saved file can carry one, and it is far smaller.
    /// </remarks>
    public static string FromDocument(
        DocumentEditorState state, ColoredHtmlOptions options = null,
        SettingsStore settings = null)
    {
        if (state == null) { throw new ArgumentNullException(nameof(state)); }

        options ??= new ColoredHtmlOptions { Inline = false };
        return Html(new Cursor(state.LyDocument), options, settings);
    }

    /// <summary>Highlights whatever a cursor covers.</summary>
    /// <param name="cursor">The selection; the whole document when it has no end.</param>
    /// <param name="options">What to produce, or null for the defaults.</param>
    /// <param name="settings">Where the colour scheme is read from, or null.</param>
    /// <returns>The HTML.</returns>
    public static string Html(
        Cursor cursor, ColoredHtmlOptions options = null, SettingsStore settings = null)
    {
        if (cursor == null) { throw new ArgumentNullException(nameof(cursor)); }

        options ??= new ColoredHtmlOptions();
        //The scheme NAME is now a preference (W12A's Fonts & Colors page):
        //exported source follows `printer_scheme' where the user set one, and
        //the editor's own scheme otherwise. options.Scheme is upstream's
        //`scheme' argument, which chooses the editor's format set or the
        //printer's.
        string name = string.Equals(options.Scheme, "printer", StringComparison.Ordinal)
            ? TextFormatData.PrinterScheme(settings)
            : TextFormatData.CurrentScheme(settings);
        var data = new TextFormatData(name, settings, options.Scheme);

        var writer = new HtmlWriter
        {
            InlineStyle = options.Inline,
            NumberLines = options.NumberLines,
            FullHtml = options.FullHtml,
            DocumentId = options.WrapAttributeName,
            Foreground = HtmlColor(data.BaseColor("text")),
            Background = HtmlColor(data.BaseColor("background")),
            CssScheme = data.ToCssScheme(),
        };
        writer.SetWrapperTag(options.WrapTag);
        writer.SetWrapperAttribute(options.WrapAttribute);
        return writer.Html(cursor);
    }

    /// <summary>Returns the name to suggest exporting a document under.</summary>
    /// <param name="documentPath">The document's path, or null.</param>
    /// <returns>The suggested name.</returns>
    /// <remarks>
    /// Upstream's own rule, oddity included: a document ALREADY called
    /// <c>.html</c> becomes <c>name_html.html</c> rather than overwriting
    /// itself.
    /// </remarks>
    public static string SuggestedName(string documentPath)
    {
        if (string.IsNullOrEmpty(documentPath)) { return "document.html"; }

        string directory = System.IO.Path.GetDirectoryName(documentPath) ?? string.Empty;
        string name = System.IO.Path.GetFileNameWithoutExtension(documentPath);
        string extension = System.IO.Path.GetExtension(documentPath);
        if (string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase))
        {
            name += "_html";
        }

        return System.IO.Path.Combine(directory, name + ".html");
    }

    /// <summary>Formats a colour the way CSS wants it.</summary>
    /// <param name="color">The colour.</param>
    /// <returns>The text.</returns>
    private static string HtmlColor(Windows.UI.Color color)
        => "#" + color.R.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)
            + color.G.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)
            + color.B.ToString("x2", System.Globalization.CultureInfo.InvariantCulture);
}
