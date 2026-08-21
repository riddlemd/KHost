using System.ComponentModel.DataAnnotations;
using KHost.Abstractions.Models;

namespace KHost.UserInterface.Models;

public class EditUserGroupModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
    public string Name { get; set; } = "";

    [MaxLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string Description { get; set; } = "";

    public bool IsAdmin { get; set; }

    public bool ExcludeFromSingerQueue { get; set; }

    public List<KHostPermission> Permissions { get; set; } = [];
}
