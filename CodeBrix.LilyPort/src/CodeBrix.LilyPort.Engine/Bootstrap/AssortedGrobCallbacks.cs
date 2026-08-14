// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// Callback leaves — <c>MAKE_SCHEME_CALLBACK</c> names declared in files whose
/// ledger rows already read <c>ported</c>.
/// <para>
/// These are the "hollow leaf" shape standing trap 7 records: the C++ TYPE is carried,
/// every C# caller works, and the ledger row is honest — but the Scheme-visible name was
/// never registered, so the Scheme side quietly received the inert
/// <see cref="UnportedValue"/> placeholder instead.
/// </para>
/// </summary>
public static class AssortedGrobCallbacks
{
    private static readonly Symbol StencilSymbol = Symbol.Intern("stencil");
    private static readonly Symbol StemSymbol = Symbol.Intern("stem");
    private static readonly Symbol WhenSymbol = Symbol.Intern("when");
    private static readonly Symbol IdealDistancesSymbol = Symbol.Intern("ideal-distances");
    private static readonly Symbol MinimumDistancesSymbol = Symbol.Intern("minimum-distances");

    /// <summary>Installs the callbacks, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        // ----- grob.cc -----

        // The PURE counterpart of ly:grob::stencil-height. The two differ in exactly one
        // way and it is the point of the entry point: this one reads the stencil's
        // PROPERTY DATA and answers the empty interval unless a real stencil is already
        // sitting there, where the ordinary version would CALL the stencil callback and
        // drag the whole stencil machinery into a spacing pass (standing trap 14).
        interpreter.DefinePrimitive("ly:grob::pure-stencil-height", 3, 3, a =>
        {
            Grob grob = AsGrob(a[0], "ly:grob::pure-stencil-height");
            if (!(grob.GetPropertyData(StencilSymbol) is Stencil))
            {
                return ToPair(Interval.Empty);
            }

            Stencil? stencil = grob.GetStencil();
            return ToPair(stencil.HasValue ? stencil.Value.Extent(Axis.Y) : Interval.Empty);
        });

        // ----- note-head.cc -----

        // Upstream's own comment: hard-coded to (0.0, 1.35) for upward stems and
        // (0.0, -1.35) for downward ones. A head with no stem, or a stem with no
        // direction, is treated as UP.
        interpreter.DefinePrimitive("ly:note-head::calc-tab-stem-attachment", 1, 1, a =>
        {
            Grob head = AsGrob(a[0], "ly:note-head::calc-tab-stem-attachment");
            Direction direction = head.GetObject(StemSymbol) is Grob stem
                ? DirectionalElementInterface.GetGrobDirection(stem)
                : Direction.Center;

            if (direction == Direction.Center)
            {
                direction = Direction.Positive;
            }

            return new Pair(0.0, (int)direction * 1.35);
        });

        // ----- axis-group-interface.cc -----

        interpreter.DefinePrimitive(
            "ly:axis-group-interface::calc-pure-staff-staff-spacing", 3, 3, a =>
                AxisGroupInterfaceVertical.CalcMaybePureStaffStaffSpacing(
                    AsGrob(a[0], "ly:axis-group-interface::calc-pure-staff-staff-spacing"),
                    true,
                    SchemeConvert.ToInt(
                        a[1], "ly:axis-group-interface::calc-pure-staff-staff-spacing"),
                    SchemeConvert.ToInt(
                        a[2], "ly:axis-group-interface::calc-pure-staff-staff-spacing")));

        // ----- system.cc -----

        interpreter.DefinePrimitive("ly:system::footnotes-before-line-breaking", 1, 1, a =>
            SystemGrob.FootnotesBeforeLineBreaking(
                AsGrob(a[0], "ly:system::footnotes-before-line-breaking")));

        interpreter.DefinePrimitive("ly:system::footnotes-after-line-breaking", 1, 1, a =>
        {
            Grob grob = AsGrob(a[0], "ly:system::footnotes-after-line-breaking");
            if (!(grob is SystemGrob system))
            {
                throw SchemeErrors.WrongType(
                    "ly:system::footnotes-after-line-breaking", "system", a[0]);
            }

            return SystemGrob.FootnotesAfterLineBreaking(system);
        });

        // ----- paper-column.cc -----

