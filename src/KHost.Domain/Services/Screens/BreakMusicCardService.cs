using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Screens;

/// <summary>Names the break music in a corner of the screen; pushed whole so a reconnect is right.</summary>
/// <remarks>Says what is playing, not what is cued: pause and hand-off both take it down.</remarks>
public sealed class BreakMusicCardService : BaseService, IDisposable, IStartsWithTheHost
{
    /// <summary>Bottom-left, away from the code's default corner, so the two stack when unset.</summary>
    private const ScreenCorner DefaultCorner = ScreenCorner.BottomLeft;

    /// <summary>The code's own default: an inset belongs to the corner, not to what sits in it.</summary>
    private const double DefaultOffset = 0.2;

    private readonly IScreenServer _screenServer;
    private readonly IVenuesService _venuesService;
    private readonly SubscriptionSet _subscriptions = new();

    // Resolved on use, never in the constructor. Taking it there would close the same DI ring
    // ScreenQrCodeService documents, since a plugin is one instance across every extension interface.
    private readonly IServiceProvider _services;
    private IBreakMusicService? _breakMusic;

    private IBreakMusicService BreakMusic => _breakMusic ??= _services.GetRequiredService<IBreakMusicService>();

    public BreakMusicCardService(
        ILogger<BreakMusicCardService> logger,
        IScreenServer screenServer,
        IVenuesService venuesService,
        IServiceProvider services,
        IMessageBroker broker)
        : base(logger)
    {
        _screenServer = screenServer;
        _venuesService = venuesService;
        _services = services;

        // Starting, pausing, stopping, and yielding to a singer all land here.
        _subscriptions.Add(broker.Subscribe<BreakMusicChanged>(_ => Republish()));

        // A provider moving to the next track on its own says so separately: the state did not
        // change, only what is playing under it.
        _subscriptions.Add(broker.Subscribe<BreakMusicTrackChanged>(_ => Republish()));

        // The venue owns whether the card is on at all and which corner it takes.
        _subscriptions.Add(broker.Subscribe<SelectedVenueChanged>(_ => Republish()));

        _screenServer.ScreenConnected += OnScreenConnected;
    }

    /// <summary>Pushes the card's state once on the way up; resolving it is what subscribes it.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => BroadcastAsync(cancellationToken);

    public async Task<SetBreakMusicCardCommand> BuildAsync(CancellationToken cancellationToken = default)
    {
        var settings = (await _venuesService.ReadSelectedVenueAsync())?.Settings;

        // A venue that wants none gets none, and a console with no venue selected has nobody to
        // have asked, which is the same answer.
        if (settings is null || !settings.BreakMusicCardEnabled)
            return new SetBreakMusicCardCommand { Enabled = false };

        // Playing only: Paused and Suspended both mean the room is hearing something else, and a
        // card naming a track nobody can hear is worse than no card.
        if (BreakMusic.State != BreakMusicState.Playing || BreakMusic.CurrentTrack is not { } track)
            return new SetBreakMusicCardCommand { Enabled = false };

        // A provider that reports no title has nothing worth a corner of the picture.
        if (string.IsNullOrWhiteSpace(track.Title))
            return new SetBreakMusicCardCommand { Enabled = false };

        return new SetBreakMusicCardCommand
        {
            Enabled = true,
            Title = track.Title,

            // Blank rather than null where the provider could not say, so the screen draws one
            // line instead of a gap it has to reason about.
            Artist = string.IsNullOrWhiteSpace(track.Artist) ? null : track.Artist,
            Corner = settings.BreakMusicCardCorner ?? DefaultCorner,

            // Resolved here rather than on the screen, which decides nothing. It is the codes'
            // setting because the inset belongs to the corner: everything stacked there shares it.
            Offset = settings.QrCodeOffset is > 0 and var offset ? offset : DefaultOffset,
        };
    }

    public void Dispose()
    {
        _screenServer.ScreenConnected -= OnScreenConnected;
        _subscriptions.Dispose();
    }

    private async Task BroadcastAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _screenServer.BroadcastCommandAsync(await BuildAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            // A caption that fails to reach the screens must not take the show down with it.
            Logger.LogWarning(ex, "Failed to send the break music card to screens");
        }
    }

    // ScreenConnected arrives on the hub thread already holding a lock, so nothing here may be
    // awaited on it.
    private void OnScreenConnected(object? sender, ScreenConnectionEventArgs e) => _ = Task.Run(async () =>
    {
        try
        {
            // Sent even when there is nothing to say: it is the whole state, so it also clears a
            // card left on a screen that dropped and came back.
            await _screenServer.SendCommandAsync(e.Connection.ScreenId, await BuildAsync());
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to send the break music card to screen {ScreenId}", e.Connection.ScreenId);
        }
    });

    private void Republish() => _ = Task.Run(() => BroadcastAsync());
}
