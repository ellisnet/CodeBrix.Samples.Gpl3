// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <content>
/// The RAG18 additions. <c>PushMarkupState</c> is a real mode push — forwarded to the
/// scanner when one is attached, which every markup test needs, since markup mode is
/// where a bare word lexes as a <c>SYMBOL</c> rather than as a note name or a keyword.
/// <c>LilyImport</c> answers a readable stand-in on RAG5's
/// <c>SyntaxConstructor</c> precedent: the real host returns the procedure bound in
/// <c>(lily)</c>, and a test only needs to see WHICH one a markup expression was built
/// around.
/// </content>
internal sealed partial class ScriptedParserHost
{
    /// <inheritdoc/>
    public void PushMarkupState() => PushMode("push-markup-state", LexerState.Markup);

    /// <inheritdoc/>
    public object LilyImport(string name) => "lily:" + name;

    /// <summary>
    /// Reproduces the vendored <c>composed-markup-list</c>
    /// (<c>scm/ly-syntax-constructors.scm</c> 241): map a chain of commands over each
    /// markup, innermost first.
    /// <para>
    /// It is reproduced rather than recorded because it is pure list surgery — its
    /// whole body is a fold of <c>(append cmd (list prev))</c> — and because the
    /// grammar READS THE RESULT BACK: <c>markup</c> and <c>markup_top</c> both take
    /// its car. Reproducing it lets a markup test assert the composed EXPRESSION,
    /// which is what the command order actually means, instead of asserting the order
    /// of an argument list and trusting the constructor.
    /// </para>
    /// <para>
    /// The commands arrive in reverse — upstream's own comment: "a list of commands
    /// with their scheme arguments, in reverse order, eg: ((italic) (raise 4)
    /// (bold))" — so the fold puts the LAST one outermost.
    /// </para>
    /// </summary>
    /// <param name="commands">The command chain, each entry a
    /// <c>(procedure . arguments)</c> list.</param>
    /// <param name="markups">The markups to compose over.</param>
    /// <returns>The list of composed markups, one per input markup.</returns>
    private static object ComposeMarkupList(object commands, object markups)
    {
        List<object> commandList = Pair.ToList(commands);
        List<object> composed = new List<object>();

        foreach (object markup in Pair.ToList(markups))
        {
            object accumulated = markup;
            foreach (object command in commandList)
            {
                List<object> applied = Pair.ToList(command);
                applied.Add(accumulated);
                accumulated = Pair.List(applied.ToArray());
            }

            composed.Add(accumulated);
        }

        return Pair.List(composed.ToArray());
    }
}
