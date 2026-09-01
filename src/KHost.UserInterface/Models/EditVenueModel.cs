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

    public MarqueePosition MarqueePosition { get; set; }

    public string? MarqueeBackgroundColor { get; set; }
    public string? MarqueeTextColor { get; set; }

    [Range(12, 96, ErrorMessage = "Text size must be between 12 and 96 pixels.")]
    public int MarqueeFontSizePixels { get; set; } = 28;

    [Range(15, 400, ErrorMessage = "Scroll speed must be between 15 and 400 pixels per second.")]
    public int MarqueeScrollSpeed { get; set; } = 90;

    public bool MarqueePinLabel { get; set; }
}
