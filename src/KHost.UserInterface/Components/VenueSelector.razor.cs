using Microsoft.AspNetCore.Components;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components;

public partial class VenueSelector : IDisposable
{
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    [Inject] private IVenuesService? VenuesService { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }

    private IReadOnlyList<Venue>? _venues;
    private Venue? _selected;

    protected override async Task OnInitializedAsync()
    {
        if (VenuesService is null) return;

        await RefreshAsync();

        VenuesService.StateChanged += OnStateChanged;
    }

    private async Task SwitchVenueAsync(Guid venueId)
    {
        if (VenuesService is not null)
            await VenuesService.SelectVenueAsync(venueId);
    }

    private async Task OpenEditDialogAsync()
    {
        if (VenuesService is null || DialogService is null || _selected is null) return;

        await DialogService.RequestEditAsync(_selected, async updated =>
        {
            if (updated is not null)
                await VenuesService.UpdateAsync(updated);
        });
    }

    private void GoToVenuesManager() => NavigationManager.NavigateTo("/settings/venues-manager");

    private async Task RefreshAsync()
    {
        if (VenuesService is null) return;

        var result = await VenuesService.ReadAllAsync(pageSize: 1000);

        _venues = result.Items;
        _selected = await VenuesService.ReadSelectedVenueAsync();
    }

    private async void OnStateChanged(object? sender, EventArgs e)
    {
        await RefreshAsync();

        await InvokeAsync(StateHasChanged);
    }

    public void Dispose() => VenuesService?.StateChanged -= OnStateChanged;
}
