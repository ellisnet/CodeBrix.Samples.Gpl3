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
using System.Collections.Generic;

namespace Fresco.Brix.Ly; //was previously: ly/document.py (class Runner);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Iterates back and forth over a document's tokens; can stop anywhere and
/// remembers its current token. Crossing a block boundary yields a synthetic
/// newline token, exactly upstream's.
/// </summary>
public class Runner
{
    private readonly bool _withPosition;
    private Token[] _tokens;
    private int _index;

    /// <summary>Initializes the runner at position 0 of a document.</summary>
    /// <param name="document">The document.</param>
    /// <param name="tokensWithPosition">Whether tokens carry document positions
    /// instead of block positions.</param>
    public Runner(DocumentBase document, bool tokensWithPosition = false)
    {
        Document = document;
        _withPosition = tokensWithPosition;
        MoveToBlock(document[0]);
    }

    /// <summary>Creates a runner positioned so that yielding forward starts
    /// with the first complete token after the cursor's start.</summary>
    /// <param name="cursor">The cursor to position at.</param>
    /// <param name="afterToken">Whether to position AFTER the token there, so
    /// it gets yielded going backward.</param>
    /// <param name="tokensWithPosition">Whether tokens carry document
    /// positions.</param>
    /// <returns>The runner.</returns>
    public static Runner At(
        Cursor cursor, bool afterToken = false, bool tokensWithPosition = false)
    {
        Runner runner = new Runner(cursor.Document, tokensWithPosition);
        runner.SetPosition(cursor.Start, afterToken);
        return runner;
    }

    /// <summary>Gets the document.</summary>
    public DocumentBase Document { get; }

    /// <summary>Gets the current block.</summary>
    public DocumentBlock Block { get; private set; }

    /// <summary>Positions the runner at the specified position.</summary>
    /// <param name="position">The character position.</param>
    /// <param name="afterToken">Whether to position AFTER the token there, so
    /// it gets yielded going backward.</param>
    public void SetPosition(int position, bool afterToken = false)
    {
        DocumentBlock block = Document.GetBlock(position);
        MoveToBlock(block);
        if (afterToken)
        {
            foreach (Token t in ForwardLine())
            {
                if (Position() + t.Length >= position)
                {
                    _index += 1;
                    break;
                }
            }
        }
        else
        {
            foreach (Token t in ForwardLine())
            {
                if (Position() + t.Length > position)
                {
                    _index -= 1;
                    break;
                }
            }
        }
    }

    /// <summary>Positions the runner at the start (or past the end) of a
    /// block.</summary>
    /// <param name="block">The block.</param>
    /// <param name="atEnd">Whether to position past the end.</param>
    /// <returns>Whether the block was valid.</returns>
    public bool MoveToBlock(DocumentBlock block, bool atEnd = false)
    {
        if (!Document.IsValid(block))
        {
            return false;
        }

        Block = block;
        _tokens = _withPosition
            ? Document.TokensWithPosition(block)
            : Document.Tokens(block);
        _index = atEnd ? _tokens.Length : -1;
        return true;
    }

    private Token NewlineToken()
    {
        int pos = Document.Text(Block).Length;
        if (_withPosition)
        {
            pos += Document.Position(Block);
        }

        return new Lex.Newline("\n", pos);
    }

    /// <summary>Returns the next token, or <see langword="null"/> if there is
    /// none (upstream returns False).</summary>
    /// <param name="currentBlock">Whether to stop at the end of the current
    /// block.</param>
    /// <returns>The token or <see langword="null"/>.</returns>
    public Token Next(bool currentBlock = false)
    {
        if (_index < _tokens.Length - 1)
        {
            _index += 1;
            return _tokens[_index];
        }

        if (currentBlock || !NextBlock())
        {
            return null;
        }

        return NewlineToken();
    }

    /// <summary>Returns the previous token, or <see langword="null"/> if there
    /// is none.</summary>
    /// <param name="currentBlock">Whether to stop at the start of the current
    /// block.</param>
    /// <returns>The token or <see langword="null"/>.</returns>
    public Token Previous(bool currentBlock = false)
    {
        if (_index > 0)
        {
            _index -= 1;
            return _tokens[_index];
        }

        if (currentBlock || !PreviousBlock())
        {
            return null;
        }

        return NewlineToken();
    }

    /// <summary>Yields tokens in forward direction in the current block.</summary>
    /// <returns>The tokens.</returns>
    public IEnumerable<Token> ForwardLine() => Forward(true);

    /// <summary>Yields tokens in forward direction across blocks.</summary>
    /// <returns>The tokens.</returns>
    public IEnumerable<Token> Forward() => Forward(false);

    private IEnumerable<Token> Forward(bool currentBlock)
    {
        while (true)
        {
            Token token = Next(currentBlock);
            if (token == null)
            {
                break;
            }

            yield return token;
        }
    }

    /// <summary>Yields tokens in backward direction in the current block.</summary>
    /// <returns>The tokens.</returns>
    public IEnumerable<Token> BackwardLine() => Backward(true);

    /// <summary>Yields tokens in backward direction across blocks.</summary>
    /// <returns>The tokens.</returns>
    public IEnumerable<Token> Backward() => Backward(false);

    private IEnumerable<Token> Backward(bool currentBlock)
    {
        while (true)
        {
            Token token = Previous(currentBlock);
            if (token == null)
            {
                break;
            }

            yield return token;
        }
    }

    /// <summary>Goes to the previous block, positioning at its end by
    /// default.</summary>
    /// <param name="atEnd">Whether to position at the end.</param>
    /// <returns>Whether there was a previous block.</returns>
    public bool PreviousBlock(bool atEnd = true)
        => MoveToBlock(Document.PreviousBlock(Block), atEnd);

    /// <summary>Goes to the next block, positioning at its start by
    /// default.</summary>
    /// <param name="atEnd">Whether to position at the end instead.</param>
    /// <returns>Whether there was a next block.</returns>
    public bool NextBlock(bool atEnd = false)
        => MoveToBlock(Document.NextBlock(Block), atEnd);

    /// <summary>Re-returns the last yielded token, or <see langword="null"/>
    /// when the block has none.</summary>
    /// <returns>The token or <see langword="null"/>.</returns>
    public Token CurrentToken()
    {
        if (_tokens.Length == 0)
        {
            return null;
        }

        int index = _index;
        if (index < 0)
        {
            index = 0;
        }
        else if (index >= _tokens.Length)
        {
            index = _tokens.Length - 1;
        }

        return _tokens[index];
    }

    /// <summary>Returns the position of the current token.</summary>
    /// <returns>The document position.</returns>
    public int Position()
    {
        if (_tokens.Length > 0)
        {
            int pos = CurrentToken().Pos;
            if (!_withPosition)
            {
                pos += Document.Position(Block);
            }

            return pos;
        }

        //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14). Python reads
        //`self._d` here — an attribute that does not exist, the field being
        //`_doc` — so upstream raises AttributeError on a block with no tokens.
        //The intended value is plainly the block's position, which is what
        //every other path in this method answers, and it is what is answered
        //here. Nothing recorded moves: no fixture reaches an empty block.
        return Document.Position(Block);
    }

    /// <summary>Returns a new runner at the current position.</summary>
    /// <returns>The copy.</returns>
    public Runner Copy()
    {
        Runner copy = new Runner(Document, _withPosition)
        {
            Block = Block,
            _tokens = _tokens,
            _index = _index,
        };
        return copy;
    }
}
