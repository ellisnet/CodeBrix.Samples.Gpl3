// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Layout;

namespace CodeBrix.LilyPort.Parsing.Actions;

/// <content>
/// The members RULE ACTION GROUP 4 (output definitions, paper and tempo) added to
/// the host seam: the INITIAL lexer-mode push an output definition's body runs
/// under, and the definition-scope push its variables land through.
/// </content>
public partial interface IParserHost
{
    /// <summary>
    /// Puts the lexer into the INITIAL (toplevel) mode, stacking the current mode —
    /// how an output definition's body escapes whatever mode surrounds it until
    /// <see cref="PopLexerState"/> restores it.
    /// <para>Upstream: <c>Lily_lexer::push_initial_state</c>.</para>
    /// </summary>
    void PushInitialState();

    /// <summary>
    /// Pushes an output definition's variable scope onto the lexer's scope stack, so
    /// that assignments in the definition's body land in it and lookups see it
    /// first; <see cref="RemoveScope"/> pops it like any other scope.
    /// <para>Upstream: <c>Lily_lexer::add_scope (def-&gt;scope_)</c>. A separate
    /// member from <see cref="AddScope"/> because upstream's <c>scope_</c> is a
    /// module while the port's <see cref="OutputDef"/> keeps its scope as a
    /// symbol-keyed dictionary — there is no module value to hand over, so the HOST
    /// is responsible for routing identifier traffic into the definition while its
    /// scope is open.</para>
    /// </summary>
    /// <param name="definition">The output definition whose scope is opened.</param>
    void AddOutputDefScope(OutputDef definition);
}
