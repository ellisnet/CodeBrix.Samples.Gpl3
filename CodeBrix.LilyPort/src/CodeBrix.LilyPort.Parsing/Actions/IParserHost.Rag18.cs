// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace CodeBrix.LilyPort.Parsing.Actions;

/// <content>
/// The members RULE ACTION GROUP 18 (markup modes, lists and structure) added: the
/// last of the lexer-mode pushes, and the <c>(lily)</c> module lookup that markup
/// expressions are BUILT from.
/// </content>
public partial interface IParserHost
{
    /// <summary>
    /// Puts the lexer into markup mode, stacking the current mode.
    /// <para>Upstream: <c>Lily_lexer::push_markup_state</c>. Like RAG12's mode
    /// pushes, a REAL host must forward this onto the running
    /// <see cref="Lexing.ModalScanner"/>: markup mode is where a bare word is a
    /// <c>SYMBOL</c> rather than a note name, and where <c>\command</c> is looked up
    /// as a markup command first — a host that merely records the push leaves the
    /// scanner reading the markup as ordinary music.</para>
    /// </summary>
    void PushMarkupState();

    /// <summary>
    /// Returns the value of a variable in the <c>(lily)</c> module WITHOUT calling it.
    /// <para>
    /// Upstream: the <c>Lily::name</c> imported variables (<c>lily/lily-imports.cc</c>,
    /// module <c>(lily)</c>) — the sibling of RAG5's <see cref="SyntaxConstructor"/>,
    /// which does the same for the <c>Syntax::</c> imports out of the separate
    /// <c>(lily ly-syntax-constructors)</c> module. This one is a lookup rather than a
    /// call because a markup IS its expression: <c>\markup \line { }</c> reduces to
    /// the LIST <c>(line-markup ...)</c>, so the procedure is consed in and applied
    /// later, when the markup is interpreted. The name is the SCHEME-side dashed name
    /// (<c>line-markup</c>), the same convention as <see cref="MakeSyntax"/>.
    /// </para>
    /// <para>
    /// The three this group reaches are all markup commands from the vendored
    /// <c>scm/define-markup-commands.scm</c>: <c>line-markup</c>,
    /// <c>score-markup</c> and <c>score-lines-markup-list</c>.
    /// </para>
    /// </summary>
    /// <param name="name">The variable's Scheme name.</param>
    /// <returns>The bound value.</returns>
    object LilyImport(string name);
}
