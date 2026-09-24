using KHost.Abstractions.Messaging;
using KHost.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.QrCodes;

/// <summary>Holds what every owner is offering and answers with the one the venue picked.</summary>
/// <remarks>Registering and showing stay separate, so a switch mid-show is instant. A display asks
/// for the offer on each change it hears and owns how it is drawn; this only says when it moved.</remarks>
public sealed class QrCodeService : BaseService, IQrCodeService
{
    private readonly IMessageBroker _broker;
    private readonly IVenuesService _venuesService;

    // Resolved on use, never in the constructor: taking IPlaybackService there closes a DI ring when a
    // plugin both shows a QR code and gates playback (this service -> playback -> the plugin's own gate).
    private readonly IServiceProvider _services;
    private IPlaybackService? _playbackService;

    private IPlaybackService Playback => _playbackService ??= _services.GetRequiredService<IPlaybackService>();

    // Singleton, so the codes are guarded rather than assumed single-threaded: a plugin shows one
    // off its own task while a venue edit is recomposing.
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>What each owner is currently offering. At most one of them is ever offered.</summary>
    private readonly Dictionary<string, (long Claim, QrCodeRegistration Code)> _codes = [];

    private long _claims;

    public QrCodeService(
        ILogger<QrCodeService> logger,
        IVenuesService venuesService,
        IServiceProvider services,
        IMessageBroker broker)
        : base(logger)
    {
        _broker = broker;
        _venuesService = venuesService;
        _services = services;
    }

    public async Task RegisterAsync(QrCodeRegistration code)
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

        // Awaited, so a caller registering during a show knows every display has been told.
        await _broker.PublishAsync(new QrCodeOfferChanged());
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

        // A caller withdrawing what it never registered is not worth a redraw, nor a
        // complaint: unregistering on the way out is the right shape either way.
        if (!removed)
            return;

        Logger.LogInformation("{OwnerId} withdrew its QR code", ownerId);

        await _broker.PublishAsync(new QrCodeOfferChanged());
    }

    public async Task<QrCodeOffer?> ReadOfferAsync(CancellationToken cancellationToken = default)
    {
        var settings = (await _venuesService.ReadSelectedVenueAsync())?.Settings;

        // Nothing named is nothing shown, and that's the default: a code goes up because someone chose it,
        // not because a plugin happened to register one.
        if (settings?.QrCodeSource is not { Length: > 0 } source)
            return null;

        // Nothing is on screen while someone sings, if the venue asked for that.
        if (settings.QrCodeHideDuringSong && Playback.CurrentPerformance is not null)
            return null;

        QrCodeRegistration? chosen;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Every other owner's code is held, not shown. They keep registering against a venue
            // that may pick them later, and nothing has to be told it was passed over.
            chosen = _codes.TryGetValue(source, out var entry) ? entry.Code : null;
        }
        finally
        {
            _lock.Release();
        }

        // The chosen source has nothing to offer yet: a plugin that is not signed in, or one that
        // withdrew. The venue's choice stands; there is simply nothing to show against it.
        if (chosen is null)
            return null;

        return new QrCodeOffer
        {
            Payload = chosen.Payload,
            Caption = chosen.Caption,

            // The venue's, with no way for a plugin to say otherwise: it is the venue's room.
            Corner = settings.QrCodeCorner,
            Size = settings.QrCodeSize,

            // A venue never asked stores zero, which means "no preference" rather than "none".
            SafeZone = settings.QrCodeSafeZone > 0 ? settings.QrCodeSafeZone : null,
            Offset = settings.QrCodeOffset > 0 ? settings.QrCodeOffset : null,
        };
    }
}
