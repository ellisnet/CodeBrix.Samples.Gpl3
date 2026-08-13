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
/// One translator's Internals-Reference metadata, as its C++ <c>ADD_TRANSLATOR</c>
/// macro and <c>boot()</c> listener declarations state it.
/// </summary>
public sealed class TranslatorDescriptionDeclaration
{
    /// <summary>Initializes a declaration.</summary>
    /// <param name="name">The translator name, for example <c>Tie_engraver</c>.</param>
    /// <param name="upstreamFile">The upstream file that declares it.</param>
    /// <param name="grobsCreated">The grobs the translator creates.</param>
    /// <param name="propertiesRead">The context properties it reads.</param>
    /// <param name="propertiesWritten">The context properties it writes.</param>
    /// <param name="eventsAccepted">The event classes it listens to.</param>
    /// <param name="description">The description, in Texinfo.</param>
    public TranslatorDescriptionDeclaration(
        string name,
        string upstreamFile,
        IReadOnlyList<string> grobsCreated,
        IReadOnlyList<string> propertiesRead,
        IReadOnlyList<string> propertiesWritten,
        IReadOnlyList<string> eventsAccepted,
        string description)
    {
        Name = name;
        UpstreamFile = upstreamFile;
        GrobsCreated = grobsCreated;
        PropertiesRead = propertiesRead;
        PropertiesWritten = propertiesWritten;
        EventsAccepted = eventsAccepted;
        Description = description;
    }

    /// <summary>Gets the translator name, for example <c>Tie_engraver</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the upstream file that declares this translator.</summary>
    public string UpstreamFile { get; }

    /// <summary>Gets the grobs the translator creates.</summary>
    public IReadOnlyList<string> GrobsCreated { get; }

    /// <summary>Gets the context properties the translator reads.</summary>
    public IReadOnlyList<string> PropertiesRead { get; }

    /// <summary>Gets the context properties the translator writes.</summary>
    public IReadOnlyList<string> PropertiesWritten { get; }

    /// <summary>Gets the event classes the translator listens to.</summary>
    public IReadOnlyList<string> EventsAccepted { get; }

    /// <summary>Gets the description, in Texinfo, as upstream writes it.</summary>
    public string Description { get; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The translator name.</returns>
    public override string ToString() => Name;
}

/// <summary>
/// The documentation metadata of the 126 translators LilyPond declares in C++, read
/// from the vendored extraction of its <c>ADD_TRANSLATOR</c> macros.
/// <para>
/// Upstream assembles this in <c>Translator::static_translator_description</c>
/// (<c>lily/translator.cc:126</c>) out of two things that exist only at C++ compile
/// time: the macro's four text blocks, and the translator's listener declarations.
/// The port's translators are C# classes, so neither survives — every one of them
/// registered an EMPTY description, and the Translation node of the Internals
/// Reference had no text at all. This is the same problem <c>ADD_INTERFACE</c> posed
/// and takes the same answer (plan decision O8, option C): a committed table,
/// registered at the point in the sequence where the static initialisers would have
/// run.
/// </para>
/// <para>
/// The table is DATA, not a second source of truth: nothing in the engine's behaviour
/// reads it, and a translator that stops existing upstream simply stops being
/// documented. <c>TranslatorDescriptionTests</c> fences it against the registry, so a
/// name that drifts apart from the C# roster fails a test rather than documenting
/// itself as blank.
/// </para>
/// </summary>
public static class TranslatorDescriptionTable
{
    private const string TableResource = "translator-descriptions.tsv";

    private static readonly Symbol DescriptionSymbol = Symbol.Intern("description");
    private static readonly Symbol GrobsCreatedSymbol = Symbol.Intern("grobs-created");
    private static readonly Symbol EventsAcceptedSymbol = Symbol.Intern("events-accepted");
    private static readonly Symbol PropertiesReadSymbol = Symbol.Intern("properties-read");
    private static readonly Symbol PropertiesWrittenSymbol = Symbol.Intern("properties-written");

    private static readonly IReadOnlyList<TranslatorDescriptionDeclaration> Declarations
        = ReadTable();

    private static readonly Dictionary<string, TranslatorDescriptionDeclaration> ByName
        = Declarations.ToDictionary(entry => entry.Name, StringComparer.Ordinal);

