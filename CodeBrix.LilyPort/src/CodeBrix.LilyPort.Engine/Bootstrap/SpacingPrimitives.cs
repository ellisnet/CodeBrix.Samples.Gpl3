// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The Scheme surface of the spacing solver: <c>lily/simple-spacer-scheme.cc</c> and
/// <c>lily/spring-smob.cc</c>.
/// <para>
/// Both are leaf binding files whose TYPES the engine has carried since horizontal spacing
/// (<see cref="SimpleSpacer"/>, <see cref="Spring"/>). Landing the bindings is standing
/// rule 3's other half, and the long-tail closure's job.
/// </para>
/// </summary>
public static class SpacingPrimitives
{
    /// <summary>Installs the spacing primitives, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallSpring(interpreter);
        InstallSpacer(interpreter);
    }

    /// <summary>
    /// <c>spring-smob.cc</c>: the constructor and the two strength setters.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallSpring(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:make-spring", 2, 2, a =>
            new Spring(
                SchemeConvert.ToDouble(a[0], "ly:make-spring"),
                SchemeConvert.ToDouble(a[1], "ly:make-spring")));

        interpreter.DefinePrimitive("ly:spring-set-inverse-compress-strength!", 2, 2, a =>
            SetStrength(a, "ly:spring-set-inverse-compress-strength!", compress: true));

        interpreter.DefinePrimitive("ly:spring-set-inverse-stretch-strength!", 2, 2, a =>
            SetStrength(a, "ly:spring-set-inverse-stretch-strength!", compress: false));
    }

    /// <summary>
    /// The shared body of the two <c>ly:spring-set-inverse-*-strength!</c> setters.
    /// </summary>
    /// <param name="arguments">The primitive's arguments.</param>
    /// <param name="procedureName">The entry point's name, for errors.</param>
    /// <param name="compress">Whether to set the compress half rather than the stretch half.</param>
    /// <returns>A fresh spring holding the new value, as upstream returns.</returns>
    /// <remarks>
    /// ⚠ These setters MUTATE THE CALLER'S SPRING IN PLACE and then return a COPY of it —
    /// upstream calls the setter on the smob it was handed, then answers
    /// <c>smobbed_copy ()</c>, which allocates a new one. Both halves are reproduced here.
    /// <para>
    /// <see cref="Spring"/> is a struct (upstream's is a plain C++ class held by a
    /// <c>Simple_smob</c>), so the value Scheme holds is a BOX. Unboxing it into a local,
    /// mutating that and returning it would leave the caller's spring untouched — the
    /// copy-not-reference defect standing trap 9 records, which has now bitten this one
    /// struct three separate times. <see cref="Unsafe.Unbox{T}"/> takes a reference INTO
    /// the existing box, which is the faithful equivalent of upstream's pointer, and is
    /// the only place in the engine that needs it.
    /// </para>
    /// </remarks>
    private static object SetStrength(object[] arguments, string procedureName, bool compress)
    {
        if (!(arguments[0] is Spring))
        {
            throw SchemeErrors.WrongType(procedureName, "spring", arguments[0]);
        }

        double strength = SchemeConvert.ToDouble(arguments[1], procedureName);

        ref Spring spring = ref Unsafe.Unbox<Spring>(arguments[0]);
        if (compress)
        {
            spring.SetInverseCompressStrength(strength);
        }
        else
        {
            spring.SetInverseStretchStrength(strength);
        }

        // Boxing here allocates the new spring upstream's smobbed_copy () returns.
        return spring;
    }

    /// <summary>
    /// <c>simple-spacer-scheme.cc</c>: solve a spring-and-rod problem and answer the
    /// force followed by every object's position.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallSpacer(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:solve-spring-rod-problem", 4, 4, a =>
        {
            // The empty-springs case answers (0.0 0.0) and returns BEFORE validating
            // anything else — upstream's first statement, and the reason a caller may
            // legitimately pass an empty rod list alongside empty springs.
            if (a[0] is Nil)
            {
                return Pair.List(0.0, 0.0);
            }

            SimpleSpacer spacer = new SimpleSpacer();
            for (object cursor = a[0]; cursor is Pair pair; cursor = pair.Cdr)
            {
                if (!(pair.Car is Pair entry) || !(entry.Cdr is Pair rest))
                {
                    throw SchemeErrors.WrongType(
                        "ly:solve-spring-rod-problem", "list of springs", a[0]);
                }

                double ideal = SchemeConvert.ToDouble(entry.Car, "ly:solve-spring-rod-problem");
                double inverseHooke
                    = SchemeConvert.ToDouble(rest.Car, "ly:solve-spring-rod-problem");

                // Upstream builds the spring with min_dist 0.0 and then sets BOTH
                // strengths to the one inverse-Hooke value the caller gave.
                Spring spring = new Spring(ideal, 0.0);
                spring.SetInverseCompressStrength(inverseHooke);
                spring.SetInverseStretchStrength(inverseHooke);
                spacer.AddSpring(spring);
            }

            for (object cursor = a[1]; cursor is Pair pair; cursor = pair.Cdr)
            {
                if (!(pair.Car is Pair entry) || !(entry.Cdr is Pair second)
                    || !(second.Cdr is Pair third))
                {
                    throw SchemeErrors.WrongType(
                        "ly:solve-spring-rod-problem", "list of rods", a[1]);
                }

                spacer.AddRod(
                    SchemeConvert.ToInt(entry.Car, "ly:solve-spring-rod-problem"),
                    SchemeConvert.ToInt(second.Car, "ly:solve-spring-rod-problem"),
                    SchemeConvert.ToDouble(third.Car, "ly:solve-spring-rod-problem"));
            }

            double length = SchemeConvert.ToDouble(a[2], "ly:solve-spring-rod-problem");
            bool ragged = Evaluator.IsTrue(a[3]);

            SpacerSolution solution = spacer.Solve(length, ragged);
            List<double> positions = spacer.SpringPositions(solution.Force, ragged);

            // The force is #f when the constraints could NOT be met, and that is the
            // caller's only signal — the positions are still answered either way.
            List<object> result = new List<object>(positions.Count + 1)
            {
                solution.Fits ? (object)solution.Force : false,
            };

            foreach (double position in positions)
            {
                result.Add(position);
            }

            return Pair.ListFrom(result);
        });
    }
}
