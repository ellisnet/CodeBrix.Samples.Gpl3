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
using System.IO.Pipes;
using System.Net.Sockets;
using System.Threading;

namespace Fresco.Brix.Services; //was previously: frescobaldi/remote/__init__.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Inter-process communication with an already running Fresco.Brix (decision
/// FD5): a second launch with files on its command line hands them to the
/// window that is already up and stops, so two processes never share one
/// settings store.
/// </summary>
/// <remarks>
/// <para>
/// Upstream uses Qt's <c>QLocalServer</c>/<c>QLocalSocket</c>, which is a
/// named pipe on Windows and a unix-domain socket everywhere else; the same
/// split is made here explicitly with <see cref="NamedPipeServerStream"/> and
/// <see cref="System.Net.Sockets.UnixDomainSocketEndPoint"/>.
/// </para>
/// <para>
/// The check happens in each head's <c>Program.Main</c>, immediately BEFORE
/// <c>App.CommandLinePaths</c> is set — before this process has opened the
/// settings store or built a host — which is where upstream's
/// <c>__main__.py</c> makes it too.
/// </para>
/// </remarks>
public static class RemoteInstance
{
    /// <summary>The setting that turns the whole mechanism off.</summary>
    /// <remarks>Upstream's own key, and its own default of <c>true</c>. It IS a
    /// reachable preference there — the General page's "Open Files in Running
    /// Instance" box — and it is one here too.</remarks>
    public const string AllowRemoteKey = "allow_remote";

    /// <summary>
    /// The environment variable naming the socket a running instance listens on.
    /// </summary>
    /// <remarks>//was previously: <c>FRESCOBALDI_SOCKET</c>. A child process
    /// started by the application reads it and reaches its parent without
    /// having to guess the name.</remarks>
    public const string SocketVariable = "FRESCO_BRIX_SOCKET";

    /// <summary>How long to wait for a connection, in milliseconds.</summary>
    /// <remarks>Upstream's <c>waitForConnected(5000)</c>. It is a CAP, not a
    /// delay: a socket that is not there fails at once.</remarks>
    public const int ConnectTimeoutMilliseconds = 5000;

    private static readonly object Gate = new object();
    private static RemoteServer _server;

    /// <summary>Gets the running server, or null when nothing is listening.</summary>
    public static RemoteServer Server
    {
        get { lock (Gate) { return _server; } }
    }

    /// <summary>Answers whether handing files over is enabled.</summary>
    /// <param name="settings">The settings store, or null for the default.</param>
    /// <returns>Whether it is enabled.</returns>
    public static bool Enabled(SettingsStore settings)
        => settings?.GetBool(AllowRemoteKey, true) ?? true;

