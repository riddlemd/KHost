namespace KHost.Abstractions.Models;

/// <summary>The outcome of one sign-in attempt.</summary>
public class AuthResult
{
    /// <summary>True when the sign-in succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>The signed-in user. Null on failure.</summary>
    public KHostUser? User { get; init; }

    /// <summary>Why it failed, in plain words for the sign-in screen. Null on success.</summary>
    public string? ErrorMessage { get; init; }
}
