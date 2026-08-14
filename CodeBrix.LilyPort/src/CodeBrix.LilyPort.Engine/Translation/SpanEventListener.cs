/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2022--2026 Daniel Eble <nine.fierce.ballads@gmail.com>

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

using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/include/span-event-listener.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - upstream is a HEADER-ONLY file, so it has no row in lily-cc-ledger.tsv and rides
//     with whichever group first needs it (scripts/dynamics: Text_spanner_engraver and
//     Dynamic_engraver).
//   - upstream's `once` is a template parameter, which exists only to make the choice
//     free at run time; a bool field costs one branch per event and reads the same.

/// <summary>
/// Records the pair of events a span is delimited by, one for each
/// <c>span-direction</c>.
/// </summary>
public class SpanEventListener
{
    private static readonly Symbol SpanDirectionSymbol = Symbol.Intern("span-direction");

    private readonly bool _once;
    private DrulArray<StreamEvent> _events = new DrulArray<StreamEvent>(null, null);

    /// <summary>Initializes a listener.</summary>
    /// <param name="once">
    /// Whether a second event on one side is a warning rather than a replacement.
    /// </param>
    protected SpanEventListener(bool once) => _once = once;

    /// <summary>Gets the START event, or <see langword="null"/>.</summary>
    public StreamEvent Start => _events[Direction.Negative];

    /// <summary>Gets the STOP event, or <see langword="null"/>.</summary>
    public StreamEvent Stop => _events[Direction.Positive];

    /// <summary>Gets either event, preferring the stop event.</summary>
    public StreamEvent StopOrStart => Stop ?? Start;

    /// <summary>Forgets everything.</summary>
    public void Reset() => _events = new DrulArray<StreamEvent>(null, null);

    /// <summary>Records one event on the side its <c>span-direction</c> names.</summary>
    /// <param name="ev">The event.</param>
    public void Listen(StreamEvent ev)
    {
        Direction d = DirectionalElementInterface.FromScheme(
            ev.GetProperty(SpanDirectionSymbol), Direction.Center);
        if (d != Direction.Center)
        {
            if (_once)
            {
                StreamEvent existing = _events[d];
                StreamEvent.AssignEventOnce(ref existing, ev);
                _events[d] = existing;
            }
            else
            {
                _events[d] = ev;
            }
        }
        else
        {
            TranslatorSchemeHelpers.EventProgrammingError(ev, "event span-direction is not set");
        }
    }
}

/// <summary>
/// Records the FIRST start event and the FIRST stop event, warning when incompatible
/// events arrive.
/// </summary>
public sealed class UniqueSpanEventListener : SpanEventListener
{
    /// <summary>Initializes a listener.</summary>
    public UniqueSpanEventListener()
        : base(true)
    {
    }
}

/// <summary>Records the MOST RECENT start and stop events.</summary>
public sealed class LastSpanEventListener : SpanEventListener
{
    /// <summary>Initializes a listener.</summary>
    public LastSpanEventListener()
        : base(false)
    {
    }
}
