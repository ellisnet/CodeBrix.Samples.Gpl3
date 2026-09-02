// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Fresco.Brix.Widgets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Fresco.Brix.Preferences; //was previously: frescobaldi/preferences/tools.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Tools page: how the log behaves, whether the Documents panel groups by
/// folder, and what the document outline looks for.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ PARTIAL, because two of upstream's rows have nothing behind them. Upstream's
/// Log group opens with a font chooser over <c>log/fontfamily</c> and
/// <c>log/fontsize</c>, and its Special Characters group is nothing BUT such a
/// chooser over <c>charmaptool/fontfamily</c> and <c>charmaptool/fontsize</c>.
/// Neither pair is read anywhere in this application: the log draws in the
/// bundled monospace face, and the character map draws in the EDITOR's face on
/// purpose, so that a character that will be tofu in the document is tofu in the
/// panel too (standing rule 6 — there is no system font, anywhere, ever). A row
/// that wrote a setting nothing reads would be a dead control, so the rows are
/// out and the Special Characters group goes with its only row. The editor's own
/// face is chosen on the Fonts &amp; Colors page.
/// </para>
/// <para>
/// The Documents group is what makes <c>document_list/group_by_folder</c> real:
/// the panel has always read it and nothing has ever written it.
/// </para>
/// <para>
/// ⚠ THE FIRST GROUP IS NOT UPSTREAM'S TOOLS PAGE AT ALL. "Running LilyPort" is
/// the surviving half of <c>preferences/lilypond.py</c>, whose page ruling FR5.1
/// retires. That page has FOUR groups, and only the first two — the versions
/// list and the default output format — are about installations and output
/// targets. Its third group, <c>_("Running LilyPond")</c>, is plain engraving
/// behaviour that FR5.1 does not touch, and one of its rows
/// (<c>save_on_run</c>) was ALREADY live in this application, read by
/// <c>Engraver.SaveDocumentIfDesired</c>, with no control anywhere to set it.
/// The group is therefore carried over onto this page — which is already
/// per-tool preferences and already configures the Log that engraving writes
/// to — keeping every one of upstream's <c>lilypond_settings/…</c> key
/// spellings, so a Frescobaldi settings file still works.
/// </para>
/// </remarks>
public sealed class ToolsPage : PreferencesPage
{
    private CheckBox _saveOnRun;
    private CheckBox _deleteIntermediate;
    private CheckBox _embedSource;
    private ListEdit _includePath;
    private CheckBox _showLog;
    private CheckBox _rawView;
    private CheckBox _hideAuto;
    private CheckBox _groupByFolder;
    private ListEdit _patterns;
    private ListEdit _commentPatterns;

