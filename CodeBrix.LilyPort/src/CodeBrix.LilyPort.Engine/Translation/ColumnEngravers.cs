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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/rhythmic-column-engraver.cc, lily/collision-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/*
  this engraver  glues together stems, rests and note heads into a NoteColumn
  grob.

  It also generates spacing objects.  Originally, we have tried to
  have the spacing functionality at different levels.

  - by simply using the sequence of Separation-item as
  spacing-sequences (at staff level). Unfortunately, this fucks up if
  there are different kinds of tuplets in different voices (8th and
  8ths triplets combined made the program believe there were 1/12 th
  notes.).

  Doing it in a separate engraver using timing info is generally
  complicated (start/end time management), and fucks up if a voice
  changes staff.

  Now we do it from here again. This has the problem that voices can
  appear and disappear at will, leaving lots of loose ends (the note
  spacing engraver don't know where to connect the last note of the
  voice on the right with), but we don't complain about those, and let
  the default spacing do its work.
*/

/// <summary>
/// Glues one voice's stems, rests and note heads together into a <c>NoteColumn</c>.
/// </summary>
public class RhythmicColumnEngraver : Engraver
{
    private static readonly Symbol StemInterface = Symbol.Intern("stem-interface");
    private static readonly Symbol FlagInterface = Symbol.Intern("flag-interface");
    private static readonly Symbol RhythmicHeadInterface
        = Symbol.Intern("rhythmic-head-interface");

    private readonly List<Grob> _rheads = new List<Grob>();
    private Grob _stem;
    private Grob _flag;
    private Grob _noteColumn;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public RhythmicColumnEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Rhythmic_column_engraver";

    /// <summary>Builds the column out of everything acknowledged this round.</summary>
    public override void ProcessAcknowledged()
    {
        if (_rheads.Count > 0)
        {
            if (_noteColumn == null)
            {
                _noteColumn = MakeItem("NoteColumn", _rheads[0]);
            }

            for (int i = 0; i < _rheads.Count; i++)
            {
                if (_rheads[i].XParent == null)
                {
                    NoteColumn.AddHead(_noteColumn, _rheads[i]);
                }
            }

            _rheads.Clear();
        }

        if (_noteColumn != null)
        {
            if (_stem != null && _stem.XParent == null)
            {
                NoteColumn.SetStem(_noteColumn, _stem);
                _stem = null;
            }

            if (_flag != null)
            {
                AxisGroupInterface.AddElement(_noteColumn, _flag);
                _flag = null;
            }
        }
    }

    /// <summary>Collects the stems, flags and rhythmic heads of this timestep.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        Grob grob = info.Grob;
        if (grob == null)
        {
            return;
        }

        if (grob.HasInterface(StemInterface))
        {
            _stem = grob;
        }

        if (grob.HasInterface(FlagInterface))
        {
            _flag = grob;
        }

        if (grob.HasInterface(RhythmicHeadInterface))
        {
            _rheads.Add(grob);
        }
    }

    /// <summary>Forgets the timestep's column, stem and flag.</summary>
    public override void StopTranslationTimestep()
    {
        _noteColumn = null;
        _stem = null;
        _flag = null;
    }
}

/// <summary>
/// Collects <c>NoteColumn</c>s and, as soon as there are two or more, puts them in a
/// <c>NoteCollision</c> object.
/// </summary>
public class CollisionEngraver : Engraver
{
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol IgnoreCollisionSymbol = Symbol.Intern("ignore-collision");

    private Item _col;
    private readonly List<Item> _noteColumns = new List<Item>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public CollisionEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Collision_engraver";

    /// <summary>Makes the collision object once two or more columns have appeared.</summary>
    public override void ProcessAcknowledged()
    {
        if (_col != null || _noteColumns.Count < 2)
        {
            return;
        }

        if (_col == null)
        {
            _col = MakeItem("NoteCollision", Nil.Instance);
        }

        for (int i = 0; i < _noteColumns.Count; i++)
        {
            NoteCollisionInterface.AddColumn(_col, _noteColumns[i]);
        }
    }

    /// <summary>Collects the note columns of this timestep.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!(info.Grob is Item item) || !item.HasInterface(NoteColumnInterface))
        {
            return;
        }

        /*should check Y axis? */
        if (NoteColumn.HasRests(item) || item.XParent != null)
        {
            return;
        }

        if (SchemeUtilities.ToBool(item.GetProperty(IgnoreCollisionSymbol)))
        {
            return;
        }

        _noteColumns.Add(item);
    }

    /// <summary>Forgets the timestep's collision object and columns.</summary>
    public override void StopTranslationTimestep()
    {
        _col = null;
        _noteColumns.Clear();
    }
}
