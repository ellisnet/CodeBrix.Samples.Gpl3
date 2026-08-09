/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2007--2026 Han-Wen Nienhuys <hanwen@lilypond.org>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/page-spacing-result.cc, lily/include/page-spacing-result.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port.

/// <summary>
/// Whether a page's system count satisfies <c>min-systems-per-page</c> and
/// <c>max-systems-per-page</c>.
/// <para>
/// This is a BITFIELD, and upstream says why in a comment worth keeping: one status value
/// stands for several pages at once, so a solution can be simultaneously too many on one
/// page and too few on another. Modelling it as three mutually exclusive states would
/// compile, read better, and lose the case the flags exist for.
/// </para>
/// </summary>
[Flags]
public enum SystemCountStatus
{
    /// <summary>Every page's system count is within bounds.</summary>
    Ok = 0,

    /// <summary>Some page carries more systems than <c>max-systems-per-page</c> allows.</summary>
    TooMany = 1,

    /// <summary>Some page carries fewer systems than <c>min-systems-per-page</c> wants.</summary>
    TooFew = 2,
}

/// <summary>
/// What a page-spacing attempt produced: how many systems went on each page, the spacing
/// force each page ended up at, and what the whole arrangement is judged to cost.
/// </summary>
public sealed class PageSpacingResult
{
    /// <summary>
    /// Initializes an empty result. <see cref="Demerits"/> starts at INFINITY, not at zero —
    /// an untouched result must lose every comparison against a real one, and
    /// <c>Page_spacer::solve</c> relies on that when it looks for a salvageable subproblem.
    /// </summary>
    public PageSpacingResult()
    {
        Penalty = 0;
        Demerits = double.PositiveInfinity;
        SystemCountStatus = SystemCountStatus.Ok;
    }

    /// <summary>Gets how many systems go on each page, in page order.</summary>
    public List<int> SystemsPerPage { get; } = new List<int>();

    /// <summary>Gets the spacing force each page ended up at, in page order.</summary>
    public List<double> Force { get; } = new List<double>();

    /// <summary>Gets or sets the accumulated page and turn penalties.</summary>
    public double Penalty { get; set; }

    /// <summary>Gets or sets what this arrangement costs, all in.</summary>
    public double Demerits { get; set; }

    /// <summary>Gets or sets whether the per-page system counts are within bounds.</summary>
    public SystemCountStatus SystemCountStatus { get; set; }

    /// <summary>Gets how many pages this result covers.</summary>
    /// <returns>The page count.</returns>
    public int PageCount() => SystemsPerPage.Count;

    /// <summary>
    /// Gets the mean of the per-page forces.
    /// <para>Upstream divides by the page count without guarding it, so an empty result
    /// answers NaN there; the port answers NaN too rather than a tidier zero, because a
    /// caller distinguishing "no pages" from "perfectly spaced" must not be handed the
    /// latter.</para>
    /// </summary>
    /// <returns>The average force.</returns>
    public double AverageForce()
    {
        double averageForce = 0;
        for (int i = 0; i < PageCount(); i++)
        {
            averageForce += Force[i];
        }

        return averageForce / PageCount();
    }
}
