using KHost.Abstractions.Models;

namespace KHost.Common.Authentication;

/// <summary>
/// Named constructors for <see cref="AuthResult"/>. A failure that forgets to leave
/// <c>User</c> null is a successful login by another name, so neither field is set by hand.
/// </summary>
public static class AuthResults
{
    public static AuthResult Succeeded(KHostUser user) => new() { Success = true, User = user };

    public static AuthResult Failed(string message) => new() { Success = false, ErrorMessage = message };
}
