// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Parsing.Session;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using Lily.Shell.Kernel;
using Lily.Shell.Kernel.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lily.Shell.Services;

/// <summary>
/// Hosts the in-process LilyPort engine for the shell: one interpreter for
/// the process lifetime, created on a background 256 MB-stack thread (the
/// Scheme layer overflows a default CLR stack), with every engine operation
/// serialized through one gate — the engine's process-global state
/// (LilyPondScheme.Current and friends) does not tolerate concurrency.
/// </summary>
/// <remarks>
/// A running Scheme evaluation cannot be interrupted; cancellation is honored
/// between operations, not inside one.
/// </remarks>
public sealed class LilyPortHost
{
    private const string DemoMusicScheme = """
        (define lily-shell-demo-music
          (make-music 'SequentialMusic
            'elements (list (make-music 'NoteEvent
                              'duration (ly:make-duration 2)
                              'pitch (ly:make-pitch 0 0 0)))))
        lily-shell-demo-music
        """;

    private readonly ShellSession _session;
    private readonly IShellIO _io;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _engineLock = new(1, 1);
    private readonly List<string> _includeDirectories = [];

    private Task _loadTask;
    private Interpreter _interpreter;
    private ShellIOTextWriter _schemeOutput;
    private LilyParserSession _parserSession;

    /// <summary>
    /// Creates the host over the session it serves: in-command messages go to
    /// the session's output; the async load-completion announcement goes
    /// through the session's out-of-band path, which knows whether a prompt
    /// repaint is needed.
    /// </summary>
    public LilyPortHost(ShellSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _io = session.Output;
    }

    /// <summary>
    /// Raised when the background load finishes, success or failure — on the
    /// load worker thread; marshal before touching UI state.
    /// </summary>
    public event Action LoadFinished;

    /// <summary>True once the Scheme layer has loaded successfully.</summary>
    public bool IsReady
    {
        get { lock (_gate) { return _loadTask is { IsCompletedSuccessfully: true }; } }
    }

    /// <summary>The include directories applied to parses (list grows via 'include').</summary>
    public IReadOnlyList<string> IncludeDirectories
    {
        get { lock (_gate) { return _includeDirectories.ToArray(); } }
    }

    /// <summary>Adds an include directory for subsequent parses.</summary>
    public void AddIncludeDirectory(string directory)
    {
        lock (_gate)
        {
            if (!_includeDirectories.Contains(directory)) { _includeDirectories.Add(directory); }
        }
    }

    /// <summary>Starts the ~20 s background engine load (idempotent).</summary>
    public void BeginLoading() => EnsureLoadTask();

    /// <summary>Waits until the engine is loaded, announcing the wait when it is not.</summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        var task = EnsureLoadTask();
        if (!task.IsCompleted)
        {
            _io.WriteLine("(waiting for the engine to finish loading...)");
        }

        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Evaluates Scheme source through the psyntax expander (the EvalString
    /// shortcut bypasses macros and is wrong for LilyPond Scheme) and returns
    /// the last result printed with `write` conventions.
    /// </summary>
    public Task<string> EvaluateSchemeAsync(string source, CancellationToken cancellationToken) =>
        RunOnEngineAsync(interpreter =>
        {
            object result = null;
            foreach (var form in SchemeReader.ReadAll(source, "<lily-shell>"))
            {
                result = interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
            }

            _schemeOutput?.Flush();
            return Printer.Write(result);
        }, cancellationToken);

    /// <summary>Parses a .ly file and returns the parser's outcome.</summary>
    public Task<ParseOutcome> ParseFileAsync(string path, CancellationToken cancellationToken)
    {
        var text = File.ReadAllText(path);
        var fileName = Path.GetFileName(path);
        AddIncludeDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));

        return RunOnEngineAsync(interpreter =>
        {
            EnsureParserSession(interpreter);
            return _parserSession.ParseText(text, fileName);
        }, cancellationToken);
    }

    /// <summary>
    /// Engraves the first-light demo (a Scheme-built quarter-note c'4) end to
    /// end and returns the SVG document.
    /// </summary>
    public Task<string> EngraveDemoAsync(CancellationToken cancellationToken) =>
        RunOnEngineAsync(interpreter =>
        {
            object music = null;
            foreach (var form in SchemeReader.ReadAll(DemoMusicScheme, "<lily-shell-demo>"))
            {
                music = interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
            }

            return LilyPortEngraver.EngraveToSvg((MusicObject)music);
        }, cancellationToken);

    private Task EnsureLoadTask()
    {
        lock (_gate)
        {
            return _loadTask ??= Task.Run(LoadEngine);
        }
    }

    private void LoadEngine()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Interpreter interpreter = null;
            Interpreter.RunWithLargeStack(() =>
            {
                interpreter = LilyPondScheme.CreateInterpreter();
                LilyPondScheme.LoadViaLilyScm(interpreter);
            });

            //Scheme display/format output goes to the terminal from here on
            _schemeOutput = new ShellIOTextWriter(_io);
            interpreter.OutputWriter = _schemeOutput;
            interpreter.ErrorWriter = new ShellIOTextWriter(_io);
            _interpreter = interpreter;

            stopwatch.Stop();
            _session.WriteOutOfBand(
                $"LilyPond Scheme layer ready ({stopwatch.Elapsed.TotalSeconds:0.0} s). " +
                "Try 'scheme' or 'demo'.");
            LoadFinished?.Invoke();
        }
        catch (Exception ex)
        {
            _session.WriteOutOfBand("Engine load FAILED: " + ex.Message);
            LoadFinished?.Invoke();
            throw;
        }
    }

    private async Task<T> RunOnEngineAsync<T>(Func<Interpreter, T> work,
        CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await _engineLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
                Interpreter.RunWithLargeStack(() => work(_interpreter))).ConfigureAwait(false);
        }
        finally
        {
            _engineLock.Release();
        }
    }

    private void EnsureParserSession(Interpreter interpreter)
    {
        if (_parserSession == null)
        {
            _io.WriteLine("(generating parse tables - first parse only...)");
            _parserSession = new LilyParserSession(interpreter);
        }

        lock (_gate)
        {
            foreach (var directory in _includeDirectories)
            {
                if (!_parserSession.IncludePath.Contains(directory))
                {
                    _parserSession.IncludePath.Add(directory);
                }
            }
        }
    }
}
