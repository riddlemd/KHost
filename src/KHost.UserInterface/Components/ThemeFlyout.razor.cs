using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

/// <summary>The settings menu's theme switcher.</summary>
public partial class ThemeFlyout
{
    [Inject] private IThemeService ThemeService { get; set; } = default!;

    /// <summary>Raised once a choice is made, so the menu can close.</summary>
    [Parameter] public EventCallback Done { get; set; }

    // A custom theme carries a name of its own; only a built-in is named by its filename.
    internal static string NameOf(IThemeService themes, string? theme)
        => string.IsNullOrEmpty(theme) ? "" : themes.DisplayNameFor(theme);

    private async Task SelectThemeAsync(string theme)
    {
        await ThemeService.SetThemeAsync(theme);

        await Done.InvokeAsync();
    }
}
