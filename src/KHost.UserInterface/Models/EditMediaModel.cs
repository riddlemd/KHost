using System.ComponentModel.DataAnnotations;
using KHost.Abstractions.Models;

namespace KHost.UserInterface.Models;

public class EditMediaModel
{
    public Guid Id { get; set; }

    public MediaStatus Status { get; set; }

    [Required(ErrorMessage = "Title is required.")]
    [MaxLength(255, ErrorMessage = "Title cannot exceed 255 characters.")]
    public string Title { get; set; } = "";

    [MaxLength(255, ErrorMessage = "Artist cannot exceed 255 characters.")]
    public string Artist { get; set; } = "";

    [MaxLength(1024, ErrorMessage = "Notes cannot exceed 1024 characters.")]
    public string Notes { get; set; } = "";
}
