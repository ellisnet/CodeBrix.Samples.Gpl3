// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Lex;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LyDocument = Fresco.Brix.Ly.Document;
using MusicTree = Fresco.Brix.Ly.Music.Document;

namespace Fresco.Brix.Documents; //was previously: frescobaldi/fileinfo.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// What can be worked out about a <c>.ly</c> file ON DISK — as opposed to one
/// open in the editor: its tokenization, its information, the files it
/// includes, and the base names a run of it is expected to write.
/// </summary>
/// <remarks>
/// The answers are cached against the file's modification time, because a
/// document that includes twenty others asks about all of them on every
/// engrave.
/// </remarks>
public static class LyFileInfo
{
    private static readonly Regex SuffixChars
        = new Regex(@"[^-\w]", RegexOptions.Compiled);

    private static readonly FileCache<CachedDocument> Cache
        = new FileCache<CachedDocument>();

    /// <summary>Gets the tokenized document for a file.</summary>
    /// <param name="fileName">The file.</param>
    /// <returns>The document.</returns>
    public static LyDocument Document(string fileName) => Cached(fileName).Document;

    /// <summary>Gets the document information for a file.</summary>
    /// <param name="fileName">The file.</param>
    /// <returns>The information.</returns>
    public static LyDocInfo DocInfo(string fileName)
    {
        CachedDocument cached = Cached(fileName);
        return cached.DocInfo ??= new LyDocInfo(cached.Document, cached.Variables);
    }

    /// <summary>Gets the music tree for a file.</summary>
    /// <param name="fileName">The file.</param>
    /// <returns>The music tree.</returns>
    public static MusicTree Music(string fileName)
    {
        CachedDocument cached = Cached(fileName);
        return cached.Music ??= new MusicTree(cached.Document);
    }

    /// <summary>Forgets everything cached about files.</summary>
    public static void ClearCache() => Cache.Clear();

    /// <summary>
    /// Works out the mode of a piece of text: the <c>mode</c> variable when it
    /// declares one, and otherwise a guess from the contents.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="guess">Whether to guess when nothing is declared.</param>
    /// <returns>The mode, or null.</returns>
    public static string TextMode(string text, bool guess = true)
    {
        string mode = DocumentVariables.Get(text, "mode");
        if (Modes.Exists(mode)) { return mode; }

        return guess ? Modes.GuessMode(text) : null;
    }

    /// <summary>
    /// Finds every file a document includes, following the includes
    /// recursively.
    /// </summary>
    /// <param name="info">The document's information.</param>
    /// <param name="includePath">The directories to search beyond the
    /// document's own.</param>
    /// <returns>The included files; the document's own is NOT among them.</returns>
    /// <remarks>
    /// Each argument is tried relative to the INCLUDING file first, then
    /// relative to the top document, and only then against the include path —
    /// upstream's order, which is what makes a chain of includes inside a
    /// subdirectory resolve the way the engine resolves it.
    /// </remarks>
    public static IReadOnlyCollection<string> IncludeFiles(
        DocInfo info, IReadOnlyList<string> includePath = null)
    {
        string fileName = info?.Document?.Filename;
        string baseDirectory = string.IsNullOrEmpty(fileName)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(fileName));
        HashSet<string> files = new HashSet<string>(StringComparer.Ordinal);

        bool TryArgument(string directory, string argument)
        {
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(argument))
            {
                return false;
            }

            string path;
            try { path = Path.GetFullPath(Path.Combine(directory, argument)); }
            catch (ArgumentException) { return false; }

            if (files.Contains(path) || !File.Exists(path)) { return false; }

