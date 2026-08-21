using CodeBrix.LilyPort;
using CodeBrix.LilyPort.Engine.Bootstrap;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

public class LilyPortInfoTests
{
    [Fact]
    public void reports_the_pinned_compatible_with_version()
    {
        //Arrange / Act / Assert -- the port targets one exact LilyPond revision, and
        //the font assets and THIRD-PARTY-NOTICES.txt are pinned to the same one.
        //was previously: this asserted LilyPortInfo.UpstreamVersion == "2.27.2". The
        //literal is gone on purpose: repeating the release here would make this file a
        //SECOND place the codebase names it, which is exactly what CompatibleWithVersion
        //exists to prevent. What is worth asserting is that the facade surfaces the one
        //declaration rather than carrying a copy of its own.
        LilyPortInfo.CompatibleWithVersion.Should().Be(LilyVersion.CompatibleWithVersion);
        LilyPortInfo.UpstreamCommit.Should().Be("2d621459bd44cb1758f822a69757242eab843060");
    }

    [Fact]
    public void reports_its_own_package_version_and_never_the_lilypond_version()
    {
        //Arrange
        //LilyPort's own version is its date-stamped nuget version -- 1.<years-since-2026>
        //.<day-of-year>.<minute-of-day>, e.g. 1.0.244.123 -- and NEVER the LilyPond
        //release it is compatible with. Anything showing a user "LilyPort 2.27.2" is a
        //defect, so the two are asserted to be different things.
        string ownVersion = LilyPortInfo.Version;

        //Act
        bool parsed = System.Version.TryParse(ownVersion, out System.Version packageVersion);

        //Assert
        parsed.Should().BeTrue();
        packageVersion.Major.Should().Be(1);
        ownVersion.Should().NotBe(LilyPortInfo.CompatibleWithVersion);
    }
}
