// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Shell.Kernel.IO;
using System.Text;

namespace Lily.Shell.Services;

/// <summary>
/// Adapts an <see cref="IShellIO"/> as a TextWriter so the Scheme
/// interpreter's display/format output lands in the terminal. Lines are
/// buffered until a newline; call <see cref="Flush"/> after an evaluation to
/// surface a trailing partial line.
/// </summary>
internal sealed class ShellIOTextWriter : System.IO.TextWriter
{
    private readonly IShellIO _io;
    private readonly StringBuilder _line = new();

    public ShellIOTextWriter(IShellIO io) => _io = io;

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        if (value == '\n')
        {
            var text = _line.ToString();
            _line.Clear();
            _io.WriteLine(text);
        }
        else if (value != '\r')
        {
            _line.Append(value);
        }
    }

    public override void Write(string value)
    {
        if (string.IsNullOrEmpty(value)) { return; }
        foreach (var c in value) { Write(c); }
    }

    public override void Flush()
    {
        if (_line.Length > 0)
        {
            _io.Write(_line.ToString());
            _line.Clear();
        }
    }
}
