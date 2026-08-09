/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com>

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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/page-turn-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - Page_turn_event is a class upstream too; it is kept a class here and, as upstream,
//     is copied into and out of vectors by value. Penalize() answers FRESH events rather
//     than editing in place, which is what makes that safe.

/// <summary>
/// Decides where a page turn may fall: at a rest long enough for the player to reach up,
/// and not in the middle of a repeat they would have to turn back through.
/// </summary>
public class PageTurnEngraver : Engraver
{
    private static readonly Symbol AllowSymbol = Symbol.Intern("allow");
    private static readonly Symbol ForceSymbol = Symbol.Intern("force");
    private static readonly Symbol PageTurnEventSymbol = Symbol.Intern("page-turn-event");
    private static readonly Symbol BreakPermissionSymbol = Symbol.Intern("break-permission");
    private static readonly Symbol BreakPenaltySymbol = Symbol.Intern("break-penalty");
    private static readonly Symbol PageTurnPermissionSymbol
        = Symbol.Intern("page-turn-permission");

    private static readonly Symbol PageTurnPenaltySymbol = Symbol.Intern("page-turn-penalty");
    private static readonly Symbol GlyphSymbol = Symbol.Intern("glyph");
    private static readonly Symbol DurationSymbol = Symbol.Intern("duration");
    private static readonly Symbol StartRepeatSymbol = Symbol.Intern("start-repeat");
    private static readonly Symbol EndRepeatSymbol = Symbol.Intern("end-repeat");

    private Moment _restBegin = new Moment(new Rational(0));
    private Moment _repeatBegin = new Moment(new Rational(-1));
    private Moment _noteEnd = new Moment(new Rational(0));
    private Rational _repeatBeginRestLength = new Rational(0);
    private bool _foundSpecialBarLine;

    private readonly List<PageTurnEvent> _forcedBreaks = new List<PageTurnEvent>();
    private readonly List<PageTurnEvent> _automaticBreaks = new List<PageTurnEvent>();
    private readonly List<PageTurnEvent> _repeatPenalties = new List<PageTurnEvent>();

    // These three stay in step with each other: one entry per breakable column.
    private readonly List<Rational> _breakableMoments = new List<Rational>();
    private readonly List<Grob> _breakableColumns = new List<Grob>();
    private readonly List<bool> _specialBarlines = new List<bool>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public PageTurnEngraver(Context context)
        : base(context)
    {
        ListenTo(PageTurnEventSymbol, ListenBreak);
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Page_turn_engraver";

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Records a manual <c>\pageTurn</c>-family instruction.</summary>
    /// <param name="ev">The event.</param>
    private void ListenBreak(StreamEvent ev)
    {
        object permission = ev.GetProperty(BreakPermissionSymbol);
        double penalty = SchemeConvert.ToDouble(ev.GetProperty(BreakPenaltySymbol), 0);
        Rational now = NowMoment.MainPart;

        _forcedBreaks.Add(new PageTurnEvent(now, now, permission, penalty));
    }

    /// <summary>Notices a bar line that is a plausible turning point.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob == null)
        {
            return;
        }

        if (!_foundSpecialBarLine && info.Grob.Name == "BarLine")
        {
            _foundSpecialBarLine = IsBarLineSpecial(AsString(info.Grob.GetProperty(GlyphSymbol)));
        }

        if (info.Grob.Name != "NoteHead")
        {
            return;
        }

        StreamEvent cause = info.EventCause;
        if (cause == null || !(cause.GetProperty(DurationSymbol) is Duration dur))
        {
            return;
        }

        if (_restBegin < NowMoment)
        {
            double pen = Penalty((NowMoment - _restBegin).MainPart);
            if (!double.IsInfinity(pen))
            {
                _automaticBreaks.Add(new PageTurnEvent(
                    _restBegin.MainPart, NowMoment.MainPart, AllowSymbol, 0));
            }
        }

        if (_restBegin <= _repeatBegin)
        {
            _repeatBeginRestLength = (NowMoment - _repeatBegin).MainPart;
        }

        _noteEnd = NowMoment + new Moment(dur.ToWholeNotes());
    }

    /// <summary>
    /// Drops the last column when it turned out not to be breakable.
    /// <para>
    /// Breakability is not known when the column is collected: <c>Paper_column_engraver</c>
    /// marks it in ITS stop-translation-timestep, which may run after this one. So every
    /// column is taken and the non-breakable ones are removed at the start of the
    /// following timestep, which is the earliest moment the answer is trustworthy.
    /// </para>
    /// </summary>
    public override void StartTranslationTimestep()
    {
        if (_breakableColumns.Count != 0
            && !PaperColumn.IsBreakable(_breakableColumns[_breakableColumns.Count - 1]))
        {
            _breakableColumns.RemoveAt(_breakableColumns.Count - 1);
            _breakableMoments.RemoveAt(_breakableMoments.Count - 1);
            _specialBarlines.RemoveAt(_specialBarlines.Count - 1);
        }
    }

