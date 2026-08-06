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
/// The scopes are ANONYMOUS, matching upstream, since LS-FIX (2026-08-05). They
/// spent EPG1–EPG3 named and registered instead, because the expander resolved an
/// imported MACRO only in a module it could name; LilyScheme now gives an anonymous
/// module a lazy name on the first <c>module-name</c> ask — Guile's own boot-9
/// behaviour — so the divergence is retired. Both halves are recorded in
/// <c>Parsing/PORT-COVERAGE.txt</c>.
/// </para>
/// </summary>
public static class LilyModules
{
    /// <summary>
    /// Makes a scope module on the ambient interpreter.
    /// <para>Equivalent to <c>ly_make_module ()</c>. The interpreter is the process-global
    /// one <see cref="LilyPondScheme.Current"/> publishes; when there is none — a fixture
    /// that builds engine objects without a Scheme layer — the module is still made, just
    /// without imports, so an <see cref="Engine.Layout.OutputDef"/> constructed in
    /// isolation still has a real scope to read and write.</para>
    /// </summary>
    /// <param name="kind">A word naming what the scope belongs to. Kept for the call
    /// sites' self-description; since the scopes went back to being ANONYMOUS
    /// (LS-FIX, 2026-08-05) it no longer reaches the module.</param>
    /// <returns>The new module.</returns>
    public static SchemeModule Make(string kind) => Make(LilyPondScheme.Current, kind);

    /// <summary>Makes a scope module on a given interpreter.</summary>
    /// <param name="interpreter">The interpreter whose module registry and <c>(lily)</c>
    /// module the scope should join, or <see langword="null"/> for a scope with no imports.</param>
    /// <param name="kind">A word naming what the scope belongs to.</param>
    /// <returns>The new module.</returns>
    public static SchemeModule Make(Interpreter interpreter, string kind)
    {
        // ANONYMOUS, matching upstream's ly_make_module at last. The scopes were
        // named-and-registered from EPG1 until 2026-08-05 as a workaround for the
        // expander not resolving imported macros in anonymous modules; LilyScheme
        // now names an anonymous module lazily on the first module-name ask,
        // exactly as Guile's boot-9 does, so the workaround is retired (LS-FIX —
        // see Parsing/PORT-COVERAGE.txt, which records both halves).
        SchemeModule module = new SchemeModule(null);

        if (interpreter == null)
        {
            return module;
        }

        module.AddUse(interpreter.Modules.RootModule);
        module.AddUse(interpreter.Modules.Resolve(Pair.List(Symbol.Intern("lily"))));
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
