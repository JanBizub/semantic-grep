using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Segrep.Update;

/// <summary>
/// Fetches release metadata from the GitHub Releases API for the segrep repository.
/// </summary>
public sealed class GitHubReleaseClient
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/JanBizub/semantic-grep/releases/latest";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Returns the latest published release, or <c>null</c> if the repository has no releases yet.
    /// </summary>
    public async Task<GitHubRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = Timeout };
        // GitHub's API requires a User-Agent and rewards the versioned Accept header.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("segrep", VersionInfo.Current));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await client.GetAsync(LatestReleaseUrl, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // No releases published for the repo yet.
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken);
    }
}

public sealed record GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; init; } = string.Empty;

    [JsonPropertyName("assets")]
    public IReadOnlyList<GitHubReleaseAsset> Assets { get; init; } = [];
}

public sealed record GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; init; } = string.Empty;
}
