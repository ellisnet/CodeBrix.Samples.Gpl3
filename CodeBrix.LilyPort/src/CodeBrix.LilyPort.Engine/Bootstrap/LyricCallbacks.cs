// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The lyric entry points: the lyric extender and hyphen stencils, the two spacing-rod
/// callbacks, the melody-spanner direction interpolator, and
/// <c>LyricCombineMusic</c>'s length callback.
/// <para>
/// Every name here is one <c>scm/define-grobs.scm</c> or
/// <c>scm/define-music-types.scm</c> already refers to, so registering them replaces the
/// pre-registered stubs and moves them from the entry-point closure's Stubbed bucket to
/// its Implemented one.
/// </para>
/// <para>
/// New-in-family binding code; the derivation is recorded in
/// <c>THIRD-PARTY-NOTICES.txt</c>.
/// </para>
/// </summary>
public static class LyricCallbacks
{
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");

    /// <summary>Installs the callbacks, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        interpreter.DefinePrimitive("ly:lyric-extender::print", 1, 1, a =>
            LyricExtender.Print(AsSpanner(a[0], "ly:lyric-extender::print")));

        interpreter.DefinePrimitive("ly:lyric-hyphen::print", 1, 1, a =>
            LyricHyphen.Print(AsSpanner(a[0], "ly:lyric-hyphen::print")));

        interpreter.DefinePrimitive("ly:lyric-hyphen::set-spacing-rods", 1, 1, a =>
            LyricHyphen.SetSpacingRods(AsSpanner(a[0], "ly:lyric-hyphen::set-spacing-rods")));

        interpreter.DefinePrimitive("ly:vowel-transition::set-spacing-rods", 1, 1, a =>
            VowelTransition.SetSpacingRods(
                AsSpanner(a[0], "ly:vowel-transition::set-spacing-rods")));

        // lily/grob-pq-engraver.cc's one LY_DEFINE, which comes with the engraver this
        // group pulled forward (standing rule 3: whoever ports a type owes its
        // LY_DEFINE surface in the same session).
        interpreter.DefinePrimitive("ly:grob-pq<?", 2, 2, a => GrobPqEngraver.PqLess(a[0], a[1]));

        InstallBezierBindings(interpreter);

        interpreter.DefinePrimitive("ly:melody-spanner::calc-neutral-stem-direction", 1, 1, a =>
            MelodySpanner.CalcNeutralStemDirection(
                AsGrob(a[0], "ly:melody-spanner::calc-neutral-stem-direction")));

        // lily/lyric-combine-music.cc, the whole file: a LyricCombineMusic is exactly as
        // long as the MELODY it follows, never as long as the lyrics, which is why its
        // length cannot come from the generic music-length machinery.
        interpreter.DefinePrimitive("ly:lyric-combine-music::length-callback", 1, 1, a =>
        {
            MusicObject me = AsMusic(a[0], "ly:lyric-combine-music::length-callback");
            MusicObject melody = FirstElement(me);
            if (melody == null)
            {
                throw SchemeErrors.WrongType(
                    "ly:lyric-combine-music::length-callback", "music", a[0]);
            }

            return melody.GetLength();
        });
    }

    // lily/bezier-scheme.cc's SECOND binding, pulled forward from the long-tail pool under standing
    // rule 3. Flower's Bezier has been ported since the Flower milestone, so this surface
    // was owed from that day; it simply had no caller until the lyrics group's context-handle fix let
    // tie files reach their own drawing code. Measured, not assumed: before this,
    // tablature-repeat-tie and tablature-tie-spanner died on #<unported ly:bezier-extent>.
    //
    // The FIRST binding, ly:bezier-extract, was already implemented — in
    // GeneralPrimitives.cs, landed by whoever needed it without the ledger row being
    // flipped. So this file completes bezier-scheme.cc rather than porting it whole, and
    // it moves the entry-point closure by ONE, not two. That mismatch is what
    // EntryPointClosureTests caught, and it is the same shape as the six
    // bindings-complete files the first pass found sitting on the worklist.
    private static void InstallBezierBindings(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:bezier-extent", 2, 2, a =>
        {
            Bezier curve = AsBezier(a[0], "ly:bezier-extent");
            Interval extent = curve.Extent(AsAxis(a[1], "ly:bezier-extent"));
            return new Pair(extent.Left, extent.Right);
        });
    }

    private static Axis AsAxis(object value, string procedureName)
    {
        long axis = SchemeConvert.ToLong(value, procedureName);
        if (axis != 0 && axis != 1)
        {
            throw SchemeErrors.WrongType(procedureName, "axis", value);
        }

        return axis == 0 ? Axis.X : Axis.Y;
    }

    // A Bezier arrives as a four-element list of (x . y) pairs -- upstream's
    // is_scm<Bezier>, which accepts exactly four control points and nothing else.
    private static Bezier AsBezier(object value, string procedureName)
    {
        Offset[] control = new Offset[Bezier.ControlCount];
        object cursor = value;
        for (int i = 0; i < Bezier.ControlCount; i++)
        {
            if (!(cursor is Pair pair) || !(pair.Car is Pair point))
            {
                throw SchemeErrors.WrongType(procedureName, "bezier", value);
            }

            control[i] = new Offset(
                SchemeConvert.ToDouble(point.Car, procedureName),
                SchemeConvert.ToDouble(point.Cdr, procedureName));

            cursor = pair.Cdr;
        }

        if (!(cursor is Nil))
        {
            throw SchemeErrors.WrongType(procedureName, "bezier", value);
        }

        return new Bezier(control);
    }

    private static MusicObject FirstElement(MusicObject me)
        => (me.GetProperty(ElementsSymbol) as Pair)?.Car as MusicObject;

    private static MusicObject AsMusic(object value, string procedureName)
        => value as MusicObject ?? throw SchemeErrors.WrongType(procedureName, "music", value);

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);

    private static Spanner AsSpanner(object value, string procedureName)
        => value as Spanner ?? throw SchemeErrors.WrongType(procedureName, "spanner", value);
}
