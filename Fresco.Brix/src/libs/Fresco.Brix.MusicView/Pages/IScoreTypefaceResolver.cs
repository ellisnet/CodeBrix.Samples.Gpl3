// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using SkiaSharp;

namespace Fresco.Brix.MusicView;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Answers the font families a page of engraved music names in its
/// <c>&lt;text&gt;</c> elements.
/// </summary>
/// <remarks>
/// <para>
/// Board trap 9. The music glyphs in the engine's SVG are outlines and need no
/// font at all, but titles, lyrics, dynamics and the tagline are real text with
/// a family name — <c>serif</c>, <c>sans</c>, <c>monospace</c> or a name a
/// document asked for by hand. Those must resolve to the very faces the ENGINE
/// measured the text with, or the words are laid out for one font and drawn in
/// another; and they must never fall through to a system font, which is the
/// standing house rule.
/// </para>
/// <para>
/// The view therefore asks its host, which is the only side that knows the
/// engine. A host that answers nothing gets tofu, which is the wanted failure.
/// </para>
/// </remarks>
public interface IScoreTypefaceResolver
{
    /// <summary>Returns the face to draw a run of score text with.</summary>
    /// <param name="familyName">
    /// The family the SVG named. May be a comma-separated CSS list.
    /// </param>
    /// <param name="weight">The weight asked for.</param>
    /// <param name="width">The width asked for.</param>
    /// <param name="slant">The slant asked for.</param>
    /// <returns>The face, or null when nothing in the host's chain provides it.</returns>
    SKTypeface Resolve(string familyName, SKFontStyleWeight weight, SKFontStyleWidth width, SKFontStyleSlant slant);
}
