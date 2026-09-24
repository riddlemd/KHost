using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Screens;

/// <summary>Names the break music in a corner of the screen; built whole so a reconnect is right.</summary>
/// <remarks>Says what is playing, not what is cued: pause and hand-off both take it down. The
/// display asks for it on each change it hears, so nothing here subscribes.</remarks>
public sealed class BreakMusicCardService : BaseService, IBreakMusicCardService
{
    /// <summary>Bottom-left, away from the code's default corner, so the two stack when unset.</summary>
    private const ScreenCorner DefaultCorner = ScreenCorner.BottomLeft;

    /// <summary>The code's own default: an inset belongs to the corner, not to what sits in it.</summary>
    private const double DefaultOffset = 0.2;

    private readonly IVenuesService _venuesService;

    // Resolved on use, never in the constructor. Taking it there would close the same DI ring
    // ScreenQrCodeService documents, since a plugin is one instance across every extension interface.
    private readonly IServiceProvider _services;
    private IBreakMusicService? _breakMusic;

    private IBreakMusicService BreakMusic => _breakMusic ??= _services.GetRequiredService<IBreakMusicService>();

    public BreakMusicCardService(
        ILogger<BreakMusicCardService> logger,
        IVenuesService venuesService,
        IServiceProvider services)
        : base(logger)
    {
        _venuesService = venuesService;
        _services = services;
    }

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
}
