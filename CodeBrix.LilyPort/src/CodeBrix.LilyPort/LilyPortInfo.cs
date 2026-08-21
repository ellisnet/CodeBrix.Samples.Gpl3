// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Reflection;
using CodeBrix.LilyPort.Engine.Bootstrap;

namespace CodeBrix.LilyPort;

/// <summary>
/// The library-global identity of CodeBrix.LilyPort: its own version, the LilyPond
/// release it is compatible with, and the provenance of the port.
/// <para>
/// //was previously: a note here said the engraving engine was not yet ported and this
/// type existed only to give the facade assembly a public surface. The engine has since
/// been ported in full and is verified against the pinned oracle's own regression suite;
/// this type remains the package's statement of provenance.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Version"/> AND <see cref="CompatibleWithVersion"/> ARE TWO DIFFERENT
/// THINGS AND MUST NEVER BE CONFLATED — this type exists partly to keep them apart.
/// </para>
/// <list type="bullet">
/// <item><description><see cref="Version"/> is the version OF CodeBrix.LilyPort — the
/// version of the NuGet package, date-stamped like <c>1.0.244.123</c>. It moves with
/// every release of this library.</description></item>
/// <item><description><see cref="CompatibleWithVersion"/> is the version of GNU LilyPond
/// that CodeBrix.LilyPort is compatible with — <c>2.27.2</c> — which is the grammar a
/// <c>.ly</c> file's <c>\version</c> statement is read against and the version engraved
/// output stamps itself with. It moves only when the port is advanced onto a newer
/// LilyPond.</description></item>
/// </list>
/// <para>
/// CodeBrix.LilyPort never reports <see cref="CompatibleWithVersion"/> as its own
/// version. Anything showing "LilyPort 2.27.2" to a user is a defect.
/// </para>
/// </remarks>
public static class LilyPortInfo
{
    /// <summary>
    /// Gets the version of CodeBrix.LilyPort itself — the version of the NuGet package
    /// this assembly was built and published as, e.g. <c>1.0.244.123</c>. This is NEVER
    /// the LilyPond version; see <see cref="CompatibleWithVersion"/> for that.
    /// </summary>
    public static string Version
    {
        get
        {
            // The package version, AssemblyVersion and FileVersion are all set from the
            // csproj's date-stamped BuildVersion, so the assembly's own version IS the
            // package version. Read it here rather than restating it in source, where it
            // would be stale the moment the next package is built.
            AssemblyName name = typeof(LilyPortInfo).Assembly.GetName();
            return name.Version == null ? "0.0.0.0" : name.Version.ToString();
        }
    }

    /// <summary>
    /// Gets the version of GNU LilyPond that this release of CodeBrix.LilyPort is
    /// compatible with, e.g. <c>2.27.2</c>. This is NOT the version of CodeBrix.LilyPort;
    /// see <see cref="Version"/> for that.
    /// </summary>
    /// <remarks>
    /// //was previously: <c>UpstreamVersion</c>, which read as though it were a second
    /// version OF this library and invited exactly the conflation the remarks on
    /// <see cref="LilyPortInfo"/> warn about. The value is declared once, in
    /// <see cref="LilyVersion.CompatibleWithVersion"/>, where the engine can reach it —
    /// the reference direction runs from this facade DOWN into the engine, so the one
    /// literal has to live at the bottom and be surfaced here.
    /// </remarks>
    public static string CompatibleWithVersion => LilyVersion.CompatibleWithVersion;

    /// <summary>Gets the upstream commit this port is derived from.</summary>
    public static string UpstreamCommit => "2d621459bd44cb1758f822a69757242eab843060";

    /// <summary>Gets the upstream project URL.</summary>
    public static string UpstreamUrl => "https://gitlab.com/lilypond/lilypond";
}
