// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Diagnostics;
using System.Reflection;

namespace Fresco.Brix.Services; //was previously: frescobaldi/appinfo.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Information about the Fresco.Brix application: the identity every window
/// title, About box and settings path is built from.
/// </summary>
/// <remarks>
/// Upstream's LilyPond-version fields (<c>required_python_ly_version</c>,
/// <c>lilydoc_stable</c>, <c>lilydoc_development</c>) have no analogue here —
/// the engine is compiled in (FR5.1) and the manuals are bundled assets (FR8).
/// </remarks>
public static class AppInfo
{
    /// <summary>The name used everywhere in the application.</summary>
    public const string AppName = "Fresco.Brix"; //was previously: appname

    /// <summary>The short name, used for settings and data directories.</summary>
    public const string Name = "fresco.brix"; //was previously: name

    /// <summary>The one-line description.</summary>
    //was previously: "LilyPond Music Editor". The one-liner labels the application
    //itself, so it names the engine the user drives. LongDescription below is About
    //text and DOES acknowledge the LilyPond lineage — that is the allowed place.
    public const string Description = "LilyPort Music Editor";

    /// <summary>The longer description, used in About.</summary>
    public const string LongDescription =
        "Fresco.Brix is an advanced text editor for LilyPond sheet music files, "
        + "engraving them in-process with no external LilyPond installation. "
        + "Features include an integrated music view and a powerful Score Wizard.";

    /// <summary>
    /// The upstream project Fresco.Brix is modelled on, credited in About and
    /// in the README (FR9). Fresco.Brix never presents AS Frescobaldi.
    /// </summary>
    public const string InspiredBy = "Frescobaldi";

    /// <summary>The maintainer.</summary>
    public const string Maintainer = "Jeremy Ellis";

    /// <summary>The licence the application is conveyed under (FR1).</summary>
    public const string License = "GPL-3.0-only";

    /// <summary>Gets the application's version.</summary>
    /// <remarks>Read off the assembly rather than written out, so the one
    /// place a version number lives is the csproj — the same rule the engine's
    /// own version follows.</remarks>
    public static string Version => field ??=
        typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? FileVersionInfo
            .GetVersionInfo(typeof(AppInfo).Assembly.Location).FileVersion
        ?? "0.0.0";
}
