// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Engrave;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/engrave/custom.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Engrave (custom) dialog: run this document once, with these settings,
/// without changing anything the application remembers.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's dialog is largely about choosing a LilyPond installation and an
/// output format, and neither survives here: there is one engine (FR5.1), and
/// it writes SVG pages and MIDI. What remains is what the dialog is FOR — the
/// engraving mode, the handful of switches, and a place to type options the
/// dialog does not offer — plus a plain statement of which engine is going to
/// run, which is the honest replacement for the version chooser.
/// </para>
/// </remarks>
public sealed class CustomEngraveDialog
{
    private readonly SettingsStore _settings;
    private ComboBox _mode;
    private CheckBox _deleteIntermediate;
    private CheckBox _embedSource;
    private TextBox _extraOptions;

    /// <summary>The setting remembering the last chosen mode.</summary>
    public const string ModeSettingKey = "engrave/custom/mode";

    /// <summary>The setting remembering the extra options.</summary>
    public const string OptionsSettingKey = "engrave/custom/options";

    /// <summary>Creates the dialog.</summary>
    /// <param name="settings">The settings store, or null.</param>
    public CustomEngraveDialog(SettingsStore settings = null) => _settings = settings;

    /// <summary>The modes the dialog offers, in upstream's order.</summary>
    public static IReadOnlyList<string> ModeNames { get; } = new[]
    {
        "preview", "publish", "incipit", "layout-control",
    };

    /// <summary>Asks the user how to engrave, and builds the job.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="document">The document to engrave.</param>
    /// <param name="layoutControlOptions">The layout-control panel's options.</param>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <returns>The configured job, or null when the user cancelled.</returns>
    public async Task<LilyPondJob> ShowAsync(
        LilyPortEngine engine,
        EditorDocument document,
        IReadOnlyList<string> layoutControlOptions,
        XamlRoot xamlRoot)
    {
        if (engine == null || document == null) { return null; }

        ContentDialog dialog = new ContentDialog
        {
            Title = I18n.Get("Engrave custom"),
            PrimaryButtonText = MenuBuilder.Display(I18n.Get("Run LilyPort")),
            CloseButtonText = StandardButtons.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            Content = BuildContent(),
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) { return null; }

        SaveSettings();
        return BuildJob(engine, document, layoutControlOptions);
    }

    private UIElement BuildContent()
    {
        StackPanel panel = new StackPanel { Spacing = 8, MinWidth = 420 };

        panel.Children.Add(new TextBlock
        {
            //The engine is named as itself and stamped with ITS OWN version; the
            //LilyPond release it implements rides along as the compatibility note.
            Text = I18n.Format(
                I18n.Get("Engine: LilyPort {version} (compatible with {compatible}), "
                    + "in this process."),
                ("version", LilyPortEngine.PortVersion),
                ("compatible", LilyPortEngine.CompatibleWithVersion)),
        });
        panel.Children.Add(new TextBlock
        {
            Text = I18n.Get("Output: SVG pages, and MIDI where a score asks for it."),
            Opacity = 0.75,
        });

        panel.Children.Add(new TextBlock { Text = I18n.Get("Engraving mode:") });
        _mode = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _mode.Items.Add(I18n.Get("Preview"));
        _mode.Items.Add(I18n.Get("Publish"));
        _mode.Items.Add(I18n.Get("First System Only"));
        _mode.Items.Add(I18n.Get("Layout Control"));
        _mode.SelectedIndex = Math.Clamp(
            _settings?.GetInt(ModeSettingKey) ?? 0, 0, ModeNames.Count - 1);
        panel.Children.Add(_mode);

        //was previously: `IsChecked = true' written out, and the embed box
        //always starting unticked. Upstream opens both on the PREFERENCES'
        //defaults (engrave/custom.py reads `lilypond_settings'), which is the
        //relationship the Tools page's "Running LilyPort" group restores.
        _deleteIntermediate = new CheckBox
        {
            Content = I18n.Get("Delete intermediate output files"),
            IsChecked = _settings?.GetBool(
                Engrave.Engraver.DeleteIntermediateSettingKey, true) ?? true,
        };
        _embedSource = new CheckBox
        {
            Content = I18n.Get("Embed Source Code"),
            IsChecked = _settings?.GetBool(
                Engrave.Engraver.EmbedSourceSettingKey, false) ?? false,
        };

        //⚠ Upstream's next row is "Run LilyPond with English messages"
        //(`no_translation'), which forces a LilyPond BINARY's translated
        //message catalogs back to English for a bug report. CodeBrix.LilyPort
        //ships no message catalogs at all — its diagnostics are English in
        //every interface language already — so the row would be a control that
        //changes nothing. Ruled out on that fact, not on FR5.1.

        panel.Children.Add(_deleteIntermediate);
        panel.Children.Add(_embedSource);

        panel.Children.Add(new TextBlock
        {
            Text = I18n.Get("Additional Command Line Options:"),
        });
        _extraOptions = new TextBox
        {
            AcceptsReturn = true,
            Height = 72,
            Text = _settings?.GetString(OptionsSettingKey, string.Empty) ?? string.Empty,
        };
        panel.Children.Add(_extraOptions);

        //Upstream's `userguide.addButton(self.buttons, "engrave_custom")'.
        panel.Children.Add(UserGuide.GuideHelp.ButtonRow("engrave_custom"));
        return panel;
    }

