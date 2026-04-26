namespace KHost.Abstractions.Models;

public class AuthResult
{
    public bool Success { get; init; }
    public KHostUser? User { get; init; }
    public string? ErrorMessage { get; init; }

    public static AuthResult Ok(KHostUser user) => new() { Success = true, User = user };
    public static AuthResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
