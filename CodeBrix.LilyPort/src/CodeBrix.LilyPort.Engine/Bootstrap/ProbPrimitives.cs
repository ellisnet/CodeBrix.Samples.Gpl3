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
/// The Scheme bindings for <see cref="Prob"/> — the behaviour of the
/// <c>LY_DEFINE</c> bodies in upstream <c>lily/prob-scheme.cc</c>, over the ported
/// property object. New-in-family code; the derivation is recorded in
/// <c>THIRD-PARTY-NOTICES.txt</c>.
/// <para>
/// Note <c>ly:prob-property</c> upstream is declared with
/// <c>LY_DEFINE_WITH_SETTER</c> — a third declaration macro alongside
/// <c>LY_DEFINE</c> and <c>MAKE_SCHEME_CALLBACK</c>, which is why it (and its five
/// siblings) were missing from the original <c>entry-points.tsv</c> extraction.
/// </para>
/// </summary>
public static class ProbPrimitives
{
    /// <summary>Registers the Prob primitives, replacing their stubs.</summary>
    /// <param name="interpreter">The interpreter to register into.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        interpreter.DefinePrimitive("ly:prob?", 1, 1, a => a[0] is Prob);

        // Return the property, or the given default — '() when no default is given.
        interpreter.DefinePrimitive("ly:prob-property", 2, 3, a =>
        {
            Prob prob = AsProb(a[0], "ly:prob-property");
            object value = prob.GetProperty(AsSymbol(a[1], "ly:prob-property"));
            if (value is Nil && a.Length > 2)
            {
                return a[2];
            }

            return value;
        });

        interpreter.DefinePrimitive("ly:prob-set-property!", 2, 3, a =>
        {
            Prob prob = AsProb(a[0], "ly:prob-set-property!");
            prob.SetProperty(
                AsSymbol(a[1], "ly:prob-set-property!"),
                a.Length > 2 ? a[2] : Nil.Instance);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:prob-property?", 2, 2, a =>
        {
            Prob prob = AsProb(a[0], "ly:prob-property?");
            return prob.GetProperty(AsSymbol(a[1], "ly:prob-property?")) is bool set && set;
        });

        interpreter.DefinePrimitive("ly:prob-type?", 2, 2, a =>
            a[0] is Prob prob && ReferenceEquals(prob.Type, a[1]));

        interpreter.DefinePrimitive("ly:make-prob", 2, -1, a =>
        {
            Prob prob = new Prob(a[0], a[1]);
            for (int i = 2; i + 1 < a.Length; i += 2)
            {
                prob.SetProperty(AsSymbol(a[i], "ly:make-prob"), a[i + 1]);
            }

            return prob;
        });

        interpreter.DefinePrimitive("ly:prob-mutable-properties", 1, 1, a =>
            AsProb(a[0], "ly:prob-mutable-properties").GetPropertyAlist(true));

        interpreter.DefinePrimitive("ly:prob-immutable-properties", 1, 1, a =>
            AsProb(a[0], "ly:prob-immutable-properties").GetPropertyAlist(false));
    }

    private static Prob AsProb(object value, string procedureName)
    {
        if (value is Prob prob)
        {
            return prob;
        }

        throw SchemeErrors.WrongType(procedureName, "Prob", value);
    }

    private static Symbol AsSymbol(object value, string procedureName)
    {
        if (value is Symbol symbol)
        {
            return symbol;
        }

        throw SchemeErrors.WrongType(procedureName, "symbol", value);
    }
}
