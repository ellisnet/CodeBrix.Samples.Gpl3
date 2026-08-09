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
/// The `lilypond file.ly` counterpart: runs a file through the real batch
/// pipeline — parse, engrave, SVG, and <c>.midi</c> whenever a score carries a
/// <c>\midi</c> block — and reports every artifact it wrote.
/// </summary>
/// <remarks>
/// Until 2026-08-08 this command stopped at the parse step, with a note that
/// the pipeline was under construction; the pipeline has produced pages since
/// EPG3 and MIDI since EPG19, and the standing expectation is that Lily.Shell
/// keeps up with user-visible engine capability.
/// </remarks>
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
    public string Summary => "Engraves a .ly file to SVG (and .midi when the score has a \\midi block).";

    /// <inheritdoc/>
    public string Usage => "engrave <file.ly> [-o <output-dir>]";

    /// <inheritdoc/>
    public async Task ExecuteAsync(ShellCommandContext context)
    {
        string path = null;
        string outputDirectory = null;

        for (var i = 0; i < context.Arguments.Count; i++)
        {
            if (context.Arguments[i] == "-o" && i + 1 < context.Arguments.Count)
            {
                outputDirectory = context.Arguments[i + 1];
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

        outputDirectory ??= Path.GetDirectoryName(Path.GetFullPath(path));
        Directory.CreateDirectory(outputDirectory);

        var result = await _host.EngraveFileAsync(path, outputDirectory, context.CancellationToken)
            .ConfigureAwait(false);

        foreach (var diagnostic in result.Diagnostics)
        {
            context.IO.WriteLine("  " + diagnostic);
        }

        if (result.SvgPath != null)
        {
            context.IO.WriteLine("SVG:  " + result.SvgPath);
        }

        foreach (var midiPath in result.MidiPaths)
        {
            context.IO.WriteLine("MIDI: " + midiPath);
        }

        if (result.SvgPath == null && result.MidiPaths.Count == 0)
        {
            context.IO.WriteLine(result.ErrorCount > 0
                ? $"No output produced ({result.ErrorCount} error(s))."
                : "No output produced (the file may hold no \\score, or engraving stopped early).");
        }
    }
}
