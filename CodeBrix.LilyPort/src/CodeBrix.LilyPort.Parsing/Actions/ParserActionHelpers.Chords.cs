/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
                 Jan Nieuwenhuizen <janneke@gnu.org>

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

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (epilogue: make_chord_step);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <content>
/// The one epilogue helper the Chords group brings in. Its chord bodies otherwise
/// REUSE <see cref="MakeChordElements"/> from the MusicAssembly group, as the standing reuse obligation
/// prescribes.
/// </content>
internal static partial class ParserActionHelpers
{
    /// <summary>
    /// Turns a written chord step — <c>7</c>, <c>9+</c>, <c>13-</c> — into the pitch
    /// that step names above the root.
    /// <para>Upstream: <c>make_chord_step</c> in <c>parser.yy</c>'s epilogue. The step
    /// is one-based in the written syntax and zero-based as a scale index, hence the
    /// <c>- 1</c>; the constructed pitch normalizes its own octave, so step 9 is
    /// already an octave up. The <c>get_notename () == 6</c> case is the SEVENTH,
    /// which in chord naming means a minor seventh — so it is flattened.</para>
    /// </summary>
    /// <param name="step">The written step number.</param>
    /// <param name="alteration">The alteration the <c>+</c> or <c>-</c> asked for.</param>
    /// <returns>The pitch.</returns>
    internal static object MakeChordStep(object step, Rational alteration)
    {
        // Notename/octave are normalized
        Pitch m = new Pitch(
            0, SchemeConvert.ToInt(step, "make-chord-step") - 1, alteration);

        if (m.NoteName == 6)
        {
            m = m.Transposed(new Pitch(0, 0, new Rational(-1, 2))); // FLAT_ALTERATION
        }

        return m;
    }
}
