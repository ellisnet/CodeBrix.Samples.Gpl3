// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Midi; //was previously: frescobaldi/midiinput/__init__.py + midiinput/widget.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>How note entry writes what is played.</summary>
/// <remarks>
/// Upstream keeps these on its MIDI Input panel and reads them off the widgets
/// as each note arrives. Ruling FR5.4 defers the panel, so they live here, are
/// remembered in the settings store under upstream's own keys, and the panel
/// binds to them when it arrives.
/// </remarks>
public sealed class MidiInputOptions
{
    /// <summary>The settings prefix, matching upstream's group name.</summary>
    public const string SettingsPrefix = "midiinputdock/";

    private readonly SettingsStore _settings;

    /// <summary>Creates the options, reading whatever was remembered.</summary>
    /// <param name="settings">The settings store, or null for the defaults.</param>
    public MidiInputOptions(SettingsStore settings = null)
    {
        _settings = settings;
        Channel = _settings?.GetInt(SettingsPrefix + "midichannel", 0) ?? 0;
        KeySignature = _settings?.GetInt(SettingsPrefix + "keysignature", 7) ?? 7;
        Sharps = (_settings?.GetString(SettingsPrefix + "accidentals", "sharps")
            ?? "sharps") != "flats";
        ChordMode = _settings?.GetBool(SettingsPrefix + "chordmode") ?? false;
        RelativeMode = _settings?.GetBool(SettingsPrefix + "relativemode") ?? false;
        RepitchMode = _settings?.GetBool(SettingsPrefix + "repitchmode") ?? false;
    }

    /// <summary>
    /// Gets or sets which MIDI channel to listen to: 0 captures all, and 1 to
    /// 16 are the channels as a human numbers them.
    /// </summary>
    public int Channel { get; set; }

    /// <summary>
    /// Gets or sets the key signature: 0 is seven flats, 7 is C major, 14 is
    /// seven sharps.
    /// </summary>
    public int KeySignature { get; set; }

    /// <summary>Gets or sets whether an altered note is spelled with a sharp.</summary>
    public bool Sharps { get; set; }

    /// <summary>Gets or sets whether notes played together become a chord.</summary>
    public bool ChordMode { get; set; }

    /// <summary>Gets or sets whether octaves are written relative to the last key.</summary>
    public bool RelativeMode { get; set; }

    /// <summary>Gets or sets whether notes REPLACE the pitches already written.</summary>
    public bool RepitchMode { get; set; }

    /// <summary>
    /// Gets or sets whether to add an octave check to each note.
    /// </summary>
    /// <remarks>
    /// ⚠ Upstream reads <c>QApplication.keyboardModifiers()</c> at the moment
    /// the note arrives — hold Shift and you get an octave check. That is a
    /// live keyboard question, not a setting, so it is not remembered; the
    /// panel sets it per note when it arrives, and a test sets it directly.
    /// </remarks>
    public bool OctaveCheck { get; set; }

    /// <summary>Writes the options back to the settings store.</summary>
    public void Save()
    {
        if (_settings == null) { return; }

        _settings.SetInt(SettingsPrefix + "midichannel", Channel);
        _settings.SetInt(SettingsPrefix + "keysignature", KeySignature);
        _settings.SetString(SettingsPrefix + "accidentals", Sharps ? "sharps" : "flats");
        _settings.SetBool(SettingsPrefix + "chordmode", ChordMode);
        _settings.SetBool(SettingsPrefix + "relativemode", RelativeMode);
        _settings.SetBool(SettingsPrefix + "repitchmode", RepitchMode);
    }
}

/// <summary>
/// Where note entry puts what it writes: the document being played into.
/// </summary>
/// <remarks>
/// Upstream reaches straight into a <c>QTextCursor</c>. This is the same three
/// questions asked of whatever holds the text, so the note-entry logic can be
/// driven by scripted events with no window at all — which is what ruling
/// FR5.4 asks for.
/// </remarks>
public interface IMidiInputTarget
{
    /// <summary>Gets the whole text.</summary>
    string Text { get; }

    /// <summary>Gets the caret's offset in the text.</summary>
    int CaretOffset { get; }

    /// <summary>Replaces a range with new text and leaves the caret after it.</summary>
    /// <param name="offset">Where the range starts.</param>
    /// <param name="length">How long it is; zero inserts.</param>
    /// <param name="text">What to put there.</param>
    void Replace(int offset, int length, string text);
}

