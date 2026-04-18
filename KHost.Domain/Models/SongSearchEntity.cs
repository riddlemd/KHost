using KHost.Abstractions.Models;

namespace KHost.Domain.Models;

public record SongSearchEntity : ISongSearchEntity
{
    public required string FilePath { get; init; }
    public required string DisplayName { get; init; }
    public required string Format { get; init; }
}
