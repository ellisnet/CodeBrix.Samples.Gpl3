// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Data;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fresco.Brix.Completion; //was previously: frescobaldi/autocomplete/documentdata.py

// Modified by Jeremy Ellis and contributors - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The completion lists that depend on one document: what it defines, what
/// its included files define, and the words it already uses.
/// </summary>
/// <remarks>
/// Every list is remembered for five seconds. Upstream does this with a
/// <c>keep</c> decorator, for a plain reason: the popup is rebuilt on every
/// keystroke, and harvesting a large document's words on each one would make
/// typing stutter. The cache is keyed by the METHOD, not by its argument, so
/// a caret that has moved a little inside the same five seconds gets the same
/// answer — which upstream accepts, and so does this.
/// </remarks>
public sealed class DocumentCompletionData
    : Plugin<EditorDocument, DocumentCompletionData>
{
    /// <summary>How long a harvested list is reused for.</summary>
    public static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(5);

    private readonly Dictionary<string, (DateTime When, CompletionModel Model)> _cache
        = new Dictionary<string, (DateTime, CompletionModel)>(StringComparer.Ordinal);

    private DocumentCompletionData(EditorDocument document)
        : base(document)
    {
    }

    /// <summary>Gets the document.</summary>
    public EditorDocument Document => Owner;

    /// <summary>Gets the data for a document, creating it on first use.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The data.</returns>
    public static DocumentCompletionData For(EditorDocument document)
        => Instance(document, owner => new DocumentCompletionData(owner));

    /// <summary>Gets the words used in comments, markup, lyrics and strings.</summary>
    /// <returns>The model.</returns>
    public CompletionModel Words()
        => Keep(nameof(Words), () => CompletionModel.Of(
            Sorted(CompletionHarvest.Words(Document).Distinct())));

    /// <summary>
    /// Gets the scheme names: the engine's own, plus any the document uses
    /// that are longer than two characters.
    /// </summary>
    /// <returns>The model.</returns>
    public CompletionModel SchemeWords()
        => Keep(nameof(SchemeWords), () => CompletionModel.Of(Sorted(
            LyData.AllSchemeWords()
                .Concat(CompletionHarvest.SchemeWords(Document).Where(w => w.Length > 2))
                .Distinct())));

    /// <summary>
    /// Gets what a markup expression can hold: every markup command, the ones
    /// this document and its includes define, and the words already in use.
    /// </summary>
    /// <param name="position">The caret offset.</param>
    /// <returns>The model.</returns>
    public CompletionModel Markup(int position)
        => Keep(nameof(Markup), () => new CompletionModel(
            Sorted(Ly.Words.Markupcommands).Select(CompletionData.Command)
                .Concat(Sorted(CompletionHarvest.MarkupCommands(Document, position)
                    .Concat(CompletionHarvest.IncludeMarkupCommands(Document, position))
                    .Distinct())
                    .Select(CompletionData.Command))
                .Concat(Sorted(CompletionHarvest.Words(Document).Distinct()))
                .Select(w => new CompletionEntry(w))));

    /// <summary>Gets what belongs inside <c>\score { }</c>.</summary>
    /// <param name="position">The caret offset.</param>
    /// <returns>The model.</returns>
    public CompletionModel ScoreCommands(int position)
        => Keep(nameof(ScoreCommands), () => CompletionModel.OfCommands(
            Sorted(CompletionData.Score
                .Concat(Identifiers(position))
                .Distinct())));

    /// <summary>Gets what belongs inside <c>\bookpart { }</c>.</summary>
    /// <param name="position">The caret offset.</param>
    /// <returns>The model.</returns>
    public CompletionModel BookPartCommands(int position)
        => Keep(nameof(BookPartCommands), () => CompletionModel.OfCommands(
            Sorted(CompletionData.BookPart
                .Concat(Identifiers(position))
                .Distinct())));

    /// <summary>Gets what belongs inside <c>\book { }</c>.</summary>
    /// <param name="position">The caret offset.</param>
    /// <returns>The model.</returns>
    public CompletionModel BookCommands(int position)
        => Keep(nameof(BookCommands), () => CompletionModel.OfCommands(
            Sorted(CompletionData.Book
                .Concat(Identifiers(position))
                .Distinct())));

    /// <summary>Gets the commands that make sense inside music.</summary>
    /// <param name="position">The caret offset.</param>
    /// <returns>The model.</returns>
    public CompletionModel MusicCommands(int position)
        => Keep(nameof(MusicCommands), () => CompletionModel.OfCommands(
            Sorted(Ly.Words.LilypondKeywords
                .Concat(Ly.Words.LilypondMusicCommands)
                .Concat(Ly.Words.Articulations)
                .Concat(Ly.Words.Ornaments)
                .Concat(Ly.Words.Fermatas)
                .Concat(Ly.Words.InstrumentScripts)
                .Concat(Ly.Words.RepeatScripts)
                .Concat(Identifiers(position))
                .Distinct())));

    /// <summary>Gets the commands that make sense inside lyrics.</summary>
    /// <param name="position">The caret offset.</param>
    /// <returns>The model.</returns>
    public CompletionModel LyricCommands(int position)
        => Keep(nameof(LyricCommands), () => CompletionModel.OfCommands(
            Sorted(new[]
            {
                "set stanza = ", "set", "override", "markup", "notemode", "repeat",
            }.Concat(Identifiers(position)).Distinct())));

    /// <summary>
    /// Gets the file names an <c>\include</c> could name, relative to the
    /// document's own directory and to the include path.
    /// </summary>
    /// <param name="directory">The subdirectory already typed, or null.</param>
    /// <returns>The model.</returns>
    /// <remarks>
    /// Upstream also lists the files in LilyPond's own <c>ly/</c> data
    /// directory. There is no external installation here (FR5.1) and the
    /// engine carries its own; the ones a user can usefully include are the
    /// ones on disk beside the document or on the include path, so those are
    /// what this offers. Not cached: it reads the file system, and the answer
    /// changes the moment the user makes a file.
    /// </remarks>
    public CompletionModel IncludeNames(string directory = null)
    {
        List<string> names = new List<string>();
        string path = Document?.Path;
        if (!string.IsNullOrEmpty(path))
        {
            string baseDirectory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                names.AddRange(Sorted(
                    FileNames(Path.Combine(baseDirectory, directory), true))
                        .Select(f => Join(directory, f)));
            }
            else
            {
                names.AddRange(Sorted(FileNames(baseDirectory, true)));
            }
        }

        foreach (var baseDirectory in DocumentInfo.For(Document).IncludePath())
        {
            string relative = directory ?? string.Empty;
            names.AddRange(
                Sorted(FileNames(Path.Combine(baseDirectory, relative), true))
                    .Select(f => Join(relative, f)));
        }

        //LilyPond uses the forward slash on every platform, so the names are
        //offered with it whatever the host writes.
        return CompletionModel.Of(names.Select(n => n.Replace('\\', '/')));
    }

    private IEnumerable<string> Identifiers(int position)
        => CompletionHarvest.Names(Document, position)
            .Concat(CompletionHarvest.IncludeIdentifiers(Document, position));

    private CompletionModel Keep(string key, Func<CompletionModel> build)
    {
        DateTime now = DateTime.UtcNow;
        if (_cache.TryGetValue(key, out var cached) && now - cached.When < CacheTime)
        {
            return cached.Model;
        }

        CompletionModel model = build();
        _cache[key] = (now, model);
        return model;
    }

    private static string Join(string directory, string name)
        => string.IsNullOrEmpty(directory) ? name : Path.Combine(directory, name);

    private static IReadOnlyList<string> Sorted(IEnumerable<string> words)
        => words.OrderBy(w => w, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Gets the includable files in a directory, and its subdirectories' names.
    /// </summary>
    /// <param name="path">The directory.</param>
    /// <param name="directories">Whether to list subdirectories too.</param>
    /// <returns>The names.</returns>
    public static IEnumerable<string> FileNames(string path, bool directories = false)
    {
        List<string> found = new List<string>();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) { return found; }

        try
        {
            foreach (var file in Directory.EnumerateFiles(path))
            {
                string name = Path.GetFileName(file);
                if (name.Length == 0 || name[0] == '.' || name[0] == '~') { continue; }

                string extension = Path.GetExtension(name).ToLowerInvariant();
                if (extension is ".ly" or ".lyi" or ".ily") { found.Add(name); }
            }

            if (!directories) { return found; }

            foreach (var directory in Directory.EnumerateDirectories(path))
            {
                string name = Path.GetFileName(directory);
                if (name.Length > 0 && name[0] != '.')
                {
                    found.Add(name + Path.DirectorySeparatorChar);
                }
            }
        }
        catch (IOException)
        {
            //A directory that cannot be read is not worth interrupting the
            //user's typing over, which is upstream's reasoning too.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return found;
    }
}
