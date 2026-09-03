// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fresco.Brix.Engrave; //was previously: frescobaldi/engrave/__init__.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>What the one engrave button on the toolbar does when it is pressed.</summary>
public enum EngraveRunnerAction
{
    /// <summary>Engrave a preview.</summary>
    Preview,

    /// <summary>Open the custom-engrave window.</summary>
    Custom,

    /// <summary>Stop the job that is running.</summary>
    Abort,
}

/// <summary>Which way a document is engraved.</summary>
public enum EngraveMode
{
    /// <summary>With anchors, for editing.</summary>
    Preview,

    /// <summary>Without anchors, for handing on.</summary>
    Publish,

    /// <summary>With the layout-control formatters.</summary>
    LayoutControl,
}

/// <summary>
/// The window's engraving: which document is engraved, in which mode, when,
/// and what happens around it.
/// </summary>
/// <remarks>
/// <para>
/// The document that gets engraved is not simply the current one. A document
/// may be marked STICKY, in which case it is always the one; and a document
/// may name a <c>master</c> variable, in which case that master is engraved
/// instead — which is how a part of a larger score is edited and the whole
/// score engraved.
/// </para>
/// </remarks>
public sealed class Engraver
{
    private readonly DocumentManager _documents;
    private readonly SettingsStore _settings;
    private readonly LilyPortEngine _engine;
    private EditorDocument _sticky;

    /// <summary>The setting deciding whether a run saves the document first.</summary>
    public const string SaveOnRunSettingKey = "lilypond_settings/save_on_run";

    /// <summary>
    /// The setting deciding whether the files a run makes on the way to its
    /// output are deleted when it finishes — the DEFAULT the Engrave-custom
    /// dialog opens on.
    /// </summary>
    /// <remarks>Upstream's <c>lilypond_settings/delete_intermediate_files</c>,
    /// default TRUE.</remarks>
    public const string DeleteIntermediateSettingKey
        = "lilypond_settings/delete_intermediate_files";

    /// <summary>
    /// The setting deciding whether publish-mode output embeds its sources —
    /// the DEFAULT the Engrave-custom dialog opens on.
    /// </summary>
    /// <remarks>Upstream's <c>lilypond_settings/embed_source_code</c>,
    /// default FALSE.</remarks>
    public const string EmbedSourceSettingKey = "lilypond_settings/embed_source_code";

    /// <summary>
    /// The setting holding the APPLICATION-WIDE include path, newline-joined.
    /// </summary>
    /// <remarks>
    /// Upstream's <c>lilypond_settings/include_path</c>. It is a plain
    /// engraving setting — where <c>\include</c> looks — and ruling FR5.1,
    /// which is about engine versions and installations, does not touch it.
    /// //was previously: only the per-SESSION path existed
    /// (<c>SessionData.IncludePath</c>), so a user with one shared library
    /// folder had to re-enter it in every session.
    /// </remarks>
    public const string IncludePathSettingKey = "lilypond_settings/include_path";

    /// <summary>Reads the application-wide include path.</summary>
    /// <param name="settings">The store, or null.</param>
    /// <returns>The folders, in order.</returns>
    public static IReadOnlyList<string> IncludePath(SettingsStore settings)
    {
        string stored = settings?.GetString(IncludePathSettingKey);
        return string.IsNullOrEmpty(stored)
            ? System.Array.Empty<string>()
            : stored.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Remembers the application-wide include path.</summary>
    /// <param name="settings">The store.</param>
    /// <param name="paths">The folders, or null for none.</param>
    public static void SetIncludePath(
        SettingsStore settings, IReadOnlyList<string> paths)
    {
        if (settings == null) { return; }

        if (paths == null || paths.Count == 0)
        {
            settings.Remove(IncludePathSettingKey);
            return;
        }

        settings.SetString(IncludePathSettingKey, string.Join("\n", paths));
    }

    /// <summary>Creates the engraving service.</summary>
    /// <param name="documents">The open documents.</param>
    /// <param name="engine">The engine.</param>
    /// <param name="actions">The engrave commands.</param>
    /// <param name="settings">The settings store, or null.</param>
    public Engraver(
        DocumentManager documents,
        LilyPortEngine engine,
        EngraveActions actions,
        SettingsStore settings = null)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _settings = settings;

        WireActions();
        JobManager.AnyJobStarted += (_, _) => UpdateActions();
        JobManager.AnyJobFinished += (_, e) =>
        {
            UpdateActions();
            OnJobFinished(e);
        };
        _documents.CurrentDocumentChanged += (_, _) => UpdateActions();
        _documents.DocumentClosed += (_, e) => AbortFor(e.Document);

        LoadSettings();
        UpdateStickyActionText();
        UpdateActions();
    }

