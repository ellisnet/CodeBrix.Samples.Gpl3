// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;

namespace Lily.Shell.Kernel.IO;

/// <summary>
/// An <see cref="IShellIO"/> that forwards everything to a single sink
/// delegate. Used by <see cref="ShellSession"/> to route command output to
/// whatever terminal view is attached.
/// </summary>
public sealed class DelegateShellIO : IShellIO
{
    private readonly Action<string> _sink;

    /// <summary>Creates the IO wrapper around the given sink.</summary>
    public DelegateShellIO(Action<string> sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    /// <inheritdoc/>
    public void Write(string text)
    {
        if (!string.IsNullOrEmpty(text)) { _sink(text); }
    }

    /// <inheritdoc/>
    public void WriteLine(string text = "")
    {
        _sink((text ?? string.Empty) + "\r\n");
    }
}
