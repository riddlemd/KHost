using KHost.Abstractions.Messaging;
using KHost.UserInterface.Messaging;
using Microsoft.AspNetCore.Components;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components;

public partial class ThemeLink : IDisposable
{
    [Inject] private IThemeService? ThemeService { get; set; }

    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    protected override void OnInitialized()
    {
        _subscriptions.Add(Broker.Subscribe<ThemeChanged>(_ => OnThemeStateChanged(null, EventArgs.Empty)));
    }

    private void OnThemeStateChanged(object? sender, EventArgs e)
        => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => _subscriptions.Dispose();
}
