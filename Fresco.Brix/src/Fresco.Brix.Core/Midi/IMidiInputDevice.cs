// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;

namespace Fresco.Brix.Midi; //was previously: frescobaldi/midihub.py + midiinput/__init__.py (class Listener)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>The kinds of MIDI message note entry cares about.</summary>
/// <remarks>The numbers are the MIDI specification's own, and upstream names
/// the two it uses as <c>NOTE_OFF_EVENT = 8</c> and
/// <c>NOTE_ON_EVENT = 9</c>.</remarks>
public enum MidiInputMessageType
{
    /// <summary>A key was released (status 0x8n).</summary>
    NoteOff = 8,

    /// <summary>A key was pressed (status 0x9n); velocity 0 means released.</summary>
    NoteOn = 9,

    /// <summary>Anything else — note entry ignores it, as upstream does.</summary>
    Other = 0,
}

/// <summary>One MIDI message from an input device.</summary>
public readonly struct MidiInputMessage
{
    /// <summary>Creates a message.</summary>
    /// <param name="type">Which kind of message.</param>
    /// <param name="channel">The channel, 0 to 15 as the wire numbers them.</param>
    /// <param name="note">The note number, 0 to 127.</param>
    /// <param name="value">The velocity, 0 to 127.</param>
    public MidiInputMessage(
        MidiInputMessageType type, int channel, int note, int value)
    {
        Type = type;
        Channel = channel;
        Note = note;
        Value = value;
    }

    /// <summary>Gets which kind of message this is.</summary>
    public MidiInputMessageType Type { get; }

    /// <summary>Gets the channel, 0 to 15.</summary>
    /// <remarks>⚠ The WIRE numbering, which counts from zero. The panel's
    /// channel setting counts from one with 0 meaning "all", which is why the
    /// port compares <c>channel == targetChannel - 1</c>.</remarks>
    public int Channel { get; }

    /// <summary>Gets the note number, 0 to 127.</summary>
    public int Note { get; }

    /// <summary>Gets the velocity, 0 to 127.</summary>
    public int Value { get; }

    /// <summary>Makes a note-on message.</summary>
    /// <param name="note">The note number.</param>
    /// <param name="velocity">The velocity; zero means the key was released.</param>
    /// <param name="channel">The channel, 0 to 15.</param>
    /// <returns>The message.</returns>
    public static MidiInputMessage NoteOn(int note, int velocity = 64, int channel = 0)
        => new MidiInputMessage(MidiInputMessageType.NoteOn, channel, note, velocity);

    /// <summary>Makes a note-off message.</summary>
    /// <param name="note">The note number.</param>
    /// <param name="channel">The channel, 0 to 15.</param>
    /// <returns>The message.</returns>
    public static MidiInputMessage NoteOff(int note, int channel = 0)
        => new MidiInputMessage(MidiInputMessageType.NoteOff, channel, note, 0);

    /// <inheritdoc/>
    public override string ToString()
        => $"{Type} ch{Channel} note {Note} value {Value}";
}

/// <summary>
/// A source of MIDI messages — a keyboard the user plays into the document.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ THIS IS A SEAM WITH NO REAL IMPLEMENTATION IN v1, BY RULING FR5.4. The
/// note-entry logic above it is ported and tested in full; the tool panel and a
/// device that really listens to hardware arrive as a fast-follow, when MIDI
/// input capture lands in the CodeBrix ecosystem. CodeBrix.Audio 1.0.214.913
/// synthesizes and plays but does not capture, so there is nothing to plug in
/// yet.
/// </para>
/// <para>
/// Upstream's shape was a <c>QThread</c> polling a <c>portmidi.Input</c> every
/// few milliseconds and emitting a Qt signal per event, with
/// <c>midihub</c> enumerating the ports. A device here pushes instead of being
/// polled, because nothing about the seam should assume polling.
/// </para>
/// </remarks>
public interface IMidiInputDevice : IDisposable
{
    /// <summary>Raised for each message the device receives.</summary>
    event EventHandler<MidiInputMessage> MessageReceived;

    /// <summary>Gets the name to show for the device.</summary>
    string Name { get; }

    /// <summary>Gets whether the device is listening.</summary>
    bool IsCapturing { get; }

    /// <summary>Starts listening.</summary>
    void Start();

    /// <summary>Stops listening.</summary>
    void Stop();
}

/// <summary>
/// The MIDI input devices the application can offer.
/// </summary>
/// <remarks>
/// This is what is left of <c>midihub.py</c> after ruling FR6 removed its
/// output half entirely (there are no output ports; the synthesizer is in this
/// process) and ruling FR5.4 deferred a real input device. Upstream's module
/// initialised PortMIDI, enumerated devices, picked a default that was not a
/// "through" port, and could restart the whole library when the preferences
/// changed. Here it is a registry: empty in v1, and the one place a device
/// implementation has to be announced to.
/// </remarks>
public static class MidiInputDevices
{
    private static readonly List<IMidiInputDevice> Devices = new List<IMidiInputDevice>();
    private static readonly object Gate = new object();

    /// <summary>Raised when a device is added or removed.</summary>
    public static event EventHandler DevicesChanged;

    /// <summary>Gets whether any device is available.</summary>
    /// <remarks>Upstream's <c>available()</c>, which answered whether PortMIDI
    /// was there at all.</remarks>
    public static bool Available
    {
        get { lock (Gate) { return Devices.Count > 0; } }
    }

    /// <summary>Gets the known devices.</summary>
    public static IReadOnlyList<IMidiInputDevice> All
    {
        get { lock (Gate) { return Devices.ToArray(); } }
    }

    /// <summary>Gets the device to use unless the user says otherwise.</summary>
    /// <returns>The device, or null when there is none.</returns>
    /// <remarks>Upstream skips any port whose name contains "through", which is
    /// ALSA's loopback port and never what a user means; the same rule applies
    /// to whatever eventually registers here.</remarks>
    public static IMidiInputDevice Default()
    {
        lock (Gate)
        {
            foreach (IMidiInputDevice device in Devices)
            {
                if (device.Name?.IndexOf(
                        "through", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return device;
                }
            }

            return Devices.Count > 0 ? Devices[0] : null;
        }
    }

    /// <summary>Gets a device by name.</summary>
    /// <param name="name">The name, or a prefix of it.</param>
    /// <returns>The device, or null.</returns>
    public static IMidiInputDevice ByName(string name)
    {
        if (string.IsNullOrEmpty(name)) { return null; }

        lock (Gate)
        {
            foreach (IMidiInputDevice device in Devices)
            {
                if (device.Name != null
                    && device.Name.StartsWith(name, StringComparison.Ordinal))
                {
                    return device;
                }
            }
        }

        return null;
    }

    /// <summary>Announces a device.</summary>
    /// <param name="device">The device.</param>
    public static void Register(IMidiInputDevice device)
    {
        if (device == null) { return; }

        lock (Gate)
        {
            if (Devices.Contains(device)) { return; }

            Devices.Add(device);
        }

        DevicesChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Withdraws a device.</summary>
    /// <param name="device">The device.</param>
    public static void Unregister(IMidiInputDevice device)
    {
        bool removed;
        lock (Gate) { removed = Devices.Remove(device); }

        if (removed) { DevicesChanged?.Invoke(null, EventArgs.Empty); }
    }
}
