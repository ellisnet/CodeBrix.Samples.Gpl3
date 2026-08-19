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
using System.IO;
using System.Threading.Tasks;

namespace Lily.Shell.Commands;

/// <summary>
/// The `lilypond file.ly` counterpart: runs a file through the real batch
/// pipeline — parse, engrave, SVG, and <c>.midi</c> whenever a score carries a
/// <c>\midi</c> block — and reports every artifact it wrote.
/// </summary>
/// <remarks>
/// This command once stopped at the parse step, with a note that
/// the pipeline was under construction; the pipeline produces pages and
/// MIDI, and the standing expectation is that Lily.Shell
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
    public string Usage => "engrave <file.ly> [-o <output-dir-or-name>]";

    /// <inheritdoc/>
    public async Task ExecuteAsync(ShellCommandContext context)
    {
        string path = null;
        string outputName = null;
        string outputDirectory;

        for (var i = 0; i < context.Arguments.Count; i++)
        {
            if (context.Arguments[i] == "-o" && i + 1 < context.Arguments.Count)
            {
                outputName = context.Arguments[i + 1];
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

        // `-o' IS lilypond's, so it means what lilypond's means: a NAME, not just a
        // directory. main.cc:729-761 asks whether the value is an existing directory and
        // uses it as one if so; otherwise it splits the value and the FILE part becomes
        // the output base name. `lily.shell engrave x.ly -o out/dir/name' therefore writes
        // out/dir/name.svg, the way `lilypond -o out/dir/name x.ly' does.
        //
        //was previously: the whole value was taken as a directory, so `-o out/dir/name'
        // created a directory called `name' and wrote x.svg inside it.
        BatchRunner.SplitOutputName(outputName, out var namedDirectory, out var baseName);

        outputDirectory = namedDirectory
            ?? Path.GetDirectoryName(Path.GetFullPath(path));
        Directory.CreateDirectory(outputDirectory);

        var result = await _host
            .EngraveFileAsync(path, outputDirectory, baseName, context.CancellationToken)
            .ConfigureAwait(false);

        foreach (var diagnostic in result.Diagnostics)
        {
            context.IO.WriteLine("  " + diagnostic);
        }

        if (result.SvgPath != null)
        {
            // The system count is worth printing now that it means something: line
            // breaking is real, so a score too long for one line
            // comes back as several systems rather than one over-full one.
            //
            // EVERY PAGE IS LISTED, not just the first. The runner
            // writes one file per page now, so reporting SvgPath alone told the user
            // about page 1 and silently dropped the rest of the book — the shell's own
            // version of the bug the comparator had, where a multi-page reference was
            // reported MISSING however well the music engraved.
            string systems = result.SystemCount == 1 ? "1 system" : result.SystemCount + " systems";
            string pages = result.SvgPaths.Count == 1
                ? "1 page"
                : result.SvgPaths.Count + " pages";
            context.IO.WriteLine("SVG:  " + pages + ", " + systems);
            foreach (var svgPath in result.SvgPaths)
            {
                context.IO.WriteLine("      " + svgPath);
            }
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
