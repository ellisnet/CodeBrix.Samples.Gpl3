// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;

namespace Fresco.Brix.Commands; //was previously: frescobaldi/miditool/__init__.py (class Actions)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>The MIDI player's transport commands.</summary>
/// <remarks>
/// <para>
/// Upstream's four, with upstream's own transport-key defaults — except Pause.
/// Qt and X11 have a Key_MediaPause distinct from Key_MediaPlay; the platform's
/// virtual-key set follows Windows' VK numbering, which has only one
/// play/pause key, so binding Pause as well would give two commands the same
/// shortcut. It is left without a default rather than colliding with Play.
/// </para>
/// <para>
/// ⚠ And the transport keys reach no Linux head yet: the X11 and Wayland key
/// tables both carry <c>MediaPlayPause</c>, <c>MediaStop</c> and
/// <c>MediaPreviousTrack</c> as commented-out rows with no keysym filled in.
/// The shortcuts are declared anyway — they are what the commands SHOULD have,
/// they show correctly on the shortcut settings page, and they start working
/// the day the heads map those keysyms. The panel's own buttons are how the
/// transport is driven meanwhile, exactly as in Frescobaldi.
/// </para>
/// </remarks>
public sealed class MidiActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "miditool";

    /// <summary>Creates the MIDI commands.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public MidiActions(SettingsStore settings = null)
        : base(CollectionName, settings)
        => Initialize();

    /// <inheritdoc/>
    public override string Title => I18n.Get("MIDI");

    /// <summary>Start or resume playing.</summary>
    public AppAction MidiPlay { get; private set; }

    /// <summary>Interrupt playing, keeping the position.</summary>
    public AppAction MidiPause { get; private set; }

    /// <summary>Stop playing.</summary>
    public AppAction MidiStop { get; private set; }

    /// <summary>Rewind, reloading the file if it has changed.</summary>
    public AppAction MidiRestart { get; private set; }

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        MidiPause = Add("midi_pause").WithIcon("media-playback-pause");
        MidiPlay = Add("midi_play").WithIcon("media-playback-start")
            .WithShortcut("Media Play");
        MidiStop = Add("midi_stop").WithIcon("media-playback-stop")
            .WithShortcut("Media Stop");
        MidiRestart = Add("midi_restart").WithIcon("media-skip-backward")
            .WithShortcut("Media Previous");
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        MidiPause.Text = I18n.Get("midi player", "Pause");
        MidiPlay.Text = I18n.Get("midi player", "Play");
        MidiStop.Text = I18n.Get("midi player", "Stop");
        MidiRestart.Text = I18n.Get("midi player", "Restart");
    }
}
