using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Signs a console user in by name and password.</summary>
/// <remarks>Host-owned; a plugin has no business with it. A host singleton, callable from any
/// thread. Announces nothing.</remarks>
public interface IAuthService
{
    /// <summary>Finds the user by exact name and hands them to the first <see cref="IAuthProvider"/>
    /// that can handle them.</summary>
    /// <returns>A failed result, never an exception, for an unknown name, a wrong password, or an
    /// account no provider can handle. An unknown name and a wrong password fail with the same
    /// message, so the response does not reveal which names exist.</returns>
    Task<AuthResult> LoginAsync(string name, string password);
}
