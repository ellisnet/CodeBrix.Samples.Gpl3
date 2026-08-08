// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The EPG12 entry points: the nine <c>ly:slur::*</c> names.
/// </summary>
/// <remarks>
/// <para>
/// Six come from <c>scm/define-grobs.scm</c> and <c>scm/output-lib.scm</c>. The other
/// three — the outside-slur trio — are never named from Scheme at all: they are chained
/// onto a dodging grob's <c>Y-offset</c> and <c>cross-staff</c> from C++, by
/// <c>Slur::auxiliary_acknowledge_extra_object</c>. They are still entry points, because
/// <c>MAKE_SCHEME_CALLBACK</c> defines the Scheme name either way, and the chaining looks
/// the procedure up BY THAT NAME — so leaving them unregistered would leave the chain
/// holding a stub.
/// </para>
/// <para>
/// Two take optional arguments. Upstream's <c>MAKE_SCHEME_CALLBACK_WITH_OPTARGS</c>
/// computes <c>required = ARGCOUNT - OPTIONAL_COUNT</c>, so <c>(2, 1)</c> is one required
/// plus one optional and <c>(4, 1)</c> is three plus one: in both cases the PREVIOUS value
/// in the callback chain is the trailing argument, and it is absent on the first link.
/// </para>
/// </remarks>
public static class Epg12Callbacks
{
    /// <summary>Installs the callbacks, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        interpreter.DefinePrimitive("ly:slur::calc-direction", 1, 1, a =>
            Slur.CalcDirection(AsGrob(a[0], "ly:slur::calc-direction")));

        interpreter.DefinePrimitive("ly:slur::calc-control-points", 1, 1, a =>
            Slur.CalcControlPoints(AsSpanner(a[0], "ly:slur::calc-control-points")));

        interpreter.DefinePrimitive("ly:slur::calc-cross-staff", 1, 1, a =>
            Slur.CalcCrossStaff(AsGrob(a[0], "ly:slur::calc-cross-staff")));

        interpreter.DefinePrimitive("ly:slur::print", 1, 1, a =>
            Slur.Print(AsGrob(a[0], "ly:slur::print")));

        interpreter.DefinePrimitive("ly:slur::height", 1, 1, a =>
            ToPair(Slur.Height(AsGrob(a[0], "ly:slur::height"))));

        interpreter.DefinePrimitive("ly:slur::pure-height", 3, 3, a =>
            ToPair(Slur.PureHeight(
                AsGrob(a[0], "ly:slur::pure-height"),
                (int)SchemeConvert.ToLong(a[1], "ly:slur::pure-height"),
                (int)SchemeConvert.ToLong(a[2], "ly:slur::pure-height"))));

        interpreter.DefinePrimitive("ly:slur::outside-slur-cross-staff", 2, 2, a =>
            Slur.OutsideSlurCrossStaff(
                AsGrob(a[0], "ly:slur::outside-slur-cross-staff"), a[1]));

        // MAKE_SCHEME_CALLBACK_WITH_OPTARGS (…, 2, 1, ""): grob, previous offset.
        interpreter.DefinePrimitive("ly:slur::outside-slur-callback", 1, 2, a =>
            Slur.OutsideSlurCallback(
                AsGrob(a[0], "ly:slur::outside-slur-callback"),
                a.Length > 1 ? a[1] : Unspecified.Instance));

        // MAKE_SCHEME_CALLBACK_WITH_OPTARGS (…, 4, 1, ""): grob, start, end, previous.
        interpreter.DefinePrimitive("ly:slur::pure-outside-slur-callback", 3, 4, a =>
            Slur.PureOutsideSlurCallback(
                AsGrob(a[0], "ly:slur::pure-outside-slur-callback"),
                (int)SchemeConvert.ToLong(a[1], "ly:slur::pure-outside-slur-callback"),
                (int)SchemeConvert.ToLong(a[2], "ly:slur::pure-outside-slur-callback"),
                a.Length > 3 ? a[3] : Unspecified.Instance));
    }

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);

    private static Spanner AsSpanner(object value, string procedureName)
        => value as Spanner ?? throw SchemeErrors.WrongType(procedureName, "spanner", value);

    private static object ToPair(Interval interval) => new Pair(interval.Left, interval.Right);
}
