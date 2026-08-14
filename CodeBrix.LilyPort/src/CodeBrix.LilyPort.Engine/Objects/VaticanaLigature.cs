/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2003--2026 Juergen Reuter <reuter@ipd.uka.de>

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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/vaticana-ligature.cc, lily/include/vaticana-ligature.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - STACKED_HEAD is a preprocessor #define in the header; it is a public const here,
//     renamed to PascalCase. Its VALUE extends gregorian-ligature.hh's context-info
//     family and is written into the `context-info' grob property, so it may not change.
//   - upstream's free functions vaticana_brew_cauda/_flexa/_join have header
//     declarations upstream but no caller outside this file and its engraver, so they are
//     private statics.
//   - ⚠ upstream writes `Bezier top_curve = curve, bottom_curve = curve;', which COPIES
//     in C++. Flower's Bezier is a CLASS here, so the same line would alias one curve
//     three ways and every control-point adjustment would land on all three. CopyOf makes
//     the copy explicit.

/// <summary>
/// A Vaticana-style Gregorian ligature: the square-notation neume shapes of the Editio
/// Vaticana, drawn head by head.
/// </summary>
/// <remarks>
/// <para>
/// As with the mensural ligature, the spanner draws nothing. Every mark comes from a note
/// head whose <c>stencil</c> <see cref="Translation.VaticanaLigatureEngraver"/> has
/// replaced with <see cref="BrewLigaturePrimitive"/>, and whose <c>glyph-name</c> that
/// engraver chose from the head's prefixes and its neighbours.
/// </para>
/// <para>
/// Three shapes are drawn here rather than fetched from the font: the CAUDA (the
/// descending tail whose length depends on where the head sits relative to the staff
/// lines), the curved FLEXA that joins two heads of a porrectus, and the vertical JOIN
/// between the two heads of a pes.
/// </para>
/// </remarks>
public static class VaticanaLigature
{
    private static readonly Symbol AddCaudaSymbol = Symbol.Intern("add-cauda");
    private static readonly Symbol AddJoinSymbol = Symbol.Intern("add-join");
    private static readonly Symbol AddStemSymbol = Symbol.Intern("add-stem");
    private static readonly Symbol BlotDiameterSymbol = Symbol.Intern("blot-diameter");
    private static readonly Symbol DeltaPositionSymbol = Symbol.Intern("delta-position");
    private static readonly Symbol FlexaHeightSymbol = Symbol.Intern("flexa-height");
    private static readonly Symbol FlexaWidthSymbol = Symbol.Intern("flexa-width");
    private static readonly Symbol GlyphNameSymbol = Symbol.Intern("glyph-name");
    private static readonly Symbol HeadXOffsetSymbol = Symbol.Intern("head-x-offset");
    private static readonly Symbol LineThicknessSymbol = Symbol.Intern("line-thickness");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");

    /// <summary>
    /// This head is stacked on the previous one — the context-info bit Vaticana adds to
    /// the family <see cref="GregorianLigature"/> defines.
    /// </summary>
    public const int StackedHead = 0x0100;

    /// <summary>
    /// The stencil of ONE head of the ligature — installed on each head by the engraver,
    /// which is why it is a callback over a note head rather than over the spanner.
    /// </summary>
    /// <param name="me">The ligature head.</param>
    /// <returns>The stencil.</returns>
    public static object BrewLigaturePrimitive(Grob me) => BrewPrimitive(me);

    /// <summary>The <c>stencil</c> callback: the ligature spanner draws nothing itself.</summary>
    /// <param name="me">The ligature spanner.</param>
    /// <returns><c>'()</c>, always.</returns>
    public static object Print(Grob me) => Nil.Instance;

    private static Stencil BrewCauda(
        Grob me, int pos, int deltaPitch, double thickness, double blotDiameter)
    {
        bool onStaffLine = StaffSymbolReferencer.OnLine(me, pos);
        bool aboveStaff = pos > StaffSymbolReferencer.StaffSpan(me)[Direction.Positive];

        if (deltaPitch > -1)
        {
            me.ProgrammingError("flexa cauda: invalid delta_pitch; assuming -1");
            deltaPitch = -1;
        }

        double length;
        if (onStaffLine)
        {
            if (deltaPitch >= -1)
            {
                length = 1.30;
            }
            else if (deltaPitch >= -2)
            {
                length = 1.35;
            }
            else
            {
                length = 1.85;
            }
        }
        else
        {
            if (deltaPitch >= -1)
            {
                length = aboveStaff ? 1.30 : 1.00;
            }
            else if (deltaPitch >= -2)
            {
                length = 1.35;
            }
            else if (deltaPitch >= -3)
            {
                length = 1.50;
            }
            else
            {
                length = 1.85;
            }
        }

        Box caudaBox = new Box(new Interval(0, thickness), new Interval(-length, 0));
        return Lookup.RoundFilledBox(caudaBox, blotDiameter);
    }

