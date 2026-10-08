using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>The venue dialog's "Ads" section: which playlist fills the gap after a performance.</summary>
public partial class EditVenueAds
{
    [Parameter, EditorRequired] public EditVenueModel Model { get; set; } = default!;

    [Inject] private IMediaPoolService MediaPools { get; set; } = default!;

    private IReadOnlyList<MediaPool> _adPools = [];
    private MediaPool? _adPool;
    private string _adPoolText = "";

    /// <summary>Read when the dialog opens, not held, since a new playlist would be missing.</summary>
    protected override async Task OnInitializedAsync()
    {
        // Null venue id: a playlist belongs to every venue unless it was scoped to one, and this
        // dialog may be editing a venue that is not the one currently selected.
        _adPools = await MediaPools.ReadAllWithEntriesAsync(PoolPurpose.Ads, venueId: null);

        _adPool = _adPools.FirstOrDefault(pool => pool.Id == Model.AdPoolId);
        _adPoolText = _adPool?.Name ?? "";
    }

    private void OnAdPoolChanged(MediaPool? pool)
    {
        _adPool = pool;
        Model.AdPoolId = pool?.Id;
    }

    private Task<IReadOnlyList<MediaPool>> SearchAdPoolsAsync(string term)
        => Task.FromResult<IReadOnlyList<MediaPool>>(
            [.. _adPools.Where(pool => pool.Name.Contains(term, StringComparison.OrdinalIgnoreCase))]);
}
