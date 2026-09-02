// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort;
using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Fresco.Brix.Engrave; //was previously: frescobaldi/job/lilypond.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A job that engraves a document with the in-process engine.
/// </summary>
/// <remarks>
/// <para>
/// Upstream this class exists to BUILD A COMMAND LINE: it gathers <c>-d</c>
/// options, include paths and backend arguments and hands them to a
/// <c>lilypond</c> process. There is no command line here, so the same
/// gathering happens into <see cref="BatchRunOptions"/> and the engine's
/// include path instead — the shape is kept because the custom-engrave dialog,
/// the layout-control panel and the autocompiler all configure a job the same
/// way upstream does.
/// </para>
/// <para>
/// ⚠ Not every option upstream can pass on a command line can be passed to
/// this engine per run. The engine restores its defaults at the start of every
/// run and re-applies exactly one host override, <c>point-and-click</c>; an
/// option set from outside a run is wiped by that restore. See
/// <see cref="PendingOptions"/>.
/// </para>
/// </remarks>
public class LilyPondJob : EngraveJob
{
    private readonly LilyPortEngine _engine;
    private readonly List<string> _includePath = new List<string>();
    private readonly Dictionary<string, object> _options
        = new Dictionary<string, object>(StringComparer.Ordinal);

    /// <summary>Creates a job for a document.</summary>
    /// <param name="engine">The engine to run on.</param>
    /// <param name="document">The document to engrave.</param>
    /// <param name="title">The job's title, or null for the default.</param>
    public LilyPondJob(
        LilyPortEngine engine, EditorDocument document, string title = null)
        : base(title)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        Document = document ?? throw new ArgumentNullException(nameof(document));

        //Where the run happens is decided HERE, not when it starts: the file
        //and the include path are read once, so an edit made while the job
        //waits in the queue cannot change what is engraved.
        DocumentInfo info = DocumentInfo.For(document);
        (string fileName, IReadOnlyList<string> includePath) = info.JobInfo(create: true);
        FileName = fileName;
        _includePath.AddRange(includePath);
        Directory = string.IsNullOrEmpty(fileName)
            ? null
            : Path.GetDirectoryName(fileName);

        //An engrave is the long job in the application; a crawl of included
        //files must be able to overtake it. Upstream's weights exactly.
        Priority = 2;

