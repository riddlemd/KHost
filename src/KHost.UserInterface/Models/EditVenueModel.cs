using System.ComponentModel.DataAnnotations;
using KHost.Abstractions.Models;
using KHost.Abstractions.Models.QueueRotation;

namespace KHost.UserInterface.Models;

public class EditVenueModel
{
    public static readonly int[] DuplicateWindowOptions = [1, 2, 4, 8, 12];

    // What the colour inputs show a venue that has never chosen: a native colour picker has no
    // empty state, so it would otherwise open on black and read as a deliberate choice.
    private const string DefaultMarqueeBackground = "#000000";
    private const string DefaultMarqueeText = "#f2f2f5";

    // The screen's own default look: today's opacity, and the singer/song colour with nothing
    // chosen, which is the same one colour as the band's own text.
    private const int DefaultMarqueeBackgroundOpacity = 82;

    /// <summary>What a venue turning the marquee on for the first time is offered.</summary>
    private const int DefaultMarqueeSingerCount = 3;

    /// <summary>Matches the screen's own default, so the dialog opens on what the room is seeing.</summary>
    private const int DefaultMarqueeFontSizePixels = 28;

    /// <summary>Also the screen's own, for the same reason.</summary>
    private const int DefaultMarqueeScrollSpeed = 90;

    public Guid Id { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(32, ErrorMessage = "Name cannot exceed 32 characters.")]
    public string Name { get; set; } = "";

