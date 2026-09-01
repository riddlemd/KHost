using KHost.Abstractions.Services.IPC;

namespace KHost.Abstractions.Services;

/// <summary>
/// Composes the screens' marquee from the venue's settings and the queue's order, and pushes it
/// whenever either moves. A plugin that wants to know what the room is being told can build the
/// same command without waiting for one to be sent.
/// </summary>
public interface IScreenMarqueeService
{
    /// <summary>Sends the current marquee to every connected screen.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>What the screens should be showing now. Disabled when the venue has no marquee.</summary>
    Task<SetMarqueeCommand> BuildAsync(CancellationToken cancellationToken = default);
}
