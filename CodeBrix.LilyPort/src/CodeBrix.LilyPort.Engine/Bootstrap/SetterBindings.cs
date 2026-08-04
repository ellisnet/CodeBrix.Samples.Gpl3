// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// Attaches setters to the six entry points upstream declares with
/// <c>LY_DEFINE_WITH_SETTER</c>, so that generalized <c>set!</c> works on them.
/// <para>
/// The macro does two things, and the port had only been doing one. It defines the
/// getter as an ordinary primitive AND binds the Scheme name to
/// <c>scm_make_procedure_with_setter (getter, setter)</c>. Miss the second and the
/// getter still works perfectly — but
/// <c>(set! (ly:music-property m 'duration) d)</c> fails with "Not a procedure with
/// setter", which is exactly the form <c>make-music</c> uses. So every piece of music
/// LilyPond's own Scheme constructs would fail to be built, while every test that only
/// READS a property would pass.
/// </para>
/// <para>
/// This is the same extraction gap that hid these six entry points from
/// <c>entry-points.tsv</c> in the first place (recorded in
/// <c>THIRD-PARTY-NOTICES.txt</c>): the tsv was corrected, but the SETTER half of the
/// macro's behaviour had still never been wired. It surfaced the moment the iterator
/// work first asked the Scheme layer to build real music.
/// </para>
/// <para>
/// New-in-family code; the derivation is recorded in <c>THIRD-PARTY-NOTICES.txt</c>.
/// </para>
/// </summary>
public static class SetterBindings
{
    /// <summary>
    /// The getter/setter pairs, exactly as the six <c>LY_DEFINE_WITH_SETTER</c>
    /// declarations name them.
    /// </summary>
    private static readonly (string Getter, string Setter)[] Pairs =
    {
        ("ly:context-property", "ly:context-set-property!"),
        ("ly:grob-property", "ly:grob-set-property!"),
        ("ly:grob-object", "ly:grob-set-object!"),
        ("ly:grob-parent", "ly:grob-set-parent!"),
        ("ly:music-property", "ly:music-set-property!"),
        ("ly:prob-property", "ly:prob-set-property!"),
    };

    /// <summary>Gets the getter names that must carry a setter.</summary>
    public static IReadOnlyList<string> GettersWithSetters
    {
        get
        {
            List<string> names = new List<string>(Pairs.Length);
            foreach ((string getter, string _) in Pairs)
            {
                names.Add(getter);
            }

            return names;
        }
    }

    /// <summary>
    /// Attaches each setter to its getter. Must run AFTER every primitive involved has
    /// been defined, because it looks both halves up by name.
    /// </summary>
    /// <param name="interpreter">The interpreter to wire.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        foreach ((string getterName, string setterName) in Pairs)
        {
            object getter = Lookup(interpreter, getterName);
            object setter = Lookup(interpreter, setterName);

            // A missing half is not fatal: the getter still works, and the entry point
            // may simply not be ported yet. It IS worth saying so, because the symptom
            // otherwise appears far away, inside whatever Scheme tried to set!.
            if (!(getter is Procedure procedure))
            {
                continue;
            }

            if (!(setter is Procedure))
            {
                CodeBrix.LilyPort.Flower.Warn.ProgrammingError(
                    "no setter available for " + getterName + "; (set! (" + getterName
                    + " …) …) will fail");
                continue;
            }

            procedure.Setter = setter;
        }
    }

    private static object Lookup(Interpreter interpreter, string name)
    {
        Variable variable = interpreter.CurrentModule.Lookup(Symbol.Intern(name));
        return variable != null && variable.IsBound ? variable.GetValue() : null;
    }
}
