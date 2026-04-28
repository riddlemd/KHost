using System.ComponentModel.DataAnnotations;

namespace KHost.UserInterface.Models;

public class BulkEditMediaModel
{
    public bool UpdateArtist { get; set; }

    [MaxLength(255, ErrorMessage = "Artist cannot exceed 255 characters.")]
    public string Artist { get; set; } = "";

    public bool SwapTitleAndArtist { get; set; }
}