    /// <summary>Gets every translator declared by a C++ <c>ADD_TRANSLATOR</c> macro.</summary>
    public static IReadOnlyList<TranslatorDescriptionDeclaration> All => Declarations;

    /// <summary>Looks up one translator's declaration.</summary>
    /// <param name="name">The translator name, for example <c>Tie_engraver</c>.</param>
    /// <returns>The declaration, or <see langword="null"/> when the table has no such
    /// translator.</returns>
    public static TranslatorDescriptionDeclaration Declaration(string name)
    {
        if (name == null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        return ByName.TryGetValue(name, out TranslatorDescriptionDeclaration declaration)
            ? declaration
            : null;
    }

    /// <summary>
    /// Builds the alist <c>ly:translator-description</c> answers for one translator.
    /// <para>
    /// The key order is upstream's, which builds the list by consing in the order
    /// grobs-created, description, events-accepted, properties-read,
    /// properties-written — so the finished alist reads back in the reverse of that.
    /// Nothing documented depends on the order, but matching it costs nothing and
    /// keeps a printed alist comparable with upstream's.
    /// </para>
    /// </summary>
    /// <param name="declaration">The translator's declaration.</param>
    /// <returns>The description alist.</returns>
    public static object BuildAlist(TranslatorDescriptionDeclaration declaration)
    {
        if (declaration == null)
        {
            throw new ArgumentNullException(nameof(declaration));
        }

        object alist = Nil.Instance;
        alist = new Pair(
            new Pair(GrobsCreatedSymbol, Symbols(declaration.GrobsCreated)), alist);
        alist = new Pair(
            new Pair(DescriptionSymbol, new MutableString(declaration.Description)), alist);
        alist = new Pair(
            new Pair(EventsAcceptedSymbol, Symbols(declaration.EventsAccepted)), alist);
        alist = new Pair(
            new Pair(PropertiesReadSymbol, Symbols(declaration.PropertiesRead)), alist);
        alist = new Pair(
            new Pair(PropertiesWrittenSymbol, Symbols(declaration.PropertiesWritten)), alist);
        return alist;
    }

    /// <summary>
    /// Fills in the description of every registered translator the table knows.
    /// <para>
    /// Called after the C++-side translators are registered and BEFORE the Scheme layer
    /// loads, because <c>scm/scheme-engravers.scm</c> registers its own translators with
    /// descriptions of their own and must not be overwritten by a later pass.
    /// </para>
    /// </summary>
    /// <param name="registries">The registries to populate.</param>
    public static void Register(EngineRegistries registries)
    {
        if (registries == null)
        {
            throw new ArgumentNullException(nameof(registries));
        }

        foreach (TranslatorDescriptionDeclaration declaration in Declarations)
        {
            Symbol name = Symbol.Intern(declaration.Name);
            if (!registries.Translators.ContainsKey(name))
            {
                // Upstream declares a translator the port has not ported. Documenting
                // one that cannot be instantiated would be a lie; the roster gap is
                // what TranslatorDescriptionTests reports.
                continue;
            }

            registries.TranslatorDescriptions[name] = BuildAlist(declaration);
        }
    }

    private static object Symbols(IReadOnlyList<string> names)
    {
        List<object> symbols = new List<object>(names.Count);
        foreach (string name in names)
        {
            symbols.Add(Symbol.Intern(name));
        }

        return Pair.ListFrom(symbols);
    }

    private static IReadOnlyList<TranslatorDescriptionDeclaration> ReadTable()
    {
        Assembly assembly = typeof(TranslatorDescriptionTable).Assembly;
        string resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(TableResource, StringComparison.Ordinal));
        if (resource == null)
        {
            throw new InvalidOperationException(
                "Embedded resource '" + TableResource + "' is missing from the assembly.");
        }

        List<TranslatorDescriptionDeclaration> entries
            = new List<TranslatorDescriptionDeclaration>();
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
                if (parts.Length < 7)
                {
                    continue;
                }

                entries.Add(new TranslatorDescriptionDeclaration(
                    parts[0],
                    parts[1],
                    SymbolList(parts[2]),
                    SymbolList(parts[3]),
                    SymbolList(parts[4]),
                    SymbolList(parts[5]),
                    Unescape(parts[6])));
            }
        }

        return entries;
    }

    private static string[] SymbolList(string field)
        => field.Length == 0
            ? Array.Empty<string>()
            : field.Split(' ', StringSplitOptions.RemoveEmptyEntries);

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
