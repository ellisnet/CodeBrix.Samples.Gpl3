/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/music-output.cc, lily/include/music-output.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// What interpreting music produced, before it is turned into a file: a
/// <see cref="PaperScore"/> for the layout path, a performance for the MIDI one.
/// <para>
/// The type is nearly empty and still worth having: <c>ly:format-output</c> takes a
/// finished context, asks it for its output, and calls <see cref="Process"/> on
/// whatever it gets. That one virtual call is the whole seam between "the music has
/// been interpreted" and "the result has been laid out", and it is what lets the same
/// toplevel handler in <c>scm/lily.scm</c> drive both paths.
/// </para>
/// </summary>
public class MusicOutput
{
    /// <summary>Gets the C++ class name this output corresponds to.</summary>
    public virtual string ClassName => "Music_output";

    /// <summary>
    /// Turns the interpreted result into its laid-out form. The base does nothing,
    /// exactly as upstream's does.
    /// </summary>
    public virtual void Process()
    {
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The output's class name.</returns>
    public override string ToString() => "#<" + ClassName + ">";
}
