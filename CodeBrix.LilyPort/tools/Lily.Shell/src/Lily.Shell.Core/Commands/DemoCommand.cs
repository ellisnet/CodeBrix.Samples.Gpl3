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
/// Runs the engine's proven "first light" path end to end: a Scheme-built
/// quarter-note c'4 through iterators, engravers, stencils and the real
/// Emmentaler font to an SVG document.
/// </summary>
public sealed class DemoCommand : IShellCommand
{
    private readonly LilyPortHost _host;

    /// <summary>Creates the command over the engine host.</summary>
    public DemoCommand(LilyPortHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <inheritdoc/>
    public string Name => "demo";

    /// <inheritdoc/>
    public string Summary => "Engraves the first-light demo (quarter-note c'4) to SVG.";

    /// <inheritdoc/>
    public string Usage => "demo [-o <out.svg>]";

    /// <inheritdoc/>
    public async Task ExecuteAsync(ShellCommandContext context)
    {
        string outputPath = null;
        for (var i = 0; i < context.Arguments.Count; i++)
        {
            if (context.Arguments[i] == "-o" && i + 1 < context.Arguments.Count)
            {
                outputPath = context.Arguments[i + 1];
                i++;
            }
        }

        context.IO.WriteLine("Engraving the first-light demo: music tree -> iterators -> engravers");
        context.IO.WriteLine("  -> paper columns -> one system -> stencils -> Emmentaler -> SVG...");

        var svg = await _host.EngraveDemoAsync(context.CancellationToken).ConfigureAwait(false);

        var noteHead = svg.Contains("noteheads.") ? "yes" : "NO";
        var clef = svg.Contains("clefs.") ? "yes" : "NO";
        context.IO.WriteLine($"Done: {svg.Length:n0} characters of SVG " +
            $"(note head: {noteHead}, clef: {clef}).");

        if (outputPath != null)
        {
            var fullPath = Path.GetFullPath(outputPath);
            File.WriteAllText(fullPath, svg);
            context.IO.WriteLine("Wrote " + fullPath);
        }
        else
        {
            context.IO.WriteLine("(add -o <file.svg> to save the document)");
        }
    }
}
