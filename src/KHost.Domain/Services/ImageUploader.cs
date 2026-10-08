using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Common.Media;

namespace KHost.Domain.Services;

public sealed class ImageUploader(
    IMediaAcquisitionService acquisition,
    IMediaFileParsingService parser,
    IMediaService media,
    IMediaRepository repository) : IImageUploader
{
    // A phone photo runs a few MB; this leaves room for a print-sized poster without inviting a video.
    public long MaxBytes => 50 * 1024 * 1024;

    public async Task<Media> AddAsync(string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(fileName);
        var extension = Path.GetExtension(name);

        if (!MediaFormats.IsImage(extension))
            throw new NotSupportedException($"{name} is not a picture the screen can show.");

        var directory = Path.Combine(acquisition.MediaDirectory, "Images");
        Directory.CreateDirectory(directory);

        // Buffered first: the same picture picked twice should find its row, not leave a "(2)" copy.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        var stem = Path.GetFileNameWithoutExtension(name);
        var path = Path.Combine(directory, name);
        var alreadyOnDisk = false;

        for (var copy = 2; File.Exists(path); copy++)
        {
            if ((await File.ReadAllBytesAsync(path, cancellationToken)).AsSpan().SequenceEqual(bytes))
            {
                if (await repository.FindByFilePathAsync(path) is { } row)
                    return row;

                alreadyOnDisk = true;
                break;
            }

            path = Path.Combine(directory, $"{stem} ({copy}){extension}");
        }

        if (!alreadyOnDisk)
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);

        try
        {
            return await media.CreateAsync(await parser.LoadAndParseAsync(path, MediaType.Image));
        }
        catch when (!alreadyOnDisk)
        {
            // A copy no row points at is clutter nothing would ever clean up.
            File.Delete(path);
            throw;
        }
    }
}
