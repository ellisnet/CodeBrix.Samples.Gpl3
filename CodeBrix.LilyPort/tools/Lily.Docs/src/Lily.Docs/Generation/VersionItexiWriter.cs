// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.IO;
using System.Text;
using CodeBrix.LilyPort;

namespace Lily.Docs.Generation;

/// <summary>
/// Writes the <c>version.itexi</c> stand-in that <c>en/macros.itexi</c> includes.
/// <para>
/// Upstream generates this file at BUILD time with
/// <c>scripts/build/create-version-itexi.py</c>, reading the repository's
/// <c>VERSION</c> file. It is therefore not in a checkout and not among the port's
/// nineteen generated files, yet nothing that includes <c>macros.itexi</c> — which
/// is every manual, the Internals Reference included — renders without it.
/// </para>
/// <para>
/// It is DELIBERATELY NOT VENDORED (decision D49). A generated file that names a
/// version has exactly one correct value, and vendoring it would freeze a copy that
/// silently disagrees with the port the moment the port's version moves. Writing it
/// from <see cref="LilyPortInfo.UpstreamVersion"/> instead means the manuals always
/// state the version of the engine that generated them.
/// </para>
/// </summary>
public static class VersionItexiWriter
{
    /// <summary>The name upstream's <c>macros.itexi</c> includes.</summary>
    public const string FileName = "version.itexi";

    /// <summary>
    /// The stable-release version. Upstream's <c>VERSION</c> file carries this as
    /// <c>VERSION_STABLE</c> alongside the development version; the port pins one
    /// upstream release, so this is recorded here rather than derived.
    /// </summary>
    private const string StableVersion = "2.26.0";

    /// <summary>
    /// Writes <c>version.itexi</c> into <paramref name="directory"/> and returns its
    /// full path. The directory is created when it does not exist.
    /// </summary>
    /// <param name="directory">The directory to write into. This directory goes on the
    /// renderer's include search path.</param>
    /// <returns>The full path of the written file.</returns>
    public static string Write(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileName);
        File.WriteAllText(path, BuildContent(), new UTF8Encoding(false));
        return path;
    }

    /// <summary>
    /// Builds the file's text. Exposed for the fence that reads it without writing.
    /// </summary>
    /// <returns>The complete contents of the stand-in.</returns>
    internal static string BuildContent()
    {
        // The three macro names, and the blank-line spacing around them, reproduce what
        // create-version-itexi.py emits. The shape matters because macros.itexi calls
        // @version{} and friends; the spacing does not, and is matched only so that a
        // diff against an upstream-built version.itexi shows version differences rather
        // than whitespace ones.
        string development = LilyPortInfo.UpstreamVersion;

        StringBuilder text = new StringBuilder();
        text.Append("@c Stand-in for the build-generated version.itexi, written by\n");
        text.Append("@c Lily.Docs from the port's own version. Upstream generates this\n");
        text.Append("@c file with scripts/build/create-version-itexi.py.\n");
        AppendMacro(text, "version", development);
        AppendMacro(text, "versionStable", StableVersion);
        AppendMacro(text, "versionDevel", development);
        return text.ToString();
    }

    private static void AppendMacro(StringBuilder text, string name, string value)
    {
        text.Append('\n');
        text.Append("@macro ").Append(name).Append('\n');
        text.Append(value).Append('\n');
        text.Append("@end macro\n");
        text.Append('\n');
    }
}
