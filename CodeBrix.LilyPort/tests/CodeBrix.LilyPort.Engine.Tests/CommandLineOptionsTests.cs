// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using System.IO;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The <c>-d</c> reader: <c>lily/main.cc:576-590</c>'s split and
/// <c>scm/lily.scm:497-543</c>'s four-way decode, ported into
/// <see cref="CommandLineOptions"/> because the port has no command line and the
/// vendored Scheme half runs once at LOAD time, where a host needs options that live
/// for ONE run.
/// <para>
/// Every case here is read off upstream's two files rather than off the port: what a
/// declared type does to the TEXT, what the <c>no-</c> prefix does to the VALUE, which
/// binding an accumulative option goes to, and what happens to text that cannot be
/// read.
/// </para>
/// </summary>
public class CommandLineOptionsTests
{
    /// <summary>
    /// A store holding one option of each declared shape the decode branches on, plus
    /// the accumulative one that matters in practice.
    /// </summary>
    /// <returns>The store.</returns>
    private static ProgramOptions Store()
    {
        ProgramOptions options = new ProgramOptions();

        // #:type ,boolean-or-symbol-or-symbol-list? -- a predicate, so the text is read.
        options.Add("point-and-click", true, "Add point & click links.");
        options.DeclareValueSyntax("point-and-click", OptionValueSyntax.Read);

        // No #:type at all, which upstream stores as the boolean? predicate.
        options.Add("resolution", 101, "Set resolution for generating PNG pixmaps.");
        options.DeclareValueSyntax("resolution", OptionValueSyntax.Read);

        // #:type string #:accumulative? #t
        options.Add("include-settings", Nil.Instance, "Include file for global settings.");
        options.DeclareValueSyntax("include-settings", OptionValueSyntax.String);
        options.MarkAccumulative("include-settings");

        options.Add("log-file", false, "Redirect output to FILE.log.");
        options.DeclareValueSyntax("log-file", OptionValueSyntax.StringOrFalse);

        options.Add("backend", Symbol.Intern("svg"), "Select backend.");
        options.DeclareValueSyntax("backend", OptionValueSyntax.StringOrBoolean);

        return options;
    }

    [Fact]
    public void a_bare_option_is_true()
    {
        //Arrange
        ProgramOptions options = Store();

        //Act
        CommandLineOptions.Apply(options, "point-and-click");

        //Assert
        options.Get("point-and-click").Should().Be(true);
    }

    [Fact]
    public void a_no_prefixed_option_is_false()
    {
        //Arrange
        // -dno-SYM is not a separate syntax: the command line hands `no-point-and-click'
        // straight through and ly_set_option strips the prefix and negates the value.
        ProgramOptions options = Store();

        //Act
        CommandLineOptions.Apply(options, "no-point-and-click");

        //Assert
        options.Get("point-and-click").Should().Be(false);
        options.Get("no-point-and-click").Should().Be(false, "no such option is invented");
    }

    [Fact]
    public void a_read_typed_option_reads_its_text_as_scheme()
    {
        //Arrange
        ProgramOptions options = Store();

        //Act
        CommandLineOptions.Apply(options, "resolution=300");
        CommandLineOptions.Apply(options, "point-and-click=#f");

        //Assert
        options.Get("resolution").Should().Be(300L);
        options.Get("point-and-click").Should().Be(false);
    }

    [Fact]
    public void a_read_typed_option_can_take_a_symbol_list()
    {
        //Arrange
        // The shape BatchRunOptions.PointAndClick documents, arriving as text instead.
        ProgramOptions options = Store();

        //Act
        CommandLineOptions.Apply(options, "point-and-click=(note-event rest-event)");

        //Assert
        options.Get("point-and-click").Should().BeOfType<Pair>();
        Pair list = (Pair)options.Get("point-and-click");
        list.Car.Should().Be(Symbol.Intern("note-event"));
    }

    [Fact]
    public void a_string_typed_option_keeps_its_text_unread()
    {
        //Arrange
        // The whole point of #:type string: a path is a path, not a Scheme datum.
        ProgramOptions options = Store();

        //Act
        CommandLineOptions.Apply(options, "include-settings=/home/a b/(1 2).ily");

        //Assert
        Pair gathered = (Pair)options.Get("include-settings");
        gathered.Car.ToString().Should().Be("/home/a b/(1 2).ily");
    }

