using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <summary>
/// Holds the QR codes the screens should be showing and keeps them in step. Host-side state rather
/// than a command a caller fires and forgets: a screen that reconnects mid-show has to be told
/// again, and nothing else knows what was up.
/// </summary>
public sealed class ScreenQrCodeService : BaseService, IScreenQrCodeService, IDisposable
{
    private readonly IScreenServer _screenServer;
    private readonly IVenuesService _venuesService;
    private readonly IPlaybackService _playback;
    private readonly SubscriptionSet _subscriptions = new();

    // Singleton, so the codes are guarded rather than assumed single-threaded: a plugin shows one
    // off its own task while a venue edit is recomposing.
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>By owner, with the order they claimed in — the newest claim wins a contested corner.</summary>
    private readonly Dictionary<string, (long Claim, ScreenQrCode Code)> _codes = [];

    private long _claims;

    public ScreenQrCodeService(
        ILogger<ScreenQrCodeService> logger,
        IScreenServer screenServer,
        IVenuesService venuesService,
        IPlaybackService playback,
        IMessageBroker broker)
        : base(logger)
    {
        _screenServer = screenServer;
        _venuesService = venuesService;
        _playback = playback;

        // The venue owns where these sit and how big they are, and whether a song hides them.
        _subscriptions.Add(broker.Subscribe<SelectedVenueChanged>(_ => Republish()));

        // Only matters to a venue that hides them during a song, but the service cannot know that
        // without reading the venue, which is what republishing does anyway.
        _subscriptions.Add(broker.Subscribe<PlaybackChanged>(_ => Republish()));

        _screenServer.ScreenConnected += OnScreenConnected;
    }

    public async Task ShowAsync(ScreenQrCode code)
    {
        await _lock.WaitAsync();
        try
        {
            _codes[code.OwnerId] = (++_claims, code);
        }
        finally
        {
            _lock.Release();
        }

        Logger.LogInformation("Showing a QR code for {OwnerId}", code.OwnerId);

        await BroadcastAsync();
    }

    public async Task HideAsync(string ownerId)
    {
        bool removed;

        await _lock.WaitAsync();
        try
        {
            removed = _codes.Remove(ownerId);
        }
        finally
        {
            _lock.Release();
        }

        // A caller taking down what it never put up is not worth a broadcast, nor a complaint:
        // hiding on the way out is the right shape even when nothing was shown.
        if (!removed)
            return;

        Logger.LogInformation("Took down {OwnerId}'s QR code", ownerId);

        await BroadcastAsync();
    }

    public async Task<SetScreenQrCodesCommand> BuildAsync(CancellationToken cancellationToken = default)
    {
        var settings = (await _venuesService.ReadSelectedVenueAsync())?.Settings;

        // Nothing is on screen while someone sings, if the venue asked for that.
        if (settings?.QrCodeHideDuringSong == true && _playback.CurrentPerformance is not null)
            return new SetScreenQrCodesCommand();

        List<(long Claim, ScreenQrCode Code)> claims;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            claims = [.. _codes.Values];
        }
        finally
        {
            _lock.Release();
        }

        var placed = new Dictionary<ScreenCorner, ScreenQrCodePlacement>();

        // Newest first, so TryAdd leaves a contested corner to the most recent claim and the one
        // it displaced simply comes down.
        foreach (var (_, code) in claims.OrderByDescending(entry => entry.Claim))
        {
            var corner = code.Corner ?? settings?.QrCodeCorner ?? ScreenCorner.BottomRight;

            placed.TryAdd(corner, new ScreenQrCodePlacement
            {
                ImageUrl = code.ImageUrl,
                Caption = code.Caption,
                Corner = corner,
                Size = code.Size ?? settings?.QrCodeSize ?? ScreenQrSize.Medium,
            });
        }

        return new SetScreenQrCodesCommand
        {
            // Ordered so two screens drawing the same set agree, and a test can read it.
            Codes = [.. placed.Values.OrderBy(placement => placement.Corner)],
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
            // A code that fails to reach the screens must not take the show down with it.
            Logger.LogWarning(ex, "Failed to send QR codes to screens");
        }
    }

    // ScreenConnected arrives on the hub thread already holding a lock, so nothing here may be
    // awaited on it.
    private void OnScreenConnected(object? sender, ScreenConnectionEventArgs e) => _ = Task.Run(async () =>
    {
        try
        {
            // Sent even when there is nothing up: it is the whole state, so it also clears a code
            // left on a screen that dropped and came back.
            await _screenServer.SendCommandAsync(e.Connection.ScreenId, await BuildAsync());
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to send QR codes to screen {ScreenId}", e.Connection.ScreenId);
        }
    });

    private void Republish() => _ = Task.Run(() => BroadcastAsync());
}
