// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using System;
using System.Collections.Generic;
using System.Linq;
using State = Fresco.Brix.Ly.Lex.State;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Editor; //was previously: frescobaldi/tokeniter.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Reaches the parsed tokens of an open document.
/// <para>
/// There is exactly one tokenization per document — the highlighter's — and
/// everything that needs to know what the text MEANS (the matcher, folding,
/// the outline, autocomplete, the music position) reads it through here rather
/// than re-tokenizing. The highlighter fills in any line not yet reached, so a
/// caller never has to think about how far highlighting has got.
/// </para>
/// </summary>
public static class TokenIter
{
    /// <summary>Gets the tokens of a line.</summary>
    /// <param name="highlighter">The document's highlighter.</param>
    /// <param name="lineNumber">The line number, from 1.</param>
    /// <returns>The tokens; empty for a line out of range.</returns>
    public static Token[] Tokens(LyHighlighter highlighter, int lineNumber)
        => lineNumber >= 1 && lineNumber <= highlighter.Document.LineCount
            ? highlighter.TokensForLine(lineNumber)
            : Array.Empty<Token>();

    /// <summary>Gets the tokens of the line an offset falls on.</summary>
    /// <param name="highlighter">The document's highlighter.</param>
    /// <param name="document">The text store.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The tokens.</returns>
    public static Token[] TokensAt(
        LyHighlighter highlighter, TextDocument document, int offset)
        => Tokens(highlighter, LineNumberAt(document, offset));

    /// <summary>Gets the lexer state at the START of a line.</summary>
    /// <param name="highlighter">The document's highlighter.</param>
    /// <param name="lineNumber">The line number, from 1.</param>
    /// <returns>The state.</returns>
    public static State StateAt(LyHighlighter highlighter, int lineNumber)
        => highlighter.StateAtLineStart(lineNumber);

    /// <summary>Gets the lexer state at the END of a line.</summary>
    /// <param name="highlighter">The document's highlighter.</param>
    /// <param name="lineNumber">The line number, from 1.</param>
    /// <returns>The state.</returns>
    public static State StateEnd(LyHighlighter highlighter, int lineNumber)
        => highlighter.StateAtLineEnd(lineNumber);

    /// <summary>
    /// Gets the index, within its line, of the token an offset is in or just
    /// after.
    /// </summary>
    /// <param name="highlighter">The document's highlighter.</param>
    /// <param name="document">The text store.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>0 when the offset is in the first token, up to the token count
    /// when it is at the end of the line.</returns>
    public static int Index(
        LyHighlighter highlighter, TextDocument document, int offset)
    {
        DocumentLine line = LineAt(document, offset);
        Token[] tokens = Tokens(highlighter, line.LineNumber);
        if (offset >= line.EndOffset)
        {
            return tokens.Length;
        }

        int position = offset - line.Offset;
        int low = 0;
        int high = tokens.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (position < tokens[middle].Pos)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low - 1;
    }

    /// <summary>
    /// Splits a line's tokens around an offset: the tokens before it, the one
    /// it falls inside (or null), and the tokens after it.
    /// </summary>
    /// <param name="highlighter">The document's highlighter.</param>
    /// <param name="document">The text store.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The three parts.</returns>
    public static (Token[] Left, Token Middle, Token[] Right) Partition(
        LyHighlighter highlighter, TextDocument document, int offset)
    {
        DocumentLine line = LineAt(document, offset);
        Token[] tokens = Tokens(highlighter, line.LineNumber);
        int index = Index(highlighter, document, offset);
        int position = offset - line.Offset;

        if (tokens.Length > 0 && index >= 0 && index < tokens.Length
            && tokens[index].Pos < position)
        {
            return (tokens.Take(index).ToArray(), tokens[index],
                tokens.Skip(index + 1).ToArray());
        }

        int split = Math.Max(index, 0);
        return (tokens.Take(split).ToArray(), null, tokens.Skip(split).ToArray());
    }

    /// <summary>Finds the first token with the given text.</summary>
    /// <param name="text">The text to match exactly.</param>
    /// <param name="tokens">The tokens to search.</param>
    /// <returns>The token, or null.</returns>
    public static Token Find(string text, IEnumerable<Token> tokens)
        => tokens?.FirstOrDefault(
            t => string.Equals(t.Text, text, StringComparison.Ordinal));

    /// <summary>Enumerates every token of a document, line by line.</summary>
    /// <param name="highlighter">The document's highlighter.</param>
    /// <returns>The tokens.</returns>
    public static IEnumerable<Token> AllTokens(LyHighlighter highlighter)
    {
        for (int line = 1; line <= highlighter.Document.LineCount; line++)
        {
            foreach (var token in Tokens(highlighter, line))
            {
                yield return token;
            }
        }
    }

    /// <summary>Gets the document offset of a token on a line.</summary>
    /// <param name="document">The text store.</param>
    /// <param name="lineNumber">The line number, from 1.</param>
    /// <param name="token">The token.</param>
    /// <returns>The offset of the token's first character.</returns>
    public static int OffsetOf(TextDocument document, int lineNumber, Token token)
        => document.GetLineByNumber(lineNumber).Offset + token.Pos;

    private static DocumentLine LineAt(TextDocument document, int offset)
        => document.GetLineByOffset(
            Math.Clamp(offset, 0, document.TextLength));

    private static int LineNumberAt(TextDocument document, int offset)
        => LineAt(document, offset).LineNumber;
}
