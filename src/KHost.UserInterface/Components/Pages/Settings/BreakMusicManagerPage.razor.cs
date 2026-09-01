using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

public partial class BreakMusicManagerPage : IDisposable
{
    [Inject] private IMediaPoolService MediaPools { get; set; } = default!;
    [Inject] private IVenuesService Venues { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private List<MediaPool> _pools = [];
    private MediaPool? _editing;
    private bool _dialogOpen;

    private Guid? _venueId;
    private Guid? _activePoolId;

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<MediaPoolsChanged>(_ => OnChanged()));
        _subscriptions.Add(Broker.Subscribe<BreakMusicChanged>(_ => OnChanged()));

        await RefreshAsync();
    }

    public void Dispose()
    {
        _subscriptions.Dispose();

        GC.SuppressFinalize(this);
    }

    private async void OnChanged()
        => await InvokeAsync(async () =>
        {
            await RefreshAsync();
            StateHasChanged();
        });

    private async Task RefreshAsync()
    {
        var venue = await Venues.ReadSelectedVenueAsync();

        _venueId = venue?.Id;
        _activePoolId = venue?.Settings.BreakMusicPoolId;

        _pools = [.. (await MediaPools.ReadAllWithEntriesAsync(PoolPurpose.BreakMusic, _venueId)).OrderBy(p => p.Name)];
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
            Flash.Show("That playlist ends up containing itself, so its entries were left as they were.", FlashType.Warning);

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
