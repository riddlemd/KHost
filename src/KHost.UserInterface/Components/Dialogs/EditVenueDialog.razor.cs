using KHost.Abstractions.Models;
using KHost.Abstractions.Models.Backgrounds;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace KHost.UserInterface.Components.Dialogs;

public partial class EditVenueDialog
{
    private const string _rootClassName = "kh-venue-edit-dialog";

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public Venue? Venue { get; set; }

    [Parameter] public EventCallback<Venue> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    [Inject] private IMediaService Media { get; set; } = default!;
    [Inject] private IMediaPoolService MediaPools { get; set; } = default!;
    [Inject] private IBreakMusicService BreakMusic { get; set; } = default!;
    [Inject] private IPluginRegistry Plugins { get; set; } = default!;
    [Inject] private IBackgroundPackService BackgroundPacks { get; set; } = default!;

    private IReadOnlyList<Media> _images = [];
    private IReadOnlyList<MediaPool> _breakMusicPools = [];
    private IReadOnlyList<MediaPool> _adPools = [];

    private Media? _brandingImage;
    private string _brandingImageText = "";
    private MediaPool? _breakMusicPool;
    private string _breakMusicPoolText = "";
    private MediaPool? _adPool;
    private string _adPoolText = "";

    private bool _isNew;
    private EditVenueModel _model = new();
    private EditContext _editContext = default!;
    private bool _rotationDialogOpen;

    /// <summary>Kept selected for an unloaded provider: an unmatched select value renders blank.</summary>
    private string? UnavailableProviderSource
        => BreakMusic.Providers.Any(p => string.Equals(p.SourceName, _model.BreakMusicProvider, StringComparison.OrdinalIgnoreCase))
            ? null
            : _model.BreakMusicProvider;

