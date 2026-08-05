// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Lily.Shell.Kernel.Commands;

/// <summary>
/// The set of commands a session dispatches. Lookup is case-insensitive;
/// registration of a duplicate name replaces the earlier command.
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, IShellCommand> _commands =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a command under its <see cref="IShellCommand.Name"/>.</summary>
    public void Register(IShellCommand command)
    {
        if (command == null) { throw new ArgumentNullException(nameof(command)); }
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            throw new ArgumentException("Command has no name.", nameof(command));
        }

        _commands[command.Name] = command;
    }

    /// <summary>Looks a command up by name.</summary>
    public bool TryGet(string name, out IShellCommand command) =>
        _commands.TryGetValue(name ?? string.Empty, out command);

    /// <summary>All registered commands, sorted by name.</summary>
    public IReadOnlyList<IShellCommand> All =>
        _commands.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
}
