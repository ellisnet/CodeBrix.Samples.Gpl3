// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Docs.Generation;
using Lily.Shell.Services;
using SilverAssertions;
using System;
using System.Collections.Generic;
using Xunit;

namespace Lily.Shell.Core.Tests;

/// <summary>
/// The contract a long-lived shell needs from documentation generation, gated with the
/// forty-second engine run replaced by a counter.
/// </summary>
/// <remarks>
/// ⚠ THE THING BEING GATED IS A TRAP, NOT A PREFERENCE. The first run of
/// <c>ly/generate-documentation.ly</c> in a process writes all nineteen files; every later
/// run in the same process returns in a tenth of a second having written NOTHING, reports
/// all nineteen missing, and does not throw. A shell is where two <c>docs</c> commands in
/// one process is the normal case, so "generate once and reuse" is what keeps the second
/// manual from rendering out of an empty directory — successfully, with its appendices
/// simply absent.
/// </remarks>
public class DocsRunnerTests
{
    [Fact]
    public void generation_happens_once_per_process_however_many_manuals_are_asked_for()
    {
        //Arrange
        var calls = new List<string>();
        var runner = new DocsRunner
        {
            Generator = directory =>
            {
                calls.Add(directory);
                return Array.Empty<string>();
            },
        };

        //Act
        string first = runner.EnsureGenerated(null);
        string second = runner.EnsureGenerated(null);
        string third = runner.EnsureGenerated(null);

        //Assert
        calls.Should().HaveCount(1);
        second.Should().Be(first);
        third.Should().Be(first);
        runner.GeneratedDirectory.Should().Be(first);
    }

    /// <summary>
    /// The control: an INCOMPLETE generation is not cached, so the next command tries
    /// again instead of rendering out of a directory that holds part of a manual.
    /// </summary>
    [Fact]
    public void an_incomplete_generation_throws_and_is_not_remembered()
    {
        //Arrange
        var calls = 0;
        var runner = new DocsRunner
        {
            Generator = _ =>
            {
                calls++;
                return new[] { "internals.texi" };
            },
        };

        //Act
        Action generate = () => runner.EnsureGenerated(null);

        //Assert
        generate.Should().Throw<InvalidOperationException>()
            .WithMessage("*18 of 19*internals.texi*");
        runner.GeneratedDirectory.Should().BeNull();
        generate.Should().Throw<InvalidOperationException>();
        calls.Should().Be(2);
    }

    /// <summary>
    /// The generated files go in a directory NAMED <c>en</c>, because the manuals include
    /// the port's own files language-qualified — <c>@include en/markup-commands.tely</c>,
    /// eighteen times in the notation manual alone — and RenderPaths refuses any other
    /// name. Getting this wrong does not fail: it silently resolves nothing.
    /// </summary>
    [Fact]
    public void the_generated_directory_is_named_en()
    {
        //Arrange
        var runner = new DocsRunner { Generator = _ => Array.Empty<string>() };

        //Act
        string generated = runner.EnsureGenerated(null);

        //Assert
        System.IO.Path.GetFileName(generated).Should().Be("en");
        generated.Should().StartWith(DocsRunner.ScratchRoot);
    }

    [Fact]
    public void the_nineteen_expected_files_are_the_generators_own_list()
    {
        //Assert
        DocumentationGenerator.ExpectedOutputs.Should().HaveCount(19);
        DocumentationGenerator.ExpectedOutputs.Should().Contain("internals.texi");
    }

    [Fact]
    public void a_run_needs_a_request()
    {
        //Arrange
        var runner = new DocsRunner();

        //Act
        Action render = () => runner.Render(null, null);

        //Assert
        render.Should().Throw<ArgumentNullException>();
    }
}