    /// <summary>Raised when the sticky document changes.</summary>
    public event EventHandler<DocumentEventArgs> StickyChanged;

    /// <summary>Raised when a job has finished, for whoever shows results.</summary>
    public event EventHandler<JobEventArgs> JobFinished;

    /// <summary>Raised when the custom-engrave dialog is asked for.</summary>
    /// <remarks>The dialog is a view; the service only knows it was
    /// asked for.</remarks>
    public event EventHandler CustomEngraveRequested;

    /// <summary>Raised when the engine information is asked for.</summary>
    public event EventHandler EngineInfoRequested;

    /// <summary>Gets the engrave commands.</summary>
    public EngraveActions Actions { get; }

    /// <summary>Gets the engine.</summary>
    public LilyPortEngine Engine => _engine;

    /// <summary>Gets the document marked sticky, or null.</summary>
    public EditorDocument StickyDocument => _sticky;

    /// <summary>
    /// Gets or sets the options a layout-control run is configured with.
    /// </summary>
    /// <remarks>The layout-control panel keeps this up to date; the service
    /// only reads it when a layout-control run starts.</remarks>
    public Func<IReadOnlyList<string>> LayoutControlOptions { get; set; }

    /// <summary>Gets or sets how a document named by <c>master</c> is opened.</summary>
    public Func<string, EditorDocument> OpenMaster { get; set; }

    /// <summary>Gets or sets how a document is saved before a run.</summary>
    public Action<EditorDocument> SaveDocument { get; set; }

