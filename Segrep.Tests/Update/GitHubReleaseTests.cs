using System.Text.Json;
using Segrep.Update;

namespace Segrep.Tests.Update;

public class GitHubReleaseTests
{
    [Fact]
    public void DeserializesGitHubReleasePayload()
    {
        const string json =
            """
            {
              "tag_name": "v0.3.0",
              "assets": [
                { "name": "segrep-osx-arm64", "browser_download_url": "https://example.com/segrep-osx-arm64" },
                { "name": "SHA256SUMS", "browser_download_url": "https://example.com/SHA256SUMS" }
              ]
            }
            """;

        var release = JsonSerializer.Deserialize<GitHubRelease>(json);

        Assert.NotNull(release);
        Assert.Equal("v0.3.0", release.TagName);
        Assert.Equal(2, release.Assets.Count);
        Assert.Equal("segrep-osx-arm64", release.Assets[0].Name);
        Assert.Equal("https://example.com/segrep-osx-arm64", release.Assets[0].BrowserDownloadUrl);
    }

    [Fact]
    public void MissingFieldsDefaultToEmpty()
    {
        var release = JsonSerializer.Deserialize<GitHubRelease>("{}");

        Assert.NotNull(release);
        Assert.Equal(string.Empty, release.TagName);
        Assert.Empty(release.Assets);
    }
}