    /*
     * TODO: move this function to class Lookup?
     */
    private static Stencil BrewFlexa(Grob me, bool solid, double lineThickness)
    {
        double staffSpace = StaffSymbolReferencer.StaffSpace(me);
        Stencil stencil = Stencil.Empty;
        double rightHeight = 0.6 * staffSpace;

        double interval;
        object flexaHeightScm = me.GetProperty(FlexaHeightSymbol);
        if (!(flexaHeightScm is Nil))
        {
            interval = SchemeConvert.ToInt(flexaHeightScm, 0);
        }
        else
        {
            me.Warning("Vaticana_ligature: flexa-height undefined; assuming 0");
            interval = 0.0;
        }

        if (interval >= 0.0)
        {
            me.Warning("ascending vaticana style flexa");
        }

        double width = SchemeConvert.ToDouble(me.GetProperty(FlexaWidthSymbol), 2);

        /*
         * Compensate curve thickness that appears to be smaller in steep
         * section of bend.
         */
        double leftHeight
            = rightHeight + (Math.Min(0.12 * Math.Abs(interval), 0.3) * staffSpace);

        /*
         * Compensate optical illusion regarding vertical position of left
         * and right endings due to curved shape.
         */
        double yposCorrection = -0.1 * staffSpace * Sign(interval);
        double intervalCorrection = 0.2 * staffSpace * Sign(interval);
        double correctedInterval = (interval * staffSpace) + intervalCorrection;

        /*
         * middle curve of flexa shape
         */
        Bezier curve = new Bezier();
        curve[0] = new Offset(0.00 * width, 0.0);
        curve[1] = new Offset(0.33 * width, correctedInterval / 2.0);
        curve[2] = new Offset(0.66 * width, correctedInterval / 2.0);
        curve[3] = new Offset(1.00 * width, correctedInterval / 2.0);

        Bezier topCurve = CopyOf(curve);
        Bezier bottomCurve = CopyOf(curve);
        for (int i = 0; i < Bezier.ControlCount; i++)
        {
            double curveThickness = 0.33 * (((3 - i) * leftHeight) + (i * rightHeight));
            topCurve[i] += new Offset(0, 0.5 * curveThickness);
            bottomCurve[i] -= new Offset(0, 0.5 * curveThickness);
        }

        if (solid)
        {
            Stencil solidHead = Lookup.BezierSandwich(topCurve, bottomCurve, 0.0);
            stencil.AddStencil(solidHead);
        }
        else // outline
        {
            Bezier innerTopCurve = CopyOf(topCurve);
            innerTopCurve.Translate(new Offset(0.0, -lineThickness));
            Stencil topEdge = Lookup.BezierSandwich(topCurve, innerTopCurve, 0.0);
            stencil.AddStencil(topEdge);

            Bezier innerBottomCurve = CopyOf(bottomCurve);
            innerBottomCurve.Translate(new Offset(0.0, +lineThickness));
            Stencil bottomEdge = Lookup.BezierSandwich(bottomCurve, innerBottomCurve, 0.0);
            stencil.AddStencil(bottomEdge);

            /*
             * TODO: Use horizontal slope with proper slope value rather
             * than filled box for left edge, since the filled box stands
             * out from the flexa shape if the interval is big and the line
             * thickness small.  The difficulty here is to compute a proper
             * slope value, as it should roughly be equal with the slope of
             * the left end of the bezier curve.
             */
            Box leftEdgeBox = new Box(
                new Interval(0, lineThickness),
                new Interval(-0.5 * leftHeight, +0.5 * leftHeight));
            Stencil leftEdge = Lookup.FilledBox(leftEdgeBox);
            stencil.AddStencil(leftEdge);

            Box rightEdgeBox = new Box(
                new Interval(-lineThickness, 0),
                new Interval(-0.5 * rightHeight, +0.5 * rightHeight));
            Stencil rightEdge = Lookup.FilledBox(rightEdgeBox);
            rightEdge.TranslateAxis(width, Axis.X);
            rightEdge.TranslateAxis(correctedInterval / 2.0, Axis.Y);
            stencil.AddStencil(rightEdge);
        }

        stencil.TranslateAxis(yposCorrection, Axis.Y);
        return stencil;
    }

