// This file is part of python-ly, https://pypi.python.org/pypi/python-ly
//
// Copyright (c) 2013 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation, either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
// See http://www.gnu.org/licenses/ for more information.

using Fresco.Brix.Ly.Slexing;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Fresco.Brix.Ly; //was previously: ly/document.py (OUTSIDE/PARTIAL/INSIDE, class Source);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// How tokens overlapping a <see cref="Source"/> range's edges are treated —
/// upstream's OUTSIDE (-1), PARTIAL (0), INSIDE (1) constants.
/// </summary>
public enum OverlapMode
{
    /// <summary>Tokens that touch the selected range are also yielded.</summary>
    Outside = -1,

    /// <summary>Tokens that overlap the start or end positions are yielded.</summary>
    Partial = 0,

    /// <summary>Only tokens fully contained in the range are yielded (default).</summary>
    Inside = 1,
}

/// <summary>
/// Helper iterator over the (block, tokens) stream of a document range.
/// <para>
/// Iterating the source object yields the tokens (with a synthetic newline at
/// each block boundary), while <see cref="Block"/> holds the current block.
/// The <see cref="Tokens"/> enumerator carries the REMAINDER of the current
/// block's tokens and is shared with the main iteration, exactly as upstream's
/// generator attribute is. With a state, every yielded token first updates it
/// (<c>state.follow</c>).
/// </para>
/// </summary>
public class Source : IEnumerable<Token>
{
    private readonly DocumentBase _doc;
    private readonly bool _withPosition;
    private readonly IEnumerator<(DocumentBlock Block, IEnumerator<Token> Tokens)> _gen;
    private readonly Func<Token> _newline;
    private readonly IEnumerator<Token> _stream;
    private bool _pushback;
    private Token _last;

    /// <summary>Initializes the iterator over a cursor's range.</summary>
    /// <param name="cursor">The range to iterate.</param>
    /// <param name="state">The state the tokens update, or <see langword="null"/>
    /// for none.</param>
    /// <param name="stateFromDocument">Whether to take the state from the
    /// document instead (upstream's <c>state=True</c>); overrides
    /// <paramref name="state"/>.</param>
    /// <param name="partial">How edge-overlapping tokens are treated.</param>
    /// <param name="tokensWithPosition">Whether tokens carry document positions
    /// instead of block positions.</param>
    public Source(
        Cursor cursor,
        Lex.State state = null,
        bool stateFromDocument = false,
        OverlapMode partial = OverlapMode.Inside,
        bool tokensWithPosition = false)
    {
        _doc = cursor.Document;
        DocumentBase document = _doc;
        DocumentBlock startBlock = document.GetBlock(cursor.Start);
        _withPosition = tokensWithPosition;
        Func<DocumentBlock, Token[]> tokensMethod = tokensWithPosition
            ? document.TokensWithPosition
            : (Func<DocumentBlock, Token[]>)document.Tokens;

        int startPos = 0;
        int endPos = 0;

        // start, end predicates
        Func<Token, bool> startPred;
        Func<Token, bool> endPred;
        switch (partial)
        {
            case OverlapMode.Outside:
                startPred = t => t.End < startPos;
                endPred = t => t.Pos > endPos;
                break;
            case OverlapMode.Partial:
                startPred = t => t.End <= startPos;
                endPred = t => t.Pos >= endPos;
                break;
            default:
                startPred = t => t.Pos < startPos;
                endPred = t => t.End > endPos;
                break;
        }

        // if a state is wanted, use it (stateFromDocument: pick it off the doc)
        if (stateFromDocument)
        {
            state = document.State(startBlock);
        }

        State = state;
        Func<DocumentBlock, IEnumerator<Token>> tokenSource;
        if (state != null)
        {
            tokenSource = block => FollowingTokens(tokensMethod(block), state);
        }
        else
        {
            tokenSource = block => ((IEnumerable<Token>)tokensMethod(block)).GetEnumerator();
        }

        // where to start
        Func<DocumentBlock, IEnumerator<Token>> sourceStart;
        if (cursor.Start != 0)
        {
            startPos = cursor.Start;
            if (!tokensWithPosition)
            {
                startPos -= document.Position(startBlock);
            }

            sourceStart = block => SkippingStart(tokenSource(block), startPred);
        }
        else
        {
            sourceStart = tokenSource;
        }

        // where to end
        DocumentBlock endBlock = null;
        if (cursor.End != null)
        {
            endBlock = cursor.EndBlock();
            endPos = cursor.End.Value;
            if (!tokensWithPosition)
            {
                endPos -= document.Position(endBlock);
            }
        }

        _gen = Generate(document, startBlock, endBlock, cursor.End != null,
            sourceStart, tokenSource, endPred);

        if (tokensWithPosition)
        {
            _newline = () => new Lex.Newline("\n", document.Position(Block) - 1);
        }
        else
        {
            _newline = () => new Lex.Newline(
                "\n", document.Text(document.PreviousBlock(Block)).Length);
        }

        // initialize block and tokens
        if (_gen.MoveNext())
        {
            Block = _gen.Current.Block;
            Tokens = _gen.Current.Tokens;
        }

        _stream = Stream().GetEnumerator();
    }

