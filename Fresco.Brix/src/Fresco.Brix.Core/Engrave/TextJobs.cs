// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Fresco.Brix.Engrave; //was previously: frescobaldi/job/lilypond.py's VolatileTextJob and CachedPreviewJob

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// An engrave of a piece of TEXT rather than of a document the user has open.
/// </summary>
/// <remarks>
/// The Score Wizard's preview and the music tooltips have LilyPond source but
/// no document: the source is written into a directory of its own and engraved
/// there, and the directory goes away with the job. There are no
/// point-and-click anchors — nothing they could point at is open.
/// </remarks>
public class VolatileTextJob : EngraveJob
{
    private readonly LilyPortEngine _engine;
    private readonly List<string> _includePath = new List<string>();
    private readonly string _text;

    /// <summary>Creates the job over some source.</summary>
    /// <param name="engine">The engine to run on.</param>
    /// <param name="text">The LilyPond source.</param>
    /// <param name="title">The job's title, or null for the default.</param>
    /// <param name="baseDirectory">A directory its relative includes resolve
    /// against, or null.</param>
    public VolatileTextJob(
        LilyPortEngine engine, string text, string title = null, string baseDirectory = null)
        : base(title)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _text = text ?? string.Empty;

        Directory = CreateDirectory();
        BaseName = "document";
        FileName = Path.Combine(Directory, BaseName + ".ly");
        Priority = 2;

        if (!string.IsNullOrEmpty(baseDirectory)) { _includePath.Add(baseDirectory); }

