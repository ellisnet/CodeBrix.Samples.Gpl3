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
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Backends;

/// <summary>
/// The headless SVG backend: turns the drawing commands a stencil unfolds into a
/// self-contained SVG document.
/// <para>
/// New-in-family C#, not a translation of <c>scm/output-svg.scm</c>. It emits the same
/// element vocabulary — <c>line</c>, <c>rect</c>, <c>polygon</c>, <c>circle</c>,
/// <c>ellipse</c>, <c>path</c>, and <c>g</c> for every transform and colour scope — so
/// the output is comparable with LilyPond's own <c>-dbackend=svg</c>, but it is
/// deliberately driven from C# rather than through Scheme's <c>format</c>.
/// </para>
/// <para>
/// The Y axis is negated on the way out. LilyPond measures upward; SVG measures
/// downward. Every coordinate that reaches an attribute goes through
/// <see cref="FormatY"/>, and that is the only place the flip happens.
/// </para>
/// </summary>
public sealed class SvgBackend : IStencilSink
{
    private static readonly Symbol SetTranslation = Symbol.Intern("settranslation");
    private static readonly Symbol ResetTranslation = Symbol.Intern("resettranslation");
    private static readonly Symbol SetColor = Symbol.Intern("setcolor");
    private static readonly Symbol ResetColor = Symbol.Intern("resetcolor");
    private static readonly Symbol SetRotation = Symbol.Intern("setrotation");
    private static readonly Symbol ResetRotation = Symbol.Intern("resetrotation");
    private static readonly Symbol SetScale = Symbol.Intern("setscale");
    private static readonly Symbol ResetScale = Symbol.Intern("resetscale");
    private static readonly Symbol StartGroupNode = Symbol.Intern("start-group-node");
    private static readonly Symbol EndGroupNode = Symbol.Intern("end-group-node");
    private static readonly Symbol GrobCause = Symbol.Intern("grob-cause");
    private static readonly Symbol NoOrigin = Symbol.Intern("no-origin");
    private static readonly Symbol DrawLine = Symbol.Intern("draw-line");
    private static readonly Symbol DashedLine = Symbol.Intern("dashed-line");
    private static readonly Symbol RoundFilledBox = Symbol.Intern("round-filled-box");
    private static readonly Symbol PolygonHead = Symbol.Intern("polygon");
    private static readonly Symbol CircleHead = Symbol.Intern("circle");
    private static readonly Symbol EllipseHead = Symbol.Intern("ellipse");
    private static readonly Symbol PathHead = Symbol.Intern("path");
    private static readonly Symbol NamedGlyph = Symbol.Intern("named-glyph");
    private static readonly Symbol GlyphString = Symbol.Intern("glyph-string");
    private static readonly Symbol Utf8String = Symbol.Intern("utf-8-string");
    private static readonly Symbol UrlLink = Symbol.Intern("url-link");

    // output-svg.scm's pango-description-regexp-comma / -nocomma. A Pango description
    // ends in its size, optionally preceded by style words; everything before the match
    // is the family list.
    private static readonly Regex PangoDescriptionComma = new Regex(
        ",(?<bold> Bold)?(?<italic> Italic)?(?<smallcaps> Small-Caps)?[ -](?<size>[0-9.]+)$",
        RegexOptions.Compiled);

    private static readonly Regex PangoDescriptionNoComma = new Regex(
        "(?<bold> Bold)?(?<italic> Italic)?(?<smallcaps> Small-Caps)?[ -](?<size>[0-9.]+)$",
        RegexOptions.Compiled);

    private readonly StringBuilder _body = new StringBuilder();

    /// <summary>Gets or sets the number of decimal places written for coordinates.</summary>
    public int Precision { get; set; } = 4;

