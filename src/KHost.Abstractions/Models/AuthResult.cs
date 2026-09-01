namespace KHost.Abstractions.Models;

public class AuthResult
{
    public bool Success { get; init; }
    public KHostUser? User { get; init; }
    public string? ErrorMessage { get; init; }
}
