// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Linq;
using System.Threading.Tasks;

namespace Lily.Shell.Kernel.Commands;

/// <summary>
/// Lists the registered commands, or shows one command's usage when invoked
/// as 'help &lt;command&gt;'.
/// </summary>
public sealed class HelpCommand : IShellCommand
{
    private readonly CommandRegistry _registry;

    /// <summary>Creates the command over the registry it describes.</summary>
    public HelpCommand(CommandRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <inheritdoc/>
    public string Name => "help";

    /// <inheritdoc/>
    public string Summary => "Lists commands, or shows usage for one command.";

    /// <inheritdoc/>
    public string Usage => "help [<command>]";

    /// <inheritdoc/>
    public Task ExecuteAsync(ShellCommandContext context)
    {
        if (context.Arguments.Count > 0)
        {
            var name = context.Arguments[0];
            if (_registry.TryGet(name, out var command))
            {
                context.IO.WriteLine(command.Name + " - " + command.Summary);
                context.IO.WriteLine("usage: " + command.Usage);
            }
            else
            {
                context.IO.WriteLine($"Unknown command: {name}");
            }

            return Task.CompletedTask;
        }

        var all = _registry.All;
        var width = all.Max(c => c.Name.Length) + 2;

        context.IO.WriteLine("Available commands:");
        context.IO.WriteLine();
        foreach (var command in all)
        {
            context.IO.WriteLine("  " + command.Name.PadRight(width) + command.Summary);
        }

        context.IO.WriteLine();
        context.IO.WriteLine("Type 'help <command>' for usage.");
        return Task.CompletedTask;
    }
}
