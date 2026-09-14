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
    public ScreenDisconnectBehavior OnScreenDisconnect { get; set; } = ScreenDisconnectBehavior.ResumeOnReconnect;
    public bool ShowEstimatedWaitTime { get; set; } = true;
    public bool TippingEnabled { get; set; } = true;
    public bool WarnOnDuplicateSong { get; set; }
    public int DuplicateSongWindowHours { get; set; } = 4;
    public bool PromptBeforeRemovingSinger { get; set; } = true;
    public bool PromptBeforeRemovingPerformance { get; set; } = true;
    public bool ClearQueueOnClose { get; set; } = true;
    public QueueRotationConfig QueueRotation { get; set; } = new();

    /// <summary>Empty is "none chosen", which is what a select with a blank first option posts.</summary>
    public Guid? BreakMusicPoolId { get; set; }
    public Guid? AdPoolId { get; set; }
    public Guid? BrandingImageMediaId { get; set; }
    public string? BreakMusicProvider { get; set; }

    public bool MarqueeEnabled { get; set; }

    // Three is the starting point the dialog offers, not what a stored venue reads: a row saved
    // before the marquee existed has no key here and comes back as zero, which is a valid
    // message-only band rather than something to correct.
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

    /// <summary>
    /// Where a QR code sits, and how big. Not nullable here the way the venue stores them: the
    /// dialog shows the corner and size a code would take anyway, and saving that back changes
    /// nothing — the same trade the marquee's sizes make with zero.
    /// </summary>
    /// <summary>The plugin whose code this venue shows, or null for none. Null is the default.</summary>
    public string? QrCodeSource { get; set; }

    public bool BreakMusicCardEnabled { get; set; }

    public ScreenCorner? BreakMusicCardCorner { get; set; }

    public ScreenCorner QrCodeCorner { get; set; } = ScreenCorner.BottomRight;

    public ScreenQrSize QrCodeSize { get; set; } = ScreenQrSize.Medium;

    public bool QrCodeHideDuringSong { get; set; }

    /// <summary>White around the code, in its own modules. Zero takes the screen's own.</summary>
    public int QrCodeSafeZone { get; set; }

    /// <summary>Inset from the two edges it sits against, as a percentage of the shorter side.</summary>
    public double QrCodeOffset { get; set; }
}
