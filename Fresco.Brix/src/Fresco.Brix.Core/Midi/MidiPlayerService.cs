// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.Sfz;
using Fresco.Brix.Services;
using System;
using System.IO;
using System.Threading;

namespace Fresco.Brix.Midi; //was previously: frescobaldi/miditool/player.py + output.py + midihub.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The MIDI player: a SoundFont (or SFZ instrument) and a sequence, sounding
/// through the audio engine in this process.
/// </summary>
/// <remarks>
/// <para>
/// This is what ruling FR6 put in place of upstream's three-part output chain.
/// <c>midihub.py</c> enumerated PortMidi ports and handed out
/// <c>portmidi.Output</c> objects; <c>miditool/output.py</c> wrapped one;
/// <c>miditool/player.py</c> pushed events at it on a timer and, when seeking,
/// replayed the controller and program changes it had skipped over. None of
/// that survives: there is no port, no device list, no "No output found!" empty
/// state, and the sequencer inside the audio engine already handles what a seek
/// has to do to the synthesizer's state.
/// </para>
/// <para>
/// ⚠ NOTHING TOUCHES THE SOUND CARD UNTIL SOMETHING IS LOADED.
/// <c>MidiMusicPlayer.Load</c> starts the shared audio device, so the service is
/// built lazily and a window whose MIDI panel is never opened never opens an
/// audio device either. That is also upstream's own arrangement — it opened its
/// output port when play was first pressed and had a preference to close it
/// again after a minute.
/// </para>
/// <para>
/// Board trap 22 applies in full: the audio engine's callbacks arrive on its
/// own real-time thread, so every event this class raises is posted back to the
/// thread that built it.
/// </para>
/// </remarks>
public sealed class MidiPlayerService : IMidiPlayer
{
    /// <summary>How often the position is reported while playing.</summary>
    /// <remarks>Upstream's <c>_timeSliderTicker</c> interval, kept.</remarks>
    public const int TickMilliseconds = 200;

    private readonly SettingsStore _settings;
    private readonly SoundFontCache _soundFonts = new SoundFontCache();
    private readonly SfzInstrumentCache _sfzInstruments = new SfzInstrumentCache();
    private readonly SynchronizationContext _context;
    private readonly object _gate = new object();

    private MidiMusicPlayer _player;
    private Timer _ticker;
    private MidiPlayerState _state = MidiPlayerState.Stopped;
    private double _tempoFactor = 1.0;
    private float _volume = 1.0f;
    private long _totalTime;
    private bool _disposed;

    /// <summary>Creates the player.</summary>
    /// <param name="settings">The store the instrument choice and the volume
    /// are remembered in, or null.</param>
    public MidiPlayerService(SettingsStore settings = null)
    {
        _settings = settings;
        _context = SynchronizationContext.Current;

        //The remembered volume, as a percentage so the settings file stays
        //readable. Upstream has no volume at all — its output was somebody
        //else's synthesizer — so this is ours, and W12's MIDI preferences page
        //reads the same key.
        int stored = _settings?.GetInt(VolumeSettingKey, 100) ?? 100;
        _volume = Math.Clamp(stored, 0, 200) / 100f;
    }

    /// <summary>The setting holding the playback volume, as a percentage.</summary>
    public const string VolumeSettingKey = "midi/volume";

    /// <inheritdoc/>
    public event EventHandler PositionChanged;

    /// <inheritdoc/>
    public event EventHandler StateChanged;

    /// <inheritdoc/>
    public event EventHandler PlaybackEnded;

    /// <summary>Raised when loading an instrument or a file failed.</summary>
    /// <remarks>The panel shows this on its display; there is nowhere else for
    /// it to go, and an exception out of a transport button is not an answer.</remarks>
    public event EventHandler<string> LoadFailed;

    /// <inheritdoc/>
    public MidiPlayerState State => _state;

    /// <inheritdoc/>
    public bool HasSong => _player is { IsLoaded: true };

    /// <inheritdoc/>
    public bool HasEvents => HasSong && (_totalTime <= 0 || CurrentTime < _totalTime);

    /// <inheritdoc/>
    public string FileName { get; private set; }

    /// <inheritdoc/>
    public MidiSong Song { get; private set; }

    /// <inheritdoc/>
    public long TotalTime => _totalTime;

