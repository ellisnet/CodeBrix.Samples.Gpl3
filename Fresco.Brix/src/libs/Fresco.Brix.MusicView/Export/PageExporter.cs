// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using SkiaSharp;
using System;
using System.IO;

namespace Fresco.Brix.MusicView; //was previously: qpageview/export.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Exports a rectangular area of one <see cref="ScorePage"/> — or the whole
/// page — to a file format.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>AbstractExporter</c>. A caller builds one over a page and a
/// rectangle, sets whichever of the settings the subclass says it
/// <c>Supports</c>, and then asks for <see cref="Data"/>, <see cref="Save"/>
/// or <see cref="PreviewPage"/>. The bytes are produced once and kept until
/// <see cref="SetPage"/> is called again, exactly as upstream caches
/// <c>_result</c>.
/// </para>
/// <para>
/// The page is COPIED on the way in (<see cref="ScorePage.Copy"/>), so the
/// export renders at its own resolution and paper colour while the view goes
/// on painting the original.
/// </para>
/// <para>
/// What upstream's class does and this one does not: the clipboard and
/// drag-and-drop members (<c>copyData</c>, <c>mimeData</c>, <c>dragData</c>,
/// <c>dragFile</c>). Those are the APPLICATION's business here — a library
/// control has no business reaching the clipboard — and they live in the
/// Fresco.Brix Copy to Image dialog with the rest of the chrome.
/// </para>
/// </remarks>
public abstract class PageExporter
{
    private ScorePage _page;
    private SKRect? _rect;
    private byte[] _result;
    private SKRectI? _autoCropRect;
    private bool _cropped;
    private string _tempFileName;

    /// <summary>Creates an exporter over a page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="rect">The region, in page coordinates; null for the whole page.</param>
    protected PageExporter(ScorePage page, SKRect? rect = null) => SetPage(page, rect);

    /// <summary>Gets or sets the wanted resolution, in dots per inch.</summary>
    public double Resolution { get; set; } = 300.0;

    /// <summary>Gets or sets whether drawing is antialiased.</summary>
    public bool Antialiasing { get; set; } = true;

    /// <summary>Gets or sets whether blank margins are trimmed off.</summary>
    public bool AutoCrop { get; set; }

    /// <summary>Gets or sets how many times over the wanted size to render.</summary>
    public int Oversample { get; set; } = 1;

    /// <summary>Gets or sets whether the result is reduced to grey.</summary>
    public bool Grayscale { get; set; }

    /// <summary>Gets or sets the paper colour, or null for transparent.</summary>
    public SKColor? PaperColor { get; set; }

    /// <summary>Gets or sets the name of the file the page came from.</summary>
    /// <remarks>Only used to suggest a name to save under.</remarks>
    public string FileName { get; set; }

    /// <summary>Gets whether this format keeps the drawing as vectors.</summary>
    public virtual bool WantsVector => true;

    /// <summary>Gets whether <see cref="Resolution"/> is used.</summary>
    public virtual bool SupportsResolution => true;

    /// <summary>Gets whether <see cref="Antialiasing"/> is used.</summary>
    public virtual bool SupportsAntialiasing => true;

    /// <summary>Gets whether <see cref="AutoCrop"/> is used.</summary>
    public virtual bool SupportsAutoCrop => true;

    /// <summary>Gets whether <see cref="Oversample"/> is used.</summary>
    public virtual bool SupportsOversample => true;

    /// <summary>Gets whether <see cref="Grayscale"/> is used.</summary>
    public virtual bool SupportsGrayscale => true;

    /// <summary>Gets whether <see cref="PaperColor"/> is used.</summary>
    public virtual bool SupportsPaperColor => true;

    /// <summary>Gets the media type the exported bytes are.</summary>
    public virtual string MimeType => "application/octet-stream";

    /// <summary>Gets the name to use when nothing else is known.</summary>
    public virtual string DefaultBaseName => "document";

    /// <summary>Gets the suffix this format's files carry.</summary>
    public virtual string DefaultExtension => "";

    /// <summary>Gets the page being exported, at the export's paper colour.</summary>
    protected ScorePage Page => _page;

    /// <summary>Gets the region being exported, in page coordinates.</summary>
    protected SKRect? Rect => _rect;

    /// <summary>Points the exporter at another page, forgetting any result.</summary>
    /// <param name="page">The page.</param>
    /// <param name="rect">The region, in page coordinates; null for the whole page.</param>
    public void SetPage(ScorePage page, SKRect? rect = null)
    {
        _page = page?.Copy() ?? throw new ArgumentNullException(nameof(page));
        _rect = rect;
        _result = null;
        _autoCropRect = null;
        _cropped = false;
        _tempFileName = null;
    }

