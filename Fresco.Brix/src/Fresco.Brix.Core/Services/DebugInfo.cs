// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Engrave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Fresco.Brix.Services; //was previously: frescobaldi/debuginfo.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The versions of everything the application is built on — what a user pastes
/// into a bug report, and what the About window's Version tab shows.
/// </summary>
/// <remarks>
/// <para>
/// ADAPTED rather than ported: upstream reports the versions of the PYTHON
/// modules it imports (PyQt, Qt, python-ly, qpageview) and, when it is running
/// from a git checkout, the branch and commit. Fresco.Brix is built on
/// PACKAGES, so the rows are the packages — read off the assemblies actually
/// loaded, so what is reported is what is running rather than what a csproj
/// says. The git rows are gone with ruling FR5.7 and the "installation kind"
/// row with them: there is one kind.
/// </para>
/// <para>
/// ⚠ RULING FR13: the ENGINE contributes TWO rows and they are never conflated
/// — <see cref="LilyPortEngine.PortVersion"/> is the port's own package
/// version, and <see cref="LilyPortEngine.CompatibleWithVersion"/> is the
/// LilyPond release whose language it implements. A reader has to be able to
/// tell which number is which.
/// </para>
/// </remarks>
public static class DebugInfo
{
    /// <summary>What a version reads as when it cannot be found.</summary>
    /// <remarks>Upstream's <c>_catch_unknown</c> decorator answers the same
    /// word rather than letting a missing module stop the report.</remarks>
    public const string Unknown = "unknown";

    /// <summary>
    /// The packages reported, by the assembly each is read from, in the order
    /// they are shown.
    /// </summary>
    /// <remarks>The name on the left is the PACKAGE, which is what a bug report
    /// needs; the assembly on the right is where its version is read from.</remarks>
    private static readonly (string Label, string Assembly)[] Packages =
    {
        ("CodeBrix.Platform", "CodeBrix.Platform"),
        ("CodeBrix.Platform.AdvancedTextEdit", "CodeBrix.Platform.UI.AdvancedTextEdit"),
        ("CodeBrix.LilyScheme", "CodeBrix.LilyScheme"),
        ("CodeBrix.Audio", "CodeBrix.Audio"),
        ("CodeBrix.PdfRasterizer", "CodeBrix.PdfRasterizer"),
        ("CodeBrix.PdfDocuments", "CodeBrix.PdfDocuments"),
        //The settings store is the AppSettings add-in; CodeBrix.Sqlite is the
        //database beneath it and arrives with it. Both rows are reported,
        //because a settings bug can be in either.
        ("CodeBrix.Platform.AppSettings", "CodeBrix.Platform.AppSettings"),
        ("CodeBrix.Sqlite", "CodeBrix.Sqlite"),
        ("CodeBrix.SkiaSvg", "CodeBrix.SkiaSvg"),
    };

    /// <summary>Yields every reported name with its version.</summary>
    /// <returns>The rows, in the order they are shown.</returns>
    /// <remarks>Upstream's <c>version_info_named</c>.</remarks>
    public static IEnumerable<(string Name, string Version)> VersionInfoNamed()
    {
        yield return (AppInfo.AppName, AppInfo.Version);
        yield return (".NET", Framework());

        //⚠ FR13: two rows, never one. The first is the engine PACKAGE's
        //version; the second is the release of the language it implements.
        yield return ("CodeBrix.LilyPort", EngineVersion());
        yield return ("compatible with", CompatibleVersion());

        foreach (var (label, assembly) in Packages)
        {
            string version = PackageVersion(assembly);
            if (version != null) { yield return (label, version); }
        }

        yield return ("OS", OperatingSystemDescription());
        yield return ("Head", Head());
    }

    /// <summary>Writes every reported version as one block of text.</summary>
    /// <param name="separator">What goes between the rows.</param>
    /// <returns>The text.</returns>
    /// <remarks>Upstream's <c>version_info_string</c>, in upstream's own
    /// <c>name: version</c> shape.</remarks>
    public static string VersionInfoString(string separator = "\n")
        => string.Join(
            separator ?? "\n",
            VersionInfoNamed().Select(row => row.Name + ": " + row.Version));

    /// <summary>
    /// The package version an assembly came from, read once out of the
    /// dependency file the build wrote.
    /// </summary>
    /// <remarks>
    /// ⚠ Some packages stamp a PLACEHOLDER assembly version — the CodeBrix
    /// Platform family's assemblies all say <c>255.255.255.255</c>, which is
    /// its Uno-fork inheritance — so the assembly cannot be asked what package
    /// it came from. The <c>.deps.json</c> beside the program CAN answer:
    /// restore wrote the resolved package identity and version for every file
    /// it copied, which is exactly what a bug report needs.
    /// </remarks>
    private static Dictionary<string, string> _depsVersions;

