using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Panels;

/// <summary>The App Setting for colour-blind friendly lyrics, one click from the song.</summary>
/// <remarks>Saved through the same settings as the App Settings page and read back from them, so the
/// two never disagree, and a second console follows on the announcement a save leads to.</remarks>
public partial class ColorBlindLyricsToggle : IDisposable
{
    [Inject] private IAppSettingsService AppSettings { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private bool _on;
    private bool _saving;

    private string Title => _on
        ? "Colour-blind friendly lyrics: on — click to turn off"
        : "Colour-blind friendly lyrics: off — click to turn on";

    protected override void OnInitialized()
    {
        _on = AppSettings.Current?.ColorBlindFriendlyLyrics ?? false;

        _subscriptions.Add(Broker.Subscribe<TimedLyricsSettingsChanged>(_ => InvokeAsync(() =>
        {
            _on = AppSettings.Current?.ColorBlindFriendlyLyrics ?? _on;
            StateHasChanged();
        })));
    }

    private async Task ToggleAsync()
    {
        // The whole snapshot goes back, as the settings page sends it, with this one value moved.
        if (AppSettings.Current is not { } settings) return;

        settings.ColorBlindFriendlyLyrics = !_on;

        _saving = true;
        try
        {
            var result = await AppSettings.SaveAsync(settings);

            if (result.Saved)
                _on = settings.ColorBlindFriendlyLyrics;
            else
                Flash.Show(result.Error ?? "Colour-blind friendly lyrics could not be saved.", FlashType.Warning);
        }
        finally
        {
            _saving = false;
        }
    }

    public void Dispose() => _subscriptions.Dispose();
}
