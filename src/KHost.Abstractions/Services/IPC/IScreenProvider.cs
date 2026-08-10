namespace KHost.Abstractions.Services.IPC;

public interface IScreenProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    Task LaunchAsync(string screenId, CancellationToken cancellationToken = default);
}
