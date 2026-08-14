/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2002--2026 Juergen Reuter <reuter@ipd.uka.de>,
  Pal Benko <benkop@freestart.hu>

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

using System.Globalization;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/mensural-ligature.cc, lily/include/mensural-ligature.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - upstream's MLP_* primitive codes are preprocessor #defines in the header; they are
//     public consts here, renamed from MACRO_CASE to PascalCase. The VALUES may not
//     change: `primitive' is a grob property holding this bit set, written by the
//     engraver and read back here.
//   - upstream's free functions brew_flexa and internal_brew_primitive have no header
//     declarations and no callers outside this file, so they are private statics.

/// <summary>
/// A mensural ligature: the white-mensural shape that fuses a run of note heads into one
/// connected graphic.
/// </summary>
/// <remarks>
/// <para>
/// The spanner itself draws nothing. What is on the page is drawn head by head, because
/// <see cref="Translation.MensuralLigatureEngraver"/> replaces each head's
/// <c>stencil</c> with <see cref="BrewLigaturePrimitive"/> and writes the
/// <c>primitive</c> property that says WHICH piece of the ligature that head is: a
/// brevis, a maxima, one half of an obliqua, with or without stems on either side.
/// </para>
/// <para>
/// Splitting an obliqua into two halves — <see cref="FlexaBegin"/> and
/// <see cref="FlexaEnd"/> — is what lets the two notes of a flexa be coloured
/// independently.
/// </para>
/// </remarks>
public static class MensuralLigature
{
    private static readonly Symbol AddJoinSymbol = Symbol.Intern("add-join");
    private static readonly Symbol BlotDiameterSymbol = Symbol.Intern("blot-diameter");
    private static readonly Symbol DeltaPositionSymbol = Symbol.Intern("delta-position");
    private static readonly Symbol FlexaIntervalSymbol = Symbol.Intern("flexa-interval");
    private static readonly Symbol FlexaWidthSymbol = Symbol.Intern("flexa-width");
    private static readonly Symbol HeadWidthSymbol = Symbol.Intern("head-width");
    private static readonly Symbol PrimitiveSymbol = Symbol.Intern("primitive");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");

    private static readonly Symbol BlackPetrucciSymbol = Symbol.Intern("blackpetrucci");
    private static readonly Symbol SemiPetrucciSymbol = Symbol.Intern("semipetrucci");

    // ----- the mensural ligature primitives -----

    /// <summary>No output.</summary>
    public const int None = 0x00;

    /// <summary>Upward left stem.</summary>
    public const int Up = 0x01;

    /// <summary>Downward left stem.</summary>
    public const int Down = 0x02;

    /// <summary>Upward right stem (in the middle, a left stem of the next note).</summary>
    public const int JoinUp = 0x04;

    /// <summary>Downward right stem.</summary>
    public const int JoinDown = 0x08;

    /// <summary>Mensural brevis head.</summary>
    public const int Brevis = 0x10;

    /// <summary>Mensural maxima head without stem.</summary>
    public const int Maxima = 0x20;

    /// <summary>Start of obliqua.</summary>
    public const int FlexaBegin = 0x40;

    /// <summary>End of obliqua.</summary>
    public const int FlexaEnd = 0x80;

    /// <summary>Final ascending longa drawn like a pes.</summary>
    public const int Pes = 0x100;

    /// <summary>Marks an invalid duration (with well defined pitch).</summary>
    public const int Invalid = 0x8000;

    /// <summary>Either left stem.</summary>
    public const int Stem = Up | Down;

    /// <summary>Either right stem.</summary>
    public const int RightStem = JoinUp | JoinDown;

    /// <summary>Either single head shape.</summary>
    public const int SingleHead = Brevis | Maxima;

    /// <summary>Either half of an obliqua.</summary>
    public const int Flexa = FlexaBegin | FlexaEnd;

    /// <summary>Any note shape at all.</summary>
    public const int Any = Flexa | SingleHead | Invalid;

    /// <summary>
    /// The stencil of ONE head of the ligature — installed on each head by the engraver,
    /// which is why it is a callback over a note head rather than over the spanner.
    /// </summary>
    /// <param name="me">The ligature head.</param>
    /// <returns>The stencil.</returns>
    public static object BrewLigaturePrimitive(Grob me) => InternalBrewPrimitive(me);

    /// <summary>The <c>stencil</c> callback: the ligature spanner draws nothing itself.</summary>
    /// <param name="me">The ligature spanner.</param>
    /// <returns><c>'()</c>, always.</returns>
    public static object Print(Grob me) => Nil.Instance;