    /// <summary>Read from manifests, not registrations, so a venue can be set up before the show.</summary>
    private IEnumerable<(string Id, string Label)> QrCodeSources
        => Plugins.Plugins
            .Where(plugin => plugin.Manifest?.QrCode is not null)
            .Select(plugin => (
                plugin.Id,
                Label: string.IsNullOrWhiteSpace(plugin.Manifest!.QrCode!.Label)
                    ? plugin.DisplayName
                    : plugin.Manifest.QrCode.Label!))
            .OrderBy(source => source.Label, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>Kept selected when no plugin declares it, which the break music mode also does.</summary>
    private string? UnavailableQrCodeSource
        => string.IsNullOrWhiteSpace(_model.QrCodeSource)
           || QrCodeSources.Any(source => string.Equals(source.Id, _model.QrCodeSource, StringComparison.OrdinalIgnoreCase))
            ? null
            : _model.QrCodeSource;

    /// <summary>Whether the mode is fed by this host's own playlists, loaded or not.</summary>
    private bool UsesLocalPlaylists
        => UnavailableProviderSource is not null
           || (BreakMusic.LibraryProvider is { } library
               && string.Equals(_model.BreakMusicProvider, library.SourceName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The mode fed by this host's playlists reads "my own music"; others name themselves.</summary>
    private string DescribeProvider(IBreakMusicProvider provider)
        => ReferenceEquals(provider, BreakMusic.LibraryProvider)
            ? $"{provider.DisplayName} playlist"
            : provider.DisplayName;

    // DialogHost keys every dialog by request id, so a fresh instance is created per open; this
    // runs exactly once with Venue already bound.
    protected override async Task OnInitializedAsync()
    {
        _isNew = Venue is null;
        _model = EditVenueModel.From(Venue, BreakMusic.ActiveProvider?.SourceName);
        _editContext = new EditContext(_model);

        await LoadChoicesAsync();
    }

    /// <summary>Read when the dialog opens, not held, since a new playlist would be missing.</summary>
    private async Task LoadChoicesAsync()
    {

        // Stills only: anything else handed to the screen as a card is a URL that serves nothing.
        // Read by type rather than paged, or a card past the first page would never be offered.
        _images = await Media.ReadAllByTypesAsync(MediaType.Image);

        // Null venue id: a playlist belongs to every venue unless it was scoped to one, and this
        // dialog may be editing a venue that is not the one currently selected.
        _breakMusicPools = await MediaPools.ReadAllWithEntriesAsync(PoolPurpose.BreakMusic, venueId: null);
        _adPools = await MediaPools.ReadAllWithEntriesAsync(PoolPurpose.Ads, venueId: null);

        await ReloadBackgroundPackAsync();

        _brandingImage = _images.FirstOrDefault(image => image.Id == _model.BrandingImageMediaId);
        _brandingImageText = _brandingImage?.Title ?? "";

        _breakMusicPool = _breakMusicPools.FirstOrDefault(pool => pool.Id == _model.BreakMusicPoolId);
        _breakMusicPoolText = _breakMusicPool?.Name ?? "";

        _adPool = _adPools.FirstOrDefault(pool => pool.Id == _model.AdPoolId);
        _adPoolText = _adPool?.Name ?? "";
    }

    private void OnBreakMusicPoolChanged(MediaPool? pool)
    {
        _breakMusicPool = pool;
        _model.BreakMusicPoolId = pool?.Id;
    }

    private void OnAdPoolChanged(MediaPool? pool)
    {
        _adPool = pool;
        _model.AdPoolId = pool?.Id;
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
        _model.BrandingImageMediaId = image?.Id;
    }

    private Task<IReadOnlyList<Media>> SearchImagesAsync(string term)
        => Task.FromResult<IReadOnlyList<Media>>(
            [.. _images.Where(image => image.Title.Contains(term, StringComparison.OrdinalIgnoreCase))]);

    private async Task SubmitAsync()
    {
        if (_editContext.Validate())
            await SaveAsync();
    }

    private BackgroundPack _backgroundPack = new();

    /// <summary>Read when the dialog opens: the folders are machine settings, not this venue's.
    /// </summary>
    private async Task ReloadBackgroundPackAsync()
        => _backgroundPack = await BackgroundPacks.ReadAsync();

    /// <summary>A stable DOM id for a background's checkbox.</summary>
    /// <remarks>Derived from the file name, which may hold spaces and dots — both legal in an id
    /// attribute but not in the selector a test or a stylesheet would reach it with.</remarks>
    private static string BackgroundInputId(BackgroundPackEntry entry)
        => "venue-background-" + string.Concat(entry.File.Select(c => char.IsLetterOrDigit(c) ? c : '-'));

    private bool IsBackgroundEnabled(BackgroundPackEntry entry)
        => _model.SongBackgrounds.Contains(entry.File, StringComparer.OrdinalIgnoreCase);

    /// <summary>Ticking a background adds it to the pool a song is picked from.</summary>
    /// <remarks>Names of clips no longer in the folder are left alone rather than tidied away: a
    /// folder that is temporarily unreachable would otherwise silently empty a venue's choices, and
    /// a name nothing matches costs nothing at render time.</remarks>
    private void ToggleBackground(BackgroundPackEntry entry, bool enabled)
    {
        _model.SongBackgrounds.RemoveAll(name => string.Equals(name, entry.File, StringComparison.OrdinalIgnoreCase));

        if (enabled) _model.SongBackgrounds.Add(entry.File);
    }

    /// <summary>The still is asked for by name, never by path — the browser cannot reach the
    /// folder, and does not need to know where it is.</summary>
    private static string BackgroundStillUrl(BackgroundPackEntry entry)
        => $"/venue/background-still?file={Uri.EscapeDataString(entry.File)}";

    /// <summary>What the venue is about to get, said back to them.</summary>
    private string BackgroundSummary()
    {
        var enabled = _backgroundPack.Entries.Count(IsBackgroundEnabled);

        return enabled switch
        {
            0 => "Songs render on plain black.",
            1 => $"Every song uses {_backgroundPack.Entries.First(IsBackgroundEnabled).Name}.",
            _ => $"A different one of these {enabled} for each song, picked as it renders.",
        };
    }

    private async Task SaveAsync()
    {
        // Applied to a copy, never to Venue itself: a save the caller ends up refusing must leave
        // nothing half-edited on the instance the rest of the app is still showing.
        var venue = Venue is null
            ? new Venue { Id = _model.Id, Name = _model.Name }
            : new Venue
            {
                Id = Venue.Id,
                Name = Venue.Name,
                NameFolded = Venue.NameFolded,
                Notes = Venue.Notes,
                Address = Venue.Address,
                Phone = Venue.Phone,
                Enabled = Venue.Enabled,
                Settings = Venue.Settings.Clone(),
            };

        _model.ApplyTo(venue);

        // DialogHost closes after awaiting this itself; closing again here would also fire
        // OnClose's onCancel, marking a successful save as a cancel.
        await OnSave.InvokeAsync(venue);
    }

    public async Task CloseAsync()
    {
        IsOpen = false;

        await OnClose.InvokeAsync();
    }

    private void CloseRotationDialog() => _rotationDialogOpen = false;

    public record DialogRequest : EditDialogRequest<Venue>
    {
        public DialogRequest(Venue? value, Func<Venue?, Task> onSave, Action? onCancel, Action onClose) : base(value, onSave, onCancel, onClose)
        {
        }
    }
}
