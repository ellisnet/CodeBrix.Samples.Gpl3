// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Docs.Manuals;
using Lily.Shell.Commands;
using SilverAssertions;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Lily.Shell.Core.Tests;

/// <summary>
/// What the shell's <c>docs</c> command accepts — gated here rather than through the
/// command, because every path through the command costs a forty-second generation and
/// then a render.
/// </summary>
public class DocsCommandLineTests
{
    [Fact]
    public void no_arguments_lists_the_manuals_rather_than_rendering_a_default_one()
    {
        //Act
        var parsed = DocsCommandLine.Parse([]);

        //Assert
        parsed.Error.Should().BeNull();
        parsed.ListOnly.Should().BeTrue();
        parsed.Manual.Should().BeNull();
    }

    [Fact]
    public void options_with_no_manual_name_also_list()
    {
        //Act
        var parsed = DocsCommandLine.Parse(["--pdf"]);

        //Assert
        parsed.ListOnly.Should().BeTrue();
    }

    /// <summary>
    /// The nine names the shell accepts ARE the catalogue's, read rather than repeated.
    /// A second hand-kept list is how a manual added at LD5 would be renderable by the
    /// tool and invisible from the shell.
    /// </summary>
    [Fact]
    public void every_manual_in_the_catalogue_is_accepted_by_name()
    {
        //Arrange
        IReadOnlyList<ManualDefinition> catalogue = ManualCatalog.All;

        //Act
        var parsed = catalogue.Select(manual => DocsCommandLine.Parse([manual.Name])).ToList();

        //Assert
        catalogue.Should().HaveCount(9);
        parsed.Should().AllSatisfy(line => line.Error.Should().BeNull());
        parsed.Select(line => line.Manual.Name).Should().Equal(catalogue.Select(m => m.Name));
    }

    /// <summary>The control for the test above: a name the catalogue does not hold fails.</summary>
    [Fact]
    public void a_name_the_catalogue_does_not_hold_is_an_error()
    {
        //Act
        var parsed = DocsCommandLine.Parse(["web"]);

        //Assert
        parsed.Error.Should().Be("unknown manual 'web'");
        parsed.Manual.Should().BeNull();
    }

    /// <summary>
    /// snippets.tely is the include-warning CONTROL, not a deliverable manual (D48), so
    /// naming it is an error rather than a render of something that was never owed.
    /// </summary>
    [Fact]
    public void the_include_warning_control_is_not_a_renderable_manual()
    {
        //Act
        var parsed = DocsCommandLine.Parse([ManualCatalog.IncludeWarningControl.Name]);

        //Assert
        parsed.Error.Should().Be("unknown manual 'snippets'");
    }

    [Fact]
    public void neither_format_asked_for_means_both()
    {
        //Act
        var parsed = DocsCommandLine.Parse(["internals"]);

        //Assert
        parsed.WantHtml.Should().BeTrue();
        parsed.WantPdf.Should().BeTrue();
    }

    [Fact]
    public void one_format_asked_for_means_only_that_one()
    {
        //Act
        var html = DocsCommandLine.Parse(["internals", "--html"]);
        var pdf = DocsCommandLine.Parse(["internals", "--pdf"]);

        //Assert
        html.WantHtml.Should().BeTrue();
        html.WantPdf.Should().BeFalse();
        pdf.WantHtml.Should().BeFalse();
        pdf.WantPdf.Should().BeTrue();
    }

    [Fact]
    public void the_engraver_is_registered_unless_the_control_run_is_asked_for()
    {
        //Assert
        DocsCommandLine.Parse(["notation"]).EngraveSnippets.Should().BeTrue();
        DocsCommandLine.Parse(["notation", "--no-snippets"]).EngraveSnippets.Should().BeFalse();
    }

    [Fact]
    public void the_output_directory_is_taken_from_the_next_argument_and_made_absolute()
    {
        //Act
        var parsed = DocsCommandLine.Parse(["internals", "-o", "manuals"]);

        //Assert
        parsed.Error.Should().BeNull();
        parsed.OutputDirectory.Should().EndWith("manuals");
        System.IO.Path.IsPathRooted(parsed.OutputDirectory).Should().BeTrue();
    }

    [Fact]
    public void an_output_switch_with_nothing_after_it_is_an_error()
    {
        //Assert
        DocsCommandLine.Parse(["internals", "-o"]).Error.Should().Be("-o needs a directory");
    }

    /// <summary>
    /// ⚠ <c>--baseline</c> IS NOT AN ARBITRARY EXAMPLE. Lily.Docs has that switch and the
    /// shell deliberately does not: a baseline is frozen from a run that was read and
    /// reviewed, by the tool that owns the file, in the repository. This test is what says
    /// the omission was a decision rather than an oversight.
    /// </summary>
    [Fact]
    public void an_unknown_option_is_an_error()
    {
        //Assert
        DocsCommandLine.Parse(["internals", "--baseline"]).Error
            .Should().Be("unknown option '--baseline'");
    }

    [Fact]
    public void two_manuals_at_once_is_an_error()
    {
        //Assert
        DocsCommandLine.Parse(["internals", "notation"]).Error
            .Should().Be("one manual at a time, please ('notation' is a second)");
    }
}
