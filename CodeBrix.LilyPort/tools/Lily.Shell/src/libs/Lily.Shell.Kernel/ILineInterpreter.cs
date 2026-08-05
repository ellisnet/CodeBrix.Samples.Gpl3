// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Threading;
using System.Threading.Tasks;

namespace Lily.Shell.Kernel;

/// <summary>
/// Handles submitted lines for a session mode. The default interpreter
/// dispatches shell commands; sub-modes (a Scheme REPL, for example) push
/// their own interpreter with <see cref="ShellSession.PushInterpreter"/> and
/// pop it to leave the mode. Ctrl+D on an empty line pops automatically.
/// </summary>
public interface ILineInterpreter
{
    /// <summary>The prompt shown while this interpreter is active (e.g. "lily&gt; ").</summary>
    string Prompt { get; }

    /// <summary>
    /// Handles one submitted line. Runs on a worker thread; write results via
    /// session.Output. The token is signalled when the user presses Ctrl+C.
    /// </summary>
    Task HandleLineAsync(ShellSession session, string line, CancellationToken cancellationToken);
}
