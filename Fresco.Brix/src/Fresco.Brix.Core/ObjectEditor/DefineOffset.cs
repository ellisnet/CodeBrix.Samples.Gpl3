// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using Fresco.Brix.Tools;
using System;
using System.Collections.Generic;
using System.Globalization;
using MusicItem = Fresco.Brix.Ly.Music.Item;
using MusicTree = Fresco.Brix.Ly.Music.Document;

namespace Fresco.Brix.ObjectEditor; //was previously: frescobaldi/objecteditor/defineoffset.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Works out which kind of engraved object an offset would be applied to, and
/// writes the override that applies it.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>DefineOffset</c>, whose own doc comment is: "Finds out which
/// type of LilyPond object the offset will be applied to using ly.music, stores
/// this data and creates and inserts an override command."
/// </para>
/// <para>
/// ⚠ ODD BUT DELIBERATE, ported faithfully (standing rule 4b): the lookup table
/// has FOUR entries and everything else answers the literal grob name
/// <c>still testing!</c> — the module's own header says "This is only a very
/// first stub". That string is upstream's, it is not a message, and it is not
/// translated; it is what the panel shows for an object the table does not
/// know, and inserting an override for it produces
/// <c>\once \override still testing!.extra-offset = …</c>, which is exactly
/// what upstream produces. Nothing here "fixes" it.
/// </para>
/// </remarks>
public sealed class DefineOffset
{
    /// <summary>
    /// The grob (and context) an item type is overridden through.
    /// </summary>
    /// <remarks>Upstream's <c>item2objectDict</c>, verbatim. The keys are
    /// python-ly's own class names — <c>String</c> is <c>StringItem</c> here,
    /// which is the port's one renamed music item.</remarks>
    private static readonly Dictionary<string, (string Grob, string Context)> Table
        = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["String"] = ("TextScript", null),
            ["Markup"] = ("TextScript", null),
            ["Tempo"] = ("MetronomeMark", "Score"),
            ["Articulation"] = ("Script", null),
        };

    private readonly EditorDocument _document;

    /// <summary>Creates the helper for a document.</summary>
    /// <param name="document">The document.</param>
    public DefineOffset(EditorDocument document)
        => _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>Gets the document.</summary>
    public EditorDocument Document => _document;

    /// <summary>Gets the grob the last-found object is overridden through.</summary>
    public string LilyObject { get; private set; }

    /// <summary>Gets the context that grob lives in, or the empty string.</summary>
    public string LilyContext { get; private set; } = string.Empty;

    /// <summary>Gets where in the document the object was found.</summary>
    public int Position { get; private set; }

    /// <summary>
    /// Answers the grob name of the object at a place in the document, and
    /// remembers it.
    /// </summary>
    /// <param name="offset">Where the caret is.</param>
    /// <returns>The grob name.</returns>
    /// <remarks>Upstream's <c>getCurrentLilyObject()</c>: the FIRST music item
    /// inside the node at the cursor decides, and the node itself decides when
    /// it contains none.</remarks>
    public string GetCurrentLilyObject(int offset)
    {
        Position = offset;
        MusicTree music = DocumentInfo.For(_document).Music();
        MusicItem node = music.NodeAt(offset);
        if (node == null) { return ItemToObject(null); }

        foreach (MusicItem child in music.IterMusic(node))
        {
            return ItemToObject(child);
        }

        return ItemToObject(node);
    }

    /// <summary>Translates a music item into the name of its grob.</summary>
    /// <param name="item">The item, or null.</param>
    /// <returns>The grob name.</returns>
    /// <remarks>Upstream's <c>item2object()</c>.</remarks>
    public string ItemToObject(MusicItem item)
    {
        string name = UpstreamName(item);
        if (!Table.TryGetValue(name ?? string.Empty, out var found))
        {
            found = ("still testing!", null);
        }

        LilyObject = found.Grob;
        if (found.Context != null) { LilyContext = found.Context; }

        return LilyObject;
    }

    /// <summary>Writes the override into the document.</summary>
    /// <param name="x">The horizontal offset.</param>
    /// <param name="y">The vertical offset.</param>
    /// <param name="settings">The store the indent settings live in.</param>
    /// <remarks>
    /// Upstream's <c>insertOverride()</c>: the override goes on a line of its
    /// OWN, in front of the line the object is on, in ONE undo step, and the
    /// document is then reformatted so the new line is indented like its
    /// neighbours.
    /// </remarks>
    public void InsertOverride(double x, double y, SettingsStore settings = null)
    {
        if (LilyObject == null) { return; }

        var store = _document.Document;
        int position = Math.Clamp(Position, 0, store.TextLength);
        int lineStart = store.GetLineByOffset(position).Offset;

        store.Insert(lineStart, CreateOffsetOverride(x, y) + "\n");
        Reformatting.Reformat(_document, settings, lineStart, lineStart);
    }

    /// <summary>Builds the override command.</summary>
    /// <param name="x">The horizontal offset.</param>
    /// <param name="y">The vertical offset.</param>
    /// <returns>The command.</returns>
    /// <remarks>Upstream's <c>createOffsetOverride()</c>, including its two
    /// decimal places and its <c>Context.Grob</c> spelling.</remarks>
    public string CreateOffsetOverride(double x, double y)
    {
        string target = LilyContext ?? string.Empty;
        if (target.Length > 0) { target += "."; }

        target += LilyObject;
        return string.Format(
            CultureInfo.InvariantCulture,
            "\\once \\override {0}.extra-offset = #'({1:0.00} . {2:0.00})",
            target,
            x,
            y);
    }

    /// <summary>
    /// The name python-ly gives an item's class, which is what the table is
    /// keyed by.
    /// </summary>
    /// <param name="item">The item, or null.</param>
    /// <returns>The name, or null.</returns>
    /// <remarks>The port renamed exactly one music item — <c>items.String</c>
    /// became <c>StringItem</c>, because <c>String</c> is taken — so the table
    /// stays keyed by upstream's own names and one name is translated back.</remarks>
    public static string UpstreamName(MusicItem item)
    {
        if (item == null) { return null; }

        string name = item.GetType().Name;
        return name == "StringItem" ? "String" : name;
    }
}
