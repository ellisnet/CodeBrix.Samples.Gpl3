// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The EPG9 entry points: the accidental interface's grob callbacks, accidental
/// placement's positioning, the clef modifier's alignment, and the two
/// relative-octave MUSIC callbacks.
/// <para>
/// The grob callbacks are named by <c>scm/define-grobs.scm</c> (an Accidental's
/// <c>stencil</c> IS <c>ly:accidental-interface::print</c>); the music callbacks are
/// named by <c>scm/define-music-types.scm</c> as <c>to-relative-callback</c> values,
/// and reached through <c>MusicObject.ToRelativeOctave</c> during music
/// interpretation rather than during engraving. Implementing them here overwrites the
/// pre-registered stubs, which is what moves them from the closure's Stubbed bucket to
/// its Implemented one.
/// </para>
/// </summary>
public static class Epg9Callbacks
{

    /// <summary>Installs the callbacks, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallAccidental(interpreter);
        InstallClefModifier(interpreter);
        InstallRelativeOctave(interpreter);

        // ly:grob::x-parent-positioning was pulled forward here by EPG9's demand
        // loop (it is the X-offset of exactly the grobs this group makes, and its
        // whole job is to DEMAND the X parent's positioning-done, which resolves to
        // AccidentalPlacement.CalcPositioningDone). EPG7 pulled the same binding —
        // and its y sibling — the same day; the single registration lives in
        // Epg7Callbacks over Objects/GrobClosure.cs.
    }

    private static void InstallAccidental(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:accidental-interface::print", 1, 1, a =>
            AccidentalInterface.Print(AsGrob(a[0], "ly:accidental-interface::print")));

        interpreter.DefinePrimitive("ly:accidental-interface::height", 1, 1, a =>
            ToPair(AccidentalInterface.Height(
                AsGrob(a[0], "ly:accidental-interface::height"))));

        interpreter.DefinePrimitive("ly:accidental-interface::horizontal-skylines", 1, 1, a =>
            AccidentalInterface.HorizontalSkylines(
                AsGrob(a[0], "ly:accidental-interface::horizontal-skylines")).ToScheme());

        interpreter.DefinePrimitive("ly:accidental-interface::remove-tied", 1, 1, a =>
        {
            AccidentalInterface.RemoveTied(
                AsGrob(a[0], "ly:accidental-interface::remove-tied"));
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:accidental-placement::calc-positioning-done", 1, 1, a =>
            AccidentalPlacement.CalcPositioningDone(
                AsGrob(a[0], "ly:accidental-placement::calc-positioning-done")));
    }

    private static void InstallClefModifier(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:clef-modifier::calc-parent-alignment", 1, 1, a =>
            ClefModifier.CalcParentAlignment(
                AsGrob(a[0], "ly:clef-modifier::calc-parent-alignment")));
    }

    private static void InstallRelativeOctave(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:relative-octave-check::relative-callback", 2, 2, a =>
            RelativeOctaveCheck.RelativeCallback(
                AsMusic(a[0], "ly:relative-octave-check::relative-callback"),
                AsPitch(a[1], "ly:relative-octave-check::relative-callback")));

        interpreter.DefinePrimitive(
            "ly:relative-octave-music::no-relative-callback", 2, 2, a =>
                RelativeOctaveMusic.NoRelativeCallback(a[0], a[1]));

        interpreter.DefinePrimitive("ly:relative-octave-music::relative-callback", 2, 2, a =>
            RelativeOctaveMusic.RelativeCallback(a[0], a[1]));
    }

    private static object ToPair(Flower.Interval interval)
        => new Pair(interval.Left, interval.Right);

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);

    private static MusicObject AsMusic(object value, string procedureName)
        => value as MusicObject
           ?? throw SchemeErrors.WrongType(procedureName, "music", value);

    private static Pitch AsPitch(object value, string procedureName)
        => value as Pitch ?? throw SchemeErrors.WrongType(procedureName, "pitch", value);
}