            files.Add(path);
            Find(DocInfo(path).IncludeArgs(), Path.GetDirectoryName(path));
            return true;
        }

        void Find(IReadOnlyList<string> arguments, string directory)
        {
            foreach (var argument in arguments)
            {
                if (TryArgument(directory, argument)) { continue; }

                if (TryArgument(baseDirectory, argument)) { continue; }

                foreach (var searched in includePath ?? Array.Empty<string>())
                {
                    if (TryArgument(searched, argument)) { break; }
                }
            }
        }

        Find(info.IncludeArgs(), baseDirectory);
        return files;
    }

    /// <summary>
    /// Works out the base names a run of a document is expected to write.
    /// </summary>
    /// <param name="info">The document's information.</param>
    /// <param name="includeFiles">The files it includes, which may name output
    /// of their own.</param>
    /// <param name="fileName">The file the run is on, or null to take the
    /// document's own.</param>
    /// <param name="replaceSuffix">Whether to sanitize an output suffix the
    /// way the engine does.</param>
    /// <returns>The base names, without extension.</returns>
    /// <remarks>
    /// Add <c>.ext</c> and <c>-&lt;n&gt;.ext</c> to each to find the files
    /// themselves; <see cref="PathUtil.Files"/> is what does that.
    /// </remarks>
    public static IReadOnlyList<string> BaseNames(
        LyDocInfo info,
        IEnumerable<string> includeFiles = null,
        string fileName = null,
        bool replaceSuffix = true)
    {
        List<string> baseNames = new List<string>();
        string source = fileName ?? info?.Document?.Filename;
        if (string.IsNullOrEmpty(source)) { return baseNames; }

        string basePath = Path.Combine(
            Path.GetDirectoryName(source) ?? string.Empty,
            Path.GetFileNameWithoutExtension(source));
        string directory = Path.GetDirectoryName(basePath) ?? string.Empty;
        string baseName = Path.GetFileName(basePath);

        baseNames.Add(basePath);

        IEnumerable<(string Kind, string Argument)> Arguments()
        {
            foreach (var argument in info.OutputArgs()) { yield return argument; }

            foreach (var included in includeFiles ?? Array.Empty<string>())
            {
                foreach (var argument in DocInfo(included).OutputArgs())
                {
                    yield return argument;
                }
            }
        }

        foreach (var (kind, argument) in Arguments())
        {
            string name = argument;
            if (kind == "suffix")
            {
                name = baseName + "-"
                    + (replaceSuffix ? ReplaceSuffixChars(name) : name);
            }

            string path = Path.Combine(directory, name);
            path = Path.GetFullPath(path);
            if (!baseNames.Contains(path, StringComparer.Ordinal))
            {
                baseNames.Add(path);
            }
        }

        return baseNames;
    }

    /// <summary>
    /// Replaces spaces and most non-alphanumeric characters with underscores.
    /// </summary>
    /// <param name="text">The suffix.</param>
    /// <returns>The sanitized suffix.</returns>
    /// <remarks>The engine does this to <c>output-suffix</c> itself
    /// (<c>scm/lily-library.scm</c>), so a caller working out file names has to
    /// do the same or it looks for a file that was never written.</remarks>
    public static string ReplaceSuffixChars(string text)
        => SuffixChars.Replace(text ?? string.Empty, "_");

    private static CachedDocument Cached(string fileName)
    {
        string path = Path.GetFullPath(fileName);
        if (Cache.TryGetValue(path, out var cached)) { return cached; }

        string text = EditorDocument.LoadData(path);
        cached = new CachedDocument
        {
            FileName = path,
            Variables = DocumentVariables.Read(text),
        };
        cached.Document = new LyDocument(
            text, cached.Variables.TryGetValue("mode", out var mode) ? mode : null)
        {
            Filename = path,
        };

        Cache.Set(path, cached);
        return cached;
    }

    /// <summary>A file's document and everything worked out from it.</summary>
    private sealed class CachedDocument
    {
        /// <summary>Gets or sets the file's full path.</summary>
        public string FileName { get; set; }

        /// <summary>Gets or sets the tokenized document.</summary>
        public LyDocument Document { get; set; }

        /// <summary>Gets or sets the document's variables.</summary>
        public IReadOnlyDictionary<string, string> Variables { get; set; }

        /// <summary>Gets or sets the document information, once asked for.</summary>
        public LyDocInfo DocInfo { get; set; }

        /// <summary>Gets or sets the music tree, once asked for.</summary>
        public MusicTree Music { get; set; }
    }
}