    /// <summary>
    /// Yields at most <paramref name="count"/> names to try for the socket.
    /// </summary>
    /// <param name="count">How many.</param>
    /// <returns>The names.</returns>
    /// <remarks>Upstream's <c>ids()</c>: the plain name, then the same name
    /// with <c>#1</c>, <c>#2</c> after it.</remarks>
    public static IEnumerable<string> Ids(int count = 3)
    {
        string id = GenerateId();
        yield return id;
        for (int c = 1; c < count; c++)
        {
            yield return id + "#" + c.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Makes the socket name: unique to the application, the user, and the X11
    /// display.
    /// </summary>
    /// <returns>The name.</returns>
    /// <remarks>
    /// Upstream's <c>generate_id()</c>, verbatim in its parts. A session with
    /// no <c>DISPLAY</c> — a pure Wayland session, or the frame-buffer head —
    /// falls into upstream's own "no display" branch and is named by the user
    /// alone, which is still unique per user.
    /// </remarks>
    public static string GenerateId()
    {
        List<string> parts = new List<string> { AppInfo.Name };

        //Upstream guards os.getuid() with AttributeError, because Windows has
        //no such call; the same platform test says the same thing here.
        if (!OperatingSystem.IsWindows())
        {
            parts.Add(Environment.UserName);
        }

        string display = Environment.GetEnvironmentVariable("DISPLAY");
        if (!string.IsNullOrEmpty(display))
        {
            parts.Add(display.Replace(':', '_').Replace('/', '_'));
        }

        return string.Join("-", parts);
    }

    /// <summary>
    /// Connects to a running instance, or answers null when there is none.
    /// </summary>
    /// <returns>The connection, or null.</returns>
    /// <remarks>Upstream's <c>get()</c>: the environment variable wins when it
    /// is set, otherwise every generated name is tried in turn.</remarks>
    public static RemoteConnection Connect()
    {
        string named = Environment.GetEnvironmentVariable(SocketVariable);
        IEnumerable<string> names = string.IsNullOrEmpty(named)
            ? Ids()
            : new[] { named };

        foreach (var name in names)
        {
            Stream stream = RemoteTransport.TryConnect(name, ConnectTimeoutMilliseconds);
            if (stream != null) { return new RemoteConnection(stream); }
        }

        return null;
    }

    /// <summary>
    /// Hands this process's command line to a running instance, answering
    /// whether it did — in which case the caller must stop.
    /// </summary>
    /// <param name="arguments">The command line, without the program name.</param>
    /// <returns>Whether the files went to another process.</returns>
    /// <remarks>
    /// Upstream's <c>if not args.new and remote.enabled(): api = remote.get()
    /// … sys.exit(0)</c>, in that order: <c>--new</c> is checked first, then
    /// the preference, and only then is a connection attempted.
    /// </remarks>
    public static bool TryHandOff(IReadOnlyList<string> arguments)
    {
        CommandLineArguments parsed = CommandLineArguments.Parse(arguments);
        if (parsed.New) { return false; }

        if (!ReadEnabledSetting()) { return false; }

        RemoteConnection connection = Connect();
        if (connection == null) { return false; }

        try
        {
            connection.CommandLine(parsed);
            connection.Close();
            return true;
        }
        catch (IOException)
        {
            //The running instance went away between connecting and writing;
            //carry on and start normally.
            connection.Dispose();
            return false;
        }
    }

    /// <summary>Starts listening for incoming connections.</summary>
    /// <param name="target">What the commands act on.</param>
    /// <param name="toUiThread">How to get onto the window's thread.</param>
    /// <remarks>Upstream's <c>init()</c>, including its stale-socket recovery:
    /// when every name is taken, each is CONTACTED, and one that answers
    /// nothing is removed and taken over.</remarks>
    public static void Init(IRemoteCommandTarget target, Action<Action> toUiThread = null)
    {
        if (target == null) { throw new ArgumentNullException(nameof(target)); }

        lock (Gate)
        {
            if (_server != null) { return; }

            IRemoteListener listener = null;
            foreach (var name in Ids())
            {
                listener = RemoteTransport.TryListen(name);
                if (listener != null) { break; }
            }

            if (listener == null)
            {
                //Every name failed. One of them may be a socket left behind by
                //a process that died; a name nobody answers on is stale.
                foreach (var name in Ids())
                {
                    Stream probe = RemoteTransport.TryConnect(name, ConnectTimeoutMilliseconds);
                    if (probe != null)
                    {
                        probe.Dispose();
                        continue;
                    }

                    RemoteTransport.RemoveServer(name);
                    listener = RemoteTransport.TryListen(name);
                    if (listener != null) { break; }
                }
            }

            //No names left: do not listen. The application still runs; it just
            //cannot be handed files by a later launch.
            if (listener == null) { return; }

            _server = new RemoteServer(listener, target, toUiThread);
            Environment.SetEnvironmentVariable(SocketVariable, listener.Id);
            _server.Start();
        }
    }

    /// <summary>Stops listening for incoming connections.</summary>
    /// <remarks>Upstream's <c>quit()</c>.</remarks>
    public static void Quit()
    {
        lock (Gate)
        {
            if (_server == null) { return; }

            _server.Dispose();
            _server = null;
            Environment.SetEnvironmentVariable(SocketVariable, null);
        }
    }

    /// <summary>Starts or stops listening according to the settings.</summary>
    /// <param name="settings">The settings store.</param>
    /// <param name="target">What the commands act on.</param>
    /// <param name="toUiThread">How to get onto the window's thread.</param>
    /// <remarks>Upstream's <c>setup()</c>, which it connects to
    /// <c>app.settingsChanged</c> — so does the window here, which is what
    /// makes the preference take effect without a restart.</remarks>
    public static void Setup(
        SettingsStore settings,
        IRemoteCommandTarget target,
        Action<Action> toUiThread = null)
    {
        if (Enabled(settings))
        {
            Init(target, toUiThread);
        }
        else
        {
            Quit();
        }
    }

    private static bool ReadEnabledSetting()
    {
        //The store is opened for one boolean and closed again, before the
        //application has taken it for itself. A store that cannot be opened —
        //busy, missing, unreadable — must not stop the application starting,
        //so upstream's default answers instead.
        try
        {
            using SettingsStore settings = new SettingsStore();
            return Enabled(settings);
        }
        catch (Exception)
        {
            return true;
        }
    }
}

/// <summary>
/// The listening half: accepts one connection at a time and performs what it
/// asks.
/// </summary>
public sealed class RemoteServer : IDisposable
{
    private readonly IRemoteListener _listener;
    private readonly IRemoteCommandTarget _target;
    private readonly Action<Action> _toUiThread;
    private readonly CancellationTokenSource _stopping = new CancellationTokenSource();

    private Thread _thread;

    /// <summary>Creates the server.</summary>
    /// <param name="listener">The bound listener.</param>
    /// <param name="target">What the commands act on.</param>
    /// <param name="toUiThread">How to get onto the window's thread.</param>
    internal RemoteServer(
        IRemoteListener listener,
        IRemoteCommandTarget target,
        Action<Action> toUiThread)
    {
        _listener = listener;
        _target = target;
        _toUiThread = toUiThread;
    }

    /// <summary>Gets the name being listened on.</summary>
    public string Id => _listener.Id;

    /// <summary>Begins accepting connections.</summary>
    public void Start()
    {
        _thread = new Thread(Loop)
        {
            //A background thread so a stuck accept can never keep the process
            //alive after the window has gone.
            IsBackground = true,
            Name = "Fresco.Brix remote",
        };
        _thread.Start();
    }

    /// <summary>Stops accepting connections and releases the socket.</summary>
    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Dispose();
        _stopping.Dispose();
    }

    private void Loop()
    {
        while (!_stopping.IsCancellationRequested)
        {
            Stream stream;
            try
            {
                stream = _listener.Accept(_stopping.Token);
            }
            catch (Exception)
            {
                //The listener was closed, or the platform refused; either way
                //there is nothing more to accept.
                return;
            }

            if (stream == null) { return; }

            try
            {
                new IncomingCommands(_target, _toUiThread).Read(stream);
            }
            catch (IOException)
            {
                //A sender that vanished mid-line is not an error worth
                //reporting; the next connection is what matters.
            }
            finally
            {
                stream.Dispose();
            }
        }
    }
}

/// <summary>A bound listener that hands over one connected stream at a time.</summary>
internal interface IRemoteListener : IDisposable
{
    /// <summary>Gets the name being listened on.</summary>
    string Id { get; }

