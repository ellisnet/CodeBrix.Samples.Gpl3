// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fresco.Brix.Engrave; //was previously: frescobaldi/job/__init__.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Which channel a line of a job's output came from.</summary>
/// <remarks>A flags enum because a log filters by a COMBINATION — upstream's
/// <c>job.OUTPUT</c>, <c>job.STATUS</c> and <c>job.ALL</c> are exactly these
/// unions.</remarks>
[Flags]
public enum MessageType
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The engine's ordinary output.</summary>
    StdOut = 1,

    /// <summary>The engine's warnings and errors.</summary>
    StdErr = 2,

    /// <summary>A status message from the application itself.</summary>
    Neutral = 4,

    /// <summary>The status message of a run that succeeded.</summary>
    Success = 8,

    /// <summary>The status message of a run that failed.</summary>
    Failure = 16,

    /// <summary>Everything the engine wrote.</summary>
    Output = StdOut | StdErr,

    /// <summary>Everything the application wrote.</summary>
    Status = Neutral | Success | Failure,

    /// <summary>Everything.</summary>
    All = Output | Status,
}

/// <summary>One line of a job's output, and where it came from.</summary>
/// <param name="Text">The text.</param>
/// <param name="Type">The channel.</param>
public readonly record struct JobMessage(string Text, MessageType Type);

/// <summary>
/// One unit of work the application runs on a document's behalf and reports on
/// as it goes: it has a title, a start and an end, output that arrives while it
/// runs, and a success or failure at the end.
/// </summary>
/// <remarks>
/// <para>
/// Upstream this class wraps a <c>QProcess</c>. Here there is no process:
/// the engine is in this one, and a job is an <c>await</c> against the engine
/// service. What is deliberately KEPT from upstream is everything a log, a
/// progress bar and a queue depend on — the message stream with its channels,
/// the history a log replays when it is opened late, the elapsed time, the
/// abort, and the exact status wording — because those are the parts the rest
/// of the application is written against.
/// </para>
/// <para>
/// A job runs ONCE. Starting a finished job is not upstream's model either.
/// </para>
/// </remarks>
public class EngraveJob
{
    private readonly List<JobMessage> _history = new List<JobMessage>();
    private readonly object _gate = new object();
    private CancellationTokenSource _cancellation;
    private SynchronizationContext _context;
    private string _title = string.Empty;
    private DateTime _startTime;
    private TimeSpan _elapsed;

    /// <summary>Creates a job.</summary>
    /// <param name="title">What the job is called in the log and the queue.</param>
    public EngraveJob(string title = "")
        => _title = title ?? string.Empty;

    /// <summary>Raised for every line of output, as it arrives.</summary>
    public event EventHandler<JobMessage> Output;

    /// <summary>Raised when the job has started running.</summary>
    public event EventHandler Started;

    /// <summary>Raised when the job has ended, however it ended.</summary>
    public event EventHandler<bool> Done;

    /// <summary>Raised when the title changes.</summary>
    public event EventHandler<string> TitleChanged;

    /// <summary>Gets or sets the job's title.</summary>
    public string Title
    {
        get => _title;
        set
        {
            string old = _title;
            _title = value ?? string.Empty;
            if (!string.Equals(old, _title, StringComparison.Ordinal))
            {
                TitleChanged?.Invoke(this, _title);
            }
        }
    }

    /// <summary>
    /// Gets or sets the ordering weight in a priority queue; lower runs first.
    /// </summary>
    /// <remarks>Upstream defaults a generic job to 1 and an engrave job to 2,
    /// so a quick crawl overtakes a long engrave.</remarks>
    public int Priority { get; set; } = 1;

    /// <summary>Gets or sets the file the job is run on.</summary>
    public string FileName { get; set; }

    /// <summary>Gets or sets the directory the job writes into.</summary>
    public string Directory { get; set; }

    /// <summary>
    /// Gets whether the job ended well; null until it has ended.
    /// </summary>
    public bool? Success { get; private set; }

    /// <summary>Gets the error that ended the job, when one did.</summary>
    public Exception Error { get; private set; }

    /// <summary>Gets whether the job is running right now.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Gets whether the job was aborted by a caller.</summary>
    public bool IsAborted { get; private set; }

    /// <summary>Gets whether the job has ever been started.</summary>
    public bool HasStarted { get; private set; }

    /// <summary>Gets the queue runner this job belongs to, when it has one.</summary>
    public JobRunner Runner { get; internal set; }

    /// <summary>Gets when the job started, or default when it has not.</summary>
    public DateTime StartTime => _startTime;

    /// <summary>Gets how long the job ran, or has been running.</summary>
    public TimeSpan ElapsedTime
        => _elapsed != TimeSpan.Zero
            ? _elapsed
            : _startTime == default
                ? TimeSpan.Zero
                : DateTime.UtcNow - _startTime;

    /// <summary>Gets the token that this job's work must honor.</summary>
    protected CancellationToken CancellationToken
        => _cancellation?.Token ?? CancellationToken.None;

