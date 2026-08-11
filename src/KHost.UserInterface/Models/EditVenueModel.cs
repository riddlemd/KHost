using System.ComponentModel.DataAnnotations;
using KHost.Abstractions.Models;

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
    public bool MoveSingerToBottomAfterPerformance { get; set; } = true;
    public bool ShowEstimatedWaitTime { get; set; } = true;
    public bool PromptBeforeRemovingSinger { get; set; } = true;
    public bool PromptBeforeRemovingPerformance { get; set; } = true;
    public bool PromptBeforeRemovingPerformanceHistory { get; set; } = true;
    public bool PromptBeforeRemovingUserGroup { get; set; } = true;
    public bool ClearQueueOnClose { get; set; } = true;
}
