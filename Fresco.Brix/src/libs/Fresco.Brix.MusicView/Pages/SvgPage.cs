// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.SkiaSvg;
using CodeBrix.SkiaSvg.TypefaceProviders;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using Shim = CodeBrix.SkiaSvg.ShimSkiaSharp;

namespace Fresco.Brix.MusicView; //was previously: qpageview/svg.py, over LilyPort's SVG output

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One page of engraved music, read from one SVG file the engine wrote.
/// </summary>
/// <remarks>
/// <para>
/// The file is parsed ONCE into a Skia picture, which then redraws at any zoom
/// in a millisecond or two because it is still vector — measured at 0.5 ms for
/// an ordinary page and 14.5 ms for the heaviest page in the engine's own
/// corpus at 400%. That is why this view has no tile cache and no render
/// threads: upstream needs them because Poppler rasterises, and nothing here
/// does.
/// </para>
/// <para>
/// The same parse yields the point-and-click anchors. The engine writes them as
/// <c>&lt;a xlink:href="textedit://…"&gt;</c> around the grob's own drawing
/// commands, and the renderer's scene graph hands each one back with the bounds
/// it actually occupies, transforms and all — so the link areas are the
/// renderer's own geometry rather than a second, hand-written reading of the
/// file.
/// </para>
/// </remarks>
public sealed class SvgPage : ScorePage, IDisposable
{
    /// <summary>The unit an SVG's user coordinates are in: CSS pixels.</summary>
    public const double SvgDpi = 96.0;

    private readonly string _fileName;
    private readonly IScoreTypefaceResolver _typefaces;

    private SKSvg _svg;
    private bool _loadFailed;
    private bool _shared;
    private (double Width, double Height)? _paperSize;

    /// <summary>Creates a page over an SVG file. The file is read lazily.</summary>
    /// <param name="fileName">The file.</param>
    /// <param name="typefaces">Who answers the score's font families (trap 9).</param>
    public SvgPage(string fileName, IScoreTypefaceResolver typefaces = null)
    {
        _fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        _typefaces = typefaces;
        Dpi = SvgDpi;
    }

    /// <summary>Gets the file this page was read from.</summary>
    public string FileName => _fileName;

    /// <summary>Gets whether the file could not be read as an SVG.</summary>
    public bool LoadFailed
    {
        get
        {
            EnsureLoaded();
            return _loadFailed;
        }
    }

