using KHost.Abstractions.Models;

namespace KHost.Common.Authentication;

/// <summary>Named constructors for <see cref="AuthResult"/>; a failure must leave User null.</summary>
/// <remarks>Forgetting that makes a failure read as a successful login.</remarks>
public static class AuthResults
{
    /// <summary>A successful login as <paramref name="user"/>.</summary>
    public static AuthResult Succeeded(KHostUser user) => new() { Success = true, User = user };

    /// <summary>A failed login carrying <paramref name="message"/>, with no user attached.</summary>
    public static AuthResult Failed(string message) => new() { Success = false, ErrorMessage = message };
}
