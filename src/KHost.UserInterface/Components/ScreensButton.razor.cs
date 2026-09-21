using Microsoft.AspNetCore.Components;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components;

public partial class ScreensButton : IDisposable
{
    [Inject] private IScreenServer? ScreenServer { get; set; }
    [Inject] private IEnumerable<IDisplayProvider> DisplayProviders { get; set; } = [];
    [Inject] private IEnumerable<IScreenProvider>? ScreenProviders { get; set; }
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private IDisplayProvider? Display => DisplayProviders.FirstOrDefault();

    private readonly SubscriptionSet _subscriptions = new();

    internal int _screenCount;

    /// <summary>Set while a launched screen has not connected yet, so a second press cannot start
    /// a second one during the few seconds the first takes to come up.</summary>
    internal bool _isBusy;

    /// <summary>A provider's device is not a screen, but the room is still watching something.</summary>
    internal bool IsSendingToDevice => Display?.ConnectedDeviceId is { Length: > 0 };

    internal bool IsActive => _screenCount > 0 || IsSendingToDevice;

    /// <summary>The button does one thing, and it says which before it is pressed.</summary>
    internal string Title
    {
        get
        {
            if (_isBusy) return "Opening a screen…";
            if (_screenCount > 0) return _screenCount == 1
                ? "Close the screen"
                : $"Close {_screenCount} screens";

            if (IsSendingToDevice)
            {
                // The provider names itself, so no transport's wording is built in here.
                var device = Display!.Devices.FirstOrDefault(d => d.IsConnected)?.Name ?? Display.Name;
                return $"Open a screen (showing on {device})";
            }

            return "Open a screen";
        }
    }

    protected override async Task OnInitializedAsync()
    {
        ScreenServer!.ScreenConnected += OnScreensChanged;
        ScreenServer.ScreenDisconnected += OnScreensChanged;

        _subscriptions.Add(Broker.Subscribe<DisplaysChanged>(OnDisplaysChanged));

        await RefreshCountAsync();
    }

    // Hub callbacks arrive off the render thread, so re-count and marshal back.
    private void OnScreensChanged(object? sender, ScreenConnectionEventArgs e) =>
        _ = InvokeAsync(RefreshCountAsync);

    // Connecting a device changes no screen, so the colour needs its own trigger.
    private void OnDisplaysChanged(DisplaysChanged message) => _ = InvokeAsync(StateHasChanged);

    private async Task RefreshCountAsync()
    {
        var count = 0;
        await foreach (var _ in ScreenServer!.GetConnectedScreensAsync())
            count++;

        _screenCount = count;

        // The screen it was waiting for arrived, so stop saying "Opening…" without waiting out
        // the timeout the launch armed.
        if (count > 0) _isBusy = false;

        StateHasChanged();
    }

    /// <summary>One press, and which way it goes is whatever the room is doing now: nothing on
    /// screen opens one, a screen already up closes it.</summary>
    /// <remarks>The dialog this used to open is still there for naming a screen, picking roles and
    /// casting; this is the one-screen case, which is nearly every night.</remarks>
    internal async Task ToggleAsync()
    {
        if (_isBusy) return;

        if (_screenCount > 0)
        {
            CloseScreens();
            return;
        }

        await LaunchAsync();
    }

    /// <summary>Only screens this host started; one somebody ran themselves is left alone.</summary>
    private void CloseScreens()
    {
        foreach (var provider in ScreenProviders ?? [])
        {
            try
            {
                provider.CloseSpawnedScreens();
            }
            catch (Exception)
            {
                // One provider failing must not leave the others' screens up.
            }
        }
    }

    private async Task LaunchAsync()
    {
        var provider = ScreenProviders?.FirstOrDefault(p => p.IsAvailable);
        if (provider is null) return;

        _isBusy = true;
        StateHasChanged();

        try
        {
            await provider.LaunchAsync(await NextScreenNameAsync());
        }
        catch (Exception)
        {
            // The process would not start. Nothing is connected, so the button goes back to
            // offering to open one rather than sitting on "Opening…" forever.
            _isBusy = false;
            StateHasChanged();
            return;
        }

        // Cleared when the screen connects, or by the timeout if it never does — a screen that
        // launched but cannot reach the host must not leave the button stuck.
        _ = ClearBusyWhenSettledAsync();
    }

    private async Task ClearBusyWhenSettledAsync()
    {
        for (var waited = 0; waited < LaunchTimeoutSeconds && _screenCount == 0; waited++)
            await Task.Delay(TimeSpan.FromSeconds(1));

        _isBusy = false;
        await InvokeAsync(StateHasChanged);
    }

    private const int LaunchTimeoutSeconds = 15;

    /// <summary>The first "Screen n" nothing is already using.</summary>
    private async Task<string> NextScreenNameAsync()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var screen in ScreenServer!.GetConnectedScreensAsync())
            taken.Add(screen.ScreenId);

        for (var i = 1; ; i++)
        {
            var name = $"Screen {i}";
            if (!taken.Contains(name)) return name;
        }
    }

    public void Dispose()
    {
        _subscriptions.Dispose();

        if (ScreenServer is null) return;

        ScreenServer.ScreenConnected -= OnScreensChanged;
        ScreenServer.ScreenDisconnected -= OnScreensChanged;
    }
}
