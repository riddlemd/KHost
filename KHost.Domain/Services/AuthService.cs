using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public class AuthService : BaseService, IAuthService
{
    private readonly IEnumerable<IAuthProvider> _providers;
    private readonly IUsersRepository _users;

    public AuthService(
        ILogger<AuthService> logger,
        IEnumerable<IAuthProvider> providers,
        IUsersRepository users)
        : base(logger)
    {
        _providers = providers;
        _users = users;
    }

    public async Task<AuthResult> LoginAsync(string name, string password)
    {
        KHostUser? user = await _users.FindByNameAsync(name);

        if (user is null)
        {
            Logger.LogWarning("Login failed: no user found for name '{Name}'", name);
            return AuthResult.Fail("Invalid name or password");
        }

        var provider = _providers.FirstOrDefault(p => p.CanHandle(user));
        if (provider is null)
        {
            Logger.LogError("No auth provider registered for user '{UserId}'", user.Id);
            return AuthResult.Fail("Authentication is not configured for this account");
        }

        var result = await provider.AuthenticateAsync(user, password);

        if (result.Success)
            Logger.LogInformation("Login succeeded for user '{UserId}' via {Provider}", user.Id, provider.GetType().Name);
        else
            Logger.LogWarning("Login failed for name '{Name}': {Reason}", name, result.ErrorMessage);

        return result;
    }
}
