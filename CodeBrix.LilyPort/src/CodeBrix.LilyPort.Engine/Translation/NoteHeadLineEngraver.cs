/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2000--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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

using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/note-head-line-engraver.cc;

/// <summary>
/// Engraves a line between two note heads in a staff switch if <c>followVoice</c> is set.
/// </summary>
/// <remarks>
/// Creates line-spanner grobs for lines that connect note heads.
/// <para>
/// TODO: have the line commit suicide if the notes are connected with either slur or beam.
/// </para>
/// </remarks>
public sealed class NoteHeadLineEngraver : Engraver
{
    private static readonly Symbol StaffSymbol = Symbol.Intern("Staff");
    private static readonly Symbol FollowVoiceSymbol = Symbol.Intern("followVoice");
    private static readonly Symbol RhythmicHeadInterface = Symbol.Intern("rhythmic-head-interface");

    private Spanner _line;
    private Context _lastStaff;
    private bool _follow;
    private Grob _head;
    private Grob _lastHead;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public NoteHeadLineEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Note_head_line_engraver";

    /// <summary>Notices a note head, and whether it arrived in a different staff.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!info.Grob.HasInterface(RhythmicHeadInterface))
        {
            return;
        }

        _head = info.Grob;
        Context tr = Context?.FindContextAbove(StaffSymbol);
        if (tr != null && tr != _lastStaff
            && SchemeUtilities.ToBool(GetProperty(FollowVoiceSymbol)))
        {
            if (_lastHead != null)
            {
                _follow = true;
            }
        }

        _lastStaff = tr;
    }

    /// <summary>Makes the follower once both ends are known.</summary>
    public override void ProcessAcknowledged()
    {
        if (_line == null && _follow && _lastHead != null && _head != null)
        {
            /* TODO: Don't follow if there's a beam.

            We can't do beam-stuff here, since beam doesn't exist yet.
            Should probably store follow_ in line_, and suicide at some
            later point */
            if (_follow)
            {
                _line = MakeSpanner("VoiceFollower", _head);
            }

            _line.SetBound(Direction.Negative, _lastHead);
            _line.SetBound(Direction.Positive, _head);

            _follow = false;
        }
    }

    /// <summary>Remembers this timestep's head as the next line's left end.</summary>
    public override void StopTranslationTimestep()
    {
        _line = null;
        if (_head != null)
        {
            _lastHead = _head;
        }

        _head = null;
    }
}
