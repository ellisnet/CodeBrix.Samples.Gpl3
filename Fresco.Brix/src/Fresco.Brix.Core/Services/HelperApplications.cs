// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Fresco.Brix.Services; //was previously: frescobaldi/helpers.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Hands a file or a URL to the desktop — the user's own PDF viewer, file
/// manager, browser, mail client or terminal.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>helpers</c> module, whole: a per-type helper command the user
/// may configure, the shell-style split that reads it, the <c>$f</c>/<c>$u</c>
/// substitutions, and — when nothing is configured — the desktop's own idea of
/// what opens this kind of thing. The one substitution is the last step: Qt's
/// <c>QDesktopServices.openUrl</c> becomes the platform's
/// <c>Windows.System.Launcher</c>, whose Skia implementation is a shell-execute
/// start, so the six heads each get whatever their desktop does with the file.
/// </para>
/// <para>
/// ⚠ This is the ONE place in Fresco.Brix that starts another program, and it
/// does so only where upstream does: it is the user asking for their viewer,
/// not the application reaching for a tool. Nothing here ever runs an
/// engraver — that is in-process, permanently (FR2/FR5.1).
/// </para>
/// <para>
/// The helper commands are stored under <c>helper_applications/&lt;type&gt;</c>,
/// which is upstream's own settings key, so W12's preferences page has one
/// place to write and this has one place to read.
/// </para>
/// <para>
/// //was previously: the obvious name for a port of <c>helpers</c> is
/// <c>Helpers</c>. It cannot be — <c>Fresco.Brix.Helpers</c> is already a
/// NAMESPACE (the scaffold's host-builder provider lives in it), and a type
/// with a namespace's name is board trap 19 again. The name used instead is
/// upstream's own settings group, <c>helper_applications</c>.
/// </para>
/// </remarks>
public sealed class HelperApplications
{
    /// <summary>The settings group the per-type commands live in.</summary>
    public const string SettingsPrefix = "helper_applications/";

    /// <summary>Splits on whitespace, keeping double-quoted runs together.</summary>
    /// <remarks>Upstream's own expression, unchanged; the comment there says
    /// shlex could not be used because of unicode, and the pattern is what it
    /// used instead.</remarks>
    private static readonly Regex ShellWords
        = new Regex("[^\\s\"]*\"[^\"]*\"[^\\s\"]*|[^\\s\"]+", RegexOptions.Compiled);

    private readonly SettingsStore _settings;

    /// <summary>Creates the helper service.</summary>
    /// <param name="settings">The settings store the commands are read from,
    /// or null to always use the desktop's own handler.</param>
    public HelperApplications(SettingsStore settings = null) => _settings = settings;

    /// <summary>
    /// Gets or sets what to do when a configured helper cannot be started.
    /// </summary>
    /// <remarks>Upstream puts up a <c>QMessageBox.critical</c>; only the window
    /// can do that here, so it hands in the reporter.</remarks>
    public Action<string> ReportError { get; set; }

    /// <summary>
    /// Splits a command line the way the UNIX shell would: on whitespace, with
    /// double-quoted runs kept together and the quotes removed.
    /// </summary>
    /// <param name="commandLine">The command line.</param>
    /// <returns>The words.</returns>
    public static IReadOnlyList<string> ShellSplit(string commandLine)
    {
        List<string> words = new List<string>();
        if (string.IsNullOrEmpty(commandLine)) { return words; }

        foreach (Match match in ShellWords.Matches(commandLine))
        {
            words.Add(match.Value.Replace("\"", string.Empty));
        }

        return words;
    }

    /// <summary>
    /// Returns the helper command configured for a type, or null when the user
    /// has configured none.
    /// </summary>
    /// <param name="type">The helper type — <c>pdf</c>, <c>midi</c>,
    /// <c>image</c>, <c>browser</c>, <c>email</c>, <c>directory</c> or
    /// <c>shell</c>.</param>
    /// <returns>The command and its arguments, or null.</returns>
    public IReadOnlyList<string> Command(string type)
    {
        string command = _settings?.GetString(SettingsPrefix + type, string.Empty);
        if (string.IsNullOrEmpty(command)) { return null; }

        //An absolute path to something executable is the whole command: it is
        //allowed to contain the spaces a split would break it on.
        if (Path.IsPathRooted(command) && IsExecutable(command))
        {
            return new[] { command };
        }

        return ShellSplit(command);
    }

