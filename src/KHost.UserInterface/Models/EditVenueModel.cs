using System.ComponentModel.DataAnnotations;
using KHost.Abstractions.Models;
using KHost.Plugins.Sdk.Models.QueueRotation;

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

    [Range(0, 100, ErrorMessage = "Break music volume must be between 0 and 100.")]
    public int BreakMusicVolume { get; set; } = 60;
}
