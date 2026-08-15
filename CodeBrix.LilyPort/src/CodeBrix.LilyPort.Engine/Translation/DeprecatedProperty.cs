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

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

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

    private static readonly Symbol SetterObjectPropertySymbol
        = Symbol.Intern("deprecated-setter-object-property");

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
    /// Looks a deprecated SETTER up through the object property a CATEGORY nominates,
    /// which is the form <c>internal_type_check</c> uses.
    /// </summary>
    /// <param name="oldSymbol">The name that was assigned to.</param>
    /// <param name="objectProperty">
    /// The category's table, from <see cref="SetterObjectProperty"/>.
    /// </param>
    /// <returns>The description, or <see langword="false"/> when never deprecated.</returns>
    public static object SetterDescription(Symbol oldSymbol, object objectProperty)
        => Describe(oldSymbol, objectProperty, 2);

    /// <summary>
    /// Answers which deprecation table a property CATEGORY uses for sets.
    /// <para>
    /// The indirection is upstream's and it is not decoration: <c>scm/lily.scm</c> links
    /// <c>deprecated-setter-object-property</c> for <c>translation-type?</c> and for
    /// nothing else, with the comment that backend and music properties "would need"
    /// similar links. Hardcoding the translation table instead would consult it for
    /// grob and music properties too, and answer a redirection that upstream does not
    /// have.
    /// </para>
    /// </summary>
    /// <param name="categoryTypeSymbol">
    /// <c>translation-type?</c>, <c>backend-type?</c> or <c>music-type?</c>.
    /// </param>
    /// <returns>The table, or <see langword="false"/> when the category has none.</returns>
    public static object SetterObjectProperty(Symbol categoryTypeSymbol)
        => CategoryTable(SetterObjectPropertySymbol, categoryTypeSymbol);

    private static object CategoryTable(Symbol tableName, Symbol categoryTypeSymbol)
    {
        object table = LilyPondScheme.LookupProcedure(tableName);
        if (table == null || categoryTypeSymbol == null)
        {
            return false;
        }

        return SchemeUtilities.CallCallback(table, categoryTypeSymbol);
    }

    /// <summary>
    /// The shared body of the two lookups. They differ ONLY in which object property
    /// holds the table and in where the replacement name sits in the description —
    /// index 0 for a getter, index 2 for a setter.
    /// </summary>
    private static object Describe(Symbol oldSymbol, Symbol tableName, int newNameIndex)
        => Describe(oldSymbol, LilyPondScheme.LookupProcedure(tableName), newNameIndex);

    private static object Describe(Symbol oldSymbol, object table, int newNameIndex)
    {
        if (table == null || table is bool || oldSymbol == null)
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
        //was previously: if (SchemeUtilities.ToBool(warned))
        // Upstream's guard is `if (!scm_is_true (warned))` around the warning block, so
        // the already-warned path is Scheme truth. The property only ever holds #t or #f,
        // so the two spellings agree; this is faithfulness, not a behaviour change.
        if (SchemeUtilities.IsSchemeTrue(warned))
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
