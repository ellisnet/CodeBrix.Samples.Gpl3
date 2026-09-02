// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// FD5's single-instance machinery against upstream's own
/// (<c>remote/__init__.py</c> and <c>remote/api.py</c>): the socket names, the
/// preference that gates it, the line protocol both ends speak, and one whole
/// conversation over a real socket.
/// </summary>
public class RemoteInstanceTests : IDisposable
{
    private readonly string _directory;
    private readonly SettingsStore _settings;

    /// <summary>Creates the fixture over a scratch directory and store.</summary>
    public RemoteInstanceTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "frescobrix-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _settings = new SettingsStore(_directory);
    }

    /// <summary>Closes the store and removes the scratch directory.</summary>
    public void Dispose()
    {
        _settings.Dispose();
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ids_are_the_name_then_the_name_with_a_hash_number()
    {
        //Arrange
        string id = RemoteInstance.GenerateId();

        //Act
        List<string> ids = RemoteInstance.Ids().ToList();

        //Assert
        ids.Should().HaveCount(3);
        ids[0].Should().Be(id);
        ids[1].Should().Be(id + "#1");
        ids[2].Should().Be(id + "#2");
    }

    [Fact]
    public void the_id_names_the_application_and_the_user()
    {
        //Act
        string id = RemoteInstance.GenerateId();

        //Assert
        id.Should().StartWith(AppInfo.Name);
        if (!OperatingSystem.IsWindows())
        {
            id.Should().Contain(Environment.UserName);
        }
    }

    [Fact]
    public void the_display_is_part_of_the_id_with_its_punctuation_replaced()
    {
        //Arrange
        string previous = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", ":1.0");

        try
        {
            //Act
            string id = RemoteInstance.GenerateId();

            //Assert
            id.Should().EndWith("-_1.0");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", previous);
        }
    }

    [Fact]
    public void a_session_with_no_display_still_gets_an_id()
    {
        //Arrange — a pure Wayland session, or the frame-buffer head: upstream's
        //own "no DISPLAY" branch.
        string previous = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", null);

        try
        {
            //Act
            string id = RemoteInstance.GenerateId();

            //Assert
            id.Should().NotBeNullOrEmpty();
            id.Should().StartWith(AppInfo.Name);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", previous);
        }
    }

    [Fact]
    public void handing_files_over_is_enabled_by_default()
    {
        //Assert — upstream's QSettings().value('allow_remote', True, bool).
        RemoteInstance.Enabled(null).Should().BeTrue();
        RemoteInstance.Enabled(_settings).Should().BeTrue();
    }

    [Fact]
    public void the_preference_turns_handing_files_over_off()
    {
        //Act
        _settings.SetBool(RemoteInstance.AllowRemoteKey, false);

        //Assert
        RemoteInstance.Enabled(_settings).Should().BeFalse();
    }

    [Fact]
    public void a_command_line_is_written_in_upstreams_own_order()
    {
        //Arrange
        MemoryStream written = new MemoryStream();
        RemoteConnection connection = new RemoteConnection(written);
        CommandLineArguments arguments = CommandLineArguments.Parse(
            new[] { "-e", "utf-8", "-l", "12", "-c", "5", "one.ly", "two.ly" });

        //Act
        connection.CommandLine(arguments);

        //Assert
        string[] lines = Encoding.UTF8.GetString(written.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("encoding utf-8");
        lines[1].Should().Be("open " + RemoteConnection.Escape("one.ly"));
        lines[2].Should().Be("open " + RemoteConnection.Escape("two.ly"));
        lines[3].Should().Be("set_current " + RemoteConnection.Escape("two.ly"));
        lines[4].Should().Be("set_cursor 12 5");
        lines[5].Should().Be("activate_window");
    }

    [Fact]
    public void a_command_line_with_no_files_only_raises_the_window()
    {
        //Arrange
        MemoryStream written = new MemoryStream();
        RemoteConnection connection = new RemoteConnection(written);

        //Act
        connection.CommandLine(CommandLineArguments.Parse(Array.Empty<string>()));

        //Assert
        Encoding.UTF8.GetString(written.ToArray()).Should().Be("activate_window\n");
    }

    [Fact]
    public void a_column_defaults_to_one_when_only_a_line_is_given()
    {
        //Arrange
        MemoryStream written = new MemoryStream();
        RemoteConnection connection = new RemoteConnection(written);

        //Act
        connection.CommandLine(CommandLineArguments.Parse(new[] { "--line=9", "a.ly" }));

        //Assert
        Encoding.UTF8.GetString(written.ToArray()).Should().Contain("set_cursor 9 1\n");
    }

    [Fact]
    public void a_path_survives_the_escaping_round_trip()
    {
        //Arrange
        string path = Path.Combine(_directory, "a file with spaces & a #.ly");

        //Act
        string escaped = RemoteConnection.Escape(path);

        //Assert
        escaped.Should().NotContain(" ");
        escaped.Should().NotContain("\n");
        RemoteConnection.Unescape(escaped).Should().Be(Path.GetFullPath(path));
    }

    [Fact]
    public void the_reader_performs_every_command_in_order()
    {
        //Arrange
        RecordingTarget target = new RecordingTarget();
        IncomingCommands reader = new IncomingCommands(target);
        string text =
            "encoding latin1\n"
            + "open " + RemoteConnection.Escape("/tmp/one.ly") + "\n"
            + "set_current " + RemoteConnection.Escape("/tmp/one.ly") + "\n"
            + "set_cursor 4 7\n"
            + "activate_window\n"
            + "bye\n";

        //Act
        reader.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)));

        //Assert
        target.Opened.Should().ContainSingle();
        target.Opened[0].Path.Should().Be("/tmp/one.ly");
        target.Opened[0].Encoding.Should().Be("latin1");
        target.Current.Should().ContainSingle();
        target.Cursor.Should().Be((4, 7));
        target.Activated.Should().Be(1);
        reader.IsFinished.Should().BeTrue();
    }

    [Fact]
    public void a_line_split_across_two_reads_is_performed_once_and_whole()
    {
        //Arrange — the stream hands over three bytes at a time, so nearly every
        //command arrives in pieces; upstream keeps the tail in its own buffer
        //for exactly this.
        RecordingTarget target = new RecordingTarget();
        IncomingCommands reader = new IncomingCommands(target);
        string text = "open " + RemoteConnection.Escape("/tmp/two.ly") + "\n"
            + "activate_window\n";

        //Act
        reader.Read(new DribblingStream(Encoding.UTF8.GetBytes(text), 3));

        //Assert
        target.Opened.Should().ContainSingle();
        target.Opened[0].Path.Should().Be("/tmp/two.ly");
        target.Activated.Should().Be(1);
    }

    [Fact]
    public void an_unknown_command_is_ignored()
    {
        //Arrange
        RecordingTarget target = new RecordingTarget();
        IncomingCommands reader = new IncomingCommands(target);

        //Act
        reader.Read(new MemoryStream(Encoding.UTF8.GetBytes("nonsense 1 2\nbye\n")));

        //Assert
        target.Opened.Should().BeEmpty();
        target.Activated.Should().Be(0);
    }

    [Fact]
    public void every_command_reaches_the_window_on_the_windows_own_thread()
    {
        //Arrange — board trap 22: the socket is read on a worker thread and
        //the window must never be touched from there.
        RecordingTarget target = new RecordingTarget();
        List<Action> posted = new List<Action>();
        IncomingCommands reader = new IncomingCommands(target, work => posted.Add(work));

        //Act
        reader.Read(new MemoryStream(Encoding.UTF8.GetBytes("activate_window\nbye\n")));

        //Assert
        target.Activated.Should().Be(0);
        posted.Should().ContainSingle();
        posted[0]();
        target.Activated.Should().Be(1);
    }

    [Fact]
    public void a_whole_conversation_travels_over_a_real_socket()
    {
        //Arrange — the transport itself: a listener on a name of this test's
        //own, so a Fresco.Brix that happens to be running is untouched.
        string id = "frescobrix-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        using IRemoteListener listener = RemoteTransport.TryListen(id);
        listener.Should().NotBeNull();

        RecordingTarget target = new RecordingTarget();
        ManualResetEventSlim done = new ManualResetEventSlim();
        Thread server = new Thread(() =>
        {
            using Stream accepted = listener.Accept(CancellationToken.None);
            new IncomingCommands(target).Read(accepted);
            done.Set();
        })
        {
            IsBackground = true,
        };
        server.Start();

        //Act
        using Stream client = RemoteTransport.TryConnect(
            id, RemoteInstance.ConnectTimeoutMilliseconds);
        client.Should().NotBeNull();
        RemoteConnection connection = new RemoteConnection(client);
        connection.CommandLine(CommandLineArguments.Parse(
            new[] { "-l", "3", Path.Combine(_directory, "score.ly") }));
        connection.Close();

        //Assert
        done.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)
            .Should().BeTrue();
        target.Opened.Should().ContainSingle();
        target.Opened[0].Path.Should().Be(Path.Combine(_directory, "score.ly"));
        target.Cursor.Should().Be((3, 1));
        target.Activated.Should().Be(1);
    }

    [Fact]
    public void a_second_listener_on_the_same_name_is_refused()
    {
        //Arrange
        string id = "frescobrix-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        using IRemoteListener first = RemoteTransport.TryListen(id);
        first.Should().NotBeNull();

        //Act
        IRemoteListener second = RemoteTransport.TryListen(id);

        //Assert — which is how a second launch knows there is a first.
        second.Should().BeNull();
    }

    [Fact]
    public void connecting_to_a_name_nobody_listens_on_answers_nothing()
    {
        //Act
        Stream stream = RemoteTransport.TryConnect(
            "frescobrix-test-" + Guid.NewGuid().ToString("N"), 200);

        //Assert
        stream.Should().BeNull();
    }

    [Fact]
    public void a_socket_left_behind_by_a_dead_process_can_be_taken_over()
    {
        //Arrange — upstream's stale-socket recovery: the file is there, but
        //nothing answers on it.
        if (OperatingSystem.IsWindows()) { return; }

        string id = "frescobrix-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        string path = RemoteTransport.SocketPath(id);
        File.WriteAllText(path, string.Empty);

        try
        {
            //Act
            RemoteTransport.TryListen(id).Should().BeNull();
            RemoteTransport.TryConnect(id, 200).Should().BeNull();
            RemoteTransport.RemoveServer(id);
            using IRemoteListener listener = RemoteTransport.TryListen(id);

            //Assert
            listener.Should().NotBeNull();
        }
        finally
        {
            try { File.Delete(path); }
            catch (IOException) { }
        }
    }

    [Fact]
    public void the_new_switch_stops_the_handover_before_anything_else()
    {
        //Act — upstream's `if not args.new and remote.enabled()'. Nothing is
        //connected to, which is why this is safe to run beside a live instance.
        bool handedOff = RemoteInstance.TryHandOff(new[] { "--new", "a.ly" });

        //Assert
        handedOff.Should().BeFalse();
    }

    private sealed class RecordingTarget : IRemoteCommandTarget
    {
        internal List<(string Path, string Encoding)> Opened { get; }
            = new List<(string, string)>();

        internal List<string> Current { get; } = new List<string>();

        internal (int Line, int Column) Cursor { get; private set; }

        internal int Activated { get; private set; }

        public void OpenPath(string path, string encoding) => Opened.Add((path, encoding));

        public void SetCurrent(string path) => Current.Add(path);

        public void SetCursor(int line, int column) => Cursor = (line, column);

        public void ActivateWindow() => Activated++;
    }

    /// <summary>A stream that hands over a few bytes at a time.</summary>
    private sealed class DribblingStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunk;
        private int _position;

        internal DribblingStream(byte[] data, int chunk)
        {
            _data = data;
            _chunk = chunk;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int take = Math.Min(Math.Min(_chunk, count), _data.Length - _position);
            Array.Copy(_data, _position, buffer, offset, take);
            _position += take;
            return take;
        }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
    }
}

