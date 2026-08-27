// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Editor;
using Fresco.Brix.Ly;
using System;
using System.Runtime.CompilerServices;
using LexState = Fresco.Brix.Ly.Lex.State;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Documents; //was previously: frescobaldi/lydocument.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// THE bridge (the plan's §5.2): implements the ported ly.document API over the
/// editor's live <see cref="TextDocument"/>, so every ported ly tool — pitch,
/// rhythm, convert-ly, reformat — operates on the open document unchanged,
/// exactly Frescobaldi's architecture. Blocks are the editor's own lines;
/// tokens come from the one shared tokenization (<see cref="LyHighlighter"/>);
/// applied changes go through the editor document, one undo group per batch.
/// </summary>
public class AteLyDocument : DocumentBase
{
    private readonly ConditionalWeakTable<DocumentLine, LineBlock> _blocks
        = new ConditionalWeakTable<DocumentLine, LineBlock>();

    /// <summary>Initializes the bridge over an editor document and its
    /// highlighter (the token cache).</summary>
    /// <param name="document">The editor document.</param>
    /// <param name="highlighter">The document's highlighter.</param>
    public AteLyDocument(TextDocument document, LyHighlighter highlighter)
    {
        TextDocument = document ?? throw new ArgumentNullException(nameof(document));
        Highlighter = highlighter ?? throw new ArgumentNullException(nameof(highlighter));
    }

    /// <summary>Gets the editor document.</summary>
    public TextDocument TextDocument { get; }

    /// <summary>Gets the highlighter supplying the shared tokenization.</summary>
    public LyHighlighter Highlighter { get; }

    /// <inheritdoc/>
    public override int Count => TextDocument.LineCount;

    /// <inheritdoc/>
    public override DocumentBlock this[int index]
        => Wrap(TextDocument.GetLineByNumber(index + 1));

    /// <inheritdoc/>
    public override string PlainText() => TextDocument.Text;

    /// <inheritdoc/>
    public override void SetPlainText(string text)
        => TextDocument.Text = (text ?? string.Empty).Replace("\r", string.Empty);

    /// <inheritdoc/>
    public override DocumentBlock GetBlock(int position)
        => position >= 0 && position <= TextDocument.TextLength
            ? Wrap(TextDocument.GetLineByOffset(position))
            : null;

    /// <inheritdoc/>
    public override int Index(DocumentBlock block) => Line(block).LineNumber - 1;

    /// <inheritdoc/>
    public override int Position(DocumentBlock block) => Line(block).Offset;

    /// <inheritdoc/>
    public override string Text(DocumentBlock block)
    {
        DocumentLine line = Line(block);
        return TextDocument.GetText(line.Offset, line.Length);
    }

    /// <inheritdoc/>
    public override bool IsValid(DocumentBlock block)
        => block != null && !Line(block).IsDeleted;

    /// <inheritdoc/>
    public override Token[] Tokens(DocumentBlock block)
        => Highlighter.TokensForLine(Line(block).LineNumber);

    /// <inheritdoc/>
    public override LexState InitialState()
        => Ly.Lex.Modes.CreateState(Highlighter.Mode);

    /// <inheritdoc/>
    public override LexState State(DocumentBlock block)
        => Highlighter.StateAtLineStart(Line(block).LineNumber);

    /// <inheritdoc/>
    public override LexState StateEnd(DocumentBlock block)
        => Highlighter.StateAtLineEnd(Line(block).LineNumber);

    /// <inheritdoc/>
    protected override void ApplyChanges()
    {
        // One undo group per batch, the editor-side spelling of Frescobaldi's
        // QTextCursor edit block. Changes arrive sorted with starts DESCENDING
        // (the base's contract), so earlier offsets stay valid while later
        // ones are replaced.
        TextDocument.BeginUpdate();
        try
        {
            foreach ((int start, int? end, string text) in ChangesList)
            {
                int changeEnd = end ?? TextDocument.TextLength;
                TextDocument.Replace(start, changeEnd - start, text);
            }
        }
        finally
        {
            TextDocument.EndUpdate();
        }
    }

    private static DocumentLine Line(DocumentBlock block) => ((LineBlock)block).Line;

    private DocumentBlock Wrap(DocumentLine line)
        => line == null ? null : _blocks.GetValue(line, l => new LineBlock(l));

    /// <summary>One editor line as a ly document block; identity is the line's
    /// own (one wrapper per line, weakly held).</summary>
    private sealed class LineBlock : DocumentBlock
    {
        public LineBlock(DocumentLine line)
        {
            Line = line;
        }

        public DocumentLine Line { get; }
    }
}
