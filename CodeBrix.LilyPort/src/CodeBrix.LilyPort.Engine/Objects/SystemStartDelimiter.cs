/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2000--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

using CodeBrix.LilyPort.Engine.Bootstrap;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/system-start-delimiter.cc, lily/include/system-start-delimiter.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Draws the brace, bracket, square or bar line in front of a system, sized to the
/// vertical span of the staves it groups — and REMOVES itself when that span
/// collapses below <c>collapse-height</c>, which is how a single unbraced staff loses
/// its delimiter.
/// <para>
/// The brace is not drawn here at all: it goes through the
/// <c>\left-brace</c> markup, whose Scheme body binary-searches the
/// <c>fetaBraces</c> font's <c>brace0…braceN</c> glyphs for the one nearest the
/// wanted height — the glyph search the upstream C++ reaches through
/// <c>Lily::make_left_brace_markup</c>.
/// </para>
/// </summary>
public static class SystemStartDelimiter
{
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");
    private static readonly Symbol LineThicknessSymbol = Symbol.Intern("line-thickness");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol CollapseHeightSymbol = Symbol.Intern("collapse-height");
    private static readonly Symbol OutputScaleSymbol = Symbol.Intern("output-scale");
    private static readonly Symbol BracketSymbol = Symbol.Intern("bracket");
    private static readonly Symbol BraceSymbol = Symbol.Intern("brace");
    private static readonly Symbol BarLineSymbol = Symbol.Intern("bar-line");
    private static readonly Symbol LineBracketSymbol = Symbol.Intern("line-bracket");
    private static readonly Symbol StaffSymbolInterface = Symbol.Intern("staff-symbol-interface");
    private static readonly Symbol MakeLeftBraceMarkupSymbol
        = Symbol.Intern("make-left-brace-markup");

    /// <summary>Draws the thick bracket, its tips taken from the music font.</summary>
    /// <param name="me">The delimiter grob.</param>
    /// <param name="height">The vertical span to cover.</param>
    /// <returns>The stencil.</returns>
    public static Stencil StaffBracket(Grob me, double height)
    {
        FontMetric fm = FontInterface.GetDefaultFont(me);

        DrulArray<Stencil> tips = new DrulArray<Stencil>(
            fm.FindByName("brackettips.down"),
            fm.FindByName("brackettips.up"));

        double thickness = NumberOr(me.GetProperty(ThicknessSymbol), 0.25);

        double overlap = 0.1 * thickness;

        Box bracketLineExtents = new Box(
            new Interval(0, thickness),
            new Interval(-1, 1) * ((height / 2) + overlap));

        Stencil bracket = Lookup.FilledBox(bracketLineExtents);
        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            bracket.AddAtEdge(Axis.Y, d, tips[d], -overlap);
        }

        // The reference for positioning the delimiter in X-direction should
        // be the bracket line, not the right bound of the bracket tips.
        // In Y-direction we have to take the tips into account, however,
        // to ensure correct bounding boxes with the EPS backend.
        // Therefore we take the X-dimensions only from the bracket line
        // and the Y-dimensions from the whole bracket.
        Box bracketExtents = new Box(bracketLineExtents[Axis.X], bracket.Extent(Axis.Y));
        bracket = new Stencil(bracketExtents, bracket.Expression);

        bracket.TranslateAxis(-0.8, Axis.X);

