/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/forbid-break-engraver.cc, lily/spanner-break-forbid-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - Two upstream files in one .cs file. They are the two halves of the same job — saying
//     "not here" to the line breaker — they are eleven and twenty lines of body between
//     them, and they were the two most demanded unported translators in the sweep (4,150
//     and 4,156 warnings each). The `was previously' line names both.

/// <summary>
/// Forbids a line break while a note is still sounding across the moment.
/// <para>
/// A line cannot begin in the middle of a note. This engraver reads <c>busyGrobs</c> —
/// the queue the <c>Grob_pq_engraver</c> maintains — skips past everything that ends
/// exactly NOW, and if anything rhythmic is still running past that point, sets
/// <c>forbidBreak</c> on the Score context.
/// </para>
/// <para>
/// Upstream's own note is kept: checking for running note heads "should probably be done
/// elsewhere".
/// </para>
/// </summary>
public class ForbidLineBreakEngraver : Engraver
{
    private static readonly Symbol BusyGrobsSymbol = Symbol.Intern("busyGrobs");
    private static readonly Symbol ForbidBreakSymbol = Symbol.Intern("forbidBreak");
    private static readonly Symbol RhythmicGrobInterfaceSymbol
        = Symbol.Intern("rhythmic-grob-interface");

    private static readonly Symbol ScoreSymbol = Symbol.Intern("Score");

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public ForbidLineBreakEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Forbid_line_break_engraver";

    /// <summary>Forbids a break when a rhythmic grob is still sounding.</summary>
    public override void PreProcessMusic()
    {
        /*
          Check for running note heads. This should probably be done elsewhere.
        */
        object busy = GetProperty(BusyGrobsSymbol);

        Moment now = NowMoment;
        while (busy is Pair pair
               && pair.Car is Pair entry
               && entry.Car is Moment when
               && when.MainPart == now.MainPart)
        {
            busy = pair.Cdr;
        }

        while (busy is Pair pair)
        {
            if (pair.Car is Pair entry && entry.Cdr is Grob g
                && g.HasInterface(RhythmicGrobInterfaceSymbol))
            {
                FindScoreContext()?.SetProperty(ForbidBreakSymbol, true);
            }

            busy = pair.Cdr;
        }
    }

    private Context FindScoreContext()
    {
        Context score = Context?.FindContextAbove(ScoreSymbol);
        if (score == null)
        {
            Warn.ProgrammingError("no score context");
        }

        return score;
    }
}

/// <summary>
/// Forbids a line break inside a spanner that says it may not be broken.
/// <para>
/// A spanner announces itself as an <c>unbreakable-spanner</c>, and unless its
/// <c>breakable</c> property says otherwise it goes on a running list. While anything on
/// that list is still live, <c>forbidBreak</c> is set. Glissandi and some brackets rely on
/// this.
/// </para>
/// <para>
/// The list is walked from the BACK and popped as it goes, so entries whose spanners have
/// since been killed are discarded rather than re-tested forever.
/// </para>
/// </summary>
public class SpannerBreakForbidEngraver : Engraver
{
    private static readonly Symbol ForbidBreakSymbol = Symbol.Intern("forbidBreak");
    private static readonly Symbol BreakableSymbol = Symbol.Intern("breakable");
    private static readonly Symbol UnbreakableSpannerInterfaceSymbol
        = Symbol.Intern("unbreakable-spanner-interface");

    private static readonly Symbol ScoreSymbol = Symbol.Intern("Score");

    private readonly List<Spanner> _runningSpanners = new List<Spanner>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public SpannerBreakForbidEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Spanner_break_forbid_engraver";

    /// <summary>Forbids a break while an unbreakable spanner is running.</summary>
    public override void PreProcessMusic()
    {
        while (_runningSpanners.Count > 0)
        {
            if (_runningSpanners[_runningSpanners.Count - 1].IsLive)
            {
                FindScoreContext()?.SetProperty(ForbidBreakSymbol, true);
                break;
            }

            _runningSpanners.RemoveAt(_runningSpanners.Count - 1);
        }
    }

    /// <summary>Starts tracking an unbreakable spanner.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob is Spanner sp
            && sp.HasInterface(UnbreakableSpannerInterfaceSymbol)
            && !SchemeUtilities.ToBool(sp.GetProperty(BreakableSymbol)))
        {
            _runningSpanners.Add(sp);
        }
    }

    /// <summary>Stops tracking a spanner that has ended.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeEndGrob(GrobInfo info)
    {
        if (info.Grob is Spanner sp && sp.HasInterface(UnbreakableSpannerInterfaceSymbol))
        {
            _runningSpanners.Remove(sp);
        }
    }

    private Context FindScoreContext()
    {
        Context score = Context?.FindContextAbove(ScoreSymbol);
        if (score == null)
        {
            Warn.ProgrammingError("no score context");
        }

        return score;
    }
}
