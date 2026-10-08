using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Common.Media;

namespace KHost.Domain.Services;

public sealed class MediaUploader(
    IMediaAcquisitionService acquisition,
    IMediaFileParsingService parser,
    IMediaService media,
    IMediaRepository repository) : IMediaUploader
{
    private sealed record Kind(IReadOnlyList<string> Extensions, string Folder, long MaxBytes, string Noun);

    // Caps sized to each kind: a print-sized poster, a long mix, a full-length video in high quality.
    private static readonly Dictionary<MediaType, Kind> _kinds = new()
    {
        [MediaType.Image] = new(MediaFormats.ImageExtensions, "Images", 50L * 1024 * 1024, "an image"),
        [MediaType.Video] = new(MediaFormats.VideoExtensions, "Videos", 4L * 1024 * 1024 * 1024, "a video"),
        [MediaType.Audio] = new(MediaFormats.AudioExtensions, "Audio", 500L * 1024 * 1024, "audio"),
    };

    public IReadOnlyList<string> ExtensionsFor(IEnumerable<MediaType> types)
        => [.. types.Distinct().Where(_kinds.ContainsKey).SelectMany(type => _kinds[type].Extensions)];

    public MediaType? TypeFor(string fileName, IEnumerable<MediaType> types)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return types.Where(type => _kinds.TryGetValue(type, out var kind) && kind.Extensions.Contains(extension))
            .Cast<MediaType?>()
            .FirstOrDefault();
    }

    public long MaxBytesFor(MediaType type) => _kinds.TryGetValue(type, out var kind) ? kind.MaxBytes : 0;

    public async Task<Media> AddAsync(string fileName, Stream content, MediaType type, CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(fileName);
        var extension = Path.GetExtension(name);

        if (!_kinds.TryGetValue(type, out var kind) || TypeFor(name, [type]) is null)
            throw new NotSupportedException(kind is null
                ? $"{type} cannot be added from a picked file."
                : $"{name} is not {kind.Noun} the host can play.");

        var directory = Path.Combine(acquisition.MediaDirectory, kind.Folder);
        Directory.CreateDirectory(directory);

        // Streamed to disk, never held in memory: a video can run to gigabytes. Hidden and unique so
        // a half-written file is never mistaken for, or written over, one the library uses.
        var partial = Path.Combine(directory, $".{Guid.NewGuid():N}.part");
        var stem = Path.GetFileNameWithoutExtension(name);
        var path = Path.Combine(directory, name);
        var alreadyOnDisk = false;

        try
        {
            await using (var file = new FileStream(partial, FileMode.CreateNew))
                await content.CopyToAsync(file, cancellationToken);

            for (var copy = 2; File.Exists(path); copy++)
            {
                if (await SameContentAsync(path, partial, cancellationToken))
                {
                    if (await repository.FindByFilePathAsync(path) is { } row)
                        return row;

                    alreadyOnDisk = true;
                    break;
                }

                path = Path.Combine(directory, $"{stem} ({copy}){extension}");
            }

            if (!alreadyOnDisk)
                File.Move(partial, path);
        }
        finally
        {
            File.Delete(partial);
        }

        try
        {
            return await media.CreateAsync(await parser.LoadAndParseAsync(path, type));
        }
        catch when (!alreadyOnDisk)
        {
            // A copy no row points at is clutter nothing would ever clean up.
            File.Delete(path);
            throw;
        }
    }

    private static async Task<bool> SameContentAsync(string first, string second, CancellationToken cancellationToken)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length)
            return false;

        await using var a = File.OpenRead(first);
        await using var b = File.OpenRead(second);
        var left = new byte[81920];
        var right = new byte[81920];

        while (true)
        {
            var read = await a.ReadAtLeastAsync(left, left.Length, throwOnEndOfStream: false, cancellationToken);
            await b.ReadExactlyAsync(right.AsMemory(0, read), cancellationToken);

            if (read == 0)
                return true;

            if (!left.AsSpan(0, read).SequenceEqual(right.AsSpan(0, read)))
                return false;
        }
    }
}
