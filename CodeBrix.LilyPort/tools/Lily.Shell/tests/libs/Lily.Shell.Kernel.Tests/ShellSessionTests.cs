// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Shell.Kernel;
using Lily.Shell.Kernel.Commands;
using SilverAssertions;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Lily.Shell.Kernel.Tests;

public class ShellSessionTests
{
    private sealed class ProbeCommand : IShellCommand
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public string Name => "probe";
        public string Summary => "Test probe.";
        public string Usage => "probe [args]";

        public Task ExecuteAsync(ShellCommandContext context)
        {
            Calls.Add(context.Arguments);
            context.IO.WriteLine("probed");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingInterpreter : ILineInterpreter
    {
        public List<string> Lines { get; } = [];
        public string Prompt => "sub> ";

        public Task HandleLineAsync(ShellSession session, string line,
            CancellationToken cancellationToken)
        {
            Lines.Add(line);
            return Task.CompletedTask;
        }
    }

    private static (ShellSession Session, StringBuilder Output, object Gate) CreateSession(
        params IShellCommand[] commands)
    {
        var registry = new CommandRegistry();
        foreach (var command in commands) { registry.Register(command); }

        var session = new ShellSession(registry, new ShellSessionOptions { Prompt = "lily> " });
        var output = new StringBuilder();
        var gate = new object();
        session.OutputProduced += text => { lock (gate) { output.Append(text); } };
        return (session, output, gate);
    }

    private static string Drain(StringBuilder output, object gate)
    {
        lock (gate) { return output.ToString(); }
    }

    [Fact]
    public void start_writes_banner_then_prompt()
    {
        //Arrange
        var registry = new CommandRegistry();
        var session = new ShellSession(registry,
            new ShellSessionOptions { Prompt = "lily> ", Banner = ["Welcome"] });
        var output = new StringBuilder();
        session.OutputProduced += text => output.Append(text);

        //Act
        session.Start();

        //Assert
        output.ToString().Should().Be("Welcome\r\nlily> ");
    }

    [Fact]
    public void typed_characters_are_echoed()
    {
        //Arrange
        var (session, output, gate) = CreateSession();

        //Act
        session.SendInput("hi");

        //Assert
        Drain(output, gate).Should().Be("hi");
    }

    [Fact]
    public async Task a_submitted_line_dispatches_the_command_with_its_arguments()
    {
        //Arrange
        var probe = new ProbeCommand();
        var (session, output, gate) = CreateSession(probe);

        //Act
        session.SendInput("probe one \"two words\"\r");
        await session.ExecutionChain;

        //Assert
        probe.Calls.Should().HaveCount(1);
        probe.Calls[0].Should().Equal("one", "two words");
        Drain(output, gate).Should().Be("probe one \"two words\"\r\nprobed\r\nlily> ");
    }

    [Fact]
    public async Task an_unknown_command_is_reported_and_the_prompt_returns()
    {
        //Arrange
        var (session, output, gate) = CreateSession();

        //Act
        session.SendInput("nosuch\r");
        await session.ExecutionChain;

        //Assert
        Drain(output, gate).Should().Contain("Unknown command: nosuch");
        Drain(output, gate).Should().EndWith("lily> ");
    }

    [Fact]
    public async Task crlf_submits_the_line_once()
    {
        //Arrange
        var probe = new ProbeCommand();
        var (session, _, _) = CreateSession(probe);

        //Act
        session.SendInput("probe\r\n");
        await session.ExecutionChain;

        //Assert
        probe.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task a_bare_lf_also_submits()
    {
        //Arrange
        var probe = new ProbeCommand();
        var (session, _, _) = CreateSession(probe);

        //Act
        session.SendInput("probe\n");
        await session.ExecutionChain;

        //Assert
        probe.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task up_arrow_recalls_the_previous_line()
    {
        //Arrange
        var probe = new ProbeCommand();
        var (session, output, gate) = CreateSession(probe);
        session.SendInput("probe alpha\r");
        await session.ExecutionChain;

        //Act
        session.SendInput("\x1b[A\r");
        await session.ExecutionChain;

        //Assert
        probe.Calls.Should().HaveCount(2);
        probe.Calls[1].Should().Equal("alpha");
        Drain(output, gate).Should().Contain("\x1b[Kprobe alpha");
    }

    [Fact]
    public void ctrl_c_on_an_idle_line_abandons_it_and_reprompts()
    {
        //Arrange
        var (session, output, gate) = CreateSession();
        session.SendInput("half a line");

        //Act
        session.SendInput("\x03");

        //Assert
        Drain(output, gate).Should().Be("half a line^C\r\nlily> ");
    }

    [Fact]
    public async Task a_command_exception_is_reported_not_thrown()
    {
        //Arrange
        var registry = new CommandRegistry();
        registry.Register(new ThrowingCommand());
        var session = new ShellSession(registry, new ShellSessionOptions { Prompt = "lily> " });
        var output = new StringBuilder();
        var gate = new object();
        session.OutputProduced += text => { lock (gate) { output.Append(text); } };

        //Act
        session.SendInput("boom\r");
        await session.ExecutionChain;

        //Assert
        Drain(output, gate).Should().Contain("error: it broke");
        Drain(output, gate).Should().EndWith("lily> ");
    }

    private sealed class ThrowingCommand : IShellCommand
    {
        public string Name => "boom";
        public string Summary => "Throws.";
        public string Usage => "boom";

        public Task ExecuteAsync(ShellCommandContext context) =>
            throw new System.InvalidOperationException("it broke");
    }

    [Fact]
    public async Task a_pushed_interpreter_receives_lines_and_ctrl_d_pops_it()
    {
        //Arrange
        var recorder = new RecordingInterpreter();
        var (session, output, gate) = CreateSession();
        session.PushInterpreter(recorder);

        //Act
        session.SendInput("(+ 1 2)\r");
        await session.ExecutionChain;
        session.SendInput("\x04");

        //Assert
        recorder.Lines.Should().Equal("(+ 1 2)");
        session.Prompt.Should().Be("lily> ");
        Drain(output, gate).Should().EndWith("\r\nlily> ");
    }

    private sealed class WaitingCommand : IShellCommand
    {
        public TaskCompletionSource Gate { get; } = new();
        public string Name => "wait";
        public string Summary => "Waits until released.";
        public string Usage => "wait";

        public Task ExecuteAsync(ShellCommandContext context) => Gate.Task;
    }

    [Fact]
    public void write_out_of_band_while_idle_repaints_the_prompt_and_input()
    {
        //Arrange
        var (session, output, gate) = CreateSession();
        session.Start();
        session.SendInput("hal");

        //Act
        session.WriteOutOfBand("Engine ready.");

        //Assert - fresh line, the message, then prompt + half-typed input back
        Drain(output, gate).Should().Be("lily> hal\r\nEngine ready.\r\nlily> hal");
    }

    [Fact]
    public async Task write_out_of_band_while_a_command_runs_writes_the_message_only()
    {
        //Arrange
        var waiting = new WaitingCommand();
        var (session, output, gate) = CreateSession(waiting);
        session.SendInput("wait\r");

        //Act - the message arrives mid-command; the command then completes
        session.WriteOutOfBand("Engine ready.");
        waiting.Gate.SetResult();
        await session.ExecutionChain;

        //Assert - exactly ONE prompt, printed by the command's completion
        var text = Drain(output, gate);
        text.Should().Be("wait\r\nEngine ready.\r\nlily> ");
    }

    [Fact]
    public async Task blank_lines_are_not_added_to_history()
    {
        //Arrange
        var probe = new ProbeCommand();
        var (session, output, gate) = CreateSession(probe);

        //Act - submit a blank, then a real line, then recall with Up twice
        session.SendInput("\r");
        await session.ExecutionChain;
        session.SendInput("probe x\r");
        await session.ExecutionChain;
        session.SendInput("\x1b[A\x1b[A\r");
        await session.ExecutionChain;

        //Assert - the recalled line is "probe x", not the blank
        probe.Calls.Should().HaveCount(2);
        probe.Calls[1].Should().Equal("x");
    }
}
