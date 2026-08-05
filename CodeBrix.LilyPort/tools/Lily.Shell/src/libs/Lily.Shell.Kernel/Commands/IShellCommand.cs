// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Threading.Tasks;

namespace Lily.Shell.Kernel.Commands;

/// <summary>
/// One shell command ("engrave", "scheme", "help", …). Implementations are
/// registered with a <see cref="CommandRegistry"/> and dispatched by the
/// session's command interpreter.
/// </summary>
public interface IShellCommand
{
    /// <summary>The name the user types (lower-case by convention).</summary>
    string Name { get; }

    /// <summary>A one-line description shown by 'help'.</summary>
    string Summary { get; }

    /// <summary>The usage line shown by 'help &lt;name&gt;' (e.g. "engrave &lt;file.ly&gt; [-o &lt;out.svg&gt;]").</summary>
    string Usage { get; }

    /// <summary>Executes the command.</summary>
    Task ExecuteAsync(ShellCommandContext context);
}
