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

    /// <summary>The venue's stored mode, captured once at open, when it names no loaded provider.
    /// Reading <see cref="Model"/>'s live value here instead loses the option the moment the host
    /// picks something else — the placeholder would vanish from the select along with it, so
    /// switching back to it saved an empty value rather than the mode the venue actually had.</summary>
    private string? _unavailableProviderSource;

    /// <summary>Kept selected for an unloaded provider: an unmatched select value renders blank.</summary>
    private string? UnavailableProviderSource => _unavailableProviderSource;

    /// <summary>Whether the select is currently sitting on that unloaded mode.</summary>
    private bool IsOnUnavailableProvider
        => _unavailableProviderSource is not null
           && string.Equals(Model.BreakMusicProvider, _unavailableProviderSource, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the mode is fed by this host's own playlists, loaded or not.</summary>
    private bool UsesLocalPlaylists
        => IsOnUnavailableProvider
           || (BreakMusic.LibraryProvider is { } library
               && string.Equals(Model.BreakMusicProvider, library.SourceName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The mode fed by this host's playlists reads "my own music"; others name themselves.</summary>
    private string DescribeProvider(IBreakMusicProvider provider)
        => ReferenceEquals(provider, BreakMusic.LibraryProvider)
            ? $"{provider.DisplayName} playlist"
            : provider.DisplayName;

    /// <summary>Read when the dialog opens, not held, since a new playlist would be missing.</summary>
    protected override async Task OnInitializedAsync()
    {
        // Snapshot before anything can change it: whether this mode is unloaded is a fact about
        // what the venue had on open, not about whatever the select currently shows.
        _unavailableProviderSource =
            !string.IsNullOrWhiteSpace(Model.BreakMusicProvider)
            && !BreakMusic.Providers.Any(p =>
                string.Equals(p.SourceName, Model.BreakMusicProvider, StringComparison.OrdinalIgnoreCase))
                ? Model.BreakMusicProvider
                : null;

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
