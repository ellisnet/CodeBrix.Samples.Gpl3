/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
  Jan Nieuwenhuizen <janneke@gnu.org>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Diagnostics;
using System.Globalization;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/paper-outputter.cc, lily/include/paper-outputter.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Dumps drawing commands to a port, routing each through a callback table.
/// <para>
/// This is the seam between a stencil and a backend written in Scheme: the
/// constructor takes an alist mapping command heads (<c>draw-line</c>,
/// <c>named-glyph</c>, …) to procedures, and <see cref="OutputStencil"/> walks a
/// stencil's expression feeding every command to the matching procedure, writing
/// whatever string the procedure answers to the port. The port's own SVG backend
/// (<c>SvgBackend</c>, in the Backends assembly above this one) does not come
/// through here — it consumes stencils directly — but everything upstream's
/// <c>scm/framework-*.scm</c> does goes through exactly this surface, so the
/// surface is what gets ported.
/// </para>
/// </summary>
public sealed class PaperOutputter : IStencilSink
{
    private readonly SchemeHashTable _callbackTable;
    private readonly object _defaultCallback;
    private readonly object _file;
    private readonly Stopwatch _timer = Stopwatch.StartNew();

    /// <summary>Initializes an outputter dumping to a port.</summary>
    /// <param name="port">The port written to.</param>
    /// <param name="alist">An alist mapping command-head symbols to procedures.</param>
    /// <param name="defaultCallback">
    /// The procedure called for expressions the alist does not cover, or any
    /// non-procedure for none.
    /// </param>
    public PaperOutputter(object port, object alist, object defaultCallback)
    {
        _file = port;

        // Upstream: Hash_table::alist_to_hashq_table. Symbols are interned, so the
        // default reference-equality table IS hashq.
        _callbackTable = new SchemeHashTable(null);
        foreach (object entry in Pair.ToList(alist))
        {
            if (entry is Pair pair)
            {
                _callbackTable.Set(pair.Car, pair.Cdr);
            }
        }

        _defaultCallback = Objects.SchemeUtilities.IsProcedure(defaultCallback)
            ? defaultCallback
            : (object)false;
    }

    /// <summary>Gets the port this outputter dumps to.</summary>
    public object File => _file;

    /// <summary>Writes a value to the port the way <c>display</c> would.</summary>
    /// <param name="value">The value to write.</param>
    /// <returns>The unspecified value, as upstream's <c>scm_display</c> answers.</returns>
    public object DumpString(object value)
    {
        Write(Printer.Display(value));
        return Unspecified.Instance;
    }

    /// <summary>
    /// Routes one drawing command through the callback table, writing a string or
    /// bytevector result to the port.
    /// </summary>
    /// <param name="expression">The command, a list whose head names the callback.</param>
    /// <returns>What the callback answered.</returns>
    public object OutputScheme(object expression)
    {
        if (!(expression is Pair pair))
        {
            return false;
        }

        object result = false;
        Pair handle = _callbackTable.GetHandle(pair.Car);
        if (handle != null && Objects.SchemeUtilities.IsProcedure(handle.Cdr))
        {
            result = Objects.SchemeUtilities.CallCallback(
                handle.Cdr, Pair.ToList(pair.Cdr).ToArray());
        }
        else if (!(_defaultCallback is bool))
        {
            result = Objects.SchemeUtilities.CallCallback(_defaultCallback, expression);
        }

        if (result is MutableString || result is string)
        {
            Write(StringPrimitives.Text(result, "paper-outputter"));
        }
        else if (result is byte[] bytes)
        {
            // Upstream writes the bytevector to the port byte-for-byte. The port's
            // ports are text writers, so the bytes travel as Latin-1, which is the
            // identity on the byte values.
            Write(System.Text.Encoding.Latin1.GetString(bytes));
        }

        return result;
    }

    /// <summary>Unfolds a stencil into drawing commands and outputs each one.</summary>
    /// <param name="stencil">The stencil to dump.</param>
    public void OutputStencil(Stencil stencil)
        => StencilInterpreter.Interpret(stencil.Expression, this, Offset.Zero);

    /// <summary>Closes the port, reporting the elapsed time at debug level.</summary>
    public void Close()
    {
        if (_file is SchemeOutputPort port)
        {
            port.Writer.Flush();
        }

        Warn.Debug(string.Format(
            CultureInfo.InvariantCulture,
            "Paper_outputter elapsed time: {0:0.00} seconds",
            _timer.Elapsed.TotalSeconds));
    }

    /// <summary>Feeds the stencil interpreter's commands to the callback table.</summary>
    /// <param name="expression">One drawing command.</param>
    /// <returns>What the callback answered.</returns>
    object IStencilSink.Output(object expression) => OutputScheme(expression);

    private void Write(string text)
    {
        if (_file is SchemeOutputPort port)
        {
            port.Writer.Write(text);
        }
    }
}
