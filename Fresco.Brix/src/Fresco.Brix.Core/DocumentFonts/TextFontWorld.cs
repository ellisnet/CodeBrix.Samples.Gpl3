// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Fonts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Fresco.Brix.DocumentFonts; //was previously: frescobaldi/fonts/textfonts.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One text face: the family it declares and where its bytes live.</summary>
public sealed class TextFontFace
{
    /// <summary>Creates the face.</summary>
    /// <param name="family">The family the face declares.</param>
    /// <param name="location">Where the bytes are.</param>
    /// <param name="isDocumentSupplied">Whether the DOCUMENT supplied it.</param>
    public TextFontFace(string family, string location, bool isDocumentSupplied)
    {
        Family = family;
        Location = location ?? string.Empty;
        IsDocumentSupplied = isDocumentSupplied;
    }

    /// <summary>Gets the family the face declares.</summary>
    public string Family { get; }

    /// <summary>Gets where the face's bytes live.</summary>
    /// <remarks>A vendored face has no path on disk — it is an embedded
    /// resource — so the engine names the assembly and the resource, joined so
    /// the whole thing reads as a path. A document-supplied face has a real
    /// path, because the document handed one over.</remarks>
    public string Location { get; }

    /// <summary>Gets whether the document supplied this face.</summary>
    public bool IsDocumentSupplied { get; }

    /// <summary>Gets the last segment of the location: the face's own file name.</summary>
    public string FileName
    {
        get
        {
            int slash = Location.LastIndexOf('/');
            int backslash = Location.LastIndexOf('\\');
            int cut = Math.Max(slash, backslash);
            string tail = cut < 0 ? Location : Location.Substring(cut + 1);

            //An embedded resource's name is dotted the whole way — the tail is
            //`CodeBrix.LilyPort.Engine.Fonts.text.C059-Roman.otf'. What the
            //user wants to see is the face, which is the last two dotted parts.
            int suffix = tail.LastIndexOf('.');
            if (suffix <= 0) { return tail; }

            int name = tail.LastIndexOf('.', suffix - 1);
            return name < 0 ? tail : tail.Substring(name + 1);
        }
    }
}

/// <summary>
/// One name a document can put in its <c>\paper</c> block, and the faces the
/// engine reaches through it.
/// </summary>
public sealed class TextFontSelector
{
    /// <summary>Creates the choice.</summary>
    /// <param name="name">The name a document asks for.</param>
    /// <param name="faces">The faces it reaches, in fallback order.</param>
    /// <param name="isDocumentSupplied">Whether the document supplied it.</param>
    public TextFontSelector(
        string name, IReadOnlyList<TextFontFace> faces, bool isDocumentSupplied)
    {
        Name = name;
        Faces = faces ?? Array.Empty<TextFontFace>();
        IsDocumentSupplied = isDocumentSupplied;
    }

    /// <summary>Gets the name that goes in the <c>\paper</c> block.</summary>
    public string Name { get; }

    /// <summary>Gets the faces the engine reaches through it.</summary>
    public IReadOnlyList<TextFontFace> Faces { get; }

    /// <summary>Gets whether the document supplied it.</summary>
    public bool IsDocumentSupplied { get; }

    /// <summary>Gets the families the faces belong to, in order, without
    /// repeats.</summary>
    public IReadOnlyList<string> FamilyNames
    {
        get
        {
            List<string> names = new List<string>();
            foreach (TextFontFace face in Faces)
            {
                if (!names.Contains(face.Family, StringComparer.Ordinal))
                {
                    names.Add(face.Family);
                }
            }

            return names;
        }
    }
}

/// <summary>One text-font family and the faces that declare it.</summary>
public sealed class TextFontFamily
{
    /// <summary>Creates the family.</summary>
    /// <param name="name">The family name.</param>
    /// <param name="faces">Its faces, in the order the engine lists them.</param>
    public TextFontFamily(string name, IReadOnlyList<TextFontFace> faces)
    {
        Name = name;
        Faces = faces ?? Array.Empty<TextFontFace>();
    }

    /// <summary>Gets the family name — what goes in the <c>\paper</c> block.</summary>
    public string Name { get; }

