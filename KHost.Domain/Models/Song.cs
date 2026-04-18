using KHost.Abstractions.Models;

namespace KHost.Domain.Models;

public class Song : ISong
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string FilePath { get; init; }
    public TimeSpan? Duration { get; init; } = GetRandomDuration();
    public SongStatus Status { get; set; }

    public required string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string Album { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public string Notes { get; set; } = "";

    public DateTimeOffset DateAdded { get; set; } = DateTime.UtcNow;

    public DateTimeOffset DateModified { get; set; } = DateTime.UtcNow;

    private static TimeSpan GetRandomDuration()
        => new(0, 0, 30 + new Random().Next(60 * 4));
}
