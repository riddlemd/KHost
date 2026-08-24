using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class PlaylistsManagerPage : IDisposable
{
    [Inject] private IMediaPoolService MediaPools { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;

    private List<MediaPool> _pools = [];
    private MediaPool? _editing;
    private bool _dialogOpen;

    protected override async Task OnInitializedAsync()
    {
        MediaPools.StateChanged += OnPoolsChanged;

        await RefreshAsync();
    }

    public void Dispose()
    {
        MediaPools.StateChanged -= OnPoolsChanged;

        GC.SuppressFinalize(this);
    }

    private async void OnPoolsChanged(object? sender, EventArgs e)
    {
        await InvokeAsync(async () =>
        {
            await RefreshAsync();
            StateHasChanged();
        });
    }

    private async Task RefreshAsync()
    {
        // Both kinds and every venue: this page manages playlists rather than running them, so it
        // is the one place a playlist scoped to another venue is still visible.
        var breakMusic = await MediaPools.ReadAllWithEntriesAsync(MediaKind.BreakMusic, venueId: null);
        var ads = await MediaPools.ReadAllWithEntriesAsync(MediaKind.Ad, venueId: null);

        _pools = [.. breakMusic.Concat(ads).OrderBy(p => p.Kind).ThenBy(p => p.Name)];
    }

    private static string DescribeSelection(MediaPool pool) => pool.SelectionMode switch
    {
        PoolSelectionMode.Sequential => "In order",
        PoolSelectionMode.Weighted => "By weight",
        _ => "Shuffled",
    };

    private static string DescribeEntries(MediaPool pool)
    {
        var nested = pool.Entries.Count(e => e.IsPool);
        var tracks = pool.Entries.Count - nested;

        return nested == 0
            ? $"{tracks}"
            : $"{tracks} + {nested} playlist{(nested == 1 ? "" : "s")}";
    }

    private static string DescribeTrigger(MediaPool pool) => pool.AdTrigger switch
    {
        AdTriggerMode.EveryNPerformances => $"Every {pool.AdTriggerInterval} songs",
        AdTriggerMode.EveryNMinutes => $"Every {pool.AdTriggerInterval} minutes",
        AdTriggerMode.OnIdle => "When nobody is queued",
        _ => "Only when asked",
    };

    private Task OpenAddDialogAsync()
    {
        _editing = null;
        _dialogOpen = true;

        return Task.CompletedTask;
    }

    private Task OpenEditDialogAsync(MediaPool pool)
    {
        _editing = pool;
        _dialogOpen = true;

        return Task.CompletedTask;
    }

    private void CloseDialog()
    {
        _dialogOpen = false;
        _editing = null;
    }

    private async Task SavePoolAsync(MediaPool pool)
    {
        var existing = await MediaPools.ReadAsync(pool.Id);

        if (existing is null)
            await MediaPools.CreateAsync(pool);
        else
            await MediaPools.UpdateAsync(pool);

        // Refused when the entries would let the playlist reach itself, which the dialog cannot
        // know until the whole edit is in.
        if (!await MediaPools.ReplaceEntriesAsync(pool.Id, pool.Entries))
            Flash.Show("That playlist ends up containing itself, so its entries were left as they were.", FlashKind.Warning);

        CloseDialog();

        await RefreshAsync();
    }

    private async Task StartDeleteAsync(MediaPool pool)
    {
        await Dialogs.ShowConfirmationAsync(
            $"Delete <span class=\"kh-emphasis\">{pool.Name}</span>? Any venue using it falls back to nothing.",
            async () =>
            {
                await MediaPools.DeleteAsync(pool.Id);
                await RefreshAsync();
            },
            "Delete Playlist",
            "Delete");
    }
}
