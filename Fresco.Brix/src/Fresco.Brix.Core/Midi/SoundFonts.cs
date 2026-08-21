// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fresco.Brix.Midi;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Where the instrument the MIDI player sounds through comes from: the bank
/// shipped in the box, or one the user has pointed at.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ THIS HAS NO UPSTREAM. Frescobaldi bundles no soundfont because it does not
/// synthesize: it sends MIDI to an external port and its
/// <c>userguide/midi_synth.md</c> tells the reader to go and run TiMidity or
/// FluidSynth. Ruling FR6 replaced that whole mechanism with in-process
/// synthesis, so the bank is ours to supply — board decision FD2, which chose
/// GeneralUser GS v2.0.3 and vendored it, with its licence, under
/// <c>assets/soundfonts</c>.
/// </para>
/// <para>
/// The bank is a DEFAULT, not a dependency: empty the folder and the
/// application still runs, offering whatever file the user picks. That is what
/// keeps the vendored data an aggregate rather than part of the program.
/// </para>
/// </remarks>
public static class SoundFonts
{
    /// <summary>The setting holding the instrument file the user chose.</summary>
    public const string InstrumentSettingKey = "midi/soundfont";

    /// <summary>The file name of the bank shipped in the box.</summary>
    public const string BundledFileName = "GeneralUser-GS.sf2";

    /// <summary>
    /// The name to SHOW for the bundled bank.
    /// </summary>
    /// <remarks>
    /// ⚠ Board trap 51: the file's own <c>INAM</c> chunk reads "GeneralUser GS
    /// 2.0.3 BETA" and its <c>ICRD</c> reads 2024-10-15, both left over from
    /// v2.0.1 — upstream's changelog dates v2.0.3 to 2026-02-22 as an ordinary
    /// release. Show <see cref="SoundFontInfo.BankName"/> anywhere and the app
    /// tells users they are running a beta. The application knows what it
    /// shipped; it says so itself.
    /// </remarks>
    public const string BundledDisplayName = "GeneralUser GS 2.0.3";

    /// <summary>Gets the folder the shipped banks are in.</summary>
    public static string BundledDirectory
        => Path.Combine(AppContext.BaseDirectory, "assets", "soundfonts");

    /// <summary>Gets the shipped bank's path, whether or not it is there.</summary>
    public static string BundledPath
        => Path.Combine(BundledDirectory, BundledFileName);

    /// <summary>The extensions an instrument file may have.</summary>
    public static IReadOnlyList<string> Extensions { get; }
        = new[] { ".sf2", ".sf3", ".sfz" };

    /// <summary>
    /// Gets the instrument to play through: the user's choice when it is still
    /// there, the shipped bank otherwise, and null when there is neither.
    /// </summary>
    /// <param name="settings">The settings store, or null.</param>
    /// <returns>The path, or null.</returns>
    public static string Resolve(SettingsStore settings = null)
    {
        string chosen = settings?.GetString(InstrumentSettingKey);
        if (!string.IsNullOrEmpty(chosen) && SafelyExists(chosen)) { return chosen; }

        return SafelyExists(BundledPath) ? BundledPath : null;
    }

    /// <summary>Gets the name to show for an instrument file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The name.</returns>
    /// <remarks>Never the bank's own <c>INAM</c> string — see trap 51 on
    /// <see cref="BundledDisplayName"/>. A file the user brought is named by
    /// its file name, which is what they picked it by.</remarks>
    public static string DisplayName(string path)
    {
        if (string.IsNullOrEmpty(path)) { return string.Empty; }

        return string.Equals(
                Path.GetFullPath(path), Path.GetFullPath(BundledPath),
                StringComparison.Ordinal)
            ? BundledDisplayName
            : Path.GetFileName(path);
    }

    /// <summary>Answers whether a path names an instrument file we can read.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Whether the extension is one of ours.</returns>
    public static bool IsInstrument(string path)
        => !string.IsNullOrEmpty(path)
            && Extensions.Contains(
                Path.GetExtension(path).ToLowerInvariant(), StringComparer.Ordinal);

    private static bool SafelyExists(string path)
    {
        try { return File.Exists(path); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
