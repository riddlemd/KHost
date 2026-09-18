using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.IntegrationTests.Domain.Services;

/// <summary>A prepared store that never has anything, so these tests drive the transcode they are
/// about rather than a copy of a render made earlier.</summary>
internal sealed class NothingPrepared : IPreparedMediaService
{
    public Task ReconcileAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public PerformancePreparation StateFor(string filePath) => PerformancePreparation.Unprepared;

    public bool RequiresPreparation(string filePath) => false;

    public string? TryResolve(string filePath) => null;

    public bool IsWaitingOnARender(string filePath) => false;

    public void Sweep() { }
}
