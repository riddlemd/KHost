using KHost.Plugins.Sdk.Messaging;
using KHost.UserInterface.Messaging;
using Microsoft.AspNetCore.Components;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components;

public partial class ThemeSelector : IDisposable
{
    [Inject] private IThemeService? ThemeService { get; set; }

    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    protected override void OnInitialized()
    {
        _subscriptions.Add(Broker.Subscribe<ThemeChanged>(_ => OnStateChanged(null, EventArgs.Empty)));
        _subscriptions.Add(Broker.Subscribe<ThemesChanged>(_ => OnStateChanged(null, EventArgs.Empty)));
    }

    private async Task SetThemeAsync(string theme)
    {
        if (ThemeService is not null)
            await ThemeService.SetThemeAsync(theme);
    }

    // A custom theme carries a name of its own; only a built-in is named by its filename.
    private string DisplayName(string? theme)
        => string.IsNullOrEmpty(theme) ? "" : ThemeService?.DisplayNameFor(theme) ?? theme;

    private async void OnStateChanged(object? sender, EventArgs e)
    {
        await InvokeAsync(StateHasChanged);
    }

    public void Dispose() => _subscriptions.Dispose();
}
