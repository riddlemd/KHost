using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Checks a password for the accounts it knows how to handle.</summary>
/// <remarks>Host-only. The plugin loader deliberately does not bind this interface: letting an
/// installed plugin supply authentication is a trust decision the host has not made, so a plugin's
/// implementation is never asked. <see cref="IAuthService"/> asks the first registered provider
/// whose <see cref="CanHandle"/> is true.</remarks>
public interface IAuthProvider
{
    /// <summary>Whether this provider can verify <paramref name="user"/>.</summary>
    /// <remarks>Only the first provider answering true is asked, so answer true only for accounts
    /// this provider can actually settle.</remarks>
    bool CanHandle(KHostUser user);

    /// <summary>Verifies <paramref name="password"/> for <paramref name="user"/>.</summary>
    /// <returns>A failed result, not an exception, for a wrong password or an account with none.</returns>
    Task<AuthResult> AuthenticateAsync(KHostUser user, string password);
}
