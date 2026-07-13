using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Segrep.Update;

/// <summary>
/// Checks GitHub Releases for a newer segrep build and, on Unix, replaces the running
/// executable in place after verifying its SHA-256 checksum.
/// </summary>
public sealed class SelfUpdater(GitHubReleaseClient releaseClient)
{
    private const string ChecksumAssetName = "SHA256SUMS";
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Compares the running version against the latest release. Does not download anything.
    /// </summary>
    public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken)
    {
        var release = await releaseClient.GetLatestReleaseAsync(cancellationToken);
        if (release is null)
        {
            return new UpdateCheck(VersionInfo.Current, LatestVersion: null, Release: null, UpdateAvailable: false);
        }

        var latest = ParseTag(release.TagName);
        var available = latest is not null && latest > VersionInfo.CurrentVersion;
        return new UpdateCheck(VersionInfo.Current, TrimTag(release.TagName), release, available);
    }

    /// <summary>
    /// Downloads the asset matching this platform, verifies its checksum, and overwrites the
    /// running executable. Progress messages are reported via <paramref name="progress"/>.
    /// Throws <see cref="PlatformNotSupportedException"/> on unsupported OS/arch (e.g. Windows).
    /// </summary>
    public async Task<UpdateResult> DownloadAndReplaceAsync(
        GitHubRelease release,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var assetName = ResolveAssetName();

        var binaryAsset = release.Assets.FirstOrDefault(a => a.Name == assetName);
        if (binaryAsset is null)
        {
            return UpdateResult.Failed(
                $"Release {release.TagName} has no asset named '{assetName}' for this platform.");
        }

        var checksumAsset = release.Assets.FirstOrDefault(a => a.Name == ChecksumAssetName);
        if (checksumAsset is null)
        {
            return UpdateResult.Failed(
                $"Release {release.TagName} is missing a '{ChecksumAssetName}' file; refusing to update without integrity verification.");
        }

        var currentPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the path of the running executable.");
        var targetDir = Path.GetDirectoryName(currentPath)
            ?? throw new InvalidOperationException($"Could not determine the directory of '{currentPath}'.");

        // Stage the download next to the current binary so the final move is same-volume (atomic).
        var tempPath = Path.Combine(targetDir, $".segrep-update-{Guid.NewGuid():N}.tmp");

        using var client = new HttpClient { Timeout = DownloadTimeout };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("segrep", VersionInfo.Current));

        try
        {
            progress.Report($"Downloading {assetName}...");
            await DownloadToFileAsync(client, binaryAsset.BrowserDownloadUrl, tempPath, cancellationToken);

            progress.Report("Verifying checksum...");
            var checksums = await client.GetStringAsync(checksumAsset.BrowserDownloadUrl, cancellationToken);
            var expected = FindChecksum(checksums, assetName);
            if (expected is null)
            {
                return UpdateResult.Failed($"'{ChecksumAssetName}' has no entry for '{assetName}'.");
            }

            var actual = await ComputeSha256Async(tempPath, cancellationToken);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                return UpdateResult.Failed(
                    $"Checksum mismatch for {assetName} — expected {expected}, got {actual}. Aborting.");
            }

            // Add execute bits (rw-r--r-- -> rwxr-xr-x). ResolveAssetName already guaranteed Unix;
            // this guard is redundant at runtime but lets the platform analyzer see it.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(tempPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            progress.Report("Installing...");
            // On Unix the running process holds the old inode open, so overwriting is safe.
            File.Move(tempPath, currentPath, overwrite: true);

            return UpdateResult.Succeeded(
                $"Updated to {TrimTag(release.TagName)}. Restart segrep to run the new version.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return PermissionFailure(currentPath, ex);
        }
        catch (IOException ex)
        {
            return PermissionFailure(currentPath, ex);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>Maps the running OS/arch to a release asset name (e.g. "segrep-osx-arm64").</summary>
    private static string ResolveAssetName()
    {
        var platform =
            OperatingSystem.IsMacOS() ? "osx" :
            OperatingSystem.IsLinux() ? "linux" :
            throw new PlatformNotSupportedException(
                "segrep update only supports macOS and Linux. On Windows, re-download the latest release manually.");

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            var other => throw new PlatformNotSupportedException($"Unsupported architecture: {other}.")
        };

        return $"segrep-{platform}-{arch}";
    }

    private static async Task DownloadToFileAsync(HttpClient client, string url, string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(path);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Finds the hex digest for <paramref name="assetName"/> in a <c>sha256sum</c>-format file.</summary>
    private static string? FindChecksum(string checksums, string assetName)
    {
        foreach (var line in checksums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Format: "<hex>  <filename>" (name may include a leading '*' for binary mode).
            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*') == assetName)
            {
                return parts[0];
            }
        }

        return null;
    }

    private static UpdateResult PermissionFailure(string currentPath, Exception ex) =>
        UpdateResult.Failed(
            $"Could not replace '{currentPath}': {ex.Message}. " +
            "Re-run with elevated permissions (e.g. sudo) or reinstall via install.sh.");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of the staging file.
        }
    }

    private static Version? ParseTag(string tag) =>
        Version.TryParse(TrimTag(tag), out var version) ? version : null;

    private static string TrimTag(string tag) =>
        tag.StartsWith('v') ? tag[1..] : tag;
}

public sealed record UpdateCheck(string CurrentVersion, string? LatestVersion, GitHubRelease? Release, bool UpdateAvailable);

public sealed record UpdateResult(bool Success, string Message)
{
    public static UpdateResult Succeeded(string message) => new(true, message);
    public static UpdateResult Failed(string message) => new(false, message);
}