    /// <summary>
    /// Gets or sets the length of one output unit, in millimetres — the layout's
    /// <c>output-scale</c>, which is one staff space.
    /// <para>
    /// This is <c>output-svg.scm</c>'s <c>lily-unit-length</c>, which
    /// <c>framework-svg.scm</c> sets through <c>set-unit-length</c> before anything is
    /// drawn. Everything the document contains is measured in these units, and the one
    /// place the value is USED is converting a font size in points into them. The
    /// default matches a 20-point staff.
    /// </para>
    /// </summary>
    public double UnitLength { get; set; } = 1.7573;

    /// <summary>
    /// Gets the grobs that produced geometry, in draw order, each with the point it
    /// was drawn at. This is what makes point-and-click possible without a PDF
    /// round-trip.
    /// </summary>
    public List<(object Grob, Offset At)> Causes { get; } = new List<(object, Offset)>();

    /// <summary>Gets the drawing commands that were not understood, for diagnosis.</summary>
    public List<string> UnhandledCommands { get; } = new List<string>();

    /// <summary>Gets the SVG fragment produced so far, without a document wrapper.</summary>
    public string Body => _body.ToString();

    /// <summary>Clears everything the backend has accumulated.</summary>
    public void Clear()
    {
        _body.Clear();
        Causes.Clear();
        UnhandledCommands.Clear();
    }

    /// <summary>Renders a stencil into an SVG fragment.</summary>
    /// <param name="stencil">The stencil to render.</param>
    /// <returns>The fragment.</returns>
    public string RenderFragment(Stencil stencil)
    {
        Clear();
        StencilInterpreter.Interpret(stencil.Expression, this, Offset.Zero);
        return Body;
    }

    /// <summary>
    /// Renders a stencil into a complete SVG document sized to the stencil's own
    /// extents.
    /// </summary>
    /// <param name="stencil">The stencil to render.</param>
    /// <returns>The document text.</returns>
    public string RenderDocument(Stencil stencil)
    {
        string fragment = RenderFragment(stencil);

        Interval x = stencil.XExtent;
        Interval y = stencil.YExtent;
        if (x.IsEmpty)
        {
            x = new Interval(0, 0);
        }

        if (y.IsEmpty)
        {
            y = new Interval(0, 0);
        }

        StringBuilder document = new StringBuilder();
        document.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");

        // The xlink namespace is NOT optional: every glyph reference is an
        // xlink:href, and a document that uses the prefix without binding it is not
        // well-formed XML. It parses in a browser and fails in an XML parser — which is
        // how the regression comparator found this, reporting UNPARSEABLE on the very
        // first document the port produced. LilyPond's own SVG binds it too.
        document.Append("<svg xmlns=\"http://www.w3.org/2000/svg\"");
        document.Append(" xmlns:xlink=\"http://www.w3.org/1999/xlink\" version=\"1.2\"\n");
        // THE mm SIZE IS SCALED BY output-scale AND THE VIEW BOX IS NOT.
        // framework-svg.scm's output-stencil computes
        // `svg-width = output-scale * device-width' for the width and height attributes
        // and passes the UNSCALED extents to the view box, so the document declares its
        // real millimetre size while its coordinates stay in staff spaces. The port
        // omitted the factor, which made every page it wrote 1/output-scale of the size
        // the oracle writes — an A4 page came out 119.5 x 169.0 instead of 210 x 297.
        document.Append(string.Format(
            CultureInfo.InvariantCulture,
            "     width=\"{0}mm\" height=\"{1}mm\"\n",
            Format(x.Length * UnitLength),
            Format(y.Length * UnitLength)));

        // The view box is in LilyPond coordinates with Y already flipped, so the
        // fragment's own numbers need no further adjustment.
        document.Append(string.Format(
            CultureInfo.InvariantCulture,
            "     viewBox=\"{0} {1} {2} {3}\">\n",
            Format(x.Left),
            Format(-y.Right),
            Format(x.Length),
            Format(y.Length)));

        document.Append("<g fill=\"currentColor\" color=\"black\">\n");
        document.Append(fragment);
        document.Append("</g>\n");
        document.Append("</svg>\n");
        return document.ToString();
    }