    [Fact]
    public void an_accumulative_option_gathers_rather_than_replaces()
    {
        //Arrange
        // Upstream's own docstring: "This can be passed several times to process
        // several files." lily.scm routes it to ly:append-to-option for exactly that.
        ProgramOptions options = Store();

        //Act
        CommandLineOptions.Apply(options, "include-settings=first.ily");
        CommandLineOptions.Apply(options, "include-settings=second.ily");

        //Assert
        Pair gathered = (Pair)options.Get("include-settings");
        gathered.Car.ToString().Should().Be("first.ily");
        ((Pair)gathered.Cdr).Car.ToString().Should().Be("second.ily");
    }

    [Fact]
    public void a_string_or_false_option_takes_false_only_for_the_exact_text()
    {
        //Arrange
        ProgramOptions options = Store();

        //Act
        CommandLineOptions.Apply(options, "log-file=#f");
        object off = options.Get("log-file");
        CommandLineOptions.Apply(options, "log-file=#t");

        //Assert
        off.Should().Be(false);
        options.Get("log-file").ToString().Should().Be("#t", "anything else is the text");
    }

    [Fact]
    public void a_string_or_boolean_option_takes_both_booleans_and_text()
    {
        //Arrange
        ProgramOptions options = Store();

        //Act
        CommandLineOptions.Apply(options, "backend=cairo");
        object named = options.Get("backend");
        CommandLineOptions.Apply(options, "backend=#t");

        //Assert
        named.ToString().Should().Be("cairo");
        options.Get("backend").Should().Be(true);
    }

    [Fact]
    public void an_option_the_engine_never_declared_takes_the_unknown_arm()
    {
        //Arrange
        // Frescobaldi's seven layout-control modes are names of ITS OWN invention, read
        // by its own formatter files with ly:get-option. Upstream's comment for this
        // arm: "probably used privately by the user".
        ProgramOptions options = Store();

        //Act
        CommandLineOptions.Apply(options, "debug-voices");
        CommandLineOptions.Apply(options, "debug-grob-anchors=#f");
        CommandLineOptions.Apply(options, "debug-annotate-spacing=whatever");

        //Assert
        options.Get("debug-voices").Should().Be(true);
        options.Get("debug-grob-anchors").Should().Be(false);
        options.Get("debug-annotate-spacing").ToString().Should().Be("whatever");
    }

    [Fact]
    public void the_value_is_split_at_the_first_equals_only()
    {
        //Arrange
        ProgramOptions options = Store();

        //Act
        CommandLineOptions.Apply(options, "include-settings=a=b=c.ily");

        //Assert
        ((Pair)options.Get("include-settings")).Car.ToString().Should().Be("a=b=c.ily");
    }

    [Fact]
    public void unreadable_text_warns_and_changes_nothing()
    {
        //Arrange
        // lily.scm:533-540 warns and falls through without setting anything -- a host
        // that asks for something unreadable is told, not quietly obeyed.
        ProgramOptions options = Store();
        TextWriter previous = Warn.Output;
        StringWriter log = new StringWriter();
        Warn.Output = log;

        //Act
        try
        {
            CommandLineOptions.Apply(options, "resolution=(1 2");
        }
        finally
        {
            Warn.Output = previous;
        }

        //Assert
        options.Get("resolution").Should().Be(101, "the default is untouched");
        log.ToString().Should().Contain("Ignoring option -dresolution=\"(1 2\"");
        log.ToString().Should().Contain("due to read error");
    }

    [Fact]
    public void a_blank_entry_is_ignored()
    {
        //Arrange
        ProgramOptions options = Store();

        //Act
        CommandLineOptions.Apply(options, null);
        CommandLineOptions.Apply(options, "   ");

        //Assert
        options.Get("point-and-click").Should().Be(true, "nothing was applied");
    }

    [Fact]
    public void the_next_runs_restore_takes_an_invented_option_away_again()
    {
        //Arrange
        // The lifetime a host relies on, and it needs no new machinery: RestoreValues
        // puts every snapshotted value back and DROPS anything the run declared.
        ProgramOptions options = Store();
        System.Collections.Generic.IReadOnlyDictionary<string, object> snapshot
            = options.SnapshotValues();

        //Act
        CommandLineOptions.Apply(options, "debug-voices");
        CommandLineOptions.Apply(options, "no-point-and-click");
        options.RestoreValues(snapshot);

        //Assert
        options.Get("debug-voices").Should().Be(false, "an unknown option answers false");
        options.Get("point-and-click").Should().Be(true);
    }
}
