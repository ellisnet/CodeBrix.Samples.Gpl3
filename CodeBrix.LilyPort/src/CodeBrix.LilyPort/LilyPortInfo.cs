// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace CodeBrix.LilyPort;

/// <summary>
/// Identifies the LilyPond release this port tracks.
/// <para>
/// //was previously: a note here said the engraving engine was not yet ported and this
/// type existed only to give the facade assembly a public surface. The engine has since
/// been ported in full and is verified against the pinned 2.27.2 oracle's own
/// regression suite; this type remains the package's statement of provenance.
/// </para>
/// </summary>
public static class LilyPortInfo
{
    /// <summary>Gets the LilyPond version this port is derived from.</summary>
    public static string UpstreamVersion => "2.27.2";

    /// <summary>Gets the upstream commit this port is derived from.</summary>
    public static string UpstreamCommit => "2d621459bd44cb1758f822a69757242eab843060";

    /// <summary>Gets the upstream project URL.</summary>
    public static string UpstreamUrl => "https://gitlab.com/lilypond/lilypond";
}
