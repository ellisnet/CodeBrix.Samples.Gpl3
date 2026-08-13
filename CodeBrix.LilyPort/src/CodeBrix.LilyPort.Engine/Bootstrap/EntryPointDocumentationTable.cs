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

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// One entry point's documentation, as its C++ <c>LY_DEFINE</c> macro states it.
/// </summary>
public sealed class EntryPointDocumentation
{
    /// <summary>Initializes a declaration.</summary>
    /// <param name="name">The Scheme name, for example <c>ly:dir?</c>.</param>
    /// <param name="upstreamFile">The upstream file that declares it.</param>
    /// <param name="argumentList">The stringified C++ argument list.</param>
    /// <param name="documentation">The docstring, in Texinfo.</param>
    public EntryPointDocumentation(
        string name, string upstreamFile, string argumentList, string documentation)
    {
        Name = name;
        UpstreamFile = upstreamFile;
        ArgumentList = argumentList;
        Documentation = documentation;
    }

    /// <summary>Gets the Scheme name, for example <c>ly:dir?</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the upstream file that declares this entry point.</summary>
    public string UpstreamFile { get; }

    /// <summary>
    /// Gets the argument list as the C preprocessor stringifies it, for example
    /// <c>(SCM s)</c>. <c>scm/document-functions.scm</c>'s <c>format-c-header</c>
    /// strips the <c>SCM</c> tokens and parentheses back off it.
    /// </summary>
    public string ArgumentList { get; }

    /// <summary>Gets the docstring, in Texinfo, as upstream writes it.</summary>
    public string Documentation { get; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The entry point's Scheme name.</returns>
    public override string ToString() => Name;
}

/// <summary>
/// The docstrings of the 408 entry points LilyPond declares with <c>LY_DEFINE</c>,
/// read from the vendored extraction of those macros.
/// <para>
/// Upstream's macro passes the name, the stringified argument list and the docstring
/// to <c>ly_add_function_documentation</c> (<c>lily/function-documentation.cc:58</c>)
/// at registration time. The port binds its entry points from C#, where a lambda has
/// nowhere to carry a docstring — so the data is carried here, exactly as
/// <c>GrobInterfaceTable</c> and <c>TranslatorDescriptionTable</c> carry the other two
/// bodies of C++-compile-time documentation.
/// </para>
/// </summary>
public static class EntryPointDocumentationTable
{
    private const string TableResource = "entry-point-docs.tsv";

    private static readonly IReadOnlyList<EntryPointDocumentation> Declarations = ReadTable();

    private static readonly Dictionary<string, EntryPointDocumentation> ByName
        = Declarations.ToDictionary(entry => entry.Name, StringComparer.Ordinal);

    /// <summary>Gets every documented entry point.</summary>
    public static IReadOnlyList<EntryPointDocumentation> All => Declarations;

    /// <summary>Looks up one entry point's documentation.</summary>
    /// <param name="name">The Scheme name, for example <c>ly:dir?</c>.</param>
    /// <returns>The documentation, or <see langword="null"/> when the table has no
    /// such entry point.</returns>
    public static EntryPointDocumentation Documentation(string name)
    {
        if (name == null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        return ByName.TryGetValue(name, out EntryPointDocumentation entry) ? entry : null;
    }

    private static IReadOnlyList<EntryPointDocumentation> ReadTable()
    {
        Assembly assembly = typeof(EntryPointDocumentationTable).Assembly;
        string resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(TableResource, StringComparison.Ordinal));
        if (resource == null)
        {
            throw new InvalidOperationException(
                "Embedded resource '" + TableResource + "' is missing from the assembly.");
        }

        List<EntryPointDocumentation> entries = new List<EntryPointDocumentation>();
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
                if (parts.Length < 4)
                {
                    continue;
                }

                entries.Add(new EntryPointDocumentation(
                    parts[0], parts[1], parts[2], Unescape(parts[3])));
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