    /*
      draws one half a flexa, i.e. a portion corresponding to a single note.
      this way coloration of the two notes building up the flexa can be
      handled independently.

     * TODO: divide this function into mensural and neo-mensural style.
     *
     * TODO: move this function to class Lookup?
     */
    private static Stencil BrewFlexa(
        Grob me, bool solid, bool semi, double width, double thickness, bool begin)
    {
        double staffSpace = StaffSymbolReferencer.StaffSpace(me);
        Stencil stencil;
        double interval = SchemeConvert.ToDouble(me.GetProperty(FlexaIntervalSymbol), 0.0);
        double slope = (interval / 2.0 * staffSpace) / width;

        // Compensate optical illusion regarding vertical position of left
        // and right endings due to slope.
        double slopeCorrection = 0.2 * staffSpace * Sign(slope);
        double correctedSlope = slope + (slopeCorrection / width);
        double blotDiameter = me.Layout.GetDimension(BlotDiameterSymbol);
        width += 2 * blotDiameter;

        if (solid) // colorated flexae
        {
            stencil = Lookup.Beam(correctedSlope, width * 0.5, staffSpace, blotDiameter);
        }
        else // outline
        {
            /*
              The thickness of the horizontal lines of the flexa shape
              should be equal to that of the horizontal lines of the
              neomensural brevis note head (see mf/parmesan-heads.mf);
              thickness of the bottom line is half space for semi-colored notes.
            */
            double topLineThickness = staffSpace * (semi ? 0.5 : 0.35);
            double bottomLineThickness = staffSpace * 0.35;

            /*
              start with the small vertical element...
            */
            double verticalHeight = staffSpace * 0.65;
            stencil = Lookup.Beam(correctedSlope, thickness, verticalHeight, blotDiameter);
            if (!begin)
            {
                stencil.TranslateAxis((width * 0.5) - thickness, Axis.X);
                stencil.TranslateAxis(correctedSlope * ((width * 0.5) - thickness), Axis.Y);
            }

            /*
              ... and add the inclined ones
            */
            Stencil bottomEdge = Lookup.Beam(
                correctedSlope, width * 0.5, bottomLineThickness, blotDiameter);
            bottomEdge.TranslateAxis(-0.5 * (staffSpace - bottomLineThickness), Axis.Y);
            stencil.AddStencil(bottomEdge);

            Stencil topEdge = Lookup.Beam(
                correctedSlope, width * 0.5, topLineThickness, blotDiameter);
            topEdge.TranslateAxis(+0.5 * (staffSpace - topLineThickness), Axis.Y);
            stencil.AddStencil(topEdge);
        }

        if (begin)
        {
            double yposCorrection = -0.1 * staffSpace * Sign(slope);
            stencil.TranslateAxis(yposCorrection, Axis.Y);
        }
        else
        {
            stencil.TranslateAxis((0.5 * thickness) - blotDiameter, Axis.X);

            stencil.TranslateAxis(interval / -4.0 * staffSpace, Axis.Y);
        }

        stencil.TranslateAxis(-thickness, Axis.X);

        return stencil;
    }

