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

    private static readonly int[] DuplicateWindowOptions = [1, 2, 4, 8, 12];

    // What the colour inputs show a venue that has never chosen: a native colour picker has no
    // empty state, so it would otherwise open on black and read as a deliberate choice.
    private const string DefaultMarqueeBackground = "#000000";
    private const string DefaultMarqueeText = "#f2f2f5";

    /// <summary>What a venue turning the marquee on for the first time is offered.</summary>
    private const int DefaultMarqueeSingerCount = 3;

    /// <summary>Matches the screen's own default, so the dialog opens on what the room is seeing.</summary>
    private const int DefaultMarqueeFontSizePixels = 28;

    /// <summary>Also the screen's own, for the same reason.</summary>
    private const int DefaultMarqueeScrollSpeed = 90;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public Venue? Venue { get; set; }
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool CloseOnScrimClick { get; set; }

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
    private bool _prevIsOpen;
    private bool _rotationDialogOpen;

    protected override void OnInitialized()
    {
        _editContext = new EditContext(_model);
    }

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

    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !_prevIsOpen)
        {
            _isNew = Venue is null;
            _model = Venue is null
                ? new EditVenueModel { BreakMusicProvider = BreakMusic.ActiveProvider?.SourceName }
                : new EditVenueModel
                {
                    Id = Venue.Id,
                    Name = Venue.Name,
                    Notes = Venue.Notes,
                    Enabled = Venue.Enabled,
                    DefaultVolume = Venue.Settings.DefaultVolume,
                    ShowEstimatedWaitTime = Venue.Settings.ShowEstimatedWaitTime,
                    SongBackgrounds = [.. Venue.Settings.SongBackgrounds ?? []],
                    TippingEnabled = Venue.Settings.TippingEnabled,
                    WarnOnDuplicateSong = Venue.Settings.WarnOnDuplicateSong,
                    // Venues saved before this setting existed read back 0, which is not an option.
                    DuplicateSongWindowHours = DuplicateWindowOptions.Contains(Venue.Settings.DuplicateSongWindowHours)
                        ? Venue.Settings.DuplicateSongWindowHours
                        : 4,
                    PromptBeforeRemovingSinger = Venue.Settings.PromptBeforeRemovingSinger,
                    PromptBeforeRemovingPerformance = Venue.Settings.PromptBeforeRemovingPerformance,
                    ClearQueueOnClose = Venue.Settings.ClearQueueOnClose,
                    AllowAliases = Venue.Settings.AllowAliases,
                    AllowGuestRemote = Venue.Settings.AllowGuestRemote,
                    ShowQueueToGuests = Venue.Settings.ShowQueueToGuests,
                    // Clone so Cancel discards rotation edits along with the rest of the model.
                    QueueRotation = Venue.Settings.QueueRotation?.Clone() ?? new(),
                    BreakMusicPoolId = Venue.Settings.BreakMusicPoolId,
                    AdPoolId = Venue.Settings.AdPoolId,
                    BrandingImageMediaId = Venue.Settings.BrandingImageMediaId,
                    // Blank, not null: a cleared setting holds "", which no option carries either.
                    // This is the same empty-select trap as a missing provider.
                    BreakMusicProvider = string.IsNullOrWhiteSpace(Venue.Settings.BreakMusicProvider)
                        ? BreakMusic.ActiveProvider?.SourceName
                        : Venue.Settings.BreakMusicProvider,

                    MarqueeEnabled = Venue.Settings.MarqueeEnabled,
                    // Zero is ambiguous (never set vs. a deliberate message-only band) except while
                    // the marquee is off, so the suggestion stands until the venue enables it once.
                    MarqueeSingerCount = Venue.Settings.MarqueeEnabled
                        ? Venue.Settings.MarqueeSingerCount
                        : DefaultMarqueeSingerCount,
                    MarqueeMessage = Venue.Settings.MarqueeMessage,
                    MarqueeEntryFormat = Venue.Settings.MarqueeEntryFormat,
                    MarqueePosition = Venue.Settings.MarqueePosition,
                    MarqueeBackgroundColor = Venue.Settings.MarqueeBackgroundColor ?? DefaultMarqueeBackground,
                    MarqueeTextColor = Venue.Settings.MarqueeTextColor ?? DefaultMarqueeText,
                    // Zero is "the screen decides", which a number input cannot say. It shows the
                    // size the screen would pick instead, and saving it back changes nothing.
                    MarqueeFontSizePixels = Venue.Settings.MarqueeFontSizePixels > 0
                        ? Venue.Settings.MarqueeFontSizePixels
                        : DefaultMarqueeFontSizePixels,
                    MarqueeScrollSpeed = Venue.Settings.MarqueeScrollSpeed > 0
                        ? Venue.Settings.MarqueeScrollSpeed
                        : DefaultMarqueeScrollSpeed,
                    MarqueePinLabel = Venue.Settings.MarqueePinLabel,

                    // Null is "no preference", which a select cannot show. It offers what a code
                    // would take anyway, and saving that back changes nothing.
                    QrCodeSource = Venue.Settings.QrCodeSource,
                    BrandingImageScaling = Venue.Settings.BrandingImageScaling,
                    BreakMusicCardEnabled = Venue.Settings.BreakMusicCardEnabled,
                    BreakMusicCardCorner = Venue.Settings.BreakMusicCardCorner ?? ScreenCorner.BottomLeft,
                    QrCodeCorner = Venue.Settings.QrCodeCorner ?? ScreenCorner.BottomRight,
                    QrCodeSize = Venue.Settings.QrCodeSize ?? ScreenQrSize.Medium,
                    QrCodeHideDuringSong = Venue.Settings.QrCodeHideDuringSong,
                    QrCodeSafeZone = Venue.Settings.QrCodeSafeZone,
                    QrCodeOffset = Venue.Settings.QrCodeOffset,
                };
            _editContext = new EditContext(_model);

            await LoadChoicesAsync();
        }
        _prevIsOpen = IsOpen;
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
        var venue = Venue ?? new Venue { Id = _model.Id, Name = _model.Name };
        venue.Name = _model.Name;
        venue.Notes = _model.Notes;
        venue.Enabled = _model.Enabled;
        venue.Settings.DefaultVolume = _model.DefaultVolume;
        venue.Settings.ShowEstimatedWaitTime = _model.ShowEstimatedWaitTime;
        venue.Settings.SongBackgrounds = [.. _model.SongBackgrounds];
        venue.Settings.TippingEnabled = _model.TippingEnabled;
        venue.Settings.WarnOnDuplicateSong = _model.WarnOnDuplicateSong;
        venue.Settings.DuplicateSongWindowHours = _model.DuplicateSongWindowHours;
        venue.Settings.PromptBeforeRemovingSinger = _model.PromptBeforeRemovingSinger;
        venue.Settings.PromptBeforeRemovingPerformance = _model.PromptBeforeRemovingPerformance;
        venue.Settings.ClearQueueOnClose = _model.ClearQueueOnClose;
        venue.Settings.AllowAliases = _model.AllowAliases;
        venue.Settings.AllowGuestRemote = _model.AllowGuestRemote;
        venue.Settings.ShowQueueToGuests = _model.ShowQueueToGuests;
        venue.Settings.QueueRotation = _model.QueueRotation;
        venue.Settings.BreakMusicPoolId = _model.BreakMusicPoolId;
        venue.Settings.AdPoolId = _model.AdPoolId;
        venue.Settings.BrandingImageMediaId = _model.BrandingImageMediaId;
        venue.Settings.BreakMusicProvider = _model.BreakMusicProvider;
        venue.Settings.MarqueeEnabled = _model.MarqueeEnabled;
        venue.Settings.MarqueeSingerCount = Math.Clamp(_model.MarqueeSingerCount, 0, 20);
        venue.Settings.MarqueeMessage = _model.MarqueeMessage;
        venue.Settings.MarqueeEntryFormat = _model.MarqueeEntryFormat;
        venue.Settings.MarqueePosition = _model.MarqueePosition;
        venue.Settings.MarqueeBackgroundColor = _model.MarqueeBackgroundColor;
        venue.Settings.MarqueeTextColor = _model.MarqueeTextColor;
        venue.Settings.MarqueeFontSizePixels = Math.Clamp(_model.MarqueeFontSizePixels, 12, 96);
        venue.Settings.MarqueeScrollSpeed = Math.Clamp(_model.MarqueeScrollSpeed, 15, 400);
        venue.Settings.MarqueePinLabel = _model.MarqueePinLabel;
        venue.Settings.QrCodeSource = _model.QrCodeSource;
        venue.Settings.BrandingImageScaling = _model.BrandingImageScaling;
        venue.Settings.BreakMusicCardEnabled = _model.BreakMusicCardEnabled;
        venue.Settings.BreakMusicCardCorner = _model.BreakMusicCardCorner;
        venue.Settings.QrCodeCorner = _model.QrCodeCorner;
        venue.Settings.QrCodeSize = _model.QrCodeSize;
        venue.Settings.QrCodeHideDuringSong = _model.QrCodeHideDuringSong;
        venue.Settings.QrCodeSafeZone = _model.QrCodeSafeZone;
        venue.Settings.QrCodeOffset = _model.QrCodeOffset;

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

    private string GetClassString()
        => $"kh-singer-edit-dialog {Class?.Trim()}".Trim();

    public record DialogRequest : EditDialogRequest<Venue>
    {
        public DialogRequest(Venue? value, Func<Venue?, Task> onSave, Action? onCancel, Action onClose) : base(value, onSave, onCancel, onClose)
        {
        }
    }
}