    /// <inheritdoc/>
    /// <remarks>
    /// ⚠ CLAMPED TO <see cref="TotalTime"/>, and the clamp is load-bearing rather than
    /// cosmetic. <c>MidiMusicPlayer.Duration</c> is the SEQUENCE's own length — its event
    /// list — while <c>Position</c> is the sequencer's clock, and that clock goes on
    /// running while the final voices ring out after the last event. An unclamped reading
    /// therefore overshoots the length by the width of the release tail, and the panel's
    /// time display and its position bar would both run past their own end. Clamping here
    /// rather than in the panel keeps every reader honest, <see cref="HasEvents"/>
    /// included.
    /// </remarks>
    public long CurrentTime
    {
        get
        {
            MidiMusicPlayer player = _player;
            if (player == null) { return 0; }

            try
            {
                long position = (long)player.Position.TotalMilliseconds;
                return _totalTime > 0
                    ? Math.Clamp(position, 0, _totalTime)
                    : Math.Max(0, position);
            }
            catch (ObjectDisposedException) { return 0; }
        }
    }

    /// <summary>Gets the instrument file in use, or null.</summary>
    public string InstrumentPath { get; private set; }

    /// <inheritdoc/>
    public double TempoFactor
    {
        get => _tempoFactor;
        set
        {
            //Upstream's slider runs -50..50 and converts with 2**(v/50), so the
            //range really used is a half to double speed. Anything at or below
            //zero would stop time.
            _tempoFactor = value <= 0 ? 1.0 : value;
            MidiMusicPlayer player = _player;
            if (player != null)
            {
                try { player.Speed = (float)_tempoFactor; }
                catch (ObjectDisposedException) { }
            }
        }
    }

