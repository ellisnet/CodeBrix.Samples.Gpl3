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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Fresco.Brix.Ly; //was previously: ly/document.py (classes DocumentBase, _Block's base);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One line of text in a document — python-ly's opaque "block" objects, given a
/// common base class so the C# APIs stay typed. A block supports only identity
/// comparison; everything else is asked of the document.
/// </summary>
public abstract class DocumentBlock
{
}

/// <summary>
/// Abstract base class for document instances: a line-oriented view over
/// LilyPond source text, each line carrying its tokens, with a change protocol
/// that batches edits and re-tokenizes what they touched.
/// <para>
/// Edits are made through <see cref="SetText"/>/<see cref="Delete"/>. Batch
/// them by bracketing with <see cref="Writing"/> (upstream's
/// <c>with document:</c> context); an unbracketed edit applies immediately.
/// Changes in one batch may not overlap. On an exception, call
/// <see cref="CancelWriting"/> — upstream's context manager cancels the batch
/// when the block raises, and a C# using-scope cannot see the exception, so
/// the cancel is explicit here.
/// </para>
/// </summary>
public abstract class DocumentBase
{
    private readonly Dictionary<int, List<(int? End, string Text)>> _changes
        = new Dictionary<int, List<(int? End, string Text)>>();

    private readonly List<WeakReference<Cursor>> _cursors
        = new List<WeakReference<Cursor>>();

    private List<(int Start, int? End, string Text)> _changesList;
    private int _writing;

    /// <summary>Gets or sets the filename of the document on disk, or
    /// <see langword="null"/>.</summary>
    public string Filename { get; set; }

    /// <summary>Gets or sets the encoding of the document on disk, or
    /// <see langword="null"/>.</summary>
    public string Encoding { get; set; }

    /// <summary>Returns the number of blocks.</summary>
    /// <returns>The block count.</returns>
    public abstract int Count { get; }

    /// <summary>Returns the block at the specified index.</summary>
    /// <param name="index">The line number, starting at 0.</param>
    /// <returns>The block.</returns>
    public abstract DocumentBlock this[int index] { get; }

    /// <summary>The document contents as a plain text string.</summary>
    /// <returns>The text, blocks joined with newlines.</returns>
    public virtual string PlainText()
        => string.Join("\n", Blocks().Select(Text));

    /// <summary>Sets the document contents to the text string.</summary>
    /// <param name="text">The new text.</param>
    public abstract void SetPlainText(string text);

    /// <summary>Returns the number of characters in the document.</summary>
    /// <returns>The size.</returns>
    public virtual int Size()
    {
        DocumentBlock lastBlock = this[Count - 1];
        return Position(lastBlock) + Text(lastBlock).Length;
    }

    /// <summary>Returns the text block at the specified character position, or
    /// <see langword="null"/> when the position is out of range.</summary>
    /// <param name="position">The character position.</param>
    /// <returns>The block.</returns>
    public abstract DocumentBlock GetBlock(int position);

    /// <summary>Returns the line number of the block (starting with 0).</summary>
    /// <param name="block">The block.</param>
    /// <returns>The line number.</returns>
    public abstract int Index(DocumentBlock block);

    /// <summary>Iterates over all blocks.</summary>
    /// <returns>The blocks, first to last.</returns>
    public IEnumerable<DocumentBlock> Blocks() => BlocksForward(this[0]);

    /// <summary>Iterates forward starting with the specified block.</summary>
    /// <param name="block">The first block.</param>
    /// <returns>The blocks.</returns>
    public IEnumerable<DocumentBlock> BlocksForward(DocumentBlock block)
    {
        while (IsValid(block))
        {
            yield return block;
            block = NextBlock(block);
        }
    }

    /// <summary>Iterates backwards starting with the specified block.</summary>
    /// <param name="block">The first block.</param>
    /// <returns>The blocks.</returns>
    public IEnumerable<DocumentBlock> BlocksBackward(DocumentBlock block)
    {
        while (IsValid(block))
        {
            yield return block;
            block = PreviousBlock(block);
        }
    }

    /// <summary>Returns the character position of the specified block.</summary>
    /// <param name="block">The block.</param>
    /// <returns>The position.</returns>
    public abstract int Position(DocumentBlock block);

    /// <summary>Returns the text of the specified block.</summary>
    /// <param name="block">The block.</param>
    /// <returns>The text.</returns>
    public abstract string Text(DocumentBlock block);

