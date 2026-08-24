using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class UserManagerPage : IDisposable
{
    [Inject] private IUsersService? UsersService { get; set; }
    [Inject] private ITipsService? TipsService { get; set; }
    [Inject] private IFlashService? Flash { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IVenuesService? VenuesService { get; set; }
    [Inject] private IAppSettingsService? AppSettingsService { get; set; }
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private int _pageSize = AppSettings.DefaultPageSize;
    private int _currentPage = 1;
    private string _searchQuery = "";
    private string? _sortColumn;
    private bool _sortDescending;
    private PaginatedResult<KHostUser>? _paginatedResult;
    private Dictionary<Guid, int> _tipTotals = [];

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<UsersChanged>(_ => OnStateChanged()));
        _subscriptions.Add(Broker.Subscribe<TipsChanged>(_ => OnStateChanged()));

        _pageSize = AppSettingsService!.Current.UsersPageSize;

        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        if (UsersService is null || TipsService is null)
            return;

        var sort = _sortColumn is not null ? new SortDescriptor(_sortColumn, _sortDescending) : null;
        _paginatedResult = await UsersService.SearchAsync(_searchQuery, _currentPage, _pageSize, sort);

        _tipTotals = [];
        foreach (var user in _paginatedResult?.Items ?? [])
        {
            _tipTotals[user.Id] = await TipsService.GetTotalInCentsByUserIdAsync(user.Id);
        }
    }

    private void OnSortColumnClicked(string column)
    {
        if (_sortColumn == column)
            _sortDescending = !_sortDescending;
        else
        {
            _sortColumn = column;
            _sortDescending = false;
        }
        _currentPage = 1;
        _ = SearchAsync();
    }

    private async Task OnSearchChangedAsync()
    {
        _currentPage = 1;

        await SearchAsync();
    }

    private async Task OpenAddDialogAsync()
    {
        await DialogService!.RequestEditAsync(new KHostUser { Name = "" }, async user => await SaveAsync(user));
    }

    private async Task OpenEditDialogAsync(KHostUser user)
    {
        await DialogService!.RequestEditAsync(user, async updated => await SaveAsync(updated));
    }

    private async Task OpenPerformanceHistoryAsync(KHostUser user)
    {
        await DialogService!.ShowSingerPerformanceHistoryAsync(user.Id);
    }

    private async Task SaveAsync(KHostUser? user)
    {
        if (UsersService is null || user is null)
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
            // host's mistake to correct, and replacing the page they were working on is no way to
            // tell them.
            Flash?.Show(taken.WhatHappened, FlashType.Warning);
        }
    }

    // Unconditional: a destructive action must not hinge on which venue is selected.
    private async Task StartDeleteAsync(KHostUser user)
    {
        if (UsersService is null || DialogService is null) return;

        await DialogService.ShowConfirmationAsync(
            $"Are you sure you want to delete <span class=\"kh-emphasis\">{user.Name}</span>?",
            async () => await UsersService.DeleteAsync(user.Id),
            "Delete User",
            "Delete"
        );
    }

    private async Task ClearSearchAsync()
    {
        _searchQuery = "";
        _currentPage = 1;

        await SearchAsync();
    }

    private async Task PreviousPageAsync()
    {
        if (_currentPage > 1)
        {
            _currentPage--;

            await SearchAsync();
        }
    }

    private async Task NextPageAsync()
    {
        if (_currentPage < (_paginatedResult?.TotalPages ?? 0))
        {
            _currentPage++;

            await SearchAsync();
        }
    }

    private async void OnStateChanged()
    {
        await SearchAsync();

        var totalPages = _paginatedResult?.TotalPages ?? 0;

        if (_paginatedResult?.Items.Count == 0 && _currentPage > 1)
        {
            _currentPage = Math.Max(1, totalPages);

            await SearchAsync();
        }

        await InvokeAsync(StateHasChanged);
    }

    public void Dispose() => _subscriptions.Dispose();
}