/// <summary>
/// The command line upstream's <c>argparse</c> block accepts, as far as FD5's
/// protocol carries it.
/// </summary>
public class CommandLineArgumentsTests
{
    [Fact]
    public void plain_arguments_are_files()
    {
        //Act
        CommandLineArguments parsed = CommandLineArguments.Parse(
            new[] { "one.ly", "two.ily" });

        //Assert
        parsed.Files.Should().Equal("one.ly", "two.ily");
        parsed.New.Should().BeFalse();
        parsed.Line.Should().BeNull();
        parsed.Column.Should().BeNull();
        parsed.Encoding.Should().BeNull();
    }

    [Theory]
    [InlineData("-n")]
    [InlineData("--new")]
    public void both_spellings_of_new_are_read(string spelling)
    {
        //Act
        CommandLineArguments parsed = CommandLineArguments.Parse(new[] { spelling });

        //Assert
        parsed.New.Should().BeTrue();
        parsed.Files.Should().BeEmpty();
    }

    [Theory]
    [InlineData("-l", "42")]
    [InlineData("--line", "42")]
    [InlineData("--line=42", null)]
    public void every_spelling_of_line_is_read(string switchText, string value)
    {
        //Arrange
        string[] arguments = value == null
            ? new[] { switchText }
            : new[] { switchText, value };

        //Act
        CommandLineArguments parsed = CommandLineArguments.Parse(arguments);

        //Assert
        parsed.Line.Should().Be(42);
        parsed.Files.Should().BeEmpty();
    }

