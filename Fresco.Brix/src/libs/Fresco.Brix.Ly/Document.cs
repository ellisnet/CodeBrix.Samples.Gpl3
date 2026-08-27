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

using Fresco.Brix.Ly.Lex;
using Fridge = Fresco.Brix.Ly.Lex.Fridge;
using Fresco.Brix.Ly.Slexing;
using Token = Fresco.Brix.Ly.Slexing.Token;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Fresco.Brix.Ly; //was previously: ly/document.py (classes Document, _Block);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A plain text LilyPond source document that auto-updates its tokens.
/// <para>
/// <see cref="Modified"/> is set as soon as the document is changed;
/// <see cref="SetPlainText"/> resets it.
/// </para>
/// </summary>
public class Document : DocumentBase
{
    private readonly Fridge _fridge = new Fridge();
    private List<StringBlock> _blocks;
    private string _mode;
    private string _guessedMode;

    /// <summary>Initializes an empty document.</summary>
    public Document()
        : this(string.Empty, null)
    {
    }

    /// <summary>Initializes a document over text, optionally forcing a mode.</summary>
    /// <param name="text">The text.</param>
    /// <param name="mode">The mode name, or <see langword="null"/> to guess.</param>
    public Document(string text, string mode = null)
    {
        _mode = mode;
        SetPlainText(text);
    }

    /// <summary>Gets or sets whether the document has been changed since it was
    /// loaded or last set.</summary>
    public bool Modified { get; set; }

    /// <summary>Loads the document from a file.</summary>
    /// <param name="filename">The file to read.</param>
    /// <param name="encoding">The encoding name; UTF-8 when omitted.</param>
    /// <param name="mode">The mode, or <see langword="null"/> to guess.</param>
    /// <returns>The document, with <see cref="DocumentBase.Filename"/> set.</returns>
    public static Document Load(string filename, string encoding = "utf-8", string mode = null)
    {
        string text = File.ReadAllText(filename, System.Text.Encoding.GetEncoding(encoding));
        Document document = new Document(text, mode)
        {
            Filename = filename,
        };
        return document;
    }

    /// <summary>Returns a full copy of the document.</summary>
    /// <returns>The copy.</returns>
    public Document Copy()
        => new Document(PlainText(), Mode())
        {
            Filename = Filename,
            Encoding = Encoding,
            Modified = Modified,
        };

    /// <inheritdoc/>
    public override int Count => _blocks.Count;

    /// <inheritdoc/>
    public override DocumentBlock this[int index] => _blocks[index];

    /// <summary>
    /// Sets the mode to one of the tokenizer modes; <see langword="null"/>
    /// auto-determines it.
    /// </summary>
    /// <param name="mode">The mode name or <see langword="null"/>.</param>
    public void SetMode(string mode)
    {
        if (!Modes.Exists(mode))
        {
            mode = null;
        }

        if (mode == _mode)
        {
            return;
        }

        string oldMode = _mode;
        _mode = mode;
        if (mode == null)
        {
            _guessedMode = Modes.GuessMode(PlainText());
            if (_guessedMode == oldMode)
            {
                return;
            }
        }
        else if (oldMode == null)
        {
            if (mode == _guessedMode)
            {
                return;
            }
        }

        UpdateAllTokens();
    }

    /// <summary>Returns the mode (lilypond, html, etc); <see langword="null"/>
    /// means automatic.</summary>
    /// <returns>The mode name or <see langword="null"/>.</returns>
    public string Mode() => _mode;

    /// <inheritdoc/>
    public override void SetPlainText(string text)
    {
        text = (text ?? string.Empty).Replace("\r", string.Empty);
        string[] lines = text.Split('\n');
        _blocks = new List<StringBlock>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            _blocks.Add(new StringBlock(lines[i], i));
        }

        int pos = 0;
        foreach (StringBlock block in _blocks)
        {
            block.Position = pos;
            pos += block.Text.Length + 1;
        }

        if (_mode == null)
        {
            _guessedMode = Modes.GuessMode(text);
        }

