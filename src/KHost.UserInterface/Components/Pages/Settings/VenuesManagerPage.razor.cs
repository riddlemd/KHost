using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class VenuesManagerPage : IDisposable
{
    [Inject] private IVenuesService? VenuesService { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IAppSettingsService? AppSettingsService { get; set; }
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private int _pageSize = AppSettings.DefaultPageSize;
    // Mirrors EditVenueModel's [MaxLength] so a generated name can't fail validation later.
    private const int NameMaxLength = 32;
    private int _currentPage = 1;
    private string _searchQuery = "";
    private string? _sortColumn;
    private bool _sortDescending;
    private PaginatedResult<Venue>? _paginatedResult;

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<VenuesChanged>(OnStateChanged));

        _pageSize = AppSettingsService!.Current.VenuesPageSize;

        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        if (VenuesService is null)
            return;

        var sort = _sortColumn is not null ? new SortDescriptor(_sortColumn, _sortDescending) : null;
        _paginatedResult = await VenuesService.SearchAsync(_searchQuery, _currentPage, _pageSize, sort);
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
        await DialogService!.RequestEditAsync(new Venue { Name = "" }, async venue => await SaveAsync(venue));
    }

    private async Task OpenEditDialogAsync(Venue venue)
    {
        await DialogService!.RequestEditAsync(venue, async updated => await SaveAsync(updated));
    }

    private async Task CloneAsync(Venue venue)
    {
        if (VenuesService is null) return;

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

            // Trim the stem, not the suffix — an over-length name fails the editor's validation
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
        if (VenuesService is null || venue is null)
            return;

        var existing = await VenuesService.ReadAsync(venue.Id);
        if (existing is null)
            await VenuesService.CreateAsync(venue);
        else
            await VenuesService.UpdateAsync(venue);
    }

    private async Task StartDeleteAsync(Venue venue)
    {
        if (VenuesService is null || DialogService is null) return;

        await DialogService.ShowConfirmationAsync(
            $"Are you sure you want to delete <span class=\"kh-emphasis\">{venue.Name}</span>?",
            async () => await VenuesService.DeleteAsync(venue.Id),
            "Delete Venue",
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

    private async void OnStateChanged(VenuesChanged message)
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