    [Fact]
    public void the_encoding_and_the_column_are_read()
    {
        //Act
        CommandLineArguments parsed = CommandLineArguments.Parse(
            new[] { "--encoding=latin1", "-c", "7", "score.ly" });

        //Assert
        parsed.Encoding.Should().Be("latin1");
        parsed.Column.Should().Be(7);
        parsed.Files.Should().Equal("score.ly");
    }

    [Fact]
    public void a_line_that_is_not_a_number_is_no_line_at_all()
    {
        //Act
        CommandLineArguments parsed = CommandLineArguments.Parse(new[] { "-l", "x" });

        //Assert
        parsed.Line.Should().BeNull();
    }

    [Fact]
    public void nothing_at_all_parses_to_nothing_at_all()
    {
        //Act
        CommandLineArguments parsed = CommandLineArguments.Parse(null);

        //Assert
        parsed.Files.Should().BeEmpty();
        parsed.New.Should().BeFalse();
    }

    [Fact]
    public void an_option_missing_its_value_takes_nothing_and_loses_nothing()
    {
        //Act — argparse would refuse; here the file list is what matters and a
        //trailing switch must not swallow one.
        CommandLineArguments parsed = CommandLineArguments.Parse(
            new[] { "score.ly", "--line" });

        //Assert
        parsed.Files.Should().Equal("score.ly");
        parsed.Line.Should().BeNull();
    }

    [Fact]
    public void the_number_is_read_in_the_invariant_culture()
    {
        //Arrange — standing rule 7: a locale must not change what "42" means.
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            //Act
            CommandLineArguments parsed = CommandLineArguments.Parse(
                new[] { "--line=42", "--column=3" });

            //Assert
            parsed.Line.Should().Be(42);
            parsed.Column.Should().Be(3);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
