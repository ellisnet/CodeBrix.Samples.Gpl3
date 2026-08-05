// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Music;

// New-in-family: this file has no upstream counterpart. Upstream reaches the same Scheme
// through lily/lily-imports.cc's Scm_module/Variable pairs, which are Guile module handles
// -- plumbing this port does not have and does not want (lily-imports.cc is a no-port row
// in the ledger). Derivation is recorded in THIRD-PARTY-NOTICES.txt.

/// <summary>
/// The Scheme side of a music-function call: the current parse location, and the two
/// error reporters in <c>(lily ly-syntax-constructors)</c>.
/// <para>
/// These are looked up BY NAME at the moment of use rather than cached at bootstrap,
/// because <c>ly-syntax-constructors.scm</c> is an on-demand module: caching a variable
/// at install time captures whatever was bound before the module loaded, which is
/// nothing, and the failure looks like a music function that silently reports no errors.
/// </para>
/// </summary>
public static class MusicFunctionSupport
{
    private static readonly Symbol LocationFluid = Symbol.Intern("%location");

    private static readonly Symbol ArgumentErrorName = Symbol.Intern("argument-error");

    private static readonly Symbol CallErrorName = Symbol.Intern("music-function-call-error");

    private static readonly object SyntaxModuleName
        = Pair.List(Symbol.Intern("lily"), Symbol.Intern("ly-syntax-constructors"));

    /// <summary>
    /// Gets the location the parser is currently working at, as
    /// <c>scm/lily.scm</c>'s <c>%location</c> fluid holds it.
    /// </summary>
    /// <returns>
    /// The current <see cref="Origins.Input"/>, or <see langword="false"/> when there is
    /// none — which is what <c>(*location*)</c> answers outside a parse.
    /// </returns>
    public static object CurrentLocation()
    {
        Interpreter interpreter = LilyPondScheme.Current;
        if (interpreter == null)
        {
            return false;
        }

        // %location lives in (lily) — scm/lily.scm line 73 — and is NOT exported, so it
        // has to be looked up there rather than in the root module, which does not use
        // (lily) and therefore answers nothing.
        Variable variable = interpreter.Modules
            .Resolve(Pair.List(Symbol.Intern("lily")))
            .Lookup(LocationFluid);
        if (variable == null || !variable.IsBound)
        {
            return false;
        }

        return variable.GetValue() is Fluid fluid ? fluid.Value : false;
    }

    /// <summary>Calls a signature predicate on a value.</summary>
    /// <param name="predicate">The predicate procedure.</param>
    /// <param name="value">The value to test.</param>
    /// <returns>Whatever the predicate answered.</returns>
    public static object CallPredicate(object predicate, object value)
        => SchemeUtilities.CallCallback(predicate, value);

    /// <summary>
    /// Reports an argument that failed its predicate, through
    /// <c>(lily ly-syntax-constructors)</c>'s <c>argument-error</c>.
    /// </summary>
    /// <param name="position">The one-based argument position.</param>
    /// <param name="predicate">The predicate it failed.</param>
    /// <param name="argument">The offending argument.</param>
    public static void ArgumentError(int position, object predicate, object argument)
    {
        object reporter = LookupSyntax(ArgumentErrorName);
        if (reporter == null)
        {
            // The module is not loaded, so there is nowhere to report to. Say so rather
            // than swallowing it -- a music function that quietly accepts a wrong-typed
            // argument is exactly the silent defect this port keeps finding.
            Flower.Warn.ProgrammingError(
                "argument-error is unavailable: (lily ly-syntax-constructors) has not loaded");
            return;
        }

        SchemeUtilities.CallCallback(reporter, (long)position, predicate, argument);
    }

    /// <summary>
    /// Reports a music function whose result failed the signature's return predicate,
    /// through <c>music-function-call-error</c>.
    /// </summary>
    /// <param name="function">The music function that was called.</param>
    /// <param name="result">The value it returned.</param>
    /// <returns>The fallback value the reporter yields, or <see langword="false"/>.</returns>
    public static object MusicFunctionCallError(MusicFunction function, object result)
    {
        object reporter = LookupSyntax(CallErrorName);
        if (reporter == null)
        {
            Flower.Warn.ProgrammingError(
                "music-function-call-error is unavailable:"
                + " (lily ly-syntax-constructors) has not loaded");
            return false;
        }

        return SchemeUtilities.CallCallback(reporter, function, result);
    }

    private static object LookupSyntax(Symbol name)
    {
        Interpreter interpreter = LilyPondScheme.Current;
        if (interpreter == null)
        {
            return null;
        }

        SchemeModule module = interpreter.Modules.Resolve(SyntaxModuleName);
        Variable variable = module?.Lookup(name);
        if (variable != null && variable.IsBound)
        {
            return variable.GetValue();
        }

        // define-public also puts the name in the root module in this port's module
        // model, so fall back there before giving up.
        variable = interpreter.GuileModule.Lookup(name);
        return variable != null && variable.IsBound ? variable.GetValue() : null;
    }
}
