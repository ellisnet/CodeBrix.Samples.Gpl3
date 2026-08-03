using CodeBrix.LilyPort;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

public class LilyPortInfoTests
{
    [Fact]
    public void reports_the_pinned_upstream_version()
    {
        //Arrange / Act / Assert -- the port targets one exact LilyPond revision, and
        //the font assets and THIRD-PARTY-NOTICES.txt are pinned to the same one
        LilyPortInfo.UpstreamVersion.Should().Be("2.27.2");
        LilyPortInfo.UpstreamCommit.Should().Be("2d621459bd44cb1758f822a69757242eab843060");
    }
}
