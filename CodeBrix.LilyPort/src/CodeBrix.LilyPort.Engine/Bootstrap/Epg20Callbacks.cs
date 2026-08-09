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
/// The EPG20 entry points: chords, frets, tablature, drums, clusters, arpeggio and
/// figured bass.
/// </summary>
/// <remarks>
/// <para>
/// Most of the group is TRANSLATORS, which carry no Scheme surface at all — they are
/// reached by <c>ly/engraver-init.ly</c> naming them in a <c>\consists</c> list, which
/// <c>TranslatorRegistry</c> answers. The names here are the group's grob callbacks and
/// its one <c>LY_DEFINE</c>.
/// </para>
/// <para>
/// <c>ly:arpeggio::pure-height</c> is registered with an OPTIONAL pair of trailing
/// arguments, matching upstream's three-argument <c>MAKE_SCHEME_CALLBACK</c>: the start
/// and end columns are ignored by the body, but a pure callback is invoked with them.
/// </para>
/// </remarks>
public static class Epg20Callbacks
{
    /// <summary>Installs the callbacks, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        // ----- arpeggio.cc -----

        interpreter.DefinePrimitive("ly:arpeggio::calc-cross-staff", 1, 1, a =>
            Arpeggio.CalcCrossStaff(AsGrob(a[0], "ly:arpeggio::calc-cross-staff")));

        interpreter.DefinePrimitive("ly:arpeggio::calc-positions", 1, 1, a =>
            Arpeggio.CalcPositions(AsGrob(a[0], "ly:arpeggio::calc-positions")));

        interpreter.DefinePrimitive("ly:arpeggio::print", 1, 1, a =>
            Arpeggio.Print(AsGrob(a[0], "ly:arpeggio::print")));

        interpreter.DefinePrimitive("ly:arpeggio::width", 1, 1, a =>
            Arpeggio.Width(AsGrob(a[0], "ly:arpeggio::width")));

        interpreter.DefinePrimitive("ly:arpeggio::pure-height", 1, 3, a =>
            Arpeggio.PureHeight(AsGrob(a[0], "ly:arpeggio::pure-height")));

        interpreter.DefinePrimitive("ly:chord-bracket::print", 1, 1, a =>
            ChordBracket.Print(AsGrob(a[0], "ly:chord-bracket::print")));

        interpreter.DefinePrimitive("ly:chord-bracket::width", 1, 1, a =>
            ChordBracket.Width(AsGrob(a[0], "ly:chord-bracket::width")));

        interpreter.DefinePrimitive("ly:chord-slur::print", 1, 1, a =>
            ChordSlur.Print(AsGrob(a[0], "ly:chord-slur::print")));

        interpreter.DefinePrimitive("ly:chord-slur::width", 1, 1, a =>
            ChordSlur.Width(AsGrob(a[0], "ly:chord-slur::width")));

        // ----- cluster.cc -----

        interpreter.DefinePrimitive("ly:cluster::calc-cross-staff", 1, 1, a =>
            Cluster.CalcCrossStaff(AsGrob(a[0], "ly:cluster::calc-cross-staff")));

        interpreter.DefinePrimitive("ly:cluster::print", 1, 1, a =>
            Cluster.Print(AsGrob(a[0], "ly:cluster::print")));

        interpreter.DefinePrimitive("ly:cluster-beacon::height", 1, 1, a =>
            ClusterBeacon.Height(AsGrob(a[0], "ly:cluster-beacon::height")));

        // ----- chord-name.cc -----

        interpreter.DefinePrimitive("ly:chord-name::after-line-breaking", 1, 1, a =>
            ChordName.AfterLineBreaking(AsItem(a[0], "ly:chord-name::after-line-breaking")));

        // ----- figured-bass-continuation.cc -----
        //
        // ONE name, not two. Upstream DECLARES Figured_bass_continuation::print and never
        // defines it; the stencil is Scheme's `figured-bass-continuation::print`, with no
        // `ly:` prefix, from output-lib.scm.

        interpreter.DefinePrimitive("ly:figured-bass-continuation::center-on-figures", 1, 1, a =>
            FiguredBassContinuation.CenterOnFigures(
                AsGrob(a[0], "ly:figured-bass-continuation::center-on-figures")));
    }

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);

    // Chord_name::after_line_breaking is the one callback in this group that upstream
    // narrows to an Item and then asserts on: it reads the grob's COLUMN, which only an
    // item has.
    private static Item AsItem(object value, string procedureName)
        => value as Item ?? throw SchemeErrors.WrongType(procedureName, "item", value);
}