    /// <summary>Starts the job.</summary>
    /// <returns>A task that completes when the job has ended.</returns>
    public async Task StartAsync()
    {
        lock (_gate)
        {
            if (IsRunning) { return; }

            Success = null;
            Error = null;
            IsAborted = false;
            IsRunning = true;
            HasStarted = true;
            _history.Clear();
            _elapsed = TimeSpan.Zero;
            _startTime = DateTime.UtcNow;
            _cancellation = new CancellationTokenSource();

            //⚠ THE ENGINE WRITES FROM ITS OWN THREAD, MID-RUN. Everything that
            //listens to a job's output touches the editor — the log writes into
            //a text document, and the error collector puts anchors into one —
            //and a text document may only be touched from the thread that owns
            //it. So the thread that STARTS a job is remembered here, and every
            //message is delivered back on it. This is what Qt gives upstream
            //for free with a queued signal connection.
            _context = SynchronizationContext.Current;
        }

        WriteStartMessage();
        Started?.Invoke(this, EventArgs.Empty);

        bool success;
        try
        {
            success = await RunAsync().ConfigureAwait(true);
            WriteFinishMessage(success);
        }
        catch (OperationCanceledException)
        {
            //An aborted run is not a failure to report as one; upstream's
            //abort path writes its own message and ends the job quietly.
            success = false;
        }
        catch (Exception error)
        {
            Error = error;
            WriteErrorMessage(error);
            success = false;
        }

        Finish(success);
    }

    /// <summary>Asks the job to stop.</summary>
    /// <remarks>Cancellation is honored where the engine can honor it — before
    /// a parse, between books, and before output is written. One book's
    /// engraving is a single uninterruptible call, so a very large score
    /// finishes that book first.</remarks>
    public void Abort()
    {
        if (!IsRunning) { return; }

        IsAborted = true;
        WriteAbortMessage();
        _cancellation?.Cancel();
    }

    /// <summary>Writes a line of output.</summary>
    /// <param name="text">The text.</param>
    /// <param name="type">The channel.</param>
    public void Message(string text, MessageType type = MessageType.Neutral)
    {
        if (text == null) { return; }

        JobMessage message = new JobMessage(text, type);

        //The history is complete the moment the message is made, so a log that
        //connects late replays everything even while the run is still going.
        lock (_gate) { _history.Add(message); }

        SynchronizationContext context = _context;
        if (context == null || context == SynchronizationContext.Current)
        {
            Output?.Invoke(this, message);
            return;
        }

        //Posted, not sent: the engine must not be made to wait for a redraw,
        //and posting keeps the messages in order.
        context.Post(_ => Output?.Invoke(this, message), null);
    }

    /// <summary>Gets the output so far, optionally filtered by channel.</summary>
    /// <param name="types">The channels to include.</param>
    /// <returns>The messages.</returns>
    /// <remarks>This is what lets a log opened halfway through a run show
    /// everything the run has said so far.</remarks>
    public IReadOnlyList<JobMessage> History(MessageType types = MessageType.All)
    {
        lock (_gate)
        {
            return _history.Where(m => (m.Type & types) != 0).ToList();
        }
    }

    /// <summary>Gets everything written to the ordinary output channel.</summary>
    /// <returns>The text.</returns>
    public string StdOut()
        => string.Concat(History(MessageType.StdOut).Select(m => m.Text));

    /// <summary>Gets everything written to the error channel.</summary>
    /// <returns>The text.</returns>
    public string StdErr()
        => string.Concat(History(MessageType.StdErr).Select(m => m.Text));

    /// <summary>Formats a duration the short way a log shows it.</summary>
    /// <param name="elapsed">The duration.</param>
    /// <returns>The text.</returns>
    public static string ElapsedToString(TimeSpan elapsed)
    {
        int minutes = (int)elapsed.TotalMinutes;
        double seconds = elapsed.TotalSeconds - (minutes * 60);
        return minutes > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0:0}'{1:0}\"", minutes, seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0:0.0}\"", seconds);
    }

    /// <summary>Does the job's actual work.</summary>
    /// <returns>Whether it succeeded.</returns>
    protected virtual Task<bool> RunAsync() => Task.FromResult(true);

    /// <summary>Announces that the job has started.</summary>
    protected virtual void WriteStartMessage()
        => Message(
            I18n.Format(I18n.Get("Starting {job}..."), ("job", NameForMessages())),
            MessageType.Neutral);

    /// <summary>Announces that the job is being aborted.</summary>
    protected virtual void WriteAbortMessage()
        => Message(
            I18n.Format(I18n.Get("Aborting {job}..."), ("job", NameForMessages())),
            MessageType.Neutral);

    /// <summary>Announces that the job has ended.</summary>
    /// <param name="success">Whether it went well.</param>
    protected virtual void WriteFinishMessage(bool success)
        => Message(
            success
                ? I18n.Format(
                    I18n.Get("Completed successfully in {time}."),
                    ("time", ElapsedToString(DateTime.UtcNow - _startTime)))
                : I18n.Get("Exited with an error."),
            success ? MessageType.Success : MessageType.Failure);

    /// <summary>Announces that the job ended in an exception.</summary>
    /// <param name="error">The error.</param>
    protected virtual void WriteErrorMessage(Exception error)
        => Message(error.Message + "\n", MessageType.Failure);

    /// <summary>Gets the name status messages call this job by.</summary>
    /// <returns>The name.</returns>
    protected virtual string NameForMessages()
        => string.IsNullOrEmpty(Title) ? AppInfo.AppName : Title;

    private void Finish(bool success)
    {
        lock (_gate)
        {
            _elapsed = DateTime.UtcNow - _startTime;
            IsRunning = false;
            Success = success;
            _cancellation?.Dispose();
            _cancellation = null;
        }

        Done?.Invoke(this, success);
    }
}
