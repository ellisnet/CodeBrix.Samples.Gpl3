// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <content>
/// The <c>IParserHost</c> members RULE ACTION GROUP 4 added: the INITIAL lexer-mode
/// push, recorded like the other mode operations, and the output-definition scope
/// push. The REAL host must route identifier assignments into the definition's own
/// variable table while its scope is open; the scripted host pushes a recorded
/// stand-in module instead — what the tests assert is WHICH definitions were
/// scoped, in what order, and which assignments arrived through each.
/// </content>
internal sealed partial class ScriptedParserHost
{
    /// <summary>Gets the definitions <see cref="AddOutputDefScope"/> scoped, each
    /// with the stand-in module it received, in order.</summary>
    public List<(OutputDef Definition, FakeModule Module)> OutputDefScopes { get; }
        = new List<(OutputDef, FakeModule)>();

    /// <summary>Records the push into the INITIAL lexer mode.</summary>
    public void PushInitialState() => LexerModeOperations.Add("push-initial-state");

    /// <summary>
    /// Pushes a stand-in module for the definition's scope, recording the pairing;
    /// <see cref="RemoveScope"/> pops it like any other scope.
    /// </summary>
    /// <param name="definition">The output definition being scoped.</param>
    public void AddOutputDefScope(OutputDef definition)
    {
        FakeModule module = new FakeModule();
        OutputDefScopes.Add((definition, module));
        Scopes.Add(module);
    }
}
