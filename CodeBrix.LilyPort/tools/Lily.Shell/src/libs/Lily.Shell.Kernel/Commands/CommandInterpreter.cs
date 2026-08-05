// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lily.Shell.Kernel.Commands;

/// <summary>
/// The session's default line interpreter: tokenizes the line, looks the
/// first token up in the <see cref="CommandRegistry"/>, and executes it.
/// </summary>
public sealed class CommandInterpreter : ILineInterpreter
{
    private readonly CommandRegistry _registry;

    /// <summary>Creates the interpreter over a registry, with the prompt it shows.</summary>
    public CommandInterpreter(CommandRegistry registry, string prompt)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Prompt = prompt ?? string.Empty;
    }

    /// <inheritdoc/>
    public string Prompt { get; }

    /// <inheritdoc/>
    public async Task HandleLineAsync(ShellSession session, string line,
        CancellationToken cancellationToken)
    {
        var tokens = CommandLineTokenizer.Tokenize(line);
        if (tokens.Count == 0) { return; }

        if (!_registry.TryGet(tokens[0], out var command))
        {
            session.Output.WriteLine($"Unknown command: {tokens[0]}  (try 'help')");
            return;
        }

        var arguments = tokens.GetRange(1, tokens.Count - 1);
        var context = new ShellCommandContext(session, arguments, cancellationToken);
        await command.ExecuteAsync(context).ConfigureAwait(false);
    }
}