    private void SaveSettings()
    {
        _settings?.SetInt(ModeSettingKey, _mode?.SelectedIndex ?? 0);
        _settings?.SetString(OptionsSettingKey, _extraOptions?.Text ?? string.Empty);
    }

    private LilyPondJob BuildJob(
        LilyPortEngine engine,
        EditorDocument document,
        IReadOnlyList<string> layoutControlOptions)
    {
        int index = Math.Clamp(_mode?.SelectedIndex ?? 0, 0, ModeNames.Count - 1);
        LilyPondJob job = ModeNames[index] switch
        {
            "publish" => new PublishJob(engine, document),
            "layout-control" => new LayoutControlJob(
                engine, document, layoutControlOptions),
            _ => new PreviewJob(engine, document),
        };

        if (ModeNames[index] == "incipit")
        {
            //Upstream's "First System Only": engrave the preview stencil and
            //print no pages, which is how an incipit is produced.
            job.SetOption("preview", true);
            job.SetOption("print-pages", false);
        }

        job.SetOption(
            "delete-intermediate-files", _deleteIntermediate?.IsChecked == true);
        if (_embedSource?.IsChecked == true) { job.SetOption("embed-source-code", true); }

        foreach (var token in (_extraOptions?.Text ?? string.Empty)
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("-d", StringComparison.Ordinal))
            {
                (string name, object value) = LilyPondJob.ParseOption(token);
                job.SetOption(name, value);
            }
            else if (token.StartsWith("-I", StringComparison.Ordinal))
            {
                job.AddIncludePath(token.Substring(2));
            }
        }

        return job;
    }
}

/// <summary>
/// A plain statement of which engine is running, in place of upstream's
/// LilyPond installation chooser.
/// </summary>
/// <remarks>FR5.1: the engine is compiled in, so this window tells the user
/// what they have rather than offering them a choice they do not have.</remarks>
public static class EngineInfoDialog
{
    /// <summary>Shows the engine information.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <returns>The task.</returns>
    public static async Task ShowAsync(LilyPortEngine engine, XamlRoot xamlRoot)
    {
        StackPanel panel = new StackPanel { Spacing = 6, MinWidth = 380 };
        //The engine's OWN version first, then the LilyPond release it is compatible
        //with. They are different numbers and neither is ever shown as the other.
        panel.Children.Add(Row(
            I18n.Get("LilyPort version:"), LilyPortEngine.PortVersion));
        panel.Children.Add(Row(
            I18n.Get("Compatible with:"), LilyPortEngine.CompatibleWithVersion));
        panel.Children.Add(Row(I18n.Get("Engine:"), StateText(engine)));
        panel.Children.Add(Row(
            I18n.Get("Load time:"),
            engine.LoadElapsed == TimeSpan.Zero
                ? "—"
                : engine.LoadElapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)
                    + " s"));
        panel.Children.Add(new TextBlock
        {
            //FR13 EXEMPT, and deliberately so: this is an INFORMATIONAL
            //message, which the ruling allows to state the lineage, and its
            //whole purpose is to answer the question a Frescobaldi user brings
            //with them — "where do I choose my LilyPond version?". Naming
            //what is NOT here is the only way to answer it.
            Text = I18n.Get(
                "Fresco.Brix engraves in this process. There is no external "
                + "LilyPond installation, and no version to choose."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        });

        if (engine.Error != null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = engine.Error.Message,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        ContentDialog dialog = new ContentDialog
        {
            Title = I18n.Get("Engine Information"),
            CloseButtonText = I18n.Get("Close"),
            XamlRoot = xamlRoot,
            Content = panel,
        };

        await dialog.ShowAsync();
    }

    private static string StateText(LilyPortEngine engine)
        => engine.State switch
        {
            EngineState.Ready => I18n.Get("ready"),
            EngineState.Loading => I18n.Get("loading..."),
            EngineState.Failed => I18n.Get("failed to load"),
            _ => I18n.Get("not started"),
        };

    private static UIElement Row(string label, string value)
    {
        StackPanel row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        row.Children.Add(new TextBlock { Text = label, MinWidth = 140 });
        row.Children.Add(new TextBlock { Text = value });
        return row;
    }
}