    /// <summary>Returns the next block, which may be <see langword="null"/>.</summary>
    /// <param name="block">The block.</param>
    /// <returns>The next block or <see langword="null"/>.</returns>
    public virtual DocumentBlock NextBlock(DocumentBlock block)
    {
        int index = Index(block);
        return index < Count - 1 ? this[index + 1] : null;
    }

    /// <summary>Returns the previous block, which may be <see langword="null"/>.</summary>
    /// <param name="block">The block.</param>
    /// <returns>The previous block or <see langword="null"/>.</returns>
    public virtual DocumentBlock PreviousBlock(DocumentBlock block)
    {
        int index = Index(block);
        return index > 0 ? this[index - 1] : null;
    }

    /// <summary>Returns whether the block is a valid block.</summary>
    /// <param name="block">The block, possibly <see langword="null"/>.</param>
    /// <returns>Whether it is valid.</returns>
    public abstract bool IsValid(DocumentBlock block);

    /// <summary>Returns whether the block is empty or blank.</summary>
    /// <param name="block">The block.</param>
    /// <returns>Whether it is blank.</returns>
    public virtual bool IsBlank(DocumentBlock block)
    {
        string text = Text(block);
        return string.IsNullOrEmpty(text) || text.All(char.IsWhiteSpace);
    }

    /// <summary>
    /// Starts (or nests) a modification batch and returns the scope whose
    /// disposal ends it — upstream's <c>with document:</c>. When the OUTERMOST
    /// scope ends, the batched changes are sorted, cursors are updated, and
    /// <see cref="ApplyChanges"/> runs.
    /// </summary>
    /// <returns>The scope to dispose.</returns>
    public IDisposable Writing()
    {
        _writing += 1;
        return new WritingScope(this);
    }

    /// <summary>
    /// Cancels every batched edit — upstream's context-exit-on-exception path.
    /// Call from an exception handler around a writing scope.
    /// </summary>
    public void CancelWriting()
    {
        _writing = 0;
        _changes.Clear();
    }

    private void EndWriting()
    {
        if (_writing == 1)
        {
            if (_changes.Count > 0)
            {
                SortChanges();
                UpdateCursors();
                ApplyChanges();
                _changesList = null;
            }

            _writing = 0;
        }
        else if (_writing > 1)
        {
            _writing -= 1;
        }
    }

    /// <summary>Makes a weak reference to the cursor; called by the Cursor
    /// constructor. The cursor gets updated when the document is changed.</summary>
    /// <param name="cursor">The cursor to track.</param>
    internal void RegisterCursor(Cursor cursor)
    {
        _cursors.Add(new WeakReference<Cursor>(cursor));
    }

    /// <summary>Debugging method that checks for overlapping edits.</summary>
    /// <exception cref="InvalidOperationException">When edits overlap.</exception>
    public void CheckChanges()
    {
        int pos = Size();
        foreach ((int start, int? end, string text) in _changesList)
        {
            if (end > pos)
            {
                string shown = text.Length > 12 ? text.Substring(0, 10) + "..." : text;
                throw new InvalidOperationException(
                    $"overlapping edit: {start}-{end}: {shown}");
            }

            pos = start;
        }
    }

    private void SortChanges()
    {
        // Upstream: starts sorted DESCENDING; per start, entries sorted by
        // (end is None, end) and then REVERSED — so int ends come before None
        // in the sort and after it in the final order.
        _changesList = new List<(int, int?, string)>();
        foreach (int start in _changes.Keys.OrderByDescending(k => k))
        {
            var items = _changes[start]
                .OrderBy(i => i.End.HasValue ? 0 : 1)
                .ThenBy(i => i.End ?? 0)
                .Reverse();
            foreach ((int? end, string text) in items)
            {
                _changesList.Add((start, end, text));
            }
        }

        _changes.Clear();
    }

    private void UpdateCursors()
    {
        List<Cursor> cursors = new List<Cursor>();
        _cursors.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (WeakReference<Cursor> reference in _cursors)
        {
            if (reference.TryGetTarget(out Cursor cursor))
            {
                cursors.Add(cursor);
            }
        }

        foreach ((int start, int? end, string text) in _changesList)
        {
            foreach (Cursor c in cursors)
            {
                if (c.Start > start)
                {
                    if (end == null || end >= c.Start)
                    {
                        c.Start = start;
                    }
                    else
                    {
                        c.Start += start + text.Length - end.Value;
                    }
                }

                if (c.End != null && c.End >= start)
                {
                    if (end == null || end >= c.End)
                    {
                        c.End = start + text.Length;
                    }
                    else
                    {
                        c.End += start + text.Length - end.Value;
                    }
                }
            }
        }
    }

