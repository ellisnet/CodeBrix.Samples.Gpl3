// This file is part of python-ly, https://pypi.python.org/pypi/python-ly
//
// Copyright (c) 2011 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation, either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
// See http://www.gnu.org/licenses/ for more information.

using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Ly.Data; //was previously: ly/data/__init__.py, ly/data/_data.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Query functions over the LilyPond-generated data — grobs, interfaces,
/// properties, context properties, engravers, glyph names and the guile word
/// lists. The LilyPond half is REGENERATED from the CodeBrix.LilyPort 2.27.2
/// engine (tools/datagen); the scheme half is python-ly's own generated data.
/// </summary>
public static class LyData
{
    /// <summary>
    /// The interfaces table with <c>_data.py</c>'s patch applied: upstream adds
    /// <c>bar-extent</c> to <c>bar-line-interface</c> when the generated data
    /// misses it ("add some missing things").
    /// </summary>
    private static readonly Dictionary<string, string[]> Interfaces = Patch();

    /// <summary>Gets the version of the engine the data was generated from.</summary>
    public static string Version => LilyPondData.Version;

    /// <summary>Returns the sorted list of all grob names.</summary>
    /// <returns>The grob names.</returns>
    public static string[] Grobs()
        => LilyPondData.Grobs.Keys.OrderBy(k => k, System.StringComparer.Ordinal).ToArray();

    /// <summary>Returns the list of properties the named grob supports.</summary>
    /// <param name="grob">The grob name.</param>
    /// <returns>The sorted, de-duplicated property names.</returns>
    public static string[] GrobProperties(string grob)
        => GrobInterfaces(grob)
            .SelectMany(iface => GrobInterfaceProperties(iface))
            .Distinct()
            .OrderBy(p => p, System.StringComparer.Ordinal)
            .ToArray();

    /// <summary>Returns (property, interface) pairs for the named grob.</summary>
    /// <param name="grob">The grob name.</param>
    /// <returns>The sorted pairs.</returns>
    public static (string Property, string Interface)[] GrobPropertiesWithInterface(string grob)
        => GrobInterfaces(grob)
            .SelectMany(iface => GrobInterfaceProperties(iface)
                .Select(prop => (Property: prop, Interface: iface)))
            .OrderBy(pair => pair.Property, System.StringComparer.Ordinal)
            .ThenBy(pair => pair.Interface, System.StringComparer.Ordinal)
            .ToArray();

    /// <summary>Returns the list of interfaces a grob supports; with a property
    /// given, only the interfaces that define it.</summary>
    /// <param name="grob">The grob name.</param>
    /// <param name="property">The property to filter by, or <see langword="null"/>.</param>
    /// <returns>The interface names.</returns>
    public static string[] GrobInterfaces(string grob, string property = null)
    {
        string[] interfaces = LilyPondData.Grobs.TryGetValue(grob, out string[] found)
            ? found
            : System.Array.Empty<string>();
        if (property == null)
        {
            return interfaces;
        }

        return interfaces
            .Where(iface => GrobInterfaceProperties(iface).Contains(property))
            .ToArray();
    }

    /// <summary>Returns the list of properties an interface supports.</summary>
    /// <param name="iface">The interface name.</param>
    /// <returns>The property names.</returns>
    public static string[] GrobInterfaceProperties(string iface)
        => Interfaces.TryGetValue(iface, out string[] props)
            ? props
            : System.Array.Empty<string>();

    /// <summary>Returns the interfaces that define the property.</summary>
    /// <param name="property">The property name.</param>
    /// <returns>The interface names.</returns>
    public static string[] GrobInterfacesForProperty(string property)
        => Interfaces
            .Where(entry => entry.Value.Contains(property))
            .Select(entry => entry.Key)
            .ToArray();

    /// <summary>Returns the list of all properties.</summary>
    /// <returns>The sorted, de-duplicated property names.</returns>
    public static string[] AllGrobProperties()
        => Interfaces.Values
            .SelectMany(props => props)
            .Distinct()
            .OrderBy(p => p, System.StringComparer.Ordinal)
            .ToArray();

    /// <summary>Returns the list of context properties.</summary>
    /// <returns>The property names.</returns>
    public static string[] ContextProperties() => LilyPondData.Contextproperties;

    /// <summary>Returns the list of engravers and performers.</summary>
    /// <returns>The translator names.</returns>
    public static string[] Engravers() => LilyPondData.Engravers;

    /// <summary>Returns the list of glyphs in the Emmentaler font.</summary>
    /// <returns>The glyph names.</returns>
    public static string[] MusicGlyphs() => LilyPondData.Musicglyphs;

    /// <summary>Returns the list of guile keywords.</summary>
    /// <returns>The words.</returns>
    public static string[] SchemeKeywords() => SchemeData.SchemeKeywords;

    /// <summary>Returns the list of scheme functions.</summary>
    /// <returns>The words.</returns>
    public static string[] SchemeFunctions() => SchemeData.SchemeFunctions;

    /// <summary>Returns the list of scheme variables.</summary>
    /// <returns>The words.</returns>
    public static string[] SchemeVariables() => SchemeData.SchemeVariables;

    /// <summary>Returns the list of scheme constants.</summary>
    /// <returns>The words.</returns>
    public static string[] SchemeConstants() => SchemeData.SchemeConstants;

    /// <summary>Returns the list of all scheme words, in upstream's order
    /// (keywords + functions + variables + constants).</summary>
    /// <returns>The words.</returns>
    public static string[] AllSchemeWords()
        => SchemeData.SchemeKeywords
            .Concat(SchemeData.SchemeFunctions)
            .Concat(SchemeData.SchemeVariables)
            .Concat(SchemeData.SchemeConstants)
            .ToArray();

    private static Dictionary<string, string[]> Patch()
    {
        Dictionary<string, string[]> interfaces
            = new Dictionary<string, string[]>(LilyPondData.Interfaces);
        if (interfaces.TryGetValue("bar-line-interface", out string[] props)
            && !props.Contains("bar-extent"))
        {
            List<string> patched = new List<string>(props);
            patched.Insert(1, "bar-extent");
            interfaces["bar-line-interface"] = patched.ToArray();
        }

        return interfaces;
    }
}
