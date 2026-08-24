using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class MediaManagerPage : IAsyncDisposable
{
    private int _pageSize = AppSettings.DefaultPageSize;
    private int _currentPage = 1;
    private string _searchQuery = "";
    private string? _sortColumn;
    private bool _sortDescending;
    private PaginatedResult<Media>? _paginatedResult;
    private HashSet<Guid> _selectedIds = [];

    [Inject] private IMediaService? MediaService { get; set; }
    [Inject] private NavigationManager? Navigation { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IVenuesService? VenuesService { get; set; }
    [Inject] private IAppSettingsService? AppSettingsService { get; set; }
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private void NavigateToImporter() => Navigation!.NavigateTo("/settings/media-importer");

    private Task EditAsync(Media media) =>
        DialogService!.RequestEditAsync(media, onSave: async updated =>
        {
            if (updated is not null)
                await MediaService!.UpdateAsync(updated);
        });

    // Unconditional: a destructive action must not hinge on which venue is selected.
    private async Task RemoveAsync(Media media)
    {
        if (MediaService is null || DialogService is null) return;

        await DialogService.ShowConfirmationAsync(
            $"Are you sure you want to remove <span class=\"kh-emphasis\">{media.Title}</span> from the library?",
            onConfirm: () => MediaService.DeleteAsync(media.Id),
            title: "Remove Media",
            confirmText: "Remove"
        );
    }

    protected override async Task OnInitializedAsync()
    {
        if (MediaService is null)
            return;

        _subscriptions.Add(Broker.Subscribe<MediaLibraryChanged>(OnMediaStateChanged));

        _pageSize = AppSettingsService!.Current.MediaPageSize;

        await SearchAsync();
    }

    private bool _addFileDialogOpen;

    private void OpenAddFileDialog() => _addFileDialogOpen = true;

    private void CloseAddFileDialog() => _addFileDialogOpen = false;

    private async Task SearchAsync()
    {
        if (MediaService is null)
            return;

        var sort = _sortColumn is not null ? new SortDescriptor(_sortColumn, _sortDescending) : null;

        // The manager is the one page that manages files rather than plays them, so it is the one
        // place break music and ads are listed alongside songs.
        _paginatedResult = await MediaService.SearchAsync(_searchQuery, _currentPage, _pageSize, sort, MediaSearchOptions.AllTypes);
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

    private async Task OnSearchKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            _currentPage = 1;
            await SearchAsync();
        }
    }

    private void OnMediaStateChanged(MediaLibraryChanged message) =>
        _ = InvokeAsync(async () =>
        {
            await SearchAsync();
            StateHasChanged();
        });

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _subscriptions.Dispose();

        await Task.CompletedTask;
    }

    private async Task ClearSearchAsync()
    {
        _searchQuery = "";
        await SearchAsync();
    }

    private async Task PreviousPageAsync()
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            _selectedIds.Clear();
            await SearchAsync();
        }
    }

    private async Task NextPageAsync()
    {
        if (_paginatedResult?.HasNextPage ?? false)
        {
            _currentPage++;
            _selectedIds.Clear();
            await SearchAsync();
        }
    }

    private void ToggleSelection(Guid mediaId)
    {
        if (_selectedIds.Contains(mediaId))
            _selectedIds.Remove(mediaId);
        else
            _selectedIds.Add(mediaId);
    }

    private void OnSelectAllClicked()
    {
        if (_selectedIds.Count == _paginatedResult?.Items.Count)
            _selectedIds.Clear();
        else
        {
            _selectedIds.Clear();
            foreach (var media in _paginatedResult?.Items ?? [])
                _selectedIds.Add(media.Id);
        }
    }

    private string SelectAllIconName =>
        _selectedIds.Count == 0 ? "square"
        : _selectedIds.Count == _paginatedResult?.Items.Count ? "check-square"
        : "slash-square";

    private async Task EditSelectedAsync()
    {
        var items = _paginatedResult?.Items
            .Where(m => _selectedIds.Contains(m.Id))
            .ToList() ?? [];

        if (items.Count == 0)
            return;

        await DialogService!.RequestBulkEditAsync(items, ApplyBulkEditAsync);
    }

    private async Task ApplyBulkEditAsync(BulkEditMediaModel model)
    {
        var items = _paginatedResult?.Items
            .Where(m => _selectedIds.Contains(m.Id))
            .ToList() ?? [];

        foreach (var media in items)
        {
            if (model.SwapTitleAndArtist)
                (media.Title, media.Artist) = (media.Artist, media.Title);
            if (model.UpdateArtist)
                media.Artist = model.Artist;

            await MediaService!.UpdateAsync(media);
        }

        _selectedIds.Clear();
    }

    private async Task DeleteSelectedAsync()
    {
        var items = _paginatedResult?.Items
            .Where(m => _selectedIds.Contains(m.Id))
            .ToList() ?? [];

        if (items.Count == 0)
            return;

        await DialogService!.ShowConfirmationAsync(
            $"Are you sure you want to remove {items.Count} item(s) from the library?",
            onConfirm: () => DeleteSelectedItemsAsync(items),
            title: "Remove Media",
            confirmText: "Remove"
        );
    }

    private async Task DeleteSelectedItemsAsync(List<Media> items)
    {
        foreach (var media in items)
            await MediaService!.DeleteAsync(media.Id);

        _selectedIds.Clear();
    }

    private async Task ClearSelectionAsync()
    {
        _selectedIds.Clear();
        await Task.CompletedTask;
    }

    // Every member spelled out rather than a catch-all: under a column headed Type, a row has to
    // say which type it is, and a new member must not quietly inherit another one's label.
    private static string DescribeType(MediaType type) => type switch
    {
        MediaType.Karaoke => "Karaoke",
        MediaType.Video => "Video",
        MediaType.Audio => "Audio",
        MediaType.Image => "Image",
        _ => type.ToString(),
    };

    private static string GetStatusBadgeClass(MediaStatus status) => MediaStatusDisplay.BadgeClass(status);
}
