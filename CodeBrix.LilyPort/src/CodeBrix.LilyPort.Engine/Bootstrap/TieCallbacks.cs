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
/// The tie entry points: every callback <c>scm/define-grobs.scm</c> puts on a
/// <c>Tie</c>, <c>TieColumn</c>, <c>LaissezVibrerTie</c>, <c>RepeatTie</c> or either of
/// their columns.
/// </summary>
/// <remarks>
/// All eight are plain one-argument callbacks; the tie family has no
/// <c>MAKE_SCHEME_CALLBACK_WITH_OPTARGS</c> anywhere.
/// </remarks>
public static class TieCallbacks
{
    /// <summary>Installs the callbacks, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        interpreter.DefinePrimitive("ly:tie::calc-direction", 1, 1, a =>
            Tie.CalcDirection(AsGrob(a[0], "ly:tie::calc-direction")));

        interpreter.DefinePrimitive("ly:tie::calc-control-points", 1, 1, a =>
            Tie.CalcControlPoints(AsSpanner(a[0], "ly:tie::calc-control-points")));

        interpreter.DefinePrimitive("ly:tie::print", 1, 1, a =>
            Tie.Print(AsGrob(a[0], "ly:tie::print")));

        interpreter.DefinePrimitive("ly:tie-column::calc-positioning-done", 1, 1, a =>
            TieColumn.CalcPositioningDone(AsGrob(a[0], "ly:tie-column::calc-positioning-done")));

        interpreter.DefinePrimitive("ly:tie-column::before-line-breaking", 1, 1, a =>
            TieColumn.BeforeLineBreaking(
                AsSpanner(a[0], "ly:tie-column::before-line-breaking")));

        interpreter.DefinePrimitive("ly:semi-tie::calc-control-points", 1, 1, a =>
            SemiTie.CalcControlPoints(AsItem(a[0], "ly:semi-tie::calc-control-points")));

        interpreter.DefinePrimitive("ly:semi-tie-column::calc-positioning-done", 1, 1, a =>
            SemiTieColumn.CalcPositioningDone(
                AsGrob(a[0], "ly:semi-tie-column::calc-positioning-done")));

        interpreter.DefinePrimitive("ly:semi-tie-column::calc-head-direction", 1, 1, a =>
            SemiTieColumn.CalcHeadDirection(
                AsGrob(a[0], "ly:semi-tie-column::calc-head-direction")));
    }

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);

    private static Spanner AsSpanner(object value, string procedureName)
        => value as Spanner ?? throw SchemeErrors.WrongType(procedureName, "spanner", value);

    private static Item AsItem(object value, string procedureName)
        => value as Item ?? throw SchemeErrors.WrongType(procedureName, "item", value);
}