    /// <summary>Gets the faces that declare this family.</summary>
    public IReadOnlyList<TextFontFace> Faces { get; }

    /// <summary>Gets whether the document supplied this family.</summary>
    public bool IsDocumentSupplied => Faces.Count > 0 && Faces[0].IsDocumentSupplied;
}

/// <summary>
/// The text fonts a document can ask for: the port's OWN font world.
/// </summary>
/// <remarks>
/// <para>
/// //was previously: <c>textfonts.TextFonts</c>, which runs
/// <c>lilypond -dshow-available-fonts</c> in a subprocess and parses the
/// fontconfig dump it prints — families, "Config files:", "Font dir:",
/// "Config dir:" — into a family/subfamily/style tree.
/// </para>
/// <para>
/// ⚠ RULING R18. The engine's <c>ly:font-config-display-fonts</c> is the
/// project's one deliberate exception to reporting exactly what upstream
/// reports: there is no host font world to report, and there never will be, so
/// it reports its OWN — the 24 vendored faces and whatever the document
/// registered. By construction NO SYSTEM FONT CAN APPEAR HERE, which is
/// standing rule 6 arriving from the other direction.
/// </para>
/// <para>
/// ⚠ MEASURED 2026-09-01, and it is why <see cref="Load"/> reads the two lists
/// rather than running the engine: the primitive writes to
/// <c>interpreter.ErrorWriter</c>, which <c>LilyPondScheme.CreateInterpreter</c>
/// binds ONCE to whatever <c>Flower.Warn.Output</c> was at interpreter
/// creation. <c>BatchRunner</c> swaps <c>Flower.Warn.Output</c> for the run's
/// <c>MessageWriter</c> but not the interpreter's error writer, so a hosted
/// application cannot capture the listing from a run: engraving a file holding
/// <c>#(ly:font-config-display-fonts)</c> printed the whole world to the
/// process's console and the job's log caught only "Processing…". Recorded on
/// the package FIXLIST. <see cref="Parse"/> exists and is tested against the
/// engine's REAL captured output all the same, so the two halves are held
/// together: the listing's format is a contract, and
/// <see cref="Load"/> answers what a parsed listing would.
/// </para>
/// </remarks>
public sealed class TextFontWorld
{
    /// <summary>Creates the world.</summary>
    /// <param name="families">The families, sorted.</param>
    public TextFontWorld(IReadOnlyList<TextFontFamily> families)
        => Families = families ?? Array.Empty<TextFontFamily>();

    /// <summary>Gets the families, sorted the way upstream sorts them.</summary>
    /// <remarks>Upstream: <c>sorted(families.keys(), key=lambda s: s.lower())</c>.</remarks>
    public IReadOnlyList<TextFontFamily> Families { get; }

    /// <summary>Gets how many faces the world holds.</summary>
    public int FaceCount => Families.Sum(family => family.Faces.Count);

    /// <summary>Gets the families the document itself supplied.</summary>
    public IReadOnlyList<TextFontFamily> DocumentSupplied
        => Families.Where(family => family.IsDocumentSupplied).ToList();

