/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/system.cc (get_vertical_alignment and vertical_skyline_elements only);

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// The two vertical-organization object callbacks of <c>lily/system.cc</c>, carried
/// beside EPG7's alignment work because <c>Objects/SystemGrob.cs</c> — the file's main
/// port — predates them and stays closed in this pass. The System grob definition
/// names both in its <c>object-callbacks</c>, so they must answer the moment a
/// <c>VerticalAlignment</c> can exist at all.
/// </summary>
public static class SystemGrobVertical
{
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol AlignInterfaceSymbol = Symbol.Intern("align-interface");
    private static readonly Symbol SystemStartDelimiterInterface
        = Symbol.Intern("system-start-delimiter-interface");

    private static readonly Symbol HaraKiriInterface
        = Symbol.Intern("hara-kiri-group-spanner-interface");

    private static readonly Symbol VerticalAlignmentSymbol
        = Symbol.Intern("vertical-alignment");

    /// <summary>
    /// The <c>ly:system::get-vertical-alignment</c> callback body: the one element of
    /// the system carrying the align interface.
    /// </summary>
    /// <param name="me">The system.</param>
    /// <returns>The alignment, or <see langword="null"/> with a diagnostic.</returns>
    public static Grob GetVerticalAlignment(Grob me)
    {
        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        Grob ret = null;
        for (int i = 0; i < elts.Count; i++)
        {
            if (elts[i].HasInterface(AlignInterfaceSymbol))
            {
                if (ret != null)
                {
                    Warn.ProgrammingError(
                        "found multiple vertical alignments in this system");
                }

                ret = elts[i];
            }
        }

        if (ret == null)
        {
            Warn.ProgrammingError("didn't find a vertical alignment in this system");
            return null;
        }

        return ret;
    }

    /// <summary>
    /// The <c>ly:system::vertical-skyline-elements</c> callback body: the system-start
    /// delimiters, plus — when there is a vertical alignment — its hara-kiri groups.
    /// This is what narrows the system's skyline computation to the grobs that matter.
    /// </summary>
    /// <param name="me">The system.</param>
    /// <returns>The grob array.</returns>
    public static GrobArray VerticalSkylineElements(Grob me)
    {
        List<Grob> verticalSkylineGrobs = new List<Grob>();
        IReadOnlyList<Grob> myElts = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        for (int i = 0; i < myElts.Count; i++)
        {
            if (myElts[i].HasInterface(SystemStartDelimiterInterface))
            {
                verticalSkylineGrobs.Add(myElts[i]);
            }
        }

        Grob align = me.GetObject(VerticalAlignmentSymbol) as Grob;
        if (align == null)
        {
            GrobArray result = new GrobArray();
            result.SetArray(verticalSkylineGrobs);
            return result;
        }

        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(align, ElementsSymbol);

        for (int i = 0; i < elts.Count; i++)
        {
            if (elts[i].HasInterface(HaraKiriInterface))
            {
                verticalSkylineGrobs.Add(elts[i]);
            }
        }

        GrobArray grobs = new GrobArray();
        grobs.SetArray(verticalSkylineGrobs);
        return grobs;
    }
}
