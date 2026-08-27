// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;

namespace Fresco.Brix.Commands; //was previously: frescobaldi/engrave/__init__.py (class Actions)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The LilyPond menu's commands: engraving a document, in each of the modes,
/// and everything around a running engrave.
/// </summary>
/// <remarks>
/// Upstream's collection, minus what FR5.1 rules out. There is one engine,
/// compiled in, so <c>engrave_open_lilypond_datadir</c> — which opens the
/// chosen installation's data directory — has nothing to point at and is
/// replaced by the engine-information command.
/// </remarks>
public sealed class EngraveActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "engrave";

    /// <summary>The setting remembering whether automatic engrave is on.</summary>
    public const string AutoCompileSettingKey = "engraving/autocompile";

    /// <summary>Creates the engrave commands.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public EngraveActions(SettingsStore settings = null)
        : base(CollectionName, settings)
        => Initialize();

    /// <inheritdoc/>
    public override string Title => I18n.Get("Engraving");

    /// <summary>LilyPond &gt; Always Engrave This Document.</summary>
    public AppAction EngraveSticky { get; private set; }

    /// <summary>The toolbar's one engrave button: run, or abort what runs.</summary>
    public AppAction EngraveRunner { get; private set; }

    /// <summary>LilyPond &gt; Engrave (preview).</summary>
    public AppAction EngravePreview { get; private set; }

    /// <summary>LilyPond &gt; Engrave (publish).</summary>
    public AppAction EngravePublish { get; private set; }

    /// <summary>LilyPond &gt; Engrave (layout control).</summary>
    public AppAction EngraveDebug { get; private set; }

    /// <summary>LilyPond &gt; Engrave (custom).</summary>
    public AppAction EngraveCustom { get; private set; }

    /// <summary>LilyPond &gt; Abort Engraving Job.</summary>
    public AppAction EngraveAbort { get; private set; }

    /// <summary>LilyPond &gt; Automatic Engrave.</summary>
    public AppAction EngraveAutoCompile { get; private set; }

    /// <summary>LilyPond &gt; Engine Information.</summary>
    public AppAction EngraveEngineInfo { get; private set; }

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        EngraveSticky = Add("engrave_sticky").WithIcon("pushpin").AsToggle();
        EngraveRunner = Add("engrave_runner").WithIcon("lilypond-run");
        EngravePreview = Add("engrave_preview").WithIcon("lilypond-run")
            .WithShortcut("Ctrl+M");
        EngravePublish = Add("engrave_publish").WithIcon("lilypond-run")
            .WithShortcut("Ctrl+Shift+P");
        EngraveDebug = Add("engrave_debug").WithIcon("lilypond-run");
        EngraveCustom = Add("engrave_custom").WithIcon("lilypond-run")
            .WithShortcut("Ctrl+Shift+M");
        EngraveAbort = Add("engrave_abort").WithIcon("lilypond-stop")
            .WithShortcut("Ctrl+Pause");
        EngraveAutoCompile = Add("engrave_autocompile").AsToggle();
        EngraveEngineInfo = Add("engrave_engine_info").WithIcon("help-about");
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        EngraveSticky.Text = I18n.Get("&Always Engrave This Document");
        EngraveRunner.Text = I18n.Get("Engrave");
        EngraveRunner.ToolTip = I18n.Get("Engrave (preview; Shift-click for custom)");
        EngravePreview.Text = I18n.Get("&Engrave (preview)");
        EngravePublish.Text = I18n.Get("Engrave (&publish)");
        EngraveDebug.Text = I18n.Get("Engrave (&layout control)");
        EngraveCustom.Text = I18n.Get("Engrave (&custom)...");
        EngraveAbort.Text = I18n.Get("Abort Engraving &Job");
        EngraveAutoCompile.Text = I18n.Get("Automatic E&ngrave");
        EngraveEngineInfo.Text = I18n.Get("Engine &Information...");
    }
}

/// <summary>The log panel's commands for stepping through error messages.</summary>
public sealed class LogActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "logtool";

    /// <summary>Creates the log commands.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public LogActions(SettingsStore settings = null)
        : base(CollectionName, settings)
        => Initialize();

    /// <inheritdoc/>
    public override string Title => I18n.Get("LilyPort Log");

    /// <summary>Jump to the next error message.</summary>
    public AppAction LogNextError { get; private set; }

    /// <summary>Jump to the previous error message.</summary>
    public AppAction LogPreviousError { get; private set; }

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        LogNextError = Add("log_next_error").WithShortcut("Ctrl+E");
        LogPreviousError = Add("log_previous_error").WithShortcut("Ctrl+Shift+E");
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        LogNextError.Text = I18n.Get("Next Error Message");
        LogPreviousError.Text = I18n.Get("Previous Error Message");
    }
}