        return bracket;
    }

    /// <summary>Draws the square line bracket.</summary>
    /// <param name="me">The delimiter grob.</param>
    /// <param name="height">The vertical span to cover.</param>
    /// <returns>The stencil.</returns>
    public static Stencil LineBracket(Grob me, double height)
    {
        double thick = me.Layout.GetDimension(LineThicknessSymbol)
                       * NumberOr(me.GetProperty(ThicknessSymbol), 1);
        double w = 0.8;

        Stencil tip1 = LineInterface.MakeLine(
            thick, new Offset(0, -height / 2), new Offset(w, -height / 2));
        Stencil tip2 = LineInterface.MakeLine(
            thick, new Offset(0, height / 2), new Offset(w, height / 2));
        Stencil vline = LineInterface.MakeLine(
            thick, new Offset(0, -height / 2), new Offset(0, height / 2));

        vline.AddStencil(tip1);
        vline.AddStencil(tip2);
        vline.TranslateAxis(-w, Axis.X);
        return vline;
    }

    /// <summary>Draws the plain bar line.</summary>
    /// <param name="me">The delimiter grob.</param>
    /// <param name="h">The vertical span to cover.</param>
    /// <returns>The stencil.</returns>
    public static Stencil SimpleBar(Grob me, double h)
    {
        double lt = me.Layout.GetDimension(LineThicknessSymbol);
        double w = lt * NumberOr(me.GetProperty(ThicknessSymbol), 1);
        return Lookup.RoundFilledBox(
            new Box(new Interval(0, w), new Interval(-h / 2, h / 2)), lt);
    }

    /// <summary>
    /// The <c>ly:system-start-delimiter::print</c> callback body: measure the spanned
    /// staves, suicide when the span collapses, otherwise draw whichever style the
    /// grob asks for.
    /// </summary>
    /// <param name="me">The delimiter spanner.</param>
    /// <returns>The stencil, or <see langword="null"/> after a suicide.</returns>
    public static Stencil? Print(Spanner me)
    {
        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        Grob common = AxisGroupInterface.CommonRefpointOfArray(elts, me, Axis.Y);

        Interval ext = Interval.Empty;
        double staffspace = 1.0;
        for (int i = elts.Count; i-- > 0;)
        {
            Spanner sp = elts[i] as Spanner;

            if (sp != null
                && ReferenceEquals(sp.GetBound(Direction.Negative), me.GetBound(Direction.Negative)))
            {
                Interval dims = sp.Extent(common, Axis.Y);
                if (!dims.IsEmpty)
                {
                    ext.Unite(dims);
                    staffspace = StaffSpaceOf(sp);
                }
            }
        }

        object glyphSym = me.GetProperty(StyleSymbol);
        double len = ext.Length;

        // Use collapse-height in multiples of the staff-space
        if (ext.IsEmpty
            || (NumberOr(me.GetProperty(CollapseHeightSymbol), 0.0) >= (len / staffspace)))
        {
            me.Suicide();
            return null;
        }

        Stencil m = Stencil.Empty;
        if (ReferenceEquals(glyphSym, BracketSymbol))
        {
            m = StaffBracket(me, len);
        }
        else if (ReferenceEquals(glyphSym, BraceSymbol))
        {
            m = StaffBrace(me, len);
        }
        else if (ReferenceEquals(glyphSym, BarLineSymbol))
        {
            m = SimpleBar(me, len);
        }
        else if (ReferenceEquals(glyphSym, LineBracketSymbol))
        {
            m = LineBracket(me, len);
        }

        m.TranslateAxis(ext.Center, Axis.Y);
        return m;
    }

    /// <summary>
    /// Draws the piano brace by interpreting a <c>\left-brace</c> markup sized in
    /// points, which is where the <c>brace0…braceN</c> glyph search happens.
    /// </summary>
    /// <param name="me">The delimiter grob.</param>
    /// <param name="y">The vertical span to cover.</param>
    /// <returns>The stencil.</returns>
    public static Stencil StaffBrace(Grob me, double y)
    {
        double outputScale = SchemeConvert.ToDouble(
            me.Layout.LookupVariable(OutputScaleSymbol), "output-scale");
        object makeMarkup = LilyPondScheme.LookupProcedure(MakeLeftBraceMarkupSymbol);
        if (makeMarkup == null || LilyPondScheme.Current == null)
        {
            Warn.ProgrammingError(
                "make-left-brace-markup is not bound; the markup layer has not loaded");
            return Stencil.Empty;
        }

        object mkup = LilyPondScheme.Current.Evaluator.Apply(
            makeMarkup,
            new object[] { y * outputScale / Dimensions.Point });
        Stencil stil = TextInterface.GrobInterpretMarkup(me, mkup);
        stil.AlignTo(Axis.X, 0.0);
        stil.TranslateAxis(-0.2, Axis.X);
        return stil;
    }

    private static double StaffSpaceOf(Grob sp)
    {
        // Staff_symbol_referencer::get_staff_symbol answers the grob ITSELF when it
        // is a staff symbol; the port's StaffSymbolReferencer lacks that identity
        // branch (reported as a finding), so the delimiter takes the staff symbol's
        // own staff space directly.
        return sp.HasInterface(StaffSymbolInterface)
            ? StaffSymbol.StaffSpace(sp)
            : StaffSymbolReferencer.StaffSpace(sp);
    }

    private static double NumberOr(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "system-start-delimiter")
            : fallback;
}
