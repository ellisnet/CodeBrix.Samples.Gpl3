// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Lily.Shell.Kernel.IO;

/// <summary>
/// The output surface shell commands write to. Text is VT data destined for
/// the terminal view; line endings are emitted as CR+LF explicitly, so the
/// consuming terminal should NOT also convert bare LF (set its ConvertEol
/// option off).
/// </summary>
public interface IShellIO
{
    /// <summary>Writes text without a line ending.</summary>
    void Write(string text);

    /// <summary>Writes text followed by CR+LF.</summary>
    void WriteLine(string text = "");
}
