// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.Sfz;
using Fresco.Brix.Midi;
using Fresco.Brix.Services;
using System;
using System.IO;

namespace Fresco.Brix.Export; //was previously: frescobaldi/file_export/__init__.py (exportAudio)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Rendering an engraved <c>.midi</c> file to a sound file.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>FileExport.exportAudio</c>, and it is a REPLACE rather than a
/// port. There the whole feature is a subprocess — <c>timidity midifile -Ow -o
/// wavfile</c> — run through <c>externalcommand.ExternalCommandDialog</c> so
/// the user can watch it scroll past; a machine without TiMidity installed
/// simply cannot export audio, and the dialog is where they find that out.
/// </para>
/// <para>
/// Fresco.Brix already synthesizes in-process, through the very bank the MIDI
/// panel plays with (decision FD2), so the export is a function call: no
/// process, no dependency on the machine, and the file sounds like what the
/// user just heard. <c>externalcommand.py</c> is DROPPED whole for exactly
/// this reason (board §6.1), and this is the last thing that would have
/// needed it.
/// </para>
/// </remarks>
public static class AudioExport
{
    /// <summary>The sample rate rendered at, in hertz.</summary>
    public const int SampleRate = 44100;

    /// <summary>
    /// How long to go on rendering after the last note, so release tails and
    /// reverb decay away instead of being cut off.
    /// </summary>
    /// <remarks>
    /// The same lesson board trap 52 records for playback: a score ending on a
    /// held chord ends AFTER its last event, and stopping at the event list's
    /// end truncates it.
    /// </remarks>
    public static readonly TimeSpan Tail = TimeSpan.FromSeconds(3);

    /// <summary>Renders a MIDI file to a WAV file.</summary>
    /// <param name="midiPath">The MIDI file to render.</param>
    /// <param name="wavPath">The WAV file to write; overwritten if it exists.</param>
    /// <param name="settings">Where the chosen instrument is recorded, or null.</param>
    /// <returns>What happened.</returns>
    /// <exception cref="ArgumentNullException">No MIDI path or no WAV path.</exception>
    public static AudioExportResult Render(
        string midiPath, string wavPath, SettingsStore settings = null)
    {
        if (midiPath == null) { throw new ArgumentNullException(nameof(midiPath)); }

        if (wavPath == null) { throw new ArgumentNullException(nameof(wavPath)); }

        string instrument = SoundFonts.Resolve(settings);
        if (instrument == null)
        {
            //FD2's bank is a default and not a dependency: with the assets
            //folder emptied there is nothing to sound through, and saying so is
            //the whole of the failure.
            return AudioExportResult.Failed(I18n.Get("No instrument found!"));
        }

        try
        {
            var sequence = new MidiSequence(midiPath);

            //Loaded through a cache of its own rather than the player's: an
            //export is not playback, it may run while the panel is sounding,
            //and a 32-megabyte bank read twice is still cheaper than the two
            //sharing state across threads.
            if (string.Equals(
                Path.GetExtension(instrument), ".sfz", StringComparison.OrdinalIgnoreCase))
            {
                using var cache = new SfzInstrumentCache();
                SoundFontRenderer.RenderToWavFile(
                    cache.Get(instrument), sequence, wavPath, SampleRate, Tail);
            }
            else
            {
                using var cache = new SoundFontCache();
                SoundFontRenderer.RenderToWavFile(
                    cache.Get(instrument), sequence, wavPath, SampleRate, Tail);
            }

            return AudioExportResult.Succeeded(wavPath, instrument);
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
            //An export that failed halfway leaves a WAV nobody can play, and a
            //user who then double-clicks it learns nothing.
            TryDelete(wavPath);
            return AudioExportResult.Failed(exception.Message);
        }
    }

    /// <summary>Returns the name to suggest exporting a document's audio under.</summary>
    /// <param name="documentPath">The document's path, or null.</param>
    /// <returns>The suggested name.</returns>
    public static string SuggestedName(string documentPath)
        => string.IsNullOrEmpty(documentPath)
            ? "document.wav"
            : Path.ChangeExtension(documentPath, ".wav");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>What an audio export did.</summary>
public sealed class AudioExportResult
{
    private AudioExportResult(bool ok, string path, string instrument, string error)
    {
        Ok = ok;
        Path = path;
        Instrument = instrument;
        Error = error;
    }

    /// <summary>Gets whether the file was written.</summary>
    public bool Ok { get; }

    /// <summary>Gets the file that was written, or null.</summary>
    public string Path { get; }

    /// <summary>Gets the instrument it was rendered with, or null.</summary>
    public string Instrument { get; }

    /// <summary>Gets what went wrong, or null.</summary>
    public string Error { get; }

    /// <summary>Returns a successful result.</summary>
    /// <param name="path">The file written.</param>
    /// <param name="instrument">What it was rendered with.</param>
    /// <returns>The result.</returns>
    public static AudioExportResult Succeeded(string path, string instrument)
        => new AudioExportResult(true, path, instrument, null);

    /// <summary>Returns a failed result.</summary>
    /// <param name="error">What went wrong.</param>
    /// <returns>The result.</returns>
    public static AudioExportResult Failed(string error)
        => new AudioExportResult(false, null, null, error);
}
