// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.PdfDocuments.Pdf;
using CodeBrix.PdfDocuments.Pdf.Annotations;
using CodeBrix.PdfDocuments.Pdf.IO;
using Fresco.Brix.MusicView;
using System;
using System.Collections.Generic;
using System.IO;

namespace Fresco.Brix.Manuscripts; //was previously: frescobaldi/viewers/pointandclick.py (function links) + qpageview/poppler.py (Page.links)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The clickable areas of a PDF: its pages' link annotations, turned into the
/// fractional rectangles the paged view hit-tests.
/// </summary>
/// <remarks>
/// <para>
/// Upstream gets these from Poppler, through qpageview: <c>page.links()</c>
/// answers areas already expressed as fractions of the page, and
/// <c>viewers/pointandclick.links()</c> walks them looking for
/// <c>textedit://</c> URLs. There is no Poppler here — ruling FR8 rasterises
/// through <c>CodeBrix.PdfRasterizer</c>, which draws pages and says nothing
/// about their annotations — so the annotations are read from the document
/// itself with <c>CodeBrix.PdfDocuments</c>, which the same package brings.
/// </para>
/// <para>
/// MEASURED before it was written (board wave W15's ruling gate): every link on
/// a page comes back from <c>PdfPage.Annotations</c> as a
/// <c>PdfGenericAnnotation</c> — a <see cref="PdfAnnotation"/>, so its
/// <c>Rectangle</c> is readable — carrying <c>/Subtype /Link</c>, its
/// <c>/Rect</c>, and either an action (<c>/A &lt;&lt; /S /URI /URI (…) &gt;&gt;</c>)
/// or an internal destination (<c>/Dest</c>).
/// </para>
/// <para>
/// ⚠ INTERNAL DESTINATIONS ARE NOT PORTED. A <c>/Dest</c> link points at a page
/// OBJECT inside the same document; <see cref="Link"/> carries a URL and
/// nothing else, so there is nowhere to put the answer, and upstream's own
/// handling of it (<c>link.targetPage</c>) has no counterpart in this view. The
/// Documentation Browser has always been in the same position — its manuals are
/// full of such links and none of them is clickable — and the contents list is
/// what stands in for them there. Recorded, not silently dropped.
/// </para>
/// </remarks>
public static class PdfLinks
{
    /// <summary>Reads every page's link annotations.</summary>
    /// <param name="path">The PDF.</param>
    /// <returns>One list per page, in page order; empty when the file cannot be
    /// read or carries no links at all.</returns>
    /// <remarks>
    /// ⚠ Costs a full read of the document's page tree, so it is done ONCE per
    /// manuscript, on a worker, exactly as the outline is
    /// (<see cref="Documentation.ManualOutline"/>).
    /// </remarks>
    public static IReadOnlyList<LinkList> Read(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return Array.Empty<LinkList>();
        }

