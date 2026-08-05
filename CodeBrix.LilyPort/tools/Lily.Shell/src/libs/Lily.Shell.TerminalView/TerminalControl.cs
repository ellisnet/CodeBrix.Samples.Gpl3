// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.TextLayout;
using CodeBrix.Terminal.Engine;
using Lily.Shell.TerminalView.Input;
using Lily.Shell.TerminalView.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using System;
using System.Collections.Generic;
using System.Text;
using Windows.System;
using TerminalBuffer = CodeBrix.Terminal.Engine.Buffer; //Required: 'Buffer' alone is ambiguous with System.Buffer

namespace Lily.Shell.TerminalView;

/// <summary>
/// A terminal view: renders a CodeBrix.Terminal buffer as a fixed monospace
/// cell grid on a Skia surface and turns keyboard input into VT byte
/// sequences. Wire <see cref="InputEmitted"/> to the shell's input and call
/// <see cref="Feed"/> with the shell's output — the control is the screen and
/// keyboard half of a terminal, the way a pty master would see it.
/// </summary>
/// <remarks>
/// Input is a hand-maintained US-QWERTY mapping (see
/// <see cref="KeyboardEncoder"/>) because the Skia heads currently expose no
/// composed-text event; there is no IME path. Text selection and clipboard
/// are not implemented in this first version.
/// </remarks>
public sealed class TerminalControl : SKXamlCanvas
{
    private const string DefaultFontFamily =
        "ms-appx:///CodeBrix.Platform.Fonts.RobotoMono/Fonts/RobotoMono.ttf";

    private readonly Terminal _terminal;
    private readonly SelectionService _selection;
    private readonly DispatcherTimer _blinkTimer;
    private readonly DispatcherTimer _dragScrollTimer;

    private CellMetrics? _metrics;
    private bool _selecting;
    private (int Column, int Row) _lastDragCell = (-1, -1);
    private double _lastPointerX;
    private double _lastPointerY;
    private long _lastClickTick;
    private (int Column, int Row) _lastClickCell = (-1, -1);
    private string _fontFamily = DefaultFontFamily;
    private float _fontSize = 14f;
    private bool _followTail = true;
    private bool _blinkOn = true;
    private bool _focused;
    private bool _shiftDown;
    private bool _controlDown;
    private bool _altDown;
    private bool _capsLock;

    /// <summary>Creates the control with an 80x25 terminal that resizes to fit.</summary>
    public TerminalControl()
    {
        _terminal = new Terminal(new ViewDelegate(this), new TerminalOptions
        {
            Cols = 80,
            Rows = 25,
            //The shell emits explicit CR+LF; double conversion would add blank rows
            ConvertEol = false
        });

        _selection = new SelectionService(_terminal);
        _selection.SelectionChanged += () => Invalidate();

        IgnorePixelScaling = true;
        IsTabStop = true;               //UIElement.IsTabStop - required for key events

        PaintSurface += OnPaintSurface;
        SizeChanged += (_, _) => RecalculateGrid();
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChanged;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;

        _dragScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _dragScrollTimer.Tick += (_, _) => AutoScrollDrag();
        GotFocus += (_, _) => { _focused = true; _blinkOn = true; Invalidate(); };
        LostFocus += (_, _) => { _focused = false; Invalidate(); };

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _blinkTimer.Tick += (_, _) => { _blinkOn = !_blinkOn; Invalidate(); };
        Loaded += (_, _) => _blinkTimer.Start();
        Unloaded += (_, _) => _blinkTimer.Stop();
    }

    /// <summary>Raised with VT-encoded keyboard input (on the UI thread).</summary>
    public event Action<string> InputEmitted;

    /// <summary>Raised when the terminal title changes (OSC 0/2).</summary>
    public event Action<string> TitleChanged;

    /// <summary>
    /// Raised with the selected text when the user copies it (right-click on
    /// the view or Ctrl+Shift+C). The host routes this to the clipboard.
    /// </summary>
    public event Action<string> CopyRequested;

    /// <summary>The translucent overlay painted over selected cells.</summary>
    public SKColor SelectionColor
    {
        get;
        set { field = value; Invalidate(); }
    } = new(0x4d, 0x8b, 0xd8, 0x66);

    /// <summary>The terminal's current column count.</summary>
    public int Columns => _terminal.Cols;

    /// <summary>The terminal's current row count.</summary>
    public int Rows => _terminal.Rows;

    /// <summary>The default text color. Default: the engine's white.</summary>
    public SKColor ForegroundColor
    {
        get;
        set { field = value; Invalidate(); }
    } = new(0xff, 0xff, 0xff);

