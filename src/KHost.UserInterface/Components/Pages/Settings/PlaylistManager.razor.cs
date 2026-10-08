using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

/// <summary>The Ads and Break Music pages, which differ only in purpose, wording and whether a
/// playlist names a trigger. A page wraps this with its own <c>@page</c> route and text.</summary>
public partial class PlaylistManager : IDisposable
{
    [Inject] private IMediaPoolService MediaPools { get; set; } = default!;
    [Inject] private IVenuesService Venues { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    [Parameter, EditorRequired] public PoolPurpose Purpose { get; set; }
    [Parameter, EditorRequired] public string Title { get; set; } = "";
    [Parameter, EditorRequired] public string Icon { get; set; } = "";
    [Parameter, EditorRequired] public string EmptyText { get; set; } = "";
    [Parameter, EditorRequired] public string DeleteWarning { get; set; } = "";

    /// <summary>False for Break Music, whose playlists have no "when it plays" of their own.</summary>
    [Parameter] public bool ShowTrigger { get; set; }

    /// <summary>Which pool this page's purpose currently draws from, off the venue's settings.</summary>
    [Parameter, EditorRequired] public Func<Venue.VenueSettings, Guid?> ActivePoolOf { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private List<MediaPool> _pools = [];
    private MediaPool? _editing;
    private bool _dialogOpen;

    private Guid? _venueId;

    /// <summary>Which playlist the venue runs, for the "In use" badge on the list.</summary>
    private Guid? _activePoolId;

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<MediaPoolsChanged>(_ => OnChanged()));

        // A pool renamed or deleted elsewhere, or a different one picked for this venue, changes
        // what this page shows; without these the "In use" badge goes stale.
        _subscriptions.Add(Broker.Subscribe<VenuesChanged>(_ => OnChanged()));
        _subscriptions.Add(Broker.Subscribe<SelectedVenueChanged>(_ => OnChanged()));

        // Only Break Music has anything else that announces this: an ad pool has no running state
        // for it to report.
        if (Purpose == PoolPurpose.BreakMusic)
            _subscriptions.Add(Broker.Subscribe<BreakMusicChanged>(_ => OnChanged()));

        await RefreshAsync();
    }

    public void Dispose()
    {
        _subscriptions.Dispose();

        GC.SuppressFinalize(this);
    }

    private void OnChanged()
        => _ = InvokeAsync(async () =>
        {
            await RefreshAsync();
            StateHasChanged();
        });

    private async Task RefreshAsync()
    {
        var venue = await Venues.ReadSelectedVenueAsync();

        _venueId = venue?.Id;
        _activePoolId = venue is null ? null : ActivePoolOf(venue.Settings);

        _pools = [.. (await MediaPools.ReadAllWithEntriesAsync(Purpose, _venueId)).OrderBy(p => p.Name)];
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
            $"Delete <span class=\"kh-emphasis\">{pool.Name}</span>? {DeleteWarning}",
            async () =>
            {
                await MediaPools.DeleteAsync(pool.Id);
                await RefreshAsync();
            },
            "Delete Playlist",
            "Delete");
    }
}
