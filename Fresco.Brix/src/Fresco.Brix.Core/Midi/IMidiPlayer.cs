// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;

namespace Fresco.Brix.Midi; //was previously: frescobaldi/miditool/player.py + qmidi/player.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>What a player is doing.</summary>
public enum MidiPlayerState
{
    /// <summary>Nothing is playing, and the position is wherever it was left.</summary>
    Stopped,

    /// <summary>Sound is coming out.</summary>
    Playing,

    /// <summary>Playing was interrupted; the position is kept.</summary>
    Paused,
}

/// <summary>
/// The MIDI transport, as everything above it sees it: load a file, play,
/// pause, stop, seek, and be told where it has got to.
/// </summary>
/// <remarks>
/// <para>
/// This seam is ruling FR6's "keep the player behind a service seam so v1 does
/// not weld it to the panel" — Jeremy intends to grow MIDI capability past
/// Frescobaldi's after v1, and the panel should not have to change for it. It
/// also keeps the tests off the sound card: <see cref="MidiPlayerService"/>
/// opens a real audio device the moment anything is loaded, which a headless
/// test process has no business doing.
/// </para>
/// <para>
/// Upstream's shape was different because its output was different: a
/// <c>qmidi.player.Player</c> stepping an event list on its own thread and
/// pushing bytes at a PortMidi port, with <c>beat</c>, <c>time</c> and
/// <c>stateChanged</c> signals. The synthesis here happens inside the audio
/// engine, so what is left is the transport and one
/// <see cref="PositionChanged"/> that the panel's ticker drives.
/// </para>
/// </remarks>
public interface IMidiPlayer : IDisposable
{
    /// <summary>Raised when the position moves — while playing, or on a seek.</summary>
    event EventHandler PositionChanged;

    /// <summary>Raised when the player starts, pauses or stops.</summary>
    event EventHandler StateChanged;

    /// <summary>Raised when a sequence plays all the way to its end.</summary>
    event EventHandler PlaybackEnded;

    /// <summary>Gets what the player is doing.</summary>
    MidiPlayerState State { get; }

    /// <summary>Gets whether a sequence is loaded and ready.</summary>
    bool HasSong { get; }

    /// <summary>Gets whether there is anything LEFT to play.</summary>
    /// <remarks>Upstream's <c>has_events()</c>, which is
    /// <c>bool(self._events) and self._position &lt; len(self._events)</c> — so
    /// it goes false at the END of a sequence, not when nothing is loaded. That
    /// is what makes the panel's Play button rewind a finished song instead of
    /// resuming it at its last note.</remarks>
    bool HasEvents { get; }

    /// <summary>Gets the loaded file's path, or null.</summary>
    string FileName { get; }

    /// <summary>Gets the loaded song's beat grid, or null.</summary>
    MidiSong Song { get; }

    /// <summary>Gets the loaded sequence's length in milliseconds.</summary>
    long TotalTime { get; }

    /// <summary>Gets the position in milliseconds.</summary>
    long CurrentTime { get; }

    /// <summary>Gets or sets the tempo multiplier: 1.0 is the file's own tempo.</summary>
    double TempoFactor { get; set; }

    /// <summary>Gets or sets the output volume, where 1.0 is unity gain.</summary>
    float Volume { get; set; }

    /// <summary>Loads a MIDI file, positioned at the start and stopped.</summary>
    /// <param name="fileName">The file, or null to unload.</param>
    /// <param name="song">The file's beat grid, when it has already been read.</param>
    /// <returns>Whether the file could be loaded.</returns>
    bool Load(string fileName, MidiSong song = null);

    /// <summary>Starts or resumes playing.</summary>
    void Play();

    /// <summary>Stops playing, keeping the position.</summary>
    /// <remarks>Upstream's Pause and Stop buttons both call its player's
    /// <c>stop()</c>, which keeps the position — its Restart button is what
    /// rewinds. Both halves are here so the panel can keep those three
    /// buttons.</remarks>
    void Pause();

    /// <summary>Stops playing and rewinds to the start.</summary>
    void Stop();

    /// <summary>Moves to a position in milliseconds.</summary>
    /// <param name="milliseconds">Where to move to.</param>
    void Seek(long milliseconds);

    /// <summary>Forgets the loaded sequence and silences everything.</summary>
    void Clear();
}