    /// <summary>Handles one drawing command.</summary>
    /// <param name="expression">The command.</param>
    /// <returns><see langword="false"/> when the command was not understood.</returns>
    public object Output(object expression)
    {
        if (!(expression is Pair pair) || !(pair.Car is Symbol head))
        {
            return false;
        }

        List<object> args = Pair.ToList(expression);
        args.RemoveAt(0);

        if (ReferenceEquals(head, SetTranslation))
        {
            _body.Append(string.Format(
                CultureInfo.InvariantCulture,
                "<g transform=\"translate({0}, {1})\">\n",
                Format(Number(args, 0)),
                FormatY(Number(args, 1))));
            return true;
        }

        if (ReferenceEquals(head, ResetTranslation)
            || ReferenceEquals(head, ResetColor)
            || ReferenceEquals(head, ResetScale)
            || ReferenceEquals(head, EndGroupNode))
        {
            _body.Append("</g>\n");
            return true;
        }

        if (ReferenceEquals(head, ResetRotation))
        {
            _body.Append("</g>\n");
            return true;
        }

        if (ReferenceEquals(head, SetColor))
        {
            _body.Append(string.Format(
                CultureInfo.InvariantCulture,
                "<g color=\"rgba({0}%, {1}%, {2}%, {3}%)\">\n",
                Format(100 * Number(args, 0)),
                Format(100 * Number(args, 1)),
                Format(100 * Number(args, 2)),
                Format(100 * NumberOr(args, 3, 1.0))));
            return true;
        }

        if (ReferenceEquals(head, SetRotation))
        {
            _body.Append(string.Format(
                CultureInfo.InvariantCulture,
                "<g transform=\"rotate({0}, {1}, {2})\">\n",
                Format(-Number(args, 0)),
                Format(Number(args, 1)),
                FormatY(Number(args, 2))));
            return true;
        }

        if (ReferenceEquals(head, SetScale))
        {
            _body.Append(string.Format(
                CultureInfo.InvariantCulture,
                "<g transform=\"scale({0}, {1})\">\n",
                Format(Number(args, 0)),
                Format(Number(args, 1))));
            return true;
        }

        if (ReferenceEquals(head, StartGroupNode))
        {
            _body.Append("<g");

            // Due to the way nested grob properties work, we may see duplicate keys --
            // upstream's own comment, and upstream's own answer is (uniqued-alist
            // attributes hashq assq), which keeps the FIRST entry for each key. A later
            // \override goes on the FRONT of the alist, so first-wins is last-override-
            // wins. Emitting both produces a duplicate XML attribute and an unparseable
            // document, which is precisely what svg-duplicate-attribute.ly tests.
            HashSet<object> seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            object cursor = args.Count > 0 ? args[0] : Nil.Instance;
            while (cursor is Pair listPair)
            {
                if (listPair.Car is Pair entry && seen.Add(entry.Car))
                {
                    _body.Append(' ');
                    _body.Append(Text(entry.Car));
                    _body.Append("=\"");
                    _body.Append(Escape(Text(entry.Cdr)));
                    _body.Append('"');
                }

                cursor = listPair.Cdr;
            }

            _body.Append(">\n");
            return true;
        }

        if (ReferenceEquals(head, GrobCause))
        {
            Offset at = args.Count > 0 && args[0] is Pair point
                ? new Offset(ToDouble(point.Car), ToDouble(point.Cdr))
                : Offset.Zero;
            Causes.Add((args.Count > 1 ? args[1] : null, at));
            return true;
        }

        if (ReferenceEquals(head, NoOrigin))
        {
            return true;
        }

        if (ReferenceEquals(head, DrawLine))
        {
            EmitLine(Number(args, 0), Number(args, 1), Number(args, 2), Number(args, 3), Number(args, 4), null);
            return true;
        }

        if (ReferenceEquals(head, DashedLine))
        {
            string dash = string.Format(
                CultureInfo.InvariantCulture,
                " stroke-dasharray=\"{0},{1}\"",
                Format(Number(args, 1)),
                Format(Number(args, 2)));
            EmitLine(Number(args, 0), 0, 0, Number(args, 3), Number(args, 4), dash);
            return true;
        }