    private static Stencil BrewJoin(
        Grob me, int deltaPitch, double joinThickness, double blotDiameter)
    {
        double staffSpace = StaffSymbolReferencer.StaffSpace(me);
        if (deltaPitch == 0)
        {
            me.ProgrammingError("Vaticana_ligature: zero join (delta_pitch == 0)");
            return Lookup.Blank(new Box(new Interval(0, 0), new Interval(0, 0)));
        }

        Interval xExtent = new Interval(0, joinThickness);
        Interval yExtent = deltaPitch > 0
            ? new Interval(0, deltaPitch * 0.5 * staffSpace)   // ascending join
            : new Interval(deltaPitch * 0.5 * staffSpace, 0);  // descending join
        Box joinBox = new Box(xExtent, yExtent);
        return Lookup.RoundFilledBox(joinBox, blotDiameter);
    }

    private static Stencil BrewPrimitive(Grob me)
    {
        object glyphNameScm = me.GetProperty(GlyphNameSymbol);
        if (glyphNameScm is Nil)
        {
            me.ProgrammingError("Vaticana_ligature: undefined glyph-name -> ignoring grob");
            return Lookup.Blank(new Box(new Interval(0, 0), new Interval(0, 0)));
        }

        string glyphName = glyphNameScm is MutableString text ? text.ToString() : string.Empty;

        Stencil outStencil;
        double thickness = SchemeConvert.ToDouble(me.GetProperty(ThicknessSymbol), 1);

        double lineThickness = thickness * me.Layout.GetDimension(LineThicknessSymbol);

        double blotDiameter = me.Layout.GetDimension(BlotDiameterSymbol);

        int pos = StaffSymbolReferencer.GetRoundedPosition(me);

        object deltaPitchScm = me.GetProperty(DeltaPositionSymbol);
        int deltaPitch = deltaPitchScm is Nil ? 0 : SchemeConvert.ToInt(deltaPitchScm, 0);

        double headXOffset = SchemeConvert.ToDouble(me.GetProperty(HeadXOffsetSymbol), 0);

        bool addStem = SchemeUtilities.ToBool(me.GetProperty(AddStemSymbol));
        bool addCauda = SchemeUtilities.ToBool(me.GetProperty(AddCaudaSymbol));
        bool addJoin = SchemeUtilities.ToBool(me.GetProperty(AddJoinSymbol));

        if (glyphName.Length == 0)
        {
            /*
             * This is an empty head.  This typically applies for the right
             * side of a curved flexa shape, which is already typeset by the
             * associated left side head.  The only possible thing left to
             * do is to draw a vertical join to the next head.  (Urgh: need
             * flexa_width.)
             */
            double staffSpace = StaffSymbolReferencer.StaffSpace(me);
            double flexaWidth
                = SchemeConvert.ToDouble(me.GetProperty(FlexaWidthSymbol), 2) * staffSpace;
            outStencil = Lookup.Blank(
                new Box(new Interval(0, 0.5 * flexaWidth), new Interval(0, 0)));
        }
        else if (glyphName == "flexa")
        {
            outStencil = BrewFlexa(me, true, lineThickness);
        }
        else
        {
            outStencil = FontInterface.GetDefaultFont(me).FindByName("noteheads.s" + glyphName);
        }

        outStencil.TranslateAxis(headXOffset, Axis.X);
        double headWidth = outStencil.Extent(Axis.X).Length;

        if (addCauda)
        {
            Stencil cauda = BrewCauda(me, pos, deltaPitch, lineThickness, blotDiameter);
            outStencil.AddStencil(cauda);
        }

        if (addStem)
        {
            Stencil stem = BrewCauda(me, pos, -1, lineThickness, blotDiameter);
            stem.TranslateAxis(headWidth - lineThickness, Axis.X);
            outStencil.AddStencil(stem);
        }

        if (addJoin)
        {
            Stencil join = BrewJoin(me, deltaPitch, lineThickness, blotDiameter);
            join.TranslateAxis(headWidth - lineThickness, Axis.X);
            outStencil.AddStencil(join);
        }

        return outStencil;
    }

    // ⚠ NOT a convenience. Flower's Bezier is a class, so `Bezier b = a;' aliases where
    // upstream's `Bezier b = a;' copies -- see the note at the top of this file.
    private static Bezier CopyOf(Bezier source) => new Bezier(source.ControlPoints);

    private static double Sign(double value) => value > 0 ? 1.0 : (value < 0 ? -1.0 : 0.0);
}
