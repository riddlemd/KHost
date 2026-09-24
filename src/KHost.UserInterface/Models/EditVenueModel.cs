using System.ComponentModel.DataAnnotations;
using KHost.Abstractions.Models;
using KHost.Abstractions.Models.QueueRotation;

namespace KHost.UserInterface.Models;

public class EditVenueModel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(32, ErrorMessage = "Name cannot exceed 32 characters.")]
    public string Name { get; set; } = "";

    [MaxLength(255, ErrorMessage = "Notes cannot exceed 255 characters.")]
    public string Notes { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public int DefaultVolume { get; set; } = 100;
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

    /// <summary>The plugin whose code this venue shows, or null for none. Null is the default.</summary>
    public string? QrCodeSource { get; set; }

    /// <summary>Backgrounds a song may be given, by file name. Empty is the black background.
    /// </summary>
    /// <remarks>Its own list rather than the venue's, so closing the dialog without saving leaves
    /// the venue's choices alone.</remarks>
    public List<string> SongBackgrounds { get; set; } = [];

    /// <summary>Null takes the image's own answer, which is what a venue that never asks gets.</summary>
    public ImageScaling? BrandingImageScaling { get; set; }

    public bool BreakMusicCardEnabled { get; set; }

    public ScreenCorner? BreakMusicCardCorner { get; set; }

    /// <summary>Corner and size are not nullable here, unlike how the venue stores them.</summary>
    /// <remarks>The dialog shows what a code would take anyway, so saving back changes nothing.</remarks>
    public ScreenCorner QrCodeCorner { get; set; } = ScreenCorner.BottomRight;

    public ScreenQrSize QrCodeSize { get; set; } = ScreenQrSize.Medium;

    public bool QrCodeHideDuringSong { get; set; }

    /// <summary>White around the code, in its own modules. Zero takes the screen's own.</summary>
    public int QrCodeSafeZone { get; set; }

    /// <summary>Inset from the two edges it sits against, as a percentage of the shorter side.</summary>
    public double QrCodeOffset { get; set; }
}
