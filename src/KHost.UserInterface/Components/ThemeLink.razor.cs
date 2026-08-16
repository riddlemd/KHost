using Microsoft.AspNetCore.Components;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components;

public partial class ThemeLink : IDisposable
{
    [Inject] private IThemeService? ThemeService { get; set; }

    protected override void OnInitialized()
    {
        ThemeService?.StateChanged += OnThemeStateChanged;
    }

    private async void OnThemeStateChanged(object? sender, EventArgs e)
    {
        await InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        ThemeService?.StateChanged -= OnThemeStateChanged;
    }
}
