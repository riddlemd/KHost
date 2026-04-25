using System.ComponentModel.DataAnnotations;

namespace KHost.UserInterface.Models;

public class MediaEditModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Title is required.")]
    [MaxLength(255, ErrorMessage = "Title cannot exceed 255 characters.")]
    public string Title { get; set; } = "";

    [MaxLength(255, ErrorMessage = "Artist cannot exceed 255 characters.")]
    public string Artist { get; set; } = "";

    [MaxLength(1024, ErrorMessage = "Notes cannot exceed 1024 characters.")]
    public string Notes { get; set; } = "";
}
