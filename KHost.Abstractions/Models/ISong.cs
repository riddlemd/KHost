namespace KHost.Abstractions.Models;

public interface ISong
{
    Guid Id { get; }
    string FilePath { get; }
    TimeSpan? Duration { get; }
    SongStatus Status { get; set; }
    string Title { get; set; }
    string Artist { get; set; }
    string Album { get; set; }
    string Format { get; set; }
    string Notes { get; set; }
    DateTimeOffset DateAdded { get; }
    DateTimeOffset DateModified { get; set; }
}

public enum SongStatus { Unknown, Ready, Downloading, Broken }