    /// <summary>Collects this timestep's column and tracks repeat boundaries.</summary>
    public override void StopTranslationTimestep()
    {
        if (GetProperty("currentCommandColumn") is Grob pc)
        {
            // In a context below the one that engraves bar lines — a Voice — no bar line
            // is ever acknowledged, but one made above is reachable through
            // currentBarLine.
            if (!_foundSpecialBarLine && GetProperty("currentBarLine") is Item bar)
            {
                _foundSpecialBarLine = IsBarLineSpecial(AsString(bar.GetProperty(GlyphSymbol)));
            }

            _breakableColumns.Add(pc);
            _breakableMoments.Add(NowMoment.MainPart);
            _specialBarlines.Add(_foundSpecialBarLine);
        }

        bool start = false;
        bool end = false;

        foreach (object entry in Pair.ToList(GetProperty("repeatCommands")))
        {
            object command = entry;
            object options = Nil.Instance;
            if (command is Pair commandPair)
            {
                options = commandPair.Cdr;
                command = commandPair.Car;
            }

            if (ReferenceEquals(command, StartRepeatSymbol))
            {
                long repCount = options is Pair optPair
                    ? SchemeConvert.ToInt(optPair.Car, 2)
                    : 2;
                if (repCount >= 2)
                {
                    start = true;
                }
            }
            else if (ReferenceEquals(command, EndRepeatSymbol))
            {
                long retCount = options is Pair optPair
                    ? SchemeConvert.ToInt(optPair.Car, 1)
                    : 1;
                if (retCount >= 1)
                {
                    end = true;
                }
            }
        }

        if (end && _repeatBegin.MainPart >= new Rational(0))
        {
            Rational now = NowMoment.MainPart;
            double pen = Penalty((NowMoment - _restBegin).MainPart + _repeatBeginRestLength);

            Rational minimumRepeatLength = ToRational(
                GetProperty("pageTurnMinimumRepeatLength"), new Rational(0));
            if (minimumRepeatLength > (NowMoment - _repeatBegin).MainPart)
            {
                pen = double.PositiveInfinity;
            }

            // An impossible turn is recorded with a NULL permission and minus infinity,
            // which is what Penalize reads as "forbid outright" rather than "cost this
            // much".
            _repeatPenalties.Add(double.IsInfinity(pen)
                ? new PageTurnEvent(
                    _repeatBegin.MainPart, now, Nil.Instance, double.NegativeInfinity)
                : new PageTurnEvent(_repeatBegin.MainPart, now, AllowSymbol, pen));

            _repeatBegin = new Moment(new Rational(-1));
        }

        if (start)
        {
            _repeatBegin = NowMoment;
            _repeatBeginRestLength = new Rational(0);
        }

        _restBegin = _noteEnd;

        _foundSpecialBarLine = false;
    }

    /// <summary>
    /// Filters the automatic turns through the repeat penalties and stamps the surviving
    /// permissions onto their columns.
    /// </summary>
    public override void FinalizeTranslation()
    {
        int repIndex = 0;
        List<PageTurnEvent> autoBreaks = new List<PageTurnEvent>();

        for (int i = 0; i < _automaticBreaks.Count; i++)
        {
            PageTurnEvent brk = _automaticBreaks[i];

            for (; repIndex < _repeatPenalties.Count
                && _repeatPenalties[repIndex].End <= brk.Start;
                repIndex++)
            {
            }

            if (repIndex >= _repeatPenalties.Count
                || brk.End <= _repeatPenalties[repIndex].Start)
            {
                autoBreaks.Add(brk);
            }
            else
            {
                List<PageTurnEvent> split = brk.Penalize(_repeatPenalties[repIndex]);

                // The last of the freshly split events may overlap the NEXT penalty, in
                // which case it goes back through the loop rather than out.
                if (repIndex + 1 < _repeatPenalties.Count && split.Count != 0
                    && split[split.Count - 1].End > _repeatPenalties[repIndex + 1].Start)
                {
                    _automaticBreaks[i] = split[split.Count - 1];
                    split.RemoveAt(split.Count - 1);
                    i--;
                }

                autoBreaks.AddRange(split);
            }
        }

        for (int i = 0; i < autoBreaks.Count; i++)
        {
            PageTurnEvent brk = autoBreaks[i];
            Grob pc = BreakableColumn(brk);
            if (pc != null)
            {
                object perm = MaxPermission(pc.GetProperty(PageTurnPermissionSymbol), brk.Permission);
                double pen = Math.Min(
                    SchemeConvert.ToDouble(
                        pc.GetProperty(PageTurnPenaltySymbol), double.PositiveInfinity),
                    brk.Penalty);
                pc.SetProperty(PageTurnPermissionSymbol, perm);
                pc.SetProperty(PageTurnPenaltySymbol, pen);
            }
        }

        // Unless a manual break says otherwise, a turn is always allowed at the very end.
        if (_breakableColumns.Count != 0)
        {
            _breakableColumns[_breakableColumns.Count - 1]
                .SetProperty(PageTurnPermissionSymbol, AllowSymbol);
        }

        for (int i = 0; i < _forcedBreaks.Count; i++)
        {
            PageTurnEvent brk = _forcedBreaks[i];
            Grob pc = BreakableColumn(brk);
            if (pc != null)
            {
                pc.SetProperty(PageTurnPermissionSymbol, brk.Permission);
                pc.SetProperty(PageTurnPenaltySymbol, brk.Penalty);
            }
        }
    }

