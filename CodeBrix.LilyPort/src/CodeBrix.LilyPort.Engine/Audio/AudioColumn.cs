/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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

namespace CodeBrix.LilyPort.Engine.Audio; //was previously: lily/audio-column.cc, lily/include/audio-column.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - OffsetWhen is upstream's protected member, friended to Score_performer alone. C#
//     has no `friend', so it is internal: the assembly boundary is the narrowest fence
//     the language offers, and ScorePerformer is still the only caller.

/// <summary>
/// Everything that sounds at one moment — the MIDI path's answer to a paper column.
/// </summary>
public sealed class AudioColumn : AudioElement
{
    private readonly List<AudioItem> _audioItems = new List<AudioItem>();

    /// <summary>Initializes a column at a moment.</summary>
    /// <param name="when">The moment this column stands at.</param>
    public AudioColumn(Moment when) => WhenMoment = when;

    /// <summary>Gets the items standing in this column.</summary>
    public IReadOnlyList<AudioItem> AudioItems => _audioItems;

    /// <summary>Gets the moment this column stands at.</summary>
    public Moment WhenMoment { get; private set; }

    /// <summary>Gets the C++ class name this element corresponds to.</summary>
    public override string ClassName => "Audio_column";

    /// <summary>Returns the moment this column stands at.</summary>
    /// <returns>The moment.</returns>
    public Moment When() => WhenMoment;

    /// <summary>Returns this column's moment in MIDI ticks.</summary>
    /// <returns>The tick count.</returns>
    public int Ticks() => AudioMoment.ToTicks(WhenMoment);

    /// <summary>Adds an item to this column and points the item back at it.</summary>
    /// <param name="item">The item to add.</param>
    public void AddAudioItem(AudioItem item)
    {
        _audioItems.Add(item);
        item.AudioColumn = this;
    }

    /// <summary>
    /// Shifts this column in time, which is how <c>skipTypesetting</c> removes the
    /// skipped stretch from the performance.
    /// </summary>
    /// <param name="delta">How far to shift.</param>
    internal void OffsetWhen(Moment delta) => WhenMoment += delta;
}
