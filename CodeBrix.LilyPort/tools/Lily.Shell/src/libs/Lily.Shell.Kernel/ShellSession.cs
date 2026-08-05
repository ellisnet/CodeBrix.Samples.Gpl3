// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Shell.Kernel.Commands;
using Lily.Shell.Kernel.Editing;
using Lily.Shell.Kernel.IO;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lily.Shell.Kernel;

/// <summary>
/// The shell's brain: consumes VT-encoded input from a terminal view, edits
/// the current line (with history), and hands submitted lines to the active
/// <see cref="ILineInterpreter"/> — the command dispatcher by default, or a
/// pushed sub-mode such as a Scheme REPL. All output (echo, prompts, command
/// results) is raised through <see cref="OutputProduced"/> as VT data for the
/// terminal view to display.
/// </summary>
/// <remarks>
/// Threading: <see cref="SendInput"/> must be called from one thread at a time
/// (the UI thread, in practice). Lines execute sequentially on worker threads,
/// so <see cref="OutputProduced"/> is raised on the input thread for echo and
/// on worker threads for command output — the attached view must marshal.
/// </remarks>
public sealed class ShellSession
{
    private readonly InputTokenizer _tokenizer = new();
    private readonly LineEditor _editor = new();
    private readonly List<string> _history = [];
    private readonly Stack<ILineInterpreter> _interpreters = new();
    private readonly object _gate = new();

    private Task _executionChain = Task.CompletedTask;
    private CancellationTokenSource _activeCts;
    private int _queuedLines;
    private int _historyIndex;
    private string _pendingLine = string.Empty;
    private bool _lastInputWasCr;

    /// <summary>Creates a session over a command registry.</summary>
    public ShellSession(CommandRegistry commands, ShellSessionOptions options = null)
    {
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
        Options = options ?? new ShellSessionOptions();
        Output = new DelegateShellIO(RaiseOutput);
        _interpreters.Push(new CommandInterpreter(Commands, Options.Prompt));
    }

    /// <summary>
    /// Raised with VT data to display. Echo is raised on the input thread;
    /// command output on worker threads. The terminal view marshals.
    /// </summary>
    public event Action<string> OutputProduced;

    /// <summary>The output surface commands and interpreters write to.</summary>
    public IShellIO Output { get; }

    /// <summary>The registered commands.</summary>
    public CommandRegistry Commands { get; }

    /// <summary>The session options.</summary>
    public ShellSessionOptions Options { get; }

    /// <summary>The prompt of the active interpreter.</summary>
    public string Prompt => CurrentInterpreter.Prompt;

    /// <summary>True while a submitted line is executing.</summary>
    public bool IsExecuting => _activeCts != null;

    /// <summary>True while a submitted line is executing or queued.</summary>
    public bool IsBusy => _activeCts != null || Volatile.Read(ref _queuedLines) > 0;

    /// <summary>The chain of queued/executing lines — awaited by tests to reach quiescence.</summary>
    internal Task ExecutionChain
    {
        get { lock (_gate) { return _executionChain; } }
    }

    private ILineInterpreter CurrentInterpreter
    {
        get { lock (_interpreters) { return _interpreters.Peek(); } }
    }

    /// <summary>Writes the banner (if any) and the first prompt.</summary>
    public void Start()
    {
        if (Options.Banner is { Length: > 0 })
        {
            foreach (var line in Options.Banner) { Output.WriteLine(line); }
        }

        WritePrompt();
    }

    /// <summary>
    /// Enters a sub-mode: subsequent lines go to the pushed interpreter until
    /// it is popped (or the user presses Ctrl+D on an empty line).
    /// </summary>
    public void PushInterpreter(ILineInterpreter interpreter)
    {
        if (interpreter == null) { throw new ArgumentNullException(nameof(interpreter)); }
        lock (_interpreters) { _interpreters.Push(interpreter); }
    }

    /// <summary>Leaves the current sub-mode. The root interpreter is never popped.</summary>
    public void PopInterpreter()
    {
        lock (_interpreters)
        {
            if (_interpreters.Count > 1) { _interpreters.Pop(); }
        }
    }

    /// <summary>Feeds VT-encoded user input (keystrokes or paste) into the session.</summary>
    public void SendInput(string data)
    {
        foreach (var token in _tokenizer.Feed(data))
        {
            switch (token.Kind)
            {
                case InputTokenKind.Character:
                    _lastInputWasCr = false;
                    ResetHistoryNavigation();
                    Echo(_editor.Insert(token.Character));
                    break;

                case InputTokenKind.Control:
                    HandleControl(token.Character);
                    break;

                case InputTokenKind.Key:
                    _lastInputWasCr = false;
                    HandleKey(token.Key);
                    break;
            }
        }
    }

