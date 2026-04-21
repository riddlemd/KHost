namespace KHost.Abstractions.Models;

public enum MediaStatus { Unknown, Ready, Downloading, Processing, Broken }

public class Media
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string FilePath { get; set; }
    public TimeSpan? Duration { get; set; }
    public MediaStatus Status { get; set; }

    public required string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public string Notes { get; set; } = "";

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}
