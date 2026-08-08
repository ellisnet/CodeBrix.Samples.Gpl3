// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The EPG17 entry points: the grob callbacks for volta brackets, tuplet brackets and
/// tuplet numbers, and the three percent-repeat stencils.
/// <para>
/// Every name here is one <c>scm/define-grobs.scm</c> already refers to — a
/// VoltaBracket's <c>stencil</c> IS <c>ly:volta-bracket-interface::print</c> — so
/// registering them replaces the pre-registered stubs and moves them from the entry-point
/// closure's Stubbed bucket to its Implemented one.
/// </para>
/// <para>
/// New-in-family binding code; the derivation is recorded in
/// <c>THIRD-PARTY-NOTICES.txt</c>.
/// </para>
/// </summary>
public static class Epg17Callbacks
{
    /// <summary>Installs the callbacks, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallVoltaBracket(interpreter);
        InstallTupletBracket(interpreter);
        InstallTupletNumber(interpreter);
        InstallPercentRepeat(interpreter);
        InstallSpannerBindings(interpreter);
        InstallStencilBindings(interpreter);
    }

    // lily/stencil-scheme.cc, the four bindings EPG17's closing sweep DEMANDED, pulled
    // forward from EPG23 under the same rule as the spanner ones.
    //
    // Announcing DalSegnoEvent for real is what reaches them: Jump_engraver turns it
    // into markup, and scm/'s formatters scale and stack the pieces. Before this,
    // repeat-dc-unaligned, repeat-ds-formatter and repeat-ds-torture all died on
    // `#<unported ly:stencil-scale>' the moment the segno styler started announcing.
    // ly:stencil-stack was the most-demanded unported stencil binding in the sweep (40
    // calls) and its three neighbours came with it; every one is a thin wrapper over a
    // Stencil method that was already ported.
    private static void InstallStencilBindings(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:stencil-scale", 2, 3, a =>
        {
            Stencil stencil = AsStencil(a[0], "ly:stencil-scale");
            double x = AsDouble(a[1], "ly:stencil-scale");
            double y = a.Length > 2 ? AsDouble(a[2], "ly:stencil-scale") : x;

            stencil.Scale(x, y);
            return stencil;
        });

        interpreter.DefinePrimitive("ly:stencil-translate", 2, 2, a =>
        {
            Stencil stencil = AsStencil(a[0], "ly:stencil-translate");
            if (!(a[1] is Pair pair))
            {
                throw SchemeErrors.WrongType("ly:stencil-translate", "offset", a[1]);
            }

            stencil.Translate(new Offset(
                AsDouble(pair.Car, "ly:stencil-translate"),
                AsDouble(pair.Cdr, "ly:stencil-translate")));

            return stencil;
        });

        interpreter.DefinePrimitive("ly:stencil-aligned-to", 3, 3, a =>
        {
            Stencil stencil = AsStencil(a[0], "ly:stencil-aligned-to");
            stencil.AlignTo(
                AsAxis(a[1], "ly:stencil-aligned-to"),
                AsDouble(a[2], "ly:stencil-aligned-to"));

            return stencil;
        });

        interpreter.DefinePrimitive("ly:stencil-stack", 4, 6, a =>
        {
            // Both stencils are optional in the Scheme sense: #f and '() stand for
            // "nothing here", which is how the markup layer accumulates lines.
            Stencil result = a[0] is Stencil first ? first : new Stencil();

            if (a[3] is Stencil second)
            {
                double padding = a.Length > 4 ? AsDouble(a[4], "ly:stencil-stack") : 0.0;
                double minimumDistance = a.Length > 5
                    ? AsDouble(a[5], "ly:stencil-stack")
                    : double.NegativeInfinity;

                result.Stack(
                    AsAxis(a[1], "ly:stencil-stack"),
                    new Direction(AsDouble(a[2], "ly:stencil-stack")),
                    second,
                    padding,
                    minimumDistance);
            }

            return result;
        });
    }

    private static Stencil AsStencil(object value, string procedureName)
        => value is Stencil stencil
            ? stencil
            : throw SchemeErrors.WrongType(procedureName, "stencil", value);

    private static double AsDouble(object value, string procedureName)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, procedureName)
            : throw SchemeErrors.WrongType(procedureName, "number", value);

    private static Axis AsAxis(object value, string procedureName)
        => SchemeConvert.IsNumber(value)
            ? (SchemeConvert.ToLong(value, procedureName) == 0 ? Axis.X : Axis.Y)
            : throw SchemeErrors.WrongType(procedureName, "axis", value);

    // lily/spanner-scheme.cc, PULLED FORWARD FROM EPG23 by EPG17's demand loop.
    //
    // The ledger files spanner-scheme.cc under EPG23 ("leaf binding file — EPG23 sweeps
    // what its type's group did not land"), but standing rule 3 says a *-scheme.cc file
    // is never a work item of its own: whoever ports a type owes its LY_DEFINE surface.
    // Spanner.cs landed long ago without this surface, and EPG17 is the group that first
    // NEEDS it — the moment VoltaBracket, TupletBracket and PercentRepeat exist,
    // scm/define-grobs.scm's own callbacks reach for ly:spanner-bound and the whole file
    // fails to engrave. Measured, not assumed: before this, `\times 2/3 { c8 d e }` died
    // on `#<unported ly:spanner-bound>`.
    //
    // ly:spanner-broken-into is in the same file and is the binding HARNESS-FIX recorded
    // measure-counter-grace coming down for, so that row may now be earnable again.
    private static void InstallSpannerBindings(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:spanner-bound", 2, 3, a =>
        {
            Spanner me = AsSpanner(a[0], "ly:spanner-bound");
            Item bound = me.GetBound(AsDirection(a[1], "ly:spanner-bound"));
            if (bound != null)
            {
                return bound;
            }

            return a.Length > 2 ? a[2] : Nil.Instance;
        });

        interpreter.DefinePrimitive("ly:spanner-set-bound!", 3, 3, a =>
        {
            Spanner me = AsSpanner(a[0], "ly:spanner-set-bound!");
            Item item = a[2] as Item
                ?? throw SchemeErrors.WrongType("ly:spanner-set-bound!", "item", a[2]);

            me.SetBound(AsDirection(a[1], "ly:spanner-set-bound!"), item);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:spanner-broken-into", 1, 1, a =>
        {
            Spanner me = AsSpanner(a[0], "ly:spanner-broken-into");
            object result = Nil.Instance;
            for (int i = me.BrokenIntos.Count; i-- > 0;)
            {
                result = new Pair(me.BrokenIntos[i], result);
            }

            return result;
        });

        interpreter.DefinePrimitive("ly:spanner-broken-neighbor", 2, 2, a =>
        {
            Spanner me = AsSpanner(a[0], "ly:spanner-broken-neighbor");
            Spanner neighbor = me.BrokenNeighbor(
                AsDirection(a[1], "ly:spanner-broken-neighbor"));

            return neighbor ?? (object)false;
        });

        interpreter.DefinePrimitive("ly:spanner?", 1, 1, a => a[0] is Spanner);
    }

    private static Direction AsDirection(object value, string procedureName)
        => DirectionalElementInterface.IsDirection(value)
            ? DirectionalElementInterface.FromScheme(value, Direction.Center)
            : throw SchemeErrors.WrongType(procedureName, "direction", value);

    private static void InstallVoltaBracket(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:volta-bracket-interface::print", 1, 1, a =>
            VoltaBracketInterface.Print(
                AsSpanner(a[0], "ly:volta-bracket-interface::print")));
    }

    private static void InstallTupletBracket(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:tuplet-bracket::calc-x-positions", 1, 1, a =>
            TupletBracket.CalcXPositions(
                AsSpanner(a[0], "ly:tuplet-bracket::calc-x-positions")));

        interpreter.DefinePrimitive("ly:tuplet-bracket::print", 1, 1, a =>
            TupletBracket.Print(AsSpanner(a[0], "ly:tuplet-bracket::print")));

        interpreter.DefinePrimitive("ly:tuplet-bracket::calc-direction", 1, 1, a =>
            TupletBracket.CalcDirection(
                AsGrob(a[0], "ly:tuplet-bracket::calc-direction")));

        interpreter.DefinePrimitive("ly:tuplet-bracket::calc-positions", 1, 1, a =>
            TupletBracket.CalcPositions(
                AsSpanner(a[0], "ly:tuplet-bracket::calc-positions")));

        interpreter.DefinePrimitive("ly:tuplet-bracket::calc-cross-staff", 1, 1, a =>
            TupletBracket.CalcCrossStaff(
                AsSpanner(a[0], "ly:tuplet-bracket::calc-cross-staff")));
    }

    private static void InstallTupletNumber(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:tuplet-number::print", 1, 1, a =>
            TupletNumber.Print(AsSpanner(a[0], "ly:tuplet-number::print")));

        interpreter.DefinePrimitive("ly:tuplet-number::calc-x-offset", 1, 1, a =>
            TupletNumber.CalcXOffset(AsSpanner(a[0], "ly:tuplet-number::calc-x-offset")));

        interpreter.DefinePrimitive("ly:tuplet-number::calc-y-offset", 1, 1, a =>
            TupletNumber.CalcYOffset(AsSpanner(a[0], "ly:tuplet-number::calc-y-offset")));
    }

    private static void InstallPercentRepeat(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:percent-repeat-interface::percent", 1, 1, a =>
            PercentRepeatInterface.Percent(
                AsGrob(a[0], "ly:percent-repeat-interface::percent")));

        interpreter.DefinePrimitive("ly:percent-repeat-interface::double-percent", 1, 1, a =>
            PercentRepeatInterface.DoublePercent(
                AsGrob(a[0], "ly:percent-repeat-interface::double-percent")));

        interpreter.DefinePrimitive("ly:percent-repeat-interface::beat-slash", 1, 1, a =>
            PercentRepeatInterface.BeatSlash(
                AsGrob(a[0], "ly:percent-repeat-interface::beat-slash")));
    }

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);

    private static Spanner AsSpanner(object value, string procedureName)
        => value as Spanner ?? throw SchemeErrors.WrongType(procedureName, "spanner", value);
}