    /// <summary>The version of one loaded assembly.</summary>
    /// <param name="assemblyName">The assembly's simple name.</param>
    /// <returns>The package version it came from, its own informational
    /// version when the dependency file has nothing, or null when there is no
    /// such assembly.</returns>
    public static string PackageVersion(string assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName)) { return null; }

        string resolved = DepsVersion(assemblyName);
        if (resolved != null) { return resolved; }

        try
        {
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(
                    a.GetName().Name, assemblyName, StringComparison.Ordinal));

            //An assembly nothing has touched yet is not loaded; its file is
            //beside ours, and reading the name off the file is enough for a
            //version row.
            if (assembly == null)
            {
                string path = Path.Combine(
                    AppContext.BaseDirectory, assemblyName + ".dll");
                if (!File.Exists(path)) { return null; }

                return AssemblyName.GetAssemblyName(path).Version?.ToString() ?? Unknown;
            }

            string informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            return string.IsNullOrEmpty(informational)
                ? assembly.GetName().Version?.ToString() ?? Unknown
                : informational.Split('+')[0];
        }
        catch (Exception exception) when (exception is IOException
            or BadImageFormatException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return Unknown;
        }
    }

    /// <summary>
    /// The package version an assembly file was restored from, or null.
    /// </summary>
    /// <param name="assemblyName">The assembly's simple name.</param>
    /// <returns>The version, or null.</returns>
    private static string DepsVersion(string assemblyName)
    {
        Dictionary<string, string> versions = _depsVersions ??= ReadDeps();
        return versions.TryGetValue(assemblyName, out var version) ? version : null;
    }

    /// <summary>
    /// Reads the dependency file the build wrote and maps every assembly file
    /// it names to the version of the package it came from.
    /// </summary>
    /// <returns>The map; empty when there is no dependency file to read.</returns>
    private static Dictionary<string, string> ReadDeps()
    {
        Dictionary<string, string> map
            = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            string name = Assembly.GetEntryAssembly()?.GetName().Name;
            if (string.IsNullOrEmpty(name)) { return map; }

            string path = Path.Combine(AppContext.BaseDirectory, name + ".deps.json");
            if (!File.Exists(path)) { return map; }

            using System.Text.Json.JsonDocument document
                = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("targets", out var targets))
            {
                return map;
            }

            foreach (var target in targets.EnumerateObject())
            {
                foreach (var library in target.Value.EnumerateObject())
                {
                    //"Package.Id/1.2.3.4" — the half after the slash is the
                    //version, and a project reference has no slash at all.
                    int slash = library.Name.LastIndexOf('/');
                    if (slash <= 0) { continue; }

                    string version = library.Name.Substring(slash + 1);
                    if (!library.Value.TryGetProperty("runtime", out var runtime))
                    {
                        continue;
                    }

                    foreach (var file in runtime.EnumerateObject())
                    {
                        string simple = Path.GetFileNameWithoutExtension(file.Name);
                        if (!string.IsNullOrEmpty(simple)) { map[simple] = version; }
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or System.Text.Json.JsonException
            or NotSupportedException or ArgumentException)
        {
            //A report with a missing row is better than no report.
        }

        return map;
    }

    /// <summary>The runtime the application is running on.</summary>
    /// <returns>The description.</returns>
    private static string Framework()
    {
        try { return RuntimeInformation.FrameworkDescription; }
        catch (PlatformNotSupportedException) { return Unknown; }
    }

    /// <summary>The engine package's own version.</summary>
    /// <returns>The version.</returns>
    private static string EngineVersion()
    {
        try { return LilyPortEngine.PortVersion ?? Unknown; }
        catch (Exception exception) when (exception is TypeInitializationException
            or MissingMemberException or FileNotFoundException)
        {
            return Unknown;
        }
    }

    /// <summary>The release of the language the engine implements.</summary>
    /// <returns>The version.</returns>
    private static string CompatibleVersion()
    {
        try { return LilyPortEngine.CompatibleWithVersion ?? Unknown; }
        catch (Exception exception) when (exception is TypeInitializationException
            or MissingMemberException or FileNotFoundException)
        {
            return Unknown;
        }
    }

    /// <summary>The operating system, with the distribution when there is one.</summary>
    /// <returns>The description.</returns>
    /// <remarks>Upstream reads <c>PRETTY_NAME</c> out of
    /// <c>platform.freedesktop_os_release()</c> on Linux and says "unknown
    /// distribution" when it cannot; the same file is read here.</remarks>
    private static string OperatingSystemDescription()
    {
        string description;
        try { description = RuntimeInformation.OSDescription; }
        catch (PlatformNotSupportedException) { return Unknown; }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) { return description; }

        //⚠ .NET's OSDescription on Linux is ALREADY the distribution's pretty
        //name, where Python's platform.platform() is the kernel string
        //upstream appends the distribution to. Appending it again reads
        //"LMDE 7 (gigi) (LMDE 7 (gigi))".
        string distribution = PrettyName();
        if (distribution == null || description.Contains(
                distribution, StringComparison.OrdinalIgnoreCase))
        {
            return description;
        }

        return description + " (" + distribution + ")";
    }

    private static string PrettyName()
    {
        foreach (var path in new[] { "/etc/os-release", "/usr/lib/os-release" })
        {
            try
            {
                if (!File.Exists(path)) { continue; }

                foreach (var line in File.ReadLines(path))
                {
                    if (!line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    return line.Substring("PRETTY_NAME=".Length).Trim().Trim('"');
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException)
            {
                //Play it safe, exactly as upstream's OSError arm does.
            }
        }

        return null;
    }

    /// <summary>
    /// Which of the six heads is running, read off the process's own assembly.
    /// </summary>
    /// <returns>The head's name.</returns>
    /// <remarks>//was previously: upstream's "installation kind", which
    /// distinguishes a Flatpak from a distribution package and a
    /// <c>.app</c> bundle from a command line. What matters here instead is
    /// which windowing head is drawing, because that is what a rendering bug
    /// report has to say.</remarks>
    private static string Head()
    {
        try
        {
            string name = Assembly.GetEntryAssembly()?.GetName().Name;
            if (string.IsNullOrEmpty(name)) { return Unknown; }

            int dot = name.LastIndexOf('.');
            return dot >= 0 && dot + 1 < name.Length ? name.Substring(dot + 1) : name;
        }
        catch (Exception exception) when (exception is BadImageFormatException
            or NotSupportedException)
        {
            return Unknown;
        }
    }
}
