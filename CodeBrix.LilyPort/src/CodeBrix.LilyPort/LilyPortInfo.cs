// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace CodeBrix.LilyPort;

/// <summary>
/// Identifies the LilyPond release this port tracks. The engraving engine is not yet
/// ported; this type exists so the facade assembly has a public surface while the port
/// is built out underneath it.
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
