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

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/pitch-squash-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Sets the vertical position of note heads to <c>squashedPosition</c>, if that
/// property is set. This can be used to make a single-line staff demonstrating the
/// rhythm of a melody — <c>\demoMode</c> and rhythmic staves consist it.
/// </summary>
public class PitchSquashEngraver : Engraver
{
    private static readonly Symbol SquashedPositionSymbol = Symbol.Intern("squashedPosition");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol NoteHeadInterface = Symbol.Intern("note-head-interface");

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public PitchSquashEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Pitch_squash_engraver";

    /// <summary>Overwrites every note head's staff position with the squashed one.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!info.Grob.HasInterface(NoteHeadInterface))
        {
            return;
        }

        object newpos = GetProperty(SquashedPositionSymbol);
        if (SchemeConvert.IsNumber(newpos))
        {
            info.Grob.SetProperty(StaffPositionSymbol, newpos);
        }
    }
}
