// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// One grob interface declared by a C++ <c>ADD_INTERFACE</c> macro.
/// </summary>
public sealed class GrobInterfaceDeclaration
{
    /// <summary>Initializes a declaration.</summary>
    /// <param name="name">The Scheme-visible interface symbol.</param>
    /// <param name="cxxName">The C++ class name the macro was given.</param>
    /// <param name="upstreamFile">The upstream file that declares it.</param>
    /// <param name="properties">The user-settable properties the interface owns.</param>
    /// <param name="description">The interface description, in Texinfo.</param>
    public GrobInterfaceDeclaration(
        string name,
        string cxxName,
        string upstreamFile,
        IReadOnlyList<string> properties,
        string description)
    {
        Name = name;
        CxxName = cxxName;
        UpstreamFile = upstreamFile;
        Properties = properties;
        Description = description;
    }

    /// <summary>Gets the Scheme-visible interface symbol, for example <c>slur-interface</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the C++ class name the macro was given, for example <c>Slur</c>.</summary>
    public string CxxName { get; }

    /// <summary>Gets the upstream file that declares this interface.</summary>
    public string UpstreamFile { get; }

    /// <summary>Gets the user-settable properties this interface owns.</summary>
    public IReadOnlyList<string> Properties { get; }

    /// <summary>Gets the interface description, in Texinfo, as upstream writes it.</summary>
    public string Description { get; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The interface name and property count.</returns>
    public override string ToString()
        => Name + " (" + Properties.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " properties)";
}

/// <summary>
/// The grob interfaces LilyPond declares in C++ rather than in Scheme, read from the
/// vendored extraction of its <c>ADD_INTERFACE</c> macros.
/// <para>
/// Upstream declares 86 interfaces this way across 77 <c>lily/*.cc</c> files. The macro
/// expands to a static <c>Grob_interface&lt;cl&gt;</c> object whose constructor registers
/// an init function, so every one of them is in the interface hash table BEFORE any
/// Scheme runs. <c>scm/define-grob-interfaces.scm</c> then declares a further 88 through
/// <c>ly:add-interface</c> while the Scheme layer loads.
/// </para>
/// <para>
/// The port cannot get the C++ half from static initialisers, because the classes those
/// macros name mostly do not exist yet -- so it reads them from a vendored data table and
/// registers them at the same point in the sequence. That is plan decision O8, option C,
/// and the second half of that decision is the reason this class exposes
/// <see cref="Declaration"/>: a grob class, as it gets ported, ASSERTS its interfaces
/// against this table rather than re-declaring them. A re-declaration would drift
/// silently; an assertion turns drift into a test failure, including whenever the port is
/// re-synced to a newer LilyPond.
/// </para>
/// </summary>
public static class GrobInterfaceTable
{
    private const string TableResource = "grob-interfaces.tsv";

    private static readonly IReadOnlyList<GrobInterfaceDeclaration> Declarations = ReadTable();

    private static readonly Dictionary<string, GrobInterfaceDeclaration> ByName = Declarations
        .ToDictionary(entry => entry.Name, StringComparer.Ordinal);

    /// <summary>Gets every interface declared by a C++ <c>ADD_INTERFACE</c> macro.</summary>
    public static IReadOnlyList<GrobInterfaceDeclaration> All => Declarations;

    /// <summary>Looks up one interface declaration.</summary>
    /// <param name="name">The interface symbol, for example <c>slur-interface</c>.</param>
    /// <returns>The declaration, or <see langword="null"/> when the table has no such interface.</returns>
    public static GrobInterfaceDeclaration Declaration(string name)
    {
        if (name == null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        return ByName.TryGetValue(name, out GrobInterfaceDeclaration declaration) ? declaration : null;
    }

    /// <summary>
    /// Checks a ported grob class's interfaces against the vendored table -- the
    /// assert-on-port rider of plan decision O8.
    /// <para>
    /// A ported class states which interfaces it implements; this confirms each one is a
    /// real upstream interface and, where the class also states the properties it expects
    /// that interface to own, that the property set has not drifted. Call it from the
    /// class's tests, not from its constructor: it is a fidelity check against upstream,
    /// not a runtime invariant.
    /// </para>
    /// </summary>
    /// <param name="grobName">The grob class name, used only in the failure message.</param>
    /// <param name="interfaceNames">The interfaces the ported class claims to implement.</param>
    /// <returns>The problems found, empty when the class agrees with upstream.</returns>
    public static IReadOnlyList<string> CheckPortedGrob(string grobName, IEnumerable<string> interfaceNames)
    {
        if (interfaceNames == null)
        {
            throw new ArgumentNullException(nameof(interfaceNames));
        }

        List<string> problems = new List<string>();
        foreach (string name in interfaceNames)
        {
            if (Declaration(name) == null)
            {
                // Not necessarily an error on its own: 88 further interfaces are declared
                // in Scheme, and a grob may implement one of those. It IS an error to
                // claim an interface that neither half of upstream declares, which is
                // what the Scheme-side check in the caller's test covers.
                problems.Add(grobName + " claims '" + name
                             + "', which no ADD_INTERFACE macro declares (check the Scheme-declared set too).");
            }
        }

        return problems;
    }

    /// <summary>
    /// Registers the vendored declarations into an interpreter's interface registry.
    /// <para>
    /// Called before LilyPond's Scheme layer loads, so that
    /// <c>scm/define-grob-interfaces.scm</c> runs afterwards and overwrites the two
    /// interfaces both halves declare -- which is upstream's order, and therefore
    /// upstream's outcome.
    /// </para>
    /// </summary>
    /// <param name="registries">The registries to populate.</param>
    public static void Register(EngineRegistries registries)
    {
        if (registries == null)
        {
            throw new ArgumentNullException(nameof(registries));
        }

        foreach (GrobInterfaceDeclaration declaration in Declarations)
        {
            Symbol name = Symbol.Intern(declaration.Name);
            List<object> properties = new List<object>(declaration.Properties.Count);
            foreach (string property in declaration.Properties)
            {
                properties.Add(Symbol.Intern(property));
            }

            // The entry shape is upstream's: (name description properties), as
            // internal_add_interface builds it with ly_list (a, b, c).
            registries.GrobInterfaces[name] = Pair.List(
                name,
                new MutableString(declaration.Description),
                Pair.ListFrom(properties));
        }
    }

    private static IReadOnlyList<GrobInterfaceDeclaration> ReadTable()
    {
        Assembly assembly = typeof(GrobInterfaceTable).Assembly;
        string resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(TableResource, StringComparison.Ordinal));
        if (resource == null)
        {
            throw new InvalidOperationException(
                "Embedded resource '" + TableResource + "' is missing from the assembly.");
        }

        List<GrobInterfaceDeclaration> entries = new List<GrobInterfaceDeclaration>();
        using (Stream stream = assembly.GetManifestResourceStream(resource))
        using (StreamReader reader = new StreamReader(stream))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] parts = line.Split('\t');
                if (parts.Length < 5)
                {
                    continue;
                }

                entries.Add(new GrobInterfaceDeclaration(
                    parts[0],
                    parts[1],
                    parts[2],
                    parts[3].Length == 0
                        ? Array.Empty<string>()
                        : parts[3].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                    Unescape(parts[4])));
            }
        }

        return entries;
    }

    private static string Unescape(string text)
    {
        if (text.IndexOf('\\') < 0)
        {
            return text;
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder(text.Length);
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\\' && index + 1 < text.Length)
            {
                index++;
                builder.Append(text[index] == 'n' ? '\n' : text[index]);
                continue;
            }

            builder.Append(text[index]);
        }

        return builder.ToString();
    }
}
