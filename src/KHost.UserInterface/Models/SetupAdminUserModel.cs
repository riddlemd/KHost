using System.ComponentModel.DataAnnotations;

namespace KHost.UserInterface.Models;

public class SetupAdminUserModel
{
    [Required(ErrorMessage = "Username is required.")]
    [MinLength(3, ErrorMessage = "Username must be at least 3 characters.")]
    [MaxLength(64, ErrorMessage = "Username cannot exceed 64 characters.")]
    public string Username { get; set; } = "Admin";

    [Required(ErrorMessage = "Password is required.")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Please confirm your password.")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string PasswordConfirm { get; set; } = "";
}
