// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The ancient-notation entry points: the ligature grobs.
/// </summary>
/// <remarks>
/// <para>
/// The group is mostly TRANSLATORS, which carry no Scheme surface at all —
/// <c>ly/engraver-init.ly</c> names them in a <c>\consists</c> list and
/// <c>TranslatorRegistry</c> answers. These five are the whole of its Scheme surface.
/// </para>
/// <para>
/// ⚠ Three of the five are <c>print</c> callbacks that return <c>'()</c>, and they must
/// be registered ANYWAY. Every ligature grob's <c>stencil</c> is one of them, so leaving
/// them unregistered would leave the inert unported placeholder in a <c>stencil</c>
/// property — which is TRUTHY, so the backend would take it for a stencil and try to draw
/// it. An empty stencil and an unported placeholder are not the same answer.
/// </para>
/// <para>
/// The two <c>brew-ligature-primitive</c> callbacks are looked up BY NAME by their
/// engravers, which install them as the <c>stencil</c> of every head they collect. An
/// unregistered name there is worse still: it is not an error, it is a ligature drawn as
/// ordinary note heads.
/// </para>
/// </remarks>
public static class LigatureCallbacks
{
    /// <summary>Installs the callbacks, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        // ----- kievan-ligature.cc -----

        interpreter.DefinePrimitive("ly:kievan-ligature::print", 1, 1, a =>
            KievanLigature.Print(AsGrob(a[0], "ly:kievan-ligature::print")));

        // ----- mensural-ligature.cc -----

        interpreter.DefinePrimitive("ly:mensural-ligature::brew-ligature-primitive", 1, 1, a =>
            MensuralLigature.BrewLigaturePrimitive(
                AsGrob(a[0], "ly:mensural-ligature::brew-ligature-primitive")));

        interpreter.DefinePrimitive("ly:mensural-ligature::print", 1, 1, a =>
            MensuralLigature.Print(AsGrob(a[0], "ly:mensural-ligature::print")));

        // ----- vaticana-ligature.cc -----

        interpreter.DefinePrimitive("ly:vaticana-ligature::brew-ligature-primitive", 1, 1, a =>
            VaticanaLigature.BrewLigaturePrimitive(
                AsGrob(a[0], "ly:vaticana-ligature::brew-ligature-primitive")));

        interpreter.DefinePrimitive("ly:vaticana-ligature::print", 1, 1, a =>
            VaticanaLigature.Print(AsGrob(a[0], "ly:vaticana-ligature::print")));
    }

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);
}
