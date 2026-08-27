// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using CodeBrix.Platform.UI.AdvancedTextEdit.Folding;
using Fresco.Brix.Ly.Lex;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Editor; //was previously: frescobaldi/folding.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Works out what can be folded, from the same tokenization everything else
/// reads: anything the lexer treats as an indent — <c>{</c>, <c>&lt;&lt;</c>,
/// a Scheme <c>(</c> — opens a fold, its partner closes it, and a block
/// comment folds as one.
/// <para>
/// Deciding it from the TOKENS rather than from the characters is what keeps a
/// brace inside a string or a comment from opening a fold.
/// </para>
/// </summary>
public sealed class LyFoldingStrategy
{
    private readonly LyHighlighter _highlighter;

    /// <summary>Creates the strategy over a document's tokenization.</summary>
    /// <param name="highlighter">The document's highlighter.</param>
    public LyFoldingStrategy(LyHighlighter highlighter)
        => _highlighter = highlighter;

    /// <summary>Works out the foldable regions of a document.</summary>
    /// <param name="document">The text store.</param>
    /// <returns>The regions, in start order.</returns>
    public IEnumerable<NewFolding> CreateFoldings(TextDocument document)
    {
        List<NewFolding> foldings = new List<NewFolding>();
        Stack<int> starts = new Stack<int>();

        for (int lineNumber = 1; lineNumber <= document.LineCount; lineNumber++)
        {
            DocumentLine line = document.GetLineByNumber(lineNumber);
            foreach (var token in TokenIter.Tokens(_highlighter, lineNumber))
            {
                int offset = line.Offset + token.Pos;
                if (token is IIndent || token is BlockCommentStart)
                {
                    starts.Push(offset + token.Text.Length);
                }
                else if (token is IDedent || token is BlockCommentEnd)
                {
                    if (starts.Count == 0) { continue; }

                    int start = starts.Pop();
                    int end = offset;

                    //A region that never leaves its line has nothing to hide.
                    if (document.GetLineByOffset(start).LineNumber
                        < document.GetLineByOffset(end).LineNumber)
                    {
                        foldings.Add(new NewFolding
                        {
                            StartOffset = start,
                            EndOffset = end,
                        });
                    }
                }
            }
        }

        return foldings.OrderBy(f => f.StartOffset).ToList();
    }

    /// <summary>Recomputes a document's foldings.</summary>
    /// <param name="manager">The editor's folding manager.</param>
    /// <param name="document">The text store.</param>
    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        if (manager == null || document == null) { return; }

        //-1: nothing is known to be broken, so every existing fold may keep
        //its open/closed state.
        manager.UpdateFoldings(CreateFoldings(document), -1);
    }

    /// <summary>Closes every fold.</summary>
    /// <param name="manager">The editor's folding manager.</param>
    public static void FoldAll(FoldingManager manager)
    {
        foreach (var folding in manager?.AllFoldings ?? Enumerable.Empty<FoldingSection>())
        {
            folding.IsFolded = true;
        }
    }

    /// <summary>Opens every fold.</summary>
    /// <param name="manager">The editor's folding manager.</param>
    public static void UnfoldAll(FoldingManager manager)
    {
        foreach (var folding in manager?.AllFoldings ?? Enumerable.Empty<FoldingSection>())
        {
            folding.IsFolded = false;
        }
    }

    /// <summary>Closes only the outermost folds.</summary>
    /// <param name="manager">The editor's folding manager.</param>
    public static void FoldTop(FoldingManager manager)
    {
        if (manager == null) { return; }

        foreach (var folding in manager.AllFoldings)
        {
            //An outermost fold is one nothing else contains.
            folding.IsFolded = manager.GetFoldingsContaining(folding.StartOffset)
                .All(f => f == folding);
        }
    }

    /// <summary>Closes the innermost fold around an offset.</summary>
    /// <param name="manager">The editor's folding manager.</param>
    /// <param name="offset">The offset, normally the caret's.</param>
    public static void FoldCurrent(FoldingManager manager, int offset)
    {
        FoldingSection folding = InnermostAt(manager, offset);
        if (folding != null) { folding.IsFolded = true; }
    }

    /// <summary>Opens the innermost fold around an offset.</summary>
    /// <param name="manager">The editor's folding manager.</param>
    /// <param name="offset">The offset, normally the caret's.</param>
    public static void UnfoldCurrent(FoldingManager manager, int offset)
    {
        FoldingSection folding = InnermostAt(manager, offset);
        if (folding != null) { folding.IsFolded = false; }
    }

    private static FoldingSection InnermostAt(FoldingManager manager, int offset)
        => manager?.GetFoldingsContaining(offset)
            .OrderByDescending(f => f.StartOffset)
            .FirstOrDefault();
}
