// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Fonts;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Fresco.Brix.DocumentFonts; //was previously: frescobaldi/fonts/musicfonts.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Something went wrong with a music font.</summary>
public class MusicFontException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong.</param>
    public MusicFontException(string message)
        : base(message)
    {
    }
}

/// <summary>A font could not be installed, typically for want of permission.</summary>
public sealed class MusicFontPermissionException : MusicFontException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong.</param>
    public MusicFontPermissionException(string message)
        : base(message)
    {
    }
}

/// <summary>A font could not be removed because it holds files this
/// application did not put there.</summary>
public sealed class MusicFontFileRemoveException : MusicFontException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The files, one per line.</param>
    public MusicFontFileRemoveException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// What a registered font file turned out to be.
/// </summary>
/// <remarks>
/// Upstream's own note: <c>MISSING</c> and <c>BROKEN_LINK</c> should never
/// occur, because a font object is only created from a file that was checked.
/// </remarks>
public enum MusicFontStatus
{
    /// <summary>A real file.</summary>
    File = 0,

    /// <summary>A name that was a file and is not any more.</summary>
    MissingFile = 1,

    /// <summary>A symbolic link to a file that is there.</summary>
    Link = 2,

    /// <summary>A symbolic link to a file that is not.</summary>
    BrokenLink = 3,

    /// <summary>Nothing is registered under that type and size.</summary>
    Missing = 4,
}

/// <summary>One font file inside a family.</summary>
public sealed class MusicFontFile
{
    /// <summary>Creates the entry.</summary>
    /// <param name="file">The file's path.</param>
    public MusicFontFile(string file) => File = file;

    /// <summary>Gets the file's path.</summary>
    public string File { get; }

    /// <summary>Gets or sets what the file turned out to be; null until asked.</summary>
    public MusicFontStatus? Status { get; set; }

    /// <summary>Gets or sets whether this file is to be installed.</summary>
    public bool Install { get; set; }
}

/// <summary>
/// A single music font family: the size-indexed faces and the brace face, in
/// each of the three file types a notation font is distributed in.
/// </summary>
/// <remarks>
/// A LilyPond music font is a set of files named
/// <c>&lt;family&gt;-&lt;size&gt;.&lt;type&gt;</c>, where the size is one of
/// eight design sizes or the word <c>brace</c>. That naming is not a
/// convention this application invented — it is how the engine asks for a
/// font: <c>FontInterface.SelectFont</c> appends the rounded design size, or
/// <c>-brace</c> for the brace encoding, and hands the result to
/// <c>AllFontMetrics.FindOtfFont</c>.
/// </remarks>
public sealed class MusicFontFamily
{
    /// <summary>The sizes a complete music font provides.</summary>
    public static readonly IReadOnlyList<string> SizesList =
        new[] { "11", "13", "14", "16", "18", "20", "23", "26" };

    /// <summary>The file types a family may hold, in upstream's own order.</summary>
    public static readonly IReadOnlyList<string> Types = new[] { "otf", "svg", "woff" };

    private static readonly Regex FontRegex = new Regex(
        @"^(?<family>.*)-(?<size>brace|\d\d)\.(?<type>otf|svg|woff)$",
        RegexOptions.CultureInvariant);

    private readonly Dictionary<string, Dictionary<string, MusicFontFile>> _files
        = new Dictionary<string, Dictionary<string, MusicFontFile>>(StringComparer.Ordinal)
        {
            ["otf"] = new Dictionary<string, MusicFontFile>(StringComparer.Ordinal),
            ["svg"] = new Dictionary<string, MusicFontFile>(StringComparer.Ordinal),
            ["woff"] = new Dictionary<string, MusicFontFile>(StringComparer.Ordinal),
        };

    /// <summary>Creates an empty family.</summary>
    public MusicFontFamily()
    {
    }

    /// <summary>Creates a family from one file.</summary>
    /// <param name="file">The file.</param>
    public MusicFontFamily(string file) => AddFile(file);

    /// <summary>Gets the family's name, or null while it holds nothing.</summary>
    public string Family { get; private set; }

