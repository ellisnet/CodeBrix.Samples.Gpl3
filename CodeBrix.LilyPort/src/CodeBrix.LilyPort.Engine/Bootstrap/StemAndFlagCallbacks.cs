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
/// The stem, flag and stem-tremolo callbacks — the group's <c>MAKE_SCHEME_CALLBACK</c>
/// surface from <c>lily/stem.cc</c>, <c>lily/flag.cc</c> and
/// <c>lily/stem-tremolo.cc</c>.
/// <para>
/// A grob's definition in <c>scm/define-grobs.scm</c> names these by SCHEME NAME, so a
/// ported callback that is not registered here is not reached at all and the grob
/// silently keeps the stub's answer. The logic lives in the static grob classes; this
/// installer only adapts.
/// </para>
/// </summary>
public static class StemAndFlagCallbacks
{
    /// <summary>Installs the callbacks, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallStem(interpreter);
        InstallFlag(interpreter);
        InstallStemTremolo(interpreter);
    }

    private static void InstallStem(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:stem::calc-cross-staff", 1, 1, a =>
            Stem.IsCrossStaff(AsGrob(a[0], "ly:stem::calc-cross-staff")));

        interpreter.DefinePrimitive("ly:stem::calc-default-direction", 1, 1, a =>
            (long)Stem.CalcDefaultDirection(
                AsGrob(a[0], "ly:stem::calc-default-direction")).Value);

        interpreter.DefinePrimitive("ly:stem::calc-direction", 1, 1, a =>
            Stem.CalcDirection(AsGrob(a[0], "ly:stem::calc-direction")));

        interpreter.DefinePrimitive("ly:stem::calc-length", 1, 1, a =>
            Stem.CalcLength(AsGrob(a[0], "ly:stem::calc-length")));

        interpreter.DefinePrimitive("ly:stem::pure-calc-length", 3, 3, a =>
            Stem.PureCalcLength(AsGrob(a[0], "ly:stem::pure-calc-length")));

        interpreter.DefinePrimitive("ly:stem::calc-positioning-done", 1, 1, a =>
            Stem.CalcPositioningDone(AsGrob(a[0], "ly:stem::calc-positioning-done")));

        interpreter.DefinePrimitive("ly:stem::calc-stem-begin-position", 1, 1, a =>
            Stem.InternalCalcStemBeginPosition(
                AsGrob(a[0], "ly:stem::calc-stem-begin-position"), true));

        interpreter.DefinePrimitive("ly:stem::pure-calc-stem-begin-position", 3, 3, a =>
            Stem.InternalCalcStemBeginPosition(
                AsGrob(a[0], "ly:stem::pure-calc-stem-begin-position"), false));

        interpreter.DefinePrimitive("ly:stem::calc-stem-end-position", 1, 1, a =>
            Stem.InternalCalcStemEndPosition(
                AsGrob(a[0], "ly:stem::calc-stem-end-position"), true));

        interpreter.DefinePrimitive("ly:stem::pure-calc-stem-end-position", 3, 3, a =>
            Stem.InternalCalcStemEndPosition(
                AsGrob(a[0], "ly:stem::pure-calc-stem-end-position"), false));

        interpreter.DefinePrimitive("ly:stem::calc-stem-info", 1, 1, a =>
            Stem.CalcStemInfo(AsGrob(a[0], "ly:stem::calc-stem-info")));

        interpreter.DefinePrimitive("ly:stem::extremal-heads", 1, 1, a =>
        {
            DrulArray<Grob> heads
                = Stem.ExtremalHeads(AsGrob(a[0], "ly:stem::extremal-heads"));
            return new Pair(
                heads[Direction.Negative] ?? (object)Nil.Instance,
                heads[Direction.Positive] ?? (object)Nil.Instance);
        });

        interpreter.DefinePrimitive("ly:stem::height", 1, 1, a =>
            ToPair(Stem.InternalHeight(AsGrob(a[0], "ly:stem::height"), true)));

        interpreter.DefinePrimitive("ly:stem::pure-height", 3, 3, a =>
            ToPair(Stem.InternalPureHeight(AsGrob(a[0], "ly:stem::pure-height"), true)));

        interpreter.DefinePrimitive("ly:stem::offset-callback", 1, 1, a =>
            Stem.OffsetCallback(AsGrob(a[0], "ly:stem::offset-callback")));

        interpreter.DefinePrimitive("ly:stem::print", 1, 1, a =>
            Stem.Print(AsGrob(a[0], "ly:stem::print")));

        interpreter.DefinePrimitive("ly:stem::width", 1, 1, a =>
            ToPair(Stem.Width(AsGrob(a[0], "ly:stem::width"))));
    }

    private static void InstallFlag(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:flag::calc-x-offset", 1, 1, a =>
            Flag.CalcXOffset(AsGrob(a[0], "ly:flag::calc-x-offset")));

        interpreter.DefinePrimitive("ly:flag::calc-y-offset", 1, 1, a =>
            Flag.CalcYOffset(AsGrob(a[0], "ly:flag::calc-y-offset")));

        interpreter.DefinePrimitive("ly:flag::pure-calc-y-offset", 3, 3, a =>
            Flag.PureCalcYOffset(AsGrob(a[0], "ly:flag::pure-calc-y-offset")));

        interpreter.DefinePrimitive("ly:flag::glyph-name", 1, 1, a =>
            Flag.GlyphName(AsGrob(a[0], "ly:flag::glyph-name")));

        interpreter.DefinePrimitive("ly:flag::print", 1, 1, a =>
            Flag.Print(AsGrob(a[0], "ly:flag::print")));

        interpreter.DefinePrimitive("ly:flag::width", 1, 1, a =>
            ToPair(Flag.Width(AsGrob(a[0], "ly:flag::width"))));
    }

    private static void InstallStemTremolo(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:stem-tremolo::calc-cross-staff", 1, 1, a =>
            StemTremolo.CalcCrossStaff(AsGrob(a[0], "ly:stem-tremolo::calc-cross-staff")));

        interpreter.DefinePrimitive("ly:stem-tremolo::calc-direction", 1, 1, a =>
            (long)StemTremolo.CalcDirection(
                AsGrob(a[0], "ly:stem-tremolo::calc-direction")).Value);

        interpreter.DefinePrimitive("ly:stem-tremolo::calc-shape", 1, 1, a =>
            StemTremolo.CalcShape(AsGrob(a[0], "ly:stem-tremolo::calc-shape")));

        interpreter.DefinePrimitive("ly:stem-tremolo::calc-slope", 1, 1, a =>
            StemTremolo.CalcSlope(AsGrob(a[0], "ly:stem-tremolo::calc-slope")));

        interpreter.DefinePrimitive("ly:stem-tremolo::calc-width", 1, 1, a =>
            StemTremolo.CalcWidth(AsGrob(a[0], "ly:stem-tremolo::calc-width")));

        interpreter.DefinePrimitive("ly:stem-tremolo::calc-y-offset", 1, 1, a =>
            StemTremolo.CalcYOffset(AsGrob(a[0], "ly:stem-tremolo::calc-y-offset")));

        interpreter.DefinePrimitive("ly:stem-tremolo::pure-calc-y-offset", 3, 3, a =>
            StemTremolo.PureCalcYOffset(
                AsGrob(a[0], "ly:stem-tremolo::pure-calc-y-offset")));

        interpreter.DefinePrimitive("ly:stem-tremolo::print", 1, 1, a =>
            StemTremolo.Print(AsGrob(a[0], "ly:stem-tremolo::print")));

        interpreter.DefinePrimitive("ly:stem-tremolo::pure-height", 3, 3, a =>
            ToPair(StemTremolo.PureHeight(AsGrob(a[0], "ly:stem-tremolo::pure-height"))));

        interpreter.DefinePrimitive("ly:stem-tremolo::width", 1, 1, a =>
            ToPair(StemTremolo.Width(AsGrob(a[0], "ly:stem-tremolo::width"))));
    }

    private static object ToPair(Interval interval) => new Pair(interval.Left, interval.Right);

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);
}