    /// <summary>
    /// The family names a document can actually ASK FOR, and the vendored
    /// families each one reaches, in fallback order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ MEASURED 2026-09-01, and this table is why the list the dialog shows
    /// is not simply <see cref="Families"/>. The engine resolves a text-font
    /// request through <c>TextFontChain.For</c>, which consults the DOCUMENT's
    /// own registrations (R16) and then a table of GENERIC names — and NEVER
    /// the family name a vendored face declares. So
    /// <c>property-defaults.fonts.serif = "Nimbus Sans"</c> does not select
    /// Nimbus Sans: it falls into ruling R14's <c>unknown</c> arm and engraves
    /// in TeX Gyre Schola, byte for byte identically to
    /// <c>= "Zzz Not A Font"</c>. All six of the families
    /// <c>ly:font-config-display-fonts</c> reports behave that way.
    /// </para>
    /// <para>
    /// A list of the six declared families would therefore be INERT — every
    /// choice on it produces the same score. What the user can actually choose
    /// between is this: the engine's own selectable names, each shown with the
    /// faces it reaches. The gap is recorded on the package FIXLIST for a
    /// LilyPort session; nothing outside this repository is changed for it.
    /// </para>
    /// <para>
    /// The table mirrors the engine's own two — <c>TextFontChain.Generics</c>
    /// crossed with <c>TextFontChain.Families</c> — in the engine's own order,
    /// exactly as <see cref="MusicView.LilyPortTypefaceResolver"/> already
    /// mirrors them for the view (board trap 60).
    /// </para>
    /// </remarks>
    /// <remarks>⚠ FR13 EXEMPT, ruled at W13's close-out sweep. Three of these
    /// names contain "LilyPond" and they are shown to the user in a picker,
    /// which the ruling normally forbids. They are exempt because the label IS
    /// THE VALUE: what the picker offers is written verbatim into the
    /// document's own <c>\paper</c> block and handed to the engine, which
    /// resolves these exact strings. A display name of "LilyPort Serif" would
    /// offer a font the engine does not know, and the user reading their own
    /// file would find a name the application never showed them. The generic
    /// selectors above reach the same faces for anyone who prefers them.
    /// </remarks>
    public static readonly IReadOnlyList<(string Name, IReadOnlyList<string> Reaches)>
        Selectors = new[]
        {
            ("serif", (IReadOnlyList<string>)new[] { "C059", "TeX Gyre Schola" }),
            ("sans", new[] { "Nimbus Sans", "TeX Gyre Heros" }),
            ("sans-serif", new[] { "Nimbus Sans", "TeX Gyre Heros" }),
            ("monospace", new[] { "Nimbus Mono PS", "TeX Gyre Cursor" }),
            ("LilyPond Serif", new[] { "C059", "TeX Gyre Schola" }),
            ("LilyPond Sans Serif", new[] { "Nimbus Sans", "TeX Gyre Heros" }),
            ("LilyPond Monospace", new[] { "Nimbus Mono PS", "TeX Gyre Cursor" }),
        };

    /// <summary>
    /// Lists what a document can ask for: the engine's selectable names with
    /// the faces behind each, then whatever the document supplied.
    /// </summary>
    /// <returns>The choices, each with the faces it reaches.</returns>
    /// <remarks>A document-supplied family is an EXACT match in the engine
    /// (R16), so it selects itself and appears under its own name.</remarks>
    public IReadOnlyList<TextFontSelector> SelectableNames()
    {
        Dictionary<string, TextFontFamily> byName
            = new Dictionary<string, TextFontFamily>(StringComparer.OrdinalIgnoreCase);
        foreach (TextFontFamily family in Families) { byName[family.Name] = family; }

        List<TextFontSelector> choices = new List<TextFontSelector>();
        foreach ((string name, IReadOnlyList<string> reaches) in Selectors)
        {
            List<TextFontFace> faces = new List<TextFontFace>();
            foreach (string reached in reaches)
            {
                if (byName.TryGetValue(reached, out TextFontFamily family))
                {
                    faces.AddRange(family.Faces);
                }
            }

            //A selector no vendored face answers is not offered: it would draw
            //tofu, which is the desired FAILURE mode and not a desired choice.
            if (faces.Count > 0) { choices.Add(new TextFontSelector(name, faces, false)); }
        }

        foreach (TextFontFamily family in DocumentSupplied)
        {
            choices.Add(new TextFontSelector(family.Name, family.Faces, true));
        }

        return choices;
    }

    /// <summary>
    /// Reads the engine's font world: its vendored faces, then the document's.
    /// </summary>
    /// <returns>The world.</returns>
    /// <remarks>The two calls are exactly the two lists
    /// <c>ly:font-config-display-fonts</c> writes, in the order it writes
    /// them — see the class remarks.</remarks>
    public static TextFontWorld Load()
    {
        List<TextFontFace> faces = new List<TextFontFace>();
        foreach (TextFace face in TextFontChain.VendoredFaces())
        {
            faces.Add(new TextFontFace(
                face.FamilyName,
                FontAssets.TextFontLocation(face.FileName),
                isDocumentSupplied: false));
        }

        foreach (var entry in TextFontChain.DocumentFontRegistrations())
        {
            faces.Add(new TextFontFace(
                entry.Key, entry.Value?.SourcePath, isDocumentSupplied: true));
        }

        return new TextFontWorld(Group(faces));
    }