    /// <summary>
    /// Reads a file name as a music font's, or answers null.
    /// </summary>
    /// <param name="file">The file, with or without a directory.</param>
    /// <returns>The family, size and type, or null when it is not one.</returns>
    /// <remarks>Upstream's <c>parse_filename</c>. The expression is greedy on
    /// the family, so <c>my-font-2-16.otf</c> is size 16 of family
    /// <c>my-font-2</c> rather than size 2 of <c>my-font</c>.</remarks>
    public static (string Family, string Size, string Type)? ParseFileName(string file)
    {
        if (string.IsNullOrEmpty(file)) { return null; }

        Match match = FontRegex.Match(System.IO.Path.GetFileName(file));
        return match.Success
            ? (match.Groups["family"].Value,
               match.Groups["size"].Value,
               match.Groups["type"].Value)
            : ((string, string, string)?)null;
    }

    /// <summary>
    /// Reads an EXISTING file as a music font's, or throws.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>The family, type and size.</returns>
    /// <exception cref="MusicFontException">It is not there, or not one.</exception>
    public static (string Family, string Type, string Size) CheckFile(string file)
    {
        if (!System.IO.File.Exists(file) && !IsSymbolicLink(file))
        {
            throw new MusicFontException("Not an existing file or link: " + file);
        }

        (string Family, string Size, string Type)? font = ParseFileName(file);
        if (font == null)
        {
            throw new MusicFontException(
                "File " + file + " does not appear to be a valid font file");
        }

        return (font.Value.Family, font.Value.Type, font.Value.Size);
    }

    /// <summary>Gets the files registered for one type, by size.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The files.</returns>
    public IReadOnlyDictionary<string, MusicFontFile> this[string type] => _files[type];

    /// <summary>Registers an already-parsed file. An existing entry is replaced.</summary>
    /// <param name="type">The type.</param>
    /// <param name="size">The size.</param>
    /// <param name="file">The file.</param>
    public void Add(string type, string size, string file)
        => _files[type][size] = new MusicFontFile(file);

    /// <summary>Registers a file, checking that it belongs to this family.</summary>
    /// <param name="file">The file.</param>
    /// <exception cref="MusicFontException">It is not a music font, or is
    /// another family's.</exception>
    public void AddFile(string file)
    {
        (string family, string type, string size) = CheckFile(file);
        if (Family != null && !string.Equals(Family, family, StringComparison.Ordinal))
        {
            throw new MusicFontException(
                "File " + file + " does not belong to font family " + Family);
        }

        Family ??= family;
        Add(type, size, file);
    }

    /// <summary>Marks every file for installation.</summary>
    public void FlagAllForInstall()
    {
        foreach (var type in _files.Values)
        {
            foreach (MusicFontFile file in type.Values) { file.Install = true; }
        }
    }

    /// <summary>
    /// Marks the files the target family has not already got.
    /// </summary>
    /// <param name="target">The family already installed.</param>
    public void FlagForInstall(MusicFontFamily target)
    {
        if (target == null) { throw new ArgumentNullException(nameof(target)); }

        foreach (var (type, sizes) in _files)
        {
            foreach (var (size, file) in sizes)
            {
                if (Present(Status(type, size)) && !Present(target.Status(type, size)))
                {
                    file.Install = true;
                }
            }
        }
    }

    /// <summary>Answers whether the family has a brace face of a type.</summary>
    /// <param name="type">The type.</param>
    /// <returns>Whether it has.</returns>
    public bool HasBrace(string type) => _files[type].ContainsKey("brace");

    /// <summary>Answers whether a type — or every type — is complete.</summary>
    /// <param name="type">The type, or null for all three.</param>
    /// <returns>Whether every size and the brace face are there.</returns>
    public bool IsComplete(string type = null)
        => type != null
            ? HasBrace(type) && MissingSizes(type).Count == 0
            : IsComplete("otf") && IsComplete("svg") && IsComplete("woff");

    /// <summary>Lists the sizes a type has not got.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The missing sizes, in the canonical order.</returns>
    public IReadOnlyList<string> MissingSizes(string type)
    {
        IReadOnlyList<string> present = Sizes(type);
        return SizesList.Where(size => !present.Contains(size)).ToList();
    }

    /// <summary>Forgets one type and size.</summary>
    /// <param name="type">The type.</param>
    /// <param name="size">The size.</param>
    public void Remove(string type, string size) => _files[type].Remove(size);