    /// <summary>Waits for the next connection.</summary>
    /// <param name="token">Cancelled when the server is stopping.</param>
    /// <returns>The connected stream, or null when stopping.</returns>
    Stream Accept(CancellationToken token);
}

/// <summary>
/// The platform half of FD5: a named pipe on Windows, a unix-domain socket
/// everywhere else.
/// </summary>
internal static class RemoteTransport
{
    /// <summary>Answers the file a unix-domain socket lives in.</summary>
    /// <param name="id">The socket name.</param>
    /// <returns>The path.</returns>
    /// <remarks>Qt puts its local sockets in the temporary directory, and so
    /// does this — which also keeps the path well inside the 107-byte limit a
    /// unix-domain address has.</remarks>
    internal static string SocketPath(string id)
        => Path.Combine(Path.GetTempPath(), id + ".socket");

    /// <summary>Connects to a listening instance, or answers null.</summary>
    /// <param name="id">The socket name.</param>
    /// <param name="timeoutMilliseconds">How long to wait at most.</param>
    /// <returns>The connected stream, or null.</returns>
    internal static Stream TryConnect(string id, int timeoutMilliseconds)
    {
        if (string.IsNullOrEmpty(id)) { return null; }

        if (OperatingSystem.IsWindows()) { return TryConnectPipe(id, timeoutMilliseconds); }

        string path = SocketPath(id);
        if (!File.Exists(path)) { return null; }

        Socket socket = new Socket(
            AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Connect(new UnixDomainSocketEndPoint(path));
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (SocketException)
        {
            socket.Dispose();
            return null;
        }
        catch (IOException)
        {
            socket.Dispose();
            return null;
        }
    }

    /// <summary>Binds a listener to a name, or answers null when it is taken.</summary>
    /// <param name="id">The socket name.</param>
    /// <returns>The listener, or null.</returns>
    internal static IRemoteListener TryListen(string id)
    {
        if (OperatingSystem.IsWindows()) { return PipeListener.TryCreate(id); }

        return UnixSocketListener.TryCreate(id);
    }

    /// <summary>Removes a socket left behind by a dead process.</summary>
    /// <param name="id">The socket name.</param>
    /// <remarks>Upstream's <c>QLocalServer.removeServer</c>. A named pipe
    /// disappears with the process that made it, so there is nothing to remove
    /// on Windows.</remarks>
    internal static void RemoveServer(string id)
    {
        if (OperatingSystem.IsWindows()) { return; }

        try { File.Delete(SocketPath(id)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static Stream TryConnectPipe(string id, int timeoutMilliseconds)
    {
        //A pipe that does not exist would otherwise be POLLED for the whole
        //timeout, which would make every cold start pay for it three times.
        if (!File.Exists(@"\\.\pipe\" + id)) { return null; }

        NamedPipeClientStream client = new NamedPipeClientStream(
            ".", id, PipeDirection.Out);
        try
        {
            client.Connect(timeoutMilliseconds);
            return client;
        }
        catch (Exception)
        {
            client.Dispose();
            return null;
        }
    }

    private sealed class UnixSocketListener : IRemoteListener
    {
        private readonly Socket _socket;
        private readonly string _path;

        private UnixSocketListener(string id, string path, Socket socket)
        {
            Id = id;
            _path = path;
            _socket = socket;
        }

        public string Id { get; }

        internal static UnixSocketListener TryCreate(string id)
        {
            string path = RemoteTransport.SocketPath(id);
            Socket socket = new Socket(
                AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                socket.Bind(new UnixDomainSocketEndPoint(path));
                socket.Listen(8);
                return new UnixSocketListener(id, path, socket);
            }
            catch (SocketException)
            {
                //The name is taken — by a live instance, or by a socket a dead
                //one left behind. The caller sorts out which.
                socket.Dispose();
                return null;
            }
            catch (IOException)
            {
                socket.Dispose();
                return null;
            }
        }

        public Stream Accept(CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; }

            Socket accepted = _socket.Accept();
            return new NetworkStream(accepted, ownsSocket: true);
        }

        public void Dispose()
        {
            try { _socket.Dispose(); }
            catch (SocketException) { }

            try { File.Delete(_path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class PipeListener : IRemoteListener
    {
        private NamedPipeServerStream _pipe;
        private bool _disposed;

        private PipeListener(string id, NamedPipeServerStream pipe)
        {
            Id = id;
            _pipe = pipe;
        }

        public string Id { get; }

        internal static PipeListener TryCreate(string id)
        {
            NamedPipeServerStream pipe = Create(id);
            return pipe == null ? null : new PipeListener(id, pipe);
        }

        public Stream Accept(CancellationToken token)
        {
            if (token.IsCancellationRequested) { return null; }

            NamedPipeServerStream pipe = _pipe;
            if (pipe == null) { return null; }

            pipe.WaitForConnection();

            //⚠ One instance at a time is what makes the NAME exclusive, the
            //way binding a unix socket is: a second process asking for the
            //same name is refused. The cost is that the next instance cannot
            //exist until this one is finished with, so the caller reads the
            //connection through and disposes it before asking again — which is
            //what RemoteServer.Loop does.
            _pipe = null;
            return new PipeConnection(this, pipe);
        }

        public void Dispose()
        {
            _disposed = true;
            NamedPipeServerStream pipe = _pipe;
            _pipe = null;
            pipe?.Dispose();
        }

        private static NamedPipeServerStream Create(string id)
        {
            try
            {
                return new NamedPipeServerStream(
                    id, PipeDirection.In, 1, PipeTransmissionMode.Byte);
            }
            catch (IOException)
            {
                //The name is taken by a live instance.
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// The accepted pipe, which makes the next server instance when it is
        /// disposed.
        /// </summary>
        private sealed class PipeConnection : Stream
        {
            private readonly PipeListener _owner;
            private readonly NamedPipeServerStream _pipe;

            internal PipeConnection(PipeListener owner, NamedPipeServerStream pipe)
            {
                _owner = owner;
                _pipe = pipe;
            }

            public override bool CanRead => _pipe.CanRead;

            public override bool CanSeek => false;

            public override bool CanWrite => _pipe.CanWrite;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() => _pipe.Flush();

            public override int Read(byte[] buffer, int offset, int count)
                => _pipe.Read(buffer, offset, count);

            public override long Seek(long offset, SeekOrigin origin)
                => throw new NotSupportedException();

            public override void SetLength(long value)
                => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
                => _pipe.Write(buffer, offset, count);

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _pipe.Dispose();
                    if (!_owner._disposed) { _owner._pipe ??= Create(_owner.Id); }
                }

                base.Dispose(disposing);
            }
        }
    }
}
