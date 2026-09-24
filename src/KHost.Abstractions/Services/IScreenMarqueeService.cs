using KHost.Abstractions.Services.IPC;

namespace KHost.Abstractions.Services;

/// <summary>Composes the marquee from the venue and queue; the display asks for it whenever
/// either moves.</summary>
/// <remarks>A display provider that draws a marquee TAKES this and calls <see cref="BuildAsync"/>
/// on connect and again whenever it hears
/// <see cref="KHost.Abstractions.Messaging.Messages.SelectedVenueChanged"/>,
/// <see cref="KHost.Abstractions.Messaging.Messages.SingerQueueChanged"/>,
/// <see cref="KHost.Abstractions.Messaging.Messages.PerformancesChanged"/> or
/// <see cref="KHost.Abstractions.Messaging.Messages.PlaybackChanged"/>. This service holds no
/// subscriptions and announces nothing: when and how the marquee is drawn is the display's. A host
/// singleton, callable from any thread.</remarks>
public interface IScreenMarqueeService
{
    /// <summary>Nothing to push: the display draws the marquee whole on every connect and asks
    /// again itself whenever the venue or queue moves.</summary>
    /// <remarks>Does nothing; no caller needs it.</remarks>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>What the screens should be showing now. Disabled when the venue has no marquee.</summary>
    /// <remarks>The whole marquee, to replace whatever was drawn: the venue's message and styling,
    /// and the next singers in queue order — leaving out whoever is singing now — each with their
    /// next song when they have one queued.</remarks>
    Task<SetMarqueeCommand> BuildAsync(CancellationToken cancellationToken = default);
}