/// <summary>
/// Note entry: turns the keys played on a MIDI keyboard into LilyPond source in
/// the document.
/// </summary>
/// <remarks>
/// <para>
/// The whole of upstream's <c>MidiIn</c>, minus its two ends. The device end is
/// <see cref="IMidiInputDevice"/> rather than a thread polling PortMIDI
/// (FR5.4); the document end is <see cref="IMidiInputTarget"/> rather than a
/// <c>QTextCursor</c>. What is between them — the channel filter, chord
/// accumulation, the re-pitch search and where the space goes — is ported
/// exactly, and is what the scripted-event tests drive.
/// </para>
/// <para>
/// ⚠ Upstream's own module docstring says what is NOT here, and it is not here
/// either: "special midi events (e.g. damper pedal) can modify notes ... current
/// limitations: special events not implemented yet". Its panel shows three
/// pedal combo boxes that nothing reads.
/// </para>
/// </remarks>
public sealed class MidiInput
{
    /// <summary>
    /// What re-pitch mode replaces: a chord or a pitch name, but not a command
    /// or a variable.
    /// </summary>
    /// <remarks>
    /// Upstream's <c>LY_REG_EXPR</c>, VERBATIM, with its own comment: "What this
    /// does was originally undocumented. It appears intended to match chord and
    /// pitch names, but not commands or variables (thanks @ksnortum)". The two
    /// halves are a one-to-three-letter word not preceded by a letter, <c>#</c>,
    /// <c>_</c>, <c>^</c>, <c>-</c> or a backslash and not followed by a letter,
    /// with any number of octave marks after it; or a <c>&lt;…&gt;</c> chord.
    /// ⚠ The letters skip 'r' and 'R' (the ranges are a-p and s-z), which is
    /// how a rest keeps its place while the notes around it are re-pitched.
    /// </remarks>
    public static readonly Regex PitchPattern = new Regex(
        @"(?<![a-zA-Z#_^\-\\])[a-ps-zA-PS-Z]{1,3}(?![a-zA-Z])['\,]*"
        + "|"
        + @"(?<![<\\])<[^<>]*>(?!>)",
        RegexOptions.Compiled);

    private readonly IMidiInputTarget _target;
    private MidiChord _chord;
    private int _activeNotes;

    /// <summary>Creates note entry over a document.</summary>
    /// <param name="target">Where the notes are written.</param>
    /// <param name="options">How they are written.</param>
    public MidiInput(IMidiInputTarget target, MidiInputOptions options = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        Options = options ?? new MidiInputOptions();
    }

    /// <summary>Gets or sets how the notes are written.</summary>
    public MidiInputOptions Options { get; set; }

    /// <summary>
    /// Gets or sets the pitch-name language notes are written in.
    /// </summary>
    /// <remarks>Upstream reads it off the document when capturing starts —
    /// <c>documentinfo.docinfo(doc).language() or 'nederlands'</c> — and does
    /// not look again while capturing.</remarks>
    public string Language { get; set; } = "nederlands";

    /// <summary>Gets the device being listened to, or null.</summary>
    public IMidiInputDevice Device { get; private set; }

    /// <summary>Gets whether notes are being captured.</summary>
    public bool IsCapturing => Device is { IsCapturing: true };

    /// <summary>Gets how many keys are currently held down.</summary>
    public int ActiveNotes => _activeNotes;

    /// <summary>Starts capturing from a device.</summary>
    /// <param name="device">The device.</param>
    /// <param name="language">The document's pitch-name language, or null to
    /// keep the current one.</param>
    public void StartCapturing(IMidiInputDevice device, string language = null)
    {
        if (device == null) { return; }

        StopCapturing();

        if (!string.IsNullOrEmpty(language)) { Language = language; }

        Device = device;
        _activeNotes = 0;
        _chord = null;
        device.MessageReceived += OnMessageReceived;
        device.Start();
    }

    /// <summary>Stops capturing.</summary>
    public void StopCapturing()
    {
        IMidiInputDevice device = Device;
        Device = null;
        _activeNotes = 0;
        _chord = null;

        if (device == null) { return; }

        device.MessageReceived -= OnMessageReceived;
        device.Stop();
    }

