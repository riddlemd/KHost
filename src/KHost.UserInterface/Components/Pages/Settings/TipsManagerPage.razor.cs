using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class TipsManagerPage : IDisposable
{
    [Inject] private ITipsService? TipsService { get; set; }
    [Inject] private IUsersService? UsersService { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IVenuesService? VenuesService { get; set; }

    private const int PageSize = 10;
    private int _currentPage = 1;
    private string _searchQuery = "";
    private string? _sortColumn;
    private bool _sortDescending;
    private PaginatedResult<Tip>? _paginatedResult;
    private Dictionary<Guid, string> _userNames = [];
    private Dictionary<Guid, string> _venueNames = [];

    protected override async Task OnInitializedAsync()
    {
        await LoadUsersAsync();
        await LoadVenuesAsync();
        await SearchAsync();
        TipsService!.StateChanged += OnStateChanged;
    }

    private async Task LoadUsersAsync()
    {
        if (UsersService is null) return;

        var result = await UsersService.ReadAllAsync(pageSize: 1000);
        _userNames = result.Items.ToDictionary(u => u.Id, u => u.Name);
    }

    private string GetSingerName(Guid userId)
    {
        return _userNames.TryGetValue(userId, out var name) ? name : "Unknown Singer";
    }

    private async Task LoadVenuesAsync()
    {
        if (VenuesService is null) return;

        var result = await VenuesService.ReadAllAsync(pageSize: 1000);
        _venueNames = result.Items.ToDictionary(v => v.Id, v => v.Name);
    }

    // Old tips predate venue stamping, and a deleted venue leaves its id behind by design.
    private string GetVenueName(Guid? venueId)
        => venueId is { } id && _venueNames.TryGetValue(id, out var name) ? name : "—";

    private async Task SearchAsync()
    {
        if (TipsService is null)
            return;

        var sort = _sortColumn is not null ? new SortDescriptor(_sortColumn, _sortDescending) : null;
        _paginatedResult = await TipsService.SearchAsync(_searchQuery, _currentPage, PageSize, sort);
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
        await DialogService!.RequestEditAsync(new Tip { Amount = 0 }, async tip => await SaveAsync(tip));
    }

    private async Task OpenEditDialogAsync(Tip tip)
    {
        await DialogService!.RequestEditAsync(tip, async updated => await SaveAsync(updated));
    }

    private async Task SaveAsync(Tip? tip)
    {
        if (TipsService is null || tip is null)
            return;

        var existing = await TipsService.ReadAsync(tip.Id);
        if (existing is null)
            await TipsService.CreateAsync(tip);
        else
            await TipsService.UpdateAsync(tip);
    }

    // Unconditional: a destructive action must not hinge on which venue is selected.
    private async Task StartDeleteAsync(Tip tip)
    {
        if (TipsService is null || DialogService is null) return;

        var singer = GetSingerName(tip.UserId);

        await DialogService.ShowConfirmationAsync(
            $"Are you sure you want to delete the tip from <span class=\"kh-emphasis\">{singer}</span> for ${tip.Amount:F2}?",
            async () => await TipsService.DeleteAsync(tip.Id),
            "Delete Tip",
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
        TipsService?.StateChanged -= OnStateChanged;
    }
}
