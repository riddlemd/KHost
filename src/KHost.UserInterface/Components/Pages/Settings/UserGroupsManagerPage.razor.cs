using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class UserGroupsManagerPage : IDisposable
{
    [Inject] private IUserGroupsService? UserGroupsService { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IVenuesService? VenuesService { get; set; }

    private const int PageSize = 20;
    private int _currentPage = 1;
    private string _searchQuery = "";
    private string? _sortColumn;
    private bool _sortDescending;
    private PaginatedResult<KHostUserGroup>? _paginatedResult;

    protected override async Task OnInitializedAsync()
    {
        await SearchAsync();

        UserGroupsService!.StateChanged += OnStateChanged;
    }

    private async Task SearchAsync()
    {
        if (UserGroupsService is null)
            return;

        var sort = _sortColumn is not null ? new SortDescriptor(_sortColumn, _sortDescending) : null;
        _paginatedResult = await UserGroupsService.SearchAsync(_searchQuery, _currentPage, PageSize, sort);
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
        await DialogService!.RequestEditAsync(new KHostUserGroup { Name = "" }, async group => await SaveAsync(group));
    }

    private async Task OpenEditDialogAsync(KHostUserGroup group)
    {
        await DialogService!.RequestEditAsync(group, async updated => await SaveAsync(updated));
    }

    private async Task SaveAsync(KHostUserGroup? group)
    {
        if (UserGroupsService is null || group is null)
            return;

        var existing = await UserGroupsService.ReadAsync(group.Id);
        if (existing is null)
            await UserGroupsService.CreateAsync(group);
        else
            await UserGroupsService.UpdateAsync(group);
    }

    // Always confirmed: deleting a group affects every user in it, not just the row clicked.
    private async Task StartDeleteAsync(KHostUserGroup group)
    {
        if (UserGroupsService is null || DialogService is null) return;

        await DialogService.ShowConfirmationAsync(
            $"Are you sure you want to delete <span class=\"kh-emphasis\">{group.Name}</span>?",
            async () => await UserGroupsService.DeleteAsync(group.Id),
            "Delete Group",
            "Delete"
        );
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
        UserGroupsService?.StateChanged -= OnStateChanged;
    }
}
