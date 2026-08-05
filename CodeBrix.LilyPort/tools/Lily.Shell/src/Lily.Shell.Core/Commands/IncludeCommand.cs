// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Shell.Kernel.Commands;
using Lily.Shell.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Lily.Shell.Commands;

/// <summary>
/// Lists or extends the include path applied to parses — the shell-session
/// counterpart of lilypond's --include option.
/// </summary>
public sealed class IncludeCommand : IShellCommand
{
    private readonly LilyPortHost _host;

    /// <summary>Creates the command over the engine host.</summary>
    public IncludeCommand(LilyPortHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <inheritdoc/>
    public string Name => "include";

    /// <inheritdoc/>
    public string Summary => "Lists or adds parser include directories.";

    /// <inheritdoc/>
    public string Usage => "include [<directory>]";

    /// <inheritdoc/>
    public Task ExecuteAsync(ShellCommandContext context)
    {
        if (context.Arguments.Count == 0)
        {
            var directories = _host.IncludeDirectories;
            if (directories.Count == 0)
            {
                context.IO.WriteLine("Include path is empty (each parsed file's folder is added automatically).");
            }
            else
            {
                foreach (var directory in directories) { context.IO.WriteLine("  " + directory); }
            }

            return Task.CompletedTask;
        }

        var path = Path.GetFullPath(context.Arguments[0]);
        if (!Directory.Exists(path))
        {
            context.IO.WriteLine("No such directory: " + path);
            return Task.CompletedTask;
        }

        _host.AddIncludeDirectory(path);
        context.IO.WriteLine("Added " + path);
        return Task.CompletedTask;
    }
}
