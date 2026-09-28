using KHost.Common.Monetary;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class TipsManagerPage : IDisposable
{
    [Inject] private ITipsService TipsService { get; set; } = default!;
    [Inject] private IUsersService UsersService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private IVenuesService VenuesService { get; set; } = default!;
    [Inject] private IAppSettingsService AppSettingsService { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private PagedSearch<Tip> _search = default!;
    private Dictionary<Guid, string> _userNames = [];
    private Dictionary<Guid, string> _venueNames = [];

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<TipsChanged>(OnStateChanged));

        _search = new PagedSearch<Tip>(TipsService.SearchAsync) { Size = AppSettingsService.Current.TipsPageSize };

        await LoadUsersAsync();
        await LoadVenuesAsync();
        await _search.SearchAsync();
    }

    private async Task LoadUsersAsync()
    {
        var result = await UsersService.ReadAllAsync(pageSize: 1000);
        _userNames = result.Items.ToDictionary(u => u.Id, u => u.Name);
    }

    private string GetSingerName(Guid userId)
    {
        return _userNames.TryGetValue(userId, out var name) ? name : "Unknown Singer";
    }

    private async Task LoadVenuesAsync()
    {
        var result = await VenuesService.ReadAllAsync(pageSize: 1000);
        _venueNames = result.Items.ToDictionary(v => v.Id, v => v.Name);
    }

    // Old tips predate venue stamping, and a deleted venue leaves its id behind by design.
    private string GetVenueName(Guid? venueId)
        => venueId is { } id && _venueNames.TryGetValue(id, out var name) ? name : "Unknown";

    private async Task OpenAddDialogAsync()
    {
        // Typed parameter: the RequestEditAsync overloads differ only in their model, so a bare
        // null and an untyped lambda cannot pick one.
        await DialogService.RequestEditAsync(null, async (Tip? tip) => await SaveAsync(tip));
    }

    private async Task OpenEditDialogAsync(Tip tip)
    {
        await DialogService.RequestEditAsync(tip, async updated => await SaveAsync(updated));
    }

    private async Task SaveAsync(Tip? tip)
    {
        if (tip is null) return;

        var existing = await TipsService.ReadAsync(tip.Id);
        if (existing is null)
            await TipsService.CreateAsync(tip);
        else
            await TipsService.UpdateAsync(tip);
    }

    // Unconditional: a destructive action must not hinge on which venue is selected.
    private async Task StartDeleteAsync(Tip tip)
    {
        var singer = GetSingerName(tip.UserId);

        await DialogService.ShowConfirmationAsync(
            $"Are you sure you want to delete the tip from <span class=\"kh-emphasis\">{singer}</span> for {tip.AmountInCents.CentsToCurrencyString()}?",
            async () => await TipsService.DeleteAsync(tip.Id),
            "Delete Tip",
            "Delete"
        );
    }

    private void OnStateChanged(TipsChanged message)
        => _ = InvokeAsync(async () =>
        {
            await _search.ReloadClampedAsync();
            StateHasChanged();
        });

    public void Dispose() => _subscriptions.Dispose();
}