    /// <summary>The terminal background. Default: the engine's black.</summary>
    public SKColor BackgroundColor
    {
        get;
        set { field = value; Invalidate(); }
    } = new(0x00, 0x00, 0x00);

    /// <summary>
    /// The terminal font family (a font URI or family name understood by
    /// TextLayout). Default: Roboto Mono from the RobotoMono fonts package.
    /// </summary>
    public string TerminalFontFamily
    {
        get => _fontFamily;
        set
        {
            _fontFamily = string.IsNullOrWhiteSpace(value) ? DefaultFontFamily : value;
            _metrics = null;
            RecalculateGrid();
            Invalidate();
        }
    }

    /// <summary>The terminal font size in DIPs. Default 14.</summary>
    public float TerminalFontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = value > 4f ? value : 4f;
            _metrics = null;
            RecalculateGrid();
            Invalidate();
        }
    }

    /// <summary>
    /// Feeds VT output data into the terminal. Safe to call from any thread —
    /// the work is marshalled to the UI thread.
    /// </summary>
    public void Feed(string data)
    {
        if (string.IsNullOrEmpty(data)) { return; }

        var queue = DispatcherQueue;
        if (queue == null) { return; }

        queue.TryEnqueue(() =>
        {
            _terminal.Feed(data);
            if (_followTail) { SnapToTail(); }
            Invalidate();
        });
    }

    /// <summary>Gives the control keyboard focus.</summary>
    public void GrabFocus() => Focus(FocusState.Programmatic);

    private void RaiseInput(string data)
    {
        //Typing snaps the view back to the live tail, like every terminal
        _followTail = true;
        SnapToTail();
        _blinkOn = true;
        InputEmitted?.Invoke(data);
        Invalidate();
    }

    private void SnapToTail()
    {
        var buffer = _terminal.Buffer;
        buffer.YDisp = buffer.YBase;
    }

    private void ScrollViewBy(int lines)
    {
        var buffer = _terminal.Buffer;
        var target = Math.Clamp(buffer.YDisp - lines, 0, buffer.YBase);
        if (target == buffer.YDisp) { return; }

        buffer.YDisp = target;
        _followTail = target == buffer.YBase;
        Invalidate();
    }

    private void RecalculateGrid()
    {
        if (ActualWidth < 1 || ActualHeight < 1) { return; }

        var cell = EnsureMetrics();
        var cols = Math.Max(4, (int)(ActualWidth / cell.Width));
        var rows = Math.Max(2, (int)(ActualHeight / cell.Height));

        if (cols != _terminal.Cols || rows != _terminal.Rows)
        {
            _terminal.Resize(cols, rows);
            if (_followTail) { SnapToTail(); }
        }

        Invalidate();
    }

    private CellMetrics EnsureMetrics() =>
        _metrics ??= CellMetrics.Measure(_fontFamily, _fontSize);

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        var point = e.GetCurrentPoint(this);

        if (point.Properties.IsRightButtonPressed)
        {
            CopySelection();
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            return;
        }

        var cell = SelectionGeometry.ToCell(point.Position.X, point.Position.Y,
            EnsureMetrics(), _terminal.Cols, _terminal.Rows);
        var now = Environment.TickCount64;

        if (now - _lastClickTick < 400 && cell == _lastClickCell)
        {
            //Double-click: word/expression selection. NOTE the engine's
            //  (col, row) parameter order - unlike its (row, col) siblings.
            _selection.SelectWordOrExpression(cell.Column, cell.Row);
            _lastClickTick = 0;
        }
        else
        {
            if (_selection.Active) { _selection.SelectNone(); }
            _selection.SetSoftStart(cell.Row, cell.Column);
            _selecting = true;
            _lastDragCell = cell;
            CapturePointer(e.Pointer);
            _lastClickTick = now;
            _lastClickCell = cell;
        }

        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_selecting) { return; }

        var position = e.GetCurrentPoint(this).Position;
        _lastPointerX = position.X;
        _lastPointerY = position.Y;

        //Dragging beyond the top/bottom edge scrolls the view while held there
        if (position.Y < 0 || position.Y > ActualHeight)
        {
            if (!_dragScrollTimer.IsEnabled) { _dragScrollTimer.Start(); }
        }
        else if (_dragScrollTimer.IsEnabled)
        {
            _dragScrollTimer.Stop();
        }

        ExtendSelectionTo(position.X, position.Y);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_selecting) { return; }

        _selecting = false;
        _dragScrollTimer.Stop();
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void ExtendSelectionTo(double x, double y)
    {
        var cell = SelectionGeometry.ToCell(x, y, EnsureMetrics(),
            _terminal.Cols, _terminal.Rows);
        if (cell == _lastDragCell && _selection.Active) { return; }

        if (!_selection.Active) { _selection.StartSelection(); }
        _selection.DragExtend(cell.Row, cell.Column);
        _lastDragCell = cell;
    }

    private void AutoScrollDrag()
    {
        if (!_selecting)
        {
            _dragScrollTimer.Stop();
            return;
        }

        //Positive lines scroll back toward the scrollback top
        ScrollViewBy(_lastPointerY < 0 ? 1 : -1);
        ExtendSelectionTo(_lastPointerX, _lastPointerY);
    }

    private void CopySelection()
    {
        if (!_selection.Active) { return; }

        var text = _selection.GetSelectedText();
        if (!string.IsNullOrEmpty(text)) { CopyRequested?.Invoke(text); }
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        ScrollViewBy(delta / 120 * 3);
        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e) =>
        UpdateModifier(e.Key, isDown: false);

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (UpdateModifier(e.Key, isDown: true)) { return; }

        //Ctrl+Shift+C copies the selection (never reaches the shell as input)
        if (_controlDown && _shiftDown && e.Key == VirtualKey.C)
        {
            CopySelection();
            e.Handled = true;
            return;
        }

        //Shift+PageUp/PageDown page through the scrollback
        if (_shiftDown && e.Key is VirtualKey.PageUp or VirtualKey.PageDown)
        {
            var page = Math.Max(1, _terminal.Rows - 1);
            ScrollViewBy(e.Key == VirtualKey.PageUp ? page : -page);
            e.Handled = true;
            return;
        }

        var encoded = EncodeKey(e);
        if (encoded != null)
        {
            RaiseInput(encoded);
            e.Handled = true;
        }
    }

    private string EncodeKey(KeyRoutedEventArgs e)
    {
        //Chords go through the VirtualKey mapping (Ctrl+letter -> C0 codes, Alt -> ESC prefix)
        if (_controlDown || _altDown)
        {
            return KeyboardEncoder.Encode(e.Key, _shiftDown, _controlDown, _altDown, _capsLock);
        }

        var special = KeyboardEncoder.EncodeSpecial(e.Key);
        if (special != null) { return special; }

        //Printables: prefer the platform's layout-composed character - the
        //  VirtualKey path cannot see shifted digit-row symbols like '('
        var composed = UnicodeKeyReader.GetUnicodeKey(e);
        if (composed is >= ' ' and not '\x7f') { return composed.ToString(); }

        return KeyboardEncoder.Encode(e.Key, _shiftDown, false, false, _capsLock);
    }

    private bool UpdateModifier(VirtualKey key, bool isDown)
    {
        switch (key)
        {
            case VirtualKey.Shift:
            case VirtualKey.LeftShift:
            case VirtualKey.RightShift:
                _shiftDown = isDown;
                return true;

            case VirtualKey.Control:
            case VirtualKey.LeftControl:
            case VirtualKey.RightControl:
                _controlDown = isDown;
                return true;

            case VirtualKey.Menu:
            case VirtualKey.LeftMenu:
            case VirtualKey.RightMenu:
                _altDown = isDown;
                return true;

            case VirtualKey.CapitalLock:
                if (isDown) { _capsLock = !_capsLock; }
                return true;

            default:
                return false;
        }
    }

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(BackgroundColor);

        var cell = EnsureMetrics();
        var buffer = _terminal.Buffer;

        for (var row = 0; row < _terminal.Rows; row++)
        {
            var lineIndex = buffer.YDisp + row;
            if (lineIndex >= buffer.Lines.Length) { break; }

            DrawLine(canvas, RunBuilder.BuildRuns(buffer.Lines[lineIndex]), row * cell.Height, cell);
        }

        if (_selection.Active) { DrawSelection(canvas, buffer, cell); }

        DrawCursor(canvas, buffer, cell);
    }

    private void DrawSelection(SKCanvas canvas, TerminalBuffer buffer, CellMetrics cell)
    {
        var start = _selection.Start;
        var end = _selection.End;
        using var paint = new SKPaint { Color = SelectionColor };

        for (var row = 0; row < _terminal.Rows; row++)
        {
            if (SelectionGeometry.TryGetRowSpan(start.X, start.Y, end.X, end.Y,
                buffer.YDisp + row, _terminal.Cols, out var first, out var last))
            {
                canvas.DrawRect(first * cell.Width, row * cell.Height,
                    (last - first + 1) * cell.Width, cell.Height, paint);
            }
        }
    }

    private void DrawLine(SKCanvas canvas, List<TextRunSegment> segments,
        float top, CellMetrics cell)
    {
        foreach (var segment in segments)
        {
            var style = AttributeDecoder.Decode(segment.Attribute, ForegroundColor, BackgroundColor);
            var left = segment.StartColumn * cell.Width;
            var width = segment.CellCount * cell.Width;

            if (style.HasVisibleBackground(BackgroundColor))
            {
                using var backPaint = new SKPaint { Color = style.Background };
                canvas.DrawRect(left, top, width, cell.Height, backPaint);
            }

            var isBlank = string.IsNullOrWhiteSpace(segment.Text);
            if (!isBlank)
            {
                var descriptor = new TextRunDescriptor(segment.Text, _fontFamily, _fontSize,
                    style.Bold ? TextFontWeight.Bold : TextFontWeight.Normal,
                    style.Italic ? TextFontStyle.Italic : TextFontStyle.Normal)
                {
                    Color = style.Foreground
                };

                using var layout = TextLayoutEngine.Layout([descriptor]);
                using var textPaint = new SKPaint { Color = style.Foreground, IsAntialias = true };
                layout.Draw(canvas, new SKPoint(left, top), textPaint);
            }

            if (style.Underline || style.CrossedOut)
            {
                using var linePaint = new SKPaint
                {
                    Color = style.Foreground,
                    StrokeWidth = Math.Max(1f, _fontSize / 14f)
                };

                if (style.Underline)
                {
                    var y = top + cell.Baseline + 2f;
                    canvas.DrawLine(left, y, left + width, y, linePaint);
                }

                if (style.CrossedOut)
                {
                    var y = top + cell.Height * 0.5f;
                    canvas.DrawLine(left, y, left + width, y, linePaint);
                }
            }
        }
    }

    private void DrawCursor(SKCanvas canvas, TerminalBuffer buffer, CellMetrics cell)
    {
        if (_terminal.CursorHidden) { return; }

        var screenRow = buffer.YBase + buffer.Y - buffer.YDisp;
        if (screenRow < 0 || screenRow >= _terminal.Rows) { return; }

        var left = buffer.X * cell.Width;
        var top = screenRow * cell.Height;

        if (!_focused)
        {
            //Steady hollow cursor while unfocused
            using var stroke = new SKPaint
            {
                Color = ForegroundColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f
            };
            canvas.DrawRect(left + 0.5f, top + 0.5f, cell.Width - 1f, cell.Height - 1f, stroke);
            return;
        }

        if (!_blinkOn) { return; }

        using var fill = new SKPaint { Color = ForegroundColor };
        canvas.DrawRect(left, top, cell.Width, cell.Height, fill);

        //Repaint the character under the block in the background color
        var lineIndex = buffer.YBase + buffer.Y;
        if (lineIndex < buffer.Lines.Length && buffer.X < buffer.Lines[lineIndex].Length)
        {
            var text = RunBuilder.CellText(buffer.Lines[lineIndex][buffer.X]);
            if (!string.IsNullOrWhiteSpace(text))
            {
                var descriptor = new TextRunDescriptor(text, _fontFamily, _fontSize)
                {
                    Color = BackgroundColor
                };
                using var layout = TextLayoutEngine.Layout([descriptor]);
                using var paint = new SKPaint { Color = BackgroundColor, IsAntialias = true };
                layout.Draw(canvas, new SKPoint(left, top), paint);
            }
        }
    }

    private sealed class ViewDelegate : ITerminalDelegate
    {
        private readonly TerminalControl _owner;

        public ViewDelegate(TerminalControl owner) => _owner = owner;

        public void ShowCursor(Terminal source) => _owner.Invalidate();

        public void SetTerminalTitle(Terminal source, string title) =>
            _owner.TitleChanged?.Invoke(title);

        public void SetTerminalIconTitle(Terminal source, string title)
        {
        }

        public void SizeChanged(Terminal source)
        {
            //Escape-sequence-driven resize is not supported; the grid follows the control size
        }

        public void Send(byte[] data) =>
            _owner.InputEmitted?.Invoke(Encoding.UTF8.GetString(data));

        public string WindowCommand(Terminal source, WindowManipulationCommand command,
            params int[] args) => null;

        public bool IsProcessTrusted() => true;
    }
}