        if (ReferenceEquals(head, RoundFilledBox))
        {
            double breadth = Number(args, 0);
            double width = Number(args, 1);
            double depth = Number(args, 2);
            double height = Number(args, 3);
            double blot = Number(args, 4);

            _body.Append(string.Format(
                CultureInfo.InvariantCulture,
                "<rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\" ry=\"{4}\" fill=\"currentColor\"/>\n",
                Format(-breadth),
                Format(-height),
                Format(breadth + width),
                Format(depth + height),
                Format(blot / 2)));
            return true;
        }

        if (ReferenceEquals(head, PolygonHead))
        {
            List<double> coordinates = Numbers(args.Count > 0 ? args[0] : Nil.Instance);
            double blot = Number(args, 1);
            bool filled = IsTrue(args.Count > 2 ? args[2] : false);

            StringBuilder points = new StringBuilder();
            for (int i = 0; i + 1 < coordinates.Count; i += 2)
            {
                if (points.Length > 0)
                {
                    points.Append(' ');
                }

                points.Append(Format(coordinates[i]));
                points.Append(' ');
                points.Append(FormatY(coordinates[i + 1]));
            }

            _body.Append(string.Format(
                CultureInfo.InvariantCulture,
                "<polygon stroke-linejoin=\"round\" stroke-linecap=\"round\" stroke-width=\"{0}\""
                + " fill=\"{1}\" stroke=\"currentColor\" points=\"{2}\"/>\n",
                Format(blot),
                filled ? "currentColor" : "none",
                points));
            return true;
        }

        if (ReferenceEquals(head, CircleHead))
        {
            _body.Append(string.Format(
                CultureInfo.InvariantCulture,
                "<circle stroke-linejoin=\"round\" stroke-linecap=\"round\" fill=\"{0}\""
                + " stroke=\"currentColor\" stroke-width=\"{1}\" r=\"{2}\"/>\n",
                IsTrue(args.Count > 2 ? args[2] : false) ? "currentColor" : "none",
                Format(Number(args, 1)),
                Format(Number(args, 0))));
            return true;
        }

        if (ReferenceEquals(head, EllipseHead))
        {
            _body.Append(string.Format(
                CultureInfo.InvariantCulture,
                "<ellipse stroke-linejoin=\"round\" stroke-linecap=\"round\" fill=\"{0}\""
                + " stroke=\"currentColor\" stroke-width=\"{1}\" rx=\"{2}\" ry=\"{3}\"/>\n",
                IsTrue(args.Count > 3 ? args[3] : false) ? "currentColor" : "none",
                Format(Number(args, 2)),
                Format(Number(args, 0)),
                Format(Number(args, 1))));
            return true;
        }

        if (ReferenceEquals(head, PathHead))
        {
            double thickness = Number(args, 0);
            bool filled = args.Count > 5 && IsTrue(args[5]);
            string data = PathData(args.Count > 1 ? args[1] : Nil.Instance);

            _body.Append(string.Format(
                CultureInfo.InvariantCulture,
                "<path stroke-linejoin=\"round\" stroke-linecap=\"round\" stroke-width=\"{0}\""
                + " stroke=\"currentColor\" fill=\"{1}\" d=\"{2}\"/>\n",
                Format(thickness),
                filled ? "currentColor" : "none",
                data));
            return true;
        }

        if (ReferenceEquals(head, NamedGlyph))
        {
            return EmitNamedGlyph(
                args.Count > 0 ? args[0] : null,
                Text(args.Count > 1 ? args[1] : Nil.Instance));
        }

        if (ReferenceEquals(head, Utf8String))
        {
            // (utf-8-string PANGO-DESCRIPTION STRING ORIGINAL-EXPRESSION). The third
            // element is the glyph-by-glyph drawing the encapsulation replaces; the SVG
            // backend deliberately does NOT use it, because upstream's SVG output sets
            // real text and lets the viewer's own font engine draw it.
            return EmitText(
                Text(args.Count > 0 ? args[0] : Nil.Instance),
                Text(args.Count > 1 ? args[1] : Nil.Instance));
        }

