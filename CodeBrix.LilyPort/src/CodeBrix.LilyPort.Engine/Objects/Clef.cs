/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/clef.cc, lily/include/clef.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// A clef sign.
/// <para>
/// The grob carries a <c>glyph</c> — the base font name such as <c>clefs.G</c> — and
/// derives the actual <c>glyph-name</c> from it, appending <c>_change</c> for a
/// mid-line clef change so the smaller variant is drawn.
/// </para>
/// </summary>
public static class Clef
{
    private static readonly Symbol GlyphSymbol = Symbol.Intern("glyph");
    private static readonly Symbol GlyphNameSymbol = Symbol.Intern("glyph-name");
    private static readonly Symbol NonDefaultSymbol = Symbol.Intern("non-default");
    private static readonly Symbol FullSizeChangeSymbol = Symbol.Intern("full-size-change");

    /// <summary>
    /// The <c>glyph-name</c> callback: the font glyph this clef draws.
    /// <para>
    /// A clef with no <c>glyph</c> at all commits suicide rather than drawing nothing,
    /// which is how <c>\clef</c> with an unknown name removes the grob instead of
    /// leaving an empty one occupying horizontal space.
    /// </para>
    /// </summary>
    /// <param name="grob">The clef.</param>
    /// <returns>The glyph name, or the unspecified value when the clef killed itself.</returns>
    public static object CalcGlyphName(Grob grob)
    {
        object glyph = grob.GetProperty(GlyphSymbol);

        if (glyph is MutableString text)
        {
            string name = text.ToString();

            if (SchemeUtilities.ToBool(grob.GetProperty(NonDefaultSymbol))
                && (!(grob is Item item) || item.BreakStatusDirection() != Direction.Positive)
                && !SchemeUtilities.ToBool(grob.GetProperty(FullSizeChangeSymbol)))
            {
                name += "_change";
            }

            return new MutableString(name);
        }

        grob.Suicide();
        return Unspecified.Instance;
    }

    /// <summary>The <c>stencil</c> callback: the clef glyph, from the music font.</summary>
    /// <param name="grob">The clef.</param>
    /// <returns>The stencil, or the empty list when there is no glyph name.</returns>
    public static object Print(Grob grob)
    {
        if (!(grob.GetProperty(GlyphNameSymbol) is MutableString glyphName))
        {
            return Nil.Instance;
        }

        string glyph = glyphName.ToString();
        FontMetric font = FontInterface.GetDefaultFont(grob);
        if (font == null)
        {
            return Nil.Instance;
        }

        Stencil result = font.FindByName(glyph);
        if (result.IsEmpty)
        {
            Warn.Warning("clef `" + glyph + "' not found");
        }

        return result;
    }
}
