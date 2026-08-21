// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Engrave; //was previously: frescobaldi/logtool/errors.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A place in a document that an engine message pointed at, which keeps
/// pointing at the same place as the document is edited.
/// </summary>
/// <remarks>
/// The engine reports <c>file:line:column</c>. Those numbers are only true for
/// the text as it was engraved; the moment the user types above the error they
/// stop being true. So as soon as the document the message names is open, the
/// numbers are turned into an ANCHOR in that document, which the editor moves
/// for us from then on.
/// </remarks>
public sealed class ErrorReference
{
    private ITextAnchor _anchor;
    private EditorDocument _document;

    /// <summary>Creates a reference to a place in a file.</summary>
    /// <param name="fileName">The file.</param>
    /// <param name="line">The line, counted from 1.</param>
    /// <param name="column">The column, counted from 1.</param>
    public ErrorReference(string fileName, int line, int column)
    {
        FileName = fileName;
        Line = line;
        Column = column;
    }

    /// <summary>Gets the file the message named.</summary>
    public string FileName { get; }

    /// <summary>Gets the line, counted from 1.</summary>
    public int Line { get; }

    /// <summary>Gets the column, counted from 1.</summary>
    public int Column { get; }

    /// <summary>Gets the document this reference is bound to, or null.</summary>
    public EditorDocument Document => _document;

    /// <summary>Gets the offset in the bound document, or null.</summary>
    public int? Offset => _anchor is { IsDeleted: false } ? _anchor.Offset : null;

    /// <summary>Binds the reference to a document that has been opened.</summary>
    /// <param name="document">The document.</param>
    public void Bind(EditorDocument document)
    {
        if (document == null) { return; }

        _document = document;
        _anchor = document.Document.CreateAnchor(
            document.OffsetAtPosition(Line, Column));
        document.Closed += (_, _) => Unbind();
    }

    /// <summary>Forgets the document, which has been closed.</summary>
    public void Unbind()
    {
        _anchor = null;
        _document = null;
    }
}

/// <summary>
/// The places in documents that the last engrave run's messages pointed at.
/// </summary>
/// <remarks>
/// Collected as the run produces them, not scraped afterwards, so a log opened
/// mid-run already knows where its messages lead.
/// </remarks>
public sealed class EngraveErrors : Plugin<EditorDocument, EngraveErrors>
{
    /// <summary>
    /// Finds a <c>file:line:column:</c> reference at the start of a line.
    /// </summary>
    /// <remarks>
    /// The trailing <c>(?=:)</c> is load-bearing: it requires the colon that
    /// SEPARATES the location from the message, so a bare <c>file:12</c> in
    /// prose is not mistaken for one. The column is optional because a
    /// message about a whole line has none.
    /// </remarks>
    public static readonly Regex MessagePattern = new Regex(
        @"^((.*?):([1-9]\d*)(?::([1-9]\d*))?)(?=:)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly Dictionary<string, ErrorReference> _references
        = new Dictionary<string, ErrorReference>(StringComparer.Ordinal);

    private EngraveJob _job;

    private EngraveErrors(EditorDocument document)
        : base(document)
    {
        JobManager manager = JobManager.For(document);
        if (manager.Job != null) { ConnectJob(manager.Job); }

        manager.JobStarted += (_, e) => ConnectJob(e.Job);
    }

    /// <summary>Gets the documents this application has open.</summary>
    /// <remarks>Set once by the window; the reference binder needs it to find
    /// the document a scratch-copy path belongs to.</remarks>
    public static DocumentManager Documents { get; set; }

    /// <summary>Gets the document.</summary>
    public EditorDocument Document => Owner;

    /// <summary>Gets the job whose messages are being collected, or null.</summary>
    public EngraveJob Job => _job;

    /// <summary>Gets the errors for a document, creating them on first use.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The errors.</returns>
    public static EngraveErrors For(EditorDocument document)
        => Instance(document, owner => new EngraveErrors(owner));

    /// <summary>Gets the reference a log anchor names, or null.</summary>
    /// <param name="url">The <c>file:line:column</c> text.</param>
    /// <returns>The reference, or null.</returns>
    public ErrorReference Reference(string url)
        => url != null && _references.TryGetValue(url, out var reference)
            ? reference
            : null;

    /// <summary>Starts collecting the references a job reports.</summary>
    /// <param name="job">The job.</param>
    public void ConnectJob(EngraveJob job)
    {
        if (job == null) { return; }

        //Stop listening to the job before this one, or its late output would
        //keep adding references to a run nobody is looking at any more.
        if (_job != null) { _job.Output -= OnJobOutput; }

        _job = job;
        _references.Clear();

        //Whatever the job has already said counts too — a job may well have
        //produced output before anything asked to follow it.
        foreach (var message in job.History(MessageType.StdErr))
        {
            Collect(message.Text);
        }

        job.Output += OnJobOutput;
    }

    private void OnJobOutput(object sender, JobMessage message)
    {
        if (message.Type == MessageType.StdErr) { Collect(message.Text); }
    }

    private void Collect(string message)
    {
        foreach (Match match in MessagePattern.Matches(message ?? string.Empty))
        {
            string url = match.Groups[1].Value;
            string fileName = Resolve(match.Groups[2].Value);
            int line = int.Parse(
                match.Groups[3].Value, CultureInfo.InvariantCulture);
            int column = match.Groups[4].Success
                ? int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture)
                : 1;

            ErrorReference reference = new ErrorReference(fileName, line, column);
            EditorDocument target = ScratchDir.FindDocument(Documents, fileName);
            if (target != null) { reference.Bind(target); }

            _references[url] = reference;
        }
    }

    /// <summary>
    /// Turns the file name in a message into a full path.
    /// </summary>
    /// <param name="fileName">The name the engine printed.</param>
    /// <returns>The full path.</returns>
    /// <remarks>
    /// ⚠ THE ENGINE NAMES THE FILE AS THE PARSER SAW IT, which for the main
    /// input is its BASE NAME — <c>score.ly:8:59: error: ...</c>, not the whole
    /// path. Upstream resolves that against the process's working directory,
    /// which for a LilyPond process IS the directory the file is in. There is
    /// no separate process here, so the JOB's directory plays that part; the
    /// application's own working directory is wherever the user launched it
    /// from and would send every error to a file that does not exist.
    /// </remarks>
    private string Resolve(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) { return fileName; }

        if (!Path.IsPathRooted(fileName) && !string.IsNullOrEmpty(_job?.Directory))
        {
            return PathUtil.NormPath(Path.Combine(_job.Directory, fileName));
        }

        return PathUtil.NormPath(fileName);
    }
}