    /// <summary>
    /// Finds the column a turn should be pinned to: the LAST special bar line inside the
    /// event's span, or failing that the last breakable column in it.
    /// <para>A special bar line is preferred because a turn reads better where the music
    /// already has a visible seam.</para>
    /// </summary>
    private Grob BreakableColumn(PageTurnEvent brk)
    {
        int start = LowerBound(_breakableMoments, brk.Start);
        int end = UpperBound(_breakableMoments, brk.End);

        if (start == _breakableMoments.Count)
        {
            return null;
        }

        if (end == 0)
        {
            return null;
        }

        int endIdx = end - 1;

        for (int i = endIdx; i >= start; i--)
        {
            if (_specialBarlines[i])
            {
                return _breakableColumns[i];
            }
        }

        return _breakableColumns[endIdx];
    }

    /// <summary>
    /// What a turn over a rest of the given length costs: nothing when the rest is long
    /// enough for the player, and INFINITY when it is not.
    /// </summary>
    private double Penalty(Rational restLen)
    {
        Rational minTurn = ToRational(
            GetProperty("pageTurnMinimumRestLength"), new Rational(1));

        return restLen < minTurn ? double.PositiveInfinity : 0;
    }

    /// <summary>
    /// A bar line worth turning at: anything other than none at all and the ordinary
    /// single line.
    /// </summary>
    private static bool IsBarLineSpecial(string glyph)
        => !string.IsNullOrEmpty(glyph) && glyph != "|";

    /// <summary>
    /// The MOST permissive of two permissions, where <c>force</c> beats <c>allow</c> and
    /// the empty list means nothing has been said yet.
    /// </summary>
    private static object MaxPermission(object perm1, object perm2)
    {
        if (perm1 is Nil)
        {
            return perm2;
        }

        if (ReferenceEquals(perm1, AllowSymbol) && ReferenceEquals(perm2, ForceSymbol))
        {
            return perm2;
        }

        return perm1;
    }

    private static string AsString(object value)
        => value is MutableString text ? text.ToString() : value as string ?? string.Empty;

    private static Rational ToRational(object value, Rational fallback)
        => SchemeConvert.TryToRational(value, out Rational result) ? result : fallback;

    /// <summary>The first index whose moment is NOT LESS than the target.</summary>
    private static int LowerBound(List<Rational> values, Rational target)
    {
        int i = 0;
        while (i < values.Count && values[i] < target)
        {
            i++;
        }

        return i;
    }

    /// <summary>The first index whose moment is GREATER than the target.</summary>
    private static int UpperBound(List<Rational> values, Rational target)
    {
        int i = 0;
        while (i < values.Count && !(target < values[i]))
        {
            i++;
        }

        return i;
    }

    /// <summary>
    /// A stretch of music over which a page turn carries one permission and one penalty.
    /// </summary>
    private sealed class PageTurnEvent
    {
        /// <summary>Initializes an event over a span.</summary>
        public PageTurnEvent(Rational start, Rational end, object permission, double penalty)
        {
            Start = start;
            End = end;
            Permission = permission;
            Penalty = penalty;
        }

        /// <summary>Gets where the span begins.</summary>
        public Rational Start { get; }

        /// <summary>Gets where the span ends.</summary>
        public Rational End { get; }

        /// <summary>Gets the permission over the span.</summary>
        public object Permission { get; }

        /// <summary>Gets what a turn in the span costs.</summary>
        public double Penalty { get; }

        /// <summary>
        /// Re-penalizes this event against an overlapping one, splitting it into as many as
        /// THREE pieces.
        /// <para>
        /// This is what happens when a turn looks fine until a volta repeat is found that
        /// the player would have to turn back through: the part before the repeat keeps its
        /// own penalty, the overlap takes the worse of the two, and the part after keeps
        /// its own again. A penalty with a NULL permission drops the overlap entirely
        /// rather than repricing it.
        /// </para>
        /// </summary>
        /// <param name="penalty">The overlapping penalty.</param>
        /// <returns>The pieces this event becomes.</returns>
        public List<PageTurnEvent> Penalize(PageTurnEvent penalty)
        {
            Rational interStart = Start > penalty.Start ? Start : penalty.Start;
            Rational interEnd = End < penalty.End ? End : penalty.End;
            List<PageTurnEvent> ret = new List<PageTurnEvent>();

            if (!(interStart < interEnd))
            {
                ret.Add(this);
                return ret;
            }

            double newPen = Math.Max(Penalty, penalty.Penalty);

            if (Start < penalty.Start)
            {
                ret.Add(new PageTurnEvent(Start, penalty.Start, Permission, Penalty));
            }

            if (!(penalty.Permission is Nil))
            {
                ret.Add(new PageTurnEvent(interStart, interEnd, Permission, newPen));
            }

            if (penalty.End < End)
            {
                ret.Add(new PageTurnEvent(penalty.End, End, Permission, Penalty));
            }

            return ret;
        }
    }
}
