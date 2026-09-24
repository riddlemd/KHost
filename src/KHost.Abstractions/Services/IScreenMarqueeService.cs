using KHost.Abstractions.Services.IPC;

namespace KHost.Abstractions.Services;

/// <summary>Composes the marquee from the venue and queue; the display asks for it whenever
/// either moves.</summary>
public interface IScreenMarqueeService
{
    /// <summary>Nothing to push: the display draws the marquee whole on every connect and asks
    /// again itself whenever the venue or queue moves.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>What the screens should be showing now. Disabled when the venue has no marquee.</summary>
    Task<SetMarqueeCommand> BuildAsync(CancellationToken cancellationToken = default);
}
