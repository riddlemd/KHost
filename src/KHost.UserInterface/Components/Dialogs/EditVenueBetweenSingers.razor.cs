using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>The venue dialog's "Between singers" section: break music, ads, the break music card
/// and the placeholder image. Loads its own choices from <see cref="Model"/>'s ids, the same way
/// the dialog itself used to.</summary>
public partial class EditVenueBetweenSingers
{
    [Parameter, EditorRequired] public EditVenueModel Model { get; set; } = default!;

    [Inject] private IMediaService Media { get; set; } = default!;
    [Inject] private IMediaPoolService MediaPools { get; set; } = default!;
    [Inject] private IBreakMusicService BreakMusic { get; set; } = default!;

    private IReadOnlyList<Media> _images = [];
    private IReadOnlyList<MediaPool> _breakMusicPools = [];
    private IReadOnlyList<MediaPool> _adPools = [];

    private Media? _brandingImage;
    private string _brandingImageText = "";
    private MediaPool? _breakMusicPool;
    private string _breakMusicPoolText = "";
    private MediaPool? _adPool;
    private string _adPoolText = "";

    /// <summary>Kept selected for an unloaded provider: an unmatched select value renders blank.</summary>
    private string? UnavailableProviderSource
        => BreakMusic.Providers.Any(p => string.Equals(p.SourceName, Model.BreakMusicProvider, StringComparison.OrdinalIgnoreCase))
            ? null
            : Model.BreakMusicProvider;

    /// <summary>Whether the mode is fed by this host's own playlists, loaded or not.</summary>
    private bool UsesLocalPlaylists
        => UnavailableProviderSource is not null
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
        // Stills only: anything else handed to the screen as a card is a URL that serves nothing.
        // Read by type rather than paged, or a card past the first page would never be offered.
        _images = await Media.ReadAllByTypesAsync(MediaType.Image);

        // Null venue id: a playlist belongs to every venue unless it was scoped to one, and this
        // dialog may be editing a venue that is not the one currently selected.
        _breakMusicPools = await MediaPools.ReadAllWithEntriesAsync(PoolPurpose.BreakMusic, venueId: null);
        _adPools = await MediaPools.ReadAllWithEntriesAsync(PoolPurpose.Ads, venueId: null);

        _brandingImage = _images.FirstOrDefault(image => image.Id == Model.BrandingImageMediaId);
        _brandingImageText = _brandingImage?.Title ?? "";

        _breakMusicPool = _breakMusicPools.FirstOrDefault(pool => pool.Id == Model.BreakMusicPoolId);
        _breakMusicPoolText = _breakMusicPool?.Name ?? "";

        _adPool = _adPools.FirstOrDefault(pool => pool.Id == Model.AdPoolId);
        _adPoolText = _adPool?.Name ?? "";
    }

    private void OnBreakMusicPoolChanged(MediaPool? pool)
    {
        _breakMusicPool = pool;
        Model.BreakMusicPoolId = pool?.Id;
    }

    private void OnAdPoolChanged(MediaPool? pool)
    {
        _adPool = pool;
        Model.AdPoolId = pool?.Id;
    }

    private Task<IReadOnlyList<MediaPool>> SearchBreakMusicPoolsAsync(string term) => MatchingAsync(_breakMusicPools, term);

    private Task<IReadOnlyList<MediaPool>> SearchAdPoolsAsync(string term) => MatchingAsync(_adPools, term);

    private static Task<IReadOnlyList<MediaPool>> MatchingAsync(IReadOnlyList<MediaPool> pools, string term)
        => Task.FromResult<IReadOnlyList<MediaPool>>(
            [.. pools.Where(pool => pool.Name.Contains(term, StringComparison.OrdinalIgnoreCase))]);

    /// <summary>Clearing the field is how a venue goes back to showing no card at all.</summary>
    private void OnBrandingImageChanged(Media? image)
    {
        _brandingImage = image;
        Model.BrandingImageMediaId = image?.Id;
    }

    private Task<IReadOnlyList<Media>> SearchImagesAsync(string term)
        => Task.FromResult<IReadOnlyList<Media>>(
            [.. _images.Where(image => image.Title.Contains(term, StringComparison.OrdinalIgnoreCase))]);
}
