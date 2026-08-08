/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/fine-iterator.cc, lily/premeasure-iterator.cc, lily/measure-remainder-iterator.cc;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// The iterator for <c>\fine</c>: it reports the music as an event once, unless it sits
/// inside <c>LyricCombineMusic</c>.
/// <para>
/// Upstream notes that deriving this from <c>Event_iterator</c> would be the obvious
/// move and declines, because there are conditions under which the event is NOT sent
/// and complicating <c>Event_iterator</c> for them is not worth it.
/// </para>
/// </summary>
public sealed class FineIterator : SimpleMusicIterator
{
    private static readonly Symbol LyricCombineMusicSymbol = Symbol.Intern("lyric-combine-music");
    private static readonly Symbol FoldedRepeatedMusicSymbol = Symbol.Intern("folded-repeated-music");
    private static readonly Symbol FineFoldedSymbol = Symbol.Intern("fine-folded");

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Fine_iterator";

    /// <summary>Reports the <c>\fine</c>, then behaves as simple music.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        if (!HasStarted)
        {
            // Ignore \fine inside LyricCombineMusic: the way Lyric_combine_music_iterator
            // drives processing tends to place things at the wrong point in time.
            bool timingIsAccurate = FindAboveByMusicType(LyricCombineMusicSymbol) == null;

            if (timingIsAccurate)
            {
                MusicObject clone = Music.Clone();
                bool folded = FindAboveByMusicType(FoldedRepeatedMusicSymbol) != null;
                clone.SetProperty(FineFoldedSymbol, folded);
                ReportEvent(clone);
            }
        }

        base.Process(until);
    }

    /// <summary>Descends to the bottom context before the base class sets up.</summary>
    protected override void CreateContexts()
    {
        DescendToBottomContext();
        base.CreateContexts();
    }
}

/// <summary>
/// The iterator for <c>\premeasure music</c>, which is effectively
/// <c>{ \initialContextFrom music \partial (length of music) music | }</c>.
/// <para>
/// Upstream implements this as an iterator rather than as music syntax so that the
/// duration is computed AFTER things like <c>\removeWithTag</c> have had their chance
/// to change it.
/// </para>
/// </summary>
public sealed class PremeasureIterator : MusicWrapperIterator
{
    private static readonly Symbol PartialEventSymbol = Symbol.Intern("PartialEvent");
    private static readonly Symbol BarCheckEventSymbol = Symbol.Intern("BarCheckEvent");
    private static readonly Symbol DurationSymbol = Symbol.Intern("duration");

    private bool _started;
    private bool _stopped;

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Premeasure_iterator";

    /// <summary>Brackets the wrapped music with the partial and the bar check.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        if (!_started)
        {
            _started = true;
            SendPartialEvent();
        }

        base.Process(until);

        if (_started && !_stopped && until == MusicLength)
        {
            _stopped = true;
            SendCheckEvent();
        }
    }

    private void SendPartialEvent()
    {
        // Upstream builds a MUSIC object through Scheme's make-music and sends THAT, so
        // the event class and the `types' ancestry come from define-music-types.scm.
        // Constructing a Stream_event directly would skip that and produce an event no
        // engraver's listener matches.
        MusicObject partial = MusicFactory.MakeMusic(PartialEventSymbol);
        partial.SetSpot(Music.Origin);
        partial.SetProperty(DurationSymbol, Duration.FromWholeNotes(MusicLength.MainPart, true));
        partial.SendToContext(Context);
    }

    private void SendCheckEvent()
    {
        MusicObject check = MusicFactory.MakeMusic(BarCheckEventSymbol);
        check.SetSpot(Music.Origin);
        check.SendToContext(Context);
    }
}

/// <summary>
/// The iterator for <c>\measureRemainder music</c>, which is effectively
/// <c>{ \initialContextFrom music \setMeasureLengthFromHere music | \setDefaultMeasureLength }</c>.
/// </summary>
public sealed class MeasureRemainderIterator : MusicWrapperIterator
{
    private static readonly Symbol MeasureLengthChangeEventSymbol
        = Symbol.Intern("MeasureLengthChangeEvent");

    private static readonly Symbol BarCheckEventSymbol = Symbol.Intern("BarCheckEvent");
    private static readonly Symbol DurationSymbol = Symbol.Intern("duration");

    private Context _eventHandler;
    private bool _started;
    private bool _stopped;

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Measure_remainder_iterator";

    /// <summary>Brackets the wrapped music with the two measure-length changes.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        if (!_started)
        {
            _started = true;
            _eventHandler = Context;
            SendChangeEvent(Direction.Negative);
        }

        base.Process(until);

        if (_started && !_stopped && until == MusicLength)
        {
            _stopped = true;
            if (_eventHandler != null)
            {
                SendCheckEvent();
                SendChangeEvent(Direction.Positive);
                _eventHandler = null;
            }
        }
    }

    private void SendChangeEvent(Direction direction)
    {
        if (direction == Direction.Negative && !MusicLength.MainPart.IsNonZero)
        {
            // After iterating the wrapped music we are still at the same main moment, and
            // measureLength is then restored to the time signature's value — so there is
            // nothing to change now. Skipping this also avoids warning at a measure
            // boundary, where it would try to set measureLength = 0, which is invalid.
            return;
        }

        StreamEvent change = Context.MakeEvent(MeasureLengthChangeEventSymbol, Music.Origin);
        if (direction == Direction.Negative)
        {
            change.SetProperty(DurationSymbol, Duration.FromWholeNotes(MusicLength.MainPart, true));
        }

        _eventHandler.SendStreamEvent(change);
    }

    private void SendCheckEvent()
        => _eventHandler.SendStreamEvent(Context.MakeEvent(BarCheckEventSymbol, Music.Origin));
}
