// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Engrave; //was previously: frescobaldi/job/queue.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Raised when a runner that is already busy is asked to start.</summary>
public sealed class RunnerBusyException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public RunnerBusyException()
        : base(I18n.Get("Job is already running. Wait for completion."))
    {
    }
}

/// <summary>Raised when a queue operation does not fit the queue's state.</summary>
public sealed class JobQueueStateException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong.</param>
    public JobQueueStateException(string message)
        : base(message)
    {
    }
}

/// <summary>Where a queue is in its life.</summary>
public enum QueueStatus
{
    /// <summary>Created, not started.</summary>
    Inactive,

    /// <summary>Running, with jobs still waiting.</summary>
    Started,

    /// <summary>Running jobs finish; no new ones start.</summary>
    Paused,

    /// <summary>Running, with nothing left waiting.</summary>
    Empty,

    /// <summary>Nothing waiting and nothing running.</summary>
    Idle,

    /// <summary>Done, and not accepting more.</summary>
    Finished,

    /// <summary>Stopped early.</summary>
    Aborted,
}

/// <summary>Whether a queue waits for more work or ends when it runs out.</summary>
public enum QueueMode
{
    /// <summary>The queue goes idle when empty and waits for more.</summary>
    Continuous,

    /// <summary>The queue finishes when it runs out.</summary>
    Single,
}

/// <summary>The order jobs come out of a queue in.</summary>
public interface IJobStore
{
    /// <summary>Gets how many jobs are waiting.</summary>
    int Count { get; }

    /// <summary>Gets whether nothing is waiting.</summary>
    bool IsEmpty => Count == 0;

    /// <summary>Adds a job.</summary>
    /// <param name="job">The job.</param>
    void Push(EngraveJob job);

    /// <summary>Removes and returns the next job.</summary>
    /// <returns>The job.</returns>
    EngraveJob Pop();

    /// <summary>Removes every waiting job.</summary>
    void Clear();
}

/// <summary>Jobs come out in the order they went in.</summary>
public sealed class FifoJobStore : IJobStore
{
    private readonly LinkedList<EngraveJob> _jobs = new LinkedList<EngraveJob>();

    /// <inheritdoc/>
    public int Count => _jobs.Count;

    /// <inheritdoc/>
    public void Push(EngraveJob job) => _jobs.AddFirst(job);

    /// <inheritdoc/>
    public EngraveJob Pop()
    {
        EngraveJob job = _jobs.Last.Value;
        _jobs.RemoveLast();
        return job;
    }

    /// <inheritdoc/>
    public void Clear() => _jobs.Clear();
}

/// <summary>The most recently added job comes out first.</summary>
public sealed class StackJobStore : IJobStore
{
    private readonly LinkedList<EngraveJob> _jobs = new LinkedList<EngraveJob>();

    /// <inheritdoc/>
    public int Count => _jobs.Count;

    /// <inheritdoc/>
    public void Push(EngraveJob job) => _jobs.AddLast(job);

    /// <inheritdoc/>
    public EngraveJob Pop()
    {
        EngraveJob job = _jobs.Last.Value;
        _jobs.RemoveLast();
        return job;
    }

    /// <inheritdoc/>
    public void Clear() => _jobs.Clear();
}

/// <summary>
/// The job with the lowest <see cref="EngraveJob.Priority"/> comes out first;
/// jobs of equal priority come out in the order they went in.
/// </summary>
public sealed class PriorityJobStore : IJobStore
{
    private readonly PriorityQueue<EngraveJob, (int Priority, long Order)> _jobs
        = new PriorityQueue<EngraveJob, (int, long)>();
    private long _inserted;

    /// <inheritdoc/>
    public int Count => _jobs.Count;

    /// <inheritdoc/>
    public void Push(EngraveJob job) => _jobs.Enqueue(job, (job.Priority, _inserted++));

    /// <inheritdoc/>
    public EngraveJob Pop() => _jobs.Dequeue();

    /// <inheritdoc/>
    public void Clear() => _jobs.Clear();
}

