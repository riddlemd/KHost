using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using KHost.Plugins.Sdk.Services;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class BreakMusicManagerPage : IDisposable
{
    [Inject] private IMediaPoolService MediaPools { get; set; } = default!;
    [Inject] private IBreakMusicService BreakMusic { get; set; } = default!;
    [Inject] private IVenuesService Venues { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;

    private List<MediaPool> _pools = [];
    private MediaPool? _editing;
    private bool _dialogOpen;

    private Guid? _venueId;
    private string? _venueName;
    private Guid? _activePoolId;
    private string? _providerSource;

    protected override async Task OnInitializedAsync()
    {
        MediaPools.StateChanged += OnChanged;
        BreakMusic.StateChanged += OnChanged;

        await RefreshAsync();
    }

    public void Dispose()
    {
        MediaPools.StateChanged -= OnChanged;
        BreakMusic.StateChanged -= OnChanged;

        GC.SuppressFinalize(this);
    }

    private async void OnChanged(object? sender, EventArgs e)
        => await InvokeAsync(async () =>
        {
            await RefreshAsync();
            StateHasChanged();
        });

    private async Task RefreshAsync()
    {
        var venue = await Venues.ReadSelectedVenueAsync();

        _venueId = venue?.Id;
        _venueName = venue?.Name;
        _activePoolId = venue?.Settings.BreakMusicPoolId;
        _providerSource = venue?.Settings.BreakMusicProvider ?? BreakMusic.ActiveProvider?.SourceName;

        _pools = [.. (await MediaPools.ReadAllWithEntriesAsync(PoolPurpose.BreakMusic, _venueId)).OrderBy(p => p.Name)];
    }

    /// <summary>
    /// The built-in one is the mode a host thinks of as "my own music"; a plugin names itself.
    /// </summary>
    private static string DescribeProvider(IBreakMusicProvider provider)
        => provider.RendersThroughHost ? $"{provider.DisplayName} playlist" : provider.DisplayName;

    private async Task OnProviderChangedAsync(ChangeEventArgs e)
    {
        var source = e.Value?.ToString();

        if (string.IsNullOrWhiteSpace(source))
            return;

        // Written to the venue as well as switched live, or the choice is forgotten on restart.
        await BreakMusic.SetActiveProviderAsync(source);
        await SaveVenueAsync(settings => settings.BreakMusicProvider = source);
    }

    private async Task OnPlaylistChangedAsync(ChangeEventArgs e)
    {
        var poolId = Guid.TryParse(e.Value?.ToString(), out var id) ? id : (Guid?)null;

        await SaveVenueAsync(settings => settings.BreakMusicPoolId = poolId);
    }

    private async Task SaveVenueAsync(Action<Venue.VenueSettings> apply)
    {
        var venue = await Venues.ReadSelectedVenueAsync();

        if (venue is null)
        {
            Flash.Show("No venue is selected, so there is nothing to save this against.", FlashKind.Warning);
            return;
        }

        apply(venue.Settings);

        await Venues.UpdateAsync(venue);
        await RefreshAsync();
    }

    private void OpenAddDialog()
    {
        _editing = null;
        _dialogOpen = true;
    }

    private void OpenEditDialog(MediaPool pool)
    {
        _editing = pool;
        _dialogOpen = true;
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
            $"Delete <span class=\"kh-emphasis\">{pool.Name}</span>? Any venue using it falls back to silence.",
            async () =>
            {
                await MediaPools.DeleteAsync(pool.Id);
                await RefreshAsync();
            },
            "Delete Playlist",
            "Delete");
    }
}
