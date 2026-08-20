// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.IO;
using System.Threading;

namespace CodeBrix.LilyPort;

/// <summary>
/// A host's per-run adjustments to one <see cref="BatchRunner"/> run — the in-process
/// equivalent of what a <c>lilypond</c> command line carries beyond the file name.
/// <para>
/// Everything here lives for ONE run: option overrides are applied after the per-file
/// restore that opens the run, so the next run's restore puts the engine back on its
/// own defaults — exactly the lifetime a per-process <c>-d</c> option has upstream,
/// where every file is its own process.
/// </para>
/// </summary>
public sealed class BatchRunOptions
{
    /// <summary>
    /// Gets or sets the <c>point-and-click</c> option's value for this run, or
    /// <see langword="null"/> to leave the engine's default (<see langword="true"/>,
    /// as upstream's) in place.
    /// <para>
    /// The option's three declared shapes are all accepted, exactly as
    /// <c>-dpoint-and-click</c> declares them: a <see cref="bool"/>, a
    /// <c>CodeBrix.LilyScheme.Values.Symbol</c> naming one event class, or a Scheme
    /// list of such symbols. The regression harness passes <see langword="false"/>
    /// here, mirroring the <c>-dno-point-and-click</c> its reference generation uses;
    /// an editor host passes <see langword="true"/> (or a class filter) for a preview
    /// build and <see langword="false"/> for a publish build.
    /// </para>
    /// </summary>
    public object PointAndClick { get; set; }

    /// <summary>
    /// Gets or sets where this run's progress and diagnostics are written, or
    /// <see langword="null"/> to leave the process-wide writer in place.
    /// <para>
    /// The writer receives everything the engine prints for the run — progress
    /// ("Interpreting music..."), warnings and errors with their
    /// <c>file:line:col</c> locations — as it prints, which is what a host's log
    /// panel wants. Parse diagnostics are ALSO collected into
    /// <see cref="BatchRunResult.Diagnostics"/> either way. The previous writer is
    /// restored when the run ends; runs are serialised, so the swap is race-free.
    /// </para>
    /// </summary>
    public TextWriter MessageWriter { get; set; }

    /// <summary>
    /// Gets or sets the token that cancels this run.
    /// <para>
    /// Cancellation is COOPERATIVE AT THE RUNNER'S OWN BOUNDARIES: before the parse,
    /// between books, and before output is written. A single book's engraving is one
    /// uninterruptible engine call, so a very large score finishes (or fails) its
    /// current book before the token is honoured. A cancelled run throws
    /// <see cref="System.OperationCanceledException"/> and writes no further output;
    /// the engine is left consistent because the next run's per-file restore is the
    /// same one every run gets.
    /// </para>
    /// </summary>
    public CancellationToken CancellationToken { get; set; }
}
