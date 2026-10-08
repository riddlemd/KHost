using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class MediaManagerPage : IAsyncDisposable
{
    private PagedSearch<Media> _search = default!;
    private HashSet<Guid> _selectedIds = [];

    [Inject] private IMediaService MediaService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private IVenuesService VenuesService { get; set; } = default!;
    [Inject] private IAppSettingsService AppSettingsService { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private void NavigateToImporter() => Navigation.NavigateTo("/settings/media-importer");

    private Task EditAsync(Media media) =>
        DialogService.RequestEditAsync(media, onSave: async updated =>
        {
            if (updated is not null)
                await MediaService.UpdateAsync(updated);
        });

    // Unconditional: a destructive action must not hinge on which venue is selected.
    private async Task RemoveAsync(Media media)
    {
        await DialogService.ShowConfirmationAsync(
            $"Are you sure you want to remove <span class=\"kh-emphasis\">{media.Title}</span> from the library?",
            onConfirm: () => MediaService.DeleteAsync(media.Id),
            title: "Remove Media",
            confirmText: "Remove"
        );
    }

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<MediaLibraryChanged>(OnMediaStateChanged));

        // The manager is the one page that manages files rather than plays them, so it is the one
        // place break music and ads are listed alongside songs.
        _search = new PagedSearch<Media>(
            (query, page, size, sort) => MediaService.SearchAsync(query, page, size, sort, MediaSearchOptions.AllTypes))
        { Size = AppSettingsService.Current.MediaPageSize };

        await _search.SearchAsync();
    }

    private bool _addFileDialogOpen;

    private void OpenAddFileDialog() => _addFileDialogOpen = true;

    private void CloseAddFileDialog() => _addFileDialogOpen = false;

    private Task OnSearchKeyDownAsync(KeyboardEventArgs e)
        => e.Key == "Enter" ? _search.SearchChangedAsync() : Task.CompletedTask;

    private void OnMediaStateChanged(MediaLibraryChanged message) =>
        _ = InvokeAsync(async () =>
        {
            await _search.SearchAsync();
            StateHasChanged();
        });

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _subscriptions.Dispose();

        await Task.CompletedTask;
    }

    // The paged move itself is a no-op at either end, so the selection is only cleared when the
    // page actually changed under it.
    private async Task PreviousPageAsync()
    {
        var page = _search.Page;
        await _search.PreviousAsync();
        if (_search.Page != page) _selectedIds.Clear();
    }

    private async Task NextPageAsync()
    {
        var page = _search.Page;
        await _search.NextAsync();
        if (_search.Page != page) _selectedIds.Clear();
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
        if (_selectedIds.Count == _search.Result?.Items.Count)
            _selectedIds.Clear();
        else
        {
            _selectedIds.Clear();
            foreach (var media in _search.Result?.Items ?? [])
                _selectedIds.Add(media.Id);
        }
    }

    private string SelectAllIconName =>
        _selectedIds.Count == 0 ? "square"
        : _selectedIds.Count == _search.Result?.Items.Count ? "check-square"
        : "slash-square";

    private async Task EditSelectedAsync()
    {
        var items = _search.Result?.Items
            .Where(m => _selectedIds.Contains(m.Id))
            .ToList() ?? [];

        if (items.Count == 0)
            return;

        await DialogService.RequestBulkEditAsync(items, ApplyBulkEditAsync);
    }

    private async Task ApplyBulkEditAsync(BulkEditMediaModel model)
    {
        var items = _search.Result?.Items
            .Where(m => _selectedIds.Contains(m.Id))
            .ToList() ?? [];

        foreach (var media in items)
        {
            if (model.SwapTitleAndArtist)
                (media.Title, media.Artist) = (media.Artist, media.Title);
            if (model.UpdateArtist)
                media.Artist = model.Artist;

            await MediaService.UpdateAsync(media);
        }

        _selectedIds.Clear();
    }

    private async Task DeleteSelectedAsync()
    {
        var items = _search.Result?.Items
            .Where(m => _selectedIds.Contains(m.Id))
            .ToList() ?? [];

        if (items.Count == 0)
            return;

        await DialogService.ShowConfirmationAsync(
            $"Are you sure you want to remove {items.Count} item(s) from the library?",
            onConfirm: () => DeleteSelectedItemsAsync(items),
            title: "Remove Media",
            confirmText: "Remove"
        );
    }

    private async Task DeleteSelectedItemsAsync(List<Media> items)
    {
        foreach (var media in items)
            await MediaService.DeleteAsync(media.Id);

        _selectedIds.Clear();
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
