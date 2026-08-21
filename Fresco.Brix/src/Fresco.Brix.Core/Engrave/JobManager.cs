// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using System;

namespace Fresco.Brix.Engrave; //was previously: frescobaldi/job/manager.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>A job and the document it was run for.</summary>
public sealed class JobEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="document">The document.</param>
    /// <param name="job">The job.</param>
    /// <param name="success">Whether the job went well, when it has ended.</param>
    public JobEventArgs(EditorDocument document, EngraveJob job, bool success = false)
    {
        Document = document;
        Job = job;
        Success = success;
    }

    /// <summary>Gets the document.</summary>
    public EditorDocument Document { get; }

    /// <summary>Gets the job.</summary>
    public EngraveJob Job { get; }

    /// <summary>Gets whether the job went well.</summary>
    public bool Success { get; }
}

/// <summary>
/// One per document: it holds that document's current job and refuses to start
/// a second one while the first is running.
/// </summary>
/// <remarks>
/// It is also where the application-wide job announcements come from, so a log,
/// a progress bar and a menu can all follow "a job started for that document"
/// without any of them knowing about each other.
/// </remarks>
public sealed class JobManager : Plugin<EditorDocument, JobManager>
{
    private EngraveJob _job;

    private JobManager(EditorDocument document)
        : base(document)
    {
    }

    /// <summary>Raised, application-wide, when any document's job starts.</summary>
    public static event EventHandler<JobEventArgs> AnyJobStarted;

    /// <summary>Raised, application-wide, when any document's job ends.</summary>
    public static event EventHandler<JobEventArgs> AnyJobFinished;

    /// <summary>Raised when this document's job starts.</summary>
    public event EventHandler<JobEventArgs> JobStarted;

    /// <summary>Raised when this document's job ends.</summary>
    public event EventHandler<JobEventArgs> JobFinished;

    /// <summary>Gets the document.</summary>
    public EditorDocument Document => Owner;

    /// <summary>Gets the document's last job, or null.</summary>
    public EngraveJob Job => _job;

    /// <summary>Gets whether a job is running for this document.</summary>
    public bool IsRunning => _job is { IsRunning: true, IsAborted: false };

    /// <summary>Gets the manager for a document, creating it on first use.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The manager.</returns>
    public static JobManager For(EditorDocument document)
        => Instance(document, owner => new JobManager(owner));

    /// <summary>Gets the job running for a document, or null.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The job, or null.</returns>
    public static EngraveJob JobFor(EditorDocument document)
        => document == null ? null : For(document).Job;

    /// <summary>Answers whether a job is running for a document.</summary>
    /// <param name="document">The document.</param>
    /// <returns>Whether one is running.</returns>
    public static bool IsRunningFor(EditorDocument document)
        => document != null && For(document).IsRunning;

    /// <summary>Starts a job on this document's behalf.</summary>
    /// <param name="job">The job.</param>
    /// <remarks>Does nothing when a job is already running — the caller aborts
    /// that one first if it means to replace it.</remarks>
    public void StartJob(EngraveJob job)
    {
        if (job == null || IsRunning) { return; }

        _job = job;
        job.Done += OnJobDone;

        //Announce BEFORE the work begins, so a log connected by the
        //announcement still sees the job's very first message.
        JobEventArgs arguments = new JobEventArgs(Document, job);
        JobStarted?.Invoke(this, arguments);
        AnyJobStarted?.Invoke(null, arguments);

        _ = job.StartAsync();
    }

    private void OnJobDone(object sender, bool success)
    {
        EngraveJob job = sender as EngraveJob;
        if (job != null) { job.Done -= OnJobDone; }

        JobEventArgs arguments = new JobEventArgs(Document, job ?? _job, success);
        JobFinished?.Invoke(this, arguments);
        AnyJobFinished?.Invoke(null, arguments);
    }
}
