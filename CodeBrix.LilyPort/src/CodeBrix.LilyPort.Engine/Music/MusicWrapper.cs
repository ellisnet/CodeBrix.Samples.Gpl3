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

using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Music; //was previously: lily/music-wrapper.cc, lily/include/music-wrapper.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// The timing callbacks for music that merely wraps one other piece of music: both
/// answer for the wrapped <c>element</c>, and zero when there is none.
/// <para>
/// Upstream's <c>Music_wrapper</c> is nothing but these two callbacks —
/// <c>scm/define-music-types.scm</c> hands them to every wrapper type
/// (<c>ContextSpeccedMusic</c>, <c>RelativeOctaveMusic</c>, <c>TransposedMusic</c>,
/// <c>GraceMusic</c>, <c>UnfoldedRepeatedMusic</c>, and a dozen more) as their
/// <c>length-callback</c> and <c>start-callback</c>. There is no wrapper CLASS to
/// speak of; the iterator half lives in
/// <see cref="Translation.MusicWrapperIterator"/>.
/// </para>
/// <para>
/// The consequence of their absence is quiet and total: a wrapper's length is asked
/// for by <c>MusicIterator</c> and by the whole spacing chain, and without a callback
/// <see cref="MusicObject.GetLength"/> answers zero — so every <c>\relative</c> or
/// <c>\fixed</c> expression reports no music at all and engraves an empty page.
/// </para>
/// </summary>
public static class MusicWrapper
{
    private static readonly Symbol ElementSymbol = Symbol.Intern("element");

    /// <summary>The <c>ly:music-wrapper::start-callback</c> callback.</summary>
    /// <param name="music">The wrapper music.</param>
    /// <returns>The wrapped element's start moment, or zero when there is no element.</returns>
    public static Moment StartCallback(MusicObject music)
        => music != null && music.GetProperty(ElementSymbol) is MusicObject element
            ? element.StartMoment()
            : new Moment(0);

    /// <summary>The <c>ly:music-wrapper::length-callback</c> callback.</summary>
    /// <param name="music">The wrapper music.</param>
    /// <returns>The wrapped element's length, or zero when there is no element.</returns>
    public static Moment LengthCallback(MusicObject music)
        => music != null && music.GetProperty(ElementSymbol) is MusicObject element
            ? element.GetLength()
            : new Moment(0);
}
