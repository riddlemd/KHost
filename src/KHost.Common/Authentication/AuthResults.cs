using KHost.Abstractions.Models;

namespace KHost.Common.Authentication;

/// <summary>Named constructors for <see cref="AuthResult"/>; a failure must leave User null.</summary>
/// <remarks>Forgetting that makes a failure read as a successful login.</remarks>
public static class AuthResults
{
    public static AuthResult Succeeded(KHostUser user) => new() { Success = true, User = user };

    public static AuthResult Failed(string message) => new() { Success = false, ErrorMessage = message };
}
