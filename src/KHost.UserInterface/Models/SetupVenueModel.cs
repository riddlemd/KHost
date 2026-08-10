using System.ComponentModel.DataAnnotations;

namespace KHost.UserInterface.Models;

public class SetupVenueModel
{
    [Required(ErrorMessage = "Venue name is required.")]
    [MaxLength(32, ErrorMessage = "Venue name cannot exceed 32 characters.")]
    public string Name { get; set; } = "Default Venue";

    public int DefaultVolume { get; set; } = 100;
    public bool MoveSingerToBottomAfterPerformance { get; set; } = true;
    public bool PromptBeforeRemovingSinger { get; set; } = true;
    public bool PromptBeforeRemovingPerformance { get; set; } = true;
    public bool PromptBeforeRemovingPerformanceHistory { get; set; } = true;
    public bool PromptBeforeRemovingUserGroup { get; set; } = true;
    public bool ClearQueueOnClose { get; set; } = true;
}
