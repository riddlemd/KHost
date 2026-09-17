using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace KHost.Domain.Services.Screens;

/// <summary>Holds what every owner is offering and draws the one the venue picked.</summary>
/// <remarks>Registering and showing stay separate, so a switch mid-show draws instantly.</remarks>
public sealed class ScreenQrCodeService : BaseService, IScreenQrCodeService, IDisposable, IStartsWithTheHost
{
    private readonly IScreenServer _screenServer;
    private readonly IVenuesService _venuesService;

    // Resolved on use, never in the constructor: taking IPlaybackService there closes a DI ring when a
    // plugin both shows a QR code and gates playback (this service -> playback -> the plugin's own gate).
    private readonly IServiceProvider _services;
    private IPlaybackService? _playbackService;

    private IPlaybackService Playback => _playbackService ??= _services.GetRequiredService<IPlaybackService>();
    private readonly SubscriptionSet _subscriptions = new();

    // Singleton, so the codes are guarded rather than assumed single-threaded: a plugin shows one
    // off its own task while a venue edit is recomposing.
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>What each owner is currently offering. At most one of them is ever drawn.</summary>
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

    public async Task RegisterAsync(ScreenQrCode code)
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

        Logger.LogInformation("{OwnerId} registered a QR code", code.OwnerId);

        await BroadcastAsync();
    }

    public async Task UnregisterAsync(string ownerId)
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

        // A caller withdrawing what it never registered is not worth a broadcast, nor a
        // complaint: unregistering on the way out is the right shape either way.
        if (!removed)
            return;

        Logger.LogInformation("{OwnerId} withdrew its QR code", ownerId);

        await BroadcastAsync();
    }

    public async Task<SetScreenQrCodesCommand> BuildAsync(CancellationToken cancellationToken = default)
    {
        var settings = (await _venuesService.ReadSelectedVenueAsync())?.Settings;

        // Nothing named is nothing shown, and that's the default: a code goes up because someone chose it,
        // not because a plugin happened to register one.
        if (settings?.QrCodeSource is not { Length: > 0 } source)
            return new SetScreenQrCodesCommand();

        // Nothing is on screen while someone sings, if the venue asked for that.
        if (settings.QrCodeHideDuringSong && Playback.CurrentPerformance is not null)
            return new SetScreenQrCodesCommand();

        ScreenQrCode? chosen;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Every other owner's code is held, not drawn. They keep registering against a venue
            // that may pick them later, and nothing has to be told it was passed over.
            chosen = _codes.TryGetValue(source, out var entry) ? entry.Code : null;
        }
        finally
        {
            _lock.Release();
        }

        // The chosen source has nothing to offer yet: a plugin that is not signed in, or one that
        // withdrew. The venue's choice stands; there is simply nothing to draw against it.
        if (chosen is null)
            return new SetScreenQrCodesCommand();

        var (image, modules) = Encode(chosen.Payload);

        return new SetScreenQrCodesCommand
        {
            Codes =
            [
                new ScreenQrCodePlacement
                {
                    ImageUrl = image,
                    Modules = modules,
                    Caption = chosen.Caption,

                    // The venue's, with no way for a plugin to say otherwise: it is the venue's
                    // screen, and the host knows what else is on it.
                    Corner = settings.QrCodeCorner ?? ScreenCorner.BottomRight,
                    Size = settings.QrCodeSize ?? ScreenQrSize.Medium,

                    // Resolved here, not on the screen: a venue never asked stores zero, which means
                    // "screen decides" rather than "none".
                    SafeZone = settings.QrCodeSafeZone is > 0 and var zone ? zone : DefaultSafeZone,
                    Offset = settings.QrCodeOffset is > 0 and var offset ? offset : DefaultOffset,
                },
            ],
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
    // the payload does. Encoding a few times a minute for an unchanged string is work for nothing.
    private readonly Dictionary<string, (string Image, int Modules)> _encoded = [];

    /// <summary>One module of white: the standard four-module border reads as a slab over video.</summary>
    private const int DefaultSafeZone = 1;

    /// <summary>Almost flush, as a fraction of the shorter side: overscan crops a flush edge.</summary>
    private const double DefaultOffset = 0.2;

    /// <summary>Encodes the payload as an SVG at the lowest correction level, fewer modules.</summary>
    private (string Image, int Modules) Encode(string payload)
    {
        lock (_encoded)
        {
            if (_encoded.TryGetValue(payload, out var cached))
                return cached;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.L);

        // One unit per module, so every size the screen asks for is an exact multiple of the grid.
        // No quiet zone drawn in: the screen paints that margin, so a corner can be as tight as it likes.
        var svg = new SvgQRCode(data).GetGraphic(1, "#000000", "#ffffff", drawQuietZones: false);

        // ModuleMatrix counts the quiet zone whether or not it is drawn, so the eight rows and
        // columns of it come off: what the screen sizes against has to be what is in the picture.
        var encoded = (
            $"data:image/svg+xml;base64,{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg))}",
            data.ModuleMatrix.Count - 8);

        lock (_encoded)
        {
            _encoded[payload] = encoded;
        }

        return encoded;
    }
}