    /// <summary>Handles one MIDI message.</summary>
    /// <param name="message">The message.</param>
    /// <remarks>Public so a test can drive the pipeline without a device at
    /// all, which is how the chord and re-pitch cases are exercised.</remarks>
    public void ProcessMessage(MidiInputMessage message)
    {
        if (message.Type != MidiInputMessageType.NoteOn
            && message.Type != MidiInputMessageType.NoteOff)
        {
            //Upstream's analyzeEvent only forwards NoteEvents.
            return;
        }

        int targetChannel = Options.Channel;

        //MIDI channels start at 1 for humans and 0 for programs; 0 here means
        //"every channel". Upstream's own comment, and its own arithmetic.
        if (targetChannel != 0 && message.Channel != targetChannel - 1) { return; }

        if (message.Type == MidiInputMessageType.NoteOn && message.Value > 0)
        {
            NoteMapping mapping = new NoteMapping(Options.KeySignature, Options.Sharps);
            MidiNote note = new MidiNote(message.Note, mapping);
            if (Options.ChordMode)
            {
                _chord ??= new MidiChord();
                _chord.Add(note);
                _activeNotes++;
            }
            else
            {
                AddToDocument(note.Output(
                    Options.RelativeMode, Language, Options.OctaveCheck));
            }

            return;
        }

        bool released = message.Type == MidiInputMessageType.NoteOff
            || (message.Type == MidiInputMessageType.NoteOn && message.Value == 0);
        if (!released || !Options.ChordMode) { return; }

        _activeNotes--;

        //Upstream's own comment: activenotes could get negative under strange
        //conditions.
        if (_activeNotes > 0) { return; }

        if (_chord != null)
        {
            AddToDocument(_chord.Output(
                Options.RelativeMode, Language, Options.OctaveCheck));
        }

        _activeNotes = 0;
        _chord = null;
    }

    /// <summary>Writes one note or chord into the document.</summary>
    /// <param name="text">The source text.</param>
    public void AddToDocument(string text)
    {
        if (string.IsNullOrEmpty(text)) { return; }

        string document = _target.Text ?? string.Empty;
        int caret = Math.Clamp(_target.CaretOffset, 0, document.Length);

        if (Options.RepitchMode)
        {
            //Upstream searches the text FROM the cursor onwards and replaces
            //the first pitch it finds, leaving every duration and rest alone.
            //A search that finds nothing writes nothing at all.
            //⚠ It searches a SLICE — `toPlainText()[cursor.position():]` — not
            //the whole text from an offset, and those are not the same thing: a
            //lookbehind cannot see past the start of a slice, so a pitch
            //directly after the caret matches even when the character before
            //the caret is a letter. Ported as the slice it is.
            string ahead = document.Substring(caret);
            Match match = PitchPattern.Match(ahead);
            if (match.Success)
            {
                _target.Replace(caret + match.Index, match.Length, text);
            }

            return;
        }

        //A space goes in first unless there is one already, or the caret is at
        //the start of its line. Upstream asks the LINE, so a caret at the start
        //of a line is "at block start" whatever precedes the newline.
        bool atLineStart = caret == 0 || document[caret - 1] == '\n';
        bool afterWhitespace = !atLineStart && char.IsWhiteSpace(document[caret - 1]);
        _target.Replace(caret, 0, atLineStart || afterWhitespace ? text : " " + text);
    }

    private void OnMessageReceived(object sender, MidiInputMessage message)
        => ProcessMessage(message);
}

/// <summary>
/// A device that plays whatever a test tells it to.
/// </summary>
/// <remarks>
/// ⚠ Ruling FR5.4 puts the virtual device "in tests", and that is where the one
/// the note-entry tests drive lives. This one is here for the same reason a
/// null renderer is: it is the thing an application can offer when a machine
/// has no MIDI keyboard, and it is what a future real device is written
/// against. It captures nothing on its own and is registered by nobody.
/// </remarks>
public sealed class VirtualMidiInputDevice : IMidiInputDevice
{
    /// <summary>Creates the device.</summary>
    /// <param name="name">The name to show.</param>
    public VirtualMidiInputDevice(string name = "Virtual MIDI keyboard")
        => Name = name;

    /// <inheritdoc/>
    public event EventHandler<MidiInputMessage> MessageReceived;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public bool IsCapturing { get; private set; }

    /// <inheritdoc/>
    public void Start() => IsCapturing = true;

    /// <inheritdoc/>
    public void Stop() => IsCapturing = false;

    /// <summary>Sends a message, as if a key had been touched.</summary>
    /// <param name="message">The message.</param>
    /// <remarks>Nothing is sent while the device is stopped, which is what a
    /// real one does too.</remarks>
    public void Send(MidiInputMessage message)
    {
        if (IsCapturing) { MessageReceived?.Invoke(this, message); }
    }

    /// <summary>Presses and releases one key.</summary>
    /// <param name="note">The note number.</param>
    /// <param name="velocity">The velocity.</param>
    /// <param name="channel">The channel, 0 to 15.</param>
    public void PlayNote(int note, int velocity = 64, int channel = 0)
    {
        Send(MidiInputMessage.NoteOn(note, velocity, channel));
        Send(MidiInputMessage.NoteOff(note, channel));
    }

    /// <inheritdoc/>
    public void Dispose() => Stop();
}
