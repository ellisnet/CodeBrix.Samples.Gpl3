// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Threading.Tasks;

namespace Lily.Shell.Kernel.Commands;

/// <summary>
/// Clears the terminal screen and homes the cursor.
/// </summary>
public sealed class ClearCommand : IShellCommand
{
    /// <inheritdoc/>
    public string Name => "clear";

    /// <inheritdoc/>
    public string Summary => "Clears the terminal screen.";

    /// <inheritdoc/>
    public string Usage => "clear";

    /// <inheritdoc/>
    public Task ExecuteAsync(ShellCommandContext context)
    {
        context.IO.Write("\x1b[2J\x1b[H");
        return Task.CompletedTask;
    }
}
