// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// GOOPS classes for the engine's own value types.
/// <para>
/// LilyPond's <c>scm/operators.scm</c> opens with
/// <c>(define &lt;Moment&gt; (class-of (ly:make-moment 0)))</c> and then specializes
/// <c>+</c>, <c>-</c>, <c>*</c>, <c>/</c> and <c>&lt;</c> on it. That only works if
/// <c>class-of</c> returns a stable, distinct class for each ported type -- so the
/// classes have to exist before the arithmetic can.
/// </para>
/// </summary>
public static class EngineClasses
{
    /// <summary>The class of <see cref="Music.Moment"/>.</summary>
    public static readonly SchemeClass Moment
        = new SchemeClass(Symbol.Intern("<Moment>"), new[] { BuiltinClasses.Top });

    /// <summary>The class of <see cref="Music.Pitch"/>.</summary>
    public static readonly SchemeClass Pitch
        = new SchemeClass(Symbol.Intern("<Pitch>"), new[] { BuiltinClasses.Top });

    /// <summary>The class of <see cref="Music.Duration"/>.</summary>
    public static readonly SchemeClass Duration
        = new SchemeClass(Symbol.Intern("<Duration>"), new[] { BuiltinClasses.Top });

    /// <summary>The class of <see cref="Music.Scale"/>.</summary>
    public static readonly SchemeClass Scale
        = new SchemeClass(Symbol.Intern("<Scale>"), new[] { BuiltinClasses.Top });

    /// <summary>Registers the classes with the core, so <c>class-of</c> finds them.</summary>
    public static void Install()
        => BuiltinClasses.ClassOfExtensionHook = ClassOf;

    /// <summary>Returns the class of an engine value, or null when it is not one.</summary>
    /// <param name="value">The value to classify.</param>
    /// <returns>The class, or <see langword="null"/>.</returns>
    public static SchemeClass ClassOf(object value)
    {
        switch (value)
        {
            case Music.Moment _:
                return Moment;
            case Music.Pitch _:
                return Pitch;
            case Music.Duration _:
                return Duration;
            case Music.Scale _:
                return Scale;
            default:
                return null;
        }
    }
}
