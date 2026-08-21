// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Fresco.Brix.Snippets; //was previously: frescobaldi/snippet/import_export.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One snippet as it travels between installations.</summary>
public sealed class PortableSnippet
{
    /// <summary>Creates a portable snippet.</summary>
    /// <param name="name">Its stable name.</param>
    /// <param name="title">Its title, or null.</param>
    /// <param name="body">Its full text.</param>
    /// <param name="shortcuts">Its shortcuts, in Qt's notation.</param>
    public PortableSnippet(
        string name, string title, string body, IReadOnlyList<string> shortcuts = null)
    {
        Name = name;
        Title = title;
        Body = body;
        Shortcuts = shortcuts ?? Array.Empty<string>();
    }

    /// <summary>Gets the stable name.</summary>
    public string Name { get; }

    /// <summary>Gets the title, or null.</summary>
    public string Title { get; }

    /// <summary>Gets the full text.</summary>
    public string Body { get; }

    /// <summary>Gets the shortcuts.</summary>
    public IReadOnlyList<string> Shortcuts { get; }
}

/// <summary>
/// Snippets to and from a file, in the same XML upstream reads and writes —
/// so a Frescobaldi user's exported snippets open here, and the other way
/// round.
/// </summary>
public static class SnippetImportExport
{
    /// <summary>The comment written at the head of an exported file.</summary>
    private const string FileComment =
        " NOTE: This file can be edited and imported into "
        + AppInfo.AppName + ". \n"
        + "      The 'id' attribute of a snippet is used to identify it. \n"
        + "      Snippets that carry the id of a built-in snippet replace it. ";

    /// <summary>Writes snippets to a file.</summary>
    /// <param name="library">The library they come from.</param>
    /// <param name="names">The snippets to write.</param>
    /// <param name="path">The file.</param>
    /// <param name="shortcuts">The shortcut collection, or null.</param>
    public static void Save(
        SnippetLibrary library,
        IEnumerable<string> names,
        string path,
        SnippetShortcuts shortcuts = null)
    {
        XElement root = new XElement("snippets", new XComment(FileComment));
        foreach (var name in names)
        {
            root.Add(new XElement(
                "snippet",
                new XAttribute("id", name),
                new XElement("title", library.Title(name, fallback: false)),
                new XElement("shortcuts", (shortcuts?.Shortcuts(name)
                    ?? Array.Empty<KeySequence>())
                    .Select(s => new XElement("shortcut", s.ToString()))),
                new XElement("body", library.Text(name))));
        }

        new XDocument(root).Save(path);
    }

    /// <summary>Reads snippets from a file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The snippets.</returns>
    /// <exception cref="InvalidDataException">The file holds no snippets.</exception>
    public static IReadOnlyList<PortableSnippet> Load(string path)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(path);
        }
        catch (XmlException error)
        {
            throw new InvalidDataException(error.Message, error);
        }

        List<PortableSnippet> snippets = document.Root?
            .Elements("snippet")
            .Select(element => new PortableSnippet(
                (string)element.Attribute("id"),
                Trimmed(element.Element("title")?.Value),
                element.Element("body")?.Value ?? string.Empty,
                element.Element("shortcuts")?
                    .Elements("shortcut")
                    .Select(s => s.Value.Trim())
                    .Where(s => s.Length > 0)
                    .ToList()))
            .Where(s => !string.IsNullOrEmpty(s.Name))
            .ToList()
            ?? new List<PortableSnippet>();

        if (snippets.Count == 0)
        {
            throw new InvalidDataException(I18n.Get("No snippets found."));
        }

        return snippets;
    }

    /// <summary>Stores imported snippets in the library.</summary>
    /// <param name="library">The library.</param>
    /// <param name="snippets">The snippets to store.</param>
    /// <param name="shortcuts">The shortcut collection, or null.</param>
    public static void Apply(
        SnippetLibrary library,
        IEnumerable<PortableSnippet> snippets,
        SnippetShortcuts shortcuts = null)
    {
        foreach (var snippet in snippets)
        {
            library.Save(snippet.Name, snippet.Body, snippet.Title);
            if (shortcuts == null || snippet.Shortcuts.Count == 0) { continue; }

            shortcuts.SetShortcuts(
                snippet.Name,
                snippet.Shortcuts.Select(KeySequence.Parse)
                    .Where(k => k != null)
                    .ToList());
        }
    }

    private static string Trimmed(string text)
        => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