    /// <summary>Lists the sizes a type has, sorted.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The sizes.</returns>
    public IReadOnlyList<string> Sizes(string type)
        => _files[type].Keys.OrderBy(size => size, StringComparer.Ordinal).ToList();

    /// <summary>Answers what a type and size turned out to be.</summary>
    /// <param name="type">The type.</param>
    /// <param name="size">The size.</param>
    /// <returns>The status; cached after the first ask, as upstream caches it.</returns>
    public MusicFontStatus Status(string type, string size)
    {
        if (!_files[type].TryGetValue(size, out MusicFontFile font))
        {
            return MusicFontStatus.Missing;
        }

        if (font.Status == null)
        {
            font.Status = Classify(font.File);
        }

        return font.Status.Value;
    }

    /// <summary>Walks every registered file.</summary>
    /// <returns>The type, size and file of each.</returns>
    public IEnumerable<(string Type, string Size, MusicFontFile File)> Walk()
    {
        foreach (string type in Types)
        {
            foreach (var (size, file) in _files[type]) { yield return (type, size, file); }
        }
    }

    /// <summary>Answers whether a status means the file is usable.</summary>
    /// <param name="status">The status.</param>
    /// <returns>Whether it is.</returns>
    private static bool Present(MusicFontStatus status)
        => status == MusicFontStatus.File || status == MusicFontStatus.Link;

    /// <summary>Answers whether a path is a symbolic link.</summary>
    /// <param name="path">The path.</param>
    /// <returns>Whether it is.</returns>
    /// <remarks>//was previously: <c>pathlib.Path.is_symlink()</c>.
    /// <c>FileSystemInfo.LinkTarget</c> is the same question in .NET, and it
    /// answers for a link whose target is gone, which is what makes
    /// <see cref="MusicFontStatus.BrokenLink"/> reachable.</remarks>
    private static bool IsSymbolicLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget != null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Reads a path's status off the file system.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The status.</returns>
    private static MusicFontStatus Classify(string path)
    {
        if (IsSymbolicLink(path))
        {
            return System.IO.File.Exists(path)
                ? MusicFontStatus.Link
                : MusicFontStatus.BrokenLink;
        }

        return System.IO.File.Exists(path)
            ? MusicFontStatus.File
            : MusicFontStatus.MissingFile;
    }
}

/// <summary>
/// A list of music font families, gathered from a directory tree.
/// </summary>
public class MusicFontList
{
    /// <summary>The families, by name.</summary>
    protected readonly Dictionary<string, MusicFontFamily> Entries
        = new Dictionary<string, MusicFontFamily>(StringComparer.Ordinal);

    /// <summary>Registers a file under its family, creating the family if new.</summary>
    /// <param name="file">The file.</param>
    /// <exception cref="MusicFontException">It is not a music font.</exception>
    public void AddFile(string file)
    {
        (string family, string type, string size) = MusicFontFamily.CheckFile(file);
        if (!Entries.TryGetValue(family, out MusicFontFamily entry))
        {
            Entries[family] = new MusicFontFamily(file);
            return;
        }

        entry.Add(type, size, file);
    }

    /// <summary>Adds an assembled family, replacing one of the same name.</summary>
    /// <param name="family">The family.</param>
    public void AddFamily(MusicFontFamily family)
    {
        if (family == null) { throw new ArgumentNullException(nameof(family)); }

        Entries[family.Family] = family;
    }

    /// <summary>Registers every music font under a directory tree.</summary>
    /// <param name="root">The tree's root; a missing one adds nothing.</param>
    /// <remarks>Upstream ignores anything that is not a music font, silently —
    /// a repository is an ordinary folder full of other things too.</remarks>
    public void AddTree(string root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) { return; }

