// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Fresco.Brix.Services; //was previously: frescobaldi/remote/api.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// What a running instance can be asked to do down the socket.
/// </summary>
/// <remarks>
/// Upstream's <c>Incoming.command</c> reaches straight into the active
/// <c>MainWindow</c>; there is no module-level active window here, so the
/// window registers itself as one of these instead.
/// </remarks>
public interface IRemoteCommandTarget
{
    /// <summary>Opens a file in a tab.</summary>
    /// <param name="path">The file.</param>
    /// <param name="encoding">The encoding name, or null to detect it.</param>
    void OpenPath(string path, string encoding);

    /// <summary>Raises the tab already showing a file.</summary>
    /// <param name="path">The file.</param>
    void SetCurrent(string path);

    /// <summary>Puts the caret at a place in the current document.</summary>
    /// <param name="line">The line, counted from 1.</param>
    /// <param name="column">The column, counted from 1.</param>
    void SetCursor(int line, int column);

    /// <summary>Brings the window to the front.</summary>
    void ActivateWindow();
}

/// <summary>
/// The commands of the inter-process protocol: one line of ASCII each,
/// terminated by a newline, arguments separated by spaces.
/// </summary>
/// <remarks>
/// Upstream's own vocabulary, kept verbatim so the shape of the conversation
/// is the one <c>remote/api.py</c> documents at the top of the file.
/// </remarks>
public static class RemoteCommands
{
    /// <summary>Open the file whose escaped path follows.</summary>
    public const string Open = "open";

    /// <summary>Read the files that follow in the named encoding.</summary>
    public const string Encoding = "encoding";

    /// <summary>Bring the window to the front.</summary>
    public const string ActivateWindow = "activate_window";

    /// <summary>Raise the tab showing the file whose escaped path follows.</summary>
    public const string SetCurrent = "set_current";

    /// <summary>Put the caret at the line and column that follow.</summary>
    public const string SetCursor = "set_cursor";

    /// <summary>The sender has finished.</summary>
    public const string Bye = "bye";
}

/// <summary>
/// Speaks to a running Fresco.Brix over an already-connected stream.
/// </summary>
/// <remarks>Upstream's <c>api.Remote</c>.</remarks>
public sealed class RemoteConnection : IDisposable
{
    private readonly Stream _stream;

    /// <summary>Creates the connection over a stream.</summary>
    /// <param name="stream">The connected stream; this object owns it.</param>
    public RemoteConnection(Stream stream)
        => _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    /// <summary>
    /// Writes a command line's worth of instructions, in upstream's own order.
    /// </summary>
    /// <param name="arguments">What this process was started with.</param>
    /// <remarks>
    /// //was previously: <c>u.toEncoded()</c>, a percent-encoded URL. This
    /// application opens local files only, so the argument is the percent-
    /// encoded PATH — which keeps upstream's property that a command is one
    /// line of ASCII with no spaces in it.
    /// </remarks>
    public void CommandLine(CommandLineArguments arguments)
    {
        if (arguments == null) { throw new ArgumentNullException(nameof(arguments)); }

        IReadOnlyList<string> files = arguments.Files;
        if (files.Count > 0)
        {
            if (!string.IsNullOrEmpty(arguments.Encoding))
            {
                Write(RemoteCommands.Encoding + " " + arguments.Encoding + "\n");
            }

            string last = null;
            foreach (var file in files)
            {
                last = Escape(file);
                Write(RemoteCommands.Open + " " + last + "\n");
            }

            //Upstream names the LAST url as the one to make current — the loop
            //variable, left where the loop finished.
            Write(RemoteCommands.SetCurrent + " " + last + "\n");

            if (arguments.Line != null)
            {
                Write(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} {1} {2}\n",
                    RemoteCommands.SetCursor,
                    arguments.Line.Value,
                    arguments.Column ?? 1));
            }
        }

        Write(RemoteCommands.ActivateWindow + "\n");
    }

    /// <summary>Says goodbye and disconnects.</summary>
    public void Close()
    {
        try
        {
            Write(RemoteCommands.Bye + "\n");
            _stream.Flush();
        }
        catch (IOException)
        {
            //The other end went away while we were talking to it; there is
            //nothing left to say.
        }

        _stream.Dispose();
    }

    /// <summary>Closes the connection.</summary>
    public void Dispose() => _stream.Dispose();

    /// <summary>Writes one line of the protocol.</summary>
    /// <param name="line">The line, newline included.</param>
    public void Write(string line)
    {
        byte[] data = Encoding.UTF8.GetBytes(line ?? string.Empty);
        _stream.Write(data, 0, data.Length);
        _stream.Flush();
    }

    /// <summary>Escapes a path into one space-free protocol argument.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The escaped path.</returns>
    public static string Escape(string path)
        => Uri.EscapeDataString(System.IO.Path.GetFullPath(path ?? string.Empty));

    /// <summary>Reads a path back out of a protocol argument.</summary>
    /// <param name="argument">The escaped path.</param>
    /// <returns>The path.</returns>
    public static string Unescape(string argument)
        => Uri.UnescapeDataString(argument ?? string.Empty);
}

