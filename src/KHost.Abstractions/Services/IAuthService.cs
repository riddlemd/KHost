using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string name, string password);
}
