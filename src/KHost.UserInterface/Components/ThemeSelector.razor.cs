using Microsoft.AspNetCore.Components;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components;

public partial class ThemeSelector : IDisposable
{
    [Inject] private IThemeService? ThemeService { get; set; }

    protected override void OnInitialized()
    {
        ThemeService?.StateChanged += OnStateChanged;
    }

    private async Task SetThemeAsync(string theme)
    {
        if (ThemeService is not null)
            await ThemeService.SetThemeAsync(theme);
    }

    private static string DisplayName(string? theme)
        => string.IsNullOrEmpty(theme) ? "" : char.ToUpper(theme[0]) + theme[1..];

    private async void OnStateChanged(object? sender, EventArgs e)
    {
        await InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        ThemeService?.StateChanged -= OnStateChanged;
    }
}
