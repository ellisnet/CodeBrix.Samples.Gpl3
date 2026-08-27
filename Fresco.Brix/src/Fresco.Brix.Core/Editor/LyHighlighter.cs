// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using CodeBrix.Platform.UI.AdvancedTextEdit.Highlighting;
using Fresco.Brix.Ly.Lex;
using Fresco.Brix.Ly.Slexing;
using System;
using System.Collections.Generic;
using System.Linq;
using State = Fresco.Brix.Ly.Lex.State;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Editor; //was previously: frescobaldi/highlighter.py and frescobaldi/tokeniter.py (combined);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The LilyPond syntax highlighter AND the per-line token cache in one object —
/// Frescobaldi's highlighter and tokeniter combined, because both are the same
/// computation here: tokenize each line from the frozen tokenizer state its
/// predecessor ended with.
/// <para>
/// The class implements the editor's <see cref="IHighlighter"/> contract with
/// exactly <c>DocumentHighlighter</c>'s invalidation protocol (stored state per
/// line, valid flags, a first-invalid watermark, and the
/// state-changed-at-line-start event), but the "span stack" is python-ly's
/// FROZEN LEXER STATE and the engine is the ported ly.lex tokenizer. Everything
/// that needs tokens — matching, folding, outline, autocomplete, the music
/// tools through the document bridge — reads <see cref="TokensForLine"/> off
/// this one tokenization, the plan's §5.2.
/// </para>
/// </summary>
public sealed class LyHighlighter : ILineTracker, IHighlighter
{
    private readonly TextDocument _document;
    private readonly WeakLineTracker _weakLineTracker;

    // storedStates[0] = state at the beginning of the document;
    // storedStates[i] = state after line i (1-based lines).
    private readonly List<FrozenState> _storedStates = new List<FrozenState>();
    private readonly List<bool> _isValid = new List<bool>();
    private readonly List<Token[]> _storedTokens = new List<Token[]>();

    private ITokenStyler _styler;
    private string _mode;
    private int _firstInvalidLine;
    private bool _isHighlighting;
    private bool _isDisposed;

    /// <summary>Initializes the highlighter over a document.</summary>
    /// <param name="document">The editor document.</param>
    /// <param name="mode">The tokenizer mode, or <see langword="null"/> to
    /// guess from the document text.</param>
    /// <param name="styler">The token-to-color mapping, or <see langword="null"/>
    /// for the built-in default.</param>
    public LyHighlighter(TextDocument document, string mode = null, ITokenStyler styler = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _styler = styler ?? new DefaultTokenStyler();
        _mode = mode;
        _weakLineTracker = WeakLineTracker.Register(document, this);
        InvalidateStates();
    }

    /// <summary>Gets the underlying text document.</summary>
    public IDocument Document => _document;

    /// <summary>Gets the tokenizer mode in force (after guessing).</summary>
    public string Mode => _mode ?? Modes.GuessMode(_document.Text);

    /// <inheritdoc/>
    public event HighlightingStateChangedEventHandler HighlightingStateChanged;

    /// <summary>Gets or sets the token-to-color mapping; setting it forces a
    /// full re-highlight.</summary>
    public ITokenStyler Styler
    {
        get => _styler;
        set
        {
            _styler = value ?? new DefaultTokenStyler();
            InvalidateHighlighting();
        }
    }

    /// <summary>Sets the tokenizer mode (<see langword="null"/> = guess) and
    /// re-highlights.</summary>
    /// <param name="mode">The mode name or <see langword="null"/>.</param>
    public void SetMode(string mode)
    {
        _mode = mode;
        InvalidateHighlighting();
    }

    /// <summary>Invalidates everything and forces a redraw — for mode or
    /// scheme changes.</summary>
    public void InvalidateHighlighting()
    {
        InvalidateStates();
        OnHighlightStateChanged(1, _document.LineCount);
    }

