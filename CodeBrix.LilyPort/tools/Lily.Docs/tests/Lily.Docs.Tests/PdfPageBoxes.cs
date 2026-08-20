// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lily.Docs.Tests;

/// <summary>
/// Reads the page boxes out of a written PDF's own bytes.
/// <para>
/// ⚠ THE POINT IS THAT IT ASKS THE FILE. Wave LD4 set the page size through
/// <c>HtmlRenderOptions.SetPageSize</c> and read the resulting points back off the same
/// options object — which proves the package understood the name, and proves nothing at all
/// about what reached the paper. A manual whose options say A4 and whose pages are US Letter
/// is wrong in no way any count in this suite can see, and that is exactly the shape of the
/// defect wave LD4 was sent to fix: wave LD1's Internals Reference was 612&#215;792 for a
/// day with every gate green.
/// </para>
/// </summary>
internal static class PdfPageBoxes
{
    // The port's PDFs carry no object streams, so every page dictionary is in plain bytes
    // and the boxes can be read without a PDF parser. ⚠ That is an OBSERVATION about the
    // writer, not a guarantee — so ReadMediaBoxes returning nothing is treated by its
    // callers as a failure to measure rather than as a page with no box.
    private static readonly Regex MediaBox = new Regex(
        @"/MediaBox\s*\[\s*(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s*\]",
        RegexOptions.Compiled);

    /// <summary>Reads every <c>/MediaBox</c> in a PDF, as width-by-height in points.</summary>
    /// <param name="pdfBytes">The PDF's bytes.</param>
    /// <returns>One entry per box found, in file order.</returns>
    public static List<PdfBox> ReadMediaBoxes(byte[] pdfBytes)
    {
        if (pdfBytes == null)
        {
            throw new ArgumentNullException(nameof(pdfBytes));
        }

        // Latin-1 rather than UTF-8: a PDF's structure is ASCII but its streams are
        // arbitrary bytes, and a decoder that replaced invalid sequences could merge or
        // split the text around a box and lose it.
        string text = Encoding.Latin1.GetString(pdfBytes);
        List<PdfBox> boxes = new List<PdfBox>();
        foreach (Match match in MediaBox.Matches(text))
        {
            double left = Number(match.Groups[1].Value);
            double bottom = Number(match.Groups[2].Value);
            double right = Number(match.Groups[3].Value);
            double top = Number(match.Groups[4].Value);
            boxes.Add(new PdfBox(right - left, top - bottom));
        }

        return boxes;
    }

    /// <summary>The distinct page sizes a PDF uses, as <c>WxH</c> strings.</summary>
    /// <param name="pdfBytes">The PDF's bytes.</param>
    /// <returns>The distinct sizes, sorted.</returns>
    /// <remarks>
    /// Distinct sizes rather than the first one, because a document that changed page size
    /// part way through would otherwise pass on its first page.
    /// </remarks>
    public static SortedSet<string> DistinctPageSizes(byte[] pdfBytes)
    {
        SortedSet<string> sizes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (PdfBox box in ReadMediaBoxes(pdfBytes))
        {
            sizes.Add(box.ToString());
        }

        return sizes;
    }

    private static double Number(string text) =>
        double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}

/// <summary>One page box, in points.</summary>
internal readonly struct PdfBox
{
    /// <summary>Creates a box.</summary>
    /// <param name="widthPoints">Width in points.</param>
    /// <param name="heightPoints">Height in points.</param>
    public PdfBox(double widthPoints, double heightPoints)
    {
        WidthPoints = widthPoints;
        HeightPoints = heightPoints;
    }

    /// <summary>Width in points.</summary>
    public double WidthPoints { get; }

    /// <summary>Height in points.</summary>
    public double HeightPoints { get; }

    /// <summary>Renders the box as <c>WxH</c>.</summary>
    /// <returns>The size.</returns>
    public override string ToString() =>
        WidthPoints.ToString("0.####", CultureInfo.InvariantCulture) + "x"
        + HeightPoints.ToString("0.####", CultureInfo.InvariantCulture);
}
