using Segrep.Update;

namespace Segrep.Tests.Update;

public class SelfUpdaterTests
{
    [Fact]
    public void FindChecksumParsesSha256SumFormat()
    {
        const string checksums =
            """
            0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  segrep-linux-x64
            fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210  segrep-osx-arm64
            """;

        Assert.Equal(
            "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210",
            SelfUpdater.FindChecksum(checksums, "segrep-osx-arm64"));
    }

    [Fact]
    public void FindChecksumHandlesBinaryModeAsterisk()
    {
        const string checksums = "abc123  *segrep-linux-arm64\n";

        Assert.Equal("abc123", SelfUpdater.FindChecksum(checksums, "segrep-linux-arm64"));
    }

    [Fact]
    public void FindChecksumReturnsNullWhenAssetMissing()
    {
        Assert.Null(SelfUpdater.FindChecksum("abc123  other-file\n", "segrep-osx-arm64"));
        Assert.Null(SelfUpdater.FindChecksum("", "segrep-osx-arm64"));
    }

    [Fact]
    public void FindChecksumIgnoresMalformedLines()
    {
        const string checksums =
            """
            just-one-token
            abc123  segrep-osx-x64
            """;

        Assert.Equal("abc123", SelfUpdater.FindChecksum(checksums, "segrep-osx-x64"));
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v0.2.0", "0.2.0")]
    public void TrimTagStripsLeadingV(string tag, string expected)
    {
        Assert.Equal(expected, SelfUpdater.TrimTag(tag));
    }

    [Fact]
    public void ParseTagParsesVersionFromTag()
    {
        Assert.Equal(new Version(1, 2, 3), SelfUpdater.ParseTag("v1.2.3"));
        Assert.Equal(new Version(0, 2, 0), SelfUpdater.ParseTag("0.2.0"));
    }

    [Fact]
    public void ParseTagReturnsNullForNonVersionTags()
    {
        Assert.Null(SelfUpdater.ParseTag("latest"));
        Assert.Null(SelfUpdater.ParseTag("v1"));
    }
}