        if (ReferenceEquals(head, UrlLink))
        {
            EmitUrlLink(
                Text(args.Count > 0 ? args[0] : Nil.Instance),
                args.Count > 1 ? args[1] : Nil.Instance,
                args.Count > 2 ? args[2] : Nil.Instance);
            return true;
        }

        if (ReferenceEquals(head, GlyphString))
        {
            // Not handled here. Returning false lets the interpreter fall back to the
            // stencil the expression carries alongside it.
            UnhandledCommands.Add(head.Name);
            return false;
        }

        UnhandledCommands.Add(head.Name);
        return false;
    }

    /// <summary>
    /// Emits a hyperlink hot-zone, which is what <c>output-svg.scm</c>'s
    /// <c>url-link</c> does: an <c>&lt;a&gt;</c> wrapping one INVISIBLE
    /// <c>&lt;rect&gt;</c> covering the linked markup's extents.
    /// <para>
    /// The rect draws nothing — <c>fill="none" stroke="none"</c> — so it is pure
    /// clickable area; the linked text is set separately by the sibling stencil. That
    /// invisibility is why its absence went unnoticed for the whole port: it changes no
    /// pixel, and it appears on 2,098 of the 2,316 reference pages because the
    /// "Music engraving by LilyPond" tagline carries one.
    /// </para>
    /// <para>
    /// Two things here are upstream's shape and are deliberately NOT tidied. First, the
    /// Y coordinate is written RAW rather than through <see cref="FormatY"/>: upstream
    /// writes <c>(car y)</c> and the interval's own height, so the rect sits mirrored
    /// about the baseline relative to the text it covers. It is invisible, upstream has
    /// always done it, and negating here would be a parity bug. Second, the URL is NOT
    /// XML-escaped, because upstream's <c>attributes</c> helper interpolates it with
    /// <c>~a</c> and escapes nothing; a URL containing an ampersand produces the same
    /// malformed document on both sides.
    /// </para>
    /// </summary>
    /// <param name="url">The link target.</param>
    /// <param name="xExtent">The linked markup's X interval, as a Scheme pair.</param>
    /// <param name="yExtent">The linked markup's Y interval, as a Scheme pair.</param>
    private void EmitUrlLink(string url, object xExtent, object yExtent)
    {
        (double left, double right) = Interval(xExtent);
        (double bottom, double top) = Interval(yExtent);

        _body.Append(string.Format(
            CultureInfo.InvariantCulture,
            "<a xlink:href=\"{0}\">\n"
            + "<rect x=\"{1}\" y=\"{2}\" width=\"{3}\" height=\"{4}\""
            + " fill=\"none\" stroke=\"none\" stroke-width=\"0.0\"/>\n"
            + "</a>\n",
            url,
            Format(left),
            Format(bottom),
            Format(right - left),
            Format(top - bottom)));
    }

    /// <summary>
    /// Reads a Scheme interval pair as a pair of doubles, answering an empty interval
    /// at the origin when the expression is not a pair.
    /// </summary>
    /// <param name="value">The expression to read.</param>
    /// <returns>The interval's start and end.</returns>
    private static (double Start, double End) Interval(object value)
        => value is Pair pair ? (ToDouble(pair.Car), ToDouble(pair.Cdr)) : (0.0, 0.0);

