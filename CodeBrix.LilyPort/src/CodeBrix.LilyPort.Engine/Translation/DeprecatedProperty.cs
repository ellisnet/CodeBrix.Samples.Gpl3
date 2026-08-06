/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2024 Daniel Eble <nine.fierce.ballads@gmail.com>

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
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/deprecated-property.cc, lily/include/deprecated-property.hh;

// Modified by Jeremy Ellis on 2026-08-05 as part of the CodeBrix port.

/// <summary>
/// The compatibility shim for renamed properties: when a lookup misses, this asks
/// whether the name was deprecated, warns ONCE, and hands back the description of what
/// to read instead.
/// <para>
/// The once-only part is what the object property <c>warned-for-deprecated-access</c>
/// is for, and it is not merely tidiness — a deprecated property read inside a grob
/// callback is read for every grob, so warning each time would bury the score's real
/// diagnostics under thousands of copies of one message.
/// </para>
/// </summary>
public static class DeprecatedProperty
{
    private static readonly Symbol WarnedSymbol
        = Symbol.Intern("warned-for-deprecated-access");

    private static readonly Symbol GetterDescriptionSymbol
        = Symbol.Intern("deprecated-translation-getter-description");

    private static readonly Symbol SetterDescriptionSymbol
        = Symbol.Intern("deprecated-translation-setter-description");

    /// <summary>
    /// Looks a deprecated GETTER up, warning the first time the name is used.
    /// </summary>
    /// <param name="oldSymbol">The name that was looked up and not found.</param>
    /// <returns>
    /// The description — <c>(new-symbol new-to-old-value-function special-warning)</c> —
    /// or <see langword="false"/> when the name was never deprecated.
    /// </returns>
    public static object GetterDescription(Symbol oldSymbol)
        => Describe(oldSymbol, GetterDescriptionSymbol, 0);

    /// <summary>
    /// Looks a deprecated SETTER up, warning the first time the name is used.
    /// </summary>
    /// <param name="oldSymbol">The name that was assigned to.</param>
    /// <returns>The description, or <see langword="false"/> when never deprecated.</returns>
    public static object SetterDescription(Symbol oldSymbol)
        => Describe(oldSymbol, SetterDescriptionSymbol, 2);

    /// <summary>
    /// The shared body of the two lookups. They differ ONLY in which object property
    /// holds the table and in where the replacement name sits in the description —
    /// index 0 for a getter, index 2 for a setter.
    /// </summary>
    private static object Describe(Symbol oldSymbol, Symbol tableName, int newNameIndex)
    {
        object table = LilyPondScheme.LookupProcedure(tableName);
        if (table == null || oldSymbol == null)
        {
            return false;
        }

        object description = SchemeUtilities.CallCallback(table, oldSymbol);
        if (!SchemeUtilities.IsSchemeTrue(description))
        {
            return false;
        }

        object warned = SchemeUtilities.ObjectProperty(
            LilyPondScheme.Current, oldSymbol, WarnedSymbol);
        if (SchemeUtilities.ToBool(warned))
        {
            return description;
        }

        SchemeUtilities.SetObjectProperty(
            LilyPondScheme.Current, oldSymbol, WarnedSymbol, true);

        object specialWarning = Nth(description, newNameIndex + 1);
        if (specialWarning is MutableString || specialWarning is string)
        {
            Warn.Warning(specialWarning.ToString());
        }
        else
        {
            object newSymbol = Nth(description, newNameIndex);
            Warn.Warning(
                "the property '" + oldSymbol.Name + "' is deprecated; use '" + newSymbol + "'");
        }

        return description;
    }

    private static object Nth(object list, int index)
    {
        object cursor = list;
        for (int i = 0; i < index && cursor is Pair pair; i++)
        {
            cursor = pair.Cdr;
        }

        return cursor is Pair result ? result.Car : Nil.Instance;
    }
}
