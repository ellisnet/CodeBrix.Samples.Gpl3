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

using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Ly; //was previously: ly/document.py (class Cursor);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A certain range (selection) in a document: a start position and an end that
/// may be <see langword="null"/>, denoting the end of the document.
/// <para>
/// As long as the cursor is alive its positions are updated when the document
/// changes: text inserted at the start position leaves the start where it is,
/// text inserted at the end moves the end along. Many ported tools describe
/// (part of) a document with one of these.
/// </para>
/// </summary>
public class Cursor
{
    /// <summary>Initializes a cursor over a document range.</summary>
    /// <param name="document">The document.</param>
    /// <param name="start">The start position.</param>
    /// <param name="end">The end position, or <see langword="null"/> for the
    /// document's end.</param>
    public Cursor(DocumentBase document, int start = 0, int? end = null)
    {
        Document = document;
        Start = start;
        End = end;
        document.RegisterCursor(this);
    }

    /// <summary>Gets the document.</summary>
    public DocumentBase Document { get; }

    /// <summary>Gets or sets the start position.</summary>
    public int Start { get; set; }

    /// <summary>Gets or sets the end position; <see langword="null"/> denotes
    /// the end of the document.</summary>
    public int? End { get; set; }

    /// <summary>Returns the block the start position points at.</summary>
    /// <returns>The block.</returns>
    public DocumentBlock StartBlock() => Document.GetBlock(Start);

    /// <summary>Returns the block the end position points at.</summary>
    /// <returns>The block.</returns>
    public DocumentBlock EndBlock()
        => End == null
            ? Document[Document.Count - 1]
            : Document.GetBlock(End.Value);

    /// <summary>
    /// Iterates over the selected blocks. If there are multiple blocks and the
    /// cursor ends on the first position of the last selected block, that
    /// block is not included.
    /// </summary>
    /// <returns>The blocks.</returns>
    public IEnumerable<DocumentBlock> Blocks()
    {
        if (End == Start)
        {
            yield return StartBlock();
        }
        else
        {
            foreach (DocumentBlock block in Document.BlocksForward(StartBlock()))
            {
                if (End != null && Document.Position(block) >= End)
                {
                    break;
                }

                yield return block;
            }
        }
    }

    /// <summary>Returns the selected text.</summary>
    /// <returns>The text.</returns>
    public string Text()
    {
        string text = Document.PlainText();
        int end = End ?? text.Length;
        return text.Substring(Start, end - Start);
    }

    /// <summary>Returns the text before the cursor in its start block.</summary>
    /// <returns>The text.</returns>
    public string TextBefore()
    {
        DocumentBlock block = StartBlock();
        int pos = Start - Document.Position(block);
        return Document.Text(block).Substring(0, pos);
    }

    /// <summary>Returns the text after the cursor in its end block.</summary>
    /// <returns>The text.</returns>
    public string TextAfter()
    {
        if (End == null)
        {
            return string.Empty;
        }

        DocumentBlock block = EndBlock();
        int pos = End.Value - Document.Position(block);
        return Document.Text(block).Substring(pos);
    }

    /// <summary>Returns whether there is some text selected.</summary>
    /// <returns>Whether the selection is non-empty.</returns>
    public bool HasSelection()
    {
        int end = End ?? Document.Size();
        return Start != end;
    }

    /// <summary>Selects all text.</summary>
    public void SelectAll()
    {
        Start = 0;
        End = null;
    }

    /// <summary>Moves the end to the end of its block.</summary>
    public void SelectEndOfBlock()
    {
        if (End != null)
        {
            DocumentBlock end = EndBlock();
            End = Document.Position(end) + Document.Text(end).Length;
        }
    }

    /// <summary>Moves the start to the start of its block.</summary>
    public void SelectStartOfBlock()
    {
        Start = Document.Position(StartBlock());
    }

    /// <summary>Moves the start to the right, like Python's lstrip.</summary>
    /// <param name="chars">The characters to strip, or <see langword="null"/>
    /// for whitespace.</param>
    public void LStrip(char[] chars = null)
    {
        if (HasSelection())
        {
            string text = Text();
            string stripped = chars == null ? text.TrimStart() : text.TrimStart(chars);
            Start += text.Length - stripped.Length;
        }
    }

    /// <summary>Moves the end to the left, like Python's rstrip.</summary>
    /// <param name="chars">The characters to strip, or <see langword="null"/>
    /// for whitespace.</param>
    public void RStrip(char[] chars = null)
    {
        if (HasSelection())
        {
            string text = Text();
            string stripped = chars == null ? text.TrimEnd() : text.TrimEnd(chars);
            int end = End ?? Document.Size();
            end -= text.Length - stripped.Length;
            if (end < Document.Size())
            {
                End = end;
            }
        }
    }

    /// <summary>Strips characters from both selection ends.</summary>
    /// <param name="chars">The characters to strip, or <see langword="null"/>
    /// for whitespace.</param>
    public void Strip(char[] chars = null)
    {
        RStrip(chars);
        LStrip(chars);
    }
}