    [MaxLength(255, ErrorMessage = "Notes cannot exceed 255 characters.")]
    public string Notes { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public bool ShowEstimatedWaitTime { get; set; } = true;
    public bool TippingEnabled { get; set; } = true;
    public bool WarnOnDuplicateSong { get; set; }
    public int DuplicateSongWindowHours { get; set; } = 4;
    public bool PromptBeforeRemovingSinger { get; set; } = true;
    public bool PromptBeforeRemovingPerformance { get; set; } = true;
    public bool ClearQueueOnClose { get; set; } = true;

    /// <summary>Off for a venue never asked, which is why the setting needed no backfill.</summary>
    public bool AllowAliases { get; set; }

    public bool AllowGuestRemote { get; set; } = true;

    public bool ShowQueueToGuests { get; set; } = true;

    public QueueRotationConfig QueueRotation { get; set; } = new();

    /// <summary>Empty is "none chosen", which is what a select with a blank first option posts.</summary>
    public Guid? BreakMusicPoolId { get; set; }
    public Guid? AdPoolId { get; set; }
    public Guid? BrandingImageMediaId { get; set; }
    public string? BreakMusicProvider { get; set; }

    public bool MarqueeEnabled { get; set; }

    // Three is only the dialog's starting point. A venue saved before the marquee existed has no
    // key here and reads back as zero, a valid message-only band, not something to correct.
    [Range(0, 20, ErrorMessage = "Show between 0 and 20 singers.")]
    public int MarqueeSingerCount { get; set; } = 3;

    [MaxLength(255, ErrorMessage = "The marquee message cannot exceed 255 characters.")]
    public string? MarqueeMessage { get; set; }

    [MaxLength(255, ErrorMessage = "The entry format cannot exceed 255 characters.")]
    public string? MarqueeEntryFormat { get; set; }

    public MarqueePosition MarqueePosition { get; set; }

    public string? MarqueeBackgroundColor { get; set; }
    public string? MarqueeTextColor { get; set; }

    [Range(12, 96, ErrorMessage = "Text size must be between 12 and 96 pixels.")]
    public int MarqueeFontSizePixels { get; set; } = 28;

    [Range(15, 400, ErrorMessage = "Scroll speed must be between 15 and 400 pixels per second.")]
    public int MarqueeScrollSpeed { get; set; } = 90;

    public bool MarqueePinLabel { get; set; }

    // Unlike font size/scroll speed, zero is a real opacity (fully transparent), so this cannot
    // reuse "zero means the screen decides" — the dialog shows the screen's own default resolved,
    // the same way it already does for the two colours below.
    [Range(0, 100, ErrorMessage = "Opacity must be between 0 and 100 percent.")]
    public int MarqueeBackgroundOpacity { get; set; } = 82;

    public string? MarqueeSingerColor { get; set; }
    public string? MarqueeSongColor { get; set; }
    public string? MarqueeDividerColor { get; set; }
    public MarqueeDividerShape MarqueeDividerShape { get; set; }
    public bool MarqueeHideDuringSong { get; set; }

    /// <summary>The plugin whose code this venue shows, or null for none. Null is the default.</summary>
    public string? QrCodeSource { get; set; }

    /// <summary>The visualisation playlist under a song's words; null leaves black.</summary>
    public Guid? VisualisationPlaylistId { get; set; }

    public NextSingerBackground NextSingerBackground { get; set; }

    /// <summary>Null takes the image's own answer, which is what a venue that never asks gets.</summary>
    public ImageScaling? BrandingImageScaling { get; set; }

    public bool BreakMusicCardEnabled { get; set; }

    public OverlayCorner? BreakMusicCardCorner { get; set; }

    /// <summary>Corner and size are not nullable here, unlike how the venue stores them.</summary>
    /// <remarks>The dialog shows what a code would take anyway, so saving back changes nothing.</remarks>
    public OverlayCorner QrCodeCorner { get; set; } = OverlayCorner.BottomRight;

    public QrCodeSize QrCodeSize { get; set; } = QrCodeSize.Medium;

    public bool QrCodeHideDuringSong { get; set; }

    /// <summary>White around the code, in its own modules. Zero takes the screen's own.</summary>
    public int QrCodeSafeZone { get; set; }

    /// <summary>Inset from the two edges it sits against, as a percentage of the shorter side.</summary>
    public double QrCodeOffset { get; set; }

    /// <summary>What the dialog opens on. Null <paramref name="venue"/> is Add, which starts from
    /// every default above plus <paramref name="activeBreakMusicProviderSource"/> — the one field
    /// a fresh venue still needs a fallback for.</summary>
    public static EditVenueModel From(Venue? venue, string? activeBreakMusicProviderSource)
    {
        if (venue is null)
            return new EditVenueModel
            {
                BreakMusicProvider = activeBreakMusicProviderSource,
                VisualisationPlaylistId = VisualisationPlaylist.DefaultId,
            };

        var settings = venue.Settings;

        return new EditVenueModel
        {
            Id = venue.Id,
            Name = venue.Name,
            Notes = venue.Notes,
            Enabled = venue.Enabled,
            ShowEstimatedWaitTime = settings.ShowEstimatedWaitTime,
            VisualisationPlaylistId = settings.VisualisationPlaylistId,
            TippingEnabled = settings.TippingEnabled,
            WarnOnDuplicateSong = settings.WarnOnDuplicateSong,
            // Venues saved before this setting existed read back 0, which is not an option.
            DuplicateSongWindowHours = DuplicateWindowOptions.Contains(settings.DuplicateSongWindowHours)
                ? settings.DuplicateSongWindowHours
                : 4,
            PromptBeforeRemovingSinger = settings.PromptBeforeRemovingSinger,
            PromptBeforeRemovingPerformance = settings.PromptBeforeRemovingPerformance,
            ClearQueueOnClose = settings.ClearQueueOnClose,
            AllowAliases = settings.AllowAliases,
            AllowGuestRemote = settings.AllowGuestRemote,
            ShowQueueToGuests = settings.ShowQueueToGuests,
            // Clone so Cancel discards rotation edits along with the rest of the model.
            QueueRotation = settings.QueueRotation?.Clone() ?? new(),
            BreakMusicPoolId = settings.BreakMusicPoolId,
            AdPoolId = settings.AdPoolId,
            BrandingImageMediaId = settings.BrandingImageMediaId,
            // Blank, not null: a cleared setting holds "", which no option carries either. This is
            // the same empty-select trap as a missing provider.
            BreakMusicProvider = string.IsNullOrWhiteSpace(settings.BreakMusicProvider)
                ? activeBreakMusicProviderSource
                : settings.BreakMusicProvider,

            MarqueeEnabled = settings.MarqueeEnabled,
            // Zero is ambiguous (never set vs. a deliberate message-only band) except while the
            // marquee is off, so the suggestion stands until the venue enables it once.
            MarqueeSingerCount = settings.MarqueeEnabled ? settings.MarqueeSingerCount : DefaultMarqueeSingerCount,
            MarqueeMessage = settings.MarqueeMessage,
            MarqueeEntryFormat = settings.MarqueeEntryFormat,
            MarqueePosition = settings.MarqueePosition,
            MarqueeBackgroundColor = settings.MarqueeBackgroundColor ?? DefaultMarqueeBackground,
            MarqueeTextColor = settings.MarqueeTextColor ?? DefaultMarqueeText,
            // Zero is "the screen decides", which a number input cannot say. It shows the size the
            // screen would pick instead, and saving it back changes nothing.
            MarqueeFontSizePixels = settings.MarqueeFontSizePixels > 0
                ? settings.MarqueeFontSizePixels
                : DefaultMarqueeFontSizePixels,
            MarqueeScrollSpeed = settings.MarqueeScrollSpeed > 0
                ? settings.MarqueeScrollSpeed
                : DefaultMarqueeScrollSpeed,
            MarqueePinLabel = settings.MarqueePinLabel,
            // Null is "the screen decides", which a number input cannot say either.
            MarqueeBackgroundOpacity = settings.MarqueeBackgroundOpacity ?? DefaultMarqueeBackgroundOpacity,
            MarqueeSingerColor = settings.MarqueeSingerColor ?? DefaultMarqueeText,
            MarqueeSongColor = settings.MarqueeSongColor ?? DefaultMarqueeText,
            MarqueeDividerColor = settings.MarqueeDividerColor ?? DefaultMarqueeText,
            MarqueeDividerShape = settings.MarqueeDividerShape,
            MarqueeHideDuringSong = settings.MarqueeHideDuringSong,

            // Null is "no preference", which a select cannot show. It offers what a code would take
            // anyway, and saving that back changes nothing.
            QrCodeSource = settings.QrCodeSource,
            BrandingImageScaling = settings.BrandingImageScaling,
            NextSingerBackground = settings.NextSingerBackground,
            BreakMusicCardEnabled = settings.BreakMusicCardEnabled,
            BreakMusicCardCorner = settings.BreakMusicCardCorner ?? OverlayCorner.BottomLeft,
            QrCodeCorner = settings.QrCodeCorner ?? OverlayCorner.BottomRight,
            QrCodeSize = settings.QrCodeSize ?? QrCodeSize.Medium,
            QrCodeHideDuringSong = settings.QrCodeHideDuringSong,
            QrCodeSafeZone = settings.QrCodeSafeZone,
            QrCodeOffset = settings.QrCodeOffset,
        };
    }

    /// <summary>Writes every field back onto <paramref name="venue"/>. Applied to a copy by the
    /// caller, not the caller's own instance, so a save the host backs out of has touched nothing.
    /// </summary>
    public void ApplyTo(Venue venue)
    {
        venue.Name = Name;
        venue.Notes = Notes;
        venue.Enabled = Enabled;
        venue.Settings.ShowEstimatedWaitTime = ShowEstimatedWaitTime;
        venue.Settings.VisualisationPlaylistId = VisualisationPlaylistId;
        venue.Settings.TippingEnabled = TippingEnabled;
        venue.Settings.WarnOnDuplicateSong = WarnOnDuplicateSong;
        venue.Settings.DuplicateSongWindowHours = DuplicateSongWindowHours;
        venue.Settings.PromptBeforeRemovingSinger = PromptBeforeRemovingSinger;
        venue.Settings.PromptBeforeRemovingPerformance = PromptBeforeRemovingPerformance;
        venue.Settings.ClearQueueOnClose = ClearQueueOnClose;
        venue.Settings.AllowAliases = AllowAliases;
        venue.Settings.AllowGuestRemote = AllowGuestRemote;
        venue.Settings.ShowQueueToGuests = ShowQueueToGuests;
        venue.Settings.QueueRotation = QueueRotation;
        venue.Settings.BreakMusicPoolId = BreakMusicPoolId;
        venue.Settings.AdPoolId = AdPoolId;
        venue.Settings.BrandingImageMediaId = BrandingImageMediaId;
        venue.Settings.BreakMusicProvider = BreakMusicProvider;
        venue.Settings.MarqueeEnabled = MarqueeEnabled;
        venue.Settings.MarqueeSingerCount = Math.Clamp(MarqueeSingerCount, 0, 20);
        venue.Settings.MarqueeMessage = MarqueeMessage;
        venue.Settings.MarqueeEntryFormat = MarqueeEntryFormat;
        venue.Settings.MarqueePosition = MarqueePosition;
        venue.Settings.MarqueeBackgroundColor = MarqueeBackgroundColor;
        venue.Settings.MarqueeTextColor = MarqueeTextColor;
        venue.Settings.MarqueeFontSizePixels = Math.Clamp(MarqueeFontSizePixels, 12, 96);
        venue.Settings.MarqueeScrollSpeed = Math.Clamp(MarqueeScrollSpeed, 15, 400);
        venue.Settings.MarqueePinLabel = MarqueePinLabel;
        venue.Settings.MarqueeBackgroundOpacity = Math.Clamp(MarqueeBackgroundOpacity, 0, 100);
        venue.Settings.MarqueeSingerColor = MarqueeSingerColor;
        venue.Settings.MarqueeSongColor = MarqueeSongColor;
        venue.Settings.MarqueeDividerColor = MarqueeDividerColor;
        venue.Settings.MarqueeDividerShape = MarqueeDividerShape;
        venue.Settings.MarqueeHideDuringSong = MarqueeHideDuringSong;
        venue.Settings.QrCodeSource = QrCodeSource;
        venue.Settings.BrandingImageScaling = BrandingImageScaling;
        venue.Settings.NextSingerBackground = NextSingerBackground;
        venue.Settings.BreakMusicCardEnabled = BreakMusicCardEnabled;
        venue.Settings.BreakMusicCardCorner = BreakMusicCardCorner;
        venue.Settings.QrCodeCorner = QrCodeCorner;
        venue.Settings.QrCodeSize = QrCodeSize;
        venue.Settings.QrCodeHideDuringSong = QrCodeHideDuringSong;
        venue.Settings.QrCodeSafeZone = QrCodeSafeZone;
        venue.Settings.QrCodeOffset = QrCodeOffset;
    }
}