    /// <summary>
    /// Reads a listing the engine's <c>ly:font-config-display-fonts</c> printed.
    /// </summary>
    /// <param name="listing">The printed listing.</param>
    /// <returns>The world it describes.</returns>
    /// <remarks>
    /// The format is the engine's own and is two counted sections of
    /// <c>"  &lt;family&gt; -- &lt;location&gt;"</c> lines:
    /// <c>vendored faces (24):</c> then <c>document-supplied fonts (n):</c>.
    /// Anything else on a line is ignored, which is what lets a caller hand
    /// over a whole captured log.
    /// </remarks>
    public static TextFontWorld Parse(string listing)
    {
        List<TextFontFace> faces = new List<TextFontFace>();
        bool documentSupplied = false;

        foreach (string raw in (listing ?? string.Empty).Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("vendored faces", StringComparison.Ordinal))
            {
                documentSupplied = false;
                continue;
            }

            if (line.StartsWith("document-supplied fonts", StringComparison.Ordinal))
            {
                documentSupplied = true;
                continue;
            }

            if (!line.StartsWith("  ", StringComparison.Ordinal)) { continue; }

            int separator = line.IndexOf(" -- ", StringComparison.Ordinal);
            if (separator < 0) { continue; }

            faces.Add(new TextFontFace(
                line.Substring(2, separator - 2),
                line.Substring(separator + 4),
                documentSupplied));
        }

