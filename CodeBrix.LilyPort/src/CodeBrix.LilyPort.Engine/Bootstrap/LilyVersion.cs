/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1999--2026 Jan Nieuwenhuizen <janneke@gnu.org>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

namespace CodeBrix.LilyPort.Engine.Bootstrap; //was previously: lily/lily-version.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The version string banners and generated output stamp themselves with.
/// </summary>
public static class LilyVersion
{
    /// <summary>The major version of the LilyPond this ports.</summary>
    public const string MajorVersion = "2";

    /// <summary>The minor version of the LilyPond this ports.</summary>
    public const string MinorVersion = "27";

    /// <summary>The patch level of the LilyPond this ports.</summary>
    public const string PatchLevel = "2";

    /// <summary>
    /// The extra patch level — empty on a release, exactly as upstream's
    /// <c>MY_PATCH_LEVEL</c> is for 2.27.2.
    /// </summary>
    public const string MyPatchLevel = "";

    /// <summary>
    /// Builds the version string, honouring the <c>deterministic</c> program option
    /// that pins every stamped output to <c>0.0.0</c> for byte-reproducible runs.
    /// </summary>
    /// <returns>The version, e.g. <c>2.27.2</c>.</returns>
    public static string VersionString()
    {
        if (Objects.SchemeUtilities.ToBool(LilyPondScheme.Options.Get("deterministic")))
        {
            return "0.0.0";
        }

        string version = MajorVersion + "." + MinorVersion + "." + PatchLevel;
        if (MyPatchLevel.Length > 0)
        {
            version += "." + MyPatchLevel;
        }

        return version;
    }
}