    /// <summary>Gets the document that would be engraved right now.</summary>
    /// <returns>The document, or null.</returns>
    public EditorDocument Document()
    {
        EditorDocument document = _sticky;
        if (document != null) { return document; }

        document = _documents.CurrentDocument;
        if (document?.Path == null) { return document; }

        //A document that names a master engraves that master instead: the
        //user edits a part and the whole score is what LilyPond is run on.
        string master = DocumentVariables.Get(document.Text, "master");
        if (string.IsNullOrEmpty(master)) { return document; }

        string path = Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(document.Path), master));
        EditorDocument existing = _documents.FindDocument(path);
        if (existing != null) { return existing; }

        try { return OpenMaster?.Invoke(path) ?? document; }
        catch (IOException) { return document; }
    }

    /// <summary>Gets the visible job running for the engraved document.</summary>
    /// <returns>The job, or null.</returns>
    /// <remarks>A hidden (automatic) job does not count: the user did not
    /// start it and must not have the engrave button turn into a stop
    /// button because of it.</remarks>
    public EngraveJob RunningJob()
    {
        EngraveJob job = JobManager.JobFor(Document());
        return job is { IsRunning: true } && !JobAttributes.For(job).Hidden ? job : null;
    }

    /// <summary>Starts an engrave.</summary>
    /// <param name="mode">Which way to engrave.</param>
    /// <param name="document">The document, or null for the usual one.</param>
    /// <param name="maySave">Whether the save-before-running preference
    /// applies.</param>
    /// <returns>The job that was started, or null.</returns>
    public EngraveJob Engrave(
        EngraveMode mode = EngraveMode.Preview,
        EditorDocument document = null,
        bool maySave = true)
    {
        EditorDocument target = document ?? Document();
        if (target == null) { return null; }

        if (maySave) { SaveDocumentIfDesired(); }

        LilyPondJob job = mode switch
        {
            EngraveMode.Publish => new PublishJob(_engine, target),
            EngraveMode.LayoutControl => new LayoutControlJob(
                _engine, target, LayoutControlOptions?.Invoke()),
            _ => new PreviewJob(_engine, target),
        };

        RunJob(job, target);
        return job;
    }

    /// <summary>Runs a job on a document's behalf.</summary>
    /// <param name="job">The job.</param>
    /// <param name="document">The document.</param>
    /// <remarks>Any job already running for that document is aborted first —
    /// in practice that is an automatic engrave, which must never stand in the
    /// way of one the user asked for.</remarks>
    public void RunJob(EngraveJob job, EditorDocument document)
    {
        if (job == null || document == null) { return; }

        EngraveJob running = JobManager.JobFor(document);
        if (running is { IsRunning: true }) { running.Abort(); }

        //The results are frozen at the moment the job starts, so that saving
        //the document while it runs cannot move where the output is looked for.
        ResultFiles.For(document).SaveDocumentInfo(DateTime.UtcNow);
        EngraveErrors.For(document);

        //was previously: the commands were updated only from
        //JobManager.AnyJobStarted, which JobManager raises BEFORE it calls
        //EngraveJob.StartAsync — deliberately, so that a log connected by the
        //announcement sees the run's very first message. At that instant the
        //job's IsRunning is still FALSE, so UpdateActions computed "not
        //running" and every one of its answers was a run behind: Engrave
        //(preview) stayed enabled through the whole run, Abort stayed
        //disabled, and the toolbar's engrave button never turned into a stop
        //button. Nothing showed it until board wave W14 gave engrave_runner a
        //caller. The job's OWN Started event is raised inside StartAsync, just
        //after IsRunning becomes true, which is the moment upstream's
        //job.manager signals fire.
        job.Started += (_, _) => UpdateActions();
        JobManager.For(document).StartJob(job);
    }

    /// <summary>Aborts the running engrave, if any.</summary>
    public void Abort()
    {
        EngraveJob job = JobManager.JobFor(Document());
        if (job is { IsRunning: true }) { job.Abort(); }
    }

    /// <summary>Marks a document sticky, or clears the mark when null.</summary>
    /// <param name="document">The document, or null.</param>
    public void SetStickyDocument(EditorDocument document = null)
    {
        EditorDocument previous = _sticky;
        _sticky = document;

        if (previous != null)
        {
            StickyChanged?.Invoke(this, new DocumentEventArgs(previous));
        }

        if (document != null)
        {
            StickyChanged?.Invoke(this, new DocumentEventArgs(document));
        }

        Actions.EngraveSticky.IsChecked = document != null;
        UpdateStickyActionText();
        UpdateActions();
    }

    /// <summary>Saves the state the window remembers.</summary>
    public void SaveSettings()
        => _settings?.SetBool(
            EngraveActions.AutoCompileSettingKey, Actions.EngraveAutoCompile.IsChecked);

    /// <summary>Answers whether a document may be closed.</summary>
    /// <param name="document">The document.</param>
    /// <returns>Whether nothing stands in the way.</returns>
    /// <remarks>Only a VISIBLE job stands in the way: an automatic engrave is
    /// simply aborted.</remarks>
    public bool CanCloseDocument(EditorDocument document)
    {
        EngraveJob job = JobManager.JobFor(document);
        return job is not { IsRunning: true } || JobAttributes.For(job).Hidden;
    }

    /// <summary>
    /// Gets or sets how to ask whether Shift is held down, or null when
    /// nothing can be asked.
    /// </summary>
    /// <remarks>
    /// Upstream reads <c>QApplication.keyboardModifiers()</c> inside
    /// <c>engraveRunner</c>. This is that read, injected: the engraver is
    /// host-free and the keyboard is the window's to ask (board trap 38 — the
    /// answer comes from the keyboard source, not from an event's arguments).
    /// </remarks>
    public Func<bool> IsShiftHeld { get; set; }

    /// <summary>Answers what the one engrave button does when it is pressed.</summary>
    /// <param name="isRunning">Whether a visible job is running.</param>
    /// <param name="shiftHeld">Whether Shift is held down.</param>
    /// <returns>What to do.</returns>
    /// <remarks>Upstream's <c>Engraver.engraveRunner</c>, whole: a running job
    /// is aborted whatever is held down; Shift asks for the custom dialog;
    /// otherwise a preview runs.</remarks>
    public static EngraveRunnerAction RunnerActionFor(bool isRunning, bool shiftHeld)
        => isRunning ? EngraveRunnerAction.Abort
            : shiftHeld ? EngraveRunnerAction.Custom
            : EngraveRunnerAction.Preview;

    /// <summary>Keeps the commands' enabled state in step with what is running.</summary>
    public void UpdateActions()
    {
        EngraveJob job = JobManager.JobFor(Document());
        bool running = job is { IsRunning: true };
        bool visible = running && !JobAttributes.For(job).Hidden;

        Actions.EngravePreview.IsEnabled = !visible;
        Actions.EngravePublish.IsEnabled = !visible;
        Actions.EngraveDebug.IsEnabled = !visible;
        Actions.EngraveCustom.IsEnabled = !visible;
        Actions.EngraveAbort.IsEnabled = running;
        Actions.EngraveRunner.IconName = visible ? "lilypond-stop" : "lilypond-run";
        Actions.EngraveRunner.ToolTip = visible
            ? I18n.Get("Abort engraving job")
            : I18n.Get("Engrave (preview; press Shift for custom)");
    }

    /// <summary>Saves the document before a run, when the user asked for that.</summary>
    public void SaveDocumentIfDesired()
    {
        if (_settings?.GetBool(SaveOnRunSettingKey) != true) { return; }

        EditorDocument document = _documents.CurrentDocument;
        if (document is { IsModified: true } && document.Path != null)
        {
            SaveDocument?.Invoke(document);
        }
    }

    private void WireActions()
    {
        Actions.EngraveRunner.Handler = () =>
        {
            //was previously: a running job was aborted and anything else ran a
            //preview — the Shift branch was missing, because nothing could
            //click the button (there was no toolbar). Upstream's
            //`engraveRunner' has three branches and the tooltip promises all
            //three: "Engrave (preview; Shift-click for custom)".
            EngraveJob job = RunningJob();
            switch (RunnerActionFor(job != null, IsShiftHeld?.Invoke() == true))
            {
                case EngraveRunnerAction.Abort:
                    job.Abort();
                    break;

                case EngraveRunnerAction.Custom:
                    CustomEngraveRequested?.Invoke(this, EventArgs.Empty);
                    break;

                default:
                    Engrave(EngraveMode.Preview);
                    break;
            }
        };
        Actions.EngravePreview.Handler = () => Engrave(EngraveMode.Preview);
        Actions.EngravePublish.Handler = () => Engrave(EngraveMode.Publish);
        Actions.EngraveDebug.Handler = () => Engrave(EngraveMode.LayoutControl);
        Actions.EngraveCustom.Handler
            = () => CustomEngraveRequested?.Invoke(this, EventArgs.Empty);
        Actions.EngraveAbort.Handler = Abort;
        Actions.EngraveSticky.Handler = () => SetStickyDocument(
            _sticky != null ? null : _documents.CurrentDocument);
        Actions.EngraveEngineInfo.Handler
            = () => EngineInfoRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AbortFor(EditorDocument document)
    {
        EngraveJob job = JobManager.JobFor(document);
        if (job is { IsRunning: true }) { job.Abort(); }

        if (document == _sticky) { SetStickyDocument(null); }
    }

    private void OnJobFinished(JobEventArgs e) => JobFinished?.Invoke(this, e);

    private void UpdateStickyActionText()
        => Actions.EngraveSticky.Text = _sticky == null
            ? I18n.Get("&Always Engrave This Document")
            : I18n.Format(
                I18n.Get("&Always Engrave [{docname}]"),
                ("docname", _sticky.DocumentName()));

    private void LoadSettings()
        => Actions.EngraveAutoCompile.IsChecked
            = _settings?.GetBool(EngraveActions.AutoCompileSettingKey) ?? false;
}