/// <summary>
/// One slot in a queue: it runs a single job at a time and tells the queue
/// when that job is done.
/// </summary>
public sealed class JobRunner
{
    private readonly JobQueue _queue;

    /// <summary>Creates a runner.</summary>
    /// <param name="queue">The queue it belongs to.</param>
    /// <param name="index">Its place in the queue's slots.</param>
    public JobRunner(JobQueue queue, int index)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        Index = index;
    }

    /// <summary>Gets the runner's place in the queue.</summary>
    public int Index { get; }

    /// <summary>Gets how many jobs this runner has completed.</summary>
    public int Completed { get; private set; }

    /// <summary>Gets the job in this slot, or null.</summary>
    public EngraveJob Job { get; private set; }

    /// <summary>Gets whether a job is running in this slot.</summary>
    public bool IsRunning => Job is { IsRunning: true };

    /// <summary>Aborts the running job, if any.</summary>
    public void Abort()
    {
        if (IsRunning) { Job.Abort(); }
    }

    /// <summary>Starts a job in this slot.</summary>
    /// <param name="job">The job.</param>
    /// <param name="force">Whether to abort a running job to make room.</param>
    public void Start(EngraveJob job, bool force = false)
    {
        if (IsRunning)
        {
            if (!force) { throw new RunnerBusyException(); }

            Abort();
        }

        Job = job;
        job.Runner = this;
        job.Done += OnJobDone;
        _ = job.StartAsync();
    }

    private void OnJobDone(object sender, bool success)
    {
        EngraveJob job = Job;
        if (job != null) { job.Done -= OnJobDone; }

        Completed++;
        Job = null;
        _queue.JobCompleted(this, job);
    }
}

/// <summary>
/// A queue of jobs and the slots that run them.
/// </summary>
/// <remarks>
/// <para>
/// The engrave queue has exactly ONE slot, and that is not a simplification:
/// the engine is process-global and serializes every call through one gate, so
/// a second slot would only queue behind the first. The slot machinery is kept
/// because it is what makes "run these in sequence and let each see the
/// previous one's output" a property of the queue rather than of every caller.
/// </para>
/// <para>
/// A continuous queue is always live and goes idle when it runs out; a single
/// queue finishes there instead.
/// </para>
/// </remarks>
public class JobQueue
{
    private readonly IJobStore _store;
    private readonly List<JobRunner> _runners;
    private readonly int? _capacity;
    private QueueMode _mode;

    /// <summary>Creates a queue.</summary>
    /// <param name="store">The order jobs come out in; FIFO by default.</param>
    /// <param name="mode">Whether the queue waits for more work.</param>
    /// <param name="runnerCount">How many jobs may run at once.</param>
    /// <param name="capacity">The most jobs that may wait, or null for no limit.</param>
    public JobQueue(
        IJobStore store = null,
        QueueMode mode = QueueMode.Continuous,
        int runnerCount = 1,
        int? capacity = null)
    {
        _store = store ?? new FifoJobStore();
        _mode = mode;
        _capacity = capacity;
        _runners = Enumerable.Range(0, Math.Max(1, runnerCount))
            .Select(index => new JobRunner(this, index))
            .ToList();

        if (mode == QueueMode.Continuous) { Start(); }
    }

    /// <summary>Raised when the queue starts.</summary>
    public event EventHandler QueueStarted;

    /// <summary>Raised when the queue is paused.</summary>
    public event EventHandler Paused;

    /// <summary>Raised when the queue resumes.</summary>
    public event EventHandler Resumed;

    /// <summary>Raised when the last waiting job is taken.</summary>
    public event EventHandler Emptied;

    /// <summary>Raised when nothing is waiting and nothing is running.</summary>
    public event EventHandler Idle;

    /// <summary>Raised when a single-mode queue has run out.</summary>
    public event EventHandler Finished;

    /// <summary>Raised when the queue is aborted.</summary>
    public event EventHandler Aborted;

    /// <summary>Raised when a job is added.</summary>
    public event EventHandler<EngraveJob> JobAdded;