    /// <inheritdoc/>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 2f);
            _settings?.SetInt(VolumeSettingKey, (int)Math.Round(_volume * 100));
            MidiMusicPlayer player = _player;
            if (player != null)
            {
                try { player.Volume = _volume; }
                catch (ObjectDisposedException) { }
            }
        }
    }

    /// <inheritdoc/>
    public bool Load(string fileName, MidiSong song = null)
    {
        if (_disposed) { return false; }

        Stop();

        if (string.IsNullOrEmpty(fileName))
        {
            Clear();
            return false;
        }

        string instrument = SoundFonts.Resolve(_settings);
        if (instrument == null)
        {
            //FD2's bank is a default, not a dependency: emptied out of the
            //assets folder, or pointed at something that has been deleted,
            //there is simply nothing to sound through.
            RaiseLoadFailed(I18n.Get("No instrument found!"));
            return false;
        }

        try
        {
            MidiSequence sequence = new MidiSequence(fileName);
            MidiMusicPlayer player = EnsurePlayer();

            if (string.Equals(
                Path.GetExtension(instrument), ".sfz", StringComparison.OrdinalIgnoreCase))
            {
                player.Load(_sfzInstruments.Get(instrument), sequence);
            }
            else
            {
                player.Load(_soundFonts.Get(instrument), sequence);
            }

            InstrumentPath = instrument;
            FileName = fileName;
            Song = song ?? SafeSong(fileName);
            _totalTime = (long)player.Duration.TotalMilliseconds;
            player.Speed = (float)_tempoFactor;
            player.Volume = _volume;
            SetState(MidiPlayerState.Stopped);
            Raise(Notification.Position);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or FormatException
            or InvalidDataException
            or NotSupportedException
            or InvalidOperationException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            RaiseLoadFailed(exception.Message);
            return false;
        }
    }

    /// <inheritdoc/>
    public void Play()
    {
        MidiMusicPlayer player = _player;
        if (player == null || !player.IsLoaded) { return; }

        try { player.Play(); }
        catch (ObjectDisposedException) { return; }
        catch (InvalidOperationException) { return; }

        SetState(MidiPlayerState.Playing);
        StartTicking();
    }

    /// <inheritdoc/>
    public void Pause()
    {
        MidiMusicPlayer player = _player;
        if (player == null || _state != MidiPlayerState.Playing) { return; }

        try { player.Pause(); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }

        StopTicking();
        SetState(MidiPlayerState.Paused);
        Raise(Notification.Position);
    }

    /// <inheritdoc/>
    public void Stop()
    {
        MidiMusicPlayer player = _player;
        if (player == null) { return; }

        try { player.Stop(); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }

        StopTicking();
        SetState(MidiPlayerState.Stopped);
        Raise(Notification.Position);
    }

    /// <inheritdoc/>
    public void Seek(long milliseconds)
    {
        MidiMusicPlayer player = _player;
        if (player == null || !player.IsLoaded) { return; }

        long clamped = Math.Clamp(milliseconds, 0, Math.Max(0, _totalTime));
        try { player.Seek(TimeSpan.FromMilliseconds(clamped)); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }

        Raise(Notification.Position);
    }

    /// <inheritdoc/>
    public void Clear()
    {
        StopTicking();
        FileName = null;
        Song = null;
        _totalTime = 0;

        MidiMusicPlayer player;
        lock (_gate)
        {
            player = _player;
            _player = null;
        }

        if (player != null)
        {
            player.PlaybackEnded -= OnPlaybackEnded;
            try { player.Dispose(); }
            catch (ObjectDisposedException) { }
        }

        SetState(MidiPlayerState.Stopped);
        Raise(Notification.Position);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;
        Clear();
        _soundFonts.Dispose();
        _sfzInstruments.Dispose();
    }

    private MidiMusicPlayer EnsurePlayer()
    {
        lock (_gate)
        {
            if (_player == null)
            {
                _player = new MidiMusicPlayer();
                _player.PlaybackEnded += OnPlaybackEnded;
            }

            return _player;
        }
    }

    private static MidiSong SafeSong(string fileName)
    {
        try { return MidiSong.Load(fileName); }
        catch (IOException) { return null; }
        catch (FormatException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private void OnPlaybackEnded(object sender, EventArgs e) => EndReached();

    /// <summary>
    /// Called on every tick of the position timer.
    /// </summary>
    /// <remarks>
    /// The tick reports the position and nothing else. THE END OF THE SEQUENCE IS
    /// REPORTED TO US, by <c>MidiMusicPlayer.PlaybackEnded</c> arriving at
    /// <see cref="OnPlaybackEnded"/>.
    /// //was previously: the end was DETECTED here, by comparing the position against
    /// //the length on every tick, because CodeBrix.Audio 1.0.214.913 never raised
    /// //PlaybackEnded for a non-looping sequence — PlaybackState stayed Playing,
    /// //ActiveVoiceCount fell to zero, and Position counted past Duration indefinitely
    /// //(a 10.2-second file was still "playing" at 18 seconds, and in the panel it ran
    /// //to 3:19). CodeBrix.Audio 1.0.233.886 fixes it at the source: a finished
    /// //non-looping sequence now ends the STREAM, a zero-length read is the engine's
    /// //only end-of-stream signal, and the event follows from it.
    /// ⚠ THE TWO ARE NOT INTERCHANGEABLE, which is why the comparison was REMOVED
    /// rather than left standing beside the event. The tick fired at the LAST EVENT;
    /// the event fires once the final voices have stopped sounding. Ending at the last
    /// event meant <see cref="EndReached"/>'s Pause() CUT THE RELEASE TAIL off every
    /// score that finishes on a held chord. Letting the event own the ending lets the
    /// tail ring out, and <see cref="CurrentTime"/>'s clamp is what stops the display
    /// drifting past the length while it does.
    /// <see cref="EndReached"/> stays idempotent, so a library that one day reports the
    /// end twice — or reports it after something else has already stopped playback —
    /// still behaves.
    /// </remarks>
    private void OnTick() => Raise(Notification.Position);

    private void EndReached()
    {
        if (_state != MidiPlayerState.Playing) { return; }

        StopTicking();

        //Pause rather than stop: the position stays at the end, which is where
        //upstream leaves it too — its Restart button is what rewinds. It also
        //takes the now-silent component off the mixer's work list.
        MidiMusicPlayer player = _player;
        if (player != null)
        {
            try { player.Pause(); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        SetState(MidiPlayerState.Stopped);
        Raise(Notification.Position);
        Raise(Notification.Ended);
    }

    private void StartTicking()
    {
        StopTicking();
        _ticker = new Timer(
            _ => OnTick(), null, TickMilliseconds, TickMilliseconds);
    }

    private void StopTicking()
    {
        Timer ticker = _ticker;
        _ticker = null;
        ticker?.Dispose();
    }

    private void SetState(MidiPlayerState state)
    {
        if (_state == state) { return; }

        _state = state;
        Raise(Notification.State);
    }

    /// <summary>Which of this service's events to raise.</summary>
    private enum Notification
    {
        /// <summary>The position moved.</summary>
        Position,

        /// <summary>The transport state changed.</summary>
        State,

        /// <summary>The sequence reached its end.</summary>
        Ended,
    }

    private void Raise(Notification notification)
    {
        //The ticker runs on a thread-pool thread and the engine's own callbacks
        //arrive on its real-time thread; both end up touching a text store or a
        //slider, which only the UI thread may do (board trap 22). The event
        //field is read at DELIVERY time, so a handler that unsubscribed while a
        //post was in flight is not called.
        if (_context == null || _context == SynchronizationContext.Current)
        {
            Deliver(notification);
            return;
        }

        _context.Post(_ => Deliver(notification), null);
    }

    private void Deliver(Notification notification)
    {
        switch (notification)
        {
            case Notification.Position:
                PositionChanged?.Invoke(this, EventArgs.Empty);
                break;
            case Notification.State:
                StateChanged?.Invoke(this, EventArgs.Empty);
                break;
            case Notification.Ended:
                PlaybackEnded?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void RaiseLoadFailed(string message)
    {
        if (_context == null || _context == SynchronizationContext.Current)
        {
            LoadFailed?.Invoke(this, message);
            return;
        }

        _context.Post(_ => LoadFailed?.Invoke(this, message), null);
    }
}