    /// <summary>
    /// Emits one music glyph as its outline, which is what
    /// <c>output-svg.scm</c>'s <c>named-glyph</c> does by way of
    /// <c>font-smob-to-text</c> and <c>dump-path</c>.
    /// <para>
    /// The path data is the font's own <c>d</c> attribute, copied verbatim in FONT
    /// units, and the whole outline is scaled down by the drawing size over the units
    /// per em — with the Y factor negated, which is where a glyph gets flipped into
    /// SVG's downward axis. That is why glyph coordinates do NOT go through
    /// <see cref="FormatY"/>: the flip is in the transform, and applying it twice would
    /// turn every note head upside down.
    /// </para>
    /// </summary>
    /// <param name="font">The font the stencil named.</param>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns><see langword="true"/> when a glyph was emitted or deliberately skipped.</returns>
    private bool EmitNamedGlyph(object font, string glyphName)
    {
        if (!(font is FontMetric metric))
        {
            UnhandledCommands.Add(NamedGlyph.Name);
            return false;
        }

        string outline = metric.GlyphOutline(glyphName);
        if (string.IsNullOrEmpty(outline))
        {
            // A glyph the font has no outline for is a space, and a name the font does
            // not know produced an empty stencil upstream of here. Neither draws.
            return true;
        }

        // TODO: not urgent, but do not hard-code this value. Carried over from
        // output-svg.scm's extract-glyph, which hardcodes 1000 with that same remark;
        // every shipped music font is drawn on a 1000-unit em, so the two agree.
        const double UnitsPerEm = 1000.0;

        string scale = Format(metric.FontScaling / UnitsPerEm);

        _body.Append(string.Format(
            CultureInfo.InvariantCulture,
            "<path transform=\"scale({0}, -{0})\" d=\"{1}\" fill=\"currentColor\"/>\n",
            scale,
            outline));
        return true;
    }

    /// <summary>
    /// Emits a run of text as an SVG <c>text</c> element, which is what
    /// <c>output-svg.scm</c>'s <c>utf-8-string</c> and
    /// <c>pango-description-to-text</c> do.
    /// <para>
    /// Text is NOT converted to outlines. The engine has already decided where the run
    /// sits and how much room it takes; the document names the family, style and size
    /// and lets the viewer draw it. That is upstream's SVG behaviour, and matching it
    /// is what makes the two documents comparable at all.
    /// </para>
    /// </summary>
    /// <param name="description">The Pango description string, such as <c>serif Bold 8</c>.</param>
    /// <param name="text">The text to set.</param>
    /// <returns><see langword="true"/> when the run was emitted.</returns>
    private bool EmitText(string description, string text)
    {
        Match match = PangoDescriptionComma.Match(description ?? string.Empty);
        if (!match.Success)
        {
            match = PangoDescriptionNoComma.Match(description ?? string.Empty);
        }

        if (!match.Success)
        {
            // Upstream warns and emits an element with no attributes at all rather
            // than dropping the text.
            UnhandledCommands.Add("utf-8-string: cannot decypher Pango description: "
                + description);
            _body.Append("<text>\n<tspan>");
            _body.Append(EscapeText(text));
            _body.Append("</tspan>\n</text>\n");
            return true;
        }

        _body.Append("<text font-family=\"");
        _body.Append(Escape(description.Substring(0, match.Index)));
        _body.Append('"');

        if (match.Groups["bold"].Success)
        {
            _body.Append(" font-weight=\"bold\"");
        }

        if (match.Groups["italic"].Success)
        {
            _body.Append(" font-style=\"italic\"");
        }

        if (match.Groups["smallcaps"].Success)
        {
            _body.Append(" font-variant=\"small-caps\"");
        }

        double size = double.TryParse(
            match.Groups["size"].Value,
            System.Globalization.NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double parsed)
            ? parsed
            : 0.0;

        _body.Append(string.Format(
            CultureInfo.InvariantCulture,
            " font-size=\"{0}\" text-anchor=\"start\" fill=\"currentColor\">\n<tspan>",
            Format(size / UnitLength)));

        _body.Append(EscapeText(text));
        _body.Append("</tspan>\n</text>\n");
        return true;
    }

