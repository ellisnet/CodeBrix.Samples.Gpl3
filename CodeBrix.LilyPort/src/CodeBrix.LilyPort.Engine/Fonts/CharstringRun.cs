// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Fonts;

/// <summary>
/// Receives a glyph's outline as it is drawn, in the font's own design units.
/// <para>
/// Coordinates are ABSOLUTE — the charstring's deltas are already accumulated — because
/// every consumer wants points rather than pen movements.
/// </para>
/// </summary>
internal interface IGlyphPathSink
{
    /// <summary>Starts a new contour.</summary>
    /// <param name="x">The starting x.</param>
    /// <param name="y">The starting y.</param>
    void MoveTo(double x, double y);

    /// <summary>Draws a straight line to a point.</summary>
    /// <param name="x">The end x.</param>
    /// <param name="y">The end y.</param>
    void LineTo(double x, double y);

    /// <summary>Draws a cubic Bézier to a point.</summary>
    /// <param name="x1">The first control point's x.</param>
    /// <param name="y1">The first control point's y.</param>
    /// <param name="x2">The second control point's x.</param>
    /// <param name="y2">The second control point's y.</param>
    /// <param name="x3">The end x.</param>
    /// <param name="y3">The end y.</param>
    void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3);

    /// <summary>Closes the current contour.</summary>
    void ClosePath();
}

/// <summary>
/// One execution of one Type 2 charstring: the little stack machine that draws a glyph.
/// <para>
/// New-in-family, written from the Type 2 Charstring Format specification. It exists to
/// answer one question the font does not record — how tall and wide a glyph's INK
/// actually is — and it can also record the outline, which is what makes it testable
/// against the shipped SVG fonts.
/// </para>
/// <para>
/// Two details decide whether an implementation draws the right shape or a plausible
/// wrong one, and both are easy to skip:
/// </para>
/// <list type="number">
/// <item>The leading WIDTH argument. Several operators may be preceded by one extra
/// odd argument giving the glyph's advance width relative to the font's default. It is
/// present or absent depending on the operand count being odd or even, and a run that
/// does not drop it shifts the entire glyph by the width.</item>
/// <item><c>hintmask</c> and <c>cntrmask</c> are followed by INLINE MASK BYTES, one per
/// eight stem hints declared so far — and they also implicitly declare vertical stems
/// from any operands still on the stack. Counting stems wrong misreads mask bytes as
/// operators, which does not fail: it draws something.</item>
/// </list>
/// </summary>
internal sealed class CharstringRun
{
    private const int MaxDepth = 10;

    private readonly CffFont _font;
    private readonly int _glyph;
    private readonly double[] _stack = new double[48];
    private readonly double[] _transient = new double[32];
    private readonly StringBuilder _path = new StringBuilder();

    private int _count;
    private int _stems;
    private bool _widthParsed;
    private double _x;
    private double _y;
    private bool _open;
    private bool _haveBounds;
    private double _left;
    private double _right;
    private double _bottom;
    private double _top;

    internal CharstringRun(CffFont font, int glyph)
    {
        _font = font;
        _glyph = glyph;
    }

    /// <summary>Gets or sets whether the outline is recorded as SVG path data.</summary>
    internal bool Recording { get; set; }

    /// <summary>
    /// Gets or sets a sink the outline is reported to as it is drawn, or
    /// <see langword="null"/> for none. Independent of <see cref="Recording"/>: a
    /// caller that wants segments rather than a string sets this instead.
    /// </summary>
    internal IGlyphPathSink Sink { get; set; }

    /// <summary>Gets the glyph's advance width, when the charstring gave one.</summary>
    internal double Width { get; private set; }

    /// <summary>Gets the recorded outline as SVG path data.</summary>
    internal string Path => _path.ToString();

    /// <summary>Gets the ink bounding box, in charstring units.</summary>
    internal Box Bounds
        => _haveBounds
            ? new Box(new Interval(_left, _right), new Interval(_bottom, _top))
            : default;

    /// <summary>Runs the glyph's program.</summary>
    internal void Execute()
    {
        (int Start, int End) charstring = _font.Charstring(_glyph);
        Run(charstring.Start, charstring.End, 0);
        Close();
    }

