// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Midi;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace Fresco.Brix.Preferences; //was previously: frescobaldi/preferences/midi.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The MIDI page: which instrument bank the player sounds through, and how
/// loudly.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ Upstream's MIDI page is entirely about PORTS — an output port to play to,
/// an input port to write notes from, whether to close an unused output, and
/// how often to poll the input. Ruling FR6 replaced ports with in-process
/// synthesis, so every one of those settings has nothing behind it and the page
/// is a REPLACEMENT rather than a port: the two things a user of Fresco.Brix
/// can usefully choose are the bank and the volume.
/// </para>
/// <para>
/// ⚠ Board trap 51: the bundled bank's own <c>INAM</c> chunk reads
/// "GeneralUser GS 2.0.3 BETA" and is a leftover; it is never shown. The
/// application names what it shipped
/// (<see cref="SoundFonts.BundledDisplayName"/>), and a file the user brought
/// is named by its file name.
/// </para>
/// </remarks>
public sealed class MidiPage : PreferencesPage
{
    private TextBlock _instrumentName;
    private TextBlock _instrumentPath;
    private TrackBar _volume;
    private TextBlock _volumeText;
    private Button _reset;

    private string _chosen = string.Empty;

    /// <summary>Creates the page.</summary>
    /// <param name="context">What the page configures.</param>
    public MidiPage(PreferencesContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    public override string Title => I18n.Get("MIDI");

    /// <inheritdoc/>
    public override string Help => "prefs_midi";

    /// <inheritdoc/>
    public override string IconName => "audio-midi";

    /// <summary>Gets the values the page reads and writes.</summary>
    public MidiValues Values { get; } = new MidiValues();

    /// <inheritdoc/>
    public override void LoadSettings()
    {
        Values.Load(Settings);
        _chosen = Values.InstrumentPath ?? string.Empty;
        ShowInstrument();

        _volume.SetValueQuietly(Values.VolumePercent);
        ShowVolume();
    }

    /// <inheritdoc/>
    public override void SaveSettings()
    {
        Values.InstrumentPath = _chosen ?? string.Empty;
        Values.VolumePercent = (int)Math.Round(_volume.Value);
        Values.Save(Settings);

        //The player is told at once, so the next note is heard at the new
        //volume rather than at the next launch. Its own setter writes the same
        //key, which is harmless and keeps the two in step.
        if (Context.MidiPlayer != null)
        {
            Context.MidiPlayer.Volume = (float)(Values.VolumePercent / 100.0);
        }
    }

    /// <inheritdoc/>
    protected override UIElement Build()
        => Stack(
            Group(I18n.Get("Instrument"), BuildInstrument()),
            Group(I18n.Get("Preferences"), BuildVolume()));

    private UIElement BuildInstrument()
    {
        _instrumentName = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _instrumentPath = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
        };

        Button choose = new Button
        {
            Content = MenuBuilder.Display(I18n.Get("&Change...")),
        };
        choose.Click += async (_, _) => await ChooseAsync();
        choose.IsEnabled = Context.PickFileAsync != null;

        _reset = new Button { Content = I18n.Get("Default") };
        ToolTipService.SetToolTip(
            _reset, I18n.Get("Restores the instrument that came with the program."));
        _reset.Click += (_, _) =>
        {
            _chosen = string.Empty;
            ShowInstrument();
            MarkChanged();
        };

        StackPanel buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        buttons.Children.Add(choose);
        buttons.Children.Add(_reset);

        return Rows(
            Note(I18n.Get(
                "Music is played inside the program, through the instrument bank "
                + "named below. Choose a SoundFont of your own to play through that "
                + "instead.")),
            Labelled(I18n.Get("Instrument:"), _instrumentName),
            _instrumentPath,
            buttons);
    }

    private UIElement BuildVolume()
    {
        //A drawn track bar rather than a Slider: every part of the theme's
        //Slider paints nothing on the Skia heads (board trap 53), which is why
        //the MIDI panel's own volume control is one of these too.
        _volume = new TrackBar
        {
            Minimum = 0,
            Maximum = 200,
            Value = 100,
            IsTracking = true,
            MinWidth = 260,
        };
        _volume.Moved += (_, _) => ShowVolume();
        _volume.ValueChanged += (_, _) =>
        {
            ShowVolume();
            MarkChanged();
        };

        _volumeText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 60,
        };

        Grid row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(_volume);
        Grid.SetColumn(_volumeText, 1);
        row.Children.Add(_volumeText);

        return Rows(Labelled(I18n.Get("Volume:"), row));
    }

    private void ShowVolume()
        => _volumeText.Text = ((int)Math.Round(_volume.Value))
            .ToString(CultureInfo.CurrentCulture) + "%";

    private void ShowInstrument()
    {
        string path = string.IsNullOrEmpty(_chosen) || !FileIsThere(_chosen)
            ? SoundFonts.BundledPath
            : _chosen;

        _instrumentName.Text = FileIsThere(path)
            ? SoundFonts.DisplayName(path)
            : I18n.Get("(none)");
        _instrumentPath.Text = path;
        _reset.IsEnabled = !string.IsNullOrEmpty(_chosen);
    }

    private static bool FileIsThere(string path)
    {
        try { return !string.IsNullOrEmpty(path) && File.Exists(path); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private async Task ChooseAsync()
    {
        Func<IReadOnlyList<string>, Task<string>> pick = Context.PickFileAsync;
        if (pick == null) { return; }

        string chosen = await pick(SoundFonts.Extensions);
        if (string.IsNullOrEmpty(chosen)) { return; }

        _chosen = chosen;
        ShowInstrument();
        MarkChanged();
    }
}
