// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The beam entry points: every <c>ly:beam::*</c> callback <c>scm/define-grobs.scm</c>
/// names on a <c>Beam</c>.
/// <para>
/// Two of them take OPTIONAL arguments, matching upstream's
/// <c>MAKE_SCHEME_CALLBACK_WITH_OPTARGS</c>: the rest-collision pair is chained onto a
/// rest's <c>Y-offset</c>, where the previous value in the chain arrives as a trailing
/// argument that is absent on the first link.
/// </para>
/// </summary>
public static class BeamCallbacks
{
    /// <summary>Installs the callbacks, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        interpreter.DefinePrimitive("ly:beam::calc-normal-stems", 1, 1, a =>
            Beam.CalcNormalStems(AsGrob(a[0], "ly:beam::calc-normal-stems")));

        interpreter.DefinePrimitive("ly:beam::calc-direction", 1, 1, a =>
            Beam.CalcDirection(AsGrob(a[0], "ly:beam::calc-direction")));

        interpreter.DefinePrimitive("ly:beam::calc-beaming", 1, 1, a =>
            Beam.CalcBeaming(AsGrob(a[0], "ly:beam::calc-beaming")));

        interpreter.DefinePrimitive("ly:beam::calc-stem-shorten", 1, 1, a =>
            Beam.CalcStemShorten(AsGrob(a[0], "ly:beam::calc-stem-shorten")));

        interpreter.DefinePrimitive("ly:beam::calc-cross-staff", 1, 1, a =>
            Beam.IsCrossStaff(AsGrob(a[0], "ly:beam::calc-cross-staff")));

        interpreter.DefinePrimitive("ly:beam::set-stem-lengths", 1, 1, a =>
            Beam.SetStemLengths(AsGrob(a[0], "ly:beam::set-stem-lengths")));

        interpreter.DefinePrimitive("ly:beam::quanting", 3, 3, a =>
            Beam.Quanting(AsGrob(a[0], "ly:beam::quanting"), a[1], a[2]));

        interpreter.DefinePrimitive("ly:beam::tremolo-springs-and-rods", 1, 1, a =>
            Beam.TremoloSpringsAndRods(
                AsSpanner(a[0], "ly:beam::tremolo-springs-and-rods")));

        interpreter.DefinePrimitive("ly:beam::calc-beam-segments", 1, 1, a =>
            Beam.CalcBeamSegments(AsSpanner(a[0], "ly:beam::calc-beam-segments")));

        interpreter.DefinePrimitive("ly:beam::calc-x-positions", 1, 1, a =>
            Beam.CalcXPositions(AsSpanner(a[0], "ly:beam::calc-x-positions")));

        interpreter.DefinePrimitive("ly:beam::print", 1, 1, a =>
            Beam.Print(AsSpanner(a[0], "ly:beam::print")));

        // MAKE_SCHEME_CALLBACK_WITH_OPTARGS (…, 2, 1, ""): grob, previous offset.
        interpreter.DefinePrimitive("ly:beam::rest-collision-callback", 1, 2, a =>
            Beam.RestCollisionCallback(
                AsGrob(a[0], "ly:beam::rest-collision-callback"),
                a.Length > 1 ? a[1] : Unspecified.Instance));

        // MAKE_SCHEME_CALLBACK_WITH_OPTARGS (…, 4, 1, ""): grob, start, end, previous.
        interpreter.DefinePrimitive("ly:beam::pure-rest-collision-callback", 3, 4, a =>
            Beam.PureRestCollisionCallback(
                AsGrob(a[0], "ly:beam::pure-rest-collision-callback"),
                a.Length > 3 ? a[3] : Unspecified.Instance));
    }

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);

    private static Spanner AsSpanner(object value, string procedureName)
        => value as Spanner ?? throw SchemeErrors.WrongType(procedureName, "spanner", value);
}
