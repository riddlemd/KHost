using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>The venue dialog's "Break music" section: what plays between singers, and the label
/// naming it on screen.</summary>
public partial class EditVenueBreakMusic
{
    [Parameter, EditorRequired] public EditVenueModel Model { get; set; } = default!;

    [Inject] private IMediaPoolService MediaPools { get; set; } = default!;
    [Inject] private IBreakMusicService BreakMusic { get; set; } = default!;

    private IReadOnlyList<MediaPool> _breakMusicPools = [];
    private MediaPool? _breakMusicPool;
    private string _breakMusicPoolText = "";

    /// <summary>Whether break music comes from this host's own playlists, the one mode a venue
    /// still picks something for.</summary>
    private bool UsesLocalPlaylists
        => BreakMusic.ActiveProvider is { } active && ReferenceEquals(active, BreakMusic.LibraryProvider);

    /// <summary>Read when the dialog opens, not held, since a new playlist would be missing.</summary>
    protected override async Task OnInitializedAsync()
    {
        // Null venue id: a playlist belongs to every venue unless it was scoped to one, and this
        // dialog may be editing a venue that is not the one currently selected.
        _breakMusicPools = await MediaPools.ReadAllWithEntriesAsync(PoolPurpose.BreakMusic, venueId: null);

        _breakMusicPool = _breakMusicPools.FirstOrDefault(pool => pool.Id == Model.BreakMusicPoolId);
        _breakMusicPoolText = _breakMusicPool?.Name ?? "";
    }

    private void OnBreakMusicPoolChanged(MediaPool? pool)
    {
        _breakMusicPool = pool;
        Model.BreakMusicPoolId = pool?.Id;
    }

    private Task<IReadOnlyList<MediaPool>> SearchBreakMusicPoolsAsync(string term)
        => Task.FromResult<IReadOnlyList<MediaPool>>(
            [.. _breakMusicPools.Where(pool => pool.Name.Contains(term, StringComparison.OrdinalIgnoreCase))]);
}
