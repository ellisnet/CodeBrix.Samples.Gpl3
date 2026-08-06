/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Fonts; //was previously: lily/freetype.cc;

// Modified by Jeremy Ellis on 2026-08-05 as part of the CodeBrix port.

/// <summary>
/// Traces a glyph's outline into a skyline — upstream's <c>Path_interpreter</c> and
/// <c>ly_FT_add_outline_to_skyline</c>.
/// <para>
/// Only this much of <c>freetype.cc</c> is ported. FreeType itself is a
/// <c>no-port</c> row in the ledger: the port reads font tables directly and runs the
/// charstrings, so it has no <c>FT_Face</c>, no rasterizer and no hinting. What it does
/// need is the DECOMPOSE loop — the part that turns an outline into line segments a
/// skyline can be built from — and that is what this file is.
/// </para>
/// <para>
/// Two decisions here shape the answer rather than refine it, and both are carried over
/// unchanged under the faithfulness rule:
/// </para>
/// <list type="number">
/// <item>Segments are added as ORIENTED CONTOUR segments, so each joins the one skyline
/// whose side of the outline is solid. That is what makes the upper skyline follow the
/// top of a note head instead of its bounding box. The handedness is a property of the
/// font format — PostScript/CFF outlines run counter-clockwise, TrueType clockwise — and
/// getting it backwards yields a skyline that is inside out, with no error.</item>
/// <item>A curve is flattened into roughly one segment per 0.2 output units, and its
/// LAST piece is added as a plain segment rather than a contour segment, so that one
/// lands in both skylines. Upstream's asymmetry, kept: a plausible tidy-up is a parity
/// bug.</item>
/// </list>
/// <para>
/// One difference is forced by where the outline comes from. FreeType's decomposer emits
/// the segment that closes each contour; a Type 2 charstring leaves it implicit, so this
/// emits it itself when a contour ends.
/// </para>
/// </summary>
public static class GlyphOutlineSkyline
{
    private const double QuantizationUnit = 0.2;

    /// <summary>
    /// Traces one glyph of a font into a skyline collector.
    /// </summary>
    /// <param name="font">The font holding the glyph programs.</param>
    /// <param name="skyline">The collector to trace into.</param>
    /// <param name="transform">
    /// The transform from the font's design units to output units.
    /// </param>
    /// <param name="index">The glyph index.</param>
    public static void AddOutline(
        CffFont font, LazySkylinePair skyline, Transform transform, int index)
    {
        if (font == null)
        {
            throw new ArgumentNullException(nameof(font));
        }

        if (skyline == null)
        {
            throw new ArgumentNullException(nameof(skyline));
        }

        if (index < 0 || index >= font.GlyphCount)
        {
            return;
        }

        Walker walker = new Walker(skyline, transform);
        CharstringRun run = new CharstringRun(font, index) { Sink = walker };
        run.Execute();
        walker.Finish();
    }

    /// <summary>
    /// Turns a glyph's drawing commands into skyline segments — upstream's
    /// <c>Path_interpreter</c>.
    /// </summary>
    private sealed class Walker : IGlyphPathSink
    {
        private readonly LazySkylinePair _skyline;
        private readonly Transform _transform;

        private Offset _current;
        private Offset _start;
        private bool _open;

        internal Walker(LazySkylinePair skyline, Transform transform)
        {
            _skyline = skyline;
            _transform = transform;
        }

        public void MoveTo(double x, double y)
        {
            Finish();
            _current = new Offset(x, y);
            _start = _current;
            _open = true;
        }

        public void LineTo(double x, double y)
        {
            Offset destination = new Offset(x, y);
            _skyline.AddContourSegment(
                _transform, Orientation.CounterClockwise, _current, destination);
            _current = destination;
        }

        public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            Bezier curve = new Bezier(new[]
            {
                _current,
                new Offset(x1, y1),
                new Offset(x2, y2),
                new Offset(x3, y3),
            });

            // The step count is measured in OUTPUT units — the chord between the
            // transformed endpoints — so a glyph set small is flattened coarsely and the
            // same glyph set large gets more segments.
            Offset chord = _transform.Apply(curve[3]) - _transform.Apply(curve[0]);
            int quantization = Math.Max(2, (int)(chord.Length / QuantizationUnit));

            for (int i = 1; i < quantization; i++)
            {
                Offset point = curve.CurvePoint(i / (double)quantization);
                _skyline.AddContourSegment(
                    _transform, Orientation.CounterClockwise, _current, point);
                _current = point;
            }

            _skyline.AddSegment(_transform, _current, curve[3]);
            _current = curve[3];
        }

        public void ClosePath() => Finish();

        /// <summary>
        /// Emits the segment back to the contour's start, which a charstring leaves
        /// implicit.
        /// </summary>
        internal void Finish()
        {
            if (_open && _current != _start)
            {
                _skyline.AddContourSegment(
                    _transform, Orientation.CounterClockwise, _current, _start);
            }

            _open = false;
        }
    }
}
