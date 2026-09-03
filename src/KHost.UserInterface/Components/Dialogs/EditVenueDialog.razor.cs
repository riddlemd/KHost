using KHost.Abstractions.Models;
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
    [Parameter] public EventCallback OnOpen { get; set; }

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

    private bool _isNew;
    private EditVenueModel _model = new();
    private EditContext _editContext = default!;
    private bool _prevIsOpen;
    private bool _rotationDialogOpen;

    protected override void OnInitialized()
    {
        _editContext = new EditContext(_model);
    }

    /// <summary>
    /// The venue's mode when nothing loaded answers for it — a plugin that failed to load, was
    /// switched off, or has been removed. It still has to appear in the list and stay selected: a
    /// select whose value matches no option renders blank, which reads as "no mode set" for a venue
    /// that has one, and hides that the next pick replaces a choice the host could not see.
    /// </summary>
    private string? UnavailableProviderSource
        => BreakMusic.Providers.Any(p => string.Equals(p.SourceName, _model.BreakMusicProvider, StringComparison.OrdinalIgnoreCase))
            ? null
            : _model.BreakMusicProvider;

    /// <summary>
    /// Whether the chosen mode is the one this host's own playlists feed — not whether the host
    /// renders the audio, which is a separate question a provider may answer either way. An
    /// unloaded mode counts as one, so the playlist a venue already chose is not hidden by a plugin
    /// that failed to start.
    /// </summary>
    private bool UsesLocalPlaylists
        => UnavailableProviderSource is not null
           || (BreakMusic.LibraryProvider is { } library
               && string.Equals(_model.BreakMusicProvider, library.SourceName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The mode this host's own playlists feed is the one a host thinks of as "my own music", so it
    /// says so; every other provider names itself.
    /// </summary>
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
                    OnScreenDisconnect = Venue.Settings.OnScreenDisconnect,
                    ShowEstimatedWaitTime = Venue.Settings.ShowEstimatedWaitTime,
                    TippingEnabled = Venue.Settings.TippingEnabled,
                    WarnOnDuplicateSong = Venue.Settings.WarnOnDuplicateSong,
                    // Venues saved before this setting existed read back 0, which is not an option.
                    DuplicateSongWindowHours = DuplicateWindowOptions.Contains(Venue.Settings.DuplicateSongWindowHours)
                        ? Venue.Settings.DuplicateSongWindowHours
                        : 4,
                    PromptBeforeRemovingSinger = Venue.Settings.PromptBeforeRemovingSinger,
                    PromptBeforeRemovingPerformance = Venue.Settings.PromptBeforeRemovingPerformance,
                    ClearQueueOnClose = Venue.Settings.ClearQueueOnClose,
                    // Clone so Cancel discards rotation edits along with the rest of the model.
                    QueueRotation = Venue.Settings.QueueRotation?.Clone() ?? new(),
                    BreakMusicPoolId = Venue.Settings.BreakMusicPoolId,
                    AdPoolId = Venue.Settings.AdPoolId,
                    BrandingImageMediaId = Venue.Settings.BrandingImageMediaId,
                    // Blank, not just null: a venue whose setting was cleared holds "", which no
                    // option carries either, and would leave the select as empty as a missing
                    // provider does.
                    BreakMusicProvider = string.IsNullOrWhiteSpace(Venue.Settings.BreakMusicProvider)
                        ? BreakMusic.ActiveProvider?.SourceName
                        : Venue.Settings.BreakMusicProvider,

                    MarqueeEnabled = Venue.Settings.MarqueeEnabled,
                    // A venue that has never had a marquee stores zero here, which is
                    // indistinguishable from a deliberate message-only band — except that it
                    // cannot have chosen one while the marquee was off. So the suggestion stands
                    // until the venue has enabled it once, and its own zero is kept after that.
                    MarqueeSingerCount = Venue.Settings.MarqueeEnabled
                        ? Venue.Settings.MarqueeSingerCount
                        : DefaultMarqueeSingerCount,
                    MarqueeMessage = Venue.Settings.MarqueeMessage,
                    MarqueeEntryFormat = Venue.Settings.MarqueeEntryFormat,
                    MarqueePosition = Venue.Settings.MarqueePosition,
                    MarqueeBackgroundColor = Venue.Settings.MarqueeBackgroundColor ?? DefaultMarqueeBackground,
                    MarqueeTextColor = Venue.Settings.MarqueeTextColor ?? DefaultMarqueeText,
                    // Zero is "the screen decides", which a number input cannot say — it shows the
                    // size the screen would pick instead, and saving it back changes nothing.
                    MarqueeFontSizePixels = Venue.Settings.MarqueeFontSizePixels > 0
                        ? Venue.Settings.MarqueeFontSizePixels
                        : DefaultMarqueeFontSizePixels,
                    MarqueeScrollSpeed = Venue.Settings.MarqueeScrollSpeed > 0
                        ? Venue.Settings.MarqueeScrollSpeed
                        : DefaultMarqueeScrollSpeed,
                    MarqueePinLabel = Venue.Settings.MarqueePinLabel,
                };
            _editContext = new EditContext(_model);

            await LoadChoicesAsync();
        }
        _prevIsOpen = IsOpen;
    }

    /// <summary>
    /// Read when the dialog opens rather than held: a playlist added on the manager page while
    /// this venue was last edited would otherwise be missing from the list.
    /// </summary>
    private async Task LoadChoicesAsync()
    {

        // Stills only: anything else handed to the screen as a card is a URL that serves nothing.
        // Read by type rather than paged, or a card past the first page would never be offered.
        _images = await Media.ReadAllByTypesAsync(MediaType.Image);

        // Null venue id: a playlist belongs to every venue unless it was scoped to one, and this
        // dialog may be editing a venue that is not the one currently selected.
        _breakMusicPools = await MediaPools.ReadAllWithEntriesAsync(PoolPurpose.BreakMusic, venueId: null);
        _adPools = await MediaPools.ReadAllWithEntriesAsync(PoolPurpose.Ads, venueId: null);

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

    private async Task SaveAsync()
    {
        var venue = Venue ?? new Venue { Id = _model.Id, Name = _model.Name };
        venue.Name = _model.Name;
        venue.Notes = _model.Notes;
        venue.Enabled = _model.Enabled;
        venue.Settings.DefaultVolume = _model.DefaultVolume;
        venue.Settings.OnScreenDisconnect = _model.OnScreenDisconnect;
        venue.Settings.ShowEstimatedWaitTime = _model.ShowEstimatedWaitTime;
        venue.Settings.TippingEnabled = _model.TippingEnabled;
        venue.Settings.WarnOnDuplicateSong = _model.WarnOnDuplicateSong;
        venue.Settings.DuplicateSongWindowHours = _model.DuplicateSongWindowHours;
        venue.Settings.PromptBeforeRemovingSinger = _model.PromptBeforeRemovingSinger;
        venue.Settings.PromptBeforeRemovingPerformance = _model.PromptBeforeRemovingPerformance;
        venue.Settings.ClearQueueOnClose = _model.ClearQueueOnClose;
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

        await OnSave.InvokeAsync(venue);

        await CloseAsync();
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
