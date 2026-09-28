using KHost.Abstractions.Models.Backgrounds;
using KHost.Abstractions.Services;

namespace KHost.Domain.Services.Backgrounds;

#pragma warning disable CS0618 // registered only so a plugin that still takes the retired service loads
/// <summary>The retired background packs, answering that there are none.</summary>
/// <remarks>Registered so a plugin whose constructor takes <see cref="IBackgroundPackService"/> is
/// still built; nothing in the host asks it anything.</remarks>
public sealed class NoBackgroundPackService : IBackgroundPackService
{
    public Task<BackgroundPack> ReadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new BackgroundPack { Problem = BackgroundPackProblem.NoFolderSet });
}
#pragma warning restore CS0618