    private void Run(int start, int end, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        byte[] data = _font.Data;
        int position = start;

        while (position < end && position < data.Length)
        {
            int b0 = data[position];

            if (b0 >= 32 || b0 == 28)
            {
                position = PushOperand(data, position);
                continue;
            }

            position++;

            switch (b0)
            {
                case 1: // hstem
                case 3: // vstem
                case 18: // hstemhm
                case 23: // vstemhm
                    CountStems();
                    break;

                case 19: // hintmask
                case 20: // cntrmask
                    // Operands still on the stack are an implicit vstemhm.
                    CountStems();
                    position += (_stems + 7) / 8;
                    break;

                case 21: // rmoveto
                    TakeWidth(2);
                    MoveTo(Argument(0), Argument(1));
                    _count = 0;
                    break;

                case 22: // hmoveto
                    TakeWidth(1);
                    MoveTo(Argument(0), 0);
                    _count = 0;
                    break;

                case 4: // vmoveto
                    TakeWidth(1);
                    MoveTo(0, Argument(0));
                    _count = 0;
                    break;

                case 5: // rlineto
                    for (int i = 0; i + 1 < _count; i += 2)
                    {
                        LineTo(_stack[i], _stack[i + 1]);
                    }

                    _count = 0;
                    break;

                case 6: // hlineto
                case 7: // vlineto
                    AlternatingLines(b0 == 6);
                    _count = 0;
                    break;

                case 8: // rrcurveto
                    for (int i = 0; i + 5 < _count; i += 6)
                    {
                        CurveTo(_stack[i], _stack[i + 1], _stack[i + 2],
                                _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                    }

                    _count = 0;
                    break;

                case 24: // rcurveline
                    {
                        int i = 0;
                        for (; i + 5 < _count - 2; i += 6)
                        {
                            CurveTo(_stack[i], _stack[i + 1], _stack[i + 2],
                                    _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                        }

                        if (i + 1 < _count)
                        {
                            LineTo(_stack[i], _stack[i + 1]);
                        }

                        _count = 0;
                    }

                    break;

                case 25: // rlinecurve
                    {
                        int i = 0;
                        for (; i + 1 < _count - 6; i += 2)
                        {
                            LineTo(_stack[i], _stack[i + 1]);
                        }

                        if (i + 5 < _count)
                        {
                            CurveTo(_stack[i], _stack[i + 1], _stack[i + 2],
                                    _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                        }

                        _count = 0;
                    }

                    break;

                case 26: // vvcurveto
                case 27: // hhcurveto
                    AxisCurves(b0 == 27);
                    _count = 0;
                    break;

                case 30: // vhcurveto
                case 31: // hvcurveto
                    AlternatingCurves(b0 == 31);
                    _count = 0;
                    break;

                case 10: // callsubr
                    CallSubroutine(_font.LocalSubrsFor(_glyph), depth);
                    break;

                case 29: // callgsubr
                    CallSubroutine(_font.GlobalSubrs, depth);
                    break;

                case 11: // return
                    return;

                case 14: // endchar
                    TakeWidth(0);
                    Close();
                    return;

                case 12:
                    position = Escape(data, position);
                    break;

                default:
                    _count = 0;
                    break;
            }
        }
    }

    private int Escape(byte[] data, int position)
    {
        int b1 = data[position++];

        switch (b1)
        {
            case 35: // flex
                if (_count >= 13)
                {
                    CurveTo(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                    CurveTo(_stack[6], _stack[7], _stack[8], _stack[9], _stack[10], _stack[11]);
                }

                break;

            case 34: // hflex
                if (_count >= 7)
                {
                    double startY = _y;
                    CurveTo(_stack[0], 0, _stack[1], _stack[2], _stack[3], 0);
                    CurveTo(_stack[4], 0, _stack[5], startY - (_y + _stack[2]), _stack[6], 0);

                    // The second curve must land back on the starting Y. Computing the
                    // final dy from the accumulated position rather than restating it is
                    // what keeps that exact.
                    _y = startY;
                }

                break;

            case 36: // hflex1
                if (_count >= 9)
                {
                    double startY = _y;
                    CurveTo(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], 0);
                    CurveTo(_stack[5], 0, _stack[6], _stack[7], _stack[8], startY - _y
                        - _stack[7]);
                    _y = startY;
                }

                break;

            case 37: // flex1
                if (_count >= 11)
                {
                    double startX = _x;
                    double startY = _y;
                    double dx = 0;
                    double dy = 0;
                    for (int i = 0; i < 10; i += 2)
                    {
                        dx += _stack[i];
                        dy += _stack[i + 1];
                    }

                    CurveTo(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                    CurveTo(_stack[6], _stack[7], _stack[8], _stack[9],
                            startX + dx + _stack[10] - _x,
                            startY + dy - _y);

                    // flex1's last point is given by ONE delta; the other is whatever
                    // returns the pen to the start of the flex in that axis.
                }

                break;

            case 3: // and
            case 4: // or
            case 5: // not
            case 9: // abs
            case 10: // add
            case 11: // sub
            case 12: // div
            case 14: // neg
            case 15: // eq
            case 18: // drop
            case 20: // put
            case 21: // get
            case 22: // ifelse
            case 23: // random
            case 24: // mul
            case 26: // sqrt
            case 27: // dup
            case 28: // exch
            case 29: // index
            case 30: // roll
                Arithmetic(b1);
                return position;

            default:
                break;
        }

        _count = 0;
        return position;
    }

    /// <summary>
    /// The arithmetic operators. No vendored face uses one, but a charstring that did
    /// and found them missing would leave the stack wrong and draw a wrong shape rather
    /// than fail, so the stack effects are honoured even where the value is not.
    /// </summary>
    /// <param name="op">The escaped operator number.</param>
    private void Arithmetic(int op)
    {
        switch (op)
        {
            case 18: // drop
                if (_count > 0)
                {
                    _count--;
                }

                break;

            case 10: // add
                Binary((a, b) => a + b);
                break;

            case 11: // sub
                Binary((a, b) => a - b);
                break;

            case 12: // div
                Binary((a, b) => b == 0 ? 0 : a / b);
                break;

            case 24: // mul
                Binary((a, b) => a * b);
                break;

            case 9: // abs
                Unary(Math.Abs);
                break;

            case 14: // neg
                Unary(value => -value);
                break;

            case 26: // sqrt
                Unary(value => Math.Sqrt(Math.Abs(value)));
                break;

            case 27: // dup
                if (_count > 0 && _count < _stack.Length)
                {
                    _stack[_count] = _stack[_count - 1];
                    _count++;
                }

                break;

            case 28: // exch
                if (_count >= 2)
                {
                    (_stack[_count - 1], _stack[_count - 2]) = (_stack[_count - 2], _stack[_count - 1]);
                }

                break;

            case 20: // put
                if (_count >= 2)
                {
                    int slot = (int)_stack[_count - 1];
                    if (slot >= 0 && slot < _transient.Length)
                    {
                        _transient[slot] = _stack[_count - 2];
                    }

                    _count -= 2;
                }

                break;

            case 21: // get
                if (_count >= 1)
                {
                    int slot = (int)_stack[_count - 1];
                    _stack[_count - 1] = slot >= 0 && slot < _transient.Length
                        ? _transient[slot]
                        : 0;
                }

                break;

            default:
                _count = 0;
                break;
        }
    }

    private void Binary(Func<double, double, double> apply)
    {
        if (_count >= 2)
        {
            _stack[_count - 2] = apply(_stack[_count - 2], _stack[_count - 1]);
            _count--;
        }
    }

    private void Unary(Func<double, double> apply)
    {
        if (_count >= 1)
        {
            _stack[_count - 1] = apply(_stack[_count - 1]);
        }
    }

    private void CallSubroutine(List<(int Start, int End)> subrs, int depth)
    {
        if (_count == 0 || subrs == null || subrs.Count == 0)
        {
            return;
        }

        int number = (int)_stack[--_count] + CffFont.Bias(subrs.Count);
        if (number >= 0 && number < subrs.Count)
        {
            Run(subrs[number].Start, subrs[number].End, depth + 1);
        }
    }

    private int PushOperand(byte[] data, int position)
    {
        int b0 = data[position];

        if (b0 == 28)
        {
            Push((short)((data[position + 1] << 8) | data[position + 2]));
            return position + 3;
        }

        if (b0 <= 246)
        {
            Push(b0 - 139);
            return position + 1;
        }

        if (b0 <= 250)
        {
            Push(((b0 - 247) * 256) + data[position + 1] + 108);
            return position + 2;
        }

        if (b0 <= 254)
        {
            Push((-(b0 - 251) * 256) - data[position + 1] - 108);
            return position + 2;
        }

        // 255: a 16.16 fixed-point number.
        int fixedValue = (data[position + 1] << 24)
                         | (data[position + 2] << 16)
                         | (data[position + 3] << 8)
                         | data[position + 4];
        Push(fixedValue / 65536.0);
        return position + 5;
    }

    private void Push(double value)
    {
        if (_count < _stack.Length)
        {
            _stack[_count++] = value;
        }
    }

    private double Argument(int index) => index < _count ? _stack[index] : 0.0;

    private void CountStems()
    {
        // An odd operand count on the FIRST stem operator means the extra leading
        // argument is the width.
        if (!_widthParsed && (_count % 2) == 1)
        {
            Width = _stack[0];
        }

        _widthParsed = true;
        _stems += _count / 2;
        _count = 0;
    }

    /// <summary>
    /// Drops the optional leading width argument.
    /// </summary>
    /// <param name="expected">
    /// How many operands the operator itself takes; more than that means the first one
    /// is a width. <c>endchar</c> passes 0, and also accepts the four-argument seac
    /// form, which is why anything above the expected count is treated the same way.
    /// </param>
    private void TakeWidth(int expected)
    {
        if (!_widthParsed)
        {
            _widthParsed = true;
            if (_count > expected && _count != 4)
            {
                Width = _stack[0];
                Array.Copy(_stack, 1, _stack, 0, _count - 1);
                _count--;
            }
        }
    }

    private void AlternatingLines(bool horizontal)
    {
        for (int i = 0; i < _count; i++)
        {
            if (horizontal)
            {
                LineTo(_stack[i], 0);
            }
            else
            {
                LineTo(0, _stack[i]);
            }

            horizontal = !horizontal;
        }
    }

    private void AxisCurves(bool horizontal)
    {
        int i = 0;
        double first = 0;

        // An odd leading argument is a one-off displacement on the OTHER axis, applied
        // to the first curve only.
        if ((_count % 4) == 1)
        {
            first = _stack[0];
            i = 1;
        }

        for (; i + 3 < _count; i += 4)
        {
            if (horizontal)
            {
                CurveTo(_stack[i], first, _stack[i + 1], _stack[i + 2], _stack[i + 3], 0);
            }
            else
            {
                CurveTo(first, _stack[i], _stack[i + 1], _stack[i + 2], 0, _stack[i + 3]);
            }

            first = 0;
        }
    }

    private void AlternatingCurves(bool horizontal)
    {
        int i = 0;

        while (i + 3 < _count)
        {
            // The very last curve may carry a fifth argument: the displacement on the
            // axis the pattern would otherwise leave at zero.
            bool last = _count - i == 5;
            double extra = last ? _stack[i + 4] : 0.0;

            if (horizontal)
            {
                CurveTo(_stack[i], 0, _stack[i + 1], _stack[i + 2], extra, _stack[i + 3]);
            }
            else
            {
                CurveTo(0, _stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], extra);
            }

            horizontal = !horizontal;
            i += 4;
        }
    }

    private void MoveTo(double dx, double dy)
    {
        Close();
        _x += dx;
        _y += dy;
        Include(_x, _y);
        _open = true;
        Sink?.MoveTo(_x, _y);

        if (Recording)
        {
            _path.Append('M');
            Append(_x);
            _path.Append(' ');
            Append(_y);
        }
    }

    private void LineTo(double dx, double dy)
    {
        _x += dx;
        _y += dy;
        Include(_x, _y);
        Sink?.LineTo(_x, _y);

        if (Recording)
        {
            _path.Append('L');
            Append(_x);
            _path.Append(' ');
            Append(_y);
        }
    }

    private void CurveTo(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
    {
        double x1 = _x + dx1;
        double y1 = _y + dy1;
        double x2 = x1 + dx2;
        double y2 = y1 + dy2;
        _x = x2 + dx3;
        _y = y2 + dy3;

        // The control points are included in the bounds. A true ink extent would solve
        // the cubic for its own extrema; the control hull is a superset, never a
        // subset, so a text line is never reported SHORTER than it draws — and that is
        // the direction that matters, since the engraver reserves room from this.
        Include(x1, y1);
        Include(x2, y2);
        Include(_x, _y);
        Sink?.CurveTo(x1, y1, x2, y2, _x, _y);

        if (Recording)
        {
            _path.Append('C');
            Append(x1);
            _path.Append(' ');
            Append(y1);
            _path.Append(' ');
            Append(x2);
            _path.Append(' ');
            Append(y2);
            _path.Append(' ');
            Append(_x);
            _path.Append(' ');
            Append(_y);
        }
    }

    private void Close()
    {
        if (_open)
        {
            if (Recording)
            {
                _path.Append('z');
            }

            Sink?.ClosePath();
        }

        _open = false;
    }

    private void Include(double x, double y)
    {
        if (!_haveBounds)
        {
            _haveBounds = true;
            _left = _right = x;
            _bottom = _top = y;
            return;
        }

        _left = Math.Min(_left, x);
        _right = Math.Max(_right, x);
        _bottom = Math.Min(_bottom, y);
        _top = Math.Max(_top, y);
    }

    private void Append(double value)
        => _path.Append(value.ToString("0.####", CultureInfo.InvariantCulture));
}
