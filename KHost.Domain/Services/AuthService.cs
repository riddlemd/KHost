using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public class AuthService : BaseService, IAuthService
{
    private readonly IEnumerable<IAuthProvider> _providers;
    private readonly IUsersRepository _users;
    private readonly IAnalyticsService _analytics;

    public AuthService(
        ILogger<AuthService> logger,
        IEnumerable<IAuthProvider> providers,
        IUsersRepository users,
        IAnalyticsService analytics)
        : base(logger)
    {
        _providers = providers;
        _users = users;
        _analytics = analytics;
    }

    public async Task<AuthResult> LoginAsync(string name, string password)
    {
        using var activity = _analytics.StartActivity(AnalyticActivities.Login);
        activity.SetTag("username", name);

        KHostUser? user = await _users.FindByNameAsync(name);

        if (user is null)
        {
            Logger.LogWarning("Login failed: no user found for name '{Name}'", name);
            activity.SetError("Invalid name or password");
            return AuthResult.Fail("Invalid name or password");
        }

        var provider = _providers.FirstOrDefault(p => p.CanHandle(user));
        if (provider is null)
        {
            Logger.LogError("No auth provider registered for user '{UserId}'", user.Id);
            activity.SetError("Authentication is not configured for this account");
            return AuthResult.Fail("Authentication is not configured for this account");
        }

        var result = await provider.AuthenticateAsync(user, password);

        if (result.Success)
        {
            Logger.LogInformation("Login succeeded for user '{UserId}' via {Provider}", user.Id, provider.GetType().Name);
            activity.SetSuccess();
        }
        else
        {
            Logger.LogWarning("Login failed for name '{Name}': {Reason}", name, result.ErrorMessage);
            activity.SetError(result.ErrorMessage);
        }

        return result;
    }

    private static class AnalyticActivities
    {
        public const string Login = "auth.login";
    }
}
