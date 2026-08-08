/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
  Copyright (C) 1998--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/script-engraver.cc (make_script_from_event only);

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.
// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - EPG11 LANDED, so the tie pair is gone: Tie::set_head/Tie::head are in Objects/Tie.cs
//     and Tie_column::add_tie is in Objects/TieColumn.cs, both in their own upstream
//     file, and Completion_heads_engraver now calls them there. Only EPG14's
//     script-engraver.cc entry is left; when EPG14 lands, this file goes.

/// <summary>
/// EPG5's SEAM, now reduced to the ONE static whose OWNING upstream file is still
/// unported: <c>script-engraver.cc</c> (EPG14).
/// <para>
/// EPG22 retired the rest on 2026-08-07. Everything this class used to carry for
/// <c>stem.cc</c>, <c>directional-element-interface.cc</c>,
/// <c>side-position-interface.cc</c>, <c>staff-symbol-referencer.cc</c>,
/// <c>context.cc</c>, <c>spanner.cc</c>, <c>grob-closure.cc</c> and
/// <c>include/misc.hh</c> is gone: every caller now goes to the canonical class, and the
/// statics that had no home in their own ported file were MOVED into it
/// (<see cref="StaffSymbolReferencer"/>, <see cref="Spanner"/>, <see cref="Misc"/>,
/// <see cref="SchemeUtilities"/>) rather than copied again.
/// </para>
/// <para>
/// EPG11 retired its half on 2026-08-08. The one method left remains a line-for-line
/// translation of the named upstream function; when EPG14 lands, its integrator
/// re-points the last caller and deletes the file.
/// </para>
/// </summary>
internal static class Epg5Seams
{
    private static readonly Symbol ScriptDefinitionsSymbol = Symbol.Intern("scriptDefinitions");
    private static readonly Symbol ScriptPrioritySymbol = Symbol.Intern("script-priority");
    private static readonly Symbol BackendTypePredicateSymbol = Symbol.Intern("backend-type?");

    // ----- lily/script-engraver.cc (make_script_from_event; EPG14's file) -----

    /// <summary>
    /// <c>make_script_from_event</c>: copies an articulation's definition — from the
    /// context's <c>scriptDefinitions</c> alist — onto a freshly made script grob.
    /// </summary>
    internal static void MakeScriptFromEvent(Grob p, Context tg, object artType, long index)
    {
        if (!(artType is Symbol))
        {
            Warn.ProgrammingError(
                "articulation-type must be a symbol since 2.23.6: "
                + SchemeUtilities.DeepCopy(artType));
        }

        object alist = tg.GetProperty(ScriptDefinitionsSymbol);
        Pair art = SchemeUtilities.Assq(artType, alist);

        if (art == null)
        {
            Warn.Warning("do not know how to interpret articulation: " + artType);
            return;
        }

        object entries = art.Cdr;
        bool priorityFound = false;

        object cursor = entries;
        while (cursor is Pair pair)
        {
            cursor = pair.Cdr;
            if (!(pair.Car is Pair propPair) || !(propPair.Car is Symbol sym))
            {
                continue;
            }

            object type = LilyPondScheme.Current != null
                ? SchemeUtilities.ObjectProperty(
                    LilyPondScheme.Current, sym, BackendTypePredicateSymbol)
                : null;
            if (!SchemeUtilities.IsProcedure(type))
            {
                Warn.ProgrammingError(
                    "invalid grob property name in script definition: " + sym.Name);
                continue;
            }

            object val = propPair.Cdr;

            if (ReferenceEquals(sym, ScriptPrioritySymbol))
            {
                priorityFound = true;
                /* Make sure they're in order of user input by adding index i.
                   Don't use the direction in this priority. Smaller means closer
                   to the head.  */
                if (SchemeConvert.IsNumber(val))
                {
                    val = SchemeConvert.ToLong(val, "script-priority") + index;
                }
            }

            object preset = p.GetPropertyData(sym);
            if (val is Nil
                || !SchemeUtilities.IsSchemeTrue(SchemeUtilities.CallCallback(type, preset)))
            {
                p.SetProperty(sym, val);
            }
        }

        if (!priorityFound)
        {
            p.SetProperty(ScriptPrioritySymbol, index);
        }
    }
}