    /// <summary>
    /// Works out which helper type a URL wants, the way upstream does: a mail
    /// address is email, and a local file is judged by its extension.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <param name="type">The type the caller asked for.</param>
    /// <returns>The type to use.</returns>
    public static string TypeFor(Uri url, string type = "browser")
    {
        if (url == null) { return type; }

        if (string.Equals(url.Scheme, "mailto", StringComparison.OrdinalIgnoreCase))
        {
            return "email";
        }

        if (!string.Equals(type, "browser", StringComparison.Ordinal)) { return type; }

        string file = LocalFile(url);
        if (string.IsNullOrEmpty(file)) { return type; }

        if (Directory.Exists(file)) { return "directory"; }

        //Invariant-culture lowering: a Turkish locale must not turn ".MIDI"
        //into something this comparison misses (standing rule 7).
        return Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".pdf" => "pdf",
            ".png" or ".jpg" or ".jpeg" => "image",
            ".midi" or ".mid" => "midi",
            _ => type,
        };
    }

    /// <summary>Opens a local file with the helper for its kind.</summary>
    /// <param name="path">The file or directory.</param>
    /// <param name="type">The helper type, or null to work it out.</param>
    /// <returns>True when something was started.</returns>
    public Task<bool> OpenPathAsync(string path, string type = "browser")
        => string.IsNullOrEmpty(path)
            ? Task.FromResult(false)
            : OpenUrlAsync(new Uri(Path.GetFullPath(path)), type);

    /// <summary>Opens a URL with the helper for its kind.</summary>
    /// <param name="url">The URL.</param>
    /// <param name="type">The helper type; refined by <see cref="TypeFor"/>.</param>
    /// <returns>True when something was started.</returns>
    public async Task<bool> OpenUrlAsync(Uri url, string type = "browser")
    {
        if (url == null) { return false; }

        type = TypeFor(url, type);
        List<string> command = Command(type) is { } configured
            ? new List<string>(configured)
            : null;

        if (command == null || command.Count == 0)
        {
            //Nothing configured. Everything but a terminal goes to the
            //desktop's own handler; a terminal has no such handler, so
            //upstream falls through to the first of its own candidates.
            if (!string.Equals(type, "shell", StringComparison.Ordinal))
            {
                return await LaunchAsync(url).ConfigureAwait(false);
            }

            foreach (var candidate in TerminalCommands())
            {
                command = new List<string>(candidate);
                break;
            }

            if (command == null || command.Count == 0) { return false; }
        }

        string program = command[0];
        command.RemoveAt(0);

        string file = LocalFile(url);
        string workingDirectory = null;
        if (!string.IsNullOrEmpty(file))
        {
            //A terminal is opened IN the directory; everything else works
            //beside the file it is given.
            workingDirectory = string.Equals(type, "shell", StringComparison.Ordinal)
                ? file
                : Path.GetDirectoryName(file);
        }

        bool substituted = false;
        for (int i = 0; i < command.Count; i++)
        {
            if (command[i].Contains("$u", StringComparison.Ordinal))
            {
                command[i] = command[i].Replace("$u", url.ToString(), StringComparison.Ordinal);
                substituted = true;
            }
            else if (command[i].Contains("$f", StringComparison.Ordinal))
            {
                command[i] = command[i].Replace("$f", file ?? string.Empty, StringComparison.Ordinal);
                substituted = true;
            }
        }

        if (!substituted)
        {
            if (type is "browser" or "email")
            {
                command.Add(url.ToString());
            }
            else if (!string.Equals(type, "shell", StringComparison.Ordinal))
            {
                command.Add(file ?? url.ToString());
            }
        }

        try
        {
            ProcessStartInfo start = new ProcessStartInfo(program)
            {
                //The helper is started directly, exactly as upstream's
                //subprocess.Popen does: the words are already split, and
                //letting a shell re-split them would break a quoted path.
                UseShellExecute = false,
            };
            foreach (var argument in command) { start.ArgumentList.Add(argument); }

            if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
            {
                start.WorkingDirectory = workingDirectory;
            }

            using Process process = Process.Start(start);
            return process != null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException or PlatformNotSupportedException)
        {
            ReportError?.Invoke(I18n.Format(
                I18n.Get("Could not start {program}.\nPlease check path and permissions."),
                ("program", program)));
            return false;
        }
    }

    /// <summary>
    /// Yields commands that open a terminal window, best first. There is always
    /// at least one.
    /// </summary>
    /// <returns>The candidate commands.</returns>
    /// <remarks>
    /// Upstream's own list and its own order. The Linux arm probes
    /// <c>PATH</c> for each candidate before offering it, which is why the
    /// generic <c>xterm</c> comes last and unconditionally.
    /// </remarks>
    public static IEnumerable<IReadOnlyList<string>> TerminalCommands()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return new[] { "cmd.exe" };
            yield break;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return new[] { "open", "-a", "Terminal", "$f" };
            yield break;
        }

        string[][] candidates =
        {
            new[] { "lxterminal", "--working-directory=$f" },
            new[] { "xfce4-terminal", "--working-directory=$f" },
            new[] { "konsole", "--workdir", "$f" },
            new[] { "gnome-terminal", "--working-directory=$f" },
        };

        foreach (var candidate in candidates)
        {
            if (FindOnPath(candidate[0]) != null) { yield return candidate; }
        }

        yield return new[] { "xterm" };
    }

    /// <summary>Gets the local file a URL names, or null when it names none.</summary>
    /// <param name="url">The URL.</param>
    /// <returns>The path, or null.</returns>
    public static string LocalFile(Uri url)
        => url != null && url.IsAbsoluteUri && url.IsFile ? url.LocalPath : null;

    /// <summary>Hands a URL to the desktop's own handler.</summary>
    /// <param name="url">The URL.</param>
    /// <returns>True when the desktop took it.</returns>
    /// <remarks>
    /// The platform's <c>Launcher</c> is a shell-execute process start on the
    /// Skia heads, so this is <c>xdg-open</c> on Linux, <c>open</c> on macOS
    /// and <c>ShellExecute</c> on Windows — one call, six heads, whatever each
    /// desktop is configured to do. A head with no desktop behind it (the
    /// frame-buffer one) simply answers false, which is the honest result.
    /// </remarks>
    private static async Task<bool> LaunchAsync(Uri url)
    {
        try
        {
            return await Windows.System.Launcher.LaunchUriAsync(url);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or NotImplementedException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsExecutable(string path)
    {
        try
        {
            if (!File.Exists(path)) { return false; }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { return true; }

            return (File.GetUnixFileMode(path)
                & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute))
                != 0;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static string FindOnPath(string program)
    {
        string path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) { return null; }

        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(directory)) { continue; }

            string candidate = Path.Combine(directory, program);
            if (IsExecutable(candidate)) { return candidate; }
        }

        return null;
    }
}
