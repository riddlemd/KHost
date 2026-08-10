using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IAuthProvider
{
    bool CanHandle(KHostUser user);
    Task<AuthResult> AuthenticateAsync(KHostUser user, string password);
}