    /// <summary>Gets the state the tokens update, or <see langword="null"/>.</summary>
    public Lex.State State { get; }

    /// <summary>Gets the current block.</summary>
    public DocumentBlock Block { get; private set; }

    /// <summary>Gets the remaining tokens of the current block — iterating this
    /// consumes them from the main stream too, upstream's shared generator.</summary>
    public IEnumerator<Token> Tokens { get; private set; }

    /// <summary>Gets the document.</summary>
    public DocumentBase Document => _doc;

    /// <summary>Returns the next token, or <see langword="null"/> at the end —
    /// upstream's <c>__next__</c> (honouring a pushback).</summary>
    /// <returns>The token or <see langword="null"/>.</returns>
    public Token NextToken()
    {
        if (_pushback)
        {
            _pushback = false;
            return _last;
        }

        if (_stream.MoveNext())
        {
            _last = _stream.Current;
            return _last;
        }

        return null;
    }

    /// <summary>Yields the last yielded token again on the next request; can be
    /// undone with <paramref name="pushback"/> = false.</summary>
    /// <param name="pushback">Whether to push back.</param>
    public void Pushback(bool pushback = true) => _pushback = pushback;

    /// <summary>Re-returns the last yielded token.</summary>
    /// <returns>The token, or <see langword="null"/> when none was yielded yet.</returns>
    public Token CurrentToken() => _last;

    /// <summary>Returns the position of the token in the document.</summary>
    /// <param name="token">The token.</param>
    /// <returns>The position.</returns>
    public int Position(Token token)
    {
        int pos = token.Pos;
        if (!_withPosition)
        {
            pos += _doc.Position(Block);
        }

        return pos;
    }

    /// <summary>
    /// Yields the tokens until the current parser is quit. Only usable with a
    /// state enabled.
    /// </summary>
    /// <returns>The tokens.</returns>
    public IEnumerable<Token> UntilParserEnd()
    {
        int depth = State.Depth();
        Token token;
        while ((token = NextToken()) != null)
        {
            yield return token;
            if (State.Depth() < depth && !_pushback)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Consumes an iterable (supposed to be reading from this source) until a
    /// position; returns the last token if that overlaps the position.
    /// </summary>
    /// <param name="iterable">The tokens to consume.</param>
    /// <param name="position">The position to stop at.</param>
    /// <returns>The overlapping token, or <see langword="null"/>.</returns>
    public Token Consume(IEnumerable<Token> iterable, int position)
    {
        if (_doc.Position(Block) < position)
        {
            foreach (Token t in iterable)
            {
                int pos = Position(t);
                int end = pos + t.Length;
                if (end == position)
                {
                    return null;
                }

                if (end > position)
                {
                    return t;
                }
            }
        }

        return null;
    }

    /// <summary>Gets the enumerator over the remaining tokens; sequential
    /// enumerations continue where the previous one stopped, like the python
    /// iterator the class ports.</summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<Token> GetEnumerator()
    {
        Token token;
        while ((token = NextToken()) != null)
        {
            yield return token;
        }
    }

    /// <summary>Gets the non-generic enumerator.</summary>
    /// <returns>The enumerator.</returns>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static IEnumerator<Token> FollowingTokens(Token[] tokens, Lex.State state)
    {
        foreach (Token t in tokens)
        {
            state.Follow(t);
            yield return t;
        }
    }

    private static IEnumerator<Token> SkippingStart(
        IEnumerator<Token> source, Func<Token, bool> startPred)
    {
        while (source.MoveNext())
        {
            if (!startPred(source.Current))
            {
                yield return source.Current;
                while (source.MoveNext())
                {
                    yield return source.Current;
                }
            }
        }
    }

    private static IEnumerator<Token> EndingTokens(
        IEnumerator<Token> source, Func<Token, bool> endPred)
    {
        while (source.MoveNext())
        {
            if (endPred(source.Current))
            {
                break;
            }

            yield return source.Current;
        }
    }

    private static IEnumerator<(DocumentBlock, IEnumerator<Token>)> Generate(
        DocumentBase document,
        DocumentBlock startBlock,
        DocumentBlock endBlock,
        bool hasEnd,
        Func<DocumentBlock, IEnumerator<Token>> sourceStart,
        Func<DocumentBlock, IEnumerator<Token>> tokenSource,
        Func<Token, bool> endPred)
    {
        Func<DocumentBlock, IEnumerator<Token>> source = sourceStart;
        DocumentBlock block = startBlock;
        if (hasEnd)
        {
            while (!ReferenceEquals(block, endBlock))
            {
                yield return (block, source(block));
                source = tokenSource;
                block = document.NextBlock(block);
            }

            yield return (block, EndingTokens(source(block), endPred));
        }
        else
        {
            foreach (DocumentBlock forward in document.BlocksForward(startBlock))
            {
                yield return (forward, source(forward));
                source = tokenSource;
            }
        }
    }

    private IEnumerable<Token> Stream()
    {
        while (Tokens != null && Tokens.MoveNext())
        {
            yield return Tokens.Current;
        }

        while (_gen.MoveNext())
        {
            Block = _gen.Current.Block;
            Tokens = _gen.Current.Tokens;
            yield return _newline();
            while (Tokens.MoveNext())
            {
                yield return Tokens.Current;
            }
        }
    }
}
