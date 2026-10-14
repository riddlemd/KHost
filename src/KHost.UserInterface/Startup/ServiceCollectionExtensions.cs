using KHost.Abstractions.Interactions;
using KHost.Abstractions.Interactions.Requests;
using KHost.Abstractions.Models;
using KHost.UserInterface.Interactions;
using KHost.UserInterface.Interactions.Handlers;
using KHost.UserInterface.Services;
using KHost.UserInterface.Services.RedirectProviders;

namespace KHost.UserInterface.Startup;

/// <summary>The registrations only this host needs — not <c>AddDomain</c>/<c>AddDataAccess</c>/
/// <c>AddPlugins</c>, which a plugin author would also expect to find in <c>ProjectExtensions</c>.</summary>
internal static class ServiceCollectionExtensions
{
    /// <summary>The UI-only services: nothing here is on a plugin's own boundary.</summary>
    internal static void AddUserInterfaceServices(this IServiceCollection services)
    {
        // Scoped, not singleton: a control's pick belongs to the circuit that made it, and a
        // reconnecting browser is a new session rather than one resuming yesterday's choices.
        services.AddScoped<IControlState, ControlState>();
        // Also by its own type: VenueBreakMusicModeMigration needs members kept off the interface.
        services.AddSingleton<AppSettingsService>();
        services.AddSingleton<IAppSettingsService>(sp => sp.GetRequiredService<AppSettingsService>());
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IAppInfoService, AppInfoService>();
        services.AddSingleton<IExternalLinkService, ExternalLinkService>();
        services.AddSingleton<IFileRevealService, FileRevealService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IStartupRedirectProvider, SetupRedirectProvider>();
        services.AddSingleton<IStartupRedirectProvider, CliStartupRedirectProvider>();
    }

    /// <summary>Bridges <see cref="IInteractionDispatcher"/> requests into dialogs.</summary>
    internal static void AddInteractionHandlers(this IServiceCollection services)
    {
        services.AddSingleton<IInteractionDispatcher, DialogInteractionDispatcher>();
        services.AddSingleton<IInteractionHandler<EditMediaRequest, Media?>, EditMediaDialogHandler>();
        services.AddSingleton<IInteractionHandler<ShowLyricsRequest>, ShowLyricsDialogHandler>();
        services.AddSingleton<IInteractionHandler<ShowPluginTableRequest>, ShowPluginTableDialogHandler>();
        services.AddSingleton<IInteractionHandler<ConfirmDuplicateSongRequest, bool>, ConfirmDuplicateSongHandler>();
        services.AddSingleton<IInteractionHandler<TextPromptRequest, IReadOnlyDictionary<string, string>?>, TextPromptDialogHandler>();
    }
}
