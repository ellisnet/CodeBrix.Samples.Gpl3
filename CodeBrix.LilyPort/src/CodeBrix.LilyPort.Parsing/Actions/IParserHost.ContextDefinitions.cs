// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace CodeBrix.LilyPort.Parsing.Actions;

/// <summary>
/// The host members the ContextDefinitions group (context definitions and modifications)
/// added, per the partial-interface convention on the main file.
/// </summary>
public partial interface IParserHost
{
    /// <summary>
    /// Returns a syntax constructor procedure WITHOUT calling it.
    /// <para>
    /// Upstream: the <c>Syntax::name</c> imported variables
    /// (<c>lily/lily-imports.cc</c>), each bound to a procedure in the vendored
    /// <c>scm/ly-syntax-constructors.scm</c>. <see cref="MakeSyntax"/> covers
    /// <c>MAKE_SYNTAX</c>, which calls the procedure with a location;
    /// <c>START_MAKE_SYNTAX</c> instead conses the procedure onto its first
    /// arguments — <c>ly_list (Syntax::name, ...)</c> — for
    /// <c>FINISH_MAKE_SYNTAX</c> (a later group's rules) to apply once the rest of
    /// the arguments exist. This member is the procedure lookup that consing needs.
    /// The name is the SCHEME-side dashed name (<c>context-find-or-create</c>),
    /// the same convention as <see cref="MakeSyntax"/>.
    /// </para>
    /// </summary>
    /// <param name="constructor">The constructor's Scheme name.</param>
    /// <returns>The constructor procedure.</returns>
    object SyntaxConstructor(string constructor);
}
