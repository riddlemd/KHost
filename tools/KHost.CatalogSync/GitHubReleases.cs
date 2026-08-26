using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace KHost.CatalogSync;

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = string.Empty;

    public bool Draft { get; set; }

    public bool Prerelease { get; set; }

    public List<GitHubAsset> Assets { get; set; } = [];
}

public sealed class GitHubAsset
{
    public string Name { get; set; } = string.Empty;

    public long Size { get; set; }

    /// <summary>"uploaded" once GitHub has finished processing; anything else is not yet fetchable.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>"sha256:..." on assets new enough to carry one. Cross-checked, never copied.</summary>
    public string? Digest { get; set; }

    [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = string.Empty;

    public string? Sha256FromDigest
        => Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? Digest[7..] : null;
}

/// <summary>
/// Everything here is deliberately unauthenticated. The host downloads with no credentials, so a
/// check that authenticates would pass against a private repo the host cannot read — which is
/// exactly how a release can look published and still be unreachable.
/// </summary>
public sealed class GitHubClient(HttpClient http)
{
    public async Task<GitHubRelease> ReadReleaseAsync(string repository, string? tag, CancellationToken cancellationToken)
    {
        var path = tag is null
            ? $"https://api.github.com/repos/{repository}/releases/latest"
            : $"https://api.github.com/repos/{repository}/releases/tags/{tag}";

        using var response = await http.GetAsync(path, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GitHub returned {(int)response.StatusCode} for {path}. A private repository reads as 404 here, "
                + "and would read the same way to KHost.");
        }

        return await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken)
            ?? throw new InvalidOperationException("GitHub returned an empty release.");
    }

    /// <summary>Downloads to <paramref name="destination"/> and returns the SHA-256 this run computed.</summary>
    public async Task<string> DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"The release asset returned {(int)response.StatusCode} without credentials. KHost sends none, "
                + "so it would see the same. Is the repository public?");
        }

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = File.Create(destination);

        var buffer = new byte[81920];
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hasher.AppendData(buffer, 0, read);

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }
}