        foreach (string file in Directory.EnumerateFiles(
            root, "*", SearchOption.AllDirectories))
        {
            try
            {
                AddFile(file);
            }
            catch (MusicFontException)
            {
                //Not a music font. Upstream's own bare except, narrowed to the
                //one exception the check can actually raise.
            }
        }
    }

    /// <summary>Forgets everything.</summary>
    public virtual void Clear() => Entries.Clear();

    /// <summary>Lists the family names, sorted.</summary>
    /// <returns>The names.</returns>
    public IReadOnlyList<string> Families()
        => Entries.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();

    /// <summary>Answers a family by name, or null.</summary>
    /// <param name="name">The family name.</param>
    /// <returns>The family, or null.</returns>
    public MusicFontFamily Family(string name)
        => name != null && Entries.TryGetValue(name, out MusicFontFamily entry)
            ? entry
            : null;

    /// <summary>Walks every file of every family.</summary>
    /// <returns>The family, its name, the type, the size and the file.</returns>
    public IEnumerable<(MusicFontFamily Family, string Name, string Type, string Size,
        MusicFontFile File)> Walk()
    {
        foreach (string name in Families())
        {
            MusicFontFamily family = Entries[name];
            foreach (var (type, size, file) in family.Walk())
            {
                yield return (family, name, type, size, file);
            }
        }
    }
}

/// <summary>
/// A folder of music fonts to install FROM — upstream's "music font
/// repository".
/// </summary>
public sealed class MusicFontRepo : MusicFontList
{
    /// <summary>Creates the repository over a directory tree.</summary>
    /// <param name="root">The tree's root.</param>
    public MusicFontRepo(string root)
    {
        Root = root;
        AddTree(root);
    }

    /// <summary>Gets the tree's root.</summary>
    public string Root { get; }

    /// <summary>Gets the files that would be installed by the last
    /// <see cref="FlagForInstall"/>.</summary>
    public MusicFontList InstallableFonts { get; } = new MusicFontList();

    /// <summary>Works out what this repository can add to an installation.</summary>
    /// <param name="installed">The fonts already installed.</param>
    public void FlagForInstall(InstalledMusicFonts installed)
    {
        if (installed == null) { throw new ArgumentNullException(nameof(installed)); }

        InstallableFonts.Clear();
        foreach (string name in Families())
        {
            MusicFontFamily repoFamily = Family(name);
            MusicFontFamily targetFamily = installed.Family(name);
            if (targetFamily == null)
            {
                repoFamily.FlagAllForInstall();
            }
            else
            {
                repoFamily.FlagForInstall(targetFamily);
            }
        }

        foreach (var entry in Walk())
        {
            if (entry.File.Install) { InstallableFonts.AddFile(entry.File.File); }
        }
    }

    /// <summary>Installs everything flagged.</summary>
    /// <param name="target">Where to install to.</param>
    /// <returns>How many files were installed.</returns>
    public int InstallFlagged(InstalledMusicFonts target)
    {
        if (target == null) { throw new ArgumentNullException(nameof(target)); }

        int installed = 0;
        foreach (var entry in InstallableFonts.Walk())
        {
            target.Install(entry.Type, entry.File.File);
            installed++;
        }

        return installed;
    }
}

/// <summary>
/// The music fonts installed in the application's OWN font folder — the folder
/// registered into the engine's <c>FontAssets.SearchPaths</c>, which the engine
/// consults before its embedded copies.
/// </summary>
/// <remarks>
/// <para>
/// //was previously: upstream installs into the LilyPond INSTALLATION's
/// <c>&lt;datadir&gt;/fonts/{otf,svg}</c>, which is why it links rather than
/// copies (so as not to fill a system directory) and why it refuses to remove
/// anything that is a real file (so as not to damage the fonts LilyPond itself
/// shipped). Neither reason survives here, and both were re-decided rather than
/// transcribed:
/// </para>
/// <para>
/// (1) THE FOLDER. There is no installation to write into. The engine's own
/// faces are EMBEDDED RESOURCES of <c>CodeBrix.LilyPort.Engine</c>, and its one
/// filesystem hook is <c>FontAssets.SearchPaths</c>, a list of directories
/// consulted before them. So the folder is the application's own, beside its
/// settings — the same place <c>LilyPortScorePdfFonts</c> already writes the
/// engine's text faces to — and it is FLAT, because the engine's lookup is
/// <c>Path.Combine(directory, fileName)</c> with no per-type subfolder.
/// </para>
/// <para>
/// (2) COPY, NOT LINK. Upstream's own <c>install()</c> already takes a
/// <c>copy</c> argument and passes it on Windows; here it is always taken. A
/// link into a folder the application owns would break the day the user moved
/// the file it points at, and a music font the engine cannot find is a FATAL
/// error naming the font — so the honest thing for a folder that exists to keep
/// fonts working is to keep the bytes. <see cref="MusicFontStatus"/> is ported
/// whole all the same, because a user may still link a file in by hand and the
/// list has to say so.
/// </para>
/// <para>
/// (3) REMOVE. Upstream refuses to remove real files. What that rule protects
/// is files the application did not put there; inside a folder the application
/// owns, everything is. The refusal is therefore scoped to a file that lies
/// OUTSIDE the folder — which is what a hand-made link's target would be, and
/// the only way the list can hold one.
/// </para>
/// </remarks>
public sealed class InstalledMusicFonts : MusicFontList
{
    private readonly string _root;

