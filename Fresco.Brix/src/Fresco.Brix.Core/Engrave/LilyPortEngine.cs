// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fresco.Brix.Engrave;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Where the engine is in its life.</summary>
public enum EngineState
{
    /// <summary>Nothing has been asked of it yet.</summary>
    NotStarted,

    /// <summary>The Scheme layer is loading.</summary>
    Loading,

    /// <summary>Loaded, and ready to engrave.</summary>
    Ready,

    /// <summary>The load failed; see <see cref="LilyPortEngine.Error"/>.</summary>
    Failed,
}

/// <summary>
/// The application's one LilyPond engine, hosted IN THIS PROCESS.
/// </summary>
/// <remarks>
/// <para>
/// There is no external <c>lilypond</c> and there never will be: the engine is
/// CodeBrix.LilyPort, compiled in, and this class is the only thing in the
/// application that touches it. Three rules govern every call and none of them
/// is negotiable:
/// </para>
/// <list type="number">
/// <item><description>The Scheme layer needs a very large stack — far more
/// than a default CLR thread has — so every engine call goes through
/// <c>Interpreter.RunWithLargeStack</c> on a background thread.</description></item>
/// <item><description>The engine's state is process-global, so every call is
/// serialized through one gate. Two engraves at once would corrupt each
/// other.</description></item>
/// <item><description>The load is SLOW (measured: about 4.5 s warm, and about
/// 33 s the first time after a rebuild, while the Scheme boot cache re-records)
/// and never happens on the UI thread. It starts in the background as the
/// window opens, and anything that needs the engine waits on it.</description></item>
/// </list>
/// <para>
/// A running Scheme evaluation cannot be interrupted. Cancellation is honored
/// at the runner's own boundaries — before the parse, between books, and
/// before output is written — which is as fine as in-process cancellation
/// gets.
/// </para>
/// </remarks>
public sealed class LilyPortEngine
{
    static LilyPortEngine()
    {
        //Fresco.Brix.Ly is platform-free and does not reference LilyPort (plan §5.1),
        //so the LilyPond release reaches its data layer by injection rather than by a
        //literal of its own. This is the one place in the application that knows the
        //engine, so it is the one place that tells Ly.
        Ly.Data.LyData.Version = LilyPortInfo.CompatibleWithVersion;
    }

    private readonly object _gate = new object();
    private readonly SemaphoreSlim _engineLock = new SemaphoreSlim(1, 1);
    private readonly List<string> _includeDirectories = new List<string>();

    private Task _loadTask;

    /// <summary>Raised when the engine's state changes.</summary>
    /// <remarks>Raised on a BACKGROUND thread; marshal before touching the
    /// UI.</remarks>
    public event EventHandler StateChanged;

    /// <summary>Gets where the engine is in its life.</summary>
    public EngineState State { get; private set; } = EngineState.NotStarted;

    /// <summary>Gets how long the load took, once it has finished.</summary>
    public TimeSpan LoadElapsed { get; private set; }

    /// <summary>Gets what went wrong, when the load failed.</summary>
    public Exception Error { get; private set; }

    /// <summary>Gets whether the engine is loaded and idle-ready.</summary>
    public bool IsReady => State == EngineState.Ready;

    /// <summary>Gets the version of LilyPond this engine implements.</summary>
    /// <remarks>
    /// FR5.1: there is ONE engine, chosen at compile time. This replaces
    /// upstream's whole version-chooser — a user can see which grammar their
    /// documents are engraved against, and cannot change it.
    /// <para>
    /// //was previously: <c>Version => LilyPortInfo.UpstreamVersion</c>. Both halves of
    /// that were misleading: a member called <c>Version</c> on the engine reads as the
    /// ENGINE's version, and what it returned was the LilyPond release. LilyPort's own
    /// version is <see cref="PortVersion"/>, and the two are never the same number.
    /// </para>
    /// </remarks>
    public static string CompatibleWithVersion => LilyPortInfo.CompatibleWithVersion;

    /// <summary>Gets the version of CodeBrix.LilyPort itself — the engine package's
    /// own version, e.g. <c>1.0.244.123</c>, NOT the LilyPond release it implements.
    /// </summary>
    /// <remarks>
    /// Shown beside <see cref="CompatibleWithVersion"/> in the engine-information
    /// dialog so a user can tell a bug in the port from a feature of the grammar.
    /// </remarks>
    public static string PortVersion => LilyPortInfo.Version;

    /// <summary>Gets the directories added to the engine's include path.</summary>
    public IReadOnlyList<string> IncludeDirectories
    {
        get { lock (_gate) { return _includeDirectories.ToArray(); } }
    }

