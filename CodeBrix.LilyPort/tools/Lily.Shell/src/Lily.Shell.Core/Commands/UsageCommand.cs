// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Bootstrap;
using Lily.Shell.Kernel.Commands;
using System.Threading.Tasks;

namespace Lily.Shell.Commands;

/// <summary>
/// Prints the command-line usage message — the shell-side half of upstream's
/// <c>ly:usage</c>.
/// </summary>
/// <remarks>
/// The text comes from <see cref="UsageText"/> in the engine, which is also what the
/// <c>ly:usage</c> Scheme binding prints. ONE string with two callers, deliberately: a
/// usage message that disagrees with itself depending on how it was asked for is worse
/// than no usage message.
/// <para>
/// This is distinct from <c>help</c>, which lists the registered commands and can
/// describe one of them. <c>usage</c> answers the question upstream's
/// <c>lilypond --help</c> answers — what this program is and how it is invoked.
/// </para>
/// </remarks>
public sealed class UsageCommand : IShellCommand
{
    /// <inheritdoc/>
    public string Name => "usage";

    /// <inheritdoc/>
    public string Summary => "Prints the command-line usage message.";

    /// <inheritdoc/>
    public string Usage => "usage";

    /// <inheritdoc/>
    public Task ExecuteAsync(ShellCommandContext context)
    {
        foreach (string line in UsageText.Text.Split('\n'))
        {
            context.IO.WriteLine(line);
        }

        return Task.CompletedTask;
    }
}
