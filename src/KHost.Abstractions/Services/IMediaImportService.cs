namespace KHost.Abstractions.Services;

public enum ImportState { Idle, Running, Cancelling }

public interface IMediaImportService : IKHostService
{
    ImportState State { get; }
    int TotalCount { get; }
    int ImportedCount { get; }
    int FailedCount { get; }
    string? CurrentFilePath { get; }
    IReadOnlyList<string> SupportedExtensions { get; }

    Task StartAsync(IEnumerable<string> filePaths);
    void Cancel();
}
