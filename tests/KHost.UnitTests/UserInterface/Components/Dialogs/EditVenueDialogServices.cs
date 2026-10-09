using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>What EditVenueDialog injects beyond its sections, for fixtures testing something else on it.</summary>
internal static class EditVenueDialogServices
{
    /// <summary>App settings reading <paramref name="current"/>, every default when null.</summary>
    public static IAppSettingsService AddAppSettings(this IServiceCollection services, AppSettings? current = null)
    {
        var settings = Substitute.For<IAppSettingsService>();
        settings.Current.Returns(current ?? new AppSettings());
        services.AddSingleton(settings);
        return settings;
    }
}