    /// <summary>Raised when a job is started.</summary>
    public event EventHandler<EngraveJob> JobStarted;

    /// <summary>Raised after a job has ended and the queue has caught up.</summary>
    public event EventHandler<EngraveJob> JobDone;

    /// <summary>Gets where the queue is in its life.</summary>
    public QueueStatus State { get; private set; } = QueueStatus.Inactive;

    /// <summary>Gets or sets whether the queue waits for more work.</summary>
    public QueueMode Mode
    {
        get => _mode;
        set => _mode = value;
    }

    /// <summary>Gets how many jobs are waiting.</summary>
    public int Size => _store.Count;

    /// <summary>Gets whether the queue is holding all it may.</summary>
    public bool IsFull => _capacity.HasValue && _store.Count == _capacity.Value;

    /// <summary>Gets whether the queue is live.</summary>
    public bool IsLive
        => State != QueueStatus.Inactive
            && State != QueueStatus.Finished
            && State != QueueStatus.Aborted;

    /// <summary>Gets whether no slot is busy.</summary>
    public bool IsIdle => _runners.All(runner => !runner.IsRunning);

    /// <summary>Gets the slots.</summary>
    public IReadOnlyList<JobRunner> Runners => _runners;

    /// <summary>Gets how many jobs have been completed.</summary>
    /// <param name="runner">A slot's index, or -1 for the total.</param>
    /// <returns>The count.</returns>
    public int Completed(int runner = -1)
        => runner >= 0
            ? _runners[runner].Completed
            : _runners.Sum(r => r.Completed);

    /// <summary>Adds a job, starting it at once when a slot is free.</summary>
    /// <param name="job">The job.</param>
    public void AddJob(EngraveJob job)
    {
        if (job == null) { throw new ArgumentNullException(nameof(job)); }

        if (IsFull) { throw new JobQueueStateException(I18n.Get("Job Queue full")); }

        if (State is QueueStatus.Finished or QueueStatus.Aborted)
        {
            throw new JobQueueStateException(
                I18n.Get("Can't add job to finished/aborted queue."));
        }

        if (State is QueueStatus.Inactive or QueueStatus.Paused)
        {
            _store.Push(job);
            JobAdded?.Invoke(this, job);
            return;
        }

        JobRunner free = IdleRunner();
        if (free != null)
        {
            JobAdded?.Invoke(this, job);
            free.Start(job);
            JobStarted?.Invoke(this, job);
        }
        else
        {
            _store.Push(job);
            JobAdded?.Invoke(this, job);
        }

        State = _store.Count == 0 ? QueueStatus.Empty : QueueStatus.Started;
    }

    /// <summary>Gets the first free slot, or null.</summary>
    /// <returns>The slot, or null.</returns>
    public JobRunner IdleRunner()
        => _runners.FirstOrDefault(runner => !runner.IsRunning);

