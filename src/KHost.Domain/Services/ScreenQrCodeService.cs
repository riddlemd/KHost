using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QRCoder;

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

    // Resolved on use, never in the constructor, and this is not a style choice — taking
    // IPlaybackService here hangs the app before it logs a line. A plugin is registered once and
    // pointed at every extension interface it implements, so a plugin that shows a QR code and
    // gates playback closes a ring: the plugin needs this service, this service needs playback,
    // playback needs every IMediaPlaybackGate, and one of those is the plugin being built. Nothing
    // asks for a code until long after the graph is up, so the lookup is free by then.
    private readonly IServiceProvider _services;
    private IPlaybackService? _playbackService;

    private IPlaybackService Playback => _playbackService ??= _services.GetRequiredService<IPlaybackService>();
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
        IServiceProvider services,
        IMessageBroker broker)
        : base(logger)
    {
        _screenServer = screenServer;
        _venuesService = venuesService;
        _services = services;

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

        // A venue that wants none gets none, whatever a plugin asks for — and a console with no
        // venue selected has nobody to have chosen, so it is the same answer.
        if (settings is null || !settings.QrCodeEnabled)
            return new SetScreenQrCodesCommand();

        // Nothing is on screen while someone sings, if the venue asked for that.
        if (settings.QrCodeHideDuringSong && Playback.CurrentPerformance is not null)
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

            var (image, modules) = Encode(code.Payload);

            placed.TryAdd(corner, new ScreenQrCodePlacement
            {
                ImageUrl = image,
                Modules = modules,
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

    // Every venue edit and every song republishes the whole set, and the picture only changes when
    // the payload does — encoding a few times a minute for an unchanged string is work for nothing.
    private readonly Dictionary<string, (string Image, int Modules)> _encoded = [];

    /// <summary>
    /// The payload as a picture the screen can draw. SVG rather than pixels: a code sits in a
    /// corner a few centimetres across, where the module edges are the whole of whether a phone
    /// can read it. Correction level L on purpose — the code is on a clean lit panel, not a
    /// printed flyer, and the lowest level spends the fewest modules on a payload of any length.
    /// </summary>
    private (string Image, int Modules) Encode(string payload)
    {
        lock (_encoded)
        {
            if (_encoded.TryGetValue(payload, out var cached))
                return cached;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.L);

        // One unit per module, so the SVG's own coordinates are the module grid and every size the
        // screen asks for is an exact multiple of it.
        var svg = new SvgQRCode(data).GetGraphic(1);
        var encoded = (
            $"data:image/svg+xml;base64,{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg))}",
            data.ModuleMatrix.Count);

        lock (_encoded)
        {
            _encoded[payload] = encoded;
        }

        return encoded;
    }
}
