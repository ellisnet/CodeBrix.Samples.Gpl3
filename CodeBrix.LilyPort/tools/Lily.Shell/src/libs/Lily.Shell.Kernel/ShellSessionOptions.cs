// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Lily.Shell.Kernel;

/// <summary>
/// Configuration for a <see cref="ShellSession"/>.
/// </summary>
public sealed class ShellSessionOptions
{
    /// <summary>The root prompt. Default "lily&gt; ".</summary>
    public string Prompt { get; set; } = "lily> ";

    /// <summary>
    /// Banner lines written by <see cref="ShellSession.Start"/> before the
    /// first prompt. Null or empty for no banner.
    /// </summary>
    public string[] Banner { get; set; }
}