    private void EmitLine(double thickness, double x1, double y1, double x2, double y2, string extra)
        => _body.Append(string.Format(
            CultureInfo.InvariantCulture,
            "<line stroke-linejoin=\"round\" stroke-linecap=\"round\" stroke-width=\"{0}\""
            + " stroke=\"currentColor\" x1=\"{1}\" y1=\"{2}\" x2=\"{3}\" y2=\"{4}\"{5}/>\n",
            Format(thickness),
            Format(x1),
            FormatY(y1),
            Format(x2),
            FormatY(y2),
            extra ?? string.Empty));

    private string PathData(object commands)
    {
        StringBuilder data = new StringBuilder();
        object cursor = commands;

        while (cursor is Pair pair)
        {
            if (!(pair.Car is Symbol op))
            {
                cursor = pair.Cdr;
                continue;
            }

            cursor = pair.Cdr;

            int count = op.Name switch
            {
                "moveto" => 2,
                "rmoveto" => 2,
                "lineto" => 2,
                "rlineto" => 2,
                "curveto" => 6,
                "rcurveto" => 6,
                _ => 0,
            };

            string letter = op.Name switch
            {
                "moveto" => "M",
                "rmoveto" => "m",
                "lineto" => "L",
                "rlineto" => "l",
                "curveto" => "C",
                "rcurveto" => "c",
                "closepath" => "z",
                _ => string.Empty,
            };

            if (letter.Length == 0)
            {
                continue;
            }

            if (data.Length > 0)
            {
                data.Append(' ');
            }

            data.Append(letter);

            for (int i = 0; i < count; i += 2)
            {
                double x = 0;
                double y = 0;
                if (cursor is Pair xp)
                {
                    x = ToDouble(xp.Car);
                    cursor = xp.Cdr;
                }

                if (cursor is Pair yp)
                {
                    y = ToDouble(yp.Car);
                    cursor = yp.Cdr;
                }

                data.Append(' ');
                data.Append(Format(x));
                data.Append(' ');
                data.Append(FormatY(y));
            }
        }

        return data.ToString();
    }

    private string Format(double value)
        => value.ToString("F" + Precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a vertical coordinate, negating it. LilyPond measures upward and SVG
    /// measures downward; this is the ONE place that flip happens.
    /// </summary>
    /// <param name="value">The LilyPond coordinate.</param>
    /// <returns>The SVG coordinate.</returns>
    private string FormatY(double value) => Format(-value);

    private static double Number(List<object> args, int index)
        => index < args.Count ? ToDouble(args[index]) : 0.0;

    private static double NumberOr(List<object> args, int index, double fallback)
        => index < args.Count ? ToDouble(args[index]) : fallback;

    private static List<double> Numbers(object list)
    {
        List<double> result = new List<double>();
        object cursor = list;
        while (cursor is Pair pair)
        {
            result.Add(ToDouble(pair.Car));
            cursor = pair.Cdr;
        }

        return result;
    }

    private static bool IsTrue(object value) => value is bool flag && flag;

    private static string Text(object value)
    {
        switch (value)
        {
            case null:
                return string.Empty;
            case Symbol symbol:
                return symbol.Name;
            case string text:
                return text;
            case MutableString mutable:
                return mutable.ToString();
            default:
                return value.ToString();
        }
    }

    private static string Escape(string text)
        => text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>
    /// Escapes the CONTENT of a text run, replacing exactly what upstream's
    /// <c>utf-8-string</c> replaces: <c>&amp;</c> first and then <c>&lt;</c>, and
    /// nothing else. A bare <c>&gt;</c> is legal character data and upstream leaves it
    /// alone, so escaping it here would be a gratuitous byte-level divergence.
    /// </summary>
    /// <param name="text">The text to set.</param>
    /// <returns>The escaped text.</returns>
    private static string EscapeText(string text)
        => (text ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal);

    private static double ToDouble(object value)
    {
        switch (value)
        {
            case double d:
                return d;
            case long l:
                return l;
            case int i:
                return i;
            case System.Numerics.BigInteger big:
                return (double)big;
            case CodeBrix.LilyScheme.Numeric.Ratio ratio:
                return (double)ratio.Numerator / (double)ratio.Denominator;
            default:
                return 0.0;
        }
    }
}
