/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2002--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/grob-interface.cc, lily/include/grob-interface.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// The grob-interface free functions: deriving an interface's Scheme name from a C++
/// class name, and the debugging check that a property a grob is given belongs to one
/// of the interfaces it declares.
/// <para>
/// DIVERGENCE, recorded in PORT-COVERAGE. Upstream's <c>add_interface</c> also
/// REGISTERS the interface, because the <c>ADD_INTERFACE</c> macro runs it from a
/// static initialiser in each grob's <c>.cc</c>. The port has no such initialisers —
/// its C++-side interface declarations are vendored into
/// <see cref="GrobInterfaceTable"/> and registered through <c>ly:add-interface</c> — so
/// what survives here is the NAME DERIVATION the macro applied, kept because it is the
/// rule the vendored table has to agree with.
/// </para>
/// </summary>
public static class GrobInterface
{
    private const string InterfaceSuffix = "-interface";

    private static readonly Symbol MetaSymbol = Symbol.Intern("meta");
    private static readonly Symbol CheckInternalTypesOption = Symbol.Intern("check-internal-types");

    /// <summary>
    /// Derives an interface's Scheme name from a C++ class name, as
    /// <c>add_interface</c> does: camel case becomes lispy, and <c>-interface</c> is
    /// appended when it is not already there.
    /// </summary>
    /// <param name="cxxName">The C++ class name, for example <c>Slur</c>.</param>
    /// <returns>The interface name, for example <c>slur-interface</c>.</returns>
    public static Symbol InterfaceName(string cxxName)
    {
        string lispyName = Misc.CamelCaseToLispIdentifier(cxxName);
        int end = lispyName.Length >= InterfaceSuffix.Length
            ? lispyName.Length - InterfaceSuffix.Length
            : 0;
        if (!string.Equals(lispyName.Substring(end), InterfaceSuffix, System.StringComparison.Ordinal))
        {
            lispyName += InterfaceSuffix;
        }

        return Symbol.Intern(lispyName);
    }

    /// <summary>
    /// Reports a property being set on a grob whose declared interfaces do not list it.
    /// <para>
    /// Gated on the <c>check-internal-types</c> program option, exactly as upstream
    /// gates it on <c>do_internal_type_checking_global</c>: the check walks every
    /// interface of every grob on every assignment, and its findings are fidelity
    /// reports rather than faults, so it is off unless asked for.
    /// </para>
    /// </summary>
    /// <param name="me">The grob being written to.</param>
    /// <param name="sym">The property name.</param>
    public static void CheckInterfacesForProperty(Grob me, Symbol sym)
    {
        if (me == null || sym == null)
        {
            return;
        }

        if (ReferenceEquals(sym, MetaSymbol))
        {
            /*
              otherwise we get in a nasty recursion loop.
            */
            return;
        }

        EngineRegistries registries = LilyPondScheme.Registries;
        if (registries == null)
        {
            return;
        }

        bool found = false;
        object ifs = me.Interfaces;
        for (; !found && ifs is Pair pair; ifs = pair.Cdr)
        {
            if (!(pair.Car is Symbol name)
                || !registries.GrobInterfaces.TryGetValue(name, out object iface))
            {
                Warn.ProgrammingError(
                    "Unknown interface `" + (pair.Car as Symbol)?.Name + "'");
                continue;
            }

            // (name description properties) -- the third element is the property list.
            object properties = ((iface as Pair)?.Cdr as Pair)?.Cdr is Pair tail ? tail.Car : Nil.Instance;
            found = SchemeUtilities.Memq(sym, properties);
        }

        if (!found)
        {
            Warn.ProgrammingError(
                "Grob `" + me.Name + "' has no interface for property `" + sym.Name + "'");
        }
    }

    /// <summary>
    /// Determines whether the <c>check-internal-types</c> option asked for the interface
    /// check to run.
    /// </summary>
    /// <returns><see langword="true"/> when the check is enabled.</returns>
    public static bool IsCheckingEnabled()
    {
        ProgramOptions options = LilyPondScheme.Options;
        return options != null && SchemeUtilities.ToBool(options.Get(CheckInternalTypesOption.Name));
    }
}
