/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2010--2026 Reinhold Kainhofer <reinhold@kainhofer.com>

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

using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/context-mod.cc, lily/include/context-mod.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/*
 * context-mod.hh
 * Implement a structure to store context modifications to be inserted
 * at some later point
 */

/// <summary>
/// Modifications for an interpretation context as given in the input — the value a
/// <c>\with { ... }</c> block builds.
/// <para>
/// A <c>Context_mod</c> is a container for a list of single context mods like
/// <c>\consists ...</c> and <c>\override ...</c>. The mods are stored newest-first
/// and handed out in written order by <see cref="GetMods"/>, which returns a FRESH
/// list each time — the grammar's <c>optional_context_mods</c> relies on that
/// freshness to append destructively.
/// </para>
/// </summary>
public class ContextMod
{
    private object _mods;

    /// <summary>Initializes an empty modification list.</summary>
    public ContextMod()
    {
        _mods = Nil.Instance;
    }

    /// <summary>
    /// Initializes a copy of another modification list. The stored list is SHARED,
    /// exactly as upstream's copy constructor shares <c>mods_</c>; sharing is safe
    /// because additions only cons onto the front.
    /// </summary>
    /// <param name="source">The modification list to copy.</param>
    public ContextMod(ContextMod source)
    {
        _mods = source == null ? Nil.Instance : source._mods;
    }

    /// <summary>
    /// Initializes the container from a list of mods in written order.
    /// <para>Upstream: <c>Context_mod (SCM mod_list)</c>, which stores
    /// <c>scm_reverse (mod_list)</c>.</para>
    /// </summary>
    /// <param name="modList">The mods, oldest first.</param>
    public ContextMod(object modList)
    {
        _mods = Reverse(modList);
    }

    /// <summary>Adds one mod, such as <c>(consists Some_engraver)</c>.</summary>
    /// <param name="mod">The mod to add.</param>
    public void AddContextMod(object mod)
    {
        _mods = new Pair(mod, _mods);
    }

    /// <summary>Adds every mod of a list, in the list's order.</summary>
    /// <param name="mods">The mods to add, oldest first.</param>
    public void AddContextMods(object mods)
    {
        for (object m = mods; m is Pair pair; m = pair.Cdr)
        {
            AddContextMod(pair.Car);
        }
    }

    /// <summary>
    /// Returns the mods in written order, as a fresh list whose pairs the caller may
    /// mutate.
    /// <para>Upstream: <c>Context_mod::get_mods</c>, whose <c>scm_reverse</c> copies —
    /// <c>parser.yy</c> documents that callers <c>append!</c> the result.</para>
    /// </summary>
    /// <returns>The mod list, oldest first.</returns>
    public object GetMods() => Reverse(_mods);

    /// <summary>Returns the external representation, in upstream's debug wording.</summary>
    /// <returns>The mods, in written order.</returns>
    public override string ToString() => "#<Context_mod " + GetMods() + ">";

    /// <summary>Non-destructively reverses a list, which is <c>scm_reverse</c>.</summary>
    /// <param name="list">The list to reverse.</param>
    /// <returns>A fresh reversed list.</returns>
    private static object Reverse(object list)
    {
        object result = Nil.Instance;
        for (object p = list; p is Pair pair; p = pair.Cdr)
        {
            result = new Pair(pair.Car, result);
        }

        return result;
    }
}
