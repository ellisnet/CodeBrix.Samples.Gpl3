// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The Scheme-side support layer LilyPond's <c>scm/</c> expects to already exist.
/// <para>
/// LilyPond runs on Guile with a handful of extras installed before <c>scm/</c> loads:
/// bindings that Guile itself provides but LilyScheme's prelude does not, and bindings
/// LilyPond's C++ startup defines that are not <c>LY_DEFINE</c> entry points. None of
/// this is a C++ port; it is the environment the Scheme layer assumes.
/// </para>
/// <para>
/// Anything genuinely implemented in C++ belongs in the engine and is reached through
/// <see cref="EnginePrimitives"/>, not here.
/// </para>
/// </summary>
public static class EngineSupport
{
    private const string SupportResource = "support.scm";

    /// <summary>Installs the support layer into a bootstrapped interpreter.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        string source = ReadSupportSource();
        SchemeBootstrap.LoadExpanded(interpreter, source, SupportResource);
    }

    /// <summary>Reads the vendored support prelude.</summary>
    /// <returns>The Scheme source text.</returns>
    public static string ReadSupportSource()
    {
        string source = LilyPondScheme.ReadSupportResource(SupportResource);
        if (source == null)
        {
            throw new InvalidOperationException(
                "Embedded resource '" + SupportResource + "' is missing from the assembly.");
        }

        return source;
    }
}