    /// <summary>Loads the pages of a set of SVG files, one page per file.</summary>
    /// <param name="fileNames">The files, in page order.</param>
    /// <param name="typefaces">Who answers the score's font families.</param>
    /// <returns>The pages.</returns>
    public static IReadOnlyList<SvgPage> Load(
        IEnumerable<string> fileNames, IScoreTypefaceResolver typefaces = null)
    {
        var pages = new List<SvgPage>();
        if (fileNames == null) { return pages; }

        foreach (string fileName in fileNames) { pages.Add(new SvgPage(fileName, typefaces)); }

        return pages;
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas, SKRect rect)
    {
        EnsureLoaded();

        if (PaperColor.HasValue) { canvas.DrawRect(Rect, new SKPaint { Color = PaperColor.Value }); }

        SKPicture picture = _svg?.Picture;
        if (picture == null) { return; }

        SKRect cull = picture.CullRect;
        canvas.Save();
        canvas.Concat(Transform());
        canvas.Translate(-cull.Left, -cull.Top);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    /// <inheritdoc/>
    protected override void EnsureSize() => EnsureLoaded();

    /// <inheritdoc/>
    protected override LinkList GetLinks()
    {
        EnsureLoaded();

        var links = new List<Link>();
        if (_svg != null && _svg.TryEnsureRetainedSceneGraph(out SvgSceneDocument scene) && scene?.Root != null)
        {
            SKRect cull = _svg.Picture?.CullRect ?? SKRect.Empty;
            if (cull.Width > 0 && cull.Height > 0) { CollectAnchors(scene.Root, cull, links); }
        }

        return new LinkList(links);
    }

    /// <summary>Forgets the parsed file, so a re-engraved page is read again.</summary>
    public void Reload()
    {
        if (!_shared) { _svg?.Dispose(); }

        _svg = null;
        _loadFailed = false;
        _paperSize = null;
        InvalidateLinks();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        //A copy BORROWED the parse (see Copy). Disposing it would pull the
        //picture out from under the page the view is still painting.
        if (!_shared) { _svg?.Dispose(); }

        _svg = null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The copy shares the parsed file rather than reading it again: an
    /// <c>SKPicture</c> is immutable, so the view may go on painting this page
    /// while an export draws the copy at another size on another thread.
    /// </remarks>
    public override ScorePage Copy()
    {
        EnsureLoaded();
        var copy = (SvgPage)base.Copy();
        copy._shared = true;
        return copy;
    }

    /// <summary>
    /// Gets the paper size the ENGINE declared, in points, or null when the
    /// file names none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ Board trap 61, and it is the difference between a PDF that measures
    /// A4 and one that does not. The engine writes the paper size twice: once
    /// exactly, as <c>width="210.0000mm" height="297.0000mm"</c> on the root
    /// element, and once as the pixel viewport the renderer computes from it —
    /// which is 794 x 1123 whole CSS pixels, 0.04% wider and taller than A4.
    /// <see cref="ScorePage.PageWidth"/> is the second of those, because that
    /// is what the view lays out in; a PDF page box has to be the first.
    /// </para>
    /// <para>
    /// Read straight out of the file's root attributes rather than from the
    /// renderer, because it is wanted BEFORE anything is drawn and because the
    /// attribute is the engine's own statement of the fact.
    /// </para>
    /// </remarks>
    public (double Width, double Height)? PaperSizePoints
    {
        get
        {
            if (_paperSize == null) { _paperSize = ReadPaperSize(_fileName); }

            return _paperSize.Value.Width > 0 && _paperSize.Value.Height > 0 ? _paperSize : null;
        }
    }

    /// <summary>
    /// Reads the root element's declared width and height, in points.
    /// </summary>
    /// <param name="fileName">The file.</param>
    /// <returns>The size, zero for either part the file does not state.</returns>
    internal static (double Width, double Height) ReadPaperSize(string fileName)
    {
        try
        {
            var settings = new System.Xml.XmlReaderSettings
            {
                DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            };
            using System.Xml.XmlReader reader = System.Xml.XmlReader.Create(fileName, settings);
            while (reader.Read())
            {
                if (reader.NodeType != System.Xml.XmlNodeType.Element) { continue; }

                if (!string.Equals(reader.LocalName, "svg", StringComparison.Ordinal)) { return (0, 0); }

                return (ParseLength(reader.GetAttribute("width")),
                    ParseLength(reader.GetAttribute("height")));
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or System.Xml.XmlException)
        {
        }

        return (0, 0);
    }

    /// <summary>Reads one CSS length in the units the engine writes, as points.</summary>
    /// <param name="text">The attribute text, such as <c>210.0000mm</c>.</param>
    /// <returns>The length in points, or zero.</returns>
    /// <remarks>
    /// Only the units an SVG root can carry are read. The engine writes
    /// millimetres, but a file a user brings in may say anything, and a length
    /// with no unit at all is CSS pixels by definition.
    /// </remarks>
    internal static double ParseLength(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) { return 0.0; }

        string value = text.Trim();
        double perUnit = 72.0 / SvgDpi;
        foreach ((string suffix, double factor) in LengthUnits)
        {
            if (!value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { continue; }

            value = value.Substring(0, value.Length - suffix.Length).Trim();
            perUnit = factor;
            break;
        }

        return double.TryParse(
            value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double number)
            ? number * perUnit
            : 0.0;
    }

    //Points per unit, longest suffix first so "mm" is not read as "m".
    private static readonly (string Suffix, double Factor)[] LengthUnits =
    {
        ("mm", 72.0 / 25.4),
        ("cm", 720.0 / 25.4),
        ("in", 72.0),
        ("pt", 1.0),
        ("pc", 12.0),
        ("px", 72.0 / SvgDpi),
        ("q", 18.0 / 25.4),
    };

    private static void CollectAnchors(SvgSceneNode node, SKRect cull, List<Link> links)
    {
        if (node.Kind == SvgSceneNodeKind.Anchor
            && node.Element is CodeBrix.SvgParse.SvgAnchor anchor
            && !string.IsNullOrEmpty(anchor.Href))
        {
            Shim.SKRect b = node.TransformedBounds;
            if (b.Width > 0 && b.Height > 0)
            {
                links.Add(new Link(
                    (b.Left - cull.Left) / cull.Width,
                    (b.Top - cull.Top) / cull.Height,
                    (b.Right - cull.Left) / cull.Width,
                    (b.Bottom - cull.Top) / cull.Height,
                    anchor.Href));
            }
        }

        if (node.Children == null) { return; }

        foreach (SvgSceneNode child in node.Children) { CollectAnchors(child, cull, links); }
    }

    private void EnsureLoaded()
    {
        if (_svg != null || _loadFailed) { return; }

        try
        {
            var svg = new SKSvg();
            if (_typefaces != null)
            {
                //The host's chain REPLACES the default provider rather than
                //standing in front of it: a family the host cannot answer must
                //draw tofu, not quietly find something on the machine.
                svg.Settings.TypefaceProviders.Clear();
                svg.Settings.TypefaceProviders.Add(new ResolverProvider(_typefaces));
            }

            svg.Load(_fileName);
            if (svg.Picture == null)
            {
                svg.Dispose();
                _loadFailed = true;
                return;
            }

            _svg = svg;
            SKRect cull = svg.Picture.CullRect;
            SetPageSize(cull.Width, cull.Height);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or InvalidOperationException or System.Xml.XmlException)
        {
            _loadFailed = true;
        }
    }

    /// <summary>Hands the renderer's font question to the host's resolver.</summary>
    private sealed class ResolverProvider : ITypefaceProvider
    {
        private readonly IScoreTypefaceResolver _resolver;

        internal ResolverProvider(IScoreTypefaceResolver resolver) => _resolver = resolver;

        public SKTypeface FromFamilyName(
            string familyName, SKFontStyleWeight weight, SKFontStyleWidth width, SKFontStyleSlant slant)
            => _resolver.Resolve(familyName, weight, width, slant);
    }
}
