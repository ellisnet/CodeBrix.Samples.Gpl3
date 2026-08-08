// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The Scheme entry points EPG8's upstream files declare: the breathing-sign
/// callbacks (breathing-sign.cc), the grid-line callbacks (grid-line-interface.cc),
/// and the print callbacks of key-signature-interface.cc, measure-grouping-spanner.cc
/// and measure-spanner.cc.
/// <para>
/// The logic lives in the static classes under <c>Objects/</c>; this installer only
/// adapts. Implementing a name here overwrites the pre-registered stub, and the
/// entry-point closure measurement picks it up automatically.
/// </para>
/// </summary>
public static class Epg8Callbacks
{
    /// <summary>Installs the callbacks, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallBreathingSign(interpreter);
        InstallGridLine(interpreter);
        InstallPrints(interpreter);
        InstallSchemeGaps(interpreter);
    }

    /// <summary>
    /// Everything EPG8's landing DEMANDED from other owners, each block naming its
    /// owner: the pure-height and stencil-constructor leaf bindings the BarLine
    /// print/spacing path calls (EPG23's sweep pool over ported types),
    /// <c>ly:broadcast</c> (same pool), and zero-valued stand-ins for the EPG7/EPG15
    /// alignment callbacks EPG8's grobs carry — without which the first multi-measure
    /// score died in <c>Paper_score::process</c>. All are recorded under FINDINGS, and
    /// each is overwritten when its real owner lands.
    /// <para>
    /// The two Guile-core numeric procedures that used to sit here, <c>finite?</c> and
    /// <c>euclidean-remainder</c>, MOVED to CodeBrix.LilyScheme's NumericPrimitives in
    /// LS-FIX3 (2026-08-07), which is where a Guile-core name belongs. They are gone from
    /// the port rather than duplicated: a copy here would shadow the interpreter's own and
    /// re-introduce the divergence the move exists to remove. The LilyScheme versions
    /// compute over that library's numeric tower rather than through the port's
    /// <c>Rational</c>, whose biased-by-one denominator is a documented trap.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallSchemeGaps(Interpreter interpreter)
    {
        // The two PURE height entry points the BarLine spacing path demands the
        // moment a Bar_engraver exists: BarLine.extra-spacing-height is
        // pure-from-neighbor-interface::account-for-span-bar, which calls both on
        // every breakable column. The port has no pure-property machinery yet
        // (unpure-pure-container.cc, EPG15), so these answer the ORDINARY extents —
        // the same divergence GrobCallbacks records for the ten pure skyline
        // callbacks. ly:grob-pure-height belongs to grob-scheme.cc (EPG23's leaf
        // sweep) and ly:axis-group-interface::pure-height to the already-ported
        // axis-group-interface.cc; both are recorded under FINDINGS.
        interpreter.DefinePrimitive("ly:grob-pure-height", 4, 5, a =>
        {
            Grob grob = AsGrob(a[0], "ly:grob-pure-height");
            Grob refp = AsGrob(a[1], "ly:grob-pure-height");
            return ToPair(grob.Extent(refp, Axis.Y));
        });

        interpreter.DefinePrimitive("ly:axis-group-interface::pure-height", 3, 3, a =>
            ToPair(AxisGroupInterface.GenericGroupExtent(
                AsGrob(a[0], "ly:axis-group-interface::pure-height"), Axis.Y)));

        // The four stencil constructors scm/bar-line.scm builds every bar line out
        // of. They belong to stencil-scheme.cc (EPG23's leaf sweep; the Stencil type
        // and Lookup are ported), and EPG8's landing is what first demands them: a
        // BarLine grob's stencil is ly:bar-line::print, pure Scheme, and with these
        // stubbed every bar line came out EMPTY with nothing to say why. Recorded
        // under FINDINGS.
        interpreter.DefinePrimitive("ly:round-filled-box", 3, 3, a =>
            Layout.Lookup.RoundFilledBox(
                new Layout.Box(
                    AsInterval(a[0], "ly:round-filled-box"),
                    AsInterval(a[1], "ly:round-filled-box")),
                SchemeConvert.ToDouble(a[2], "ly:round-filled-box")));

        interpreter.DefinePrimitive("ly:stencil-add", 0, -1, a =>
        {
            Layout.Stencil sum = default;
            foreach (object value in a)
            {
                sum.AddStencil(AsStencil(value, "ly:stencil-add"));
            }

            return sum;
        });

        interpreter.DefinePrimitive("ly:stencil-combine-at-edge", 4, 5, a =>
        {
            Layout.Stencil first = AsStencil(a[0], "ly:stencil-combine-at-edge");
            Axis axis = SchemeConvert.ToInt(a[1], "ly:stencil-combine-at-edge") == 0
                ? Axis.X
                : Axis.Y;
            long direction = SchemeConvert.ToLong(a[2], "ly:stencil-combine-at-edge");
            Layout.Stencil second = AsStencil(a[3], "ly:stencil-combine-at-edge");
            double padding = a.Length > 4
                ? SchemeConvert.ToDouble(a[4], "ly:stencil-combine-at-edge")
                : 0.0;

            first.AddAtEdge(
                axis,
                direction < 0 ? Direction.Negative : Direction.Positive,
                second,
                padding);
            return first;
        });

        interpreter.DefinePrimitive("ly:stencil-translate-axis", 3, 3, a =>
        {
            Layout.Stencil stencil = AsStencil(a[0], "ly:stencil-translate-axis");
            double amount = SchemeConvert.ToDouble(a[1], "ly:stencil-translate-axis");
            Axis axis = SchemeConvert.ToInt(a[2], "ly:stencil-translate-axis") == 0
                ? Axis.X
                : Axis.Y;
            stencil.TranslateAxis(amount, axis);
            return stencil;
        });

        // Zero stand-ins for break-alignment-interface.cc anchors (EPG15). A
        // BarNumber, RehearsalMark or MetronomeMark carries break-align-anchor
        // callbacks; with those stubbed, the first multi-measure score DIED in
        // Paper_score::process: the stub's inert value reached Scheme arithmetic.
        // Zero is what a grob with no anchor gets, the same stand-in device
        // GrobCallbacks already uses for force-hara-kiri-callback; the real
        // implementations overwrite these when EPG15 lands.
        //
        // EPG8 originally shipped the same zero stand-ins for nine EPG7 names
        // (self-alignment x6, side-position x3); EPG7's REAL implementations landed
        // in the same wave, so those were removed at integration and only the
        // EPG15-owed trio remains.
        interpreter.DefinePrimitive(
            "ly:break-alignable-interface::self-align-callback", 1, 1, a => 0.0);
        interpreter.DefinePrimitive(
            "ly:break-aligned-interface::calc-extent-aligned-anchor", 1, 1, a => 0.0);
        interpreter.DefinePrimitive(
            "ly:break-aligned-interface::calc-average-anchor", 1, 1, a => 0.0);

        // ly:broadcast used to live here as an EPG8 demand pull-forward. EPG22 ported
        // the whole of dispatcher-scheme.cc (2026-08-07), so it now lives with its
        // siblings in DispatcherPrimitives and this block is gone.
    }

    private static void InstallBreathingSign(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:breathing-sign::divisio-minima", 1, 1, a =>
            BreathingSign.DivisioMinima(AsGrob(a[0], "ly:breathing-sign::divisio-minima")));

        interpreter.DefinePrimitive("ly:breathing-sign::divisio-maior", 1, 1, a =>
            BreathingSign.DivisioMaior(AsGrob(a[0], "ly:breathing-sign::divisio-maior")));

        interpreter.DefinePrimitive("ly:breathing-sign::divisio-maxima", 1, 1, a =>
            BreathingSign.DivisioMaxima(AsGrob(a[0], "ly:breathing-sign::divisio-maxima")));

        interpreter.DefinePrimitive("ly:breathing-sign::finalis", 1, 1, a =>
            BreathingSign.Finalis(AsGrob(a[0], "ly:breathing-sign::finalis")));

        interpreter.DefinePrimitive("ly:breathing-sign::offset-callback", 1, 1, a =>
            BreathingSign.OffsetCallback(
                AsGrob(a[0], "ly:breathing-sign::offset-callback")));

        interpreter.DefinePrimitive("ly:breathing-sign::set-breath-properties", 3, 3, a =>
        {
            Grob grob = AsGrob(a[0], "ly:breathing-sign::set-breath-properties");
            if (!(a[1] is Context context))
            {
                throw SchemeErrors.WrongType(
                    "ly:breathing-sign::set-breath-properties", "context", a[1]);
            }

            BreathingSign.SetBreathProperties(grob, context, a[2]);
            return Unspecified.Instance;
        });
    }

    private static void InstallGridLine(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:grid-line-interface::print", 1, 1, a =>
            GridLineInterface.Print(AsGrob(a[0], "ly:grid-line-interface::print")));

        interpreter.DefinePrimitive("ly:grid-line-interface::width", 1, 1, a =>
            ToPair(GridLineInterface.Width(AsGrob(a[0], "ly:grid-line-interface::width"))));
    }

    private static void InstallPrints(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:key-signature-interface::print", 1, 1, a =>
            KeySignatureInterface.Print(AsGrob(a[0], "ly:key-signature-interface::print")));

        interpreter.DefinePrimitive("ly:measure-grouping::print", 1, 1, a =>
            MeasureGrouping.Print(AsSpanner(a[0], "ly:measure-grouping::print")));

        interpreter.DefinePrimitive("ly:measure-spanner::print", 1, 1, a =>
            MeasureSpanner.Print(AsSpanner(a[0], "ly:measure-spanner::print")));
    }

    private static object ToPair(Interval interval) => new Pair(interval.Left, interval.Right);

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);

    private static Spanner AsSpanner(object value, string procedureName)
        => value as Spanner ?? throw SchemeErrors.WrongType(procedureName, "spanner", value);

    private static Layout.Stencil AsStencil(object value, string procedureName)
        => value is Layout.Stencil stencil
            ? stencil
            : throw SchemeErrors.WrongType(procedureName, "stencil", value);

    private static Interval AsInterval(object value, string procedureName)
    {
        if (value is Pair pair
            && SchemeConvert.IsNumber(pair.Car)
            && SchemeConvert.IsNumber(pair.Cdr))
        {
            return new Interval(
                SchemeConvert.ToDouble(pair.Car, procedureName),
                SchemeConvert.ToDouble(pair.Cdr, procedureName));
        }

        throw SchemeErrors.WrongType(procedureName, "number pair", value);
    }
}
