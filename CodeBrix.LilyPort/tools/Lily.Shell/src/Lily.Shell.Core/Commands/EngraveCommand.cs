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
/// The `lilypond file.ly` counterpart. Today it runs what exists of the
/// pipeline — the real parser over the live Scheme layer — and reports
/// honestly where the pipeline currently ends (the .ly-to-music step lands
/// with the EPG1-EPG3 engine groups). The 'demo' command shows the
/// music-to-SVG half working end to end.
/// </summary>
public sealed class EngraveCommand : IShellCommand
{
    private readonly LilyPortHost _host;

    /// <summary>Creates the command over the engine host.</summary>
    public EngraveCommand(LilyPortHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <inheritdoc/>
    public string Name => "engrave";

    /// <inheritdoc/>
    public string Summary => "Engraves a .ly file (currently: parses; full pipeline under construction).";

    /// <inheritdoc/>
    public string Usage => "engrave <file.ly> [-o <out.svg>]";

    /// <inheritdoc/>
    public async Task ExecuteAsync(ShellCommandContext context)
    {
        string path = null;
        string outputPath = null;

        for (var i = 0; i < context.Arguments.Count; i++)
        {
            if (context.Arguments[i] == "-o" && i + 1 < context.Arguments.Count)
            {
                outputPath = context.Arguments[i + 1];
                i++;
            }
            else if (path == null)
            {
                path = context.Arguments[i];
            }
        }

        if (path == null)
        {
            context.IO.WriteLine("usage: " + Usage);
            return;
        }

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

        if (!outcome.Success)
        {
            context.IO.WriteLine($"Parse FAILED ({outcome.ErrorCount} errors) - nothing to engrave.");
            return;
        }

        context.IO.WriteLine("Parse OK.");
        context.IO.WriteLine("The .ly-to-engraving connection is not built yet - music functions and");
        context.IO.WriteLine("the init layer land with the next engine groups (EPG1-EPG3). Until then,");
        context.IO.WriteLine("'demo' engraves the working end-to-end path and 'scheme' talks to the engine.");

        if (outputPath != null)
        {
            context.IO.WriteLine($"(-o {outputPath} noted - it will apply once the pipeline connects)");
        }
    }
}
