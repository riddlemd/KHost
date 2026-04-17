namespace KHost.UserInterface.Models;

public class Song
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string FilePath { get; init; }
    public required string DisplayName { get; init; }
    public TimeSpan? Duration { get; init; } = GetRandomDuration();

    private static TimeSpan GetRandomDuration()
        => new(0, 0, 30 + new Random().Next(60 * 4));
}