    private void InvalidateStates()
    {
        CheckIsHighlighting();
        _storedStates.Clear();
        _storedStates.Add(CreateInitialState().Freeze());
        _storedTokens.Clear();
        _storedTokens.Add(Array.Empty<Token>());
        _isValid.Clear();
        _isValid.Add(true);
        for (int i = 0; i < _document.LineCount; i++)
        {
            _storedStates.Add(null);
            _storedTokens.Add(null);
            _isValid.Add(false);
        }

        _firstInvalidLine = 1;
    }

    private State CreateInitialState()
        => Modes.CreateState(_mode ?? Modes.GuessMode(_document.Text));

    /// <summary>Disposes the highlighter, deregistering from the document.</summary>
    public void Dispose()
    {
        _weakLineTracker?.Deregister();
        _isDisposed = true;
    }

    void ILineTracker.BeforeRemoveLine(DocumentLine line)
    {
        CheckIsHighlighting();
        int number = line.LineNumber;
        _storedStates.RemoveAt(number);
        _storedTokens.RemoveAt(number);
        _isValid.RemoveAt(number);
        if (number < _isValid.Count)
        {
            _isValid[number] = false;
            if (number < _firstInvalidLine)
            {
                _firstInvalidLine = number;
            }
        }
    }

    void ILineTracker.SetLineLength(DocumentLine line, int newTotalLength)
    {
        CheckIsHighlighting();
        int number = line.LineNumber;
        _isValid[number] = false;
        if (number < _firstInvalidLine)
        {
            _firstInvalidLine = number;
        }
    }

    void ILineTracker.LineInserted(DocumentLine insertionPos, DocumentLine newLine)
    {
        CheckIsHighlighting();
        int lineNumber = newLine.LineNumber;
        _storedStates.Insert(lineNumber, null);
        _storedTokens.Insert(lineNumber, null);
        _isValid.Insert(lineNumber, false);
        if (lineNumber < _firstInvalidLine)
        {
            _firstInvalidLine = lineNumber;
        }
    }

    void ILineTracker.RebuildDocument() => InvalidateStates();

    void ILineTracker.ChangeComplete(DocumentChangeEventArgs e)
    {
    }

    /// <inheritdoc/>
    public HighlightedLine HighlightLine(int lineNumber)
    {
        CheckIsHighlighting();
        _isHighlighting = true;
        try
        {
            HighlightUpTo(lineNumber - 1);
            DocumentLine line = _document.GetLineByNumber(lineNumber);
            Token[] tokens = ScanLine(lineNumber);
            HighlightedLine result = new HighlightedLine(_document, line);
            foreach (Token token in tokens)
            {
                HighlightingColor color = _styler.ColorFor(token);
                if (color != null)
                {
                    result.Sections.Add(new HighlightedSection
                    {
                        Offset = line.Offset + token.Pos,
                        Length = token.Length,
                        Color = color,
                    });
                }
            }

            return result;
        }
        finally
        {
            _isHighlighting = false;
        }
    }

    /// <summary>
    /// Returns the tokens of one line (1-based), block-relative positions —
    /// the tokeniter's cache, computed on demand.
    /// </summary>
    /// <param name="lineNumber">The line number.</param>
    /// <returns>The tokens.</returns>
    public Token[] TokensForLine(int lineNumber)
    {
        if (_firstInvalidLine <= lineNumber || _storedTokens[lineNumber] == null)
        {
            UpdateHighlightingState(lineNumber);
        }

        return _storedTokens[lineNumber] ?? Array.Empty<Token>();
    }

    /// <summary>
    /// Returns a LIVE tokenizer state as it stands at the START of the given
    /// line (1-based) — thawed from the frozen store, safe to mutate.
    /// </summary>
    /// <param name="lineNumber">The line number.</param>
    /// <returns>The state.</returns>
    public State StateAtLineStart(int lineNumber)
    {
        if (_firstInvalidLine <= lineNumber - 1)
        {
            UpdateHighlightingState(lineNumber - 1);
        }

        return State.Thaw(_storedStates[lineNumber - 1]);
    }