    /// <summary>Creates the list over a folder.</summary>
    /// <param name="root">The folder, or null for the application's own.</param>
    public InstalledMusicFonts(string root = null)
    {
        _root = root ?? DefaultDirectory();
        Reload();
    }

    /// <summary>Gets the folder the fonts live in.</summary>
    public string FontRoot => _root;

    /// <summary>Gets the folder the application keeps installed music fonts in.</summary>
    /// <returns><c>&lt;ApplicationData&gt;/Fresco.Brix/fonts/music</c>.</returns>
    public static string DefaultDirectory()
        => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppInfo.AppName, "fonts", "music");

    /// <summary>
    /// Puts the application's music-font folder in front of the engine's
    /// embedded faces, once per process.
    /// </summary>
    /// <param name="directory">The folder, or null for the default.</param>
    /// <returns>The folder registered.</returns>
    /// <remarks>
    /// <c>FontAssets.SearchPaths</c> is an ordinary list the engine appends to
    /// through <c>BatchRunner.UseFontsFrom</c>; adding the same folder twice
    /// would search it twice, so the list is checked first. The folder is made
    /// whether or not anything is in it, because the engine only ever READS it.
    /// </remarks>
    public static string Register(string directory = null)
    {
        string folder = directory ?? DefaultDirectory();
        Directory.CreateDirectory(folder);

        IList<string> paths = FontAssets.SearchPaths;
        if (!paths.Any(path => string.Equals(path, folder, StringComparison.Ordinal)))
        {
            paths.Add(folder);
        }

        return folder;
    }

    /// <summary>Reads the folder again.</summary>
    public void Reload()
    {
        Clear();
        Directory.CreateDirectory(_root);
        AddTree(_root);
    }

    /// <summary>
    /// Answers where a type's files go. One folder for all three, see the class
    /// remarks.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The folder.</returns>
    public string FontDirectory(string type) => _root;

    /// <summary>Installs one font file.</summary>
    /// <param name="type">The type.</param>
    /// <param name="fontFile">The file to install.</param>
    /// <exception cref="MusicFontPermissionException">It could not be copied.</exception>
    public void Install(string type, string fontFile)
    {
        string target = System.IO.Path.Combine(
            FontDirectory(type), System.IO.Path.GetFileName(fontFile));
        try
        {
            Directory.CreateDirectory(FontDirectory(type));
            System.IO.File.Copy(fontFile, target, overwrite: true);
        }
        catch (IOException error)
        {
            throw new MusicFontPermissionException(
                I18n.Get("Font installation failed:") + "\n" + error.Message);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new MusicFontPermissionException(
                I18n.Get("Font installation failed:") + "\n" + error.Message);
        }

        AddFile(target);
    }

    /// <summary>Removes whole families.</summary>
    /// <param name="familyNames">The families to remove.</param>
    /// <exception cref="MusicFontFileRemoveException">A family holds a file
    /// outside the application's folder; nothing is removed.</exception>
    /// <exception cref="MusicFontPermissionException">A file would not go.</exception>
    public void Remove(IReadOnlyList<string> familyNames)
    {
        if (familyNames == null) { throw new ArgumentNullException(nameof(familyNames)); }

        //Upstream checks the whole family BEFORE removing anything, so a
        //refusal leaves the family as it was rather than half gone.
        List<string> foreign = new List<string>();
        foreach (string name in familyNames)
        {
            MusicFontFamily family = Family(name);
            if (family == null) { continue; }

            foreach (var (_, _, file) in family.Walk())
            {
                if (!IsInsideRoot(file.File)) { foreign.Add(file.File); }
            }
        }

        if (foreign.Count > 0)
        {
            throw new MusicFontFileRemoveException(string.Join("\n", foreign));
        }

        foreach (string name in familyNames)
        {
            MusicFontFamily family = Family(name);
            if (family == null) { continue; }

            try
            {
                foreach (var (_, _, file) in family.Walk())
                {
                    if (System.IO.File.Exists(file.File)
                        || new FileInfo(file.File).LinkTarget != null)
                    {
                        System.IO.File.Delete(file.File);
                    }
                }
            }
            catch (IOException error)
            {
                throw new MusicFontPermissionException(
                    I18n.Get("Font removal failed:") + "\n" + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                throw new MusicFontPermissionException(
                    I18n.Get("Font removal failed:") + "\n" + error.Message);
            }

            Entries.Remove(name);
        }
    }

    /// <summary>Answers whether a path lies inside the application's folder.</summary>
    /// <param name="path">The path.</param>
    /// <returns>Whether it does.</returns>
    private bool IsInsideRoot(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        string root = System.IO.Path.GetFullPath(_root);
        if (!root.EndsWith(System.IO.Path.DirectorySeparatorChar))
        {
            root += System.IO.Path.DirectorySeparatorChar;
        }

        return full.StartsWith(root, StringComparison.Ordinal);
    }
}

