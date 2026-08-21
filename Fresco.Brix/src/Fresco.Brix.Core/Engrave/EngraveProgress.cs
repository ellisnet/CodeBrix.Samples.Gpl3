// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using System;
using System.Globalization;

namespace Fresco.Brix.Engrave; //was previously: frescobaldi/progress.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// How far along a running engrave is, estimated from how long the last one
/// took.
/// </summary>
/// <remarks>
/// <para>
/// There is nothing to measure: the engine reports progress in words, not in
/// percentages. So this does what upstream does — it times the previous run of
/// the same document, remembers that, and moves a bar across at that speed,
/// holding just short of the end until the run actually finishes. The estimate
/// is stored per document with the rest of what the application remembers
/// about it, so the second time a user engraves a score the bar is honest.
/// </para>
/// <para>
/// A document that has never been engraved gets upstream's very arbitrary
/// guess: three seconds plus a twentieth of a second per line.
/// </para>
/// </remarks>
public sealed class EngraveProgress
{
    /// <summary>The remembered value's name.</summary>
    public const string BuildTimeName = "buildtime";

    private DateTime _startedAt;
    private double _expectedSeconds;

    /// <summary>Declares the remembered build time.</summary>
    /// <remarks>Call once at startup: a value that is not declared is a value
    /// that is not stored.</remarks>
    public static void Define() => MetaInfo.Define(BuildTimeName, "0");

    /// <summary>Raised when the shown fraction changes.</summary>
    public event EventHandler Changed;

    /// <summary>Gets whether a run is being tracked.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Gets whether the bar should be thin and quiet, because the run is one
    /// the user never asked for.
    /// </summary>
    public bool IsHidden { get; private set; }

    /// <summary>Gets whether the last tracked run finished successfully.</summary>
    public bool ShowFinished { get; private set; }

    /// <summary>Gets how far along the run is, from 0 to 1.</summary>
    public double Fraction
    {
        get
        {
            if (!IsRunning || _expectedSeconds <= 0) { return 0.0; }

            double elapsed = (DateTime.UtcNow - _startedAt).TotalSeconds;

            //Never quite arrives: a bar that sits at 100% while the run
            //continues is worse than one that sits at 99%.
            return Math.Min(0.99, elapsed / _expectedSeconds);
        }
    }

    /// <summary>Gets the text shown on the bar.</summary>
    public string Text
        => IsRunning
            ? string.Format(
                CultureInfo.InvariantCulture,
                "{0:0}%",
                Fraction * 100)
            : string.Empty;

    /// <summary>Starts tracking a job.</summary>
    /// <param name="document">The document being engraved.</param>
    /// <param name="job">The job.</param>
    /// <param name="lineCount">The document's line count, for the first
    /// estimate.</param>
    /// <param name="metaInfo">Where the estimate is remembered, or null.</param>
    public void Start(
        EditorDocument document, EngraveJob job, int lineCount, MetaInfo metaInfo)
    {
        double remembered = 0.0;
        string stored = metaInfo?.Get(BuildTimeName);
        if (stored != null)
        {
            double.TryParse(
                stored, NumberStyles.Float, CultureInfo.InvariantCulture, out remembered);
        }

        _expectedSeconds = remembered > 0 ? remembered : 3.0 + (lineCount / 20.0);
        _startedAt = job?.StartTime == default ? DateTime.UtcNow : job.StartTime;
        IsRunning = true;
        IsHidden = job != null && JobAttributes.For(job).Hidden;
        ShowFinished = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops tracking, remembering how long the run took.</summary>
    /// <param name="job">The job that ended.</param>
    /// <param name="success">Whether it went well.</param>
    /// <param name="metaInfo">Where the estimate is remembered, or null.</param>
    public void Stop(EngraveJob job, bool success, MetaInfo metaInfo)
    {
        IsRunning = false;
        ShowFinished = success && !(job != null && JobAttributes.For(job).Hidden);

        if (success && job != null && metaInfo != null)
        {
            metaInfo.Set(
                BuildTimeName,
                job.ElapsedTime.TotalSeconds.ToString("R", CultureInfo.InvariantCulture));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
