using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class VenuesManagerPage : IDisposable
{
    [Inject] private IVenuesService VenuesService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private IAppSettingsService AppSettingsService { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    // Mirrors EditVenueModel's [MaxLength] so a generated name can't fail validation later.
    private const int NameMaxLength = 32;

    private PagedSearch<Venue> _search = default!;

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<VenuesChanged>(OnStateChanged));

        _search = new PagedSearch<Venue>(VenuesService.SearchAsync) { Size = AppSettingsService.Current.VenuesPageSize };

        await _search.SearchAsync();
    }

    private async Task OpenAddDialogAsync()
    {
        // Null, not a stand-in Venue: the dialog reads Venue is null as Add, which is what starts
        // EditVenueModel on its own defaults — including the default visualisation playlist.
        await DialogService.RequestEditAsync(null, async (Venue? venue) => await SaveAsync(venue));
    }

    private async Task OpenEditDialogAsync(Venue venue)
    {
        await DialogService.RequestEditAsync(venue, async updated => await SaveAsync(updated));
    }

    private async Task CloneAsync(Venue venue)
    {
        var taken = (await VenuesService.ReadAllAsync(pageSize: 1000)).Items
            .Select(v => v.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await VenuesService.CreateAsync(venue.CloneAs(BuildCopyName(venue.Name, taken)));
    }

    private static string BuildCopyName(string baseName, HashSet<string> taken)
    {
        for (var attempt = 1; attempt <= 1000; attempt++)
        {
            var suffix = attempt == 1 ? " (copy)" : $" (copy {attempt})";

            // Trim the stem, not the suffix. An over-length name fails the editor's validation
            // the moment someone opens the clone.
            var stem = baseName.Length + suffix.Length > NameMaxLength
                ? baseName[..Math.Max(0, NameMaxLength - suffix.Length)].TrimEnd()
                : baseName;

            var candidate = stem + suffix;

            if (!taken.Contains(candidate))
                return candidate;
        }

        return $"{Guid.NewGuid()}"[..NameMaxLength];
    }

    private async Task SaveAsync(Venue? venue)
    {
        if (venue is null) return;

        var existing = await VenuesService.ReadAsync(venue.Id);
        if (existing is null)
            await VenuesService.CreateAsync(venue);
        else
            await VenuesService.UpdateAsync(venue);
    }

    private async Task StartDeleteAsync(Venue venue)
    {
        await DialogService.ShowConfirmationAsync(
            $"Are you sure you want to delete <span class=\"kh-emphasis\">{venue.Name}</span>?",
            async () => await VenuesService.DeleteAsync(venue.Id),
            "Delete Venue",
            "Delete"
        );
    }

    private void OnStateChanged(VenuesChanged message)
        => _ = InvokeAsync(async () =>
        {
            await _search.ReloadClampedAsync();
            StateHasChanged();
        });

    public void Dispose() => _subscriptions.Dispose();
}
