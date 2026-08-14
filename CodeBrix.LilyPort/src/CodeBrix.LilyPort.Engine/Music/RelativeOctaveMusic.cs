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

using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Music; //was previously: lily/relative-octave-music.cc, lily/relative-octave-check.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The <c>to-relative-callback</c>s of <c>\relative</c> itself.
/// <para>
/// Both answer the incoming pitch UNCHANGED: a <c>RelativeOctaveMusic</c> wrapper marks
/// music whose insides have already been converted, so a surrounding <c>\relative</c>
/// must neither re-convert it nor let it move the reference pitch. Which of the two
/// callbacks a music type carries depends on the session's
/// <c>relative-includes</c> option, decided in <c>define-music-types.scm</c> — the
/// distinction lives there, not here, which is why the two bodies are identical.
/// </para>
/// </summary>
public static class RelativeOctaveMusic
{
    /// <summary>
    /// The callback under <c>relative-includes = #f</c>: the wrapped music neither
    /// converts nor moves the reference pitch.
    /// </summary>
    /// <param name="music">The <c>RelativeOctaveMusic</c> (unused, as upstream).</param>
    /// <param name="pitch">The reference pitch.</param>
    /// <returns>The reference pitch, unchanged.</returns>
    public static object NoRelativeCallback(object music, object pitch) => pitch;

    /// <summary>
    /// The callback under <c>relative-includes = #t</c>. Same answer; see the class
    /// remarks for why both exist.
    /// </summary>
    /// <param name="music">The <c>RelativeOctaveMusic</c> (unused, as upstream).</param>
    /// <param name="pitch">The reference pitch.</param>
    /// <returns>The reference pitch, unchanged.</returns>
    public static object RelativeCallback(object music, object pitch) => pitch;
}

/// <summary>
/// The <c>to-relative-callback</c> of the <c>\octaveCheck</c> music: verifies that the
/// running reference pitch is where the written check note says it should be, warns
/// when it is not, and CORRECTS the reference by the octave difference so one wrong
/// octave does not cascade through the rest of the piece.
/// </summary>
public static class RelativeOctaveCheck
{
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");

    /// <summary>
    /// The callback: compares the check note against the reference pitch and answers
    /// the (possibly octave-corrected) reference.
    /// </summary>
    /// <param name="music">The <c>RelativeOctaveCheck</c> music.</param>
    /// <param name="lastPitch">The running reference pitch.</param>
    /// <returns>The new reference pitch.</returns>
    public static object RelativeCallback(MusicObject music, Pitch lastPitch)
    {
        Pitch p = lastPitch;
        MusicObject m = music;
        Pitch checkP = m.GetProperty(PitchSymbol) as Pitch;

        int deltaOct = 0;
        if (checkP != null)
        {
            Pitch noOctave = new Pitch(-1, checkP.NoteName, checkP.Alteration);

            Pitch result = noOctave.ToRelativeOctave(p);

            if (!result.Equals(checkP))
            {
                string s = "Failed octave check, got: ";
                s += result.ToString();

                if (m.Origin is Input origin)
                {
                    origin.Warning(s);
                }
                else
                {
                    Warn.Warning(s);
                }

                deltaOct = checkP.Octave - result.Octave;
            }
        }

        return new Pitch(p.Octave + deltaOct, p.NoteName, p.Alteration);
    }
}