        try
        {
            //Import rather than InformationOnly: the annotation dictionaries
            //have to be resolvable, and the cheap open does not carry them.
            using PdfDocument document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            List<LinkList> pages = new List<LinkList>(document.PageCount);
            bool any = false;

            for (int i = 0; i < document.PageCount; i++)
            {
                PdfPage page = document.Pages[i];
                List<Link> links = LinksOf(page);
                any |= links.Count > 0;
                pages.Add(new LinkList(links));
            }

            return any ? pages : Array.Empty<LinkList>();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or InvalidOperationException
            or NotSupportedException or ArgumentException)
        {
            //A file whose annotations cannot be read is a file with no links,
            //which is what almost every manuscript is anyway.
            return Array.Empty<LinkList>();
        }
    }

    /// <summary>
    /// Maps a rectangle in PDF user space onto the page AS IT IS DISPLAYED, in
    /// coordinates from 0.0 to 1.0 with the origin at the top-left.
    /// </summary>
    /// <param name="rectangle">The annotation's <c>/Rect</c>, in points.</param>
    /// <param name="box">The page's box, in points, BEFORE <c>/Rotate</c>.</param>
    /// <param name="rotate">The page's <c>/Rotate</c>, in degrees clockwise.</param>
    /// <returns>The area, as a link's four fractions.</returns>
    /// <remarks>
    /// <para>
    /// Two changes of frame, in this order. PDF counts from the BOTTOM-LEFT in
    /// points and a page carries an arbitrary origin, so the rectangle is first
    /// made relative to the box and flipped in <c>y</c>. Then <c>/Rotate</c> is
    /// applied, because the page is DISPLAYED turned by it — board trap 65 says
    /// <c>PdfPage.Width/Height</c> are already the turned box, and this is the
    /// other half of that same fact: the annotation's own coordinates are NOT
    /// turned, so the link would land on the wrong quarter of a rotated page if
    /// this step were skipped.
    /// </para>
    /// <para>
    /// A quarter turn clockwise sends the unrotated point
    /// (<c>u</c>, <c>v</c>) to (1&#160;&#8722;&#160;<c>v</c>, <c>u</c>); the
    /// other three follow from it.
    /// </para>
    /// </remarks>
    public static (float Left, float Top, float Right, float Bottom) AreaOf(
        (double X1, double Y1, double X2, double Y2) rectangle,
        (double X1, double Y1, double X2, double Y2) box,
        int rotate)
    {
        double width = box.X2 - box.X1;
        double height = box.Y2 - box.Y1;
        if (width <= 0.0 || height <= 0.0) { return (0f, 0f, 0f, 0f); }

        double left = Math.Min(rectangle.X1, rectangle.X2);
        double right = Math.Max(rectangle.X1, rectangle.X2);
        double bottom = Math.Min(rectangle.Y1, rectangle.Y2);
        double top = Math.Max(rectangle.Y1, rectangle.Y2);

        //Relative to the box, with y running DOWN as the view counts it.
        double u0 = (left - box.X1) / width;
        double u1 = (right - box.X1) / width;
        double v0 = 1.0 - ((top - box.Y1) / height);
        double v1 = 1.0 - ((bottom - box.Y1) / height);

        (double L, double T, double R, double B) turned = Turn(rotate) switch
        {
            90 => (1.0 - v1, u0, 1.0 - v0, u1),
            180 => (1.0 - u1, 1.0 - v1, 1.0 - u0, 1.0 - v0),
            270 => (v0, 1.0 - u1, v1, 1.0 - u0),
            _ => (u0, v0, u1, v1),
        };

        return ((float)turned.L, (float)turned.T, (float)turned.R, (float)turned.B);
    }

    /// <summary>Reduces any <c>/Rotate</c> to one of the four quarter turns.</summary>
    /// <param name="rotate">The value, in degrees.</param>
    /// <returns>0, 90, 180 or 270.</returns>
    /// <remarks>The specification allows any multiple of 90, positive or
    /// negative, so &#8722;90 and 630 both mean the same three-quarter turn.</remarks>
    public static int Turn(int rotate)
    {
        int quarters = ((rotate / 90) % 4 + 4) % 4;
        return quarters * 90;
    }

    private static List<Link> LinksOf(PdfPage page)
    {
        List<Link> links = new List<Link>();
        PdfAnnotations annotations = page.Annotations;
        if (annotations == null || annotations.Count == 0) { return links; }

        //The page's box BEFORE /Rotate. CropBox is what a viewer shows when it
        //is there; MediaBox is the sheet. PdfPage.Width/Height cannot be used
        //here — they are the box already turned (board trap 65).
        PdfRectangle raw = page.CropBox;
        if (raw == null || raw.Width <= 0 || raw.Height <= 0) { raw = page.MediaBox; }

        if (raw == null || raw.Width <= 0 || raw.Height <= 0) { return links; }

        (double, double, double, double) box = (raw.X1, raw.Y1, raw.X2, raw.Y2);
        int rotate = page.Rotate;

        for (int i = 0; i < annotations.Count; i++)
        {
            if (annotations[i] is not PdfDictionary annotation) { continue; }

            if (!string.Equals(
                annotation.Elements.GetName("/Subtype"), "/Link", StringComparison.Ordinal))
            {
                continue;
            }

            string url = UrlOf(annotation);
            if (string.IsNullOrEmpty(url)) { continue; }

            PdfRectangle rect = annotation.Elements.GetRectangle("/Rect");
            if (rect == null) { continue; }

            var (left, top, right, bottom) = AreaOf(
                (rect.X1, rect.Y1, rect.X2, rect.Y2), box, rotate);
            links.Add(new Link(left, top, right, bottom, url));
        }

        return links;
    }

    private static string UrlOf(PdfDictionary annotation)
    {
        //  /A << /S /URI /URI (https://…) >>   — the only shape that names a
        //  place OUTSIDE the document, and the shape a textedit:// link takes.
        if (annotation.Elements.GetDictionary("/A") is not PdfDictionary action)
        {
            return null;
        }

        if (!string.Equals(
            action.Elements.GetName("/S"), "/URI", StringComparison.Ordinal))
        {
            return null;
        }

        //The value is a PDF string object; there is no GetString on this
        //package's DictionaryElements, so the item is taken and read.
        string url = action.Elements["/URI"] is PdfString text ? text.Value : null;
        return string.IsNullOrEmpty(url) ? null : url;
    }
}
