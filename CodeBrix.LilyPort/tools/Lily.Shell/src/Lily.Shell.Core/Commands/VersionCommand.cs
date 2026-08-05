// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort;
using Lily.Shell.Kernel.Commands;
using Lily.Shell.Services;
using System;
using System.Threading.Tasks;

namespace Lily.Shell.Commands;

/// <summary>
/// Reports the ported LilyPond version, the upstream pin, and the engine state.
/// </summary>
public sealed class VersionCommand : IShellCommand
{
    private readonly LilyPortHost _host;

    /// <summary>Creates the command over the engine host.</summary>
    public VersionCommand(LilyPortHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <inheritdoc/>
    public string Name => "version";

    /// <inheritdoc/>
    public string Summary => "Shows the ported LilyPond version and engine state.";

    /// <inheritdoc/>
    public string Usage => "version";

    /// <inheritdoc/>
    public Task ExecuteAsync(ShellCommandContext context)
    {
        var assemblyVersion = typeof(LilyPortEngraver).Assembly.GetName().Version;

        context.IO.WriteLine($"CodeBrix.LilyPort - a port of GNU LilyPond {LilyPortInfo.UpstreamVersion}");
        context.IO.WriteLine($"  upstream: {LilyPortInfo.UpstreamUrl} @ {LilyPortInfo.UpstreamCommit[..10]}");
        context.IO.WriteLine($"  engine assembly: {assemblyVersion}");
        context.IO.WriteLine($"  scheme layer: {(_host.IsReady ? "loaded" : "loading...")}");
        return Task.CompletedTask;
    }
}