    /// <summary>
    /// Returns a LIVE tokenizer state as it stands at the END of the given
    /// line (1-based) — thawed from the frozen store, safe to mutate.
    /// </summary>
    /// <param name="lineNumber">The line number.</param>
    /// <returns>The state.</returns>
    public State StateAtLineEnd(int lineNumber)
    {
        if (_firstInvalidLine <= lineNumber)
        {
            UpdateHighlightingState(lineNumber);
        }

        return State.Thaw(_storedStates[lineNumber]);
    }

    /// <inheritdoc/>
    public IEnumerable<HighlightingColor> GetColorStack(int lineNumber)
        //The per-line tokens already carry the multiline span colors (a block
        //comment's continuation lines are Comment tokens), so the editor needs
        //no extra span context here.
        => Enumerable.Empty<HighlightingColor>();

    /// <inheritdoc/>
    public void UpdateHighlightingState(int lineNumber)
    {
        CheckIsHighlighting();
        _isHighlighting = true;
        try
        {
            HighlightUpTo(lineNumber);
        }
        finally
        {
            _isHighlighting = false;
        }
    }

    private void HighlightUpTo(int targetLineNumber)
    {
        for (int currentLine = 0; currentLine <= targetLineNumber; currentLine++)
        {
            if (_firstInvalidLine > currentLine)
            {
                if (_firstInvalidLine <= targetLineNumber)
                {
                    currentLine = _firstInvalidLine;
                }
                else
                {
                    break;
                }
            }

            ScanLine(currentLine);
        }
    }

    /// <summary>Tokenizes one line from its predecessor's stored state, stores
    /// tokens + end state, and maintains the validity protocol.</summary>
    private Token[] ScanLine(int lineNumber)
    {
        DocumentLine line = _document.GetLineByNumber(lineNumber);
        string text = _document.GetText(line.Offset, line.Length);
        State state = State.Thaw(_storedStates[lineNumber - 1]);
        Token[] tokens = state.Tokens(text).ToArray();
        _storedTokens[lineNumber] = tokens;
        FrozenState frozen = state.Freeze();

        if (!frozen.Equals(_storedStates[lineNumber]))
        {
            _isValid[lineNumber] = true;
            _storedStates[lineNumber] = frozen;
            if (lineNumber + 1 < _isValid.Count)
            {
                _isValid[lineNumber + 1] = false;
                _firstInvalidLine = lineNumber + 1;
            }
            else
            {
                _firstInvalidLine = int.MaxValue;
            }

            if (lineNumber + 1 <= _document.LineCount)
            {
                OnHighlightStateChanged(lineNumber + 1, lineNumber + 1);
            }
        }
        else if (_firstInvalidLine == lineNumber)
        {
            _isValid[lineNumber] = true;
            _firstInvalidLine = _isValid.IndexOf(false);
            if (_firstInvalidLine < 0)
            {
                _firstInvalidLine = int.MaxValue;
            }
        }

        return tokens;
    }

    /// <inheritdoc/>
    public void BeginHighlighting()
    {
    }

    /// <inheritdoc/>
    public void EndHighlighting()
    {
    }

    /// <inheritdoc/>
    public HighlightingColor GetNamedColor(string name) => null;

    /// <inheritdoc/>
    public HighlightingColor DefaultTextColor => null;

    private void CheckIsHighlighting()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(LyHighlighter));
        }

        if (_isHighlighting)
        {
            throw new InvalidOperationException(
                "Invalid call - a highlighting operation is currently running.");
        }
    }

    private void OnHighlightStateChanged(int fromLineNumber, int toLineNumber)
        => HighlightingStateChanged?.Invoke(fromLineNumber, toLineNumber);
}

/// <summary>Maps a token to the color it draws with.</summary>
public interface ITokenStyler
{
    /// <summary>Returns the color for a token, or <see langword="null"/> for
    /// the default text color.</summary>
    /// <param name="token">The token.</param>
    /// <returns>The color or <see langword="null"/>.</returns>
    HighlightingColor ColorFor(Token token);
}
