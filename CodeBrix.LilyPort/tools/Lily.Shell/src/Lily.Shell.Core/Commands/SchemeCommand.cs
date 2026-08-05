// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Shell.Kernel.Commands;
using Lily.Shell.Services;
using System;
using System.Threading.Tasks;

namespace Lily.Shell.Commands;

/// <summary>
/// Enters the LilyScheme REPL with the full engine environment loaded — the
/// port's counterpart of upstream's `lilypond scheme-sandbox`.
/// </summary>
public sealed class SchemeCommand : IShellCommand
{
    private readonly LilyPortHost _host;

    /// <summary>Creates the command over the engine host.</summary>
    public SchemeCommand(LilyPortHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <inheritdoc/>
    public string Name => "scheme";

    /// <inheritdoc/>
    public string Summary => "Enters the LilyScheme REPL (the engine's Scheme sandbox).";

    /// <inheritdoc/>
    public string Usage => "scheme";

    /// <inheritdoc/>
    public async Task ExecuteAsync(ShellCommandContext context)
    {
        await _host.EnsureLoadedAsync(context.CancellationToken).ConfigureAwait(false);
        context.IO.WriteLine("Entering the LilyScheme REPL - 'exit' or Ctrl+D on an empty line returns.");
        context.Session.PushInterpreter(new SchemeReplInterpreter(_host));
    }
}