        UpdateAllTokens();
        Modified = false;
    }

    /// <inheritdoc/>
    public override Lex.State InitialState() => Modes.CreateState(_mode ?? _guessedMode);

    /// <inheritdoc/>
    public override Lex.State StateEnd(DocumentBlock block)
        => (Lex.State)_fridge.Thaw(((StringBlock)block).State);

    /// <inheritdoc/>
    public override DocumentBlock GetBlock(int position)
    {
        StringBlock last = _blocks[_blocks.Count - 1];
        if (position < 0 || position > last.Position + last.Text.Length)
        {
            return null;
        }

        int lo = 0;
        int hi = _blocks.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (position < _blocks[mid].Position)
            {
                hi = mid;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return _blocks[lo - 1];
    }

    /// <inheritdoc/>
    public override int Index(DocumentBlock block) => ((StringBlock)block).Index;

    /// <inheritdoc/>
    public override int Position(DocumentBlock block) => ((StringBlock)block).Position;

    /// <inheritdoc/>
    public override string Text(DocumentBlock block) => ((StringBlock)block).Text;

    /// <inheritdoc/>
    public override bool IsValid(DocumentBlock block) => block != null;

    /// <inheritdoc/>
    public override Token[] Tokens(DocumentBlock block) => ((StringBlock)block).Tokens;

    private void UpdateAllTokens()
    {
        Lex.State state = InitialState();
        foreach (StringBlock block in _blocks)
        {
            block.Tokens = state.Tokens(block.Text).ToArray();
            block.State = _fridge.Freeze(state);
        }
    }

    /// <inheritdoc/>
    protected override void ApplyChanges()
    {
        StringBlock changed = null;
        foreach ((int start, int? end, string text) in ChangesList)
        {
            StringBlock s = (StringBlock)GetBlock(start);
            changed = s;

            // first remove the old contents
            if (end == null)
            {
                // all text to the end should be removed
                s.Text = s.Text.Substring(0, start - s.Position);
                _blocks.RemoveRange(s.Index + 1, _blocks.Count - (s.Index + 1));
            }
            else
            {
                // remove until the end position
                StringBlock e = (StringBlock)GetBlock(end.Value);
                s.Text = s.Text.Substring(0, start - s.Position)
                    + e.Text.Substring(end.Value - e.Position);
                _blocks.RemoveRange(s.Index + 1, e.Index - s.Index);
            }

            // now insert the new stuff
            if (text.Length > 0)
            {
                string[] lines = text.Split('\n');
                lines[lines.Length - 1] += s.Text.Substring(start - s.Position);
                s.Text = s.Text.Substring(0, start - s.Position) + lines[0];
                List<StringBlock> inserted = new List<StringBlock>(lines.Length - 1);
                for (int i = 1; i < lines.Length; i++)
                {
                    inserted.Add(new StringBlock(lines[i], -1));
                }

                _blocks.InsertRange(s.Index + 1, inserted);
            }

            // make sure this line gets reparsed
            s.Tokens = null;
        }

        // update the position of all the blocks from the last changed one on
        int pos = changed.Position;
        for (int i = changed.Index; i < _blocks.Count; i++)
        {
            _blocks[i].Index = i;
            _blocks[i].Position = pos;
            pos += _blocks[i].Text.Length + 1;
        }

        Modified = true;

        // if the initial state has changed, reparse everything
        if (_mode == null)
        {
            string mode = Modes.GuessMode(PlainText());
            if (mode != _guessedMode)
            {
                _guessedMode = mode;
                UpdateAllTokens();
                return;
            }
        }

        // update the tokens starting at the changed block
        Lex.State state = (Lex.State)State(changed);
        bool reparse = false;
        for (int i = changed.Index; i < _blocks.Count; i++)
        {
            StringBlock block = _blocks[i];
            if (reparse || block.Tokens == null)
            {
                block.Tokens = state.Tokens(block.Text).ToArray();
                int frozen = _fridge.Freeze(state);
                reparse = block.State != frozen;
                block.State = frozen;
            }
            else
            {
                state = (Lex.State)_fridge.Thaw(block.State);
            }
        }
    }

    /// <summary>A line of text; used only by this Document implementation.</summary>
    private sealed class StringBlock : DocumentBlock
    {
        public StringBlock(string text, int index)
        {
            Text = text;
            Index = index;
        }

        /// <summary>The block's text, without newline.</summary>
        public string Text { get; set; }

        /// <summary>The line number.</summary>
        public int Index { get; set; }

        /// <summary>The character position of the block's first character.
        /// int.MaxValue until assigned, so an unpositioned block is never
        /// picked by the binary search — upstream's sys.maxsize default.</summary>
        public int Position { get; set; } = int.MaxValue;

        /// <summary>The fridge number of the state at the END of this block.</summary>
        public int State { get; set; }

        /// <summary>The block's tokens, positions block-relative;
        /// <see langword="null"/> marks it needing a reparse.</summary>
        public Token[] Tokens { get; set; }
    }
}
