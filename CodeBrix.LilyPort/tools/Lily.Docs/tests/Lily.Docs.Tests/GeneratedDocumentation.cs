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

namespace Lily.Docs.Tests;

/// <summary>
/// The port's nineteen documentation files, generated ONCE for the whole test assembly.
/// <para>
/// ⚠ ONCE IS NOT AN OPTIMISATION. IT IS THE ONLY NUMBER THAT WORKS. Documentation
/// generation is a once-per-process act: the first call writes all nineteen files in about
/// forty seconds, and every later call in the same process returns in a tenth of a second
/// having written NOTHING, reporting all nineteen as missing. Upstream never meets this
/// because it gets a fresh process per run; a test assembly with two generating fixtures
/// meets it immediately.
/// </para>
/// <para>
/// ⚠ AND THE SECOND CALL DOES NOT THROW. It returns a result whose
/// <see cref="DocumentationGenerationResult.IsComplete"/> is false, which a caller that
/// does not look is free to ignore — and then renders a manual out of an EMPTY directory.
/// That is what it did here, and the shape of the damage is worth remembering: the render
/// succeeded, and the eighteen appendices simply were not in the manual, behind eighteen
/// Include warnings among the other warnings a baseline already tolerates. The behaviour is
/// pinned by <see cref="GeneratedDocumentationTests"/> rather than only described here.
/// </para>
/// <para>
/// This is a static holder rather than an xunit fixture because the constraint belongs to
/// the PROCESS, not to any collection of tests: whoever asks first pays for it and everyone
/// else gets the same directory.
/// </para>
/// </summary>
internal static class GeneratedDocumentation
{
    private static readonly Lazy<GenerationRun> LazyRun =
        new Lazy<GenerationRun>(Generate, isThreadSafe: true);

    /// <summary>Where the nineteen files were written. Always a directory named <c>en</c>.</summary>
    public static string Directory => LazyRun.Value.Directory;

    /// <summary>The generation run itself, so a gate can assert what it wrote.</summary>
    public static DocumentationGenerationResult Result => LazyRun.Value.Result;

    /// <summary>
    /// Forces the generation to have happened. Any test that goes on to call the generator
    /// again must call this FIRST, or it takes the one working call for itself and leaves
    /// every fixture in the assembly with an empty directory.
    /// </summary>
    public static void EnsureGenerated()
    {
        _ = LazyRun.Value;
    }

    private static GenerationRun Generate()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "lily-docs-generated-" + Guid.NewGuid().ToString("N").Substring(0, 12));

        // Named 'en': the manuals include the port's own files as 'en/<name>', so
        // RenderPaths requires that name and puts the parent on the search path.
        string directory = Path.Combine(root, RenderPaths.GeneratedDirectoryName);
        DocumentationGenerationResult result = new DocumentationGenerator().Generate(directory);

        AppDomain.CurrentDomain.ProcessExit += (sender, arguments) =>
        {
            try
            {
                if (System.IO.Directory.Exists(root))
                {
                    System.IO.Directory.Delete(root, true);
                }
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a green suite over.
            }
        };

        return new GenerationRun(directory, result);
    }

    private sealed class GenerationRun
    {
        public GenerationRun(string directory, DocumentationGenerationResult result)
        {
            Directory = directory;
            Result = result;
        }

        public string Directory { get; }

        public DocumentationGenerationResult Result { get; }
    }
}
