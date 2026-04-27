using System.ComponentModel.DataAnnotations;

namespace KHost.UserInterface.Models;

public class EditUserModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(64, ErrorMessage = "Name cannot exceed 64 characters.")]
    public string Name { get; set; } = "";

    [MaxLength(255, ErrorMessage = "Notes cannot exceed 255 characters.")]
    public string Notes { get; set; } = "";

    public List<Guid> SelectedGroupIds { get; set; } = [];
}
