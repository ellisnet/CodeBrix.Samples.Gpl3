/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
  Copyright (C) 2006--2026 Han-Wen Nienhuys <hanwen@lilypond.org>

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
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/dot-column-engraver.cc, lily/dots-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// Collects the dots of a timestep's rhythmic heads into a <c>DotColumn</c>, so they
/// shift right of the notes together. If omitted, dots appear on top of the notes.
/// </summary>
public class DotColumnEngraver : Engraver
{
    private static readonly Symbol DotSymbol = Symbol.Intern("dot");
    private static readonly Symbol RhythmicHeadInterface
        = Symbol.Intern("rhythmic-head-interface");

    private Grob _dotcol;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public DotColumnEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Dot_column_engraver";

    /// <summary>Forgets the timestep's column.</summary>
    public override void StopTranslationTimestep()
    {
        _dotcol = null;
    }

    /// <summary>Adds each dotted head's dot to the column, making it on first need.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob == null || !info.Grob.HasInterface(RhythmicHeadInterface))
        {
            return;
        }

        Grob d = info.Grob.GetObject(DotSymbol) as Grob;
        if (d != null)
        {
            if (_dotcol == null)
            {
                _dotcol = MakeItem("DotColumn", Nil.Instance);
            }

            DotColumn.AddHead(_dotcol, info.Grob);
        }
    }
}

/// <summary>
/// Makes a <c>Dots</c> grob for every rhythmic head whose causing event carries a
/// dotted duration.
/// </summary>
public class DotsEngraver : Engraver
{
    private static readonly Symbol DotSymbol = Symbol.Intern("dot");
    private static readonly Symbol DurationSymbol = Symbol.Intern("duration");
    private static readonly Symbol RhythmicHeadInterface
        = Symbol.Intern("rhythmic-head-interface");

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public DotsEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Dots_engraver";

    /// <summary>Makes the dots for a dotted head.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob == null || !info.Grob.HasInterface(RhythmicHeadInterface))
        {
            return;
        }

        StreamEvent cause = info.EventCause;
        if (cause == null)
        {
            return;
        }

        Grob note = info.Grob;
        if (note.GetObject(DotSymbol) is Grob)
        {
            return;
        }

        if (cause.GetProperty(DurationSymbol) is Duration dur && dur.DotCount != 0)
        {
            Item d = MakeItem("Dots", note);
            RhythmicHead.SetDots(note, d);

            d.YParent = note;
        }
    }
}
