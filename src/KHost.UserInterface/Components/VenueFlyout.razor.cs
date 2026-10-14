using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

/// <summary>The settings menu's venue switcher.</summary>
public partial class VenueFlyout
{
    [Inject] private IVenuesService VenuesService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    /// <summary>The venues to offer; the menu reads them once for its own row and page list.</summary>
    [Parameter] public IReadOnlyList<Venue> Venues { get; set; } = [];

    [Parameter] public Venue? SelectedVenue { get; set; }

    /// <summary>Raised once a choice is made, so the menu can close.</summary>
    [Parameter] public EventCallback Done { get; set; }

    private async Task SelectVenueAsync(Guid venueId)
    {
        await VenuesService.SelectVenueAsync(venueId);

        await Done.InvokeAsync();
    }

    private async Task EditVenueAsync()
    {
        if (SelectedVenue is not { } venue) return;

        await Done.InvokeAsync();

        await DialogService.RequestEditAsync(venue, async updated =>
        {
            if (updated is not null)
                await VenuesService.UpdateAsync(updated);
        });
    }
}
