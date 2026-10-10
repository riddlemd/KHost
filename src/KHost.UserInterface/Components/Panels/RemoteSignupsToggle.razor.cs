using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Panels;

/// <summary>The selected venue's <see cref="Venue.VenueSettings.AllowGuestRemote"/>, one click from
/// the singer queue: closes or reopens remote sign-ups mid-show.</summary>
/// <remarks>Saved on the venue itself, so the Edit Venue dialog shows the same answer, and a guest
/// room hears it through the announcement the save leads to.</remarks>
public partial class RemoteSignupsToggle : IDisposable
{
    [Inject] private IVenuesService Venues { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private Venue? _venue;
    private bool _open;
    private bool _saving;

    private string Title => _open
        ? "Remote sign-ups: open — click to close"
        : "Remote sign-ups: closed — click to open";

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<SelectedVenueChanged>(_ => InvokeAsync(async () =>
        {
            await ReadVenueAsync();
            StateHasChanged();
        })));

        await ReadVenueAsync();
    }

    private async Task ReadVenueAsync()
    {
        _venue = await Venues.ReadSelectedVenueAsync();
        _open = _venue?.Settings.AllowGuestRemote ?? false;
    }

    private async Task ToggleAsync()
    {
        // Read again rather than saving the copy held since the last announcement: the Edit Venue
        // dialog may have saved other settings on it since.
        if (await Venues.ReadSelectedVenueAsync() is not { } venue) return;

        venue.Settings.AllowGuestRemote = !_open;

        _saving = true;
        try
        {
            await Venues.UpdateAsync(venue);
            _venue = venue;
            _open = venue.Settings.AllowGuestRemote;
        }
        catch (Exception)
        {
            Flash.Show("Remote sign-ups could not be changed.", FlashType.Warning);
        }
        finally
        {
            _saving = false;
        }
    }

    public void Dispose() => _subscriptions.Dispose();
}
