using KHost.Abstractions.Services;
using KHost.DataAccess.Services;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Startup;

/// <summary>Carries the selected venue's old break music mode into App Settings, so an install from
/// before the mode moved there keeps playing what it played.</summary>
/// <remarks>Runs after the venue is restored and before break music resolves its provider. Does
/// nothing once App Settings names a mode, so a later run, or a mode the host has since picked, is
/// left alone.</remarks>
internal sealed class VenueBreakMusicModeMigration
{
    private readonly AppSettingsService _appSettings;
    private readonly IVenuesService _venues;
    private readonly IRetiredVenueSettingsReader _retiredSettings;
    private readonly ILogger<VenueBreakMusicModeMigration> _logger;

    public VenueBreakMusicModeMigration(
        AppSettingsService appSettings,
        IVenuesService venues,
        IRetiredVenueSettingsReader retiredSettings,
        ILogger<VenueBreakMusicModeMigration> logger)
    {
        _appSettings = appSettings;
        _venues = venues;
        _retiredSettings = retiredSettings;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        if (_appSettings.BreakMusicProviderSaved)
            return;

        if (_venues.SelectedVenueId is not { } venueId)
            return;

        // Unset stays unset, so the built-in default applies rather than being pinned into the overlay.
        if (await _retiredSettings.ReadBreakMusicProviderAsync(venueId) is not { } provider)
            return;

        await _appSettings.SaveBreakMusicProviderAsync(provider);

        _logger.LogInformation("Break music mode {Provider} carried from venue {VenueId} into App Settings", provider, venueId);
    }
}
