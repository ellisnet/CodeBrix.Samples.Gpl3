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
/// The version string banners and generated output stamp themselves with, and the
/// single point in the whole port at which the LilyPond release it is compatible
/// with is named.
/// </summary>
/// <remarks>
/// <para>
/// //was previously: upstream declares MAJOR_VERSION, MINOR_VERSION, PATCH_LEVEL and
/// MY_PATCH_LEVEL as four separate literals in lily/lily-version.cc, and
/// lily/general-scheme.cc names the same release AGAIN for <c>ly:version</c>. The port
/// inverts that: <see cref="CompatibleWithVersion"/> is the ONE literal and the four
/// components are split out of it, so the release is stated once and every other
/// statement of it follows.
/// </para>
/// <para>
/// TWO DIFFERENT VERSIONS MEET HERE AND MUST NEVER BE CONFLATED. The version of
/// CodeBrix.LilyPort is its NuGet package version — date-stamped
/// <c>1.&lt;years-since-2026&gt;.&lt;day-of-year&gt;.&lt;minute-of-day&gt;</c>, e.g.
/// <c>1.0.244.123</c>, and reported by <c>CodeBrix.LilyPort.LilyPortInfo.Version</c>.
/// What THIS type carries is the LilyPond release the port is COMPATIBLE WITH, which
/// is what a <c>\version</c> statement in a <c>.ly</c> file is compared against and
/// what engraved output stamps itself with. LilyPort never reports
/// <see cref="CompatibleWithVersion"/> as its own version.
/// </para>
/// <para>
/// To move the port onto a newer LilyPond, change <see cref="CompatibleWithVersion"/>
/// here and nowhere else. A search of the codebase for the release number should find
/// it at this one site, and otherwise only in comments and documentation.
/// </para>
/// </remarks>
public static class LilyVersion
{
    /// <summary>
    /// The LilyPond release this port is compatible with — THE one literal statement of
    /// it in CodeBrix.LilyPort. This is NOT the version of CodeBrix.LilyPort itself; see
    /// the remarks on <see cref="LilyVersion"/>, and
    /// <c>CodeBrix.LilyPort.LilyPortInfo.Version</c> for that.
    /// </summary>
    public const string CompatibleWithVersion = "2.27.2";

    /// <summary>
    /// <see cref="CompatibleWithVersion"/> split on '.', so the four components below are
    /// derived from it rather than restating it.
    /// </summary>
    private static readonly string[] Components = CompatibleWithVersion.Split('.');

    /// <summary>The major version of the LilyPond this ports.</summary>
    public static string MajorVersion => Components[0];

    /// <summary>The minor version of the LilyPond this ports.</summary>
    public static string MinorVersion => Components.Length > 1 ? Components[1] : "0";

    /// <summary>The patch level of the LilyPond this ports.</summary>
    public static string PatchLevel => Components.Length > 2 ? Components[2] : "0";

    /// <summary>
    /// The extra patch level — empty on a release, exactly as upstream's
    /// <c>MY_PATCH_LEVEL</c> is for a released version. It is the fourth component of
    /// <see cref="CompatibleWithVersion"/> when that names one, and empty otherwise.
    /// </summary>
    public static string MyPatchLevel => Components.Length > 3 ? Components[3] : "";

    /// <summary>
    /// Builds the version string, honouring the <c>deterministic</c> program option
    /// that pins every stamped output to <c>0.0.0</c> for byte-reproducible runs.
    /// </summary>
    /// <returns>The version, e.g. the value of <see cref="CompatibleWithVersion"/>.</returns>
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
