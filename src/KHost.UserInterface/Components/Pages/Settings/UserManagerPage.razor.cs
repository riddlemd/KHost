using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class UserManagerPage : IDisposable
{
    [Inject] private IUsersService UsersService { get; set; } = default!;
    [Inject] private ITipsService TipsService { get; set; } = default!;
    [Inject] private IFlashService? Flash { get; set; }
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private IVenuesService VenuesService { get; set; } = default!;
    [Inject] private IAppSettingsService AppSettingsService { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private PagedSearch<KHostUser> _search = default!;
    private Dictionary<Guid, int> _tipTotals = [];

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<UsersChanged>(_ => OnStateChanged()));
        _subscriptions.Add(Broker.Subscribe<TipsChanged>(_ => OnStateChanged()));

        _search = new PagedSearch<KHostUser>(UsersService.SearchAsync)
        {
            Size = AppSettingsService.Current.UsersPageSize,
            OnSearched = RefreshTipTotalsAsync,
        };

        await _search.SearchAsync();
    }

    private async Task RefreshTipTotalsAsync(PaginatedResult<KHostUser> result)
    {
        _tipTotals = [];
        foreach (var user in result.Items)
            _tipTotals[user.Id] = await TipsService.GetTotalInCentsByUserIdAsync(user.Id);
    }

    private async Task OpenAddDialogAsync()
    {
        await DialogService.RequestEditAsync(new KHostUser { Name = "" }, async user => await SaveAsync(user));
    }

    private async Task OpenEditDialogAsync(KHostUser user)
    {
        await DialogService.RequestEditAsync(user, async updated => await SaveAsync(updated));
    }

    private async Task OpenPerformanceHistoryAsync(KHostUser user)
    {
        await DialogService.ShowSingerPerformanceHistoryAsync(user.Id);
    }

    private async Task SaveAsync(KHostUser? user)
    {
        if (user is null)
            return;

        try
        {
            var existing = await UsersService.ReadAsync(user.Id);
            if (existing is null)
                await UsersService.CreateAsync(user);
            else
                await UsersService.UpdateAsync(user);
        }
        catch (KHostException taken)
        {
            // Caught here rather than left to the error boundary: a name already in use is the
            // host's mistake to correct, not a reason to replace the page they were working on.
            Flash?.Show(taken.WhatHappened, FlashType.Warning);
        }
    }

    // Unconditional: a destructive action must not hinge on which venue is selected.
    private async Task StartDeleteAsync(KHostUser user)
    {
        await DialogService.ShowConfirmationAsync(
            $"Are you sure you want to delete <span class=\"kh-emphasis\">{user.Name}</span>?",
            async () => await UsersService.DeleteAsync(user.Id),
            "Delete User",
            "Delete"
        );
    }

    private void OnStateChanged()
        => _ = InvokeAsync(async () =>
        {
            await _search.ReloadClampedAsync();
            StateHasChanged();
        });

    public void Dispose() => _subscriptions.Dispose();
}