    /// <summary>
    /// Returns the region to export, trimmed of blank margins when
    /// <see cref="AutoCrop"/> asks for it.
    /// </summary>
    /// <returns>The region, in page coordinates; null for the whole page.</returns>
    /// <remarks>
    /// Upstream renders the region at the page's CURRENT displayed resolution
    /// to find the ink, then grows the answer by a pixel "to prevent loosing
    /// small joins or curves"; both are kept.
    /// </remarks>
    public SKRect? AutoCroppedRect()
    {
        if (!AutoCrop) { return _rect; }

        if (!_cropped)
        {
            _cropped = true;
            _autoCropRect = null;

            //⚠ THE PROBE MUST RENDER AT THE PAGE'S OWN DISPLAYED RESOLUTION, or
            //the rectangle it finds is in the PROBE's pixels and the caller
            //reads it as page coordinates. Upstream computes exactly this dpi
            //(`p.width / p.defaultSize().width() * p.dpi`) and that is why.
            ////was previously: _page.Dpi, which is only right when the page
            //happens to be displayed at 1:1 — in the Copy to Image dialog it
            //never is, and the region came back eight times too large, which
            //asked Skia for a surface it could not allocate and crashed on the
            //null it gets back.
            var (naturalWidth, naturalHeight) = _page.DefaultSize();
            double dpiX = naturalWidth > 0 ? _page.Width / naturalWidth * _page.Dpi : _page.Dpi;
            double dpiY = naturalHeight > 0 ? _page.Height / naturalHeight * _page.Dpi : _page.Dpi;

            using SKImage image = _page.Image(_rect, dpiX, dpiY, PaperColor);
            SKRectI? ink = AutoCropping.InkRect(image);
            if (ink != null)
            {
                SKRectI whole = new SKRectI(0, 0, image.Width, image.Height);
                SKRectI grown = ink.Value;
                grown.Inflate(1, 1);
                if (grown.IntersectsWith(whole))
                {
                    grown.Intersect(whole);
                    _autoCropRect = grown;
                }
            }
        }

        if (_autoCropRect == null) { return _rect; }

        //The ink rectangle is in the pixels that were rendered, which for the
        //probe above are the page's own displayed pixels — so it is already in
        //page coordinates, and only needs putting back where the region was.
        SKRectI ink2 = _autoCropRect.Value;
        var result = new SKRect(ink2.Left, ink2.Top, ink2.Right, ink2.Bottom);
        if (_rect != null) { result.Offset(_rect.Value.Left, _rect.Value.Top); }

        return result;
    }

    /// <summary>Produces the exported bytes. Called once; the result is kept.</summary>
    /// <returns>The bytes, or null when the export failed.</returns>
    protected abstract byte[] Export();

    /// <summary>Gets the exported bytes, producing them on the first request.</summary>
    /// <returns>The bytes, or null when the export failed.</returns>
    public byte[] Data() => _result ??= Export();

    /// <summary>Gets whether the export produced anything.</summary>
    /// <returns>True when it did.</returns>
    public bool Successful() => Data() != null;

    /// <summary>Writes the exported bytes to a file.</summary>
    /// <param name="fileName">The file to write.</param>
    /// <exception cref="InvalidOperationException">The export failed.</exception>
    public virtual void Save(string fileName)
    {
        byte[] data = Data() ?? throw new InvalidOperationException("The export produced nothing.");
        File.WriteAllBytes(fileName, data);
    }

    /// <summary>
    /// Returns a page showing what would be exported, for a preview.
    /// </summary>
    /// <returns>The page, or null when the export failed.</returns>
    /// <remarks>
    /// Upstream's <c>document()</c>, which builds a one-page Document of the
    /// exported thing. There is no Document type here — a view is given pages —
    /// so this hands back the one page.
    /// </remarks>
    public abstract ScorePage PreviewPage();

    /// <summary>
    /// Returns a name to suggest saving under, never the source's own name.
    /// </summary>
    /// <returns>The name, with a directory when the source had one.</returns>
    public string SuggestedFileName()
    {
        if (string.IsNullOrEmpty(FileName)) { return DefaultBaseName + DefaultExtension; }

        string withoutExtension = Path.Combine(
            Path.GetDirectoryName(FileName) ?? string.Empty,
            Path.GetFileNameWithoutExtension(FileName));
        string name = withoutExtension + DefaultExtension;
        return string.Equals(name, FileName, StringComparison.Ordinal)
            ? withoutExtension + "-export" + DefaultExtension
            : name;
    }

    /// <summary>Writes the export to a temporary file and returns its name.</summary>
    /// <returns>The file name.</returns>
    public string TempFileName()
    {
        if (_tempFileName != null) { return _tempFileName; }

        string baseName = string.IsNullOrEmpty(FileName)
            ? DefaultBaseName
            : Path.GetFileNameWithoutExtension(FileName);
        string directory = Path.Combine(Path.GetTempPath(), "fresco-brix-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _tempFileName = Path.Combine(directory, baseName + DefaultExtension);
        Save(_tempFileName);
        return _tempFileName;
    }

    /// <summary>Returns the resolution to render at, oversampling included.</summary>
    /// <returns>The resolution.</returns>
    protected double EffectiveResolution()
        => SupportsOversample && Oversample > 1 ? Resolution * Oversample : Resolution;
}