    private void HandleControl(char c)
    {
        var wasCr = _lastInputWasCr;
        _lastInputWasCr = c == '\r';

        switch (c)
        {
            case '\r':
                SubmitLine();
                break;

            case '\n':
                //Half of a CRLF pair that was already submitted by the CR
                if (!wasCr) { SubmitLine(); }
                break;

            case '\b':
            case '\x7f':
                ResetHistoryNavigation();
                Echo(_editor.Backspace());
                break;

            case '\x03': //Ctrl+C
                if (_activeCts is { } cts)
                {
                    cts.Cancel();
                }
                else
                {
                    Echo("^C\r\n");
                    _editor.Reset();
                    ResetHistoryNavigation();
                    WritePrompt();
                }
                break;

            case '\x04': //Ctrl+D - leave a sub-mode when the line is empty
                if (_editor.Text.Length == 0 && InterpreterDepth > 1)
                {
                    Echo("\r\n");
                    PopInterpreter();
                    WritePrompt();
                }
                break;

            case '\x0c': //Ctrl+L - clear screen, repaint prompt and line
                Echo("\x1b[2J\x1b[H");
                Echo(Prompt);
                Echo(_editor.Redraw());
                break;
        }
    }

    private void HandleKey(EditKey key)
    {
        switch (key)
        {
            case EditKey.Left: Echo(_editor.MoveLeft()); break;
            case EditKey.Right: Echo(_editor.MoveRight()); break;
            case EditKey.Home: Echo(_editor.MoveHome()); break;
            case EditKey.End: Echo(_editor.MoveEnd()); break;

            case EditKey.Delete:
                ResetHistoryNavigation();
                Echo(_editor.Delete());
                break;

            case EditKey.Up:
                if (_historyIndex > 0)
                {
                    if (_historyIndex == _history.Count) { _pendingLine = _editor.Text; }
                    _historyIndex--;
                    Echo(_editor.ReplaceWith(_history[_historyIndex]));
                }
                break;

            case EditKey.Down:
                if (_historyIndex < _history.Count)
                {
                    _historyIndex++;
                    var text = _historyIndex == _history.Count
                        ? _pendingLine
                        : _history[_historyIndex];
                    Echo(_editor.ReplaceWith(text));
                }
                break;

            //PageUp/PageDown are the view's scrollback keys - nothing to do here
        }
    }

    private int InterpreterDepth
    {
        get { lock (_interpreters) { return _interpreters.Count; } }
    }

    private void SubmitLine()
    {
        var line = _editor.TakeLine();
        Echo("\r\n");

        if (line.Trim().Length > 0 &&
            (_history.Count == 0 || _history[^1] != line))
        {
            _history.Add(line);
        }

        ResetHistoryNavigation();
        EnqueueLine(line);
    }

    private void ResetHistoryNavigation()
    {
        _historyIndex = _history.Count;
        _pendingLine = string.Empty;
    }

    private void EnqueueLine(string line)
    {
        lock (_gate)
        {
            Interlocked.Increment(ref _queuedLines);
            var previous = _executionChain;
            //Task.Run keeps interpreter work off the input (UI) thread even
            //  when the previous chain link has already completed.
            _executionChain = Task.Run(() => RunAfterAsync(previous, line));
        }
    }

    private async Task RunAfterAsync(Task previous, string line)
    {
        await previous.ConfigureAwait(false);
        await ExecuteLineAsync(line).ConfigureAwait(false);
    }

    private async Task ExecuteLineAsync(string line)
    {
        var interpreter = CurrentInterpreter;
        var cts = new CancellationTokenSource();
        _activeCts = cts;
        try
        {
            await interpreter.HandleLineAsync(this, line, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Output.WriteLine("^C");
        }
        catch (Exception ex)
        {
            Output.WriteLine("error: " + DeepestMessage(ex));
        }
        finally
        {
            _activeCts = null;
            cts.Dispose();
            WritePrompt();
            //Decrement AFTER the prompt: an out-of-band message racing this
            //  completion then writes message-only instead of a second prompt
            Interlocked.Decrement(ref _queuedLines);
        }
    }

    /// <summary>
    /// Writes message lines that arrive outside any command execution (a
    /// background task finishing, for example). Idle at a prompt: opens a
    /// fresh line, writes the lines, and repaints the prompt with any
    /// half-typed input. While a line is executing or queued: writes the
    /// lines only — the command's own completion prints the next prompt.
    /// </summary>
    public void WriteOutOfBand(params string[] lines)
    {
        var busy = IsBusy;
        if (!busy) { Output.WriteLine(); }

        if (lines != null)
        {
            foreach (var line in lines) { Output.WriteLine(line); }
        }

        if (!busy) { RepaintPrompt(); }
    }

    /// <summary>
    /// The innermost exception message — wrapper exceptions ("evaluation
    /// failed on thread X") hide the message the user actually needs.
    /// </summary>
    public static string DeepestMessage(Exception exception)
    {
        while (exception.InnerException != null) { exception = exception.InnerException; }
        return exception.Message;
    }

    /// <summary>
    /// Reprints the prompt and the line being edited — call after out-of-band
    /// output (a background task finishing) interrupted the input line. Call
    /// from the same thread that calls <see cref="SendInput"/>.
    /// </summary>
    public void RepaintPrompt()
    {
        Output.Write(Prompt);
        Echo(_editor.Redraw());
    }

    private void WritePrompt() => Output.Write(Prompt);

    private void Echo(string text)
    {
        if (!string.IsNullOrEmpty(text)) { RaiseOutput(text); }
    }

    private void RaiseOutput(string text) => OutputProduced?.Invoke(text);
}
