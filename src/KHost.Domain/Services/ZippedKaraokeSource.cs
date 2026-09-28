using KHost.Abstractions.Services;
using KHost.Common.Media;

namespace KHost.Domain.Services;

/// <summary>Hands the encoder a zipped CD+G pair as the two loose files it reads.</summary>
/// <remarks>ffmpeg cannot read inside a deflated zip, so the pair is written into the stream
/// session's directory at every open and swept with it. Registered by the host ahead of every
/// plugin's source, so a zip is always this one's to answer.</remarks>
public sealed class ZippedKaraokeSource : IPlayableMediaSource
{
    public bool CanResolve(string filePath) => MediaFormats.IsKaraokeArchive(filePath);

    public async Task<string?> ResolvePlayableAsync(
        string filePath,
        string workingDirectory,
        CancellationToken cancellationToken = default)
        => await KaraokeZip.ExtractPairAsync(filePath, workingDirectory, cancellationToken: cancellationToken);
}
