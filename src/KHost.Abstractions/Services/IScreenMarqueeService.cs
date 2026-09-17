using KHost.Abstractions.Services.IPC;

namespace KHost.Abstractions.Services;

/// <summary>Composes the marquee from the venue and queue, pushing it whenever either moves.</summary>
public interface IScreenMarqueeService
{
    /// <summary>Sends the current marquee to every connected screen.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>What the screens should be showing now. Disabled when the venue has no marquee.</summary>
    Task<SetMarqueeCommand> BuildAsync(CancellationToken cancellationToken = default);
}