        return new TextFontWorld(Group(faces));
    }

    /// <summary>
    /// Filters the families the way upstream's proxy model does.
    /// </summary>
    /// <param name="pattern">The user's regular expression; empty matches all.</param>
    /// <param name="installedNotationFonts">The music-font family names, lower
    /// case, which are never shown as TEXT fonts.</param>
    /// <returns>The families that survive.</returns>
    /// <remarks>
    /// Upstream's <c>FontFilterProxyModel</c>: "Child elements are never
    /// filtered. Font names that are also in the list of installed notation
    /// fonts are always filtered." The expression is case-insensitive and
    /// matches anywhere in the name, which is what
    /// <c>QSortFilterProxyModel.setFilterRegularExpression</c> does; a pattern
    /// that will not compile matches nothing rather than throwing (board trap
    /// 48's discipline — a user types these as they go).
    /// </remarks>
    public IReadOnlyList<TextFontFamily> Filter(
        string pattern, IReadOnlyCollection<string> installedNotationFonts = null)
        => Sift(Families, family => family.Name, pattern, installedNotationFonts);

    /// <summary>
    /// Filters the SELECTABLE names the same way — what the dialog's tree
    /// shows.
    /// </summary>
    /// <param name="pattern">The user's regular expression; empty matches all.</param>
    /// <param name="installedNotationFonts">The music-font family names, which
    /// are never offered as TEXT fonts.</param>
    /// <returns>The choices that survive.</returns>
    public IReadOnlyList<TextFontSelector> FilterSelectable(
        string pattern, IReadOnlyCollection<string> installedNotationFonts = null)
        => Sift(
            SelectableNames(), choice => choice.Name, pattern, installedNotationFonts);

    /// <summary>Applies upstream's two filters to a list.</summary>
    /// <typeparam name="T">What is being filtered.</typeparam>
    /// <param name="entries">The entries.</param>
    /// <param name="name">How to read an entry's name.</param>
    /// <param name="pattern">The user's regular expression.</param>
    /// <param name="hiddenNames">Names never shown.</param>
    /// <returns>What survives.</returns>
    private static IReadOnlyList<T> Sift<T>(
        IEnumerable<T> entries,
        Func<T, string> name,
        string pattern,
        IReadOnlyCollection<string> hiddenNames)
    {
        IEnumerable<T> remaining = entries;
        if (hiddenNames != null && hiddenNames.Count > 0)
        {
            HashSet<string> hidden = new HashSet<string>(
                hiddenNames, StringComparer.OrdinalIgnoreCase);
            remaining = remaining.Where(entry => !hidden.Contains(name(entry)));
        }

        if (string.IsNullOrEmpty(pattern)) { return remaining.ToList(); }

        Regex expression;
        try
        {
            expression = new Regex(
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return Array.Empty<T>();
        }

        List<T> matched = new List<T>();
        foreach (T entry in remaining)
        {
            try
            {
                if (expression.IsMatch(name(entry))) { matched.Add(entry); }
            }
            catch (RegexMatchTimeoutException)
            {
                //A pattern that will not finish matches nothing, rather than
                //hanging the window while the user is still typing it.
            }
        }

        return matched;
    }

    /// <summary>
    /// Describes where the engine looks for fonts, for the Miscellaneous tab.
    /// </summary>
    /// <param name="musicFontFolder">The application's own music-font folder.</param>
    /// <returns>The three groups upstream's Miscellaneous tab shows, said about
    /// the port's own world.</returns>
    /// <remarks>
    /// //was previously: <c>MiscTreeModel</c>, whose three groups are
    /// fontconfig's Configuration Files, Configuration Directories and Searched
    /// Font Directories. The port has no fontconfig and D23 forbids ever
    /// acquiring one, so the three groups say what is true here instead: the
    /// override folders the engine searches first (rule R18's own answer, and
    /// what <c>AllFontMetrics</c> prints when it cannot find a music font), the
    /// embedded store behind them, and the faces the document itself supplied.
    /// </remarks>
    public IReadOnlyList<(string Title, IReadOnlyList<string> Entries)> Describe(
        string musicFontFolder = null)
    {
        List<string> searched = FontAssets.SearchPaths
            .Where(path => !string.IsNullOrEmpty(path))
            .ToList();
        if (!string.IsNullOrEmpty(musicFontFolder)
            && !searched.Any(path => string.Equals(
                path, musicFontFolder, StringComparison.Ordinal)))
        {
            searched.Add(musicFontFolder);
        }

        List<string> embedded = new List<string>
        {
            typeof(FontAssets).Assembly.GetName().Name + ".dll",
        };

        List<string> supplied = DocumentSupplied
            .SelectMany(family => family.Faces)
            .Select(face => face.Family + " — " + face.Location)
            .ToList();

        return new[]
        {
            (Services.I18n.Get("Searched Font Directories"), (IReadOnlyList<string>)searched),
            (Services.I18n.Get("Fonts built into the program"), (IReadOnlyList<string>)embedded),
            (Services.I18n.Get("Fonts supplied by the document"), (IReadOnlyList<string>)supplied),
        };
    }

    /// <summary>Writes the world the way the engine's primitive writes it.</summary>
    /// <returns>The listing.</returns>
    /// <remarks>Only for showing a user what the engine would print; nothing
    /// reads it back.</remarks>
    public string ToListing()
    {
        List<TextFontFace> vendored = Families
            .Where(family => !family.IsDocumentSupplied)
            .SelectMany(family => family.Faces).ToList();
        List<TextFontFace> supplied = Families
            .Where(family => family.IsDocumentSupplied)
            .SelectMany(family => family.Faces).ToList();

        StringBuilder text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"vendored faces ({vendored.Count}):\n");
        foreach (TextFontFace face in vendored)
        {
            text.Append(CultureInfo.InvariantCulture, $"  {face.Family} -- {face.Location}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"document-supplied fonts ({supplied.Count}):\n");
        foreach (TextFontFace face in supplied)
        {
            text.Append(CultureInfo.InvariantCulture, $"  {face.Family} -- {face.Location}\n");
        }

        return text.ToString();
    }

    /// <summary>Groups faces into families, in upstream's own order.</summary>
    /// <param name="faces">The faces.</param>
    /// <returns>The families.</returns>
    private static IReadOnlyList<TextFontFamily> Group(IReadOnlyList<TextFontFace> faces)
    {
        Dictionary<string, List<TextFontFace>> grouped
            = new Dictionary<string, List<TextFontFace>>(StringComparer.Ordinal);
        foreach (TextFontFace face in faces)
        {
            if (!grouped.TryGetValue(face.Family, out List<TextFontFace> list))
            {
                grouped[face.Family] = list = new List<TextFontFace>();
            }

            list.Add(face);
        }

        return grouped
            .OrderBy(entry => entry.Key.ToLowerInvariant(), StringComparer.Ordinal)
            .Select(entry => new TextFontFamily(entry.Key, entry.Value))
            .ToList();
    }
}
