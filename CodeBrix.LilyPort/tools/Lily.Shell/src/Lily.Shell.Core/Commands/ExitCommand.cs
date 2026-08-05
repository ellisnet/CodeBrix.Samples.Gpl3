// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Shell.Kernel.Commands;
using System;
using System.Threading.Tasks;

namespace Lily.Shell.Commands;

/// <summary>
/// Closes the application. The actual shutdown is a delegate supplied by the
/// view model (marshalled to the UI thread), keeping the command layer free
/// of window plumbing.
/// </summary>
public sealed class ExitCommand : IShellCommand
{
    private readonly Action _quitApplication;

    /// <summary>Creates the command over the app-quit delegate.</summary>
    public ExitCommand(Action quitApplication)
    {
        _quitApplication = quitApplication ?? throw new ArgumentNullException(nameof(quitApplication));
    }

    /// <inheritdoc/>
    public string Name => "exit";

    /// <inheritdoc/>
    public string Summary => "Closes Lily.Shell.";

    /// <inheritdoc/>
    public string Usage => "exit";

    /// <inheritdoc/>
    public Task ExecuteAsync(ShellCommandContext context)
    {
        context.IO.WriteLine("Goodbye.");
        _quitApplication();
        return Task.CompletedTask;
    }
}