    private static Stencil InternalBrewPrimitive(Grob me)
    {
        object primitiveScm = me.GetProperty(PrimitiveSymbol);
        if (primitiveScm is Nil)
        {
            Warn.ProgrammingError("Mensural_ligature: undefined primitive -> ignoring grob");
            return Lookup.Blank(new Box(new Interval(0, 0), new Interval(0, 0)));
        }

        int primitive = SchemeConvert.ToInt(primitiveScm, 0);

        double thickness = 0.0;
        double width = 0.0;
        double flexaWidth = 0.0;
        double staffSpace = StaffSymbolReferencer.StaffSpace(me);

        object style = me.GetProperty(StyleSymbol);
        bool black = ReferenceEquals(style, BlackPetrucciSymbol);
        bool semi = ReferenceEquals(style, SemiPetrucciSymbol);

        if ((primitive & Any) != 0)
        {
            thickness = SchemeConvert.ToDouble(me.GetProperty(ThicknessSymbol), .13);
            width = SchemeConvert.ToDouble(me.GetProperty(HeadWidthSymbol), staffSpace)
                - thickness;
        }

        if ((primitive & Flexa) != 0)
        {
            flexaWidth = SchemeConvert.ToDouble(me.GetProperty(FlexaWidthSymbol), 2.0)
                * staffSpace;
        }

        Stencil outStencil;
        int noteShape = primitive & Any;
        int durationLog = 0;
        FontMetric fm = FontInterface.GetDefaultFont(me);
        const string prefix = "noteheads.";
        string index;
        string suffix;
        string color = string.Empty;
        if (black)
        {
            color = "black";
        }

        if (semi)
        {
            color = "semi";
        }

        switch (noteShape)
        {
            case None:
                return Lookup.Blank(new Box(new Interval(0, 0), new Interval(0, 0)));
            case Maxima:
            case Brevis:
                if (noteShape == Maxima)
                {
                    durationLog -= 2;
                }

                durationLog--;
                suffix = durationLog.ToString(CultureInfo.InvariantCulture) + color
                    + (durationLog < -1 ? "lig" : string.Empty) + "mensural";
                index = prefix + "s";
                outStencil = fm.FindByName(index + "r" + suffix);
                if (!outStencil.IsEmpty
                    && !StaffSymbolReferencer.OnLine(
                        me, SchemeConvert.ToInt(me.GetProperty(StaffPositionSymbol), 0)))
                {
                    index += "r";
                }

                outStencil = fm.FindByName(index + suffix);
                break;
            case Invalid:
                outStencil = fm.FindByName("noteheads.s2cross");
                break;
            case FlexaBegin:
            case FlexaEnd:
                outStencil = BrewFlexa(
                    me, black, semi, flexaWidth, thickness, noteShape == FlexaBegin);
                break;
            default:
                Warn.ProgrammingError("Mensural_ligature: unexpected case fall-through");
                return Lookup.Blank(new Box(new Interval(0, 0), new Interval(0, 0)));
        }

        /*
          we use thickness because the stem end of the glyph
          "noteheads.sM2ligmensural" is round.
        */
        double blotDiameter = thickness;

        /*
          instead of 5.0 the length of a longa stem should be used, but

          Font_interface::get_default_font (me)->find_by_name
          ("noteheads.sM2ligmensural").extent (Y_AXIS).length ()

          doesn't work
        */
        const int longaStemLength = 5;
        double stemLength = longaStemLength * 0.5 * staffSpace;

        if ((primitive & Stem) != 0)
        {
            // assume MLP_UP
            double yBottom = 0.0;
            double yTop = stemLength;

            if ((primitive & Down) != 0)
            {
                yBottom = -yTop;
                yTop = 0.0;
            }

            Interval xExtent = new Interval(-thickness, 0);
            Interval yExtent = new Interval(yBottom, yTop);
            Box joinBox = new Box(xExtent, yExtent);

            Stencil join = Lookup.RoundFilledBox(joinBox, blotDiameter);
            outStencil.AddStencil(join);
        }

        bool hasJoin = SchemeUtilities.ToBool(me.GetProperty(AddJoinSymbol));
        bool hasRightStem = (primitive & RightStem) != 0;
        if (hasJoin || hasRightStem)
        {
            int joinRight = hasJoin
                ? SchemeConvert.ToInt(me.GetProperty(DeltaPositionSymbol), 0)
                : 0;
            double yTop = joinRight * 0.5 * staffSpace;
            double yBottom = 0.0;

            if (yTop < 0.0)
            {
                yBottom = yTop;
                yTop = 0.0;
            }

            if (hasRightStem)
            {
                /*
                  if the previous note has a right downward stem,
                  the joining line may hide that,
                  so make join longer to serve as stem as well
                */
                if ((primitive & JoinDown) != 0)
                {
                    yBottom -= stemLength + (0.25 * blotDiameter);
                }

                /*
                  if next note has a left upward stem,
                  the joining line may hide that,
                  so make join longer to serve as stem as well
                */
                if ((primitive & JoinUp) != 0)
                {
                    yTop += stemLength + (0.25 * blotDiameter);
                }
            }

            Interval xExtent = new Interval(width - thickness, width);
            Interval yExtent = new Interval(yBottom, yTop);
            Box joinBox = new Box(xExtent, yExtent);
            Stencil join = Lookup.RoundFilledBox(joinBox, blotDiameter);

            outStencil.AddStencil(join);
        }

        if ((primitive & Pes) != 0)
        {
            outStencil.TranslateAxis(-width, Axis.X);
        }

        // Upstream carries an `#if 0' block here asking what happened to the ledger
        // lines of a flexa. It is dead in upstream and dead here; it is not ported.
        return outStencil;
    }

    private static double Sign(double value) => value > 0 ? 1.0 : (value < 0 ? -1.0 : 0.0);
}