        interpreter.DefinePrimitive("ly:paper-column::print", 1, 1, a =>
            PaperColumnPrint(AsGrob(a[0], "ly:paper-column::print")));
    }

    /// <summary>
    /// <c>Paper_column::print</c>: the DEBUG stencil for a paper column — its rank number
    /// over its moment, a vertical rule, and one arrow per spacing relationship (blue for
    /// the ideal distances, red for the minimum ones).
    /// </summary>
    /// <param name="grob">The paper column.</param>
    /// <returns>The debug stencil.</returns>
    /// <remarks>
    /// ⚠ Nothing reaches this in a normal run, and that is upstream's arrangement rather
    /// than a gap in the port: <c>scm/define-grobs.scm</c> carries
    /// <c>(stencil . ,ly:paper-column::print)</c> COMMENTED OUT on both
    /// <c>PaperColumn</c> and <c>NonMusicalPaperColumn</c>, so it is drawn only when a
    /// user overrides the stencil back on. It is registered because a name the Scheme can
    /// name must not answer with the inert placeholder (trap 7), and because
    /// <c>ly:paper-column::print</c> is exactly the override a user reaches for when
    /// debugging spacing.
    /// <para>
    /// The two arrow loops share one running counter <c>j</c> across BOTH of them —
    /// upstream's comment says so explicitly ("number of printed arrows from *both*
    /// loops") — so the minimum-distance arrows stack below the ideal-distance ones
    /// rather than overprinting them.
    /// </para>
    /// </remarks>
    private static object PaperColumnPrint(Grob grob)
    {
        FontMetric musicFont = FontInterface.GetDefaultFont(grob);

        string rank = grob is PaperColumn column
            ? column.Rank.ToString(CultureInfo.InvariantCulture)
            : "?";
        string when = grob.GetProperty(WhenSymbol) is Moment moment ? moment.ToString() : "?/?";

        Stencil text = TextInterface.GrobInterpretMarkup(grob, new MutableString(rank));
        Stencil whenStencil = TextInterface.GrobInterpretMarkup(grob, new MutableString(when));
        text.Scale(1.2, 1.4);
        text.AddAtEdge(Axis.Y, Direction.Negative, whenStencil, 0.1);
        text.AlignTo(Axis.X, (double)Direction.Negative);

        // Compensate for font serifs and half the letter distance.
        text.Translate(new Offset(-0.1, 0));
        text.AlignTo(Axis.Y, (double)Direction.Negative);

        Stencil rule = Lookup.FilledBox(
            new Box(new Interval(0, 0.02), new Interval(-8, -1)));

        const double SmallPad = 0.15;
        const double BigPad = 0.35;
        int arrows = 0;

        for (object cursor = grob.GetObject(IdealDistancesSymbol);
             cursor is Pair pair;
             cursor = pair.Cdr)
        {
            if (!(pair.Car is Pair entry) || !(entry.Car is Spring spring))
            {
                continue;
            }

            // Skip a relationship whose other end is not on this system — its distance
            // would be measured across a line break.
            if (!(entry.Cdr is Grob other) || other.GetSystem() == null)
            {
                continue;
            }

            arrows++;
            rule.AddStencil(DistanceArrow(
                grob, musicFont, spring.IdealDistance, arrows, -2.5, SmallPad, BigPad,
                Direction.Negative, 0.2, 0.4, 1.0));
        }

        for (object cursor = grob.GetObject(MinimumDistancesSymbol);
             cursor is Pair pair;
             cursor = pair.Cdr)
        {
            if (!(pair.Car is Pair entry) || !(entry.Car is Grob other))
            {
                continue;
            }

            if (other.GetSystem() != grob.GetSystem())
            {
                continue;
            }

            arrows++;
            rule.AddStencil(DistanceArrow(
                grob, musicFont, SchemeConvert.ToDouble(entry.Cdr, "ly:paper-column::print"),
                arrows, -3.0, SmallPad, BigPad, Direction.Positive, 1.0, 0.25, 0.25));
        }

        text.AddStencil(rule);
        return text;
    }

    /// <summary>
    /// One labelled distance arrow of <see cref="PaperColumnPrint"/>'s two families.
    /// </summary>
    /// <param name="grob">The column the arrow belongs to.</param>
    /// <param name="musicFont">The font the arrowhead glyph comes from.</param>
    /// <param name="distance">The distance the arrow spans.</param>
    /// <param name="index">How many arrows have been drawn, including this one.</param>
    /// <param name="baseY">The first arrow's y-coordinate.</param>
    /// <param name="smallPad">The label's padding.</param>
    /// <param name="bigPad">The padding between stacked arrows.</param>
    /// <param name="labelAlignment">Which way the label aligns off its baseline.</param>
    /// <param name="red">The red component of the arrow's colour.</param>
    /// <param name="green">The green component.</param>
    /// <param name="blue">The blue component.</param>
    /// <returns>The arrow, coloured.</returns>
    /// <remarks>
    /// The colours are deliberately LIGHT shades — upstream's comment says they stay
    /// legible on a black background.
    /// </remarks>
    private static Stencil DistanceArrow(
        Grob grob,
        FontMetric musicFont,
        double distance,
        int index,
        double baseY,
        double smallPad,
        double bigPad,
        Direction labelAlignment,
        double red,
        double green,
        double blue)
    {
        Stencil arrowhead = musicFont.FindByName("arrowheads.open.01");

        // Initial scaling; it also scales with font-size.
        arrowhead.Scale(1, 1.66);
        double headLength = arrowhead.Extent(Axis.X).Length;

        Stencil number = TextInterface.GrobInterpretMarkup(
            grob,
            new MutableString(distance.ToString("F2", CultureInfo.InvariantCulture)
                .PadLeft(5)));
        number.Scale(1, 1.1);
        double numberHeight = number.Extent(Axis.Y).Length;
        double numberLength = number.Extent(Axis.X).Length;
        number.AlignTo(Axis.Y, (double)labelAlignment);

        double y = baseY - (index * (numberHeight + smallPad + bigPad));

        // Horizontally centre the number on the arrow, EXCLUDING the arrowhead.
        Offset numberOffset = new Offset(
            (distance - numberLength - headLength) / 2,
            y + ((int)labelAlignment * smallPad));

        List<Offset> points = new List<Offset> { new Offset(0, y), new Offset(distance, y) };
        Stencil arrow = Lookup.PointsToLineStencil(0.1, points);
        arrow.AddStencil(arrowhead.Translated(new Offset(distance, y)));
        arrow.AddStencil(number.Translated(numberOffset));
        return arrow.InColor(red, green, blue);
    }

    private static object ToPair(Interval interval) => new Pair(interval.Left, interval.Right);

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);
}