    /// <summary>Starts the background load; safe to call more than once.</summary>
    /// <returns>The load task.</returns>
    public Task BeginLoadingAsync() => EnsureLoadTask();

    /// <summary>Waits until the engine is loaded.</summary>
    /// <param name="cancellationToken">Cancels the WAIT, not the load.</param>
    /// <returns>The task.</returns>
    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
        => EnsureLoadTask().WaitAsync(cancellationToken);

    /// <summary>
    /// Adds a directory the engine searches for <c>\include</c> files.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns>The task.</returns>
    /// <remarks>The engine's parser session lives for the whole process and its
    /// include path is not reset between runs, so a directory added here stays
    /// added — which is exactly what a document's own folder wants.</remarks>
    public Task AddIncludeDirectoryAsync(string directory)
    {
        if (string.IsNullOrEmpty(directory)) { return Task.CompletedTask; }

        lock (_gate)
        {
            if (_includeDirectories.Contains(directory, StringComparer.Ordinal))
            {
                return Task.CompletedTask;
            }

            _includeDirectories.Add(directory);
        }

        return RunOnEngineAsync(() =>
        {
            var session = LilyPondInit.Session();
            if (!session.IncludePath.Contains(directory))
            {
                session.IncludePath.Add(directory);
            }

            return true;
        }, CancellationToken.None);
    }

    /// <summary>Engraves a file.</summary>
    /// <param name="filePath">The <c>.ly</c> file.</param>
    /// <param name="outputDirectory">Where the output lands.</param>
    /// <param name="outputBaseName">The output base name, or null for the
    /// input file's own.</param>
    /// <param name="options">The run's adjustments.</param>
    /// <param name="cancellationToken">Cancels the run at its boundaries.</param>
    /// <returns>What the run produced and reported.</returns>
    public Task<BatchRunResult> RunFileAsync(
        string filePath,
        string outputDirectory,
        string outputBaseName = null,
        BatchRunOptions options = null,
        CancellationToken cancellationToken = default)
        => RunOnEngineAsync(
            () => BatchRunner.RunFile(
                filePath, outputDirectory, outputBaseName, options),
            cancellationToken);

    /// <summary>Engraves a piece of text.</summary>
    /// <param name="text">The LilyPond source.</param>
    /// <param name="baseName">The output base name, without extension.</param>
    /// <param name="includeDirectory">The directory its own includes resolve
    /// against, or null.</param>
    /// <param name="outputDirectory">Where the output lands.</param>
    /// <param name="options">The run's adjustments.</param>
    /// <param name="cancellationToken">Cancels the run at its boundaries.</param>
    /// <returns>What the run produced and reported.</returns>
    public Task<BatchRunResult> RunTextAsync(
        string text,
        string baseName,
        string includeDirectory,
        string outputDirectory,
        BatchRunOptions options = null,
        CancellationToken cancellationToken = default)
        => RunOnEngineAsync(
            () => BatchRunner.RunText(
                text, baseName, includeDirectory, outputDirectory, options),
            cancellationToken);

    /// <summary>
    /// Runs work that needs the engine but is not a batch run — reading a
    /// document's declared version, for instance.
    /// </summary>
    /// <typeparam name="T">What the work returns.</typeparam>
    /// <param name="work">The work.</param>
    /// <param name="cancellationToken">Cancels the WAIT for the engine gate.</param>
    /// <returns>The result.</returns>
    public Task<T> RunEngineWorkAsync<T>(
        Func<T> work, CancellationToken cancellationToken = default)
        => RunOnEngineAsync(work, cancellationToken);

    private Task EnsureLoadTask()
    {
        lock (_gate)
        {
            return _loadTask ??= Task.Run(Load);
        }
    }

    private void Load()
    {
        SetState(EngineState.Loading);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            Interpreter.RunWithLargeStack(() =>
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                LilyPondScheme.LoadViaLilyScm(interpreter);
            });

            stopwatch.Stop();
            LoadElapsed = stopwatch.Elapsed;
            SetState(EngineState.Ready);
        }
        catch (Exception error)
        {
            stopwatch.Stop();
            LoadElapsed = stopwatch.Elapsed;
            Error = error;
            SetState(EngineState.Failed);
            throw;
        }
    }

    private void SetState(EngineState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<T> RunOnEngineAsync<T>(
        Func<T> work, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await _engineLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () => Interpreter.RunWithLargeStack(work), CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _engineLock.Release();
        }
    }
}
