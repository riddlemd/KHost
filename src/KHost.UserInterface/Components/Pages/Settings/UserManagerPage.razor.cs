using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class UserManagerPage : IDisposable
{
    [Inject] private IUsersService? UsersService { get; set; }
    [Inject] private ITipsService? TipsService { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IVenuesService? VenuesService { get; set; }

    private const int PageSize = 20;
    private int _currentPage = 1;
    private string _searchQuery = "";
    private string? _sortColumn;
    private bool _sortDescending;
    private PaginatedResult<KHostUser>? _paginatedResult;
    private Dictionary<Guid, decimal> _tipTotals = [];

    protected override async Task OnInitializedAsync()
    {
        await SearchAsync();

        UsersService!.StateChanged += OnStateChanged;
        TipsService!.StateChanged  += OnStateChanged;
    }

    private async Task SearchAsync()
    {
        if (UsersService is null || TipsService is null)
            return;

        var sort = _sortColumn is not null ? new SortDescriptor(_sortColumn, _sortDescending) : null;
        _paginatedResult = await UsersService.SearchAsync(_searchQuery, _currentPage, PageSize, sort);

        _tipTotals = [];
        foreach (var user in _paginatedResult?.Items ?? [])
        {
            _tipTotals[user.Id] = await TipsService.GetTotalByUserIdAsync(user.Id);
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

    private async Task OnSearchChanged()
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

        var existing = await UsersService.ReadAsync(user.Id);
        if (existing is null)
            await UsersService.CreateAsync(user);
        else
            await UsersService.UpdateAsync(user);
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

    private async Task ClearSearch()
    {
        _searchQuery = "";
        _currentPage = 1;

        await SearchAsync();
    }

    private async Task PreviousPage()
    {
        if (_currentPage > 1)
        {
            _currentPage--;

            await SearchAsync();
        }
    }

    private async Task NextPage()
    {
        if (_currentPage < (_paginatedResult?.TotalPages ?? 0))
        {
            _currentPage++;

            await SearchAsync();
        }
    }

    private async void OnStateChanged(object? sender, EventArgs e)
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

    public void Dispose()
    {
        UsersService?.StateChanged -= OnStateChanged;
    }
}