/// <summary>
/// One row of the music-font list: a family and what it holds, ready to show.
/// </summary>
/// <remarks>//was previously: <c>MusicFontsModel</c>, whose seven columns are
/// the family and a checked/partial/unchecked pair per type. The columns are
/// data here and the tree draws them (board trap 45).</remarks>
public sealed class MusicFontRow
{
    /// <summary>Creates a row from a family.</summary>
    /// <param name="family">The family.</param>
    public MusicFontRow(MusicFontFamily family)
    {
        if (family == null) { throw new ArgumentNullException(nameof(family)); }

        Family = family.Family;
        Types = MusicFontFamily.Types
            .Select(type => new MusicFontTypeState(
                type,
                family.MissingSizes(type),
                family.HasBrace(type)))
            .ToList();
    }

    /// <summary>Gets the family's name.</summary>
    public string Family { get; }

    /// <summary>Gets what the family holds of each type, in upstream's order.</summary>
    public IReadOnlyList<MusicFontTypeState> Types { get; }

    /// <summary>Gets the row as one line of text.</summary>
    /// <returns>The line.</returns>
    public string Describe()
        => Family + "  " + string.Join(
            "  ", Types.Select(type => type.Describe()));
}

/// <summary>What a family holds of one file type.</summary>
public sealed class MusicFontTypeState
{
    /// <summary>Creates the state.</summary>
    /// <param name="type">The type.</param>
    /// <param name="missingSizes">The sizes it has not got.</param>
    /// <param name="hasBrace">Whether it has a brace face.</param>
    public MusicFontTypeState(
        string type, IReadOnlyList<string> missingSizes, bool hasBrace)
    {
        Type = type;
        MissingSizes = missingSizes ?? Array.Empty<string>();
        HasBrace = hasBrace;
    }

    /// <summary>Gets the file type.</summary>
    public string Type { get; }

    /// <summary>Gets the sizes the type has not got.</summary>
    public IReadOnlyList<string> MissingSizes { get; }

    /// <summary>Gets whether the type has a brace face.</summary>
    public bool HasBrace { get; }

    /// <summary>Gets whether the type has nothing at all.</summary>
    public bool IsEmpty => MissingSizes.Count == MusicFontFamily.SizesList.Count;

    /// <summary>Gets whether every size is there.</summary>
    public bool IsComplete => MissingSizes.Count == 0;

    /// <summary>Describes the type the way the list shows it.</summary>
    /// <returns>The description.</returns>
    /// <remarks>Upstream draws a tri-state tick and writes "Missing: 11, 13"
    /// beside a partial one; the three states are said in words here because
    /// the tree draws text (board trap 45).</remarks>
    public string Describe()
    {
        string label = Type.ToUpperInvariant();
        string sizes = IsComplete
            ? "✓"
            : IsEmpty
                ? "–"
                : I18n.Get("Missing:") + " " + string.Join(", ", MissingSizes);
        string brace = HasBrace ? "✓" : "–";
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} ({2} {3})", label, sizes, I18n.Get("(Brace)"), brace);
    }
}