        if (string.IsNullOrEmpty(title))
        {
            //was previously: "LilyPond {version} [{document}]" with the LILYPOND
            //release as {version}, so the log announced "Starting LilyPond 2.27.2
            //[score.ly]..." — naming the wrong product and reporting a version that is
            //not the engine's. The engine is LilyPort, stamped with its own package
            //version, and the LilyPond release it implements is the compatibility note.
            Title = I18n.Format(
                I18n.Get("LilyPort {version} (compatible with {compatible}) [{document}]"),
                ("version", LilyPortEngine.PortVersion),
                ("compatible", LilyPortEngine.CompatibleWithVersion),
                ("document", document.DocumentName()));
        }
    }

    /// <summary>Gets the document being engraved.</summary>
    public EditorDocument Document { get; }

    /// <summary>Gets what the run produced, once it has finished.</summary>
    public BatchRunResult Result { get; private set; }

    /// <summary>Gets the directories the run searches for includes.</summary>
    public IReadOnlyList<string> IncludePath => _includePath;

    /// <summary>
    /// Gets the engine options this job asks for, by their <c>-d</c> name.
    /// </summary>
    public IReadOnlyDictionary<string, object> Options => _options;

    /// <summary>
    /// Gets the options this job asked for that the engine seam cannot carry
    /// into a run.
    /// </summary>
    /// <remarks>
    /// //was previously: EVERYTHING except <c>point-and-click</c>, on the
    /// ground that "the engine restores its defaults at the top of every run
    /// and re-applies only the host overrides its run options declare, so an
    /// option set outside a run never reaches it". That was true when this was
    /// written and has not been true for several engine versions:
    /// <c>BatchRunOptions.Options</c> is a LIST of <c>-d</c> option texts,
    /// applied after the per-file restore and before <c>PointAndClick</c>, and
    /// <c>include-settings</c> is accumulative — which is exactly what the
    /// layout-control formatters need. Every option is carried now, so the list
    /// is empty and the "not applied" message it fed is gone with it. The
    /// property stays because the shape is right: if an option ever cannot be
    /// carried, this is where it is named rather than silently dropped.
    /// </remarks>
    public IReadOnlyList<string> PendingOptions => Array.Empty<string>();

    /// <summary>
    /// The options this run asks the engine for, as <c>-d</c> TEXTS — what
    /// follows a <c>-d</c> on a command line, which is the form
    /// <c>BatchRunOptions.Options</c> reads.
    /// </summary>
    /// <remarks><c>point-and-click</c> is left out: it has a typed property of
    /// its own on the run options, which the engine applies after this list, so
    /// passing it twice would only be a way of disagreeing with itself.</remarks>
    public IReadOnlyList<string> RunOptionTexts()
        => _options
            .Where(pair => !string.Equals(
                pair.Key, "point-and-click", StringComparison.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value switch
            {
                true => pair.Key,
                false => "no-" + pair.Key,
                var value => pair.Key + "="
                    + System.Convert.ToString(
                        value, System.Globalization.CultureInfo.InvariantCulture),
            })
            .ToList();

    /// <summary>Adds a directory to this run's include path.</summary>
    /// <param name="path">The directory.</param>
    public void AddIncludePath(string path)
    {
        if (!string.IsNullOrEmpty(path)
            && !_includePath.Contains(path, StringComparer.Ordinal))
        {
            _includePath.Add(path);
        }
    }

    /// <summary>Sets an engine option for this run.</summary>
    /// <param name="key">The option's <c>-d</c> name, without the prefix.</param>
    /// <param name="value">The value.</param>
    public void SetOption(string key, object value = null) => _options[key] = value ?? true;

    /// <summary>Reads an engine option this run asks for.</summary>
    /// <param name="key">The option's name.</param>
    /// <returns>The value, or null when it is not set.</returns>
    public object Option(string key)
        => _options.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Splits a <c>-d</c> token into its name and value, the way a command line
    /// carries them.
    /// </summary>
    /// <param name="token">The token, e.g. <c>-dno-point-and-click</c>.</param>
    /// <returns>The name and value.</returns>
    /// <remarks>Kept because the custom-engrave dialog lets a user type raw
    /// options, exactly as upstream's does.</remarks>
    public static (string Name, object Value) ParseOption(string token)
    {
        string text = token != null && token.StartsWith("-d", StringComparison.Ordinal)
            ? token.Substring(2)
            : token ?? string.Empty;

        int equals = text.IndexOf('=');
        if (equals > 0)
        {
            return (text.Substring(0, equals), text.Substring(equals + 1));
        }

        return text.StartsWith("no-", StringComparison.Ordinal)
            ? (text.Substring(3), false)
            : (text, true);
    }

    /// <summary>Formats the options as the command line would carry them.</summary>
    /// <param name="options">The options.</param>
    /// <param name="ordered">Whether to sort by name.</param>
    /// <returns>The tokens.</returns>
    public static IReadOnlyList<string> SerializeOptions(
        IReadOnlyDictionary<string, object> options, bool ordered = false)
    {
        IEnumerable<string> keys = options.Keys;
        if (ordered) { keys = keys.OrderBy(key => key, StringComparer.Ordinal); }

        return keys.Select(key => options[key] switch
        {
            true => "-d" + key,
            false => "-dno-" + key,
            var value => "-d" + key + "=" + value,
        }).ToList();
    }

    /// <inheritdoc/>
    protected override async Task<bool> RunAsync()
    {
        if (string.IsNullOrEmpty(FileName))
        {
            Message(I18n.Get("The document has no file to engrave."), MessageType.Failure);
            return false;
        }

        foreach (var directory in _includePath)
        {
            await _engine.AddIncludeDirectoryAsync(directory).ConfigureAwait(true);
        }

        IReadOnlyList<string> pending = PendingOptions;
        if (pending.Count > 0)
        {
            //Said once, plainly, rather than silently dropped: a user who ticks
            //a layout-control box and sees an ordinary score deserves to know.
            Message(
                I18n.Format(
                    I18n.Get("These options are not applied by this engine yet: {options}"),
                    ("options", string.Join(" ", SerializeOptions(
                        pending.ToDictionary(name => name, name => _options[name]),
                        ordered: true)))) + "\n",
                MessageType.Neutral);
        }

        //was previously: PointAndClick alone. Every other option this job asked
        //for — the layout-control formatters' -d switches, the
        //-dinclude-settings that pulls the formatter chain in, and whatever the
        //custom-engrave dialog's box holds — was gathered, REPORTED as
        //unsupported, and thrown away. `BatchRunOptions.Options' takes them, in
        //order, after the per-file restore that opens the run.
        BatchRunOptions runOptions = new BatchRunOptions
        {
            PointAndClick = Option("point-and-click"),
            Options = RunOptionTexts().ToList(),
            MessageWriter = new JobMessageWriter(this),
            CancellationToken = CancellationToken,
        };

        string source = FileName;
        string outputName = null;
        string wrapper = SettingsWrapper();
        if (wrapper != null)
        {
            source = wrapper;
            outputName = Path.GetFileNameWithoutExtension(FileName);
        }

        try
        {
            //The anchors an engrave writes name the file as the engine resolved
            //it AT PAGE-WRITE TIME, against the output directory. Engraving
            //where the output goes is therefore what makes a click in the Music
            //View land in the file the user is editing.
            Result = await _engine.RunFileAsync(
                source, Directory, outputName, runOptions, CancellationToken)
                .ConfigureAwait(true);
        }
        finally
        {
            if (wrapper != null)
            {
                try { File.Delete(wrapper); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        ReportCollectedDiagnostics();
        return Result.ErrorCount == 0;
    }

    /// <summary>
    /// Writes the one-line wrapper an <c>include-settings</c> run is engraved
    /// through, or null when this run asks for no settings file.
    /// </summary>
    /// <returns>The wrapper's path, or null.</returns>
    /// <remarks>
    /// ⚠ A WORKAROUND FOR AN ENGINE GAP, not a design. Upstream's layout
    /// control works by handing LilyPond <c>-dinclude-settings=…</c>, which
    /// makes it read the formatter file before the document; Frescobaldi's
    /// <c>debug-layout-options.ly</c> then looks at the <c>-d</c> switches and
    /// <c>\include</c>s the formatters those switches asked for.
    /// CodeBrix.LilyPort 1.0.244.98 DECLARES <c>include-settings</c> — it takes
    /// the option without complaint, where an undeclared name is warned about —
    /// but never reads the file: a settings file that would warn on inclusion
    /// stays silent, and so does a name that does not exist. Measured with a
    /// direct <c>BatchRunner</c> probe and recorded on
    /// <c>~/ClaudeHome/FIXLIST_codebrix_packages_2026-09-01.txt</c>; nothing
    /// outside this repository is changed for it.
    /// <para>
    /// So the file is included the way a document would include it. The wrapper
    /// lives in the temporary area, never beside the user's score; the OUTPUT
    /// base name is forced back to the document's, so the results land where
    /// <c>ResultFiles</c> looks for them; and the point-and-click anchors still
    /// name the document, because the engine records the file each TOKEN came
    /// from (verified: every anchor in a wrapped run names the score, with its
    /// own line and column).
    /// </para>
    /// </remarks>
    private string SettingsWrapper()
    {
        if (Option("include-settings") is not string settings
            || string.IsNullOrEmpty(settings)
            || string.IsNullOrEmpty(FileName))
        {
            return null;
        }

        //A relative name is what the panel passes; it is resolved here rather
        //than left to the engine, because the wrapper is not in the folder the
        //name is relative to.
        string resolved = Path.IsPathRooted(settings)
            ? settings
            : Resolve(settings);
        if (resolved == null) { return null; }

        try
        {
            string wrapper = Path.Combine(
                PathUtil.TempDir(),
                Path.GetFileNameWithoutExtension(FileName) + "-layoutcontrol.ly");
            File.WriteAllText(
                wrapper,
                "\\version \"" + LilyPortEngine.CompatibleWithVersion + "\"\n"
                    + "\\include \"" + resolved.Replace('\\', '/') + "\"\n"
                    + "\\include \"" + FileName.Replace('\\', '/') + "\"\n");
            return wrapper;
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException)
        {
            Message(
                I18n.Format(
                    I18n.Get("Could not write to {filename}:\n{error}"),
                    ("filename", "…-layoutcontrol.ly"), ("error", error.Message))
                    + "\n",
                MessageType.Failure);
            return null;
        }
    }

    /// <summary>Finds a settings file on this run's include path.</summary>
    /// <param name="name">The file name.</param>
    /// <returns>The path, or null when no directory holds it.</returns>
    private string Resolve(string name)
    {
        foreach (var directory in _includePath)
        {
            string candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) { return candidate; }
        }

        return null;
    }

    /// <summary>
    /// Writes the diagnostics the run COLLECTED rather than printed.
    /// </summary>
    /// <remarks>
    /// The engine prints its progress and its warnings as it goes, but a PARSE
    /// error is gathered into the result instead of printed — so without this,
    /// a run would say "Exited with 1 error" and never say what the error was.
    /// They arrive at the end rather than in place, which is the one way this
    /// log reads differently from a log of a real <c>lilypond</c> process;
    /// their <c>file:line:column:</c> prefix is identical, so they are just as
    /// clickable.
    /// </remarks>
    private void ReportCollectedDiagnostics()
    {
        if (Result?.Diagnostics == null || Result.Diagnostics.Count == 0) { return; }

        //A warning goes to BOTH places, so anything already printed is skipped
        //rather than said twice.
        string printed = StdErr();
        foreach (var diagnostic in Result.Diagnostics)
        {
            if (string.IsNullOrEmpty(diagnostic)) { continue; }

            if (printed.IndexOf(diagnostic, StringComparison.Ordinal) < 0)
            {
                Message(diagnostic + "\n", MessageType.StdErr);
            }
        }
    }

    /// <inheritdoc/>
    protected override void WriteFinishMessage(bool success)
    {
        if (success)
        {
            base.WriteFinishMessage(true);
            return;
        }

        //Upstream reports the process's exit code; the in-process equivalent is
        //the count of errors the run reported.
        int errors = Result?.ErrorCount ?? 0;
        Message(
            errors > 0
                ? I18n.Format(
                    I18n.Get("Exited with {count} error(s)."), ("count", errors))
                : I18n.Get("Exited with an error."),
            MessageType.Failure);
    }

    /// <summary>Sends everything the engine prints into the job's log.</summary>
    /// <remarks>
    /// The engine writes progress and diagnostics as it goes, so the log fills
    /// while the run is happening rather than all at once at the end. The
    /// writer is installed for exactly one run and taken off again by the
    /// runner.
    /// </remarks>
    private sealed class JobMessageWriter : TextWriter
    {
        private readonly LilyPondJob _job;

        internal JobMessageWriter(LilyPondJob job) => _job = job;

        /// <inheritdoc/>
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        /// <inheritdoc/>
        public override void Write(char value) => Write(value.ToString());

        /// <inheritdoc/>
        public override void Write(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                //Everything the engine prints is a diagnostic channel: the
                //error-location scanner and the log's colouring both key off
                //this, exactly as they key off the process's stderr upstream.
                _job.Message(value, MessageType.StdErr);
            }
        }

        /// <inheritdoc/>
        public override void WriteLine(string value) => Write((value ?? string.Empty) + "\n");
    }
}

/// <summary>An engrave with point-and-click anchors, for editing.</summary>
/// <remarks>The anchors are what make a click on a note land on its source, so
/// a preview build always has them.</remarks>
public sealed class PreviewJob : LilyPondJob
{
    /// <summary>Creates the job.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="document">The document.</param>
    /// <param name="title">The title, or null for the default.</param>
    public PreviewJob(LilyPortEngine engine, EditorDocument document, string title = null)
        : base(engine, document, title)
        => SetOption("point-and-click", true);
}

/// <summary>An engrave without anchors, for output meant to be handed on.</summary>
/// <remarks>The anchors carry absolute paths from this machine, which is why a
/// published score must not have them.</remarks>
public sealed class PublishJob : LilyPondJob
{
    /// <summary>Creates the job.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="document">The document.</param>
    /// <param name="title">The title, or null for the default.</param>
    public PublishJob(LilyPortEngine engine, EditorDocument document, string title = null)
        : base(engine, document, title)
        => SetOption("point-and-click", false);
}

/// <summary>An engrave with the layout-control formatters switched on.</summary>
public sealed class LayoutControlJob : LilyPondJob
{
    /// <summary>Creates the job.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="document">The document.</param>
    /// <param name="options">The options the layout-control panel asks for.</param>
    /// <param name="title">The title, or null for the default.</param>
    public LayoutControlJob(
        LilyPortEngine engine,
        EditorDocument document,
        IEnumerable<string> options = null,
        string title = null)
        : base(engine, document, title)
    {
        foreach (var token in options ?? Array.Empty<string>())
        {
            if (token.StartsWith("-I", StringComparison.Ordinal))
            {
                AddIncludePath(token.Substring(2));
                continue;
            }

            if (token.StartsWith("-d", StringComparison.Ordinal))
            {
                (string name, object value) = ParseOption(token);
                SetOption(name, value);
                continue;
            }

            //Upstream's one long-form switch. It is not a -d option, but it is
            //an engine setting all the same, so it is carried and reported the
            //same way the rest are.
            if (string.Equals(token, "--verbose", StringComparison.Ordinal))
            {
                SetOption("verbose", true);
            }
        }
    }
}
