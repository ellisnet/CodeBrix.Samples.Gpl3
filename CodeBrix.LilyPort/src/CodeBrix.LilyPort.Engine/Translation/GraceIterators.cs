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

using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/grace-iterator.cc, lily/grace-music.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The iterator for <c>\grace</c>: it runs the wrapped music in GRACE TIME, which is the
/// second half of a <see cref="Moment"/>.
/// <para>
/// Grace notes hang off the moment before the one they decorate, so a moment carries two
/// rationals: the main part and a grace part that is normally negative. This iterator
/// converts between the two clocks — the outside world's moment and the wrapped music's.
/// </para>
/// </summary>
public sealed class GraceIterator : MusicWrapperIterator
{
    private static readonly Symbol GraceChangeSymbol = Symbol.Intern("GraceChange");

    private bool _inGrace;

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Grace_iterator";

    /// <summary>
    /// Gets when the next event comes due, translated onto the grace clock.
    /// </summary>
    public override Moment PendingMoment
    {
        get
        {
            Moment pending = base.PendingMoment;
            if (pending.MainPart.IsFinite)
            {
                pending = new Moment(
                    Rational.Zero,
                    MusicStartMoment.GracePart + pending.MainPart);
            }

            return pending;
        }
    }

    /// <summary>Processes the wrapped music on the grace clock.</summary>
    /// <param name="until">The moment to process up to, on the outside clock.</param>
    public override void Process(Moment until)
    {
        Moment main = new Moment(-MusicStartMoment.GracePart + until.GracePart);

        // GraceChange is announced so that Grace_engraver can tell
        // \stemNeutral \grace { ... apart from \grace { \stemNeutral ...
        bool nowInGrace = until.GracePart.IsNonZero;
        if (_inGrace != nowInGrace)
        {
            MusicIterator child = Child;
            if (child?.Context != null)
            {
                child.Context.SendStreamEvent(
                    Context.MakeEvent(GraceChangeSymbol, Origin));
            }
        }

        _inGrace = nowInGrace;

        base.Process(main);

        // Safe because \grace is always inside sequential music.
        DescendToChild(Child.Context);
    }
}

/// <summary>
/// The <c>start-callback</c> of <c>\grace</c> music: grace music starts BEFORE the moment
/// it is written at, by its own whole length.
/// </summary>
public static class GraceMusic
{
    /// <summary>
    /// Answers where grace music starts, relative to where it sits in the stream.
    /// <para>Upstream: <c>ly:grace-music::start-callback</c>.</para>
    /// </summary>
    /// <param name="music">The grace music.</param>
    /// <returns>A moment whose grace part is the negated total length.</returns>
    public static Moment StartCallback(MusicObject music)
    {
        Moment length = MusicWrapper.LengthCallback(music);
        return new Moment(Rational.Zero, -(length.MainPart + length.GracePart));
    }
}