        if (string.IsNullOrEmpty(title))
        {
            Title = I18n.Format(
                I18n.Get("LilyPort {version} (compatible with {compatible}) [{document}]"),
                ("version", LilyPortEngine.PortVersion),
                ("compatible", LilyPortEngine.CompatibleWithVersion),
                ("document", BaseName + ".ly"));
        }
    }

    /// <summary>Gets the name the output files are built from.</summary>
    public string BaseName { get; protected set; }

    /// <summary>Gets the source this job engraves.</summary>
    protected string Source => _text;

    /// <summary>Gets what the run produced, once it has finished.</summary>
    public BatchRunResult Result { get; private set; }

    /// <summary>Gets the engraved pages, in page order.</summary>
    public virtual IReadOnlyList<string> ResultFiles
        => Result?.SvgPaths ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>Gets whether the run is worth making at all.</summary>
    /// <returns>Whether it is.</returns>
    /// <remarks>Always, for a volatile job; a cached one may already have the
    /// answer on disk.</remarks>
    public virtual bool NeedsCompilation() => true;

    /// <summary>Throws the job's working directory away.</summary>
    public virtual void Cleanup()
    {
        try
        {
            if (!string.IsNullOrEmpty(Directory) && System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
        catch (IOException)
        {
            //A preview's scratch directory is not worth an error in front of
            //the user; the operating system clears the temporary tree anyway.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Makes the directory the run happens in.</summary>
    /// <returns>The directory.</returns>
    protected virtual string CreateDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "frescobrix-preview-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        System.IO.Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Writes the source out, unless it is already there.</summary>
    protected virtual void WriteSource()
        => File.WriteAllText(FileName, _text, new UTF8Encoding(false));

    /// <inheritdoc/>
    protected override async Task<bool> RunAsync()
    {
        WriteSource();

        foreach (string directory in _includePath)
        {
            await _engine.AddIncludeDirectoryAsync(directory).ConfigureAwait(true);
        }

        BatchRunOptions options = new BatchRunOptions
        {
            //A preview is not editable, so there is nothing for an anchor to
            //point at: this is upstream's publish-mode run.
            PointAndClick = false,
            MessageWriter = new JobMessageWriter(this),
            CancellationToken = CancellationToken,
        };

        Result = await _engine.RunFileAsync(
            FileName, Directory, BaseName, options, CancellationToken)
            .ConfigureAwait(true);

        ReportCollectedDiagnostics();
        return Result.ErrorCount == 0;
    }

    /// <inheritdoc/>
    protected override void WriteFinishMessage(bool success)
    {
        if (success)
        {
            base.WriteFinishMessage(true);
            return;
        }

        int errors = Result?.ErrorCount ?? 0;
        Message(
            errors > 0
                ? I18n.Format(I18n.Get("Exited with {count} error(s)."), ("count", errors))
                : I18n.Get("Exited with an error."),
            MessageType.Failure);
    }

    /// <summary>Writes the diagnostics the run collected rather than printed.</summary>
    /// <remarks>The same gap as an ordinary engrave's (board trap 23): parse
    /// errors are gathered into the result instead of written out.</remarks>
    private void ReportCollectedDiagnostics()
    {
        if (Result?.Diagnostics == null || Result.Diagnostics.Count == 0) { return; }

        string printed = StdErr();
        foreach (string diagnostic in Result.Diagnostics)
        {
            if (string.IsNullOrEmpty(diagnostic)) { continue; }

            if (printed.IndexOf(diagnostic, StringComparison.Ordinal) < 0)
            {
                Message(diagnostic + "\n", MessageType.StdErr);
            }
        }
    }

    /// <summary>Sends everything the engine prints into the job's log.</summary>
    private sealed class JobMessageWriter : TextWriter
    {
        private readonly VolatileTextJob _job;

        internal JobMessageWriter(VolatileTextJob job) => _job = job;

        /// <inheritdoc/>
        public override Encoding Encoding => Encoding.UTF8;

        /// <inheritdoc/>
        public override void Write(char value) => Write(value.ToString());

        /// <inheritdoc/>
        public override void Write(string value)
        {
            if (!string.IsNullOrEmpty(value)) { _job.Message(value, MessageType.StdErr); }
        }

        /// <inheritdoc/>
        public override void WriteLine(string value) => Write((value ?? string.Empty) + "\n");
    }
}

/// <summary>
/// An engrave of a piece of text that is kept, so the same source is only ever
/// engraved once.
/// </summary>
/// <remarks>
/// The file is named after the MD5 of the source, so identical source finds
/// its own output already on disk — which is what makes a music tooltip appear
/// at once the second time it is asked for. Upstream's own arrangement, hash
/// and all.
/// </remarks>
public sealed class CachedPreviewJob : VolatileTextJob
{
    private static readonly string SharedDirectory = Path.Combine(
        Path.GetTempPath(),
        "frescobrix-preview-cache-" + Guid.NewGuid().ToString("N").Substring(0, 8));

    private readonly string _targetDirectory;
    private readonly string _hashName;
    private bool _needsCompilation = true;

    /// <summary>Creates the job over some source.</summary>
    /// <param name="engine">The engine to run on.</param>
    /// <param name="text">The LilyPond source.</param>
    /// <param name="targetDirectory">Where to keep the output, or null for the
    /// session's own cache.</param>
    /// <param name="title">The job's title, or null for the default.</param>
    /// <param name="baseDirectory">A directory its relative includes resolve
    /// against, or null.</param>
    public CachedPreviewJob(
        LilyPortEngine engine,
        string text,
        string targetDirectory = null,
        string title = null,
        string baseDirectory = null)
        : base(engine, text, title, baseDirectory)
    {
        _targetDirectory = targetDirectory ?? SharedDirectory;
        _hashName = Hash(text);
        System.IO.Directory.CreateDirectory(_targetDirectory);

        Directory = _targetDirectory;
        BaseName = _hashName;
        FileName = Path.Combine(_targetDirectory, _hashName + ".ly");
        _needsCompilation = ExistingPages().Count == 0;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> ResultFiles
    {
        get
        {
            IReadOnlyList<string> pages = base.ResultFiles;
            return pages.Count > 0 ? pages : ExistingPages();
        }
    }

    /// <inheritdoc/>
    public override bool NeedsCompilation() => _needsCompilation;

    /// <inheritdoc/>
    /// <remarks>Deliberately keeps everything: the point of the cache is that
    /// the next run finds it.</remarks>
    public override void Cleanup()
    {
    }

    /// <inheritdoc/>
    protected override string CreateDirectory() => Path.GetTempPath();

    /// <inheritdoc/>
    protected override void WriteSource()
    {
        if (!_needsCompilation) { return; }

        File.WriteAllText(FileName, Source, new UTF8Encoding(false));
        _needsCompilation = false;
    }

    /// <summary>Answers the pages this source has already been engraved to.</summary>
    /// <returns>The files, in page order.</returns>
    private IReadOnlyList<string> ExistingPages()
    {
        if (!System.IO.Directory.Exists(_targetDirectory))
        {
            return Array.Empty<string>();
        }

        return System.IO.Directory
            .GetFiles(_targetDirectory, _hashName + "*.svg")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Names a piece of source by its digest.</summary>
    /// <param name="text">The source.</param>
    /// <returns>The name.</returns>
    private static string Hash(string text)
    {
        byte[] digest = MD5.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        StringBuilder name = new StringBuilder(digest.Length * 2);
        foreach (byte value in digest)
        {
            name.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }

        return name.ToString();
    }
}