    /// <summary>Starts the queue.</summary>
    public void Start()
    {
        if (IsLive)
        {
            throw new JobQueueStateException(I18n.Get("Can't 'start' an active Job Queue."));
        }

        StartInternal();
        QueueStarted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops starting new jobs; running jobs finish.</summary>
    public void Pause()
    {
        if (!IsLive)
        {
            throw new JobQueueStateException(
                I18n.Get("Non-running Job Queue can't be paused."));
        }

        State = QueueStatus.Paused;
        Paused?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts jobs again after a pause.</summary>
    public void Resume()
    {
        if (State != QueueStatus.Paused)
        {
            throw new JobQueueStateException(I18n.Get("Job Queue not paused, can't resume."));
        }

        StartInternal();
        Resumed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops the queue.</summary>
    /// <param name="force">Whether to abort the jobs that are running.</param>
    public void Abort(bool force = true)
    {
        if (State is QueueStatus.Finished or QueueStatus.Aborted)
        {
            throw new JobQueueStateException(I18n.Get("Inactive Job Queue can't be aborted"));
        }

        State = QueueStatus.Aborted;
        _mode = QueueMode.Single;
        _store.Clear();
        if (force)
        {
            foreach (var runner in _runners) { runner.Abort(); }
        }

        Aborted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes the next waiting job.</summary>
    /// <returns>The job.</returns>
    public EngraveJob Pop()
    {
        if (State == QueueStatus.Empty)
        {
            throw new JobQueueStateException("Job Queue is empty.");
        }

        if (State != QueueStatus.Started)
        {
            throw new JobQueueStateException(
                I18n.Get("Can't pop job from non-started Job Queue"));
        }

        EngraveJob job = _store.Pop();
        if (_store.Count == 0)
        {
            State = QueueStatus.Empty;
            Emptied?.Invoke(this, EventArgs.Empty);
        }

        return job;
    }

    /// <summary>Called by a slot when its job has ended.</summary>
    /// <param name="runner">The slot.</param>
    /// <param name="job">The job that ended.</param>
    internal void JobCompleted(JobRunner runner, EngraveJob job)
    {
        if (State == QueueStatus.Started)
        {
            runner.Start(Pop());
        }
        else if (State == QueueStatus.Paused)
        {
            //A single-mode queue that empties while paused is done.
            if (_store.Count == 0 && IsIdle && _mode == QueueMode.Single)
            {
                QueueFinished();
            }
        }
        else if (IsIdle)
        {
            if (_mode == QueueMode.Single)
            {
                QueueFinished();
            }
            else
            {
                State = QueueStatus.Idle;
                Idle?.Invoke(this, EventArgs.Empty);
            }
        }

        JobDone?.Invoke(this, job);
    }

    private void QueueFinished()
    {
        if (State != QueueStatus.Aborted) { State = QueueStatus.Finished; }

        Finished?.Invoke(this, EventArgs.Empty);
    }

    private void StartInternal()
    {
        if (State is QueueStatus.Finished or QueueStatus.Aborted)
        {
            throw new JobQueueStateException(
                I18n.Get("Can't (re)start a finished/aborted Job Queue."));
        }

        if (State == QueueStatus.Started)
        {
            throw new JobQueueStateException(I18n.Get("Queue already started."));
        }

        if (State == QueueStatus.Empty && _mode == QueueMode.Single)
        {
            throw new JobQueueStateException(I18n.Get("Can't start SINGLE-mode empty queue"));
        }

        if (_store.Count == 0)
        {
            State = QueueStatus.Idle;
            return;
        }

        State = QueueStatus.Started;
        foreach (var runner in _runners)
        {
            if (_store.Count == 0) { break; }

            if (!runner.IsRunning) { runner.Start(Pop()); }
        }
    }
}

/// <summary>
/// The application's job queues: one for engraving, one for the background
/// crawling of included files, and one for everything else.
/// </summary>
/// <remarks>Upstream keeps a single global instance; the same is done here,
/// with the instance held by the engrave service rather than by a module.</remarks>
public sealed class GlobalJobQueue
{
    private readonly Dictionary<string, JobQueue> _queues;

    /// <summary>Creates the queues.</summary>
    public GlobalJobQueue()
        => _queues = new Dictionary<string, JobQueue>(StringComparer.Ordinal)
        {
            ["crawl"] = new JobQueue(),
            ["engrave"] = new JobQueue(),
            ["generic"] = new JobQueue(),
        };

    /// <summary>Gets a queue by name.</summary>
    /// <param name="target">The queue name.</param>
    /// <returns>The queue.</returns>
    public JobQueue Queue(string target)
        => _queues.TryGetValue(target ?? string.Empty, out var queue)
            ? queue
            : throw new ArgumentException(
                I18n.Format(I18n.Get("Invalid job queue target: {name}"), ("name", target)),
                nameof(target));

    /// <summary>Adds a job to one of the queues.</summary>
    /// <param name="job">The job.</param>
    /// <param name="target">Which queue.</param>
    public void AddJob(EngraveJob job, string target = "engrave")
        => Queue(target).AddJob(job);
}
