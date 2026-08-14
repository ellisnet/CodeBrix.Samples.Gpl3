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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/balloon.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// A collection of routines to put text balloons around an object.
/// </summary>
public static class BalloonInterface
{
    private static readonly Symbol StickyHostSymbol = Symbol.Intern("sticky-host");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol AnnotationBalloonSymbol
        = Symbol.Intern("annotation-balloon");
    private static readonly Symbol AnnotationLineSymbol = Symbol.Intern("annotation-line");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol TextAlignmentXSymbol = Symbol.Intern("text-alignment-X");
    private static readonly Symbol TextAlignmentYSymbol = Symbol.Intern("text-alignment-Y");
    private static readonly Symbol XAttachmentSymbol = Symbol.Intern("X-attachment");
    private static readonly Symbol YAttachmentSymbol = Symbol.Intern("Y-attachment");

    /// <summary>Draws the balloon and its pointer line.</summary>
    /// <param name="me">The balloon grob.</param>
    /// <returns>The stencil.</returns>
    public static object Print(Grob me)
    {
        Grob annotated = me.GetObject(StickyHostSymbol) as Grob;
        if (annotated == null)
        {
            me.ProgrammingError("sticky grob without host");
            return Stencil.Empty;
        }

        Offset off = new Offset(
            me.RelativeCoordinate(annotated, Axis.X),
            me.RelativeCoordinate(annotated, Axis.Y));
        Box b = new Box(
            LooseColumns.RobustRelativeExtent(annotated, annotated, Axis.X),
            LooseColumns.RobustRelativeExtent(annotated, annotated, Axis.Y));

        return InternalBalloonPrint(me, b, off);
    }

    /// <summary>The <c>width</c> callback.</summary>
    /// <param name="me">The balloon grob.</param>
    /// <returns>The horizontal extent as a number pair.</returns>
    public static object Width(Grob me)
    {
        Grob annotated = me.GetObject(StickyHostSymbol) as Grob;
        if (annotated == null)
        {
            me.ProgrammingError("sticky grob without host");
            return ToPair(Interval.Empty);
        }

        Box b = new Box(
            LooseColumns.RobustRelativeExtent(annotated, annotated, Axis.X),
            new Interval(0, 0));
        double off = me.RelativeCoordinate(annotated, Axis.X);
        return ToPair(
            InternalBalloonPrint(me, b, new Offset(off, 0)).Extent(Axis.X));
    }

    /// <summary>The <c>pure-height</c> callback.</summary>
    /// <param name="me">The balloon grob.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The vertical extent as a number pair.</returns>
    public static object PureHeight(Grob me, int start, int end)
    {
        Grob annotated = me.GetObject(StickyHostSymbol) as Grob;
        if (annotated == null)
        {
            me.ProgrammingError("sticky grob without host");
            return ToPair(Interval.Empty);
        }

        Interval y = Grob.RobustRelativePureYExtent(annotated, annotated, start, end);
        double off = me.RelativeCoordinate(annotated, Axis.Y);
        return ToPair(
            InternalBalloonPrint(me, new Box(new Interval(0, 0), y), new Offset(0, off))
                .Extent(Axis.Y));
    }

    /// <summary>Builds the balloon frame, text and pointer line around a box.</summary>
    /// <param name="me">The balloon grob.</param>
    /// <param name="b">The box to annotate.</param>
    /// <param name="off">Where the balloon sits relative to it.</param>
    /// <returns>The stencil.</returns>
    public static Stencil InternalBalloonPrint(Grob me, Box b, Offset off)
    {
        double padding = ToDouble(me.GetProperty(PaddingSymbol), .1);
        b.Widen(padding, padding);

        Stencil result = Stencil.Empty;
        if (SchemeUtilities.ToBool(me.GetProperty(AnnotationBalloonSymbol)))
        {
            double thickness = ToDouble(me.GetProperty(ThicknessSymbol), 1.0);
            thickness *= StaffSymbolReferencer.LineThickness(me);

            const double BlotDiameter = 0.05; // FIXME: hardcoded

            result = Lookup.Frame(b, thickness, BlotDiameter);
        }

        object bt = me.GetProperty(TextSymbol);

        // TODO: cache somehow?
        Stencil textStil = TextInterface.GrobInterpretMarkup(me, bt);

        Offset z1 = Offset.Zero;
        foreach (Axis a in new[] { Axis.X, Axis.Y })
        {
            /* By default, we use these alignments:

                Balloon text
                            \
                             \
                              grob

                           Balloon text
                                |
                                |
                              grob

                                    Balloon text
                                   /
                                  /
                              grob
            */
            double offSign = Math.Sign(off[a]);
            Symbol textAlignProp = a == Axis.X ? TextAlignmentXSymbol : TextAlignmentYSymbol;
            double textAlign = ToDouble(me.GetProperty(textAlignProp), -offSign);
            Symbol attachAlignProp = a == Axis.X ? XAttachmentSymbol : YAttachmentSymbol;
            double attachAlign = ToDouble(me.GetProperty(attachAlignProp), offSign);

            z1 = z1.With(a, b[a].LinearCombination(attachAlign));
            textStil.AlignTo(a, textAlign);
        }

        Offset z2 = z1 + off;
        if (SchemeUtilities.ToBool(me.GetProperty(AnnotationLineSymbol)))
        {
            result.AddStencil(LineInterface.Line(me, z1, z2));
        }

        textStil.Translate(z2);
        result.AddStencil(textStil);
        result.Translate(-off);

        return result;
    }

    private static object ToPair(Interval interval) => new Pair(interval.Left, interval.Right);

    private static double ToDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "balloon-interface")
            : fallback;
}
