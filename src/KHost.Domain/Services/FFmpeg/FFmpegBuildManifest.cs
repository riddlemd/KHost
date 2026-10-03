using System.Text.Json;

namespace KHost.Domain.Services.FFmpeg;

/// <summary>The FFmpeg builds the host will download: one per platform, every file hash-pinned.</summary>
/// <remarks>The trust root for what the installer runs, the same role the plugin catalog plays for
/// plugins. Embedded in the assembly so it changes only with a KHost release.</remarks>
public sealed record FFmpegBuildManifest
{
    public const int SupportedSchemaVersion = 1;

    private const string ResourceName = "KHost.Domain.ffmpeg-builds.json";

    public int SchemaVersion { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<FFmpegBuild> Builds { get; init; } = [];

    /// <summary>The manifest this build of KHost shipped with.</summary>
    public static FFmpegBuildManifest Embedded { get; } = LoadEmbedded();

    public static FFmpegBuildManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<FFmpegBuildManifest>(json, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The FFmpeg build manifest is empty.");

        // An unknown schema is refused whole rather than read for the fields that happen to match:
        // the ones that might have moved are the ones carrying the checksums.
        if (manifest.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidOperationException($"FFmpeg build manifest schema {manifest.SchemaVersion} is not one this host reads.");

        return manifest;
    }

    /// <summary>The build for exactly this platform and architecture, or null when none is pinned.</summary>
    /// <remarks>Exact: an x64 build is not offered to arm64, even where the OS would emulate it,
    /// because nobody has run it there.</remarks>
    public FFmpegBuild? SelectFor(string platform, string architecture)
    {
        var rid = $"{platform}-{architecture}";

        return Builds.FirstOrDefault(build => string.Equals(build.Rid, rid, StringComparison.OrdinalIgnoreCase));
    }

    private static FFmpegBuildManifest LoadEmbedded()
    {
        using var stream = typeof(FFmpegBuildManifest).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is not embedded in {typeof(FFmpegBuildManifest).Assembly.GetName().Name}.");
        using var reader = new StreamReader(stream);

        return Parse(reader.ReadToEnd());
    }
}

/// <summary>One platform's pinned build.</summary>
public sealed record FFmpegBuild
{
    /// <summary>Platform and architecture, e.g. <c>macos-arm64</c>.</summary>
    public string Rid { get; init; } = "";
    public string Version { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Licence { get; init; } = "";
    public string? Notes { get; init; }
    public IReadOnlyList<FFmpegDownload> Downloads { get; init; } = [];
}

/// <summary>One archive of a build, and which of its entries are the programs.</summary>
public sealed record FFmpegDownload
{
    public string Url { get; init; } = "";
    public string Sha256 { get; init; } = "";

    /// <summary>The exact size of the pinned file; a download that runs past it is stopped.</summary>
    public long Size { get; init; }

    /// <summary>Program name (<c>ffmpeg</c>, <c>ffprobe</c>) to its entry path inside the archive.</summary>
    public IReadOnlyDictionary<string, string> Files { get; init; } = new Dictionary<string, string>();
}