    /// <summary>Applies the batched changes and updates the tokens.</summary>
    protected abstract void ApplyChanges();

    /// <summary>Gets the sorted change list, for <see cref="ApplyChanges"/>
    /// implementations: (start, end, text), ends possibly null (to end of
    /// document), starts descending.</summary>
    protected IReadOnlyList<(int Start, int? End, string Text)> ChangesList
        => _changesList;

    /// <summary>Returns the tuple of tokens of the specified block; the pos and
    /// end of every token point into the BLOCK.</summary>
    /// <param name="block">The block.</param>
    /// <returns>The tokens.</returns>
    public abstract Token[] Tokens(DocumentBlock block);

    /// <summary>Returns the tokens of the block with pos and end pointing into
    /// the DOCUMENT instead of the block.</summary>
    /// <param name="block">The block.</param>
    /// <returns>The re-positioned tokens, same classes.</returns>
    public virtual Token[] TokensWithPosition(DocumentBlock block)
    {
        int pos = Position(block);
        Token[] tokens = Tokens(block);
        Token[] result = new Token[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            result[i] = TokenReposition.At(tokens[i], pos + tokens[i].Pos);
        }

        return result;
    }

    /// <summary>Returns the state at the beginning of the document.</summary>
    /// <returns>The initial state.</returns>
    public abstract Lex.State InitialState();

    /// <summary>Returns the state at the start of the specified block.</summary>
    /// <param name="block">The block.</param>
    /// <returns>The state.</returns>
    public virtual Lex.State State(DocumentBlock block)
    {
        DocumentBlock previous = PreviousBlock(block);
        return IsValid(previous) ? StateEnd(previous) : InitialState();
    }

    /// <summary>Returns the state at the end of the specified block.</summary>
    /// <param name="block">The block.</param>
    /// <returns>The state.</returns>
    public abstract Lex.State StateEnd(DocumentBlock block);

    /// <summary>
    /// Changes a range of text — upstream's slice assignment. When
    /// <paramref name="end"/> is less than <paramref name="start"/> they are
    /// swapped; <see langword="null"/> means to the end of the document.
    /// Carriage returns are stripped from the text. Outside a writing scope the
    /// change applies immediately.
    /// </summary>
    /// <param name="start">The start position.</param>
    /// <param name="end">The end position, or <see langword="null"/>.</param>
    /// <param name="text">The replacement text.</param>
    public void SetText(int start, int? end, string text)
    {
        if (end != null && start > end)
        {
            (start, end) = (end.Value, start);
        }

        text = (text ?? string.Empty).Replace("\r", string.Empty);
        if (text.Length > 0 || start != end)
        {
            if (!_changes.TryGetValue(start, out List<(int?, string)> items))
            {
                _changes[start] = items = new List<(int?, string)>();
            }

            items.Add((end, text));

            if (_writing == 0)
            {
                SortChanges();
                UpdateCursors();
                ApplyChanges();
                _changesList = null;
            }
        }
    }

    /// <summary>Removes a range of text.</summary>
    /// <param name="start">The start position.</param>
    /// <param name="end">The end position, or <see langword="null"/> for the
    /// end of the document.</param>
    public void Delete(int start, int? end) => SetText(start, end, string.Empty);

    private sealed class WritingScope : IDisposable
    {
        private DocumentBase _document;

        public WritingScope(DocumentBase document)
        {
            _document = document;
        }

        public void Dispose()
        {
            _document?.EndWriting();
            _document = null;
        }
    }
}

/// <summary>
/// Re-instantiates a token at a different position, preserving its exact class —
/// upstream's <c>type(t)(t, pos)</c> in <c>tokens_with_position</c>.
/// </summary>
internal static class TokenReposition
{
    private static readonly ConcurrentDictionary<Type, ConstructorInfo> Constructors
        = new ConcurrentDictionary<Type, ConstructorInfo>();

    /// <summary>Makes a copy of a token at a new position.</summary>
    /// <param name="token">The token to copy.</param>
    /// <param name="pos">The new position.</param>
    /// <returns>The copy, of the token's exact class.</returns>
    internal static Token At(Token token, int pos)
    {
        ConstructorInfo constructor = Constructors.GetOrAdd(
            token.GetType(),
            type => type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(int) },
                null));
        return (Token)constructor.Invoke(new object[] { token.Text, pos });
    }
}
