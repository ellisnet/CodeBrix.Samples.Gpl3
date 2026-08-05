// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// Makes the anonymous Guile modules LilyPond hands out as SCOPES — an output
/// definition's variable table, a <c>\header</c> block, a parser scope.
/// <para>
/// Upstream this is <c>ly_make_module</c> in <c>lily/ly-module.cc</c>: a fresh module
/// that exports everything it defines and uses both the root module and <c>(lily)</c>,
/// so a <c>#(...)</c> evaluated inside the scope can still call the Scheme layer. The
/// ledger carries <c>ly-module.cc</c> as a no-port row because LilyScheme's own module
/// objects replace Guile's plumbing wholesale; the RECIPE, which is behaviour rather
/// than plumbing, is reproduced here so every scope in the port is built the same way.
/// </para>
/// <para>
/// DIVERGENCE, and it is load-bearing: upstream's scope modules are ANONYMOUS, and
/// these are NAMED and REGISTERED. The expander resolves an imported MACRO only in a
/// module it can name — in an anonymous one, <c>define-music-function</c> reads as an
/// ordinary variable and its argument list is evaluated. The same divergence, for the
/// same reason, as the parser's own scopes; the underlying expander limitation is
/// recorded in <c>Parsing/PORT-COVERAGE.txt</c> under LS-FIX, and closing it there lets
/// this go back to matching upstream.
/// </para>
/// </summary>
public static class LilyModules
{
    private static readonly object Gate = new object();

    private static long _serial;

    /// <summary>
    /// Makes a scope module on the ambient interpreter.
    /// <para>Equivalent to <c>ly_make_module ()</c>. The interpreter is the process-global
    /// one <see cref="LilyPondScheme.Current"/> publishes; when there is none — a fixture
    /// that builds engine objects without a Scheme layer — the module is still made, just
    /// without imports, so an <see cref="Engine.Layout.OutputDef"/> constructed in
    /// isolation still has a real scope to read and write.</para>
    /// </summary>
    /// <param name="kind">A word naming what the scope belongs to, used in the module's
    /// registered name.</param>
    /// <returns>The new module.</returns>
    public static SchemeModule Make(string kind) => Make(LilyPondScheme.Current, kind);

    /// <summary>Makes a scope module on a given interpreter.</summary>
    /// <param name="interpreter">The interpreter whose module registry and <c>(lily)</c>
    /// module the scope should join, or <see langword="null"/> for a scope with no imports.</param>
    /// <param name="kind">A word naming what the scope belongs to.</param>
    /// <returns>The new module.</returns>
    public static SchemeModule Make(Interpreter interpreter, string kind)
    {
        long serial;
        lock (Gate)
        {
            serial = ++_serial;
        }

        SchemeModule module = new SchemeModule(
            Pair.List(Symbol.Intern("lily"), Symbol.Intern(kind ?? "scope"), serial));

        if (interpreter == null)
        {
            return module;
        }

        module.AddUse(interpreter.Modules.RootModule);
        module.AddUse(interpreter.Modules.Resolve(Pair.List(Symbol.Intern("lily"))));
        interpreter.Modules.Register(module);
        return module;
    }

    /// <summary>
    /// Copies every binding of one module into another.
    /// <para>Upstream: <c>ly_module_copy</c>, which <c>Output_def</c>'s copy constructor
    /// and <c>get_header</c> both use. LOCAL bindings only — what a module imports is not
    /// its to hand on.</para>
    /// </summary>
    /// <param name="destination">The module to copy into.</param>
    /// <param name="source">The module to copy from.</param>
    public static void Copy(SchemeModule destination, SchemeModule source)
    {
        if (destination == null || source == null)
        {
            return;
        }

        foreach (KeyValuePair<Symbol, Variable> binding in source.Bindings)
        {
            if (binding.Value.IsBound)
            {
                destination.Define(binding.Key, binding.Value.GetValue());
            }
        }
    }
}
