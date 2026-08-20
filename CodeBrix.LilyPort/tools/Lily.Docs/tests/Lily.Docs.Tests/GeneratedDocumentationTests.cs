// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using Lily.Docs.Generation;
using Lily.Docs.Rendering;
using SilverAssertions;
using Xunit;

namespace Lily.Docs.Tests;

/// <summary>
/// Documentation generation is a ONCE-PER-PROCESS act, and this pins it.
/// <para>
/// Found at wave LD3, by a fixture rather than by a gate: adding a second generating
/// fixture to this assembly gave the second one an EMPTY directory and a manual with no
/// appendices in it. Described in <see cref="GeneratedDocumentation"/>; asserted here,
/// because a limitation only written down is one the next session rediscovers.
/// </para>
/// </summary>
public sealed class GeneratedDocumentationTests
{
    /// <summary>The shared generation writes all nineteen documentation files.</summary>
    [Fact]
    public void the_shared_generation_writes_all_nineteen_documentation_files()
    {
        //Arrange
        GeneratedDocumentation.EnsureGenerated();

        //Act
        DocumentationGenerationResult result = GeneratedDocumentation.Result;

        //Assert
        result.MissingFiles.Should().BeEmpty();
        result.IsComplete.Should().BeTrue();
        DocumentationGenerator.ExpectedOutputs.Count.Should().Be(19);
        Path.GetFileName(GeneratedDocumentation.Directory)
            .Should().Be(RenderPaths.GeneratedDirectoryName);
    }

    /// <summary>A second generation in the same process writes nothing and says so.</summary>
    [Fact]
    public void a_second_generation_in_the_same_process_writes_nothing_and_says_so()
    {
        //Arrange
        // ⚠ FIRST, ALWAYS. Without this the order tests happen to run in decides whether
        // this test takes the process's one working generation for itself and leaves every
        // fixture in the assembly rendering out of an empty directory.
        GeneratedDocumentation.EnsureGenerated();
        string directory = Path.Combine(Path.GetTempPath(),
            "lily-docs-second-" + Guid.NewGuid().ToString("N").Substring(0, 12),
            RenderPaths.GeneratedDirectoryName);

        try
        {
            //Act
            DocumentationGenerationResult second =
                new DocumentationGenerator().Generate(directory);

            //Assert
            // ⚠ THIS IS THE BEHAVIOUR, NOT THE WISH. It is asserted so that the day the
            // engine gains a reusable generation the assertion goes red and someone reads
            // GeneratedDocumentation's remarks and simplifies it, rather than the constraint
            // quietly outliving its reason.
            //
            // Note what it does NOT do: throw. A caller that never looks at MissingFiles
            // gets a directory with nothing in it and a manual missing its appendices,
            // which is exactly how this was found.
            second.MissingFiles.Count.Should().Be(19);
            second.IsComplete.Should().BeFalse();
            Directory.GetFiles(directory).Should().BeEmpty();
        }
        finally
        {
            string root = Path.GetDirectoryName(directory);
            if (root != null && Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