    /// <summary>Creates the page.</summary>
    /// <param name="context">What the page configures.</param>
    public ToolsPage(PreferencesContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Tools");

    /// <inheritdoc/>
    public override string Help => "prefs_tools";

    /// <inheritdoc/>
    public override string IconName => "preferences-other";

    /// <summary>Gets the values the page reads and writes.</summary>
    public ToolsValues Values { get; } = new ToolsValues();

    /// <inheritdoc/>
    public override void LoadSettings()
    {
        Values.Load(Settings);

        _saveOnRun.IsChecked = Values.SaveDocumentOnRun;
        _deleteIntermediate.IsChecked = Values.DeleteIntermediateFiles;
        _embedSource.IsChecked = Values.EmbedSourceCode;
        _includePath.Value = Values.IncludePath;
        _showLog.IsChecked = Values.ShowLogOnStart;
        _rawView.IsChecked = Values.RawLogView;
        _hideAuto.IsChecked = Values.HideAutomaticEngraves;
        _groupByFolder.IsChecked = Values.GroupDocumentsByFolder;
        _patterns.Value = Values.OutlinePatterns;
        _commentPatterns.Value = Values.OutlineCommentPatterns;
    }

    /// <inheritdoc/>
    public override void SaveSettings()
    {
        Values.SaveDocumentOnRun = _saveOnRun.IsChecked == true;
        Values.DeleteIntermediateFiles = _deleteIntermediate.IsChecked == true;
        Values.EmbedSourceCode = _embedSource.IsChecked == true;
        Values.IncludePath = _includePath.Value;
        Values.ShowLogOnStart = _showLog.IsChecked == true;
        Values.RawLogView = _rawView.IsChecked == true;
        Values.HideAutomaticEngraves = _hideAuto.IsChecked == true;
        Values.GroupDocumentsByFolder = _groupByFolder.IsChecked == true;
        Values.OutlinePatterns = _patterns.Value;
        Values.OutlineCommentPatterns = _commentPatterns.Value;

        Values.Save(Settings);
    }

    /// <inheritdoc/>
    protected override UIElement Build()
        => Stack(
            //was previously: "Running LilyPond", on the retired LilyPond page.
            //FR13 — no UI element of Fresco.Brix names LilyPond.
            Group(I18n.Get("Running LilyPort"), BuildRunning()),
            //was previously: "LilyPond Log". FR13 — no UI element of Fresco.Brix
            //names LilyPond; the panel this group configures is the LilyPort Log.
            Group(I18n.Get("LilyPort Log"), BuildLog()),
            Group(I18n.Get("Documents"), BuildDocumentList()),
            Group(I18n.Get("Outline"), BuildOutline()));

    /// <summary>
    /// Answers whether a pattern can be used as a regular expression.
    /// </summary>
    /// <param name="text">The typed pattern.</param>
    /// <returns>Whether it compiles.</returns>
    /// <remarks>
    /// Upstream's <c>is_regex</c>, which compiles with <c>re.M</c> because the
    /// outline's patterns are matched against the whole document.
    /// ⚠ An EMPTY pattern compiles in both languages, but it cannot survive this
    /// port's store: <c>DocumentStructure.Patterns</c> reads the list back by
    /// splitting on newlines with empty entries removed, so an empty row would
    /// vanish between OK and the next launch. It is refused where it is typed
    /// instead of being silently dropped later.
    /// </remarks>
    private static bool IsRegularExpression(string text)
    {
        if (string.IsNullOrEmpty(text)) { return false; }

        try
        {
            _ = new Regex(text, RegexOptions.Multiline);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private UIElement BuildRunning()
    {
        _saveOnRun = Tick(
            I18n.Get("Save document if possible"),
            //was previously: "…Otherwise a temporary file is used to run
            //LilyPond." FR13 names the engine as itself; the mechanism is the
            //per-document scratch directory rather than one temporary file.
            I18n.Get(
                "If checked, the document is saved when it is local and modified.\n"
                + "Otherwise a scratch copy is engraved instead."));
        _deleteIntermediate = Tick(
            I18n.Get("Delete intermediate output files"),
            //was previously: "…LilyPond will delete intermediate PostScript
            //files." There is no PostScript stage here — the engine emits SVG
            //pages — so the tooltip says what it actually does. FR13/FR14.
            I18n.Get(
                "If checked, the files a run produces on the way to its output\n"
                + "are deleted when it finishes."));
        _embedSource = Tick(
            I18n.Get("Embed Source Code files in publish mode"),
            //was previously: "…the LilyPond source files will be embedded in the
            //PDF when LilyPond is started in publish mode." (FR13.)
            I18n.Get(
                "If checked, the source files are embedded in the exported PDF\n"
                + "when the score is engraved in publish mode."));

        _includePath = new ListEdit
        {
            OpenEditorAsync = PickFolderAsync,
            //The list is a SEARCH order, so the order is the user's to set —
            //upstream turns internal-move dragging on for this list too.
            CanReorder = true,
        };
        _includePath.Changed += (_, _) => MarkChanged();

        //⚠ "Run LilyPond with English messages" (`no_translation') is NOT here,
        //and this is the reason rather than an oversight: it exists upstream to
        //force a LilyPond BINARY, which ships translated message catalogs, back
        //to English for a bug report. The engine here is CodeBrix.LilyPort,
        //which carries no message catalogs at all — its diagnostics are English
        //in every interface language already — so the row would be a control
        //that changes nothing. Ruled out on the fact, not on FR5.1.
        return Rows(
            _saveOnRun,
            _deleteIntermediate,
            _embedSource,
            //was previously: "LilyPond include path:" (FR13).
            Note(I18n.Get("LilyPort include path:")),
            _includePath);
    }

    private async Task<string> PickFolderAsync(string current)
    {
        Func<UrlRequesterMode, string, Task<string>> pick = Context.PickAsync;
        if (pick == null) { return null; }

        return await pick(UrlRequesterMode.Directory, current);
    }

    private UIElement BuildLog()
    {
        _showLog = Tick(I18n.Get("Show log when a job is started"));
        _rawView = Tick(
            I18n.Get("Display plain log output"),
            //was previously: "…Frescobaldi will not shorten…" (FR9).
            I18n.Format(
                I18n.Get(
                    "If checked, {appname} will not shorten filenames in the log "
                    + "output."),
                ("appname", AppInfo.AppName)));
        _hideAuto = Tick(
            I18n.Get("Hide automatic engraving jobs"),
            //was previously: "…Frescobaldi will not show… (LilyPond->Auto-engrave)."
            //FR9 names the application through a placeholder, and FR13 names the
            //menu the command actually lives on here.
            I18n.Format(
                I18n.Get(
                    "If checked, {appname} will not show the log for automatically\n"
                    + "started engraving jobs (LilyPort->Auto-engrave)."),
                ("appname", AppInfo.AppName)));

        //was previously: upstream's font family and size row, over log/fontfamily
        //and log/fontsize. Nothing reads either key in this application — the log
        //draws in the bundled monospace face — so the row would be dead, and a
        //dead control is worse than none. See the class remarks.

        return Rows(_showLog, _rawView, _hideAuto);
    }

    private UIElement BuildDocumentList()
    {
        _groupByFolder = Tick(I18n.Get("Group documents by directory"));
        return Rows(_groupByFolder);
    }

    private UIElement BuildOutline()
    {
        _patterns = BuildPatternList(Documents.DocumentStructure.DefaultPatterns);
        _commentPatterns = BuildPatternList(Documents.DocumentStructure.DefaultCommentPatterns);

        return Rows(
            Note(I18n.Get(
                "Patterns to match in text (excluding comments) that are shown in "
                + "outline:")),
            _patterns,
            Note(I18n.Get(
                "Patterns to match in text (including comments) that are shown in "
                + "outline:")),
            _commentPatterns);
    }

    private ListEdit BuildPatternList(IReadOnlyList<string> defaults)
    {
        ListEdit list = new ListEdit
        {
            OpenEditorAsync = EditPatternAsync,
            //Upstream turns internal-move dragging on for exactly these two
            //lists, because the order the patterns are tried in is the user's.
            CanReorder = true,
        };
        list.Changed += (_, _) => MarkChanged();

        Button restore = new Button { Content = I18n.Get("Default") };
        ToolTipService.SetToolTip(
            restore, I18n.Get("Restores the built-in outline patterns."));
        restore.Click += (_, _) =>
        {
            list.Value = defaults;
            MarkChanged();
        };
        list.AddButton(restore);
        return list;
    }

    private async Task<string> EditPatternAsync(string current)
    {
        TextDialog dialog = new TextDialog(
            I18n.Get("Outline"),
            I18n.Get("Enter a regular expression to match:"))
        {
            Text = current ?? string.Empty,
        };
        dialog.SetValidateFunction(IsRegularExpression);

        //Upstream's `userguide.addButton(dlg.buttonBox(), "outline_configure")'.
        //A ContentDialog carries three buttons (board trap 50), so the button
        //goes inside the content — which is what WidgetDialog.HelpPage does.
        dialog.HelpPage = "outline_configure";

        return (await dialog.ShowAsync(DialogRoot)) ? dialog.Text : null;
    }
}
