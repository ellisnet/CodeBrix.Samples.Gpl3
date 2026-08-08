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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/grid-point-engraver.cc, lily/grid-line-span-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// Generates grid points: one <c>GridPoint</c> whenever the current moment is a
/// multiple of <c>gridInterval</c>.
/// </summary>
public class GridPointEngraver : Engraver
{
    private static readonly Symbol GridIntervalSymbol = Symbol.Intern("gridInterval");

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public GridPointEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Grid_point_engraver";

    /// <summary>Makes a <c>GridPoint</c> on grid moments.</summary>
    public override void ProcessMusic()
    {
        Rational gridInterval = Epg8Support.ToRational(
            GetProperty(GridIntervalSymbol), Rational.Infinity);
        if (gridInterval.IsFinite)
        {
            Music.Moment now = NowMoment;

            if (!(now.MainPart % gridInterval).IsNonZero)
            {
                MakeItem("GridPoint", Nil.Instance);
            }
        }
    }
}

/// <summary>
/// Makes cross-staff grid lines: it catches all normal lines and draws a single span
/// line across them.
/// </summary>
public class GridLineSpanEngraver : Engraver
{
    private static readonly Symbol GridPointInterfaceSymbol
        = Symbol.Intern("grid-point-interface");

    private Item _spanline;
    private readonly List<Item> _lines = new List<Item>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public GridLineSpanEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Grid_line_span_engraver";

    /// <summary>Collects grid points and opens the span line at the second one.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!(info.Grob is Item item) || !item.HasInterface(GridPointInterfaceSymbol))
        {
            return;
        }

        _lines.Add(item);

        if (_lines.Count >= 2 && _spanline == null)
        {
            _spanline = MakeItem("GridLine", Nil.Instance);
            _spanline.XParent = _lines[0];
        }
    }

    /// <summary>Hands the span line its grid points.</summary>
    public override void StopTranslationTimestep()
    {
        if (_spanline != null)
        {
            foreach (Item line in _lines)
            {
                GridLineInterface.AddGridPoint(_spanline, line);
            }

            _spanline = null;
        }

        _lines.Clear();
    }
}
