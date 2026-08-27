// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Engrave;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace Fresco.Brix.Documents; //was previously: frescobaldi/resultfiles.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The files an engrave run left on disk for a document.
/// </summary>
/// <remarks>
/// <para>
/// The subtlety this class exists for: the answers are FROZEN when a job
/// starts. If the user saves the document while a job is running, the document
/// stops being modified, the scratch copy stops being what would be engraved,
/// and asking afterwards where the output went would point at the wrong place
/// entirely. So the file being engraved and the base names it will write are
/// taken over at the moment the job starts, and forgotten again the next time
/// the user saves — but never while a job is running.
/// </para>
/// </remarks>
public sealed class ResultFiles : Plugin<EditorDocument, ResultFiles>
{
    private string _jobFile;
    private IReadOnlyList<string> _baseNames;
    private DateTime _startTime;

    private ResultFiles(EditorDocument document)
        : base(document)
        => document.Saved += (_, _) => ForgetDocumentInfo();

    /// <summary>Gets the document.</summary>
    public EditorDocument Document => Owner;

    /// <summary>Gets the results for a document, creating them on first use.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The results.</returns>
    public static ResultFiles For(EditorDocument document)
        => Instance(document, owner => new ResultFiles(owner));

    /// <summary>
    /// Takes over what a starting job is about to do, so the answers survive a
    /// save made while it runs.
    /// </summary>
    /// <param name="startTime">When the job started.</param>
    public void SaveDocumentInfo(DateTime startTime)
    {
        EditorDocument document = Document;
        if (document == null) { return; }

        DocumentInfo info = DocumentInfo.For(document);
        _startTime = startTime;
        _jobFile = info.JobInfo().FileName;
        _baseNames = info.BaseNames();
    }

    /// <summary>Forgets the frozen answers, unless a job is running.</summary>
    public void ForgetDocumentInfo()
    {
        EditorDocument document = Document;
        if (document != null && JobManager.IsRunningFor(document)) { return; }

        _startTime = default;
        _jobFile = null;
        _baseNames = null;
    }

    /// <summary>Gets the file that is being, or will be, engraved.</summary>
    /// <returns>The file, or null.</returns>
    public string JobFile()
        => _jobFile ?? (Document == null
            ? null
            : DocumentInfo.For(Document).JobInfo().FileName);

    /// <summary>Gets the base names the last or running job writes.</summary>
    /// <returns>The base names.</returns>
    public IReadOnlyList<string> BaseNames()
        => _baseNames ?? (Document == null
            ? Array.Empty<string>()
            : DocumentInfo.For(Document).BaseNames());

    /// <summary>Gets the existing output files.</summary>
    /// <param name="extension">The extension to match; <c>*</c> for any.</param>
    /// <param name="newer">Whether to keep only the files newer than the
    /// source.</param>
    /// <returns>The files.</returns>
    public IReadOnlyList<string> Files(string extension = "*", bool newer = true)
    {
        string jobFile = JobFile();
        if (string.IsNullOrEmpty(jobFile)) { return Array.Empty<string>(); }

        IReadOnlyList<string> files = PathUtil.Files(BaseNames(), extension);
        if (!newer) { return files; }

        try
        {
            return PathUtil.NewerFiles(files, File.GetLastWriteTimeUtc(jobFile));
        }
        catch (IOException)
        {
            return files;
        }
    }

    /// <summary>Gets the files the LAST job wrote.</summary>
    /// <param name="extension">The extension to match.</param>
    /// <returns>The files.</returns>
    /// <remarks>Before any job has run, this is the same as
    /// <see cref="Files"/>.</remarks>
    public IReadOnlyList<string> FilesFromLastJob(string extension = "*")
    {
        if (_startTime == default) { return Files(extension); }

        IReadOnlyList<string> files = PathUtil.Files(BaseNames(), extension);
        try { return PathUtil.NewerFiles(files, _startTime); }
        catch (IOException) { return files; }
    }

    /// <summary>Answers whether an output file is newer than the source.</summary>
    /// <param name="fileName">The output file.</param>
    /// <returns>Whether it is newer; true also when either time cannot be
    /// read.</returns>
    public bool IsNewer(string fileName)
    {
        string jobFile = JobFile();
        if (string.IsNullOrEmpty(jobFile)) { return true; }

        try
        {
            return File.GetLastWriteTimeUtc(fileName)
                > File.GetLastWriteTimeUtc(jobFile);
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    /// <summary>
    /// Gets the directory the document's output is in — the temporary one when
    /// that is where the last run happened.
    /// </summary>
    /// <returns>The directory, or null.</returns>
    public string CurrentDirectory()
    {
        string jobFile = JobFile();
        if (string.IsNullOrEmpty(jobFile)) { return null; }

        string directory = Path.GetDirectoryName(jobFile);
        return Directory.Exists(directory) ? directory : null;
    }
}
