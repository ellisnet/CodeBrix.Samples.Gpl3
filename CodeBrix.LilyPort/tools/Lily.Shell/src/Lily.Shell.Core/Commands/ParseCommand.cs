// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyScheme.Runtime;
using Lily.Shell.Kernel.Commands;
using Lily.Shell.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Lily.Shell.Commands;

/// <summary>
/// Parses a .ly file with the port's real parser (Track P) against the live
/// Scheme layer and reports the outcome and every diagnostic.
/// </summary>
public sealed class ParseCommand : IShellCommand
{
    private readonly LilyPortHost _host;

    /// <summary>Creates the command over the engine host.</summary>
    public ParseCommand(LilyPortHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <inheritdoc/>
    public string Name => "parse";

    /// <inheritdoc/>
    public string Summary => "Parses a .ly file and shows diagnostics.";

    /// <inheritdoc/>
    public string Usage => "parse <file.ly>";

    /// <inheritdoc/>
    public async Task ExecuteAsync(ShellCommandContext context)
    {
        if (context.Arguments.Count != 1)
        {
            context.IO.WriteLine("usage: " + Usage);
            return;
        }

        var path = context.Arguments[0];
        if (!File.Exists(path))
        {
            context.IO.WriteLine("No such file: " + path);
            return;
        }

        var outcome = await _host.ParseFileAsync(path, context.CancellationToken)
            .ConfigureAwait(false);

        foreach (var diagnostic in outcome.AllDiagnostics())
        {
            context.IO.WriteLine("  " + diagnostic);
        }

        if (outcome.Success)
        {
            context.IO.WriteLine($"Parse OK ({outcome.Diagnostics.Count} diagnostics).");
            var result = Printer.Write(outcome.Result);
            if (!string.IsNullOrEmpty(result) && result != "#<unspecified>")
            {
                context.IO.WriteLine("result: " + Truncate(result, 500));
            }
        }
        else
        {
            context.IO.WriteLine($"Parse FAILED ({outcome.ErrorCount} errors).");
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + " ...";
}
