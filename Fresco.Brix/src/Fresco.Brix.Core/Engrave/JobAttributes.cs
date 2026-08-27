// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;

namespace Fresco.Brix.Engrave; //was previously: frescobaldi/job/attributes.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// What the application knows about a job that the job itself has no business
/// knowing: chiefly whether it is a background job the user never asked for.
/// </summary>
/// <remarks>
/// Kept beside the job rather than on it, and collected with it, because a job
/// is passed around by the queue and the log and must not grow a reference to
/// the window it was started from.
/// </remarks>
public sealed class JobAttributes : Plugin<EngraveJob, JobAttributes>
{
    private JobAttributes(EngraveJob job)
        : base(job)
    {
    }

    /// <summary>Gets the job.</summary>
    public EngraveJob Job => Owner;

    /// <summary>
    /// Gets or sets whether the job runs out of the user's sight — an
    /// automatic engrave they did not ask for.
    /// </summary>
    /// <remarks>A hidden job does not raise the log, does not disable the
    /// engrave commands, and shows only a thin progress bar.</remarks>
    public bool Hidden { get; set; }

    /// <summary>Gets the attributes of a job, creating them on first use.</summary>
    /// <param name="job">The job.</param>
    /// <returns>The attributes.</returns>
    public static JobAttributes For(EngraveJob job)
        => Instance(job, owner => new JobAttributes(owner));
}