/// <summary>
/// Reads the protocol off one incoming connection and performs what it asks.
/// </summary>
/// <remarks>
/// Upstream's <c>api.Incoming</c>, which is fed by Qt's <c>readyRead</c>. Here
/// the socket is read on a worker thread and every command is handed to the
/// window on ITS thread, which is board trap 22's rule.
/// </remarks>
public sealed class IncomingCommands
{
    private readonly IRemoteCommandTarget _target;
    private readonly Action<Action> _toUiThread;
    private string _encoding;

    /// <summary>Creates the reader.</summary>
    /// <param name="target">What the commands act on.</param>
    /// <param name="toUiThread">How to get onto the window's thread; null runs
    /// the command where it was read, which is what the tests want.</param>
    public IncomingCommands(IRemoteCommandTarget target, Action<Action> toUiThread = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _toUiThread = toUiThread;
    }

    /// <summary>Gets whether the sender has said goodbye.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>Performs one command line.</summary>
    /// <param name="line">The line, without its newline.</param>
    public void Command(string line)
    {
        string[] parts = (line ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { return; }

        string command = parts[0];
        switch (command)
        {
            case RemoteCommands.Open:
                if (parts.Length > 1)
                {
                    string path = RemoteConnection.Unescape(parts[1]);
                    string encoding = _encoding;
                    Post(() => _target.OpenPath(path, encoding));
                }

                break;

            case RemoteCommands.Encoding:
                //Upstream keeps the encoding on the handler and applies it to
                //every later `open'; the reader is the one holding the state.
                if (parts.Length > 1) { _encoding = parts[1]; }

                break;

            case RemoteCommands.ActivateWindow:
                Post(() => _target.ActivateWindow());
                break;

            case RemoteCommands.SetCurrent:
                if (parts.Length > 1)
                {
                    string current = RemoteConnection.Unescape(parts[1]);
                    Post(() => _target.SetCurrent(current));
                }

                break;

            case RemoteCommands.SetCursor:
                if (parts.Length > 2
                    && int.TryParse(parts[1], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int cursorLine)
                    && int.TryParse(parts[2], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int cursorColumn))
                {
                    Post(() => _target.SetCursor(cursorLine, cursorColumn));
                }

                break;

            case RemoteCommands.Bye:
                IsFinished = true;
                break;
        }
    }

    /// <summary>
    /// Reads a whole connection, performing each complete line as it arrives.
    /// </summary>
    /// <param name="stream">The connected stream.</param>
    /// <remarks>Upstream's <c>read()</c>: the buffer keeps a partial line for
    /// the next read rather than performing it half-written.</remarks>
    public void Read(Stream stream)
    {
        if (stream == null) { throw new ArgumentNullException(nameof(stream)); }

        StringBuilder pending = new StringBuilder();
        byte[] buffer = new byte[1024];
        int read;
        while (!IsFinished && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            pending.Append(Encoding.UTF8.GetString(buffer, 0, read));
            string text = pending.ToString();
            int end = 0;
            int position;
            while ((position = text.IndexOf('\n', end)) >= 0)
            {
                Command(text.Substring(end, position - end));
                end = position + 1;
                if (IsFinished) { break; }
            }

            pending.Clear();
            if (end < text.Length) { pending.Append(text, end, text.Length - end); }
        }
    }

    private void Post(Action work)
    {
        if (_toUiThread == null)
        {
            work();
            return;
        }

        _toUiThread(work);
    }
}
